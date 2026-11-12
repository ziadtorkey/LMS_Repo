// THROWAWAY: run the spike host first (dotnet run --project spike/m4b-package-host), then: dotnet run --project spike/m4b-package-host/probe
using Microsoft.Playwright;

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });

foreach (var query in new[] { "", "?csp=eval" })
{
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var navigations = new List<string>();
    page.FrameNavigated += (_, f) => navigations.Add(f.Url);
    await page.GotoAsync("http://app.localhost:5101/" + query);
    await page.WaitForFunctionAsync("document.title === 'DONE'", null, new() { Timeout = 15000 });
    await page.WaitForTimeoutAsync(500);
    Console.WriteLine($"===== app page{(query == "" ? " (strict CSP)" : " (CSP with unsafe-eval)")} =====");
    Console.WriteLine(await page.InnerTextAsync("#result"));
    Console.WriteLine("main frame URL after run: " + page.Url);
    Console.WriteLine("tracked by server: " + await page.EvaluateAsync<string>("fetch('/tracked').then(r => r.text())"));
    Console.WriteLine("frames navigated: " + string.Join(" | ", navigations.Select(u => u.Length > 70 ? u[..70] + "..." : u)));
}
