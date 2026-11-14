using Lms.Domain.AcademicYears;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.AcademicYears;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.AcademicYears;

public enum TermStructureOutcome
{
    /// <summary>Missing terms were created (or none were missing).</summary>
    Applied,

    Closed,

    /// <summary>The year is Active: its structure is fixed.</summary>
    NotPlanned,

    /// <summary>A term already has a gradebook, slot or attendance session.</summary>
    HasDependants,

    /// <summary>The days after the last existing term are too few for the missing terms.</summary>
    NoRoom,
}

/// <summary>
/// Two or three terms per year (M5 L8, D62). The structure a unit would use is the client setting <c>AcademicYears.TermsPerYear</c>
/// (default 2); applying a structure to a <c>Planned</c> year creates the missing terms from <see cref="TermTemplate"/> after the last
/// existing term, splitting the remaining days evenly. It never deletes, renames or moves an existing term (the admin edits them), and it
/// is refused once any term has dependants.
/// </summary>
public sealed class TermStructureService(LmsDbContext db, ISettingsResolver settings, IEnumerable<ITermDependants> dependants)
{
    public async Task<TermStructureDto> GetAsync(AcademicYear year, CancellationToken ct)
    {
        var configured = (await settings.ResolveAsync(SettingKeys.AcademicYearsTermsPerYear, year.OrgUnitPath, ct)).Value;
        var terms = TermTemplate.IsValid(configured) ? configured : TermTemplate.MinTerms;
        return new TermStructureDto(terms, TermTemplate.NamesFor(terms));
    }

    public async Task<TermStructureOutcome> ApplyAsync(AcademicYear year, int termsPerYear, CancellationToken ct)
    {
        if (year.Status == AcademicYearStatus.Closed)
        {
            return TermStructureOutcome.Closed;
        }

        if (year.Status != AcademicYearStatus.Planned)
        {
            return TermStructureOutcome.NotPlanned;
        }

        var terms = await db.Set<Term>().Where(t => t.AcademicYearId == year.Id).ToListAsync(ct);
        var ids = terms.Select(t => t.Id).ToList();
        foreach (var dependant in dependants)
        {
            if (ids.Count > 0 && await dependant.AnyAsync(ids, ct))
            {
                return TermStructureOutcome.HasDependants;
            }
        }

        var missing = termsPerYear - terms.Count;
        if (missing <= 0)
        {
            return TermStructureOutcome.Applied;
        }

        var from = terms.Count == 0 ? year.StartDate : terms.Max(t => t.EndDate).AddDays(1);
        var days = year.EndDate.DayNumber - from.DayNumber + 1;
        if (days < missing)
        {
            return TermStructureOutcome.NoRoom;
        }

        var names = TermTemplate.NamesFor(termsPerYear);
        var existing = terms.Count;
        var each = days / missing;
        var start = from;
        for (var i = 0; i < missing; i++)
        {
            var end = i == missing - 1 ? year.EndDate : start.AddDays(each - 1);
            var name = names[existing + i];
            var term = new Term(year, name.NameAr, name.NameEn, start, end);
            db.Set<Term>().Add(term);
            terms.Add(term);
            start = end.AddDays(1);
        }

        var order = 1;
        foreach (var term in terms.OrderBy(t => t.StartDate))
        {
            term.SetOrder(order++);
        }

        await db.SaveChangesAsync(ct);
        return TermStructureOutcome.Applied;
    }
}
