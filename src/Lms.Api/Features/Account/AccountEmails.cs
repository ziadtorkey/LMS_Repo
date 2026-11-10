using System.Globalization;
using System.Net;
using Lms.Api.Resources;
using Lms.Domain.Abstractions;
using Lms.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lms.Api.Features.Account;

public sealed class AccountEmails(
    UserManager<User> users,
    IEmailSender sender,
    IStringLocalizer<AccountResource> localizer,
    IOptions<AccountOptions> options) : IAccountEmails
{
    public async Task SendPasswordResetAsync(User user, string culture, CancellationToken cancellationToken = default)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await SendAsync(user, culture, token, invite: false, "ResetEmailSubject", "ResetEmailBody", cancellationToken);
    }

    public async Task SendInvitationAsync(User user, string culture, CancellationToken cancellationToken = default)
    {
        var token = await users.GenerateUserTokenAsync(user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose);
        await SendAsync(user, culture, token, invite: true, "InviteEmailSubject", "InviteEmailBody", cancellationToken);
    }

    private async Task SendAsync(
        User user, string culture, string token, bool invite, string subjectKey, string bodyKey, CancellationToken cancellationToken)
    {
        // The link opens the page in the language of the email.
        var lang = culture.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar";
        var link = $"{options.Value.PublicBaseUrl}{AccountModule.ResetPasswordPath}?userId={user.Id}" +
                   $"&token={Uri.EscapeDataString(token)}{(invite ? "&invite=1" : string.Empty)}&culture={lang}&ui-culture={lang}";

        // Texts come from the resources in the language of the email, whatever the culture of the current request.
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(lang);
        try
        {
            var subject = localizer[subjectKey].Value;
            var body = localizer[bodyKey].Value;
            var html = $"<!DOCTYPE html><html lang=\"{lang}\" dir=\"{(lang == "ar" ? "rtl" : "ltr")}\"><body>" +
                       $"<p>{WebUtility.HtmlEncode(body)}</p>" +
                       $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(link)}</a></p></body></html>";
            await sender.SendAsync(new EmailMessage(user.Email!, subject, html), cancellationToken);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
