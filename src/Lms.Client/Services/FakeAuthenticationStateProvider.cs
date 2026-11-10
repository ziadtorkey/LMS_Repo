using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Lms.Client.Services;

/// <summary>Dev-only: always signed in as a fake user.</summary>
public sealed class FakeAuthenticationStateProvider : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "ahmed@example.test")], "fake"))));
}
