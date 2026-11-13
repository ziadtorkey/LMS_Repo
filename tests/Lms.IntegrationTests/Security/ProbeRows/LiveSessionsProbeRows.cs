using Lms.Domain.Classes;
using Lms.Domain.Organizations;
using Lms.Domain.Configuration;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Classes;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Blueprints;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Live sessions (M4b Task 1 contract, route table "Live sessions"). Rows that need no live session exist yet: the class course
/// list and create, <c>upcoming</c> and <c>provider-status</c>. The routes of one session (get, update, delete, join, recording,
/// participants) get their rows, with a real session in each unit, when the session store lands (Task 4); the derived 403 checks of
/// <see cref="M4bEndpointMatrixTests"/> already cover them by their permission.
/// <list type="bullet">
/// <item>Everything of a class course hangs off a class course at School1: School 2 and the other client get 404 (an invisible unit);
/// a Teacher without an assignment to it gets 404 (relationship).</item>
/// <item>Create sends an invalid recurrence on purpose: an authorised caller is answered 400 (<c>recurrence_invalid</c>) without
/// reaching the meeting provider, so the rows depend on no provider and create nothing. The 404 of an invisible class course comes
/// before validation, as everywhere (the same assumption as the M4a reorder row).</item>
/// <item><c>upcoming</c> is filtered by relationship and unit scope and never 404; <c>provider-status</c> has no relationship check.</item>
/// </list>
/// </summary>
public sealed class LiveSessionsProbeRows : IProbeRowModule
{
    private static readonly Guid Section = Guid.NewGuid(), Lesson = Guid.NewGuid();

    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M4b });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("live sessions of a class course list", HttpMethod.Get, Routes.ClassCourseLiveSessionsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseLiveSessions((await NewClassCourseAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("live session create (invalid recurrence)", HttpMethod.Post, Routes.ClassCourseLiveSessionsPattern,
            async (w, _) =>
            {
                await EnableVirtualClassesAsync(w);
                return new ProbeRequest(Routes.ClassCourseLiveSessions((await NewClassCourseAsync(w)).Id), InvalidCreateBody());
            },
            Expect(BadRequest, BadRequest, NotFound, NotFound, NotFound, BadRequest)),
        new("live sessions upcoming", HttpMethod.Get, Routes.LiveSessionsUpcoming,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.LiveSessionsUpcoming)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
        new("live sessions provider status", HttpMethod.Get, Routes.LiveSessionsProviderStatus,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.LiveSessionsProviderStatus)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
    ];

    /// <summary>A weekly series with neither an end date nor a count: <c>recurrence_invalid</c> (the contract's rule).</summary>
    private static CreateLiveSessionRequest InvalidCreateBody() =>
        new(Lesson,
            new ModuleSettingsInput("حصة", "Live", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.None, null),
            DateTime.UtcNow.AddDays(2), 60, new LiveRecurrenceInput(1, ["Monday"], null, null, "Asia/Riyadh"));

    /// <summary>A class course with a one-lesson plan at School1.</summary>
    internal static async Task<ClassCourse> NewClassCourseAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var school1 = w.Callers.A.School1;
        var year = await AcademicTestKit.AddYearAsync(factory, school1);
        var cls = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, school1);
        await LessonPlanTestKit.PublishAsync(factory, course, BlueprintApplyModes.PushToExisting,
            new TestSection(Section, "Week 1", new TestLesson(Lesson, "Intro")));
        return await LessonPlanTestKit.AddClassCourseWithPlanAsync(factory, cls, course);
    }

    /// <summary>Turns <c>Features.VirtualClasses</c> on at the root of the probe world's client (inherited by every unit), once.</summary>
    internal static async Task EnableVirtualClassesAsync(M1ProbeWorld w) => await EnableFlagAsync(w, FeatureFlags.VirtualClasses.Name);

    internal static async Task EnableFlagAsync(M1ProbeWorld w, string key)
    {
        var root = w.Callers.A.Root;
        await using var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        if (await db.Set<OrgUnitSetting>().AnyAsync(s => s.OrgUnitId == root.Id && s.Key == key))
        {
            return;
        }

        db.Add(new OrgUnitSetting(root.OrganizationId, root.Id, root.Path, key, "true", false));
        await db.SaveChangesAsync();
    }
}
