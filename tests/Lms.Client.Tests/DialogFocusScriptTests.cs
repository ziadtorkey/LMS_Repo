namespace Lms.Client.Tests;

/// <summary>The focus-return script is app-wide: it must exist and be loaded by the host page.</summary>
public sealed class DialogFocusScriptTests
{
    private static string Wwwroot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "src", "Lms.Client", "wwwroot");
    }

    [Fact]
    public void The_host_page_loads_the_dialog_focus_script()
    {
        Assert.Contains("js/dialog-focus.js", File.ReadAllText(Path.Combine(Wwwroot(), "index.html")));
        Assert.True(File.Exists(Path.Combine(Wwwroot(), "js", "dialog-focus.js")));
    }

    [Fact]
    public void The_script_restores_focus_to_the_element_that_had_it_before_the_dialog_opened()
    {
        var js = File.ReadAllText(Path.Combine(Wwwroot(), "js", "dialog-focus.js"));

        Assert.Contains("focusin", js);
        Assert.Contains(".mud-dialog", js);
        Assert.Contains(".focus()", js);
    }
}
