namespace Lms.Api.Infrastructure;

/// <summary>Extension point for endpoints registered through DI (production modules use app.Map&lt;Module&gt;Endpoints()).</summary>
public interface IEndpointModule
{
    void Map(IEndpointRouteBuilder endpoints);
}