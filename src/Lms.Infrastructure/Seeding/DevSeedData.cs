using Lms.Domain.Organizations;

namespace Lms.Infrastructure.Seeding;

/// <summary>Sample taxonomies mirroring the backlog sheets (M0 plan, Task 12).</summary>
public static class DevSeedData
{
    public sealed record LevelSeed(string NameAr, string NameEn, bool CanHostClasses = false);

    public sealed record UnitSeed(
        string Code, string Level, string NameAr, string NameEn, bool IsLevelTransfer = false, params UnitSeed[] Children);

    public sealed record ClientSeed(
        Guid OrganizationId, string NameAr, string NameEn, OrganizationType Type,
        IReadOnlyList<LevelSeed> Levels, UnitSeed Root);

    public static readonly Guid SchoolGroupId = Guid.Parse("018f0000-0000-7000-8000-000000000001");
    public static readonly Guid UniversityId = Guid.Parse("018f0000-0000-7000-8000-000000000002");

    public static ClientSeed SchoolGroup { get; } = new(
        SchoolGroupId, "مجموعة مدارس التجمع", "Altagamo3 Schools", OrganizationType.SchoolGroup,
        [new("مدرسة", "School"), new("مرحلة", "Stage"), new("فصل", "Section", CanHostClasses: true)],
        new UnitSeed("S", "School", "مدرسة التجمع", "School Altagamo3", false,
            new UnitSeed("KG", "Stage", "رياض الأطفال", "KG", true,
                new UnitSeed("KG-S1", "Section", "الفصل 1", "Section 1"),
                new UnitSeed("KG-S2", "Section", "الفصل 2", "Section 2")),
            new UnitSeed("PRI", "Stage", "المرحلة الابتدائية", "Primary", true,
                new UnitSeed("PRI-S1", "Section", "الفصل 1", "Section 1"),
                new UnitSeed("PRI-S2", "Section", "الفصل 2", "Section 2"),
                new UnitSeed("PRI-S3", "Section", "الفصل 3", "Section 3"))));

    public static ClientSeed University { get; } = new(
        UniversityId, "جامعة MNF", "MNF University", OrganizationType.University,
        [
            new("جامعة", "University"), new("كلية", "Faculty"), new("سنة", "Year"),
            new("تخصص", "Specialization", CanHostClasses: true),
        ],
        new UnitSeed("MNF", "University", "جامعة MNF", "MNF", false,
            new UnitSeed("FCI", "Faculty", "كلية الحاسبات والمعلومات", "FCI", false,
                new UnitSeed("FCI-1", "Year", "الفرقة الأولى", "First", true,
                    new UnitSeed("FCI-1-GEN", "Specialization", "عام", "general"),
                    new UnitSeed("FCI-1-SOFT", "Specialization", "برمجيات", "Soft"),
                    new UnitSeed("FCI-1-BIO", "Specialization", "معلوماتية حيوية", "Bio")),
                new UnitSeed("FCI-2", "Year", "الفرقة الثانية", "Second", true,
                    new UnitSeed("FCI-2-GEN", "Specialization", "عام", "general"),
                    new UnitSeed("FCI-2-SOFT", "Specialization", "برمجيات", "soft"),
                    new UnitSeed("FCI-2-BIO", "Specialization", "معلوماتية حيوية", "bio"))),
            new UnitSeed("FE", "Faculty", "كلية الهندسة", "F-E", false,
                new UnitSeed("FE-1", "Year", "الفرقة الأولى", "First", true,
                    new UnitSeed("FE-1-GEN", "Specialization", "عام", "general")),
                new UnitSeed("FE-2", "Year", "الفرقة الثانية", "Second", true,
                    new UnitSeed("FE-2-ELEC", "Specialization", "كهرباء", "Electric")))));
}