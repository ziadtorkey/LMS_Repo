using Lms.Shared.AcademicYears;
using Lms.Client.Services.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Lms.Client.Services;

/// <summary>
/// The academic year the user works in (X-04): defaults to the active year, remembered per role assignment
/// (localStorage <c>lms.year.{assignmentId}</c>), reloaded when the context changes. Later lists pass
/// <see cref="SelectedYearId"/> as the <c>yearId</c> query parameter.
/// </summary>
public interface IYearStore
{
    Guid? SelectedYearId { get; }
    AcademicYearDto? Selected { get; }
    IReadOnlyList<AcademicYearDto> Years { get; }
    /// <summary>True when the selected year is Closed: screens show data but block edits.</summary>
    bool IsReadOnly { get; }
    /// <summary>Loads the years of <paramref name="orgUnitId"/> (default: the unit of the active context).</summary>
    Task LoadAsync(Guid? orgUnitId = null);
    Task SelectAsync(Guid yearId);
    event Action? Changed;
}

public sealed class YearStore : IYearStore, IDisposable
{
    public const string StoragePrefix = "lms.year.";

    private readonly IYearsApi _api;
    private readonly IContextStore _context;
    private readonly IJSRuntime _js;
    private readonly NavigationManager _navigation;

    public YearStore(IYearsApi api, IContextStore context, IJSRuntime js, NavigationManager navigation)
    {
        _api = api; _context = context; _js = js; _navigation = navigation;
        _context.Changed += OnContextChanged;
    }

    public IReadOnlyList<AcademicYearDto> Years { get; private set; } = [];
    public Guid? SelectedYearId { get; private set; }
    public AcademicYearDto? Selected => Years.FirstOrDefault(y => y.Id == SelectedYearId);
    public bool IsReadOnly => Selected?.Status == AcademicYearStatuses.Closed;
    public event Action? Changed;

    public async Task LoadAsync(Guid? orgUnitId = null)
    {
        var assignment = _context.ActiveAssignmentId;
        if (assignment is null)
        {
            Years = []; SelectedYearId = null;
            Changed?.Invoke();
            return;
        }

        YearContextDto? context = null;
        try
        {
            context = await _api.GetEffectiveAsync(orgUnitId);
        }
        catch (ApiForbiddenException) { } // no AcademicYears.View in this context: the selector stays hidden, no redirect
        catch (Exception ex) when (ApiCalls.TryRedirect(ex, _navigation)) { return; }
        catch (HttpRequestException) { }
        if (context is null)
        {
            Years = []; SelectedYearId = null;
            Changed?.Invoke();
            return;
        }

        var stored = await ReadStoredAsync(assignment.Value);
        Years = context.Years;
        SelectedYearId = Years.Any(y => y.Id == stored) ? stored
            : Years.Any(y => y.Id == context.CurrentYearId) ? context.CurrentYearId : null;
        Changed?.Invoke();
    }

    public async Task SelectAsync(Guid yearId)
    {
        if (Years.All(y => y.Id != yearId)) return;
        SelectedYearId = yearId;
        if (_context.ActiveAssignmentId is { } assignment)
        {
            try { await _js.InvokeVoidAsync("localStorage.setItem", StoragePrefix + assignment, yearId.ToString()); }
            catch (JSException) { } // storage blocked: the choice still applies for this session
        }
        Changed?.Invoke();
    }

    private async Task<Guid?> ReadStoredAsync(Guid assignment)
    {
        try
        {
            var value = await _js.InvokeAsync<string?>("localStorage.getItem", StoragePrefix + assignment);
            return Guid.TryParse(value, out var id) ? id : null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    private void OnContextChanged() => _ = LoadAsync();

    public void Dispose() => _context.Changed -= OnContextChanged;
}
