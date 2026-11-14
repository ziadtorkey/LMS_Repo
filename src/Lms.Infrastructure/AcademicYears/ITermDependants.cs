namespace Lms.Infrastructure.AcademicYears;

/// <summary>
/// A module that hangs data on terms (a gradebook, timetable slots, attendance sessions) says whether any of the given terms has some, so
/// the term structure of a year is not changed under it (M5 L8). Each module registers its own implementation as it lands; until then
/// <see cref="NoTermDependants"/> answers false.
/// </summary>
public interface ITermDependants
{
    Task<bool> AnyAsync(IReadOnlyCollection<Guid> termIds, CancellationToken ct);
}

/// <summary>The default: nothing hangs on terms yet.</summary>
public sealed class NoTermDependants : ITermDependants
{
    public Task<bool> AnyAsync(IReadOnlyCollection<Guid> termIds, CancellationToken ct) => Task.FromResult(false);
}
