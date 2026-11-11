using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Lms.IntegrationTests.Auth;

/// <summary>
/// A throw-away external OpenID Connect provider (navikt/mock-oauth2-server) that signs everybody in as one fixed identity.
/// The image digest is the same as the <c>mock-oidc</c> service in docker-compose.yml, which the E2E suite uses.
/// </summary>
public sealed class MockOidcServer : IAsyncDisposable
{
    public const string Image = "ghcr.io/navikt/mock-oauth2-server@sha256:65d4ed47ce094cc3849e41e03499bfb32372d69c726ec22b3d5a19eba9bc787e";
    public const string IssuerId = "client-a";

    private readonly IContainer _container;

    private MockOidcServer(IContainer container) => _container = container;

    /// <summary>Authority to configure on the provider: the issuer URL as the test process reaches it.</summary>
    public string Authority => $"http://localhost:{_container.GetMappedPublicPort(8080)}/{IssuerId}";

    public static async Task<MockOidcServer> StartAsync(string subject, IReadOnlyDictionary<string, string> claims)
    {
        // Non-interactive: /authorize answers at once; every token carries these claims (id token included).
        var allClaims = new Dictionary<string, string>(claims) { ["sub"] = subject };
        var config = JsonSerializer.Serialize(new
        {
            interactiveLogin = false,
            tokenCallbacks = new[]
            {
                new
                {
                    issuerId = IssuerId,
                    requestMappings = new[] { new { requestParam = "grant_type", match = "*", claims = allClaims } },
                },
            },
        });
        var container = new ContainerBuilder(Image)
            .WithPortBinding(8080, true)
            .WithEnvironment("JSON_CONFIG", config)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath($"/{IssuerId}/.well-known/openid-configuration")))
            .Build();
        await container.StartAsync();
        return new MockOidcServer(container);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
