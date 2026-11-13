using Lms.Client.Services.Api;
using Lms.Shared.Configuration;
using Lms.Shared.Me;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Support;

/// <summary>Setup of the class-course and lesson-plan view tests: fake plans API and unit settings.</summary>
public abstract class LessonPlansTestContext : StaffTestContext
{
    protected static readonly Guid UnitId = Guid.Parse("00000000-0000-0000-0000-00000000c101");

    protected FakeLessonPlansApi Plans { get; } = new();
    protected StubSettingsApi Settings { get; } = new();

    protected LessonPlansTestContext()
    {
        Services.AddSingleton<ILessonPlansApi>(Plans);
        Services.AddSingleton<Lms.Client.Services.Packages.IPackagePlayerApi, Lms.Client.Services.Packages.FakePackagePlayerApi>(); // the package card on Home and the course home
        Services.AddSingleton<Lms.Client.Services.Api.IConfigApi>(Settings);
    }

    /// <summary>Makes the active context sit at <see cref="UnitId"/>, so unit settings (Hijri dates) are read from there.</summary>
    protected void PlaceContextAtUnit()
    {
        var active = Context.ActiveAssignmentId ?? Guid.NewGuid();
        Api.Me = Api.Me with
        {
            Contexts = [new UserContextDto(active, Guid.NewGuid(), "مدرسة", "School", UnitId, "الصف", "Grade", "معلم", "Teacher")],
        };
    }

    protected void ShowHijri() => Settings.Values[SettingKeys.UiShowHijriDates.Name] = "true";
}
