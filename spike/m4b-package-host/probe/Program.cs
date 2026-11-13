// THROWAWAY: run the spike host first (dotnet run --project spike/m4b-package-host), then: dotnet run --project spike/m4b-package-host/probe
using Microsoft.Playwright;

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });

// 1. The embedded run, per header variant. "outside requests" counts requests that reached example.com (the data-leak test).
foreach (var (label, query) in new[]
{
    ("strict CSP + sandbox directive", ""),
    ("CSP with unsafe-eval", "?csp=eval"),
    ("DEMO: app page without frame-src", "?hdr=nofs"),
})
{
    await using var context = await browser.NewContextAsync();
    var outside = new List<string>();
    await context.RouteAsync("**://example.com/**", route => { outside.Add(route.Request.Url); return route.AbortAsync(); });
    var page = await context.NewPageAsync();
    var navigations = new List<string>();
    page.FrameNavigated += (_, f) => navigations.Add(f.Url);
    await page.GotoAsync("http://app.localhost:5101/" + query);
    await page.WaitForFunctionAsync("document.title === 'DONE'", null, new() { Timeout = 15000 });
    await page.WaitForTimeoutAsync(800);
    Console.WriteLine($"===== embedded: {label} =====");
    Console.WriteLine(await page.InnerTextAsync("#result"));
    Console.WriteLine("main frame URL after run: " + page.Url);
    Console.WriteLine("tracked by server: " + (await page.EvaluateAsync<int>("fetch('/tracked').then(r => r.json()).then(a => a.length)")) + " commits (cumulative)");
    Console.WriteLine("frames navigated: " + string.Join(" | ", navigations.Select(u => u.Length > 70 ? u[..70] + "..." : u)));
    Console.WriteLine("requests that reached example.com: " + (outside.Count == 0 ? "none" : string.Join(", ", outside)));
}

// 2. The package URL opened directly as a top-level page (a leaked or lured URL), with and without the CSP sandbox directive.
foreach (var (label, query) in new[] { ("with sandbox directive", ""), ("DEMO: without sandbox directive", "?hdr=nosandbox") })
{
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    await page.GotoAsync("http://app.localhost:5101/");
    var launch = await page.GetAttributeAsync("iframe", "src");
    var top = launch!.Replace("index.html", "top.html").Split('?')[0] + query;
    await page.GotoAsync(top);
    for (var i = 0; i < 100 && await page.TitleAsync() != "TOPDONE"; i++) await page.WaitForTimeoutAsync(100);
    Console.WriteLine($"===== package opened as top-level page: {label} =====");
    Console.WriteLine(await page.InnerTextAsync("#r"));
}
