using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.LiveSessions;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Security;

/// <summary>The callers of the M4b probes (plan Task 20): the people the route table names.</summary>
public enum M4bCaller
{
    /// <summary>Teacher of the class course.</summary>
    Teacher,

    /// <summary>Teacher of another class course of the same course: no relationship to this one.</summary>
    OtherTeacher,

    /// <summary>Student enrolled in the class course and in group 1.</summary>
    StudentGroup1,

    /// <summary>Student enrolled in the class course and in group 2 (not group 1).</summary>
    StudentGroup2,

    /// <summary>A student of School1 who is not enrolled in the class course.</summary>
    StudentStranger,

    /// <summary>A guardian (no live-session or package permission).</summary>
    Guardian,

    /// <summary>Org Admin of the client at its root: unit scope, no relationship needed.</summary>
    Admin,

    /// <summary>Org Admin of another client.</summary>
    ClientBAdmin,
}

/// <summary>
/// The M4b probe world: the <see cref="ContentTestHost"/> world (class course, its teacher, a teacher of another class course, an enrolled student, a stranger
/// student, an admin) plus a second enrolled student, two groups of the class course, a guardian and the admin of another client, with the
/// <c>VirtualClasses</c> flag on. Built per test class run because a test host is disposed with it (the leaked-host guard). Live sessions are made through
/// the API as the teacher, like a user would.
/// </summary>
internal sealed class M4bWorld : IDisposable
{
    private readonly ContentTestHost _host;
    private readonly Dictionary<M4bCaller, RoleAssignment> _callers = [];

    private M4bWorld(ContentTestHost host, ContentWorld content, Lms.Domain.Enrollment.StudentGroup group1, Lms.Domain.Enrollment.StudentGroup group2)
    {
        _host = host;
        Content = content;
        Group1 = group1;
        Group2 = group2;
    }

    public ContentWorld Content { get; }

    public Lms.Domain.Enrollment.StudentGroup Group1 { get; }

    public Lms.Domain.Enrollment.StudentGroup Group2 { get; }

    public ContentTestHost Host => _host;

    public void Dispose() => _host.Dispose();

    public RoleAssignment Of(M4bCaller caller) => _callers[caller];

    public Task<HttpResponseMessage> SendAsync(M4bCaller caller, HttpMethod method, string url, object? body = null) =>
        _host.SendAsync(_callers[caller], method, url, body);

    public static async Task<M4bWorld> CreateAsync(LmsApiFactory factory, string name, Action<IServiceCollection>? configure = null)
    {
        var host = new ContentTestHost(factory, configure);
        try
        {
            var w = await host.SetUpAsync(name);
            await LiveSessionTestKit.EnableAsync(factory, w);
            var (student2, profile2) = await host.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
            var group1 = await host.AddGroupAsync(w.ClassCourse, w.StudentProfileId);
            var group2 = await host.AddGroupAsync(w.ClassCourse, profile2);
            var guardian = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Guardian, (await PeopleTestKit.AddUserAsync(factory)).Id);
            var treeB = await AccessTestData.SeedAsync(factory, name + "B");
            var adminB = await AccessTestData.AssignAsync(factory, treeB, treeB.Root, BuiltInRoles.OrgAdmin, (await PeopleTestKit.AddUserAsync(factory)).Id);

            var world = new M4bWorld(host, w, group1, group2);
            world._callers[M4bCaller.Teacher] = w.Teacher;
            world._callers[M4bCaller.OtherTeacher] = w.OtherTeacher;
            world._callers[M4bCaller.StudentGroup1] = w.Student;
            world._callers[M4bCaller.StudentGroup2] = student2;
            world._callers[M4bCaller.StudentStranger] = w.Stranger;
            world._callers[M4bCaller.Guardian] = guardian;
            world._callers[M4bCaller.Admin] = w.Admin;
            world._callers[M4bCaller.ClientBAdmin] = adminB;
            return world;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>A session of the class course visible to group 1 only (its module is targeted at the group), made as the teacher.</summary>
    public Task<Guid> NewGroup1SessionAsync() => NewSessionAsync(group: Group1.Id);

    /// <summary>A session visible to the whole class course.</summary>
    public Task<Guid> NewClassSessionAsync() => NewSessionAsync(group: null);

    private async Task<Guid> NewSessionAsync(Guid? group)
    {
        var module = new ModuleSettingsInput("حصة", "Live " + Guid.NewGuid().ToString("N")[..6], ModuleVisibilities.Published, null, null,
            group is null ? ModuleTargets.ClassCourse : ModuleTargets.Group, group, CompletionRules.None, null);
        var request = new CreateLiveSessionRequest(ContentTestHost.Lesson1, module, LiveSessionTestKit.NextSunday(), 60);
        using var response = await SendAsync(M4bCaller.Teacher, HttpMethod.Post, Routes.ClassCourseLiveSessions(Content.ClassCourse.Id), request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"M4bWorld: creating a session failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<List<LiveSessionDto>>())!.Single().Id;
    }
}
