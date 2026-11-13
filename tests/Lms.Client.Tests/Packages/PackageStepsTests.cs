using Bunit;
using Lms.Client.Components.Packages;
using Lms.Client.Tests.Support;

namespace Lms.Client.Tests.Packages;

/// <summary>The package stepper uses only classes that lms-activities.css defines (it used lms-step-dot and lms-step--state, which nothing styled).</summary>
public sealed class PackageStepsTests : ClientTestContext
{
    [Fact]
    public void Steps_use_the_defined_classes_and_mark_done_current_and_failed()
    {
        var cut = Render<PackageSteps>(p => p.Add(x => x.Current, 2).Add(x => x.Failed, false));

        Assert.Contains("lms-step-done", cut.Find("[data-testid=package-step-1]").ClassName);
        Assert.Equal("step", cut.Find("[data-testid=package-step-2]").GetAttribute("aria-current"));
        Assert.DoesNotContain("lms-step-done", cut.Find("[data-testid=package-step-3]").ClassName ?? "");
        Assert.All(cut.FindAll("[data-testid^=package-step-] > span:first-child"), s => Assert.Contains("lms-step-number", s.ClassName));
        Assert.DoesNotContain("lms-step-dot", cut.Markup);
        Assert.DoesNotContain("lms-step--", cut.Markup);
    }

    [Fact]
    public void A_failed_step_is_marked_failed_with_its_icon()
    {
        var cut = Render<PackageSteps>(p => p.Add(x => x.Current, 2).Add(x => x.Failed, true));

        var step = cut.Find("[data-testid=package-step-2]");
        Assert.Contains("lms-step-failed", step.ClassName);
        Assert.NotNull(step.QuerySelector(".lms-step-number svg"));
    }
}
