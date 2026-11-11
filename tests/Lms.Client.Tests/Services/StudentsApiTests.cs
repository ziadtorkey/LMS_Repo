using System.Net;
using System.Text;
using Lms.Client.Services.Api;
using Lms.Shared.People;

namespace Lms.Client.Tests.Services;

public sealed class StudentsApiTests
{
    [Fact]
    public void Query_string_leaves_unset_filters_out()
    {
        Assert.Equal("?page=1&pageSize=50", StudentsApi.ToQueryString(new StudentQuery(null, PageSize: 50)));
    }

    [Fact]
    public void Query_string_carries_every_filter()
    {
        var unit = Guid.NewGuid();
        var cls = Guid.NewGuid();
        var year = Guid.NewGuid();

        var query = StudentsApi.ToQueryString(new StudentQuery(unit, false, "Active", "Male", "a b", StudentSorts.All[0], 2, 25, cls, true, year));

        Assert.Equal($"?page=2&pageSize=25&orgUnitId={unit}&includeDescendants=false&status=Active&gender=Male&search=a%20b&classId={cls}&unplaced=true&academicYearId={year}&sort={StudentSorts.All[0]}", query);
    }

    [Fact]
    public async Task Problem_response_exposes_the_count_extension()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"code\":\"possible_duplicate\",\"count\":3}", Encoding.UTF8, "application/problem+json"),
        };

        var problem = await ApiProblemException.FromResponseAsync(response);

        Assert.Equal("possible_duplicate", problem.Code);
        Assert.Equal(3, problem.Count);
    }
}
