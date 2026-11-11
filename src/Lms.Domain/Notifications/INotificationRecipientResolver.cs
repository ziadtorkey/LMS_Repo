namespace Lms.Domain.Notifications;

public interface INotificationRecipientResolver
{
    /// <summary>
    /// Active users with a currently valid assignment of one of the event's default recipient roles at the unit or any
    /// unit below it, in that client.
    /// </summary>
    Task<IReadOnlyList<Guid>> ResolveAsync(
        string eventKey, Guid organizationId, string orgUnitPath, CancellationToken cancellationToken = default);
}
