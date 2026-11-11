using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;

namespace Lms.Api.Features.Classes;

public static class ClassEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.Classes,
                ([AsParameters] ClassQuery query, HttpContext http, ClassHandlers h, CancellationToken ct) => h.ListAsync(query, http, ct))
            .RequirePermission(Permissions.Classes.View);
        app.MapGet(Routes.ClassPattern, (Guid id, HttpContext http, ClassHandlers h, CancellationToken ct) => h.GetAsync(id, http, ct))
            .RequirePermission(Permissions.Classes.View);
        app.MapPost(Routes.Classes, (SaveClassRequest r, HttpContext http, ClassHandlers h, CancellationToken ct) => h.CreateAsync(r, http, ct))
            .RequirePermission(Permissions.Classes.Create);
        app.MapPut(Routes.ClassPattern,
                (Guid id, SaveClassRequest r, HttpContext http, ClassHandlers h, CancellationToken ct) => h.UpdateAsync(id, r, http, ct))
            .RequirePermission(Permissions.Classes.Update);
        app.MapDelete(Routes.ClassPattern, (Guid id, HttpContext http, ClassHandlers h, CancellationToken ct) => h.DeleteAsync(id, http, ct))
            .RequirePermission(Permissions.Classes.Delete);
        app.MapPost(Routes.ClassClosePattern, (Guid id, HttpContext http, ClassHandlers h, CancellationToken ct) => h.CloseAsync(id, http, ct))
            .RequirePermission(Permissions.Classes.Close);
        app.MapPost(Routes.ClassesBulk,
                (BulkCreateClassesRequest r, HttpContext http, BulkClassHandlers h, CancellationToken ct) => h.BulkAsync(r, http, ct))
            .RequirePermission(Permissions.Classes.Create);
        app.MapPost(Routes.ClassesCopy,
                (CopyClassesRequest r, HttpContext http, BulkClassHandlers h, CancellationToken ct) => h.CopyAsync(r, http, ct))
            .RequirePermission(Permissions.Classes.Create);
    }
}
