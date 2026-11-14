using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Grading;

namespace Lms.Client.Services.Grading;

/// <summary>
/// In-memory <see cref="IGradebookApi"/> mimicking the rules the UI relies on: automatic items start hidden, students and guardians see published
/// items only, a stale row version is a 409 <c>concurrency_conflict</c>, a locked gradebook refuses writes, an out-of-range score is 422, an override needs
/// a reason, and publishing under a provisional scale needs the acknowledgement.
/// <para><b>The calculation here is a demo engine</b> (a simple weighted average with the Normalise policy): the real engine lives on the server and the
/// UI never computes a grade, so nothing may depend on these numbers beyond showing them.</para>
/// </summary>
public sealed class FakeGradebookApi : IGradebookApi
{
    /// <summary>True: the numbers computed by this fake are demo values, not the server's engine.</summary>
    public const bool IsDemoEngine = true;

    public static readonly Guid DemoClassCourseId = Lms.Client.Services.Api.FakeLessonPlansApi.ArabicClassCourseId;
    public static readonly Guid DemoTermId = Guid.Parse("00000000-0000-0000-0000-0000000e0010");
    public static readonly Guid HomeworkCategoryId = Guid.Parse("00000000-0000-0000-0000-0000000e0021");
    public static readonly Guid ExamsCategoryId = Guid.Parse("00000000-0000-0000-0000-0000000e0022");
    public static readonly Guid ManualItemId = Guid.Parse("00000000-0000-0000-0000-0000000e0031");
    public static readonly Guid AutoItemId = Guid.Parse("00000000-0000-0000-0000-0000000e0032");
    public static readonly Guid[] StudentIds =
    [
        Guid.Parse("00000000-0000-0000-0000-0000000e0041"), Guid.Parse("00000000-0000-0000-0000-0000000e0042"), Guid.Parse("00000000-0000-0000-0000-0000000e0043"),
    ];

    private readonly FakeGradingScalesApi _scales;
    private readonly Guid _scaleId;
    private readonly List<GradeCategoryDto> _categories =
    [
        new(HomeworkCategoryId, "الواجبات", "Homework", 30m, 1, false),
        new(ExamsCategoryId, "الاختبارات", "Exams", 70m, 2, false),
    ];
    private readonly List<GradeItemDto> _items =
    [
        new(ManualItemId, ExamsCategoryId, "اختبار قصير", "Quiz 1", null, null, true, 20m, PublishStates.Hidden, null, null, 1),
        new(AutoItemId, HomeworkCategoryId, "الواجب الأول", "Assignment 1", "Assignment", Guid.Parse("00000000-0000-0000-0000-0000000e0051"), false, 10m, PublishStates.Hidden, null, null, 2),
    ];
    private readonly Dictionary<(Guid Student, Guid Item), GradeCellDto> _cells = [];
    private readonly Dictionary<Guid, long> _versions = StudentIds.ToDictionary(s => s, _ => 1L);
    private readonly Dictionary<Guid, string> _names = new()
    {
        [StudentIds[0]] = "سارة أحمد|Sara Ahmed", [StudentIds[1]] = "خالد علي|Khaled Ali", [StudentIds[2]] = "ليلى حسن|Layla Hassan",
    };

    /// <summary>When true the gradebook is locked: writes are 409 <c>gradebook_locked</c>.</summary>
    public bool Locked { get; set; }

    /// <summary>The caller can manage / publish (a teacher). False models a read-only viewer.</summary>
    public bool CanManage { get; set; } = true;

    /// <summary>The last grid query the page sent (term and paging), for tests.</summary>
    public GradebookQuery? LastQuery { get; private set; }

    /// <summary>How many times the staff preview of a student was read.</summary>
    public int StudentViewCalls { get; private set; }

    /// <summary>Models a course with no students placed, or with no grade items yet (the empty states of the grid).</summary>
    public bool NoStudents { get; set; }

    public bool NoItems { get; set; }

    /// <summary>When set, the next <see cref="UpsertEntriesAsync"/> fails once with it (a failed save: revert, text, retry).</summary>
    public Exception? NextUpsertFailure { get; set; }

