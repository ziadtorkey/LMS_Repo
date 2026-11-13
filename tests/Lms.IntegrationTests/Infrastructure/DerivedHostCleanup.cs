using System.Reflection;
using Xunit.Sdk;

[assembly: Lms.IntegrationTests.Infrastructure.DerivedHostCleanup]

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>After every test, disposes the derived hosts (<c>WithWebHostBuilder</c>) the test created and did not dispose itself.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class DerivedHostCleanupAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest) => LmsApiFactory.Current?.DisposeDerivedHosts();
}
