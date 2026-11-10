using System.Globalization;
using Bunit;
using Lms.Client.Components;
using Lms.Client.Tests.Support;

namespace Lms.Client.Tests.Components;

public sealed class BilingualNameFieldsTests : ClientTestContext
{
    [Fact]
    public void Arabic_name_field_is_rtl_even_in_english_ui()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        var cut = Render<BilingualNameFields>();

        Assert.Equal("rtl", cut.Find("[data-testid=name-ar]").GetAttribute("dir"));
        Assert.Equal("ltr", cut.Find("[data-testid=name-en]").GetAttribute("dir"));
    }

    [Fact]
    public async Task Typing_updates_both_two_way_values()
    {
        string ar = "", en = "";
        var cut = Render<BilingualNameFields>(p => p
            .Add(c => c.NameAr, ar).Add(c => c.NameArChanged, v => ar = v)
            .Add(c => c.NameEn, en).Add(c => c.NameEnChanged, v => en = v));

        await cut.Find("input[data-testid=name-ar]").ChangeAsync(new() { Value = "مدرسة" });
        await cut.Find("input[data-testid=name-en]").ChangeAsync(new() { Value = "School" });

        Assert.Equal("مدرسة", ar);
        Assert.Equal("School", en);
    }
}
