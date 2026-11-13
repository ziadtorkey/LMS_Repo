using System.Net;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Packages;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.PackageHost;

/// <summary>
/// Tokens already issued stop working, within the cache bound and without markers, when the module of the package expires, is deleted
/// on its own, or sits in a lesson or section that is hidden (#596). A preview token is not tied to the module. Any other method than
/// GET/HEAD/OPTIONS gets the same 404.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PackageAvailabilityTests(LmsApiFactory factory) : IDisposable
{
    private readonly PackageHostKit _kit = new(factory);

    public void Dispose() => _kit.Dispose();

    private async Task<(PackageUpload Package, Guid ModuleId, string Token, HttpClient Client)> LiveAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var package = await _kit.SeedPackageAsync(tree.Root, tree.School1, ("index.html", "<p/>"));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var moduleId = (await db.Set<PackageUpload>().SingleAsync(p => p.Id == package.Id)).ModuleId!.Value;
        var token = _kit.Token(package, Guid.NewGuid());
        var client = _kit.Content();
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, token));
        return (package, moduleId, token, client);
    }

    private static async Task<HttpStatusCode> GetAsync(HttpClient client, string token) => (await client.GetAsync($"/p/{token}/index.html")).StatusCode;

    private static async Task EventuallyNotFoundAsync(HttpClient client, string token)
    {
        for (var i = 0; i < 30; i++)
        {
            if (await GetAsync(client, token) == HttpStatusCode.NotFound)
            {
                return;
            }

            await Task.Delay(500);
        }

        Assert.Fail("The token still worked after 15 s.");
    }

    [Fact]
    public async Task Expired_visibility_window_stops_the_tokens()
    {
        var (_, moduleId, token, client) = await LiveAsync("AV1");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var module = await db.Set<LessonModule>().SingleAsync(m => m.Id == moduleId);
            module.Apply("ع", "T", ModuleVisibilities.Scheduled, null, DateTime.UtcNow.AddMilliseconds(-1), ModuleTargets.ClassCourse, null, CompletionRules.None, null);
            await db.SaveChangesAsync();
        }

        await EventuallyNotFoundAsync(client, token);
    }

    [Fact]
    public async Task Deleting_the_module_on_its_own_stops_the_tokens()
    {
        var (_, moduleId, token, client) = await LiveAsync("AV2");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<LessonModule>().SingleAsync(m => m.Id == moduleId));
            await db.SaveChangesAsync();
        }

        await EventuallyNotFoundAsync(client, token);
    }

    [Theory]
    [InlineData("lesson")]
    [InlineData("section")]
    public async Task Hiding_the_lesson_or_the_section_stops_the_tokens(string what)
    {
        var (_, moduleId, token, client) = await LiveAsync("AV3" + what);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var lesson = await (from m in db.Set<LessonModule>() join l in db.Set<ClassLesson>() on m.ClassLessonId equals l.Id where m.Id == moduleId select l).SingleAsync();
            if (what == "lesson")
            {
                lesson.Update(lesson.Order, lesson.TitleAr, lesson.TitleEn, lesson.ObjectivesAr, lesson.ObjectivesEn, lesson.PlannedOffsetDays, true);
            }
            else
            {
                var section = await db.Set<ClassSection>().SingleAsync(s => s.Id == lesson.SectionId);
                section.Update(section.Order, section.TitleAr, section.TitleEn, true);
            }

            await db.SaveChangesAsync();
        }

        await EventuallyNotFoundAsync(client, token);
    }

    [Fact]
    public async Task Preview_token_works_without_a_visible_module()
    {
        var (package, moduleId, _, client) = await LiveAsync("AV4");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<LessonModule>().SingleAsync(m => m.Id == moduleId));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, _kit.Token(package, preview: true)));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task Other_methods_get_the_same_404_with_the_host_headers(string method)
    {
        var (_, _, token, client) = await LiveAsync("AV5" + method);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"/p/{token}/index.html"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.StartsWith("sandbox allow-scripts;", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }
}
