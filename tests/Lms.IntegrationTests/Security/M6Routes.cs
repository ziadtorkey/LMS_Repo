using System.Reflection;
using Lms.Shared;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Which API routes belong to M6 (communication, plan Task 31). The route table of the contract (<c>Routes.Messaging.cs</c>) is the oracle:
/// <see cref="ContractPatterns"/> reads every route constant from it, so a route added to the contract without a probe row is listed by
/// <see cref="M6EndpointMatrixTests"/> before the backend even maps it. Prefixes are listed here; a new M6 prefix is added here (a route
/// missing from every list is still caught by <c>M1EndpointMatrixTests</c>, which needs a probe row for every mapped route).
/// </summary>
public static class M6Routes
{
    /// <summary>Top-level prefixes owned by M6.</summary>
    private static readonly string[] Prefixes =
    [
        "/api/conversations", "/api/messages", "/api/messaging", "/api/moderation", "/api/comms", "/api/dev/comms",
        "/api/me/blocks", "/api/me/messaging", "/api/me/announcements", "/api/me/push-subscriptions", "/api/me/channels",
        "/api/me/notification-preferences/effective", "/api/me/capabilities", "/api/integrations/email",
    ];

    /// <summary>Anonymous by design: the email provider webhook (signature, timestamp window and rate limit protect it). Its rows are 401 bad, 401 stale, 413, 429, 200 once, duplicate no-op.</summary>
    public const string EmailWebhook = "/api/integrations/email/events";

    /// <summary>Mapped only in Development (the outbox peek of the E2E demo), 404 elsewhere.</summary>
    public const string DevOutbox = "/api/dev/comms/outbox";

    /// <summary>The routes that name a conversation: only a participant reaches them, every other caller gets 404 (L6).</summary>
    public static bool IsParticipantBound(string pattern) =>
        pattern.StartsWith("/api/conversations/", StringComparison.OrdinalIgnoreCase) && pattern.Contains("{id", StringComparison.OrdinalIgnoreCase);

    public static bool Is(string pattern) =>
        Prefixes.Any(p => pattern.Equals(p, StringComparison.OrdinalIgnoreCase) || pattern.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

    public static bool IsAnonymousByDesign(string pattern) => pattern.Equals(EmailWebhook, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every route constant of the contract that is an M6 route pattern (starts with <c>/api/</c>), with its constant name.</summary>
    public static IReadOnlyList<(string Name, string Pattern)> ContractPatterns() =>
        [.. typeof(Routes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.GetRawConstantValue() is string v && v.StartsWith("/api/", StringComparison.Ordinal) && Is(v))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!))];
}
