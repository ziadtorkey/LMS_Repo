using System.Net.Mail;
using Lms.Api.Features.Assignments;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Imports;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Staff.Import;

/// <summary>
/// Staff from an Excel file (FR-STF-04, X-06): one row is one person with one role at one unit (home unit = role unit = the
/// row's unit code). The rules are those of creating a person by hand, row by row: permission and policies at the unit,
/// role kind and escalation, role-blocking policies, duplicates in the file and in the client. Commit runs inside the
/// framework's all-or-nothing transaction; invitations go out only after it committed.
/// </summary>
public sealed class StaffImportDefinition(
    LmsDbContext db, IClock clock, IPersonDuplicateFinder duplicates, IAccessEvaluator access, IRoleGrantGuard roleGuard,
    CallerPermissions caller, StaffCreator creator) : IImportDefinition
{
    public const string KindName = "staff";

    private const string NameAr = nameof(NameAr), NameEn = nameof(NameEn), Email = nameof(Email), Mobile = nameof(Mobile),
        NationalId = nameof(NationalId), JobTitleAr = nameof(JobTitleAr), JobTitleEn = nameof(JobTitleEn), Role = nameof(Role),
        UnitCode = nameof(UnitCode), ValidFrom = nameof(ValidFrom), ExpiresOn = nameof(ExpiresOn);

    public string Kind => KindName;
    public string Permission => Permissions.Staff.Import;

    public IReadOnlyList<ImportColumn> Columns { get; } =
    [
        new(NameAr, "الاسم بالعربية", "Name (Arabic)", true),
        new(NameEn, "الاسم بالإنجليزية", "Name (English)", true),
        new(Email, "البريد الإلكتروني", "Email", true, ExampleValue: "teacher@school.example"),
        new(Mobile, "رقم الجوال", "Mobile", false, AsText: true, ExampleValue: "+201001234567"),
        new(NationalId, "رقم الهوية الوطنية", "National ID", false, AsText: true),
        new(JobTitleAr, "المسمى الوظيفي بالعربية", "Job title (Arabic)", false),
        new(JobTitleEn, "المسمى الوظيفي بالإنجليزية", "Job title (English)", false),
        new(Role, "الدور", "Role", true, ExampleValue: "Teacher"),
        new(UnitCode, "رمز الوحدة", "Unit code", true, AsText: true),
        new(ValidFrom, "بداية صلاحية الدور", "Role valid from", false, ExampleValue: "2026-09-01"),
        new(ExpiresOn, "تاريخ انتهاء الحساب", "Account expires on", false, ExampleValue: "2027-06-30"),
    ];

    public async Task<IReadOnlyList<RowError>> ValidateAsync(
        ImportValidationContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken ct)
    {
        var errors = new List<RowError>();
        errors.AddRange(ImportChecks.DuplicatesInFile(rows, Email, PersonNormalizer.Email));
        errors.AddRange(ImportChecks.DuplicatesInFile(rows, Mobile, PersonNormalizer.Mobile));
        errors.AddRange(ImportChecks.DuplicatesInFile(rows, NationalId, PersonNormalizer.NationalId));

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var organizationId = context.TargetUnit.OrganizationId;
        var roles = await LoadRolesAsync(organizationId, ct);
        foreach (var row in rows)
        {
            var email = PersonNormalizer.Email(row.Get(Email));
            var mobile = PersonNormalizer.Mobile(row.Get(Mobile));
            var nationalId = PersonNormalizer.NationalId(row.Get(NationalId));
            var identityOk = true;
            if (email is null || email.Length > 256 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            {
                errors.Add(new RowError(row.RowNumber, Email, "invalid_email"));
                identityOk = false;
            }

            if (!string.IsNullOrEmpty(row.Get(Mobile)) && mobile is null)
            {
                errors.Add(new RowError(row.RowNumber, Mobile, "invalid_mobile"));
                identityOk = false;
            }

            if (!string.IsNullOrEmpty(row.Get(NationalId)) && nationalId is null)
            {
                errors.Add(new RowError(row.RowNumber, NationalId, "invalid_national_id"));
                identityOk = false;
            }

            AddIfNotNull(errors, ImportChecks.Date(row, ValidFrom));
            if (ImportChecks.Date(row, ExpiresOn) is { } badDate)
            {
                errors.Add(badDate);
            }
            else if (ImportChecks.TryParseDate(row.Get(ExpiresOn), out var expiry) && expiry < today)
            {
                errors.Add(new RowError(row.RowNumber, ExpiresOn, "invalid_expiry"));
            }

            if (ImportChecks.UnitCode(row, UnitCode, context) is { } unknownUnit)
            {
                errors.Add(unknownUnit);
            }
            else if (row.Get(UnitCode) is { } code && context.UnitsByCode.TryGetValue(code.ToUpperInvariant(), out var unit))
            {
                errors.AddRange(await CheckUnitAndRoleAsync(row, unit, roles, ct));
            }

            if (identityOk && email is not null
                && await duplicates.FindStaffConflictAsync(organizationId, email, mobile, nationalId, null, ct) is { } field)
            {
                errors.Add(new RowError(row.RowNumber, Column(field), "duplicate_person",
                    new Dictionary<string, string> { ["field"] = field }));
            }
        }

        return errors;
    }

    public async Task<int> CommitAsync(ImportCommitContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken ct)
    {
        var roles = await LoadRolesAsync(context.TargetUnit.OrganizationId, ct);
        foreach (var row in rows)
        {
            var unit = context.UnitsByCode[row.Get(UnitCode)!.ToUpperInvariant()];
            var role = FindRole(roles, row.Get(Role)!)!;
            DateOnly? validFrom = ImportChecks.TryParseDate(row.Get(ValidFrom), out var from) ? from : null;
            DateOnly? expiresOn = ImportChecks.TryParseDate(row.Get(ExpiresOn), out var expiry) ? expiry : null;
            var created = await creator.AddAsync(new StaffDraft(
                unit, row.Get(NameAr)!.Trim(), row.Get(NameEn)!.Trim(), PersonNormalizer.Email(row.Get(Email))!,
                PersonNormalizer.Mobile(row.Get(Mobile)), PersonNormalizer.NationalId(row.Get(NationalId)),
                Clean(row.Get(JobTitleAr)), Clean(row.Get(JobTitleEn)), null, expiresOn,
                [new StaffRoleDraft(role, unit, validFrom, null)]), ct);
            if (created.AssignmentExists)
            {
                // Rolls the whole import back; the framework reports the batch as failed.
                throw new InvalidOperationException($"Row {row.RowNumber}: the person already has this role at this unit.");
            }

            var user = created.User!;
            context.AfterCommit.Add(token => creator.InviteAsync(user, unit, token));
        }

        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    private async Task<IEnumerable<RowError>> CheckUnitAndRoleAsync(
        ImportRowValues row, OrgUnit unit, IReadOnlyList<Role> roles, CancellationToken ct)
    {
        var errors = new List<RowError>();

        // The same permissions and policies as creating the person by hand at that unit.
        foreach (var permission in new[] { Permissions.Staff.Import, Permissions.Assignments.Create })
        {
            var decision = await access.EvaluateAsync(permission, unit.Path, ct);
            if (!decision.Allowed)
            {
                errors.Add(decision.Reason == AccessDenialReason.BlockedByPolicy
                    ? new RowError(row.RowNumber, UnitCode, "blocked_by_policy")
                    : new RowError(row.RowNumber, UnitCode, "unknown_unit", new Dictionary<string, string> { ["value"] = row.Get(UnitCode)! }));
                return errors;
            }
        }

        var roleText = row.Get(Role);
        var role = roleText is null ? null : FindRole(roles, roleText);
        if (role is null)
        {
            errors.Add(new RowError(row.RowNumber, Role, "unknown_role", new Dictionary<string, string> { ["value"] = roleText ?? string.Empty }));
        }
        else if (role.BaseKind != RoleBaseKind.Staff)
        {
            errors.Add(new RowError(row.RowNumber, Role, "invalid_role_kind"));
        }
        else if (!await AssignmentQueries.CallerMayManageRoleAsync(db, caller, role.Id, ct))
        {
            errors.Add(new RowError(row.RowNumber, Role, "permission_escalation"));
        }
        else if (!(await roleGuard.CheckAsync(role, unit, ct)).Allowed)
        {
            errors.Add(new RowError(row.RowNumber, Role, "blocked_by_policy"));
        }

        return errors;
    }

    private async Task<IReadOnlyList<Role>> LoadRolesAsync(Guid organizationId, CancellationToken ct) =>
        await db.Roles.Where(r => r.OrganizationId == organizationId).ToListAsync(ct);

    /// <summary>A role by its Arabic name, English name or built-in key, ignoring case and surrounding spaces.</summary>
    private static Role? FindRole(IReadOnlyList<Role> roles, string text)
    {
        var wanted = text.Trim();
        return roles.FirstOrDefault(r =>
            string.Equals(r.NameAr, wanted, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.NameEn, wanted, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.BuiltInKey, wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static string Column(string field) => field switch
    {
        "mobile" => Mobile,
        "nationalId" => NationalId,
        _ => Email,
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddIfNotNull(List<RowError> errors, RowError? error)
    {
        if (error is not null)
        {
            errors.Add(error);
        }
    }
}