    /// <summary>When true, the next <see cref="UpsertEntriesAsync"/> answers 409 <c>concurrency_conflict</c> as if someone else saved the row first.</summary>
    public bool ConflictOnce { get; set; }

    /// <summary>How many save calls reached the fake (a test asserts that an unchanged cell sends nothing).</summary>
    public int UpsertCalls { get; private set; }

    /// <summary>When set, the next <see cref="GetAsync"/> fails once (a load failure with Try again).</summary>
    public Exception? NextFailure { get; set; }

    /// <summary>Lets a test reshape the answer of <see cref="GetAsync"/> (a different total, a locked state, values the page must show as given).</summary>
    public Func<GradebookDto, GradebookQuery, GradebookDto>? Transform { get; set; }

    /// <summary>Sets one cell directly, in any state (the demo cannot reach every state through the rules).</summary>
    public void SetCell(Guid studentId, GradeCellDto cell) => _cells[(studentId, cell.ItemId)] = cell;

    /// <summary>The last publish request, for tests.</summary>
    public PublishItemsRequest? LastPublish { get; private set; }

    /// <summary>The last entries request, for tests.</summary>
    public UpsertEntriesRequest? LastEntries { get; private set; }

    public FakeGradebookApi() : this(new FakeGradingScalesApi()) { }

    public FakeGradebookApi(FakeGradingScalesApi scales)
    {
        _scales = scales;
        var preset = scales.CopyPresetAsync(FakeGradingScalesApi.DemoUnitId, new CopyScalePresetRequest("sa-percentage")).GetAwaiter().GetResult();
        _scaleId = preset.Id;
        scales.MarkInUse(_scaleId);
        _cells[(StudentIds[0], AutoItemId)] = new GradeCellDto(AutoItemId, 9m, GradeCellStates.Event, null);
        _cells[(StudentIds[0], ManualItemId)] = new GradeCellDto(ManualItemId, 18m, GradeCellStates.Manual, null);
        _cells[(StudentIds[1], AutoItemId)] = new GradeCellDto(AutoItemId, 6m, GradeCellStates.Event, null);
    }

    private static ApiProblemException Problem(int status, string code) => new(status, code, null, null);

    private GradingScaleDto Scale => _scales.GetScaleAsync(FakeGradingScalesApi.DemoUnitId, _scaleId).GetAwaiter().GetResult();

    private void EnsureCourse(Guid classCourseId)
    {
        if (classCourseId != DemoClassCourseId) throw Problem(404, ProblemCodes.not_found);
    }

    private void EnsureWritable(Guid classCourseId)
    {
        EnsureCourse(classCourseId);
        if (!CanManage) throw Problem(403, "MissingPermission");
        if (Locked) throw Problem(409, ProblemCodes.gradebook_locked);
    }

    private GradeItemDto Item(Guid id) => _items.FirstOrDefault(i => i.Id == id) ?? throw Problem(404, ProblemCodes.not_found);

    private GradeCellDto Cell(Guid student, Guid item) =>
        _cells.TryGetValue((student, item), out var c) ? c : new GradeCellDto(item, null, GradeCellStates.Empty, null);

    private (decimal? Percent, string? Code, string? LabelAr, string? LabelEn, decimal? Points) Final(IEnumerable<(Guid Category, decimal Points, decimal Max)> scored)
    {
        var scoredList = scored.ToList();
        var weighted = 0m;
        var weights = 0m;
        foreach (var group in scoredList.GroupBy(s => s.Category))
        {
            var category = _categories.First(c => c.Id == group.Key);
            var max = group.Sum(g => g.Max);
            if (max == 0m) continue;
            weighted += group.Sum(g => g.Points) / max * 100m * category.Weight;
            weights += category.Weight;
        }

        if (weights == 0m) return (null, null, null, null, null);
        var percent = Math.Round(weighted / weights, 2);
        var band = Scale.Bands.FirstOrDefault(b => percent >= b.MinPercent && percent <= b.MaxPercent);
        return (percent, band?.Code, band?.LabelAr, band?.LabelEn, band?.Points);
    }

