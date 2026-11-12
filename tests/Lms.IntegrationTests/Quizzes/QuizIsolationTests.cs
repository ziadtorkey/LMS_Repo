using System.Reflection;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Quizzes;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Quizzes;

/// <summary>
/// Table-level isolation of the quiz tables (M4a Task 3 and 6, PRD 4.2): another client sees nothing, a sibling unit sees
/// nothing, a parent sees its descendants, no scope sees nothing, and a soft-deleted row disappears for everyone.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class QuizIsolationTests(LmsApiFactory factory)
{
    private sealed record Rows(Guid Quiz, Guid Question, Guid Option, Guid Attempt, Guid Answer);

    private sealed record Table(string Name, Type Type, Func<Rows, Guid> Id, bool SoftDeletable);

    private static readonly Table[] Tables =
    [
        new("Quiz", typeof(Quiz), r => r.Quiz, true),
        new("QuizQuestion", typeof(QuizQuestion), r => r.Question, true),
        new("QuizOption", typeof(QuizOption), r => r.Option, true),
        new("QuizAttempt", typeof(QuizAttempt), r => r.Attempt, false),
        new("QuizAnswer", typeof(QuizAnswer), r => r.Answer, false),
    ];

    private static TestCurrentContext In(AccessTree t, OrgUnit u) => TestCurrentContext.For(t.Org.Id, u.Path);

    private async Task<Rows> AddAsync(OrgUnit unit)
    {
        var academic = await AcademicTestKit.AddAllAsync(factory, unit);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == academic.ClassCourse.Id);
        var plan = new ClassLessonPlan(classCourse, null, null, null);
        db.Add(plan);
        var section = new ClassSection(plan, Guid.NewGuid(), 1, "قسم", "Section");
        db.Add(section);
        var lesson = new ClassLesson(section, Guid.NewGuid(), 1, "درس", "Lesson", null, null, null);
        db.Add(lesson);
        var module = new LessonModule(classCourse, lesson, ModuleKinds.Quiz, 1);
        module.Apply("ص", "Quiz", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.Score, null);
        db.Add(module);
        var quiz = new Quiz(classCourse, lesson.Key);
        quiz.AttachModule(module.Id);
        quiz.Apply(null, null, null, null, "Highest", false, false, null, null, "None", null);
        db.Add(quiz);
        var question = new QuizQuestion(quiz, 1, "SingleChoice", "Q", 1);
        db.Add(question);
        var option = new QuizOption(question, 1, "A", true);
        db.Add(option);
        var attempt = new QuizAttempt(quiz, academic.Student.Id, 1, DateTime.UtcNow, null, 1);
        db.Add(attempt);
        var answer = new QuizAnswer(attempt, question.Id, 1, [option.Id]);
        db.Add(answer);
        await db.SaveChangesAsync();
        return new Rows(quiz.Id, question.Id, option.Id, attempt.Id, answer.Id);
    }

    private async Task<List<Guid>> VisibleAsync(Table table, TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        var method = typeof(QuizIsolationTests).GetMethod(nameof(IdsOf), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(table.Type);
        var ids = await (Task<List<Guid>>)method.Invoke(null, [db])!;
        return ids.Where(among.Contains).Order().ToList();
    }

    private static Task<List<Guid>> IdsOf<T>(LmsDbContext db) where T : class =>
        db.Set<T>().Select(e => EF.Property<Guid>(e, "Id")).ToListAsync();

    [Fact]
    public async Task Every_quiz_table_is_isolated_by_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "CI1A");
        var b = await AccessTestData.SeedAsync(factory, "CI1B");
        var s1 = await AddAsync(a.School1);
        var s2 = await AddAsync(a.School2);
        var other = await AddAsync(b.Root);
        var atRoot = await AddAsync(a.Root);

        foreach (var table in Tables)
        {
            Guid[] all = [table.Id(s1), table.Id(s2), table.Id(other), table.Id(atRoot)];

            Assert.True(new[] { table.Id(other) }.SequenceEqual(await VisibleAsync(table, In(b, b.Root), all)), table.Name + ": other client");
            Assert.True(new[] { table.Id(s1) }.SequenceEqual(await VisibleAsync(table, In(a, a.School1), all)), table.Name + ": school 1");
            Assert.True(new[] { table.Id(s2) }.SequenceEqual(await VisibleAsync(table, In(a, a.School2), all)), table.Name + ": school 2");
            Assert.True(
                new[] { table.Id(s1), table.Id(s2), table.Id(atRoot) }.Order().SequenceEqual(await VisibleAsync(table, In(a, a.Root), all)),
                table.Name + ": parent");
            Assert.Empty(await VisibleAsync(table, TestCurrentContext.For(a.Org.Id, null), all));
            Assert.Empty(await VisibleAsync(table, new TestCurrentContext { UserId = Guid.NewGuid() }, all));
            Assert.True(all.Order().SequenceEqual(await VisibleAsync(table, TestCurrentContext.PlatformAdmin(), all)), table.Name + ": platform admin");
        }
    }

    [Fact]
    public async Task Soft_deleted_quiz_rows_disappear_for_everyone()
    {
        var a = await AccessTestData.SeedAsync(factory, "CI2");
        var s1 = await AddAsync(a.School1);
        var s2 = await AddAsync(a.School2);

        foreach (var table in Tables.Where(t => t.SoftDeletable))
        {
            Guid[] both = [table.Id(s1), table.Id(s2)];
            await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
            {
                db.Remove((await db.FindAsync(table.Type, table.Id(s1)))!);
                await db.SaveChangesAsync();
            }

            Assert.True(new[] { table.Id(s2) }.SequenceEqual(await VisibleAsync(table, In(a, a.Root), both)), table.Name);
            Assert.Empty(await VisibleAsync(table, In(a, a.School1), both));
            Assert.True(new[] { table.Id(s2) }.SequenceEqual(await VisibleAsync(table, TestCurrentContext.PlatformAdmin(), both)), table.Name + ": admin");
        }
    }

    [Fact]
    public async Task Repather_covers_the_quiz_tables()
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        var covered = OrgUnitRepather.DiscoverTargets(db.Model).Select(t => t.ClrType).ToHashSet();

        foreach (var table in Tables)
        {
            Assert.Contains(table.Type, covered);
        }
    }
}
