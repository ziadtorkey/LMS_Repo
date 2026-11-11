using Lms.Api.Features.Assignments;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.People;

namespace Lms.Api.Features.Staff;

/// <summary>PUT /api/staff/{id}/status: deactivate or reactivate with a reason (FR-STF-07, X-03); the history row is written by the service.</summary>
public sealed class StaffStatusHandler(
    LmsDbContext db, ICurrentContext context, IClock clock, IPersonStatusService statuses, StaffQueries queries,
    Lms.Infrastructure.Access.CallerPermissions caller)
{
    private const int MaxReason = 500;

    public async Task<IResult> SetAsync(Guid id, SetPersonStatusRequest? request, HttpContext http, CancellationToken ct)
    {
        var reason = request?.Reason?.Trim() ?? string.Empty;
        if (request is null || (request.Status != PersonStatuses.Active && request.Status != PersonStatuses.Deactivated))
        {
            return AssignmentQueries.Invalid(nameof(SetPersonStatusRequest.Status), "invalid_value");
        }

        if (reason.Length is 0 or > MaxReason)
        {
            return AssignmentQueries.Invalid(nameof(SetPersonStatusRequest.Reason), reason.Length == 0 ? "required" : "too_long");
        }

        var (profile, problem) = await StaffAccess.FindForUpdateAsync(db, context, http, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        // Nobody changes their own status: another admin must do it (no self lock-out).
        if (profile!.UserId == context.UserId)
        {
            return ApiProblems.Conflict(ProblemCodes.cannot_change_own_status);
        }

        var to = Enum.Parse<PersonStatus>(request.Status);

        // Changing the status switches the person's access on or off (D16): the caller must be allowed to manage every role
        // the person holds, whichever direction the change goes.
        if (await StaffAssignments.CheckCallerMayRemoveAccessAsync(db, caller, profile, ct) is { } refused)
        {
            return refused;
        }

        if (to == PersonStatus.Active && profile.Status == PersonStatus.Expired
            && profile.ExpiresOn is { } expiry && expiry < DateOnly.FromDateTime(clock.UtcNow))
        {
            // The expiry date has to be extended (edit the profile) before the person can be reactivated.
            return ApiProblems.BadRequest(ProblemCodes.invalid_expiry);
        }

        if (profile.Status != to)
        {
            await statuses.ChangeAsync(PersonKind.Staff, profile.Id, to, reason, ct);
        }

        return await queries.GetAsync(profile.Id, ct);
    }
}
