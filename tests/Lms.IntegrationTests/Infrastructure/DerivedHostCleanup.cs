using System.Reflection;
using Xunit.Sdk;

[assembly: Lms.IntegrationTests.Infrastructure.DerivedHostCleanup]

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>
/// After every test of the serial api collection, disposes the derived hosts (<c>WithWebHostBuilder</c>) that test created and did not
/// dispose itself. Tests outside the collection run in parallel with it and never reach the factory, so they neither create derived hosts
/// nor may dispose the ones a running api test still uses: the hook ignores them.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class DerivedHostCleanupAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest)
    {
        if (methodUnderTest.DeclaringType is { } type && InApiCollection(type))
        {
            LmsApiFactory.Current?.DisposeDerivedHosts();
        }
    }

    internal static bool InApiCollection(Type type) =>
        type.GetCustomAttributesData()
            .Any(a => a.AttributeType == typeof(CollectionAttribute) && a.ConstructorArguments.Count > 0
                      && a.ConstructorArguments[0].Value as string == ApiCollection.Name);
}
