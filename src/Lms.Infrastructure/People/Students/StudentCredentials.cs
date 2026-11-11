using Lms.Domain.Identity;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.People.Guardians;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.People.Students;

/// <inheritdoc />
public sealed class StudentCredentials(
    UserManager<User> users, LmsDbContext db, IPersonAccounts accounts, ISettingsResolver settings, IAccountEmails emails,
    ILogger<StudentCredentials> logger) : IStudentCredentials
{
    private readonly List<(string UserName, string Password, string ToEmail, string Culture)> _queue = [];

    public async Task<IssuedCredentials> IssueAsync(
        StudentProfile student, string? requestedPassword, bool reset, CancellationToken ct)
    {
        // D47: the sign-in name is always {code}@{client short code}, so two clients can reuse a student code without sharing an account.
        var shortCode = await db.Organizations.Where(o => o.Id == student.OrganizationId).Select(o => o.ShortCode).SingleAsync(ct);
        var userName = $"{student.StudentCode.ToLowerInvariant()}@{shortCode}";
        var password = string.IsNullOrEmpty(requestedPassword) ? PasswordGenerator.Generate() : requestedPassword;
        if (!string.IsNullOrEmpty(requestedPassword))
        {
            await ValidateAsync(userName, requestedPassword);
        }

        User user;
        var created = false;
        if (student.UserId is { } linked)
        {
            user = await users.FindByIdAsync(linked.ToString()) ?? throw new InvalidOperationException("The student's account is missing.");
        }
        else
        {
            // An existing account is reused only when its owner already has a profile in THIS client (a staff member who is also a
            // parent, a guardian, another student): otherwise setting its password would take over another client's account.
            var emailOwner = PersonNormalizer.Email(student.Email) is { } email ? await users.FindByEmailAsync(email) : null;
            if (emailOwner is not null && !await HasProfileInClientAsync(emailOwner.Id, student.OrganizationId, ct))
            {
                throw new DuplicateAccountException("Email");
            }

            var nameOwner = await users.FindByNameAsync(userName);
            if (nameOwner is not null && !await HasProfileInClientAsync(nameOwner.Id, student.OrganizationId, ct))
            {
                throw new DuplicateAccountException("StudentCode");
            }

            (user, created) = await accounts.EnsureUserAsync(student.Email, userName, student.NameAr, student.NameEn, ct);
        }

        student.LinkUser(user.Id);

        // An account that already belongs to someone with a password of their own (a staff member who is also a student's parent
        // account, say) keeps it: the default password is only for accounts that have none, or that still use their default.
        if (!(created || reset || user.PasswordHash is null || user.MustChangePassword))
        {
            return new IssuedCredentials(user, null, CredentialsDelivery.None);
        }

        user.MustChangePassword = true;
        if (user.PasswordHash is not null)
        {
            ThrowIfFailed(await users.RemovePasswordAsync(user));
        }

        ThrowIfFailed(await users.AddPasswordAsync(user, password));
        await users.UpdateSecurityStampAsync(user); // sessions that used the old password end

        var (toEmail, delivered) = await ResolveRecipientAsync(student, ct);
        if (toEmail is not null)
        {
            var culture = (await settings.ResolveAsync(SettingKeys.UiDefaultCulture, student.OrgUnitPath, ct)).Value;
            _queue.Add((userName, password, toEmail, culture));
        }

        return new IssuedCredentials(user, password, delivered);
    }

    public async Task FlushDeliveriesAsync(CancellationToken ct)
    {
        var pending = _queue.ToArray();
        _queue.Clear();
        foreach (var (userName, password, toEmail, culture) in pending)
        {
            try
            {
                await emails.SendStudentCredentialsAsync(userName, password, toEmail, culture, ct);
            }
            catch (Exception ex)
            {
                // Never log the password; the school can still hand the credentials over (they were shown once in the response).
                logger.LogWarning(ex, "Credentials email for student account {UserName} could not be sent", userName);
            }
        }
    }

    private async Task<bool> HasProfileInClientAsync(Guid userId, Guid organizationId, CancellationToken ct)
    {
        // ISOLATION-BYPASS: the caller may be scoped to one school; an account is shared when its owner has a profile anywhere in the
        // same client, which is exactly what this explicit OrganizationId filter decides (never another client's).
        var inClient = await db.Set<StaffProfile>().IgnoreQueryFilters().AnyAsync(p => p.UserId == userId && p.OrganizationId == organizationId, ct)
            || await db.Set<StudentProfile>().IgnoreQueryFilters().AnyAsync(p => p.UserId == userId && p.OrganizationId == organizationId, ct)
            || await db.Set<GuardianProfile>().IgnoreQueryFilters().AnyAsync(p => p.UserId == userId && p.OrganizationId == organizationId, ct);
        return inClient;
    }

    private async Task<(string? ToEmail, string Delivered)> ResolveRecipientAsync(StudentProfile student, CancellationToken ct)
    {
        if (PersonNormalizer.Email(student.Email) is { } own)
        {
            return (own, CredentialsDelivery.Student);
        }

        var primary = await db.Set<StudentGuardian>().Where(l => l.StudentId == student.Id && l.IsPrimary)
            .Select(l => l.GuardianId).FirstOrDefaultAsync(ct);
        if (primary != Guid.Empty)
        {
            var email = await GuardianVisibility.Visible(db).Where(g => g.Id == primary).Select(g => g.Email).FirstOrDefaultAsync(ct);
            if (PersonNormalizer.Email(email) is { } guardianEmail)
            {
                return (guardianEmail, CredentialsDelivery.PrimaryGuardian);
            }
        }

        return (null, CredentialsDelivery.None);
    }

    /// <summary>Runs the configured password rules against the requested password before any account is created.</summary>
    private async Task ValidateAsync(string userName, string password)
    {
        var probe = new User { UserName = userName };
        var errors = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, probe, password);
            errors.AddRange(result.Errors.Select(e => e.Code));
        }

        if (errors.Count > 0)
        {
            throw new StudentCredentialsException(errors);
        }
    }

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new StudentCredentialsException(result.Errors.Select(e => e.Code).ToList());
        }
    }
}
