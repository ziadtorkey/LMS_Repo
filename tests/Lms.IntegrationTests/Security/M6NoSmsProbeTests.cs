using System.Reflection;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The no-SMS probes (M6 plan Task 31, L9, D76): v1 sends no SMS anywhere. Two layers:
/// <list type="number">
/// <item><b>The surface scan</b> (runs now): no public type, member, constant, enum value, route, setting or channel of the contract, the domain or the
/// infrastructure assemblies is named or valued "sms". A later PR that adds a channel, a DTO field, a setting or a route with SMS in it fails here.</item>
/// <item><b>The outbox</b> (<c>DeliveryOutbox</c>, #750): for every publish path the table holds no <c>Sms</c> row. It waits for the table; when it exists the
/// check runs against the rows every other test in this collection has made, and the per-path probes (announcement normal and urgent, mandatory events,
/// guardian absence notices, account emails, moderation notices) are added to this class as each path merges. Mutation check recorded in the PR: removing the
/// outbox's <c>Sms</c> refusal (L9) must fail a probe.</item>
/// </list>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M6NoSmsProbeTests(LmsApiFactory factory, ITestOutputHelper output)
{
    private static readonly Assembly[] Assemblies =
    [
        typeof(Routes).Assembly,
        typeof(Lms.Domain.Common.Entity).Assembly,
        typeof(Lms.Infrastructure.Persistence.LmsDbContext).Assembly,
    ];

    /// <summary>
    /// Public names that may contain "sms" (L9, QA1 on #770). The plan keeps <c>NotificationChannels.Sms</c> out of v1 but allows the outbox to refuse an <c>Sms</c> row by a
    /// <b>private</b> constant or a literal, which this scan does not see. A public name for the refusal (a test seam, a catalog guard) is not needed; if #750 needs one it is
    /// added here with the reason, by exact name, in the PR that adds it. Empty today.
    /// </summary>
    private static readonly HashSet<string> AllowedPublicNames = [];

    private static bool IsSms(string? text) => text is not null && !AllowedPublicNames.Contains(text) && text.Contains("sms", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Type> PublicTypes(Assembly assembly) =>
        assembly.GetTypes().Where(t => t.IsPublic || t.IsNestedPublic);

    [Fact]
    public void No_public_type_or_member_of_the_contract_domain_or_infrastructure_is_named_sms()
    {
        var hits = new List<string>();
        foreach (var assembly in Assemblies)
        {
            foreach (var type in PublicTypes(assembly))
            {
                if (IsSms(type.Name))
                {
                    hits.Add($"type {type.FullName}");
                }

                foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (IsSms(member.Name))
                    {
                        hits.Add($"{type.FullName}.{member.Name}");
                    }

                    if (member is MethodBase method && !method.IsSpecialName)
                    {
                        hits.AddRange(method.GetParameters().Where(p => IsSms(p.Name)).Select(p => $"{type.FullName}.{member.Name}({p.Name})"));
                    }
                    else if (member is ConstructorInfo ctor)
                    {
                        hits.AddRange(ctor.GetParameters().Where(p => IsSms(p.Name)).Select(p => $"{type.FullName} ctor({p.Name})"));
                    }
                }
            }
        }

        Assert.True(hits.Count == 0, "SMS has no place in v1 (D76). Found:\n" + string.Join('\n', hits.Distinct()));
    }

    [Fact]
    public void No_constant_value_of_the_contract_is_sms()
    {
        var hits = typeof(Routes).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => f.IsLiteral && f.GetRawConstantValue() is string v && IsSms(v))
                .Select(f => $"{t.FullName}.{f.Name} = \"{f.GetRawConstantValue()}\""))
            .ToList();
        Assert.True(hits.Count == 0, "No route, code, key or catalog value may mention SMS (D76):\n" + string.Join('\n', hits));
    }

    [Fact]
    public void The_channels_are_in_app_email_and_push_only()
    {
        Assert.Equal(["InApp", "Email", "Push"], NotificationChannels.All);
        Assert.DoesNotContain(SettingCatalog.All, d => IsSms(d.Name) || IsSms(d.DefaultJson));
    }

    [Fact]
    public async Task The_outbox_holds_no_sms_row()
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.OutboxTable))
        {
            return;
        }

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var sms = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM DeliveryOutbox WHERE LOWER(Channel) = 'sms'").SingleAsync();
        Assert.Equal(0, sms);
        // This reads the rows the other tests of the collection happened to make. The real L9 probe (the writer refuses an Sms row, the dispatcher never claims one,
        // per publish path) is M6PendingProbeTests.The_outbox_writer_refuses_an_sms_row_and_the_dispatcher_never_claims_it, which fails with instructions once #750 is here.
    }
}
