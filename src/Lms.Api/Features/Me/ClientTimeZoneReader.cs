using System.Text.Json;
using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Me;

/// <summary>
/// The <c>Org.TimeZone</c> of the clients a user holds contexts in (D64). <c>/api/me</c> runs before a context is chosen, so the caller has no
/// client yet and the settings resolver would see nothing: the read is bound to the client and to the unit's ancestor paths of each valid assignment.
/// </summary>
internal static class ClientTimeZoneReader
{
    public static async Task<IReadOnlyDictionary<(Guid Org, string Path), string>> ReadAsync(
        LmsDbContext db, IReadOnlyCollection<(Guid Org, string Path)> units, CancellationToken ct)
    {
        var result = new Dictionary<(Guid, string), string>();
        foreach (var org in units.Select(u => u.Org).Distinct())
        {
            var paths = units.Where(u => u.Org == org).SelectMany(u => OrgUnitPath.AncestorsAndSelf(u.Path)).Distinct().ToList();

            // ISOLATION-BYPASS: the caller has no active client yet (GET /api/me); rows are limited to the client of one of the caller's valid assignments and to the
            // ancestor paths of the assignment units, live rows only; only the zone id is read.
            var rows = await db.Set<OrgUnitSetting>().IgnoreQueryFilters()
                .Where(x => !x.IsDeleted && x.Key == SettingKeys.OrgTimeZone.Name && x.OrganizationId == org && paths.Contains(x.OrgUnitPath))
                .OrderBy(x => x.OrgUnitPath.Length).ToListAsync(ct);
            foreach (var unit in units.Where(u => u.Org == org))
            {
                var chain = OrgUnitPath.AncestorsAndSelf(unit.Path);
                var applicable = rows.Where(r => chain.Contains(r.OrgUnitPath)).ToList();
                var winner = applicable.FirstOrDefault(r => r.IsLocked) ?? applicable.LastOrDefault();
                result[unit] = Valid(winner is null ? null : JsonSerializer.Deserialize<string>(winner.ValueJson));
            }
        }

        return result;
    }

    private static string Valid(string? value)
    {
        if (value is not null)
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(value);
                return value;
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // a stored id this server does not know: show the default
            }
        }

        return SettingKeys.OrgTimeZone.Default;
    }
}