    private GradeRowDto Row(Guid student)
    {
        var cells = _items.Select(i => Cell(student, i.Id)).ToList();
        var scored = cells.Where(c => c.Points is not null).Select(c => (Item(c.ItemId).CategoryId, c.Points!.Value, Item(c.ItemId).MaxPoints)).ToList();
        var results = _categories.Select(cat =>
        {
            var inCat = scored.Where(s => s.CategoryId == cat.Id).ToList();
            var max = inCat.Sum(s => s.MaxPoints);
            var percent = max == 0m ? (decimal?)null : Math.Round(inCat.Sum(s => s.Item2) / max * 100m, 2);
            return new GradeCategoryResultDto(cat.Id, percent, percent is null ? null : Math.Round(percent.Value * cat.Weight / 100m, 2));
        }).ToList();
        var f = Final(scored);
        var names = _names[student].Split('|');
        return new GradeRowDto(student, names[0], names[1], "S-" + (Array.IndexOf(StudentIds, student) + 1).ToString("D4"), cells, results, f.Percent, f.Code, f.LabelAr, f.LabelEn, f.Points, _versions[student]);
    }

    public Task<GradebookDto> GetAsync(Guid classCourseId, GradebookQuery query, CancellationToken ct = default)
    {
        EnsureCourse(classCourseId);
        LastQuery = query;
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            throw failure;
        }

