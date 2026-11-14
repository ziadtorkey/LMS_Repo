using Microsoft.AspNetCore.Components;

namespace Lms.Client.Services;

/// <summary>Effective permissions of the active context; reloaded whenever the context changes, cleared on logout.</summary>
public sealed class PermissionState : IDisposable
{
    private readonly ILmsApi _api;
    private readonly IContextStore _store;
    private readonly NavigationManager _navigation;
    private HashSet<string> _permissions = [];

    public PermissionState(ILmsApi api, IContextStore store, NavigationManager navigation)
    {
        _api = api; _store = store; _navigation = navigation;
        _store.Changed += OnContextChanged;
    }

    public bool IsPlatformAdmin { get; private set; }
    /// <summary>The signed-in user (from <c>/api/me</c>); null until loaded. Pages that tell "mine" from "theirs" (messages) compare against it.</summary>
    public Guid? UserId { get; private set; }
    public bool Loaded { get; private set; }
    public event Action? Changed;

    public bool Has(string permission) => _permissions.Contains(permission);

    public async Task RefreshAsync()
    {
        var (me, _) = await ApiCalls.RunAsync(() => _api.GetMeAsync(), _navigation);
        IsPlatformAdmin = me?.IsPlatformAdmin ?? false;
        UserId = me?.UserId;

        _permissions = [];
        if (_store.ActiveAssignmentId is not null)
        {
            var (dto, _) = await ApiCalls.RunAsync(() => _api.GetMyPermissionsAsync(), _navigation);
            if (dto is not null) _permissions = [.. dto.Permissions];
        }
        Loaded = true;
        Changed?.Invoke();
    }

    public void Clear()
    {
        _permissions = [];
        IsPlatformAdmin = false;
        UserId = null;
        Loaded = false;
        Changed?.Invoke();
    }

    private void OnContextChanged() => _ = RefreshAsync();

    public void Dispose() => _store.Changed -= OnContextChanged;
}
