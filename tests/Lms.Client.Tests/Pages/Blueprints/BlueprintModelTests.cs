using Lms.Client.Pages.Blueprints;
using Lms.Client.Services.Api;

namespace Lms.Client.Tests.Pages.Blueprints;

public sealed class BlueprintModelTests
{
    private static BlueprintModel Seeded()
    {
        var api = new FakeBlueprintsApi();
        return BlueprintModel.From(api.SeedPublished(Guid.NewGuid()));
    }

    [Fact]
    public void Loading_a_blueprint_is_not_dirty_and_keeps_keys_and_revision()
    {
        var model = Seeded();

        Assert.False(model.IsDirty);
        Assert.Equal(2, model.Sections.Count);
        Assert.All(model.Sections, s => Assert.NotNull(s.Key));
        Assert.Equal(1, model.Revision);
    }

    [Fact]
    public void Moving_a_lesson_to_another_section_reorders_and_marks_dirty()
    {
        var model = Seeded();
        var lesson = model.Sections[0].Lessons[0];

        var moved = model.MoveLesson(lesson, model.Sections[1], 1);

        Assert.True(moved);
        Assert.True(model.IsDirty);
        Assert.Single(model.Sections[0].Lessons);
        Assert.Equal(3, model.Sections[1].Lessons.Count);
        Assert.Same(lesson, model.Sections[1].Lessons[1]);
    }

    [Fact]
    public void Dropping_a_lesson_where_it_already_is_changes_nothing()
    {
        var model = Seeded();
        var lesson = model.Sections[0].Lessons[0];

        Assert.False(model.MoveLesson(lesson, model.Sections[0], 0));
        Assert.False(model.IsDirty);
    }

    [Fact]
    public void Stepping_a_lesson_down_swaps_it_with_its_neighbour()
    {
        var model = Seeded();
        var first = model.Sections[0].Lessons[0];

        Assert.True(model.StepLesson(model.Sections[0], first, 1));

        Assert.Same(first, model.Sections[0].Lessons[1]);
    }

    [Fact]
    public void Stepping_the_last_lesson_down_enters_the_next_section_at_the_top()
    {
        var model = Seeded();
        var last = model.Sections[0].Lessons[1];

        Assert.True(model.StepLesson(model.Sections[0], last, 1));

        Assert.Same(last, model.Sections[1].Lessons[0]);
    }

    [Fact]
    public void Stepping_past_the_first_or_last_section_does_nothing()
    {
        var model = Seeded();

        Assert.False(model.StepLesson(model.Sections[0], model.Sections[0].Lessons[0], -1));
        Assert.False(model.StepLesson(model.Sections[1], model.Sections[1].Lessons[1], 1));
        Assert.False(model.IsDirty);
    }

    [Fact]
    public void Sections_can_be_stepped_and_removed()
    {
        var model = Seeded();
        var first = model.Sections[0];

        Assert.True(model.StepSection(first, 1));
        Assert.Same(first, model.Sections[1]);
        Assert.False(model.StepSection(first, 1));

        model.RemoveSection(first);
        Assert.Single(model.Sections);
    }

    [Fact]
    public void Request_carries_the_revision_keeps_old_keys_and_leaves_new_ones_null()
    {
        var model = Seeded();
        var oldKey = model.Sections[0].Key;
        var section = model.AddSection();
        section.TitleAr = "قسم"; section.TitleEn = "Section";
        var lesson = model.AddLesson(section);
        lesson.TitleAr = "درس"; lesson.TitleEn = "Lesson"; lesson.ObjectivesEn = " goals "; lesson.PlannedOffsetDays = 9;

        var request = model.ToRequest();

        Assert.Equal(1, request.Revision);
        Assert.Equal(oldKey, request.Sections[0].Key);
        Assert.Null(request.Sections[2].Key);
        var sent = request.Sections[2].Lessons[0];
        Assert.Null(sent.Key);
        Assert.Equal("goals", sent.ObjectivesEn);
        Assert.Null(sent.ObjectivesAr);
        Assert.Equal(9, sent.PlannedOffsetDays);
    }

    [Fact]
    public void A_tree_is_complete_only_when_every_title_is_filled()
    {
        var model = Seeded();
        Assert.True(model.IsComplete);

        model.AddSection();

        Assert.False(model.IsComplete);
    }

    [Fact]
    public void Marking_saved_clears_dirty_and_takes_the_new_revision()
    {
        var api = new FakeBlueprintsApi();
        var dto = api.SeedPublished(Guid.NewGuid());
        var model = BlueprintModel.From(dto);
        model.AddSection();

        model.MarkSaved(dto with { Summary = dto.Summary with { Revision = 5 } });

        Assert.False(model.IsDirty);
        Assert.Equal(5, model.Revision);
    }
}
