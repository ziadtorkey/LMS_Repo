using System.Security.Claims;
using System.Text.Json;
using Lms.Domain.Auditing;
using Lms.Domain.Identity;
using Lms.Domain.IdentityProviders;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Identity.External;

/// <summary>The account an external sign-in resolved to, or the problem code to show (never both).</summary>
public sealed record ExternalLinkResult(User? User, string? ErrorCode);

/// <summary>
/// Turns an identity proven by a client's provider into one of that client's existing accounts (FR-SEC-08). Nothing is
/// ever created: an identity nobody in the client owns is <c>external_account_not_found</c>. Only people of the provider's
/// own client can match, so one client's provider can never reach a user who belongs to another client alone.
/// </summary>
public sealed class ExternalLoginLinker(UserManager<User> users, LmsDbContext db, IAuditWriter audit)
{
    public const string NotFound = "external_account_not_found";
    public const string Ambiguous = "external_account_ambiguous";
    public const string Failed = "external_failed";

    public async Task<ExternalLinkResult> ResolveAsync(
        IdentityProviderConfig provider, ClaimsPrincipal external, CancellationToken ct)
    {
        var scheme = ExternalSchemeRegistry.SchemeName(provider.Id);
        var subject = external.FindFirst("sub")?.Value ?? external.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(subject))
        {
            return await FailAsync(provider, Failed, ct);
        }

        // 1) Already linked: the link wins, whatever the provider now says about the e-mail address.
        var linked = await users.FindByLoginAsync(scheme, subject);
        if (linked is not null)
        {
            // A link is not a membership: the person may have left the client since, and must never be a platform admin.
            if (linked.IsPlatformAdmin)
            {
                return await FailAsync(provider, NotFound, ct, linked.Id, "platform_admin");
            }

            if (!await BelongsToClientAsync(linked.Id, provider.OrganizationId, ct))
            {
                return await FailAsync(provider, NotFound, ct, linked.Id, "not_member");
            }

            await audit.WriteSecurityEventAsync("ExternalLoginSucceeded", linked.Id, Details(provider), null, ct);
            return new ExternalLinkResult(linked, null);
        }

        // 2) First sign-in: find the one person of this client the claims point to.
        var (candidate, error) = provider.MatchBy == ExternalMatchBy.NationalId
            ? await MatchByNationalIdAsync(provider, external, ct)
            : await MatchByEmailAsync(provider, external, ct);
        if (candidate is null)
        {
            return await FailAsync(provider, error!, ct);
        }

        // A session started through a client's provider never carries platform rights (the tenant controls that provider).
        // The caller sees the same generic message as for an unknown identity; the reason is only in the audit log.
        if (candidate.IsPlatformAdmin)
        {
            return await FailAsync(provider, NotFound, ct, candidate.Id, "platform_admin");
        }

        // 3) Link and sign in.
        var added = await users.AddLoginAsync(candidate, new UserLoginInfo(scheme, subject, provider.DisplayNameEn));
        if (!added.Succeeded)
        {
            return await FailAsync(provider, Failed, ct);
        }

        await audit.WriteSecurityEventAsync("ExternalLoginLinked", candidate.Id, Details(provider), null, ct);
        await audit.WriteSecurityEventAsync("ExternalLoginSucceeded", candidate.Id, Details(provider), null, ct);
        return new ExternalLinkResult(candidate, null);
    }

    private async Task<(User?, string?)> MatchByEmailAsync(IdentityProviderConfig provider, ClaimsPrincipal external, CancellationToken ct)
    {
        var email = external.FindFirst("email")?.Value;
        if (string.IsNullOrWhiteSpace(email) && provider.Kind == IdentityProviderKind.AzureAd)
        {
            email = external.FindFirst("preferred_username")?.Value;
        }

        email = PersonNormalizer.Email(email);
        var user = email is null ? null : await users.FindByEmailAsync(email);
        if (user is null || !await BelongsToClientAsync(user.Id, provider.OrganizationId, ct))
        {
            return (null, NotFound);
        }

        return (user, null);
    }

    private async Task<(User?, string?)> MatchByNationalIdAsync(IdentityProviderConfig provider, ClaimsPrincipal external, CancellationToken ct)
    {
        var nationalId = provider.NationalIdClaim is { } claim ? PersonNormalizer.NationalId(external.FindFirst(claim)?.Value) : null;
        if (nationalId is null)
        {
            return (null, NotFound);
        }

        var organizationId = provider.OrganizationId;

        // ISOLATION-BYPASS: sign-in happens before any client context exists. Every query below is limited to the provider's
        // own client and live rows explicitly; only user ids leave this method.
        var staff = db.Set<StaffProfile>().IgnoreQueryFilters()
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted && p.NationalId == nationalId).Select(p => (Guid?)p.UserId);
        // ISOLATION-BYPASS: same as above (own client, live rows, explicit).
        var students = db.Set<StudentProfile>().IgnoreQueryFilters()
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted && p.NationalId == nationalId && p.UserId != null).Select(p => p.UserId);
        // ISOLATION-BYPASS: same as above (own client, live rows, explicit).
        var guardians = db.Set<GuardianProfile>().IgnoreQueryFilters()
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted && p.NationalId == nationalId && p.UserId != null).Select(p => p.UserId);
        var ids = (await staff.Concat(students).Concat(guardians).Distinct().ToListAsync(ct)).Where(id => id is not null).ToList();

        return ids.Count switch
        {
            0 => (null, NotFound),
            1 => (await users.FindByIdAsync(ids[0]!.Value.ToString()), null),
            _ => (null, Ambiguous),
        };
    }

    private async Task<bool> BelongsToClientAsync(Guid userId, Guid organizationId, CancellationToken ct)
    {
        // ISOLATION-BYPASS: see MatchByNationalIdAsync; the client and soft-delete filters are re-applied explicitly.
        var staff = db.Set<StaffProfile>().IgnoreQueryFilters()
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted && p.UserId == userId);
        // ISOLATION-BYPASS: same as above.
        var students = db.Set<StudentProfile>().IgnoreQueryFilters()
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted && p.UserId == userId);
        // ISOLATION-BYPASS: same as above.
        var guardians = db.Set<GuardianProfile>().IgnoreQueryFilters()
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted && p.UserId == userId);
        // ISOLATION-BYPASS: same as above.
        var assignments = db.RoleAssignments.IgnoreQueryFilters()
            .Where(a => a.OrganizationId == organizationId && !a.IsDeleted && a.UserId == userId);
        return await staff.AnyAsync(ct) || await students.AnyAsync(ct) || await guardians.AnyAsync(ct) || await assignments.AnyAsync(ct);
    }

    private async Task<ExternalLinkResult> FailAsync(
        IdentityProviderConfig provider, string code, CancellationToken ct, Guid? userId = null, string? reason = null)
    {
        await audit.WriteSecurityEventAsync("ExternalLoginFailed", userId, Details(provider, reason ?? code), null, ct);
        return new ExternalLinkResult(null, code);
    }

    private static string Details(IdentityProviderConfig provider, string? reason = null) =>
        JsonSerializer.Serialize(new { provider = provider.Slug, reason });
}
