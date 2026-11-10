using Lms.Shared.Authorization;
using Lms.Shared.Me;

namespace Lms.Client.Services;

/// <summary>Standalone dev fake (Lms:UseFakeApi=true) until the real Me endpoints are used.</summary>
public sealed class FakeLmsApi : ILmsApi
{
    private static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    public Task<MeDto> GetMeAsync(CancellationToken ct = default) => Task.FromResult(new MeDto(
        Guid.Parse("00000000-0000-0000-0000-0000000000b1"), "أحمد المدير", "Ahmed Admin", true,
        [
            new(Guid.Parse("00000000-0000-0000-0000-0000000000c1"), OrgId, "مدارس النور", "Al Noor Schools",
                Guid.Parse("00000000-0000-0000-0000-0000000000d1"), "المدرسة الرئيسية", "Main School", "مدير المنظمة", "Org Admin"),
            new(Guid.Parse("00000000-0000-0000-0000-0000000000c2"), OrgId, "مدارس النور", "Al Noor Schools",
                Guid.Parse("00000000-0000-0000-0000-0000000000d2"), "فرع الشمال", "North Branch", "معلم", "Teacher"),
        ]));

    public Task<EffectivePermissionsDto> GetMyPermissionsAsync(CancellationToken ct = default) =>
        Task.FromResult(new EffectivePermissionsDto(null, Permissions.All));
}
