using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Components.People;

public sealed class PhotoUploadTests : ClientTestContext
{
    private readonly FakeFileUploadApi _files = new();
    private Guid? _value;

    public PhotoUploadTests() => Services.AddSingleton<IFileUploadApi>(_files);

    private IRenderedComponent<PhotoUpload> RenderUpload(Guid unit)
    {
        Render<MudPopoverProvider>();
        return Render<PhotoUpload>(p => p
            .Add(x => x.OrgUnitId, unit)
            .Add(x => x.Value, _value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<Guid?>(this, id => _value = id)));
    }

    [Fact]
    public void Photo_upload_posts_file_and_shows_preview()
    {
        var unit = Guid.NewGuid();
        var cut = RenderUpload(unit);

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "me.png", contentType: "image/png"));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=photo-preview]")));
        var upload = Assert.Single(_files.Uploads);
        Assert.Equal(("me.png", "image/png", 3L, unit), upload);
        Assert.NotNull(_value);
    }

    [Fact]
    public void Non_image_file_is_rejected_before_upload()
    {
        var cut = RenderUpload(Guid.NewGuid());

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText("hello", "notes.txt", contentType: "text/plain"));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=photo-error]")));
        Assert.Empty(_files.Uploads);
    }

    [Fact]
    public void Remove_clears_the_value()
    {
        _value = Guid.NewGuid();
        var cut = RenderUpload(Guid.NewGuid());

        cut.Find("[data-testid=photo-remove]").Click();

        Assert.Null(_value);
    }
}
