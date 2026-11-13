using System.Text.RegularExpressions;
using Lms.Domain.Notifications;
using Lms.Shared;

namespace Lms.UnitTests.Contracts;

/// <summary>Each <see cref="PageRoutes"/> value equals the <c>@page</c> template its Blazor page declares (parameters compared by position).</summary>
public sealed partial class PageRoutesTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static TheoryData<string, string> Routes => new()
    {
        { "ChooseContext", PageRoutes.ChooseContext },
        { "Staff", PageRoutes.Staff(A).Replace(A.ToString(), "{}") },
        { "ClassCourseForum", PageRoutes.ClassCourseForum(A).Replace(A.ToString(), "{}") },
        { "ForumThread", PageRoutes.ForumThread(A, B).Replace(A.ToString(), "{}").Replace(B.ToString(), "{}") },
        { "ClassCourseAnnouncements", PageRoutes.ClassCourseAnnouncements(A).Replace(A.ToString(), "{}") },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void Page_route_equals_a_page_directive_of_the_client(string name, string route)
    {
        var declared = DeclaredPages();

        Assert.True(declared.Contains(route), $"PageRoutes.{name} = \"{route}\" is not an @page of any Lms.Client page.");
    }

    [Fact]
    public void Page_routes_are_valid_notification_deep_links()
    {
        string[] links =
        [
            PageRoutes.ChooseContext, PageRoutes.Staff(A), PageRoutes.ClassCourseForum(A),
            PageRoutes.ForumThread(A, B), PageRoutes.ClassCourseAnnouncements(A),
        ];

        Assert.All(links, link => Assert.True(NotificationDeepLinks.IsValid(link), link));
    }

    private static HashSet<string> DeclaredPages()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Lms.Client", "Pages")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var pages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir.FullName, "src", "Lms.Client", "Pages"), "*.razor", SearchOption.AllDirectories))
        {
            foreach (Match m in PageDirective().Matches(File.ReadAllText(file)))
            {
                pages.Add(Parameter().Replace(m.Groups[1].Value, "{}"));
            }
        }

        return pages;
    }

    [GeneratedRegex("""^@page\s+"([^"]+)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex PageDirective();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Parameter();
}
