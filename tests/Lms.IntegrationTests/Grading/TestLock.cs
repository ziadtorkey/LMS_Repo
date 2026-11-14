using Lms.Domain.Grading;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Grading;

/// <summary>The lock seam only (M5 L5): M7 adds the lock action, approvals and the UI. Tests lock a gradebook directly.</summary>
internal static class TestLock
{
    public static async Task LockAsync(LmsApiFactory factory, Guid gradebookId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        (await db.Set<Gradebook>().SingleAsync(g => g.Id == gradebookId)).Lock(DateTime.UtcNow);
        await db.SaveChangesAsync();
    }
}
