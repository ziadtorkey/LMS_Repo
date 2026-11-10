using Lms.Shared.Me;

namespace Lms.Client.Services;

/// <summary>Dev-only fake (enabled with Lms:UseFakeApi in wwwroot/appsettings.Development.json) until GET /api/me exists.</summary>
public sealed class FakeLmsApi : ILmsApi
{
    private static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    public Task<MeDto> GetMeAsync(CancellationToken ct = default) => Task.FromResult(new MeDto(
        Guid.Parse("00000000-0000-0000-0000-0000000000b1"), "أحمد المدير", "Ahmed Admin", false,
        [
            new(Guid.Parse("00000000-0000-0000-0000-0000000000c1"), OrgId, "مدارس النور", "Al Noor Schools",
                Guid.Parse("00000000-0000-0000-0000-0000000000d1"), "المدرسة الرئيسية", "Main School", "مدير المنظمة", "Org Admin"),
        ]));

    public Task<EffectivePermissionsDto> GetMyPermissionsAsync(CancellationToken ct = default) =>
        Task.FromResult(new EffectivePermissionsDto(null, []));
}
