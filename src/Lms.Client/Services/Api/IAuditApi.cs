using System.Globalization;
using Lms.Shared;
using Lms.Shared.Auditing;

namespace Lms.Client.Services.Api;

/// <summary>Audit log viewer API (FR-AUD-03, FR-AUD-04).</summary>
public interface IAuditApi
{
    Task<PagedResult<AuditEntryDto>> QueryAsync(AuditQuery query, CancellationToken ct = default);
    Task<AuditEntryDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary><paramref name="format"/> is <see cref="AuditExportFormats.Csv"/> or <see cref="AuditExportFormats.Xlsx"/>.</summary>
    Task<(Stream Content, string FileName)> ExportAsync(AuditQuery query, string format, CancellationToken ct = default);
}

public sealed class AuditApi(ApiHttp http) : IAuditApi
{
    public async Task<PagedResult<AuditEntryDto>> QueryAsync(AuditQuery query, CancellationToken ct = default) =>
        await http.GetAsync<PagedResult<AuditEntryDto>>($"{Routes.Audit}?{QueryString(query)}", ct);

    public async Task<AuditEntryDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        await http.GetAsync<AuditEntryDetailDto>(Routes.AuditEntry(id), ct);

    public Task<(Stream Content, string FileName)> ExportAsync(AuditQuery query, string format, CancellationToken ct = default) =>
        http.DownloadAsync($"{Routes.AuditExport}?{QueryString(query)}&format={format}", ct);

    /// <summary>Query string of the filters (only the ones that are set).</summary>
    public static string QueryString(AuditQuery q)
    {
        var parts = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value)) parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }
        Add("userId", q.UserId?.ToString());
        Add("entityType", q.EntityType);
        Add("entityId", q.EntityId?.ToString());
        Add("action", q.Action);
        Add("fromUtc", q.FromUtc?.ToString("o", CultureInfo.InvariantCulture));
        Add("toUtc", q.ToUtc?.ToString("o", CultureInfo.InvariantCulture));
        Add("page", q.Page.ToString(CultureInfo.InvariantCulture));
        Add("pageSize", q.PageSize.ToString(CultureInfo.InvariantCulture));
        return string.Join('&', parts);
    }
}

/// <summary>In-memory audit log for the standalone dev server and bUnit tests.</summary>
public sealed class FakeAuditApi : IAuditApi
{
    private readonly List<AuditEntryDetailDto> _entries = [];

    public Guid AdminId { get; } = Guid.NewGuid();
    public AuditQuery? LastQuery { get; private set; }
    public string? LastExportFormat { get; private set; }

    public FakeAuditApi()
    {
        Add(AdminId, AuditActions.Updated, "Role", """{"NameEn":"Teacher","NameAr":"المعلم","Active":true}""",
            """{"NameEn":"Instructor","NameAr":"المعلم","Active":true}""");
        Add(AdminId, AuditActions.Created, "OrgUnit", null, """{"NameEn":"Grade 1","NameAr":"الصف الأول","Code":"G1"}""");
        Add(null, AuditActions.SecurityEvent, AuditSecurityEvents.LoginFailed, null, null);
    }

    public AuditEntryDetailDto Add(Guid? userId, string action, string entityType, string? before, string? after)
    {
        var dto = new AuditEntryDetailDto(new AuditEntryDto(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-_entries.Count * 7), userId,
            userId is null ? null : "admin@example.test", userId is null ? null : "المسؤول", userId is null ? null : "Admin",
            userId is null ? null : "مسؤول النظام", userId is null ? null : "Admin", "مدارس النور", "Al Noor Schools",
            action, entityType, Guid.NewGuid(), "10.0.0.1"), before, after);
        _entries.Add(dto);
        return dto;
    }

    public Task<PagedResult<AuditEntryDto>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        var rows = _entries.Select(e => e.Entry)
            .Where(e => query.UserId is null || e.UserId == query.UserId)
            .Where(e => query.EntityType is null || e.EntityType == query.EntityType)
            .Where(e => query.Action is null || e.Action == query.Action)
            .Where(e => query.FromUtc is null || e.TimestampUtc >= query.FromUtc)
            .Where(e => query.ToUtc is null || e.TimestampUtc <= query.ToUtc)
            .OrderByDescending(e => e.TimestampUtc).ToList();
        return Task.FromResult(new PagedResult<AuditEntryDto>(
            rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    public Task<AuditEntryDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_entries.First(e => e.Entry.Id == id));

    public Task<(Stream Content, string FileName)> ExportAsync(AuditQuery query, string format, CancellationToken ct = default)
    {
        LastQuery = query;
        LastExportFormat = format;
        return Task.FromResult<(Stream, string)>((new MemoryStream([105, 100, 10]), $"audit.{format}"));
    }
}
