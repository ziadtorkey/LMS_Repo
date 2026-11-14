using System.Net;
using Lms.Api.Infrastructure;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// M6 permission probes (M6 plan Task 31, PRD §4.2.3, FR-SEC-05, X-05). The M6 rows live next to the route tables they test (<c>Security/ProbeRows</c>,
/// milestone <see cref="ProbeMilestones.M6"/>) and <see cref="M1EndpointMatrixTests"/> sends them with the seven M1 callers. This class adds what only
/// M6 needs:
/// <list type="bullet">
/// <item>the contract route table is the oracle: every conversation route of <c>Routes.Messaging.cs</c> has a probe row (or is listed with the reason it
/// has none yet), whether or not the backend has mapped it;</item>
/// <item>every mapped M6 route has a probe row and declares a permission (the email webhook excepted), and its rows carry the M6 milestone;</item>
/// <item>a row on a conversation route expects 404 for every caller of the M1 world and 401 for anonymous, never a 2xx: the participant gate (L6);</item>
/// <item>a caller whose built-in role lacks the route's permission is refused with 403 (derived from <c>RequirePermission</c> and the role defaults, so a
/// new route is covered without editing this file).</item>
/// </list>
/// What a participant, a guardian, a blocked user or a restricted user gets is in <see cref="M6ParticipantIsolationTests"/> and
/// <see cref="M6SafeguardingMatrixTests"/>. Rows go live by themselves when their route is mapped.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M6EndpointMatrixTests(LmsApiFactory factory, ITestOutputHelper output)
{
    /// <summary>Contract routes with no probe row yet, with the reason. Remove an entry when its row is written; the test below then enforces it.</summary>
    private static readonly Dictionary<string, string> NoRowYet = new(StringComparer.OrdinalIgnoreCase)
    {
        [Routes.ConversationAttachmentsPattern] = "multipart upload: the row needs the file part of the asset probes (follow-up of Task 31, with the attachment slice of Task 10)",
        [Routes.ConversationAttachmentPattern] = "needs a pending upload to delete (same slice)",
        [Routes.MessageAttachmentUrlPattern] = "needs a message with an attachment (same slice)",
    };

    /// <summary>M6 routes that are deliberately not probed by the matrix, with the reason.</summary>
    private static readonly Dictionary<string, string> NotProbed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GET " + M6Routes.DevOutbox] = "Development-only outbox peek of the E2E demo: mapped only in Development, 404 elsewhere",
    };

    private static IEnumerable<ProbeRow> M6Rows => M1ProbeRows.All.Where(r => M6Routes.Is(r.Pattern));

    private static IEnumerable<ProbeRow> PermissionRows => M6Rows.Where(r => !M6Routes.IsAnonymousByDesign(r.Pattern));

    private static string RoleOf(M4aCaller caller) => caller switch
    {
        M4aCaller.TeacherA or M4aCaller.TeacherB => BuiltInRoles.Teacher,
        M4aCaller.Guardian => BuiltInRoles.Guardian,
        _ => BuiltInRoles.Student,
    };

    public static TheoryData<string, M4aCaller> Cases()
    {
        var data = new TheoryData<string, M4aCaller>();
        foreach (var row in PermissionRows)
        {
            foreach (var caller in Enum.GetValues<M4aCaller>())
            {
                data.Add(row.Name, caller);
            }
        }

        if (data.Count == 0)
        {
            data.Add(string.Empty, M4aCaller.StudentGroup1);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Caller_is_refused_with_403_where_the_role_lacks_the_permission(string rowName, M4aCaller caller)
    {
        if (rowName.Length == 0)
        {
            output.WriteLine("No M6 row yet.");
            return;
        }

        var row = M1ProbeRows.All.Single(r => r.Name == rowName);
        if (!IsMapped(row))
        {
            output.WriteLine($"PENDING (route not mapped yet): {row.Method} {row.Pattern}");
            return;
        }

        var permission = PermissionOf(row);
        Assert.True(permission is not null, $"{row.Method} {row.Pattern} declares no RequirePermission.");
        if (BuiltInRoles.Get(RoleOf(caller)).DefaultPermissions.Contains(permission))
        {
            output.WriteLine($"{caller} holds {permission}: answered by the participant probes, not asserted here.");
            return;
        }

        var w = await M6Kit.WorldAsync(factory);
        var request = await row.Build(w.World, Caller.RootAdmin);
        using var response = await w.SendAsync(caller, row.Method, request.Url, request.Body);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"{row.Name}: {row.Method} {request.Url} as {caller} ({RoleOf(caller)} lacks {permission}) → expected 403, got {(int)response.StatusCode}. Body: {Truncate(body)}");
    }

    /// <summary>The route table of the contract is the oracle: a conversation route added to <c>Routes.Messaging.cs</c> needs a row before it can be mapped.</summary>
    [Fact]
    public void Every_conversation_route_of_the_contract_has_a_probe_row()
    {
        var rows = M6Rows.Select(r => r.Pattern.ToUpperInvariant()).ToHashSet();
        var missing = M6Routes.ContractPatterns()
            .Where(c => M6Routes.IsParticipantBound(c.Pattern) && !rows.Contains(c.Pattern.ToUpperInvariant()) && !NoRowYet.ContainsKey(c.Pattern))
            .Select(c => $"{c.Name} = {c.Pattern}").ToList();
        Assert.True(missing.Count == 0, "Conversation routes of the contract without a probe row (add one in Security/ProbeRows/M6ConversationsProbeRows.cs):\n" + string.Join('\n', missing));
    }

    [Fact]
    public void Contract_routes_without_a_probe_row_are_listed()
    {
        // Informational: the user-level, announcement, moderation and comms rows come with their backend merges (#747, #759, #760 and the next ones).
        var rows = M6Rows.Select(r => r.Pattern.ToUpperInvariant()).ToHashSet();
        var open = M6Routes.ContractPatterns().Where(c => !rows.Contains(c.Pattern.ToUpperInvariant())).Select(c => $"{c.Name} = {c.Pattern}").ToList();
        output.WriteLine($"{open.Count} contract route(s) without a row yet:\n" + string.Join('\n', open));
    }

    [Fact]
    public void Every_M6_api_route_is_in_the_probe_matrix()
    {
        var rows = M1ProbeRows.All.Select(r => $"{r.Method.Method} {r.Pattern}".ToUpperInvariant()).ToHashSet();
        var missing = MappedM6Routes()
            .Where(r => !rows.Contains($"{r.Method} {r.Pattern}".ToUpperInvariant()) && !NotProbed.ContainsKey($"{r.Method} {r.Pattern}"))
            .Select(r => $"{r.Method} {r.Pattern}").Distinct().ToList();
        Assert.True(missing.Count == 0,
            "M6 routes without a probe row (add one in Security/ProbeRows, milestone ProbeMilestones.M6; this includes both attachment-upload rows and the hub row):\n" + string.Join('\n', missing));
    }

    [Fact]
    public void Every_M6_api_route_requires_a_permission()
    {
        var open = MappedM6Routes()
            .Where(r => r.Permission is null && !M6Routes.IsAnonymousByDesign(r.Pattern) && !NotProbed.ContainsKey($"{r.Method} {r.Pattern}"))
            .Select(r => $"{r.Method} {r.Pattern}").Distinct().ToList();
        Assert.True(open.Count == 0,
            "M6 routes without RequirePermission (D42: explicit permissions; a route open to every signed-in user is listed in the plan, for example GET /api/me/capabilities, and is added to the exception list here with the reason):\n" + string.Join('\n', open));
    }

    [Fact]
    public void Every_M6_probe_row_declares_the_M6_milestone()
    {
        var wrong = M6Rows.Where(r => r.Milestone != ProbeMilestones.M6).Select(r => $"{r.Method} {r.Pattern} ({r.Name}) = {r.Milestone}").ToList();
        Assert.True(wrong.Count == 0, "Rows of M6 routes must set Milestone = ProbeMilestones.M6:\n" + string.Join('\n', wrong));
    }

    /// <summary>
    /// The participant gate as a guard, not a convention: a row on a route that names a conversation must expect 404 for every non-participant caller
    /// of the M1 world (an Org Admin and the platform admin included) and 401 for anonymous, never a 2xx and never a 403 (a 403 would say the
    /// conversation exists). A feature PR that declares success for them fails here before the row is even sent.
    /// </summary>
    [Fact]
    public void Conversation_rows_expect_404_for_every_non_participant_and_401_for_anonymous()
    {
        var wrong = new List<string>();
        foreach (var row in M6Rows.Where(r => M6Routes.IsParticipantBound(r.Pattern)))
        {
            foreach (var caller in Enum.GetValues<Caller>().Where(c => c != Caller.Anonymous))
            {
                if (!row.Expected.TryGetValue(caller, out var status) || status != 404)
                {
                    wrong.Add($"{row.Method} {row.Pattern} ({row.Name}): {caller} expects {(row.Expected.TryGetValue(caller, out var s) ? s.ToString() : "nothing")}, must be 404");
                }
            }

            if (!row.Expected.TryGetValue(Caller.Anonymous, out var anonymous) || anonymous != 401)
            {
                wrong.Add($"{row.Method} {row.Pattern} ({row.Name}): Anonymous must expect 401");
            }
        }

        Assert.True(wrong.Count == 0, "M6 rows on conversation routes must hide the conversation from every non-participant:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void Every_M6_row_name_is_unique()
    {
        var duplicates = M6Rows.GroupBy(r => r.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate probe row names: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void Pending_M6_rows_are_listed()
    {
        var pending = M6Rows.Where(r => !IsMapped(r)).Select(r => $"{r.Method} {r.Pattern} ({r.Name})").ToList();
        output.WriteLine(pending.Count == 0 ? "No pending M6 rows." : "Pending M6 rows:\n" + string.Join('\n', pending));
    }

    [Theory]
    [InlineData("/api/conversations", true)]
    [InlineData("/api/conversations/{id:guid}/messages", true)]
    [InlineData("/api/messages/search", true)]
    [InlineData("/api/messages/{id:guid}/report", true)]
    [InlineData("/api/messaging/recipients", true)]
    [InlineData("/api/moderation/reports", true)]
    [InlineData("/api/comms/announcements/{id:guid}/publish", true)]
    [InlineData("/api/me/blocks/{userId:guid}", true)]
    [InlineData("/api/me/announcements", true)]
    [InlineData("/api/me/capabilities", true)]
    [InlineData("/api/integrations/email/events", true)]
    [InlineData("/api/me/notifications", false)] // M4a notifications
    [InlineData("/api/me/notification-preferences", false)] // M4a preferences; only /effective is M6
    [InlineData("/api/me", false)]
    [InlineData("/api/conversationsx", false)]
    public void Only_m6_routes_are_in_scope(string pattern, bool m6) => Assert.Equal(m6, M6Routes.Is(pattern));

    [Fact]
    public void Participant_bound_means_a_conversation_id_in_the_path()
    {
        Assert.True(M6Routes.IsParticipantBound("/api/conversations/{id:guid}"));
        Assert.True(M6Routes.IsParticipantBound("/api/conversations/{id:guid}/messages/{mid:guid}"));
        Assert.False(M6Routes.IsParticipantBound("/api/conversations"));
        Assert.False(M6Routes.IsParticipantBound("/api/messages/{id:guid}/report"));
    }

    private IEnumerable<(string Method, string Pattern, string? Permission)> MappedM6Routes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && M6Routes.Is(raw))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(m => (m, e.RoutePattern.RawText!, e.Metadata.GetMetadata<RequiresPermissionMetadata>()?.Permission)));

    private string? PermissionOf(ProbeRow row) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => string.Equals(e.RoutePattern.RawText, row.Pattern, StringComparison.OrdinalIgnoreCase)
                        && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                        .Contains(row.Method.Method, StringComparer.OrdinalIgnoreCase))
            .Select(e => e.Metadata.GetMetadata<RequiresPermissionMetadata>()?.Permission)
            .FirstOrDefault();

    private bool IsMapped(ProbeRow row) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => string.Equals(e.RoutePattern.RawText, row.Pattern, StringComparison.OrdinalIgnoreCase))
            .Any(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Contains(row.Method.Method, StringComparer.OrdinalIgnoreCase));

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
