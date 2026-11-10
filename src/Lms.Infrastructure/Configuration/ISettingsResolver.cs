using Lms.Shared.Configuration;

namespace Lms.Infrastructure.Configuration;

public sealed record ResolvedSetting<T>(T Value, Guid? SourceOrgUnitId, bool IsLocked);

public interface ISettingsResolver
{
    Task<ResolvedSetting<T>> ResolveAsync<T>(SettingKey<T> key, string orgUnitPath, CancellationToken cancellationToken = default);

    /// <exception cref="Lms.Domain.Configuration.SettingLockedException">A strict ancestor locked the key.</exception>
    Task SetAsync<T>(SettingKey<T> key, Guid orgUnitId, T value, bool isLocked, CancellationToken cancellationToken = default);
}
