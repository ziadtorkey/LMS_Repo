using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Components.People;

public sealed class StaffAvatarTests : StaffTestContext
{
    [Fact]
    public void Shows_the_photo_when_the_person_has_one()
    {
        var cut = Render<StaffAvatar>(p => p.Add(x => x.PhotoFileId, Guid.NewGuid()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=staff-avatar-img]")));
    }

    [Fact]
    public void Shows_the_placeholder_without_a_photo()
    {
        var cut = Render<StaffAvatar>();

        Assert.Empty(cut.FindAll("[data-testid=staff-avatar-img]"));
    }

    [Theory]
    [InlineData("Sara Ahmed", "S‌A")]
    [InlineData("khaled", "K")]
    [InlineData("سارة أحمد", "س‌أ")]
    public void Shows_the_initials_without_a_photo(string name, string expected)
    {
        var cut = Render<StaffAvatar>(p => p.Add(x => x.Name, name));

        Assert.Equal(expected, cut.Find(".lms-avatar-initials").TextContent);
    }

    [Fact]
    public void Avatar_is_hidden_from_screen_readers_because_the_name_is_shown_next_to_it() =>
        Assert.Equal("true", Render<StaffAvatar>().Find("[data-testid=staff-avatar]").GetAttribute("aria-hidden"));

    [Fact]
    public void Keeps_the_placeholder_when_the_url_cannot_be_loaded()
    {
        Files.UrlFailure = new ApiProblemException(403, "forbidden", null, null);

        var cut = Render<StaffAvatar>(p => p.Add(x => x.PhotoFileId, Guid.NewGuid()));

        Assert.Empty(cut.FindAll("[data-testid=staff-avatar-img]"));
    }
}
