using Lms.Client.Services.Api;
using Lms.Shared.Courses;

namespace Lms.Client.Tests.Services;

public sealed class CoursesApiTests
{
    [Fact]
    public void Query_string_leaves_unset_filters_out()
    {
        Assert.Equal("?page=1&pageSize=50", CoursesApi.ToQueryString(new CourseQuery(null, PageSize: 50)));
    }

    [Fact]
    public void Query_string_carries_every_filter()
    {
        var unit = Guid.NewGuid();

        var query = CoursesApi.ToQueryString(new CourseQuery(unit, false, CourseStatuses.Active, "a b", true, CourseSorts.NameDesc, 2, 25));

        Assert.Equal($"?page=2&pageSize=25&orgUnitId={unit}&includeInherited=false&status=Active&search=a%20b&supervisedByMe=true&sort=-name", query);
    }
}
