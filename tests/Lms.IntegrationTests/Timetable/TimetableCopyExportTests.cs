using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Lms.Domain.AcademicYears;
using Lms.Domain.Teaching;
using Lms.Domain.Timetable;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Timetable;

/// <summary>M5 Task 14 part 2: copy-term into drafts with a skipped list (security row 5.12) and the Excel export (row 5.13, escaped cells).</summary>
[Collection(ApiCollection.Name)]
public sealed class TimetableCopyExportTests(LmsApiFactory factory)
{
    private async Task<Term> AddTermAsync(TimetableWorld w)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var term = new Term(w.Year, "فصل ٢", "Term 2", w.Year.StartDate.AddMonths(5), w.Year.StartDate.AddMonths(9));
        db.Add(term);
        await db.SaveChangesAsync();
        return term;
    }

    private async Task<Lms.Domain.Classes.Class> AcademicTestKit_AddClassAsync(TimetableWorld w) =>
        await Lms.IntegrationTests.Academic.AcademicTestKit.AddClassAsync(factory, w.Unit, w.Year);

    private static CopyTermRequest Copy(TimetableWorld w, Term target) => new(w.Term.Id, target.Id);

    [Fact]
    public async Task Copy_term_creates_drafts_and_lists_skipped_rows()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CT1");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);
        var target = await AddTermAsync(w);
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Sunday, w.First, "Lab"));
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Science, w.ScienceTeacher, DayOfWeek.Monday, w.First));
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Tuesday, w.Second));
        // the target already holds a slot at Monday / first period, and the science teacher is no longer assigned
        await CreateSlotAsync(factory, admin, w.Class.Id, new SaveSlotRequest(w.Math.Id, target.Id, w.MathTeacher, DayOfWeek.Monday, w.First.Id, null, null));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.RemoveRange(await db.Set<ClassCourseTeacher>().Where(t => t.ClassCourseId == w.Science.Id).ToListAsync());
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableCopyTerm(w.Class.Id), admin, Copy(w, target));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CopyTermResultDto>(Web))!;
        Assert.Equal(2, result.Copied);
        Assert.Equal([CopySkipReasons.TeacherMissing], result.Skipped.Select(s => s.Reason));
        await using var read = factory.CreateDbContext(TestCurrentContext.For(tree.Org.Id, tree.Root.Path));
        var copied = await read.Set<TimetableSlot>().Where(s => s.ClassId == w.Class.Id && s.TermId == target.Id).ToListAsync();
        Assert.Equal(3, copied.Count); // the existing one and the two copies
        Assert.All(copied.Where(s => s.Day != DayOfWeek.Monday), s => Assert.Equal(TimetableSlot.DraftState, s.State));
        Assert.Contains(copied, s => s.Day == DayOfWeek.Sunday && s.RoomName == "Lab");
    }

    [Fact]
    public async Task A_slot_where_the_target_already_has_one_is_skipped_as_SlotExists()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CT2");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);
        var target = await AddTermAsync(w);
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Sunday, w.First));
        await CreateSlotAsync(factory, admin, w.Class.Id, new SaveSlotRequest(w.Science.Id, target.Id, w.ScienceTeacher, DayOfWeek.Sunday, w.First.Id, null, null));

        var result = (await (await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableCopyTerm(w.Class.Id), admin, Copy(w, target))).Content.ReadFromJsonAsync<CopyTermResultDto>(Web))!;

        Assert.Equal(0, result.Copied);
        Assert.Equal([CopySkipReasons.SlotExists], result.Skipped.Select(s => s.Reason));
        Assert.False(string.IsNullOrEmpty(result.Skipped[0].CourseNameEn));
    }

    [Fact]
    public async Task Copy_never_takes_slots_of_another_class_and_refuses_foreign_or_same_terms()
    {
        var a = await AccessTestData.SeedAsync(factory, "CT3A");
        var b = await AccessTestData.SeedAsync(factory, "CT3B");
        var admin = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, a.School1);
        var wb = await NewWorldAsync(factory, b.School1); // another client, with its own term
        var other = await AcademicTestKit_AddClassAsync(w);
        var (otherCourse, otherTeacher) = await AddCourseWithTeacherAsync(factory, a.School1, other);
        var target = await AddTermAsync(w);
        await CreateSlotAsync(factory, admin, other.Id, Slot(w, otherCourse, otherTeacher, DayOfWeek.Sunday, w.First));

        var empty = (await (await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableCopyTerm(w.Class.Id), admin, Copy(w, target))).Content.ReadFromJsonAsync<CopyTermResultDto>(Web))!;
        var same = await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableCopyTerm(w.Class.Id), admin, new CopyTermRequest(w.Term.Id, w.Term.Id));
        var foreignSource = await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableCopyTerm(w.Class.Id), admin, new CopyTermRequest(wb.Term.Id, target.Id));
        var foreignTarget = await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableCopyTerm(w.Class.Id), admin, new CopyTermRequest(w.Term.Id, wb.Term.Id));

        Assert.Equal((0, 0), (empty.Copied, empty.Skipped.Count)); // the other class's slot is not this class's
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignSource.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignTarget.StatusCode);
    }

    [Fact]
    public async Task Copy_and_export_of_another_school_or_client_are_404_and_a_teacher_cannot_copy()
    {
        var a = await AccessTestData.SeedAsync(factory, "CT4A");
        var b = await AccessTestData.SeedAsync(factory, "CT4B");
        var admin = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school2Admin = await AccessTestData.AssignAsync(factory, a, a.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var adminB = await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacher = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var w = await NewWorldAsync(factory, a.School1);
        var target = await AddTermAsync(w);
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Sunday, w.First));

        foreach (var who in new[] { school2Admin, adminB })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableCopyTerm(w.Class.Id), who, Copy(w, target))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.ClassTimetableExport(w.Class.Id, w.Term.Id), who)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableCopyTerm(w.Class.Id), teacher, Copy(w, target))).StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.School1.Path));
        Assert.False(await db.Set<TimetableSlot>().AnyAsync(s => s.TermId == target.Id)); // nothing was copied by the refused calls
    }

    private static async Task<XLWorkbook> OpenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        return new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task Export_escapes_cells_and_holds_only_this_class_and_term()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CT5");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);
        var other = await AcademicTestKit_AddClassAsync(w);
        var (otherCourse, otherTeacher) = await AddCourseWithTeacherAsync(factory, tree.School1, other);
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Sunday, w.First, "=HYPERLINK(\"http://evil.test\",\"x\")"));
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Science, w.ScienceTeacher, DayOfWeek.Monday, w.Second, "@SUM(A1)"));
        await CreateSlotAsync(factory, admin, other.Id, Slot(w, otherCourse, otherTeacher, DayOfWeek.Tuesday, w.First, "OTHER-CLASS-ROOM"));

        using var workbook = await OpenAsync(await SendAsync(factory, HttpMethod.Get, Routes.ClassTimetableExport(w.Class.Id, w.Term.Id), admin));

        var sheet = workbook.Worksheet(1);
        var cells = sheet.CellsUsed().ToList();
        Assert.All(cells, c =>
        {
            Assert.False(c.HasFormula, c.Address.ToString());
            Assert.Equal(XLDataType.Text, c.DataType);
            Assert.False(c.GetString().StartsWith('=') || c.GetString().StartsWith('@') || c.GetString().StartsWith('+') || c.GetString().StartsWith('-'), c.GetString());
        });
        var text = string.Join("\n", cells.Select(c => c.GetString()));
        Assert.Contains("=HYPERLINK", text); // inside a cell that starts with the course name, so it is plain text
        Assert.Contains("@SUM(A1)", text); // a room text is inside brackets, so it cannot start a cell
        Assert.DoesNotContain("OTHER-CLASS-ROOM", text);
    }

    [Fact]
    public async Task Names_that_look_like_formulas_are_neutralised_in_every_text_cell()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CT6");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Sunday, w.First));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cls = await db.Set<Lms.Domain.Classes.Class>().IgnoreQueryFilters().SingleAsync(c => c.Id == w.Class.Id);
            db.Entry(cls).Property(c => c.NameEn).CurrentValue = "=1+1";
            var period = await db.Set<Period>().IgnoreQueryFilters().SingleAsync(p => p.Id == w.First.Id);
            db.Entry(period).Property(p => p.NameEn).CurrentValue = "+cmd|' /C calc'!A0";
            await db.SaveChangesAsync();
        }

        using var workbook = await OpenAsync(await SendAsync(factory, HttpMethod.Get, Routes.ClassTimetableExport(w.Class.Id, w.Term.Id), admin));

        var sheet = workbook.Worksheet(1);
        // SpreadsheetCell.Safe prefixes an apostrophe; Excel keeps it as the quote prefix of the cell, so the value shows as typed but is text, never a formula.
        foreach (var cell in new[] { sheet.Cell(1, 1), sheet.Cell(3, 1) })
        {
            Assert.Equal(XLDataType.Text, cell.DataType);
            Assert.False(cell.HasFormula);
            Assert.True(cell.Style.IncludeQuotePrefix, cell.GetString());
        }

        Assert.Equal("=1+1", sheet.Cell(1, 1).GetString());
        Assert.All(sheet.CellsUsed(), c => Assert.False(c.HasFormula));
    }

    [Fact]
    public async Task Drafts_are_exported_only_for_staff_who_can_manage()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CT7");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, w.MathTeacher);
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Sunday, w.First, "DRAFT-ROOM"));

        using var asAdmin = await OpenAsync(await SendAsync(factory, HttpMethod.Get, Routes.ClassTimetableExport(w.Class.Id, w.Term.Id), admin));
        using var asTeacher = await OpenAsync(await SendAsync(factory, HttpMethod.Get, Routes.ClassTimetableExport(w.Class.Id, w.Term.Id), teacher));

        Assert.Contains(asAdmin.Worksheet(1).CellsUsed(), c => c.GetString().Contains("DRAFT-ROOM"));
        Assert.DoesNotContain(asTeacher.Worksheet(1).CellsUsed(), c => c.GetString().Contains("DRAFT-ROOM"));
    }
}
