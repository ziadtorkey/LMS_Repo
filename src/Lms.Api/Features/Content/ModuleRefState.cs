using Lms.Domain.Content;
using Lms.Domain.Library;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Content;

/// <summary>
/// Read-time state of the activity a module points to (#526): a library module whose resource was deleted is kept in closed classes as
/// history and shown as "resource removed" (<c>ModuleNodeDto.RefRemoved</c>); there is no column for it.
/// </summary>
internal static class ModuleRefState
{
    /// <summary>The ids of the library resources behind <paramref name="modules"/> that are deleted.</summary>
    public static async Task<IReadOnlySet<Guid>> RemovedAsync(LmsDbContext db, IReadOnlyCollection<LessonModule> modules, CancellationToken ct)
    {
        var refs = modules.Where(m => m.Kind == ModuleKinds.LibraryResource && m.RefId is not null)
            .Select(m => (Org: m.OrganizationId, Id: m.RefId!.Value)).Distinct().ToList();
        if (refs.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var removed = new HashSet<Guid>();
        foreach (var byOrg in refs.GroupBy(r => r.Org))
        {
            var ids = byOrg.Select(r => r.Id).ToList();
            // ISOLATION-BYPASS: the resource sits at an ancestor unit above the caller's scope and is deleted (hidden by the soft-delete filter);
            // only the ids of resources a module of this client already points to leave, client matched, so a deleted resource is told apart
            // from one the caller cannot see.
            removed.UnionWith(await db.Set<LibraryResource>().IgnoreQueryFilters()
                .Where(r => r.OrganizationId == byOrg.Key && r.IsDeleted && ids.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct));
        }

        return removed;
    }
}
