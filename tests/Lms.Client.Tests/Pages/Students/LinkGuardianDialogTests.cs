using System.Globalization;
using Bunit;
using Lms.Client.Pages.Students;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Students;

public sealed class LinkGuardianDialogTests : StudentsTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Saving_right_after_typing_a_mobile_links_a_guardian_with_that_mobile()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Students.View, Permissions.Students.Create);
        RenderProviders();
        // The real form-flush.js blurs the focused input (committing a debounced field); keep its promise pending until the test did that.
        var flush = JSInterop.SetupVoid("lmsFormFlush");
        var studentId = Guid.NewGuid();
        _ = Services.GetRequiredService<IDialogService>().ShowAsync<LinkGuardianDialog>("t",
            new DialogParameters<LinkGuardianDialog> { { x => x.StudentId, studentId } });
        Dialogs.WaitForElement("[data-testid=dialog-save]", Wait);
        Dialogs.Find("input[data-testid=guardian-name-ar-0]").Change("ماجد");
        Dialogs.Find("input[data-testid=guardian-name-en-0]").Change("Majed");
        var mobile = Dialogs.Find("input[data-testid=guardian-mobile-0]");
        mobile.Input("0508888888"); // inside the 500 ms debounce

        _ = Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
        Dialogs.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, i => i.Identifier == "lmsFormFlush"), Wait);
        await mobile.BlurAsync(new());
        flush.SetVoidResult();

        Dialogs.WaitForAssertion(() => Assert.NotEmpty(GuardiansApi.Links), Wait);
        Assert.Equal("0508888888", GuardiansApi.Links.Single().Input.NewGuardian!.Mobile);
    }
}
