using Lms.Domain.Access;

namespace Lms.Api.Infrastructure;

/// <summary>Endpoint filter that lets a caller through when any one of the permissions is granted.</summary>
public sealed class AnyPermissionEndpointFilter(IReadOnlyList<string> permissions) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var http = invocation.HttpContext;
        var evaluator = http.RequestServices.GetRequiredService<IAccessEvaluator>();

        AccessDecision? last = null;
        foreach (var permission in permissions)
        {
            last = await evaluator.EvaluateAsync(permission, null, http.RequestAborted);
            if (last.Allowed)
            {
                return await next(invocation);
            }
        }

        return AccessProblem.From(last!, http);
    }
}
