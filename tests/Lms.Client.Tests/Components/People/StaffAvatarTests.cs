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

    [Fact]
    public void Keeps_the_placeholder_when_the_url_cannot_be_loaded()
    {
        Files.UrlFailure = new ApiProblemException(403, "forbidden", null, null);

        var cut = Render<StaffAvatar>(p => p.Add(x => x.PhotoFileId, Guid.NewGuid()));

        Assert.Empty(cut.FindAll("[data-testid=staff-avatar-img]"));
    }
}
