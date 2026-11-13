using System.Net.Mail;
using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Imports;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Lms.Api.Features.Students.Import.StudentImportColumns;

namespace Lms.Api.Features.Students.Import;

/// <summary>
/// Students from an Excel file (FR-STU-05, FR-STU-06, FR-ENR-03; D24, D25, D34): one row is one student with up to two guardians
/// and an optional class. Every row obeys the rules of creating a student by hand (permission and policy at the row's unit, code
/// mode, duplicates, guardians required, class capacity). A possible duplicate (same name and birth date) blocks the file: the
/// person removes or fixes the row. Commit creates the rows in file order through <see cref="StudentCreator"/> inside the
/// framework's all-or-nothing transaction; credentials emails and the one-time credentials sheet are produced only after it.
/// The <c>DefaultPassword</c> column is sensitive: the framework never stores or shows its values.
/// </summary>
internal sealed class StudentImportDefinition(
    LmsDbContext db, IClock clock, IAccessEvaluator access, IPersonDuplicateFinder duplicates, IStudentCodeGenerator codes,
    ISettingsResolver settings, UserManager<User> users, IAcademicYearResolver years, StudentCreator creator,
    IStudentCredentials credentials, ImportCredentialsStore sheets) : IImportDefinition
{
    public string Kind => Lms.Shared.Imports.ImportKinds.Students;
    public string Permission => Permissions.Students.Import;
    public IReadOnlyList<ImportColumn> Columns => All;

    private sealed record UnitRules(bool HostsClasses, string CodeMode, bool GuardianRequired, Guid? YearId);

    public async Task<IReadOnlyList<RowError>> ValidateAsync(
        ImportValidationContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken ct)
    {
        var errors = new List<RowError>();
        errors.AddRange(ImportChecks.DuplicatesInFile(rows, StudentCode, v => v?.Trim().ToUpperInvariant()));
        errors.AddRange(ImportChecks.DuplicatesInFile(rows, NationalId, PersonNormalizer.NationalId));
        errors.AddRange(ImportChecks.DuplicatesInFile(rows, Email, PersonNormalizer.Email));

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var organizationId = context.TargetUnit.OrganizationId;
        var rules = new Dictionary<Guid, UnitRules>();
        var classes = new Dictionary<(Guid Year, string Code), Class?>();
        var added = new Dictionary<Guid, int>(); // students this file adds to each class so far (capacity)
        foreach (var row in rows)
        {
            var before = errors.Count;
            OrgUnit? unit = null;
            if (ImportChecks.UnitCode(row, UnitCode, context) is { } unknown)
            {
                errors.Add(unknown);
            }
            else if (row.Get(UnitCode) is { } unitCode && context.UnitsByCode.TryGetValue(unitCode.ToUpperInvariant(), out var found))
            {
                unit = found;
                var decision = await access.EvaluateAsync(Permissions.Students.Create, unit.Path, ct);
                if (!decision.Allowed)
                {
                    errors.Add(decision.Reason == AccessDenialReason.BlockedByPolicy
                        ? new RowError(row.RowNumber, UnitCode, "blocked_by_policy")
                        : new RowError(row.RowNumber, UnitCode, "unknown_unit", new Dictionary<string, string> { ["value"] = unitCode }));
                    unit = null;
                }
            }

            ValidateIdentity(row, today, errors);
            var email = PersonNormalizer.Email(row.Get(Email));
            if (email is not null && errors.Count == before && await users.FindByEmailAsync(email) is not null)
            {
                errors.Add(new RowError(row.RowNumber, Email, "duplicate_person", new Dictionary<string, string> { ["field"] = "email" }));
            }

            await ValidatePasswordAsync(row, errors);
            if (unit is null)
            {
                continue;
            }

            if (!rules.TryGetValue(unit.Id, out var unitRules))
            {
                unitRules = await LoadRulesAsync(unit, ct);
                rules[unit.Id] = unitRules;
            }

            if (!unitRules.HostsClasses)
            {
                errors.Add(new RowError(row.RowNumber, UnitCode, "invalid_level_type"));
            }

            await ValidateCodeAsync(row, unitRules, organizationId, errors, ct);
            await ValidateDuplicatesAsync(row, organizationId, errors, ct);
            ValidateGuardians(row, unitRules, errors);
            await ValidateClassAsync(row, unit, unitRules, classes, added, errors, ct);
        }

        return errors;
    }

    public async Task<int> CommitAsync(ImportCommitContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var sheet = new List<IssuedCredentialRow>();
        var homes = new Dictionary<Guid, (UnitRules Rules, string CapacityMode)>();
        foreach (var row in rows)
        {
            var unit = context.UnitsByCode[row.Get(UnitCode)!.ToUpperInvariant()];
            if (!homes.TryGetValue(unit.Id, out var home))
            {
                var capacity = (await settings.ResolveAsync(SettingKeys.ClassesCapacityMode, unit.Path, ct)).Value;
                home = (await LoadRulesAsync(unit, ct), capacity);
                homes[unit.Id] = home;
            }

            Class? placement = null;
            if (row.Get(ClassCode) is { Length: > 0 } classCode && home.Rules.YearId is { } yearId)
            {
                var key = classCode.Trim().ToUpperInvariant();
                placement = await db.Set<Class>().FirstAsync(c => c.AcademicYearId == yearId && c.CodeKey == key, ct);
            }

            var data = new CleanStudentData(
                row.Get(NameAr)!.Trim(), row.Get(NameEn)!.Trim(), null, null, DateOnly.ParseExact(row.Get(DateOfBirth)!, "yyyy-MM-dd"),
                Gen(row.Get(Gender))!, Rel(row.Get(Religion))!, Nat(row.Get(Nationality))!, PersonNormalizer.NationalId(row.Get(NationalId)),
                PersonNormalizer.Mobile(row.Get(Mobile)), PersonNormalizer.Email(row.Get(Email)), null);
            var guardians = GuardiansOf(row);
            var created = await creator.CreateAsync(
                new StudentDraft(
                    unit, data, home.Rules.CodeMode == StudentCodeModes.Manual ? row.Get(StudentCode)!.Trim() : null, guardians,
                    string.IsNullOrEmpty(row.Get(DefaultPassword)) ? null : row.Get(DefaultPassword), placement,
                    home.CapacityMode != "Block", today),
                ct);
            sheet.Add(new IssuedCredentialRow(
                created.Student.StudentCode, created.Student.NameAr, created.Student.NameEn, created.Issued.User.UserName ?? string.Empty,
                created.Issued.Password, created.Issued.DeliveredTo));
        }

        await db.SaveChangesAsync(ct);
        context.AfterCommit.Add(token => credentials.FlushDeliveriesAsync(token));
        context.AfterCommit.Add(_ =>
        {
            sheets.Put(context.Batch.Id, context.Batch.RequestedBy, context.Batch.Culture, sheet);
            return Task.CompletedTask;
        });
        return rows.Count;
    }

    private async Task<UnitRules> LoadRulesAsync(OrgUnit unit, CancellationToken ct)
    {
        var level = await db.LevelTypes.FirstOrDefaultAsync(l => l.Id == unit.LevelTypeId, ct);
        var mode = (await settings.ResolveAsync(SettingKeys.StudentsCodeMode, unit.Path, ct)).Value;
        var required = (await settings.ResolveAsync(SettingKeys.GuardiansDataRequired, unit.Path, ct)).Value;
        var year = await years.GetCurrentAsync(unit.Path, ct);
        return new UnitRules(level is { CanHostClasses: true }, mode, required, year?.Id);
    }

    private static void ValidateIdentity(ImportRowValues row, DateOnly today, List<RowError> errors)
    {
        if (row.Get(DateOfBirth) is { } dob && !(TryDate(dob, out var date) && date <= today && date.Year >= 1990))
        {
            errors.Add(new RowError(row.RowNumber, DateOfBirth, "invalid_date"));
        }

        if (row.Get(Gender) is not null && Gen(row.Get(Gender)) is null)
        {
            errors.Add(new RowError(row.RowNumber, Gender, "not_allowed", new Dictionary<string, string> { ["value"] = row.Get(Gender)! }));
        }

        if (row.Get(Religion) is not null && Rel(row.Get(Religion)) is null)
        {
            errors.Add(new RowError(row.RowNumber, Religion, "not_allowed", new Dictionary<string, string> { ["value"] = row.Get(Religion)! }));
        }

        if (row.Get(Nationality) is not null && Nat(row.Get(Nationality)) is null)
        {
            errors.Add(new RowError(row.RowNumber, Nationality, "not_allowed", new Dictionary<string, string> { ["value"] = row.Get(Nationality)! }));
        }

        CheckContact(row, NationalId, Mobile, Email, errors);
    }

    private static void CheckContact(ImportRowValues row, string nationalIdKey, string mobileKey, string emailKey, List<RowError> errors)
    {
        if (!string.IsNullOrEmpty(row.Get(nationalIdKey)) && PersonNormalizer.NationalId(row.Get(nationalIdKey)) is null)
        {
            errors.Add(new RowError(row.RowNumber, nationalIdKey, "invalid_national_id"));
        }

        if (!string.IsNullOrEmpty(row.Get(mobileKey)) && PersonNormalizer.Mobile(row.Get(mobileKey)) is null)
        {
            errors.Add(new RowError(row.RowNumber, mobileKey, "invalid_mobile"));
        }

        if (!string.IsNullOrEmpty(row.Get(emailKey))
            && (PersonNormalizer.Email(row.Get(emailKey)) is not { Length: <= 256 } email
                || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
        {
            errors.Add(new RowError(row.RowNumber, emailKey, "invalid_email"));
        }
    }

    /// <summary>The Identity password rules, run on the value the commit will use; the value itself never leaves this method.</summary>
    private async Task ValidatePasswordAsync(ImportRowValues row, List<RowError> errors)
    {
        if (string.IsNullOrEmpty(row.Get(DefaultPassword)))
        {
            return;
        }

        var probe = new User { UserName = "probe" };
        foreach (var validator in users.PasswordValidators)
        {
            if (!(await validator.ValidateAsync(users, probe, row.Get(DefaultPassword)!)).Succeeded)
            {
                errors.Add(new RowError(row.RowNumber, DefaultPassword, "invalid_password"));
                return;
            }
        }
    }

    private async Task ValidateCodeAsync(ImportRowValues row, UnitRules rules, Guid organizationId, List<RowError> errors, CancellationToken ct)
    {
        var code = row.Get(StudentCode)?.Trim();
        if (rules.CodeMode == StudentCodeModes.Auto)
        {
            if (!string.IsNullOrEmpty(code))
            {
                errors.Add(new RowError(row.RowNumber, StudentCode, "student_code_not_allowed"));
            }

            return;
        }

        if (string.IsNullOrEmpty(code))
        {
            errors.Add(new RowError(row.RowNumber, StudentCode, "student_code_required"));
        }
        else if (await codes.ValidateManualAsync(organizationId, code, null, ct) is { } problem)
        {
            errors.Add(problem == ProblemCodes.student_code_taken
                ? new RowError(row.RowNumber, StudentCode, "student_code_taken")
                : new RowError(row.RowNumber, StudentCode, "invalid_student_code"));
        }
    }

    private async Task ValidateDuplicatesAsync(ImportRowValues row, Guid organizationId, List<RowError> errors, CancellationToken ct)
    {
        var nationalId = PersonNormalizer.NationalId(row.Get(NationalId));
        if (nationalId is not null && await duplicates.StudentNationalIdExistsAsync(organizationId, nationalId, null, ct))
        {
            errors.Add(new RowError(row.RowNumber, NationalId, "duplicate_person", new Dictionary<string, string> { ["field"] = "nationalId" }));
            return;
        }

        if (TryDate(row.Get(DateOfBirth), out var dob) && row.Get(NameAr) is { } nameAr && row.Get(NameEn) is { } nameEn
            && await duplicates.CountStudentsByNameAndDobAsync(
                organizationId, PersonNormalizer.NameKey(nameAr), PersonNormalizer.NameKey(nameEn), dob, null, ct) > 0)
        {
            // A possible duplicate blocks the file (Q12): the person removes the row or changes it.
            errors.Add(new RowError(row.RowNumber, null, "possible_duplicate"));
        }
    }

    private static void ValidateGuardians(ImportRowValues row, UnitRules rules, List<RowError> errors)
    {
        var filled = new[] { GroupFilled(row, 1), GroupFilled(row, 2) };
        if (rules.GuardianRequired && !filled[0])
        {
            errors.Add(new RowError(row.RowNumber, G(1, GName), "guardian_required"));
        }

        for (var n = 1; n <= 2; n++)
        {
            if (!filled[n - 1])
            {
                continue;
            }

            foreach (var part in new[] { GName, GNameEn, GRelation, GMobile })
            {
                if (string.IsNullOrWhiteSpace(row.Get(G(n, part))))
                {
                    errors.Add(new RowError(row.RowNumber, G(n, part), "guardian_incomplete"));
                }
            }

            CheckContact(row, G(n, GNationalId), G(n, GMobile), G(n, GEmail), errors);
        }

        if (filled[1] && !filled[0])
        {
            errors.Add(new RowError(row.RowNumber, G(2, GName), "guardian_incomplete")); // guardian 1 is the primary one
        }
    }

    private async Task ValidateClassAsync(
        ImportRowValues row, OrgUnit unit, UnitRules rules, Dictionary<(Guid Year, string Code), Class?> classes,
        Dictionary<Guid, int> added, List<RowError> errors, CancellationToken ct)
    {
        if (row.Get(ClassCode)?.Trim() is not { Length: > 0 } code)
        {
            return;
        }

        if (rules.YearId is not { } yearId)
        {
            errors.Add(new RowError(row.RowNumber, ClassCode, "unknown_class", new Dictionary<string, string> { ["value"] = code }));
            return;
        }

        var key = code.ToUpperInvariant();
        if (!classes.TryGetValue((yearId, key), out var cls))
        {
            cls = await db.Set<Class>().FirstOrDefaultAsync(c => c.AcademicYearId == yearId && c.CodeKey == key, ct);
            classes[(yearId, key)] = cls;
        }

        if (cls is null)
        {
            errors.Add(new RowError(row.RowNumber, ClassCode, "unknown_class", new Dictionary<string, string> { ["value"] = code }));
        }
        else if (cls.OrgUnitId != unit.Id)
        {
            errors.Add(new RowError(row.RowNumber, ClassCode, "class_unit_mismatch"));
        }
        else if (cls.Status == Class.ClosedStatus)
        {
            errors.Add(new RowError(row.RowNumber, ClassCode, "class_closed"));
        }
        else if (cls.Capacity is { } capacity)
        {
            var current = await db.Set<ClassStudent>().CountAsync(c => c.ClassId == cls.Id && c.Status == ClassStudent.ActiveStatus, ct);
            var mine = added.GetValueOrDefault(cls.Id) + 1;
            added[cls.Id] = mine;
            var mode = (await settings.ResolveAsync(SettingKeys.ClassesCapacityMode, cls.OrgUnitPath, ct)).Value;
            if (current + mine > capacity && mode == "Block")
            {
                errors.Add(new RowError(row.RowNumber, ClassCode, "class_full"));
            }
        }
    }

    private static IReadOnlyList<GuardianLink> GuardiansOf(ImportRowValues row)
    {
        var links = new List<GuardianLink>();
        for (var n = 1; n <= 2; n++)
        {
            if (!GroupFilled(row, n))
            {
                continue;
            }

            links.Add(new GuardianLink(
                null,
                new GuardianData(
                    row.Get(G(n, GName))!.Trim(), row.Get(G(n, GNameEn))!.Trim(), PersonNormalizer.Mobile(row.Get(G(n, GMobile)))!,
                    PersonNormalizer.Email(row.Get(G(n, GEmail))), PersonNormalizer.NationalId(row.Get(G(n, GNationalId)))),
                GuardianRelations.All.First(r => string.Equals(r, row.Get(G(n, GRelation))!.Trim(), StringComparison.OrdinalIgnoreCase)),
                n == 1));
        }

        return links;
    }
}
