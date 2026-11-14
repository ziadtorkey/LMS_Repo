using Lms.Domain.Abstractions;
using Lms.Shared.LiveSessions;
using Microsoft.Extensions.Options;

namespace Lms.Infrastructure.Zoom;

/// <summary>
/// Answers "is the platform Zoom app configured, and does Zoom accept our credentials right now" for the provider-status screen and
/// the <c>VirtualClasses</c> enable guard. The answer carries no configuration detail and is cached briefly, so the screen cannot
/// turn into a stream of token requests.
/// </summary>
public sealed class ZoomProviderStatus(ZoomTokenCache tokens, IOptions<ZoomOptions> options, IClock clock)
{
    private static readonly TimeSpan HealthyFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UnhealthyFor = TimeSpan.FromSeconds(15);

    private readonly object _state = new();
    private ProviderStatusDto? _cached;
    private DateTime _until;

    public async Task<ProviderStatusDto> GetAsync(CancellationToken ct = default)
    {
        if (!options.Value.IsConfigured)
        {
            return new ProviderStatusDto(false, false);
        }

        lock (_state)
        {
            if (_cached is not null && clock.UtcNow < _until)
            {
                return _cached;
            }
        }

        bool healthy;
        try
        {
            await tokens.GetTokenAsync(ct);
            healthy = true;
        }
        catch (ZoomProviderException)
        {
            healthy = false;
        }

        var dto = new ProviderStatusDto(true, healthy);
        lock (_state)
        {
            _cached = dto;
            _until = clock.UtcNow + (healthy ? HealthyFor : UnhealthyFor);
        }

        return dto;
    }
}
