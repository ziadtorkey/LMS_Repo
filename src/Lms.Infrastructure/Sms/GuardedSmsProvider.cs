using Lms.Domain.Abstractions;

namespace Lms.Infrastructure.Sms;

/// <summary>
/// Wraps the chosen SMS provider (D72): a message to a minor, or about another person (a child), is refused with <see cref="SmsRefusal.NotAllowedForMinor"/> before the
/// provider is touched. The channel resolver normally picks email or push for those cases and sets the flags; this is the backstop. The adapter behind it stays generic,
/// so a real gateway (D73: in-Kingdom or GCC preferred) needs no rule of its own.
/// </summary>
public sealed class GuardedSmsProvider(ISmsProvider inner) : ISmsProvider
{
    public Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        if (message.RecipientIsMinor || message.ConcernsAnotherPerson)
        {
            throw new SmsRefusedException(SmsRefusal.NotAllowedForMinor);
        }

        return inner.SendAsync(message, cancellationToken);
    }
}
