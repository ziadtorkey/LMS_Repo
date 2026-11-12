using Lms.Client.Services;
using Lms.Shared.AcademicYears;
using Lms.Shared.Organizations;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Support;

/// <summary>Shared setup of the class tests: the student test setup plus a controllable selected academic year and class-hosting level types.</summary>
public abstract class ClassesTestContext : StudentsTestContext
{
    protected static readonly Guid YearId = Guid.Parse("00000000-0000-0000-0000-0000000000c9");
    protected static readonly Guid NextYearId = Guid.Parse("00000000-0000-0000-0000-0000000000ca");

    protected StubYearStore Year { get; } = new(YearId);

    protected ClassesTestContext()
    {
        Services.AddSingleton<IYearStore>(Year);
        Org.Levels.Add(new LevelTypeDto(FakeUnitsLevels.Group, "المجموعة", "Group", 1, false, 1));
        Org.Levels.Add(new LevelTypeDto(FakeUnitsLevels.School, "المدرسة", "School", 2, false, 1));
        Org.Levels.Add(new LevelTypeDto(FakeUnitsLevels.Grade, "الصف", "Grade", 3, true, 1));
    }
}

/// <summary>The level type ids of the sample units (see <c>FakeUnits</c>).</summary>
public static class FakeUnitsLevels
{
    public static Guid Group => Lms.Client.Services.Api.FakeUnits.GroupLevelId;
    public static Guid School => Lms.Client.Services.Api.FakeUnits.SchoolLevelId;
    public static Guid Grade => Lms.Client.Services.Api.FakeUnits.GradeLevelId;
}

/// <summary>A year store whose selected year, the years and the closed flag are set by the test.</summary>
public sealed class StubYearStore : IYearStore
{
    public StubYearStore(Guid selectedId)
    {
        SelectedYearId = selectedId;
        Years =
        [
            Make(selectedId, "2026/2027", "2026/2027", AcademicYearStatuses.Active),
            Make(Guid.Parse("00000000-0000-0000-0000-0000000000ca"), "2027/2028", "2027/2028", AcademicYearStatuses.Planned),
        ];
    }

    public Guid? SelectedYearId { get; set; }
    public IReadOnlyList<AcademicYearDto> Years { get; set; }
    public AcademicYearDto? Selected => Years.FirstOrDefault(y => y.Id == SelectedYearId);
    public bool IsReadOnly => Selected?.Status == AcademicYearStatuses.Closed;

    public event Action? Changed;

    public Task LoadAsync(Guid? orgUnitId = null) => Task.CompletedTask;

    public Task SelectAsync(Guid yearId)
    {
        SelectedYearId = yearId;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public void Close()
    {
        Years = Years.Select(y => y.Id == SelectedYearId ? y with { Status = AcademicYearStatuses.Closed } : y).ToList();
        Changed?.Invoke();
    }

    private static AcademicYearDto Make(Guid id, string ar, string en, string status) =>
        new(id, Lms.Client.Services.Api.FakeUnits.GroupId, "", "", ar, en, new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30), status, false, []);
}
