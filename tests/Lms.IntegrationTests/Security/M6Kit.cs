using System.Net.Http.Json;
using Lms.Domain.Configuration;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Shared setup of the M6 probes (plan Task 31). The world is the M4a probe world (<see cref="M4aCallers"/>: teachers A and B, two students of one
/// class course in different groups, a student of another class, the guardian of student 1, and the M1 admins of School1, School2 and client B), with
/// the <c>Messaging</c> flag on for client A. Conversations are made through the API, as a real user would, so a row works on whatever the backend
/// has merged. The callers the plan lists beyond these (guardian of two children, inactive link, former teacher, counselor, supervisor, manager,
/// Org Admin who is also a teacher, restricted, blocked) are added as their routes and data land.
/// </summary>
public static class M6Kit
{
    public static async Task<M4aCallers> WorldAsync(LmsApiFactory factory)
    {
        var w = await M4aCallers.GetAsync(factory);
        var tree = w.Callers.A;
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        if (!await db.Set<OrgUnitSetting>().AnyAsync(s => s.OrganizationId == tree.Org.Id && s.Key == FeatureFlags.Messaging.Name))
        {
            db.Add(new OrgUnitSetting(tree.Org.Id, tree.Root.Id, tree.Root.Path, FeatureFlags.Messaging.Name, "true", false));
            await db.SaveChangesAsync();
        }

        return w;
    }

    /// <summary>Whether the backend has mapped this route and method (rows and tests wait for it).</summary>
    public static bool IsMapped(LmsApiFactory factory, HttpMethod method, string pattern) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => string.Equals(e.RoutePattern.RawText, pattern, StringComparison.OrdinalIgnoreCase))
            .Any(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"]).Contains(method.Method, StringComparer.OrdinalIgnoreCase));

    /// <summary>A direct conversation of teacher A and student 1 (made by the teacher), with one message.</summary>
    public static Task<Guid> NewDirectAsync(M1ProbeWorld w) =>
        CreateAsync(w, ConversationKinds.Direct, null, M4aCaller.StudentGroup1);

    /// <summary>A group of teacher A (the owner) with both students of class course X, with one message.</summary>
    public static Task<Guid> NewGroupAsync(M1ProbeWorld w) =>
        CreateAsync(w, ConversationKinds.Group, "QA group", M4aCaller.StudentGroup1, M4aCaller.StudentGroup2);

    private static async Task<Guid> CreateAsync(M1ProbeWorld w, string kind, string? title, params M4aCaller[] others)
    {
        var world = await WorldAsync(w.Callers.Factory);
        var ids = others.Select(o => world.AssignmentOf(o).UserId).ToList();
        using var response = await world.SendAsync(M4aCaller.TeacherA, HttpMethod.Post, Routes.Conversations,
            new CreateConversationRequest(kind, ids, title, "hello"));
        Assert.True(response.IsSuccessStatusCode, $"M6Kit: creating the {kind} conversation failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<ConversationDto>())!.Id;
    }

    /// <summary>The first message of a conversation, read as teacher A.</summary>
    public static async Task<Guid> FirstMessageAsync(M1ProbeWorld w, Guid conversationId)
    {
        var world = await WorldAsync(w.Callers.Factory);
        using var response = await world.SendAsync(M4aCaller.TeacherA, HttpMethod.Get, Routes.ConversationMessages(conversationId));
        return (await response.Content.ReadFromJsonAsync<MessagePageDto>())!.Items.First().Id;
    }
}
