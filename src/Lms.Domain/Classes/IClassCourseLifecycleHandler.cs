namespace Lms.Domain.Classes;

/// <summary>Thrown by a handler to veto removing a class course (it still has data that depends on it).</summary>
public sealed class ClassCourseInUseException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

/// <summary>
/// Hook for modules that react to a class course being added, removed or made (non-)optional. All registered handlers
/// run in registration order inside the caller transaction.
/// </summary>
public interface IClassCourseLifecycleHandler
{
    Task OnAddedAsync(ClassCourse classCourse, CancellationToken cancellationToken);

    /// <summary>Throw <see cref="ClassCourseInUseException"/> to block the removal.</summary>
    Task OnRemovingAsync(ClassCourse classCourse, CancellationToken cancellationToken);

    Task OnOptionalChangedAsync(ClassCourse classCourse, CancellationToken cancellationToken);
}
