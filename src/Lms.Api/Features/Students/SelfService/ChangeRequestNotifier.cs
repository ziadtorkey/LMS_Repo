using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Shared.Notifications;

namespace Lms.Api.Features.Students.SelfService;

/// <summary>Students.ChangeRequested / ChangeRequestDecided (FR-NOT-01). A notification failure never undoes the change.</summary>
internal sealed class ChangeRequestNotifier(INotificationPublisher publisher, ILogger<ChangeRequestNotifier> logger)
{
    public Task RequestedAsync(StudentProfile student, CancellationToken ct) =>
        SendAsync(new NotificationRequest(
            NotificationEvents.StudentsChangeRequested, student.OrganizationId, [], Names(student), OrgUnitPath: student.OrgUnitPath), ct);

    public Task DecidedAsync(StudentProfile student, StudentChangeRequest request, CancellationToken ct) =>
        SendAsync(new NotificationRequest(
            NotificationEvents.StudentsChangeRequestDecided, student.OrganizationId, [request.RequestedBy],
            new Dictionary<string, string>(Names(student)) { ["Decision"] = request.Status.ToString() }), ct);

    private static Dictionary<string, string> Names(StudentProfile s) =>
        new() { ["StudentNameAr"] = s.NameAr, ["StudentNameEn"] = s.NameEn };

    private async Task SendAsync(NotificationRequest request, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Notification {Event} could not be published", request.EventKey);
        }
    }
}
