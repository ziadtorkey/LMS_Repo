using Lms.Infrastructure.Jobs;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Jobs;

/// <summary>
/// The lock behind idempotent scheduling must exclude callers on different connections. (Hangfire's own MySQL distributed lock did not:
/// a probe of 12 parallel holders overlapped, which is why <see cref="MySqlJobKeyLock"/> uses database named locks.)
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class JobKeyLockTests(LmsApiFactory factory)
{
    private static (int Overlaps, List<string> Errors) Hammer(IJobKeyLock keyLock, string resource)
    {
        var inside = 0;
        var overlaps = 0;
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
        Task.WaitAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
        {
            try
            {
                using var gate = keyLock.Acquire(resource, TimeSpan.FromSeconds(20));
                if (Interlocked.Increment(ref inside) > 1)
                {
                    Interlocked.Increment(ref overlaps);
                }

                Thread.Sleep(50);
                Interlocked.Decrement(ref inside);
            }
            catch (Exception ex)
            {
                errors.Add(ex.GetType().Name + ": " + ex.Message);
            }
        })).ToArray());
        return (overlaps, [.. errors]);
    }

    [Fact]
    public void The_mysql_lock_excludes_parallel_holders_across_connections()
    {
        var connectionString = factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Lms"]!;

        var (overlaps, errors) = Hammer(new MySqlJobKeyLock(connectionString), "probe:" + Guid.NewGuid());

        Assert.Equal(0, overlaps);
        Assert.Empty(errors);
    }

    [Fact]
    public void Different_resources_do_not_block_each_other_and_a_held_lock_times_out()
    {
        var connectionString = factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Lms"]!;
        var keyLock = new MySqlJobKeyLock(connectionString);

        using var a = keyLock.Acquire("probe-a:" + Guid.NewGuid(), TimeSpan.FromSeconds(2));
        using var b = keyLock.Acquire("probe-b:" + Guid.NewGuid(), TimeSpan.FromSeconds(2)); // another resource: free at once
        var held = "probe-held:" + Guid.NewGuid();
        using var first = keyLock.Acquire(held, TimeSpan.FromSeconds(2));

        Assert.Throws<TimeoutException>(() => keyLock.Acquire(held, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_released_lock_can_be_taken_again_and_the_in_process_lock_behaves_the_same()
    {
        var connectionString = factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Lms"]!;
        var mysql = new MySqlJobKeyLock(connectionString);
        var resource = "probe-reuse:" + Guid.NewGuid();

        mysql.Acquire(resource, TimeSpan.FromSeconds(2)).Dispose();
        mysql.Acquire(resource, TimeSpan.FromSeconds(2)).Dispose();
        var inProcess = Hammer(new InProcessJobKeyLock(), "probe:" + Guid.NewGuid());

        Assert.Equal(0, inProcess.Overlaps);
        Assert.Equal(MySqlJobKeyLock.LockName("a").Length, MySqlJobKeyLock.LockName(new string('b', 300)).Length); // fixed length
        Assert.True(MySqlJobKeyLock.LockName(new string('x', 500)).Length <= 64);
    }
}
