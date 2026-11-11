using Lms.Client.Services.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Support;

/// <summary>Shared setup of the M3 course tests: the staff test setup plus a fake courses API.</summary>
public abstract class CoursesTestContext : StaffTestContext
{
    protected FakeCoursesApi Courses { get; } = new();

    protected CoursesTestContext() => Services.AddSingleton<ICoursesApi>(Courses);
}
