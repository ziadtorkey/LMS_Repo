using Lms.Shared;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;

namespace Lms.Infrastructure.Comms.Preferences;

/// <summary>
/// What decides one cell (event x channel) of a user's notification preferences. <c>UserChoice</c> is the stored preference (null = none).
/// <c>HasContact</c> is false when the user has no usable address for the channel (none stored, or the address is suppressed); in-app needs none.
/// </summary>
public sealed record PreferenceInputs(
    NotificationEventDefinition Event, string Channel, bool? UserChoice, bool ClientChannelEnabled, bool HasContact);

/// <summary>
/// The one effective-preference function (M6 plan Task 9, L10): event default AND user choice AND client switch AND a usable contact; a mandatory event
/// is on whatever the user chose. A user can only narrow: a channel the event does not offer by default cannot be switched on. The client switch and a
/// missing contact win over everything (nothing can be sent), and then say why through <c>DisabledReason</c> (<see cref="DeliverySkipReasons"/>).
/// In-app is always on (<c>in_app_required</c>). The same function answers the preferences page and the outbox writer, so they cannot disagree.
/// </summary>
public static class EffectivePreferences
{
    private static readonly IReadOnlyList<string> InAppOnly = [NotificationChannels.InApp];

    public static IReadOnlyList<string> DefaultChannelsOf(NotificationEventDefinition definition) => definition.DefaultChannels ?? InAppOnly;

    public static EffectivePreferenceDto Resolve(PreferenceInputs input)
    {
        var (definition, channel) = (input.Event, input.Channel);
        var offered = DefaultChannelsOf(definition).Contains(channel, StringComparer.Ordinal);

        if (channel == NotificationChannels.InApp)
        {
            return new EffectivePreferenceDto(definition.Key, channel, true, definition.Mandatory ? PreferenceSources.Mandatory : PreferenceSources.Default, definition.Mandatory);
        }

        if (!input.ClientChannelEnabled)
        {
            return Off(input, DeliverySkipReasons.ChannelOff);
        }

        if (!input.HasContact)
        {
            return Off(input, DeliverySkipReasons.NoContact);
        }

        if (!offered)
        {
            return new EffectivePreferenceDto(definition.Key, channel, false, PreferenceSources.Default, definition.Mandatory);
        }

        if (definition.Mandatory)
        {
            return new EffectivePreferenceDto(definition.Key, channel, true, PreferenceSources.Mandatory, true);
        }

        return new EffectivePreferenceDto(
            definition.Key, channel, input.UserChoice ?? true, input.UserChoice.HasValue ? PreferenceSources.User : PreferenceSources.Default, false);
    }

    private static EffectivePreferenceDto Off(PreferenceInputs input, string reason) =>
        new(input.Event.Key, input.Channel, false,
            input.Event.Mandatory && EffectivePreferences.DefaultChannelsOf(input.Event).Contains(input.Channel) ? PreferenceSources.Mandatory : PreferenceSources.Default,
            input.Event.Mandatory, reason);

    /// <summary>
    /// The problem code of a change a user asked for, or null when it is allowed: in-app cannot be switched off (<c>in_app_required</c>), a mandatory
    /// event cannot be switched off on a channel it offers (<c>notification_mandatory</c>), a channel the client switched off cannot be switched on
    /// (<c>channel_not_allowed</c>), and an unknown event or channel is a validation error.
    /// </summary>
    public static string? CheckChange(NotificationEventDefinition? definition, string channel, bool enabled, bool clientChannelEnabled)
    {
        if (definition is null || !NotificationChannels.All.Contains(channel, StringComparer.Ordinal))
        {
            return ProblemCodes.validation_failed;
        }

        if (channel == NotificationChannels.InApp)
        {
            return enabled ? null : ProblemCodes.in_app_required;
        }

        if (!enabled)
        {
            return definition.Mandatory && DefaultChannelsOf(definition).Contains(channel, StringComparer.Ordinal) ? ProblemCodes.notification_mandatory : null;
        }

        return clientChannelEnabled ? null : ProblemCodes.channel_not_allowed;
    }
}
