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
            // An email or sign-in name that belongs to another account is never reused (D47 decision): setting or resetting a password on
            // someone else's account, even inside the same client, would let one school's admin take over e.g. the Org Admin.
            var emailOwner = PersonNormalizer.Email(student.Email) is { } email ? await users.FindByEmailAsync(email) : null;
            if (emailOwner is not null)
            {
                throw new DuplicateAccountException("Email");
            }

            if (await users.FindByNameAsync(userName) is not null)
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
        // RemovePassword/AddPassword rotated the security stamp: sessions that used the old password end (pinned by a test).

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
