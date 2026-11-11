using System.Text.Json;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.IdentityProviders;
using Lms.Infrastructure.Identity.External;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.IdentityProviders;
using Microsoft.EntityFrameworkCore;
using DomainMatchBy = Lms.Domain.IdentityProviders.ExternalMatchBy;

namespace Lms.Api.Features.IdentityProviders;

public static class IdentityProviderEndpoints
{
    public const string DiscoveryClient = "idp-discovery";

    public static IEndpointRouteBuilder MapIdentityProviderEndpoints(this IEndpointRouteBuilder app)
    {
        // Providers belong to a client as a whole: every call also needs its permission at the client root
        // (a school-level admin gets 403 OutOfScope).
        app.MapGet(Routes.IdentityProviders, ListAsync).RequirePermission(Permissions.IdentityProviders.View);
        app.MapPost(Routes.IdentityProviders, CreateAsync).RequirePermission(Permissions.IdentityProviders.Manage);
        app.MapPut(Routes.IdentityProviderPattern, UpdateAsync).RequirePermission(Permissions.IdentityProviders.Manage);
        app.MapDelete(Routes.IdentityProviderPattern, DeleteAsync).RequirePermission(Permissions.IdentityProviders.Manage);
        app.MapPost(Routes.IdentityProviderTestPattern, TestAsync).RequirePermission(Permissions.IdentityProviders.Manage);
        return app;
    }

    private static async Task<IResult> ListAsync(HttpContext http, LmsDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.IdentityProviders.View, ct) is { } refused)
        {
            return refused;
        }

        var rows = await db.Set<IdentityProviderConfig>().OrderBy(p => p.Slug).ToListAsync(ct);
        return Results.Ok(rows.Select(p => ToDto(p, configuration)).ToList());
    }

    private static async Task<IResult> CreateAsync(
        HttpContext http, LmsDbContext db, ICurrentContext context, IConfiguration configuration, IHostEnvironment env,
        ProviderSecretProtector protector, ExternalSchemeRegistry registry, IClock clock, SaveIdentityProviderRequest? body, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.IdentityProviders.Manage, ct) is { } refused)
        {
            return refused;
        }

        var (request, problem) = IdentityProviderValidators.Validate(body, isCreate: true, env.IsDevelopment());
        if (problem is not null)
        {
            return problem;
        }

        if (await SlugTakenAsync(db, request!.Slug, excludeId: null, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.provider_slug_taken);
        }

        var row = new IdentityProviderConfig(
            context.OrganizationId!.Value, Enum.Parse<IdentityProviderKind>(request.Kind), request.Slug, request.DisplayNameAr,
            request.DisplayNameEn, request.Authority!, request.AzureTenantId, request.ClientId, protector.Protect(request.ClientSecret!),
            request.Scopes, Enum.Parse<DomainMatchBy>(request.MatchBy), request.NationalIdClaim,
            string.Join(',', request.EmailDomains), request.Enabled, clock.UtcNow);
        db.Set<IdentityProviderConfig>().Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race for the slug (unique index).
            return ApiProblems.Conflict(ProblemCodes.provider_slug_taken);
        }

        await registry.SyncAsync(ct);
        return Results.Created(Routes.IdentityProvider(row.Id), ToDto(row, configuration));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, HttpContext http, LmsDbContext db, IConfiguration configuration, IHostEnvironment env,
        ProviderSecretProtector protector, ExternalSchemeRegistry registry, IClock clock, SaveIdentityProviderRequest? body, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.IdentityProviders.Manage, ct) is { } refused)
        {
            return refused;
        }

        // Filtered set: another client's provider simply does not exist for the caller.
        var row = await db.Set<IdentityProviderConfig>().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound();
        }

        var (request, problem) = IdentityProviderValidators.Validate(body, isCreate: false, env.IsDevelopment());
        if (problem is not null)
        {
            return problem;
        }

        // The slug is part of the callback URL registered at the provider, and the kind decides how the authority is built.
        if (request!.Slug != row.Slug || request.Kind != row.Kind.ToString())
        {
            return IdentityProviderValidators.ValidationFailed(new Dictionary<string, string[]>
            {
                [request.Slug != row.Slug ? "slug" : "kind"] = ["immutable"],
            });
        }

        row.Update(
            request.DisplayNameAr, request.DisplayNameEn, request.Authority!, request.AzureTenantId, request.ClientId,
            request.ClientSecret is null ? null : protector.Protect(request.ClientSecret), request.Scopes,
            Enum.Parse<DomainMatchBy>(request.MatchBy), request.NationalIdClaim, string.Join(',', request.EmailDomains),
            request.Enabled, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await registry.SyncAsync(ct);
        return Results.Ok(ToDto(row, configuration));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, HttpContext http, LmsDbContext db, ExternalSchemeRegistry registry, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.IdentityProviders.Manage, ct) is { } refused)
        {
            return refused;
        }

        var row = await db.Set<IdentityProviderConfig>().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound();
        }

        db.Remove(row); // soft delete
        await db.SaveChangesAsync(ct);
        await registry.SyncAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> TestAsync(
        Guid id, HttpContext http, LmsDbContext db, IHttpClientFactory httpClients, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.IdentityProviders.Manage, ct) is { } refused)
        {
            return refused;
        }

        var row = await db.Set<IdentityProviderConfig>().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound();
        }

        // Never a 5xx: every failure is reported in the body as a short machine key.
        try
        {
            using var client = httpClients.CreateClient(DiscoveryClient);
            var url = row.Authority.TrimEnd('/') + "/.well-known/openid-configuration";
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Results.Ok(new ProviderTestResultDto(false, null, "discovery_http_error"));
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var issuer = doc.RootElement.TryGetProperty("issuer", out var value) ? value.GetString() : null;
            return Results.Ok(string.IsNullOrEmpty(issuer)
                ? new ProviderTestResultDto(false, null, "discovery_invalid")
                : new ProviderTestResultDto(true, issuer, null));
        }
        catch (JsonException)
        {
            return Results.Ok(new ProviderTestResultDto(false, null, "discovery_invalid"));
        }
        catch (HttpRequestException ex) when (ex.InnerException is BlockedAddressException)
        {
            return Results.Ok(new ProviderTestResultDto(false, null, "authority_not_allowed"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return Results.Ok(new ProviderTestResultDto(false, null, "provider_unreachable"));
        }
    }

    private static async Task<bool> SlugTakenAsync(LmsDbContext db, string slug, Guid? excludeId, CancellationToken ct) =>
        // ISOLATION-BYPASS: the slug is unique across all clients (the callback path carries no client); only existence is
        // checked, nothing of the other client's row is returned. Soft-deleted rows count: the unique index includes them.
        await db.Set<IdentityProviderConfig>().IgnoreQueryFilters().AnyAsync(p => p.Slug == slug && p.Id != excludeId, ct);

    private static IdentityProviderDto ToDto(IdentityProviderConfig p, IConfiguration configuration) => new(
        p.Id, p.Kind.ToString(), p.Slug, p.DisplayNameAr, p.DisplayNameEn, p.Authority, p.AzureTenantId, p.ClientId,
        p.ProtectedClientSecret is not null, p.Scopes, p.MatchBy.ToString(), p.NationalIdClaim,
        p.EmailDomains.Split(',', StringSplitOptions.RemoveEmptyEntries), p.Enabled,
        $"{(configuration["App:PublicBaseUrl"] ?? string.Empty).TrimEnd('/')}/signin-oidc/{p.Slug}");
}
