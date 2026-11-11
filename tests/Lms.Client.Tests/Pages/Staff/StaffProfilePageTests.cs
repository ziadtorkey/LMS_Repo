using System.Globalization;
using Bunit;
using Lms.Client.Pages.Staff;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.People;

namespace Lms.Client.Tests.Pages.Staff;

public sealed class StaffProfilePageTests : StaffTestContext
{
    private async Task<IRenderedComponent<StaffProfilePage>> RenderAsync(Guid id)
    {
        await GrantAsync(Permissions.Staff.View);
        RenderProviders();
        return Render<StaffProfilePage>(p => p.Add(x => x.Id, id));
    }

    [Fact]
    public async Task Profile_shows_details_roles_and_history()
    {
        Culture.Current = new CultureInfo("en");
        var person = Staff.All.First(s => s.Summary.NameEn == "Sara Ahmed");
        var cut = await RenderAsync(person.Summary.Id);

        cut.WaitForElement("[data-testid=profile-name]");
        Assert.Contains("Sara Ahmed", cut.Find("[data-testid=profile-name]").TextContent);
        Assert.Contains("1234567890", cut.Find("[data-testid=profile-national-id]").TextContent);
        Assert.Contains("Manager", cut.Find("[data-testid=profile-roles]").TextContent);
        Assert.NotNull(cut.Find("[data-testid=profile-history]"));
    }

    [Fact]
    public async Task Profile_limited_view_hides_national_id_and_history()
    {
        Culture.Current = new CultureInfo("en");
        var person = Staff.Add("زائر", "Guest Teacher", "guest@x.test", PersonStatuses.Active, [], limited: true);
        var cut = await RenderAsync(person.Summary.Id);

        cut.WaitForElement("[data-testid=profile-limited]");
        Assert.Empty(cut.FindAll("[data-testid=profile-national-id]"));
        Assert.Empty(cut.FindAll("[data-testid=profile-mobile]"));
        Assert.Empty(cut.FindAll("[data-testid=profile-history]"));
    }

    [Fact]
    public async Task Profile_page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Staff.View);
        var id = Staff.All.First(s => s.Summary.NameEn == "Sara Ahmed").Summary.Id;

        var layout = Render<Lms.Client.Layout.MainLayout>(p => p.Add(l => l.Body,
            (Microsoft.AspNetCore.Components.RenderFragment)(b =>
            {
                b.OpenComponent<StaffProfilePage>(0);
                b.AddAttribute(1, nameof(StaffProfilePage.Id), id);
                b.CloseComponent();
            })));

        layout.WaitForElement("[data-testid=profile-name]");
        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("سارة أحمد", layout.Find("[data-testid=profile-name]").TextContent);
        Assert.Equal("ltr", layout.Find("[data-testid=profile-history] span[dir=ltr]").GetAttribute("dir"));
    }

    [Fact]
    public async Task Status_history_time_is_shown_in_local_time()
    {
        var person = Staff.All.First(s => s.Summary.NameEn == "Sara Ahmed");
        var cut = await RenderAsync(person.Summary.Id);

        var expected = person.StatusHistory[0].ChangedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Assert.Contains(expected, cut.Find("[data-testid=profile-history]").TextContent);
    }

    [Fact]
    public async Task Missing_person_shows_the_not_found_message()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderAsync(Guid.NewGuid());

        cut.WaitForElement("[data-testid=profile-error]");
        Assert.Contains("not found", cut.Find("[data-testid=profile-error]").TextContent);
    }

    [Fact]
    public async Task Expiry_date_is_shown_when_set()
    {
        var person = Staff.Add("منتهي", "Expiring", "exp@x.test", PersonStatuses.Active, [], new DateOnly(2027, 1, 31));
        var cut = await RenderAsync(person.Summary.Id);

        cut.WaitForElement("[data-testid=profile-expires]");
        Assert.Contains("2027-01-31", cut.Find("[data-testid=profile-expires]").TextContent);
    }
}
