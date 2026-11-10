using Lms.Domain.Access;

namespace Lms.Api.Infrastructure;

/// <summary>Endpoint filter enforcing one permission through <see cref="IAccessEvaluator"/>.</summary>
public sealed class PermissionEndpointFilter(string permission) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var http = invocation.HttpContext;
        var evaluator = http.RequestServices.GetRequiredService<IAccessEvaluator>();

        var decision = await evaluator.EvaluateAsync(permission, null, http.RequestAborted);
        return decision.Allowed ? await next(invocation) : AccessProblem.From(decision, http);
    }
}