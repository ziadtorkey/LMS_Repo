using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Shared.Notifications;

namespace Lms.Api.Features.Enrollment.Exceptions;

/// <summary>Enrollment.ExceptionRequested / ExceptionDecided (FR-NOT-01). A notification failure never undoes the change.</summary>
internal sealed class ExceptionNotifier(INotificationPublisher publisher, ILogger<ExceptionNotifier> logger)
{
    /// <summary>To the course supervisors; a course without any goes to the Managers at and above the class (D40).</summary>
    public Task RequestedAsync(EnrollmentException request, StudentProfile student, Course course, IReadOnlyList<Guid> supervisors, CancellationToken ct) =>
        SendAsync(new NotificationRequest(
            NotificationEvents.EnrollmentExceptionRequested, request.OrganizationId, supervisors, Names(student, course),
            OrgUnitPath: request.OrgUnitPath), ct);

    public Task DecidedAsync(EnrollmentException request, StudentProfile student, Course course, CancellationToken ct) =>
        SendAsync(new NotificationRequest(
            NotificationEvents.EnrollmentExceptionDecided, request.OrganizationId, [request.RequestedBy],
            new Dictionary<string, string>(Names(student, course)) { ["Decision"] = request.Status }), ct);

    private static Dictionary<string, string> Names(StudentProfile s, Course c) => new()
    {
        ["StudentNameAr"] = s.NameAr, ["StudentNameEn"] = s.NameEn, ["CourseNameAr"] = c.NameAr, ["CourseNameEn"] = c.NameEn,
    };

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
