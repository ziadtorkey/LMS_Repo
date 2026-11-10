using Microsoft.JSInterop;

namespace Lms.Client.Services;

public interface IContextStore
{
    Guid? ActiveAssignmentId { get; }
    Task SetAsync(Guid assignmentId);
    Task ClearAsync();
    event Action? Changed;
}

/// <summary>Active role-assignment id, persisted in localStorage as lms.context.</summary>
public sealed class ContextStore(IJSRuntime js) : IContextStore
{
    public const string StorageKey = "lms.context";

    public Guid? ActiveAssignmentId { get; private set; }
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        ActiveAssignmentId = Guid.TryParse(stored, out var id) ? id : null;
    }

    public async Task ClearAsync()
    {
        ActiveAssignmentId = null;
        await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        Changed?.Invoke();
    }

    public async Task SetAsync(Guid assignmentId)
    {
        ActiveAssignmentId = assignmentId;
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, assignmentId.ToString());
        Changed?.Invoke();
    }
}