        var all = NoStudents ? [] : StudentIds.Select(Row).ToList();
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, GradingLimits.GridPageSizeMax);
        var total = _categories.Sum(c => c.Weight);
        var now = DateTime.UtcNow;
        var rows = new PagedResult<GradeRowDto>([.. all.Skip((page - 1) * size).Take(size)], all.Count, page, size);
        var config = new GradebookConfigDto(_scaleId, Scale.NameAr, Scale.NameEn, Scale.Provisional, GradingLimits.DecimalPlacesDefault,
            CategoryMethods.PointsWeighted, MissingPolicies.Normalise, RoundingModes.HalfUp);
        var book = new GradebookDto(Guid.Parse("00000000-0000-0000-0000-0000000e0061"), classCourseId, DemoTermId, "الفصل الأول", "Term 1", config,
            [.. _categories], NoItems ? [] : [.. _items.OrderBy(i => i.Order)], rows, total == 100m, total, Locked ? GradebookStates.Locked : GradebookStates.Open,
            Locked ? now : null, 0, CanManage, CanManage, now, now);
        return Task.FromResult(Transform is null ? book : Transform(book, query));
    }

    public Task<GradebookConfigDto> SaveConfigAsync(Guid classCourseId, SaveGradebookConfigRequest request, CancellationToken ct = default)
    {
        EnsureWritable(classCourseId);
        if (request.DecimalPlaces is < GradingLimits.DecimalPlacesMin or > GradingLimits.DecimalPlacesMax
            || !CategoryMethods.All.Contains(request.CategoryMethod) || !MissingPolicies.All.Contains(request.MissingPolicy))
            throw Problem(400, ProblemCodes.validation_failed);
        return Task.FromResult(new GradebookConfigDto(request.ScaleId ?? _scaleId, Scale.NameAr, Scale.NameEn, Scale.Provisional, request.DecimalPlaces,
            request.CategoryMethod, request.MissingPolicy, RoundingModes.HalfUp));
    }

    public Task<IReadOnlyList<GradeCategoryDto>> SaveCategoriesAsync(Guid classCourseId, SaveGradeCategoriesRequest request, CancellationToken ct = default)
    {
        EnsureWritable(classCourseId);
        if (request.Categories.Any(c => c.Weight is < GradingLimits.WeightMin or > GradingLimits.WeightMax)) throw Problem(400, ProblemCodes.weights_invalid);
        _categories.Clear();
        _categories.AddRange(request.Categories.Select((c, i) => new GradeCategoryDto(c.Id ?? Guid.NewGuid(), c.NameAr, c.NameEn, c.Weight, i + 1, false)));
        return Task.FromResult<IReadOnlyList<GradeCategoryDto>>([.. _categories]);
    }

    private void ValidateItem(Guid categoryId, string titleAr, string titleEn, decimal maxPoints)
    {
        if (_categories.All(c => c.Id != categoryId) || string.IsNullOrWhiteSpace(titleAr) || string.IsNullOrWhiteSpace(titleEn)
            || maxPoints is < GradingLimits.MaxPointsMin or > GradingLimits.MaxPointsMax)
            throw Problem(400, ProblemCodes.validation_failed);
    }

    public Task<GradeItemDto> CreateItemAsync(Guid classCourseId, CreateGradeItemRequest request, CancellationToken ct = default)
    {
        EnsureWritable(classCourseId);
        ValidateItem(request.CategoryId, request.TitleAr, request.TitleEn, request.MaxPoints);
        var item = new GradeItemDto(Guid.NewGuid(), request.CategoryId, request.TitleAr, request.TitleEn, null, null, true, request.MaxPoints,
            PublishStates.Hidden, null, null, _items.Count + 1);
        _items.Add(item);
        return Task.FromResult(item);
    }

    public Task<GradeItemDto> UpdateItemAsync(Guid classCourseId, Guid itemId, UpdateGradeItemRequest request, CancellationToken ct = default)
    {
        EnsureWritable(classCourseId);
        var current = Item(itemId);
        ValidateItem(request.CategoryId, request.TitleAr, request.TitleEn, request.MaxPoints);
        var next = current with { CategoryId = request.CategoryId, TitleAr = request.TitleAr, TitleEn = request.TitleEn, MaxPoints = request.MaxPoints, Order = request.Order };
        _items[_items.IndexOf(current)] = next;
        return Task.FromResult(next);
    }

    public Task DeleteItemAsync(Guid classCourseId, Guid itemId, CancellationToken ct = default)
    {
        EnsureWritable(classCourseId);
        var current = Item(itemId);
        if (!current.IsManual) throw Problem(409, ProblemCodes.validation_failed);
        _items.Remove(current);
        foreach (var key in _cells.Keys.Where(k => k.Item == itemId).ToList()) _cells.Remove(key);
        return Task.CompletedTask;
    }

    public Task<GradeRowDto> UpsertEntriesAsync(Guid classCourseId, UpsertEntriesRequest request, CancellationToken ct = default)
    {
        EnsureWritable(classCourseId);
        LastEntries = request;
        UpsertCalls++;
        if (NextUpsertFailure is { } fail)
        {
            NextUpsertFailure = null;
            throw fail;
        }

        if (ConflictOnce)
        {
            ConflictOnce = false;
            _versions[request.StudentId] = _versions.GetValueOrDefault(request.StudentId) + 1; // someone else saved first
            throw Problem(409, ProblemCodes.concurrency_conflict);
        }

        if (!_versions.TryGetValue(request.StudentId, out var version)) throw Problem(404, ProblemCodes.not_found);
        if (request.Version != version) throw Problem(409, ProblemCodes.concurrency_conflict);
        // Validate everything first so a bad change leaves the row untouched.
        foreach (var change in request.Changes) Validate(change);
        foreach (var change in request.Changes) Apply(request.StudentId, change);
        _versions[request.StudentId] = version + 1;
        return Task.FromResult(Row(request.StudentId));
    }

    private void Validate(GradeEntryChange change)
    {
        var item = Item(change.ItemId);
        if (!GradeEntryActions.All.Contains(change.Action)) throw Problem(400, ProblemCodes.validation_failed);
        if (change.Action is GradeEntryActions.Set or GradeEntryActions.Override)
        {
            if (change.Points is not { } p || p < 0m || p > item.MaxPoints) throw Problem(422, ProblemCodes.grade_out_of_range);
        }

        if (change.Action == GradeEntryActions.Override
            && (change.Reason is null || change.Reason.Length is < GradingLimits.ReasonMinLength or > GradingLimits.ReasonMaxLength))
            throw Problem(422, ProblemCodes.grade_override_reason_required);
    }

    private void Apply(Guid student, GradeEntryChange change)
    {
        var key = (student, change.ItemId);
        switch (change.Action)
        {
            case GradeEntryActions.Set:
                _cells[key] = new GradeCellDto(change.ItemId, change.Points, Cell(student, change.ItemId).State == GradeCellStates.Event ? GradeCellStates.Override : GradeCellStates.Manual, null);
                break;
            case GradeEntryActions.Override:
                _cells[key] = new GradeCellDto(change.ItemId, change.Points, GradeCellStates.Override, change.Reason);
                break;
            case GradeEntryActions.KeepOverride:
                _cells[key] = Cell(student, change.ItemId) with { State = GradeCellStates.Override };
                break;
            case GradeEntryActions.DropOverride:
                _cells[key] = Cell(student, change.ItemId) with { State = GradeCellStates.Event, Reason = null };
                break;
            case GradeEntryActions.Excuse:
                _cells[key] = new GradeCellDto(change.ItemId, null, GradeCellStates.Excused, change.Reason);
                break;
            default:
                _cells.Remove(key);
                break;
        }
    }

    public Task<PublishResultDto> PublishAsync(Guid classCourseId, PublishItemsRequest request, CancellationToken ct = default)
    {
        EnsureWritable(classCourseId);
        LastPublish = request;
        if (!PublishModes.All.Contains(request.Mode) || request.ItemIds.Count == 0) throw Problem(400, ProblemCodes.validation_failed);
        if (request.Mode != PublishModes.Hide && Scale.Provisional && !request.AcknowledgeProvisionalScale) throw Problem(409, ProblemCodes.scale_provisional);
        if (request.Mode == PublishModes.Schedule && (request.AtUtc is not { } at || at <= DateTime.UtcNow)) throw Problem(400, ProblemCodes.validation_failed);
        var now = DateTime.UtcNow;
        foreach (var id in request.ItemIds)
        {
            var item = Item(id);
            _items[_items.IndexOf(item)] = request.Mode switch
            {
                PublishModes.Now => item with { PublishState = PublishStates.Published, PublishAtUtc = null, PublishedAtUtc = now },
                PublishModes.Schedule => item with { PublishState = PublishStates.Scheduled, PublishAtUtc = request.AtUtc, PublishedAtUtc = null },
                _ => item with { PublishState = PublishStates.Hidden, PublishAtUtc = null, PublishedAtUtc = null },
            };
        }

        return Task.FromResult(new PublishResultDto(request.ItemIds.Count, StudentIds.Length, request.Mode == PublishModes.Now ? StudentIds.Length : 0));
    }

    private StudentCourseGradesDto CourseGrades(Guid student)
    {
        var published = _items.Where(i => i.PublishState == PublishStates.Published).ToList();
        var items = published.Select(i =>
        {
            var cell = Cell(student, i.Id);
            return new PublishedGradeItemDto(i.Id, i.TitleAr, i.TitleEn, i.CategoryId, cell.Points, i.MaxPoints, cell.State == GradeCellStates.Excused, i.PublishedAtUtc ?? DateTime.UtcNow);
        }).ToList();
        var scored = items.Where(i => i.Points is not null).Select(i => (i.CategoryId!.Value, i.Points!.Value, i.MaxPoints)).ToList();
        var categories = _categories.Select(c =>
        {
            var inCat = scored.Where(s => s.Item1 == c.Id).ToList();
            var max = inCat.Sum(s => s.MaxPoints);
            return new StudentGradeCategoryDto(c.Id, c.NameAr, c.NameEn, c.Weight, max == 0m ? null : Math.Round(inCat.Sum(s => s.Item2) / max * 100m, 2));
        }).ToList();
        var f = Final(scored);
        var scale = Scale;
        var legend = new BandLegendDto(scale.NameAr, scale.NameEn, scale.Kind,
            [.. scale.Bands.Select(b => new BandLegendEntryDto(b.Code, b.MinPercent, b.MaxPercent, b.LabelAr, b.LabelEn, b.DescriptorAr, b.DescriptorEn, b.Points))]);
        return new StudentCourseGradesDto(DemoClassCourseId, Guid.Parse("00000000-0000-0000-0000-0000000e0061"), "اللغة العربية", "Arabic", DemoTermId, "الفصل الأول", "Term 1",
            GradingLimits.DecimalPlacesDefault, items, categories, f.Percent, f.Code, f.LabelAr, f.LabelEn, f.Points, legend);
    }

    private StudentGradesDto Grades(Guid student)
    {
        var names = _names[student].Split('|');
        var now = DateTime.UtcNow;
        return new StudentGradesDto(student, names[0], names[1], [CourseGrades(student)], now, now);
    }

    public Task<StudentCourseGradesDto> GetStudentViewAsync(Guid classCourseId, Guid studentId, Guid? termId = null, CancellationToken ct = default)
    {
        EnsureCourse(classCourseId);
        if (!CanManage) throw Problem(403, "MissingPermission");
        StudentViewCalls++;
        return Task.FromResult(CourseGrades(_versions.ContainsKey(studentId) ? studentId : throw Problem(404, ProblemCodes.not_found)));
    }

    /// <summary>The student the "me" and guardian reads answer for.</summary>
    public Guid CurrentStudentId { get; set; } = StudentIds[0];

    public Task<StudentGradesDto> GetMyGradesAsync(Guid? termId = null, CancellationToken ct = default) => Task.FromResult(Grades(CurrentStudentId));

    public Task<StudentGradesDto> GetStudentGradesAsync(Guid studentId, Guid? termId = null, CancellationToken ct = default) =>
        Task.FromResult(Grades(_versions.ContainsKey(studentId) ? studentId : throw Problem(404, ProblemCodes.not_found)));

    public Task<(Stream Content, string FileName)> ExportItemAsync(Guid classCourseId, Guid itemId, CancellationToken ct = default)
    {
        EnsureCourse(classCourseId);
        var item = Item(itemId);
        Stream stream = new MemoryStream("student number,points\n"u8.ToArray());
        return Task.FromResult((stream, $"{item.TitleEn}.xlsx"));
    }

    public Task<GradeImportPreviewDto> PreviewImportAsync(Guid classCourseId, Guid itemId, string fileName, Stream content, CancellationToken ct = default)
    {
        EnsureWritable(classCourseId);
        if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) throw Problem(400, ProblemCodes.grade_import_invalid);
        return Task.FromResult(new GradeImportPreviewDto(Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddMinutes(GradingLimits.ImportTokenMinutes), 0, 0, [], [], [], []));
    }

    public Task<GradeImportResultDto> ApplyImportAsync(Guid classCourseId, Guid itemId, ApplyGradeImportRequest request, CancellationToken ct = default)
    {
        EnsureWritable(classCourseId);
        if (string.IsNullOrWhiteSpace(request.Token)) throw Problem(400, ProblemCodes.grade_import_expired);
        return Task.FromResult(new GradeImportResultDto(0, []));
    }

    public Task<GradeReportDto> GetReportAsync(string kind, GradeReportQuery query, CancellationToken ct = default)
    {
        if (!GradeReportKinds.All.Contains(kind)) throw Problem(404, ProblemCodes.not_found);
        var rows = StudentIds.Select(Row).Select(r => new GradeReportRowDto(r.StudentId, r.StudentNameAr, r.StudentNameEn, r.StudentNumber, DemoClassCourseId,
            "اللغة العربية", "Arabic", r.FinalPercent, r.BandCode, r.BandLabelAr, r.BandLabelEn, r.BandPoints)).ToList();
        var now = DateTime.UtcNow;
        return Task.FromResult(new GradeReportDto(kind, new PagedResult<GradeReportRowDto>(rows, rows.Count, 1, Math.Max(rows.Count, 1)), now, now));
    }

    public Task<(Stream Content, string FileName)> ExportReportAsync(string kind, GradeReportQuery query, CancellationToken ct = default)
    {
        Stream stream = new MemoryStream("report"u8.ToArray());
        return Task.FromResult((stream, $"grades-{kind}.xlsx"));
    }
}
