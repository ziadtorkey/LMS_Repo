using Microsoft.Playwright;

namespace Lms.E2ETests.Infrastructure;

/// <summary>
/// One Playwright + Chromium per test run. Expects the stack to be running already
/// (docker compose + API in Development, see README); waits for GET /health before any test starts.
/// </summary>
public sealed class E2EFixture : IAsyncLifetime
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(90);

    public E2ESettings Settings { get; } = E2ESettings.Load();
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await WaitForHealthyAsync();
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Settings.Headless,
            SlowMo = Settings.SlowMoMs,
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        Playwright?.Dispose();
    }

    private async Task WaitForHealthyAsync()
    {
        using var http = new HttpClient { BaseAddress = Settings.BaseUrl, Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + HealthTimeout;
        string last = "no response";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await http.GetAsync("/health");
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode && body == "Healthy")
                {
                    return;
                }

                last = $"{(int)response.StatusCode} {body}";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex.Message;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException(
            $"The API at {Settings.BaseUrl} was not healthy within {HealthTimeout.TotalSeconds}s (last: {last}). " +
            "Start it first: docker compose up -d; dotnet run --project src/Lms.Api (see tests/Lms.E2ETests/README.md).");
    }
}

[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<E2EFixture>
{
    // Tests share the seeded database, so they run one after another.
    public const string Name = "e2e";
}
