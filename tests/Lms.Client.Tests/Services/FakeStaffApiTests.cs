using Lms.Client.Services.Api;
using Lms.Shared.People;

namespace Lms.Client.Tests.Services;

public sealed class FakeStaffApiTests
{
    [Fact]
    public async Task List_honours_the_unit_filter()
    {
        var api = new FakeStaffApi();

        var all = await api.ListAsync(new StaffQuery(null));
        var otherUnit = await api.ListAsync(new StaffQuery(Guid.NewGuid()));
        var school = await api.ListAsync(new StaffQuery(FakeUnits.SchoolId));

        Assert.NotEmpty(all.Items);
        Assert.Empty(otherUnit.Items);
        Assert.NotEmpty(school.Items);
    }
}
