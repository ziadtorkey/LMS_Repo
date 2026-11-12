using System.Text.Json;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.People.Students;

/// <summary>A feature flag for one student (§4.4): the student's own override if there is one, else the setting at their home unit.</summary>
public interface IStudentFeatureResolver
{
    Task<bool> IsEnabledAsync(Guid studentId, SettingKey<bool> flag, CancellationToken cancellationToken);
}

public sealed class StudentFeatureResolver(LmsDbContext db, ISettingsResolver settings) : IStudentFeatureResolver
{
    public static IReadOnlyDictionary<string, bool> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, bool>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? new Dictionary<string, bool>();
    }

    public async Task<bool> IsEnabledAsync(Guid studentId, SettingKey<bool> flag, CancellationToken ct)
    {
        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == studentId, ct)
            ?? throw new KeyNotFoundException("The student is not visible.");
        return Parse(student.FeatureFlagOverridesJson).TryGetValue(flag.Name, out var own)
            ? own
            : (await settings.ResolveAsync(flag, student.OrgUnitPath, ct)).Value;
    }
}
