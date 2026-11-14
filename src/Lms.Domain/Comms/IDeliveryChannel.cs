namespace Lms.Domain.Comms;

/// <summary>What a channel needs to send one row: ids and the notification's event data. The address is resolved by the dispatcher (<see cref="IContactResolver"/>) and given here.</summary>
public sealed record DeliveryRequest(
    Guid OutboxId, Guid OrganizationId, Guid UserId, string Address, string? EventKey, string? ParametersJson, string? DeepLink, string? TemplateKey);

/// <summary>One delivery channel (email now, push later). It never throws for a delivery problem; it answers with a typed <see cref="DeliveryOutcome"/>.</summary>
public interface IDeliveryChannel
{
    /// <summary>The channel it serves, a <c>NotificationChannels</c> value (not in-app).</summary>
    string Channel { get; }

    string ProviderName { get; }

    Task<DeliveryOutcome> SendAsync(DeliveryRequest request, CancellationToken cancellationToken);
}

/// <summary>Resolves delivery addresses per channel at send time; a user with none is missing from the answer (no row is written at publish; a row whose contact vanished is skipped).</summary>
public interface IContactResolver
{
    Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IReadOnlyCollection<Guid> userIds, string channel, CancellationToken cancellationToken);
}
