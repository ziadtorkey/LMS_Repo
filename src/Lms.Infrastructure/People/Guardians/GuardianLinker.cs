using System.Net.Mail;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.People.Guardians;

/// <inheritdoc />
public sealed class GuardianLinker(
    LmsDbContext db, IPersonDuplicateFinder duplicates, ISettingsResolver settings, IEnumerable<IGuardianLinkObserver> observers)
    : IGuardianLinker
{
    public const string NotFoundCode = "not_found";

    private const int MaxName = 200;

    public async Task<IReadOnlyList<StudentGuardian>> LinkAsync(
        StudentProfile student, IReadOnlyList<GuardianLink> inputs, CancellationToken ct)
    {
        var existing = await LinksOfAsync(student, ct);
        var all = existing.ToList();
        var created = new List<(StudentGuardian Link, bool Primary)>();
        var guardians = new Dictionary<Guid, GuardianProfile>();
        foreach (var input in inputs)
        {
            ValidateRelation(input.Relation);
            var guardian = await ResolveAsync(student, input, ct);
            if (all.Any(l => l.GuardianId == guardian.Id) || created.Any(c => c.Link.GuardianId == guardian.Id))
            {
                throw new GuardianLinkException(ProblemCodes.guardian_link_exists);
            }

            var link = StudentGuardian.For(student, guardian, input.Relation, isPrimary: false);
            db.Set<StudentGuardian>().Add(link);
            all.Add(link);
            created.Add((link, input.IsPrimary));
            guardians[guardian.Id] = guardian;
        }

        // Exactly one primary afterwards: the first input marked primary, else the one that already was, else the oldest link.
        var newPrimary = created.FirstOrDefault(c => c.Primary).Link;
        var changed = new HashSet<Guid>(created.Select(c => c.Link.GuardianId));
        if (newPrimary is not null)
        {
            foreach (var link in all.Where(l => l.IsPrimary && l != newPrimary))
            {
                link.Update(link.Relation, false);
                changed.Add(link.GuardianId);
            }

            newPrimary.Update(newPrimary.Relation, true);
        }
        else if (all.Count > 0 && all.All(l => !l.IsPrimary))
        {
            var oldest = existing.OrderBy(l => l.CreatedAt).FirstOrDefault() ?? created[0].Link;
            oldest.Update(oldest.Relation, true);
            changed.Add(oldest.GuardianId);
        }

        await db.SaveChangesAsync(ct);
        await NotifyAsync(changed, guardians, ct);
        return created.Select(c => c.Link).ToList();
    }

    public async Task<StudentGuardian> UpdateLinkAsync(
        StudentProfile student, Guid guardianId, string relation, bool isPrimary, CancellationToken ct)
    {
        ValidateRelation(relation);
        var links = await LinksOfAsync(student, ct);
        var link = links.FirstOrDefault(l => l.GuardianId == guardianId) ?? throw new GuardianLinkException(GuardianLinker.NotFoundCode);
        var changed = new HashSet<Guid> { guardianId };
        if (!isPrimary && link.IsPrimary)
        {
            throw new GuardianLinkException(ProblemCodes.primary_guardian_required);
        }

        if (isPrimary)
        {
            foreach (var other in links.Where(l => l.IsPrimary && l != link))
            {
                other.Update(other.Relation, false);
                changed.Add(other.GuardianId);
            }
        }

        link.Update(relation, isPrimary);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(changed, null, ct);
        return link;
    }

    public async Task UnlinkAsync(StudentProfile student, Guid guardianId, CancellationToken ct)
    {
        var links = await LinksOfAsync(student, ct);
        var link = links.FirstOrDefault(l => l.GuardianId == guardianId) ?? throw new GuardianLinkException(GuardianLinker.NotFoundCode);
        var remaining = links.Where(l => l != link).OrderBy(l => l.CreatedAt).ToList();
        await EnsureRequirementAsync(student, remaining.Count, ct);

        db.Set<StudentGuardian>().Remove(link); // soft delete; the guardian profile stays (siblings may share it)
        if (link.IsPrimary && remaining.Count > 0)
        {
            remaining[0].Update(remaining[0].Relation, true); // the oldest remaining link takes over
        }

        await db.SaveChangesAsync(ct);
        await NotifyAsync([guardianId, .. remaining.Take(1).Select(l => l.GuardianId)], null, ct);
    }

    public async Task EnsureRequirementAsync(StudentProfile student, int guardianCount, CancellationToken ct)
    {
        if (guardianCount > 0)
        {
            return;
        }

        var required = (await settings.ResolveAsync(SettingKeys.GuardiansDataRequired, student.OrgUnitPath, ct)).Value;
        if (required)
        {
            throw new GuardianLinkException(ProblemCodes.guardian_required);
        }
    }

    public async Task<GuardianProfile> UpdateGuardianAsync(Guid guardianId, GuardianData data, CancellationToken ct)
    {
        var guardian = await db.Set<GuardianProfile>().FirstOrDefaultAsync(g => g.Id == guardianId, ct)
            ?? throw new GuardianLinkException(GuardianLinker.NotFoundCode);
        var clean = Validate(data);

        // Another guardian of the client with the same national id or mobile: only the field name is reported.
        var conflict = await db.Set<GuardianProfile>().Where(g => g.Id != guardianId).Select(g => new { g.NationalId, g.Mobile })
            .Where(g => (clean.NationalId != null && g.NationalId == clean.NationalId) || g.Mobile == clean.Mobile)
            .FirstOrDefaultAsync(ct);
        if (conflict is not null)
        {
            throw new GuardianLinkException(ProblemCodes.duplicate_person,
                clean.NationalId != null && conflict.NationalId == clean.NationalId ? "nationalId" : "mobile");
        }

        guardian.Update(clean.NameAr, clean.NameEn, clean.Mobile, clean.Email, clean.NationalId);
        await db.SaveChangesAsync(ct);
        await NotifyAsync([guardianId], new Dictionary<Guid, GuardianProfile> { [guardianId] = guardian }, ct);
        return guardian;
    }

    private async Task<List<StudentGuardian>> LinksOfAsync(StudentProfile student, CancellationToken ct) =>
        await db.Set<StudentGuardian>().Where(l => l.StudentId == student.Id).ToListAsync(ct);

    private async Task<GuardianProfile> ResolveAsync(StudentProfile student, GuardianLink input, CancellationToken ct)
    {
        if ((input.ExistingGuardianId is null) == (input.New is null))
        {
            throw new GuardianLinkException(ProblemCodes.validation_failed, "Guardian");
        }

        if (input.ExistingGuardianId is { } id)
        {
            // The client filter of the table: a guardian of another client does not exist here. The id comes from the guardian
            // list or from the match endpoint of this client.
            return await db.Set<GuardianProfile>().FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == student.OrganizationId, ct)
                ?? throw new GuardianLinkException(GuardianLinker.NotFoundCode);
        }

        var data = Validate(input.New!);
        // Siblings share one guardian: a guardian of the client with the same national id (else mobile) is reused as is.
        var found = await duplicates.FindGuardianAsync(student.OrganizationId, data.NationalId, data.Mobile, ct);
        if (found is not null)
        {
            return found;
        }

        var guardian = new GuardianProfile(student.OrganizationId, data.NameAr, data.NameEn, data.Mobile, data.Email, data.NationalId);
        db.Set<GuardianProfile>().Add(guardian);
        return guardian;
    }

    private static GuardianData Validate(GuardianData data)
    {
        var nameAr = data.NameAr?.Trim() ?? string.Empty;
        var nameEn = data.NameEn?.Trim() ?? string.Empty;
        if (nameAr.Length is 0 or > MaxName)
        {
            throw new GuardianLinkException(ProblemCodes.validation_failed, "NameAr");
        }

        if (nameEn.Length is 0 or > MaxName)
        {
            throw new GuardianLinkException(ProblemCodes.validation_failed, "NameEn");
        }

        var mobile = PersonNormalizer.Mobile(data.Mobile) ?? throw new GuardianLinkException(ProblemCodes.validation_failed, "Mobile");
        string? email = null;
        if (!string.IsNullOrWhiteSpace(data.Email))
        {
            email = PersonNormalizer.Email(data.Email);
            if (email is null || email.Length > 256 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            {
                throw new GuardianLinkException(ProblemCodes.validation_failed, "Email");
            }
        }

        string? nationalId = null;
        if (!string.IsNullOrWhiteSpace(data.NationalId))
        {
            nationalId = PersonNormalizer.NationalId(data.NationalId) ?? throw new GuardianLinkException(ProblemCodes.validation_failed, "NationalId");
        }

        return new GuardianData(nameAr, nameEn, mobile, email, nationalId);
    }

    private static void ValidateRelation(string? relation)
    {
        if (relation is null || !GuardianRelations.All.Contains(relation))
        {
            throw new GuardianLinkException(ProblemCodes.validation_failed, "Relation");
        }
    }

    private async Task NotifyAsync(IEnumerable<Guid> guardianIds, IReadOnlyDictionary<Guid, GuardianProfile>? known, CancellationToken ct)
    {
        foreach (var id in guardianIds.Distinct())
        {
            var guardian = known?.GetValueOrDefault(id) ?? await db.Set<GuardianProfile>().FirstOrDefaultAsync(g => g.Id == id, ct);
            if (guardian is null)
            {
                continue;
            }

            foreach (var observer in observers)
            {
                await observer.OnLinksChangedAsync(guardian, ct);
            }
        }
    }
}
