using Lms.Domain.Abstractions;
using Lms.Domain.Identity;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.People;

public sealed class PersonStatusService(LmsDbContext db, ICurrentContext current, IClock clock) : IPersonStatusService
{
    public async Task ChangeAsync(
        PersonKind kind, Guid profileId, PersonStatus to, string reason, CancellationToken cancellationToken)
    {
        // Inside the caller's scope: a profile the caller cannot see cannot be changed.
        Guid? userId;
        PersonStatusChange change;
        switch (kind)
        {
            case PersonKind.Staff:
                var staff = await db.Set<StaffProfile>().FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken)
                    ?? throw new KeyNotFoundException("Staff profile not found.");
                change = PersonStatusChange.For(staff, staff.Status, to, reason, current.UserId, clock.UtcNow);
                staff.SetStatus(to);
                userId = staff.UserId;
                break;
            case PersonKind.Student:
                var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken)
                    ?? throw new KeyNotFoundException("Student profile not found.");
                change = PersonStatusChange.For(student, student.Status, to, reason, current.UserId, clock.UtcNow);
                student.SetStatus(to);
                userId = student.UserId;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only staff and student profiles have a status.");
        }

        db.Set<PersonStatusChange>().Add(change);
        await db.SaveChangesAsync(cancellationToken);

        if (userId is { } id)
        {
            await RecomputeUserStatusAsync(id, cancellationToken);
        }
    }

    public async Task RecomputeUserStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.IsPlatformAdmin || user.Status == UserStatus.Withdrawn)
        {
            return;
        }

        // ISOLATION-BYPASS: a user's account status depends on their profiles in every client, not only the caller's.
        // Only the statuses of this one user's live profiles are read; nothing of them leaves this method.
        var staff = await db.Set<StaffProfile>().IgnoreQueryFilters()
            .Where(p => p.UserId == userId && !p.IsDeleted).Select(p => p.Status).ToListAsync(cancellationToken);
        // ISOLATION-BYPASS: same reason as above (student profiles of this user in every client).
        var students = await db.Set<StudentProfile>().IgnoreQueryFilters()
            .Where(p => p.UserId == userId && !p.IsDeleted).Select(p => p.Status).ToListAsync(cancellationToken);
        // ISOLATION-BYPASS: same reason as above (guardian profiles have no status: any of them keeps the account active).
        var hasGuardian = await db.Set<GuardianProfile>().IgnoreQueryFilters()
            .AnyAsync(p => p.UserId == userId && !p.IsDeleted, cancellationToken);

        var statuses = staff.Concat(students).ToList();
        var next = hasGuardian || statuses.Count == 0 || statuses.Contains(PersonStatus.Active)
            ? UserStatus.Active
            : statuses.All(s => s == PersonStatus.Expired) ? UserStatus.Expired : UserStatus.Deactivated;

        if (user.Status != next)
        {
            user.Status = next;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
