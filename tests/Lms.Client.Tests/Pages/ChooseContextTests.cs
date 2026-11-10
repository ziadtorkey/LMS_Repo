using Bunit;
using Lms.Client.Layout;
using Lms.Client.Pages;
using Lms.Client.Tests.Support;
using Lms.Shared.Me;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace Lms.Client.Tests.Pages;

public sealed class ChooseContextTests : ClientTestContext
{
    private static UserContextDto Ctx(Guid id, string role = "Teacher") =>
        new(id, Guid.NewGuid(), "مدارس النور", "Al Noor", Guid.NewGuid(), "الفرع", "Branch", role, role);

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private void SetContexts(params UserContextDto[] contexts) => Api.Me = Api.Me with { Contexts = contexts };

    [Fact]
    public void Single_context_is_auto_selected()
    {
        var id = Guid.NewGuid();
        SetContexts(Ctx(id));

        var cut = Render<ContextGate>();
        cut.WaitForState(() => Context.ActiveAssignmentId == id);

        Assert.Equal(id, Context.ActiveAssignmentId);
    }

    [Fact]
    public void Multiple_contexts_without_stored_choice_redirect_to_chooser()
    {
        SetContexts(Ctx(Guid.NewGuid()), Ctx(Guid.NewGuid()));

        var cut = Render<ContextGate>();
        cut.WaitForState(() => Nav.Uri.EndsWith("/choose-context"));

        Assert.Null(Context.ActiveAssignmentId);
    }

    [Fact]
    public async Task Stored_context_not_in_me_list_is_discarded()
    {
        await Context.SetAsync(Guid.NewGuid());
        SetContexts(Ctx(Guid.NewGuid()), Ctx(Guid.NewGuid()));

        var cut = Render<ContextGate>();
        cut.WaitForState(() => Context.ActiveAssignmentId is null);

        Assert.Null(Context.ActiveAssignmentId);
        cut.WaitForState(() => Nav.Uri.EndsWith("/choose-context"));
    }

    [Fact]
    public async Task Multiple_contexts_show_chooser_and_store_selection()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        SetContexts(Ctx(a, "Teacher"), Ctx(b, "Manager"));

        var cut = Render<ChooseContext>();
        cut.WaitForElement($"[data-testid=context-option-{b}]");
        Assert.NotEmpty(cut.FindAll($"[data-testid=context-option-{a}]"));

        await cut.Find($"[data-testid=context-option-{b}]").ClickAsync(new());

        Assert.Equal(b, Context.ActiveAssignmentId);
    }

    [Theory]
    [InlineData("ar", "لا توجد صلاحيات")]
    [InlineData("en", "No roles assigned")]
    public void Zero_contexts_show_a_localized_empty_state(string culture, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        SetContexts();

        var cut = Render<ChooseContext>();
        cut.WaitForElement("[data-testid=no-contexts]");

        Assert.Contains(expected, cut.Find("[data-testid=no-contexts]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid^=context-option-]"));
    }
}
