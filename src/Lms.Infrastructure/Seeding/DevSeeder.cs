using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Lms.Infrastructure.Seeding;

/// <summary>
/// Seeds the two sample clients (school group and university) with level types, trees and built-in roles.
/// Development only and idempotent: every row is looked up by a stable key before it is created.
/// </summary>
public sealed class DevSeeder(string connectionString, IHostEnvironment environment, SeedOptions? options = null)
{
    private sealed class SeedContext : ICurrentContext
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public Guid? AssignmentId => null;
        public string? ScopePath => null;
        public bool IsPlatformAdmin => true;
    }

    /// <summary>Returns false (and touches nothing) outside the Development environment.</summary>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
        {
            return false;
        }

        await using var db = LmsDbContextFactory.Create(connectionString, new SeedContext());
        await db.Database.MigrateAsync(cancellationToken);

        var school = await SeedTreeAsync(db, DevSeedData.SchoolGroup, cancellationToken);
        await SeedTreeAsync(db, DevSeedData.University, cancellationToken);

        if (!string.IsNullOrWhiteSpace(options?.DefaultPassword))
        {
            await SeedUsersAsync(db, school, options!, cancellationToken);
        }

        return true;
    }

    private sealed record SeededClient(Organization Organization, Dictionary<string, OrgUnit> Units);

    private static async Task<SeededClient> SeedTreeAsync(LmsDbContext db, DevSeedData.ClientSeed seed, CancellationToken ct)
    {
        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == seed.OrganizationId, ct);
        if (organization is null)
        {
            organization = new Organization(seed.OrganizationId, seed.NameAr, seed.NameEn, seed.Type);
            db.Organizations.Add(organization);
            await db.SaveChangesAsync(ct);
        }

        var levels = await db.LevelTypes.Where(l => l.OrganizationId == organization.Id).ToDictionaryAsync(l => l.NameEn, ct);
        foreach (var (level, index) in seed.Levels.Select((l, i) => (l, i)))
        {
            if (!levels.ContainsKey(level.NameEn))
            {
                var created = new LevelType(organization.Id, level.NameAr, level.NameEn, index + 1, level.CanHostClasses);
                db.LevelTypes.Add(created);
                levels[level.NameEn] = created;
            }
        }

        await db.SaveChangesAsync(ct);

        var units = await db.OrgUnits.Where(u => u.OrganizationId == organization.Id).ToDictionaryAsync(u => u.Code, ct);
        await AddUnitsAsync(db, organization, levels, units, seed.Root, parent: null, ct);

        await new DefaultRoleProvisioner(db).ProvisionAsync(organization.Id, seed.Type, ct);
        return new SeededClient(organization, units);
    }

    private static async Task SeedUsersAsync(LmsDbContext db, SeededClient school, SeedOptions options, CancellationToken ct)
    {
        var roles = await db.Roles.Where(r => r.OrganizationId == school.Organization.Id && r.BuiltInKey != null)
            .ToDictionaryAsync(r => r.BuiltInKey!, ct);

        if (!string.IsNullOrWhiteSpace(options.PlatformAdminEmail))
        {
            await EnsureUserAsync(db, options.PlatformAdminEmail, "مدير المنصة", "Platform Admin", options.DefaultPassword!, true, ct);
        }

        // One user per built-in role, plus the chooser user (two contexts) and the lockout user (used only by the
        // lockout E2E so shared users never get locked).
        (string Email, string NameAr, string NameEn, (string Role, string Unit)[] Assignments)[] people =
        [
            ("orgadmin@altagamo3.test", "مدير النظام", "Org Admin", [("OrgAdmin", "S")]),
            ("manager@altagamo3.test", "مدير المدرسة", "School Manager", [("Manager", "S")]),
            ("supervisor@altagamo3.test", "المشرف الأكاديمي", "Academic Supervisor", [("Supervisor", "PRI")]),
            ("counselor@altagamo3.test", "الأخصائي الاجتماعي", "Social Worker", [("Counselor", "S")]),
            ("teacher@altagamo3.test", "المعلم", "Teacher", [("Teacher", "KG-S1")]),
            ("student@altagamo3.test", "الطالب", "Student", [("Student", "KG-S1")]),
            ("guardian@altagamo3.test", "ولي الأمر", "Parent", [("Guardian", "S")]),
            ("multi@altagamo3.test", "معلم ومشرف", "Teacher and Supervisor", [("Teacher", "KG"), ("Supervisor", "PRI")]),
            ("lockout-e2e@altagamo3.test", "مستخدم القفل", "Lockout Test User", [("Teacher", "KG")]),
        ];

        // Accounts in each blocked status (FR-SEC-07), used only by the blocked-login E2E: the right password must show
        // the status message instead of signing in.
        (string Email, string NameAr, string NameEn, UserStatus Status)[] blocked =
        [
            ("deactivated-e2e@altagamo3.test", "حساب معطّل", "Deactivated Test User", UserStatus.Deactivated),
            ("expired-e2e@altagamo3.test", "حساب منتهي", "Expired Test User", UserStatus.Expired),
            ("withdrawn-e2e@altagamo3.test", "حساب منسحب", "Withdrawn Test User", UserStatus.Withdrawn),
        ];
        people = [.. people, .. blocked.Select(b => (b.Email, b.NameAr, b.NameEn, new[] { ("Teacher", "KG") }))];

        foreach (var person in people)
        {
            var user = await EnsureUserAsync(db, person.Email, person.NameAr, person.NameEn, options.DefaultPassword!, false, ct);
            foreach (var (role, unitCode) in person.Assignments)
            {
                var unit = school.Units[unitCode];
                var roleId = roles[role].Id;
                var exists = await db.RoleAssignments.AnyAsync(
                    a => a.UserId == user.Id && a.RoleId == roleId && a.OrgUnitId == unit.Id, ct);
                if (!exists)
                {
                    db.RoleAssignments.Add(new RoleAssignment(unit, user.Id, roleId));
                }
            }

            await db.SaveChangesAsync(ct);
        }

        foreach (var b in blocked)
        {
            var user = await db.Users.SingleAsync(u => u.NormalizedEmail == b.Email.ToUpperInvariant(), ct);
            if (user.Status != b.Status)
            {
                user.Status = b.Status;
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private static async Task<User> EnsureUserAsync(
        LmsDbContext db, string email, string nameAr, string nameEn, string password, bool isPlatformAdmin, CancellationToken ct)
    {
        var normalized = email.ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is not null)
        {
            return user;
        }

        user = new User
        {
            UserName = email,
            NormalizedUserName = normalized,
            Email = email,
            NormalizedEmail = normalized,
            EmailConfirmed = true,
            DisplayNameAr = nameAr,
            DisplayNameEn = nameEn,
            IsPlatformAdmin = isPlatformAdmin,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            LockoutEnabled = true,
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }
    private static async Task AddUnitsAsync(
        LmsDbContext db, Organization organization, Dictionary<string, LevelType> levels,
        Dictionary<string, OrgUnit> units, DevSeedData.UnitSeed node, OrgUnit? parent, CancellationToken ct)
    {
        if (!units.TryGetValue(node.Code, out var unit))
        {
            var levelId = levels[node.Level].Id;
            unit = parent is null
                ? OrgUnit.CreateRoot(organization.Id, levelId, node.NameAr, node.NameEn, node.Code)
                : OrgUnit.CreateChild(parent, levelId, node.NameAr, node.NameEn, node.Code);
            if (node.IsLevelTransfer)
            {
                unit.MarkAsLevelTransfer(null);
            }

            db.OrgUnits.Add(unit);
            await db.SaveChangesAsync(ct);
            units[node.Code] = unit;
        }

        foreach (var child in node.Children)
        {
            await AddUnitsAsync(db, organization, levels, units, child, unit, ct);
        }
    }
}