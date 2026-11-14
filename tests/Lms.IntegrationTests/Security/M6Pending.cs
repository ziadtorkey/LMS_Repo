using System.Text.Json;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The pinned set of things the M6 probes wait for (QA1's review of #770, item 1). A probe that waits for a route, a table or a type must never report a
/// green it did not earn: it asks <see cref="SkipAsync"/> first. A key that is <b>pinned and not live</b> skips with a visible "PENDING" line; a key that is
/// <b>not pinned and not live</b> fails (a route landed under another verb or pattern, or a table was renamed, and nothing would have probed it); a key that is
/// <b>live and still pinned</b> fails in <see cref="M6PendingProbeTests"/> until the pin is removed, so the merge of #733, #747, #750, #759 and #760 forces the
/// probes to run. Remove a pin in the same PR that makes its key live.
/// </summary>
public static class M6Pending
{
    public const string CreateConversation = "ROUTE POST /api/conversations";
    public const string Recipients = "ROUTE GET /api/messaging/recipients";
    public const string ModerationList = "ROUTE GET /api/moderation/reports";
    public const string PushSubscribe = "ROUTE POST /api/me/push-subscriptions";
    public const string OutboxTable = "TABLE DeliveryOutbox";
    public const string OutboxWriter = "TYPE Lms.Infrastructure.Comms.DeliveryPlanner";
    public const string NoStoreHeader = "HEADER Cache-Control no-store on /api/me";

    /// <summary>Key to what unblocks it. Edit this list only to remove a key once its backend is on the branch.</summary>
    public static readonly IReadOnlyDictionary<string, string> Pins = new Dictionary<string, string>
    {
        [CreateConversation] = "#733 (messaging kernel)",
        [Recipients] = "#747 (recipient search and rules summary)",
        [ModerationList] = "#760 (moderation)",
        [PushSubscribe] = "#767 (push subscription endpoint allow-list)",
        [OutboxTable] = "#750 (delivery outbox)",
        [OutboxWriter] = "#750 (delivery outbox writer: the L9 refusal probe must be written)",
        [NoStoreHeader] = "#736 (Cache-Control private, no-store on every /api response)",
    };

    public static async Task<bool> IsLiveAsync(LmsApiFactory factory, string key)
    {
        var (kind, rest) = (key[..key.IndexOf(' ')], key[(key.IndexOf(' ') + 1)..]);
        switch (kind)
        {
            case "ROUTE":
                var parts = rest.Split(' ', 2);
                return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
                    .Where(e => string.Equals(e.RoutePattern.RawText, parts[1], StringComparison.OrdinalIgnoreCase))
                    .Any(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"]).Contains(parts[0], StringComparer.OrdinalIgnoreCase));
            case "TABLE":
                await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
                {
                    var tables = await db.Database.SqlQueryRaw<string>(
                        "SELECT TABLE_NAME AS Value FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = {0}", rest).ToListAsync();
                    return tables.Count > 0;
                }

            case "TYPE":
                return typeof(Lms.Infrastructure.Persistence.LmsDbContext).Assembly.GetType(rest) is not null;
            case "HEADER":
                var w = await M6Kit.WorldAsync(factory);
                using (var response = await w.SendAsync(M4aCaller.TeacherA, HttpMethod.Get, "/api/me"))
                {
                    return response.Headers.CacheControl is { NoStore: true, Private: true };
                }

            default:
                throw new ArgumentException("Unknown pin kind: " + key);
        }
    }

    /// <summary>
    /// True when the probe must stop here because its key is still pending (written to the test output as PENDING). Fails when the key is neither live nor
    /// pinned. Returns false when the probe must run (the key is live), whether or not it is still pinned (the pin test reports that).
    /// </summary>
    public static async Task<bool> SkipAsync(LmsApiFactory factory, ITestOutputHelper output, string key)
    {
        var live = await IsLiveAsync(factory, key);
        if (live)
        {
            return false;
        }

        Assert.True(Pins.ContainsKey(key), $"{key} is not live and is not pinned in M6Pending.Pins: nothing would probe it. Pin it with the PR that unblocks it, or fix the route/table name.");
        output.WriteLine($"PENDING (waits for {Pins[key]}): {key}");
        return true;
    }

    /// <summary>The <c>reason</c> of a problem body (an extension of the problem details), or null.</summary>
    public static string? ReasonOf(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return Find(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Find(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("reason") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }

                    if (Find(property.Value) is { } nested)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>A problem body without the parts that legitimately differ between two answers (trace id, instance).</summary>
    public static string Normalize(string body) =>
        System.Text.RegularExpressions.Regex.Replace(body, "\"(traceId|instance|requestId)\"\\s*:\\s*\"[^\"]*\"", "\"$1\":\"\"");
}
