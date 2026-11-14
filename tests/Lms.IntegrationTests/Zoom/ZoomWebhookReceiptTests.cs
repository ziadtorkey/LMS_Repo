using Lms.Domain.LiveSessions;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Zoom;

[Collection(ApiCollection.Name)]
public sealed class ZoomWebhookReceiptTests(LmsApiFactory factory)
{
    [Fact]
    public async Task A_second_receipt_for_the_same_event_id_is_refused_by_the_database()
    {
        var eventId = "evt-" + Guid.NewGuid().ToString("N");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Set<ZoomWebhookReceipt>().Add(new ZoomWebhookReceipt(eventId, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        await using var again = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        again.Set<ZoomWebhookReceipt>().Add(new ZoomWebhookReceipt(eventId, DateTime.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync());
    }

    [Fact]
    public async Task Receipts_are_a_platform_table_with_no_client_columns()
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var columns = db.Model.FindEntityType(typeof(ZoomWebhookReceipt))!.GetProperties().Select(p => p.Name).Order().ToList();

        Assert.Equal(["EventId", "Id", "ReceivedAtUtc"], columns);
    }
}
