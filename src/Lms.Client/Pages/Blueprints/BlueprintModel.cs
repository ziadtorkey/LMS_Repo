using Lms.Shared.Blueprints;

namespace Lms.Client.Pages.Blueprints;

/// <summary>One lesson of the tree being edited. <c>Key</c> is null for a lesson that was added in this session.</summary>
public sealed class LessonRow
{
    public Guid? Key { get; set; }
    public string TitleAr { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public string ObjectivesAr { get; set; } = "";
    public string ObjectivesEn { get; set; } = "";
    public int? PlannedOffsetDays { get; set; }

    public bool HasTitles => !string.IsNullOrWhiteSpace(TitleAr) && !string.IsNullOrWhiteSpace(TitleEn);
}

/// <summary>One section (week, unit or chapter) of the tree being edited.</summary>
public sealed class SectionRow
{
    public Guid? Key { get; set; }
    public string TitleAr { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public List<LessonRow> Lessons { get; } = [];

    public bool HasTitles => !string.IsNullOrWhiteSpace(TitleAr) && !string.IsNullOrWhiteSpace(TitleEn);
}

/// <summary>
/// The editable copy of a blueprint draft: sections and lessons with move operations, a dirty flag and the save request.
/// Positions are list positions; the server numbers them.
/// </summary>
public sealed class BlueprintModel
{
    public List<SectionRow> Sections { get; } = [];
    public int Revision { get; private set; }

    /// <summary>True once the tree differs from the last load or save.</summary>
    public bool IsDirty { get; private set; }

    public static BlueprintModel From(BlueprintDto dto)
    {
        var model = new BlueprintModel { Revision = dto.Summary.Revision };
        foreach (var section in dto.Sections.OrderBy(s => s.Order))
        {
            var row = new SectionRow { Key = section.Key, TitleAr = section.TitleAr, TitleEn = section.TitleEn };
            foreach (var lesson in section.Lessons.OrderBy(l => l.Order))
                row.Lessons.Add(new LessonRow
                {
                    Key = lesson.Key, TitleAr = lesson.TitleAr, TitleEn = lesson.TitleEn, ObjectivesAr = lesson.ObjectivesAr ?? "",
                    ObjectivesEn = lesson.ObjectivesEn ?? "", PlannedOffsetDays = lesson.PlannedOffsetDays,
                });
            model.Sections.Add(row);
        }
        return model;
    }

    /// <summary>Marks the tree changed (called after an edit made directly on a row, e.g. a lesson dialog).</summary>
    public void Touch() => IsDirty = true;

    /// <summary>Called after a successful save: the saved tree is the new baseline.</summary>
    public void MarkSaved(BlueprintDto saved)
    {
        Revision = saved.Summary.Revision;
        IsDirty = false;
    }

    public SectionRow AddSection()
    {
        var section = new SectionRow();
        Sections.Add(section);
        IsDirty = true;
        return section;
    }

    public LessonRow AddLesson(SectionRow section)
    {
        var lesson = new LessonRow();
        section.Lessons.Add(lesson);
        IsDirty = true;
        return lesson;
    }

    public void RemoveSection(SectionRow section)
    {
        if (Sections.Remove(section)) IsDirty = true;
    }

    public void RemoveLesson(SectionRow section, LessonRow lesson)
    {
        if (section.Lessons.Remove(lesson)) IsDirty = true;
    }

    /// <summary>Moves a section to <paramref name="newIndex"/> (clamped). Returns false when nothing changed.</summary>
    public bool MoveSection(SectionRow section, int newIndex)
    {
        var from = Sections.IndexOf(section);
        if (from < 0) return false;
        newIndex = Math.Clamp(newIndex, 0, Sections.Count - 1);
        if (newIndex == from) return false;
        Sections.RemoveAt(from);
        Sections.Insert(newIndex, section);
        IsDirty = true;
        return true;
    }

    /// <summary>Moves a lesson to another position, possibly in another section. Returns false when nothing changed.</summary>
    public bool MoveLesson(LessonRow lesson, SectionRow target, int newIndex)
    {
        var source = Sections.FirstOrDefault(s => s.Lessons.Contains(lesson));
        if (source is null || !Sections.Contains(target)) return false;
        var from = source.Lessons.IndexOf(lesson);
        source.Lessons.RemoveAt(from);
        newIndex = Math.Clamp(newIndex, 0, target.Lessons.Count);
        target.Lessons.Insert(newIndex, lesson);
        if (ReferenceEquals(source, target) && newIndex == from) return false;
        IsDirty = true;
        return true;
    }

    /// <summary>Moves a lesson one step up or down; at the edge of a section it crosses into the neighbouring section.</summary>
    public bool StepLesson(SectionRow section, LessonRow lesson, int delta)
    {
        var index = section.Lessons.IndexOf(lesson);
        if (index < 0) return false;
        var target = index + delta;
        if (target >= 0 && target < section.Lessons.Count) return MoveLesson(lesson, section, target);

        var s = Sections.IndexOf(section) + Math.Sign(delta);
        if (s < 0 || s >= Sections.Count) return false;
        var neighbour = Sections[s];
        return MoveLesson(lesson, neighbour, delta < 0 ? neighbour.Lessons.Count : 0);
    }

    public bool StepSection(SectionRow section, int delta) => MoveSection(section, Sections.IndexOf(section) + delta);

    /// <summary>Whether every section and lesson has both titles (the server also requires lessons in every section to publish).</summary>
    public bool IsComplete => Sections.All(s => s.HasTitles && s.Lessons.All(l => l.HasTitles));

    public SaveBlueprintDraftRequest ToRequest() => new(Revision, Sections.Select(s => new BlueprintSectionInput(s.Key, s.TitleAr.Trim(), s.TitleEn.Trim(),
        s.Lessons.Select(l => new BlueprintLessonInput(l.Key, l.TitleAr.Trim(), l.TitleEn.Trim(),
            string.IsNullOrWhiteSpace(l.ObjectivesAr) ? null : l.ObjectivesAr.Trim(),
            string.IsNullOrWhiteSpace(l.ObjectivesEn) ? null : l.ObjectivesEn.Trim(), l.PlannedOffsetDays)).ToList())).ToList());
}
