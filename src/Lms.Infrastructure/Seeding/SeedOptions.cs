namespace Lms.Infrastructure.Seeding;

/// <summary>Dev-only seed values (configuration section "Seed"); without a password no users are created.</summary>
public sealed record SeedOptions(string? PlatformAdminEmail, string? DefaultPassword, int PerfStudents = 0);