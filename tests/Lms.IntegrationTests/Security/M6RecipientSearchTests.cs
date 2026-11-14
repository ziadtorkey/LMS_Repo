using System.Net;
using System.Net.Http.Json;
using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Recipient search under load and across clients (QA1's review of #770, item 4; QA1's #747 finding): the candidate window of the search is filtered by the D69
/// rules <b>after</b> it is taken, so a world with more than 180 name matches can hide the one allowed person. This class builds that world once: 200 students
/// named "Ahmad ..." in School1 (people the searcher may not reach), and one allowed person, student 2 of the M4a world (taught by teacher A), renamed to sort
/// last ("Ahmad Zzz"). Teacher A searching "Ahmad" must still find student 2, a call with no query must list every allowed person, a one-character search is
/// refused or empty (minimum two), and a user of another client is never listed, whatever the search. Waits for <c>GET /api/messaging/recipients</c> (#747).
/// The profile property names used to rename the student (<c>NameEn</c>) are checked when the route lands.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M6RecipientSearchTests(LmsApiFactory factory, ITestOutputHelper output)
{
    private const int Crowd = 200;
    private const string Name = "Ahmad";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _built;

    private async Task<M4aCallers> CrowdedWorldAsync()
    {
        var w = await M6Kit.WorldAsync(factory);
        await Gate.WaitAsync();
        try
        {
            if (_built)
            {
                return w;
            }

            var tree = w.Callers.A;
            for (var i = 0; i < Crowd; i++)
            {
                var user = await PeopleTestKit.AddUserAsync(factory);
                var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode(), "أحمد " + i, $"{Name} {i:000}");
                await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
                {
                    (await db.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id)).LinkUser(user.Id);
                    await db.SaveChangesAsync();
                }

                await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, user.Id);
            }

            // The allowed person sorts last by name.
            await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
            {
                var allowed = await db.Set<StudentProfile>().SingleAsync(s => s.Id == w.Data.StudentGroup2.Id);
                db.Entry(allowed).Property("NameEn").CurrentValue = $"{Name} Zzz";
                await db.SaveChangesAsync();
            }

            _built = true;
            return w;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<List<RecipientDto>> SearchAsync(M4aCallers w, M4aCaller who, string? search)
    {
        var url = search is null ? Routes.MessagingRecipients : Routes.MessagingRecipientsSearch(search);
        using var response = await w.SendAsync(who, HttpMethod.Get, url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<List<RecipientDto>>() ?? [];
    }

    [Fact]
    public async Task The_allowed_person_is_found_among_more_than_180_matches()
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.Recipients))
        {
            return;
        }

        var w = await CrowdedWorldAsync();
        var found = await SearchAsync(w, M4aCaller.TeacherA, Name);
        var allowedId = w.AssignmentOf(M4aCaller.StudentGroup2).UserId;
        Assert.Contains(found, p => p.UserId == allowedId);
        Assert.DoesNotContain(found, p => p.NameEn.StartsWith(Name + " 0", StringComparison.Ordinal) && !IsAllowed(p, w)); // a student of another class is never listed for teacher A
    }

    [Fact]
    public async Task A_call_without_a_query_lists_every_allowed_person()
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.Recipients))
        {
            return;
        }

        var w = await CrowdedWorldAsync();
        var listed = (await SearchAsync(w, M4aCaller.TeacherA, null)).Select(p => p.UserId).ToHashSet();
        foreach (var allowed in new[] { M4aCaller.StudentGroup1, M4aCaller.StudentGroup2, M4aCaller.Guardian, M4aCaller.TeacherB })
        {
            Assert.Contains(w.AssignmentOf(allowed).UserId, listed);
        }

        Assert.DoesNotContain(w.AssignmentOf(M4aCaller.StudentOfOtherClass).UserId, listed);
    }

    [Fact]
    public async Task A_one_character_search_is_refused_or_empty()
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.Recipients))
        {
            return;
        }

        var w = await CrowdedWorldAsync();
        using var response = await w.SendAsync(M4aCaller.TeacherA, HttpMethod.Get, Routes.MessagingRecipientsSearch("A"));
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.Empty(await response.Content.ReadFromJsonAsync<List<RecipientDto>>() ?? []);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Name)]
    [InlineData("Student")]
    public async Task Recipient_search_never_returns_another_clients_user(string? search)
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.Recipients))
        {
            return;
        }

        var w = await CrowdedWorldAsync();
        var foreign = w.Callers.AssignmentOf(Caller.ClientBAdmin).UserId;
        foreach (var who in Enum.GetValues<M4aCaller>())
        {
            Assert.DoesNotContain(await SearchAsync(w, who, search), p => p.UserId == foreign);
        }
    }

    private static bool IsAllowed(RecipientDto person, M4aCallers w) =>
        person.UserId == w.AssignmentOf(M4aCaller.StudentGroup2).UserId || person.UserId == w.AssignmentOf(M4aCaller.StudentGroup1).UserId;
}
