using Bunit;
using Lms.Client.Pages.Dev;
using Lms.Client.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Components.Messaging;

/// <summary>The M6 component gallery is a standalone-dev-server aid: against a real API it is only the not-found page.</summary>
public sealed class MessagingGalleryTests : ClientTestContext
{
    private void UseFakeApi(bool on) =>
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Lms:UseFakeApi"] = on ? "true" : "false" }).Build());

    [Fact]
    public void Gallery_is_not_found_without_the_fake_api()
    {
        UseFakeApi(false);
        var cut = Render<MessagingGallery>();

        Assert.Empty(cut.FindAll("[data-testid=gallery-title]"));
        Assert.Empty(cut.FindAll(".lms-inbox"));
    }

    [Fact]
    public void Gallery_shows_the_components_on_the_standalone_server()
    {
        UseFakeApi(true);
        var cut = Render<MessagingGallery>();

        Assert.NotEmpty(cut.FindAll("[data-testid=gallery-title]"));
        Assert.Equal(3, cut.FindAll("a.lms-conv").Count);
        Assert.Equal(2, cut.FindAll("article.lms-ann").Count);
    }
}
