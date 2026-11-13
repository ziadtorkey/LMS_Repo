using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Reflection;
using Xunit.Sdk;

[assembly: Lms.IntegrationTests.Infrastructure.PoolSamplerMark]

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>
/// Evidence for the MySQL pool exhaustion (#466, "All pooled connections are in use"): MySqlConnector publishes its pool metrics through
/// <c>System.Diagnostics.Metrics</c>, and this listener turns them into a time series. Every 15 seconds it appends one line per pool with
/// the connections in use, idle and pending, plus the test that started last, to the file named by <c>LMS_CONN_LOG</c> (shared with the server-side process-list sampler) or <c>pool-samples.log</c>. Lines also name the highest value seen so far, so a creep (a leak) and a spike (a burst) look different.
/// </summary>
internal sealed class PoolSampler : IDisposable
{
    private static readonly object Gate = new();
    private static PoolSampler? _instance;

    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, double> _values = new();
    private readonly ConcurrentDictionary<string, double> _peaks = new();
    private readonly string _path = Environment.GetEnvironmentVariable("LMS_CONN_LOG") ?? Path.Combine(AppContext.BaseDirectory, "pool-samples.log");
    private readonly Timer _timer;
    private volatile string _lastTest = "";

    private PoolSampler()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "MySqlConnector")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<int>((i, v, tags, _) => Record(i, v, tags));
        _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i, v, tags));
        _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i, v, tags));
        _listener.Start();
        _timer = new Timer(_ => Sample(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    /// <summary>Starts the sampler once per test process; safe to call from every test.</summary>
    public static void EnsureStarted()
    {
        lock (Gate)
        {
            _instance ??= new PoolSampler();
        }
    }

    public static void NoteTest(string name)
    {
        EnsureStarted();
        _instance!._lastTest = name;
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var key = instrument.Name;
        foreach (var tag in tags)
        {
            key += $"|{tag.Key}={tag.Value}";
        }

        // Counters that go up and down publish deltas; observable gauges publish the current value.
        if (instrument.GetType().Name.Contains("Observable", StringComparison.Ordinal))
        {
            _values[key] = value;
        }
        else
        {
            _values.AddOrUpdate(key, value, (_, old) => old + value);
        }

        _peaks.AddOrUpdate(key, value, (_, old) => Math.Max(old, _values[key]));
    }

    private void Sample()
    {
        try
        {
            _listener.RecordObservableInstruments();
            var line = string.Join(" ", _values.Where(kv => kv.Key.Contains("usage") || kv.Key.Contains("pending")).OrderBy(kv => kv.Key).Select(kv =>
                $"{Short(kv.Key)}={kv.Value.ToString(CultureInfo.InvariantCulture)}(peak {_peaks.GetValueOrDefault(kv.Key).ToString(CultureInfo.InvariantCulture)})"));
            File.AppendAllText(_path, $"pool {DateTime.UtcNow:HH:mm:ss} test={_lastTest} threadpool(threads={ThreadPool.ThreadCount} pending={ThreadPool.PendingWorkItemCount}) {line}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Diagnostics only: never fail a test run.
        }
    }

    /// <summary>"db.client.connections.usage|pool.name=Server=127.0.0.1;Port=8190;...|state=used" becomes "usage[8190,used]".</summary>
    private static string Short(string key)
    {
        var name = key.Split('|')[0].Replace("db.client.connections.", "");
        var port = System.Text.RegularExpressions.Regex.Match(key, @"Port=(\d+)").Groups[1].Value;
        var state = System.Text.RegularExpressions.Regex.Match(key, @"state=(\w+)").Groups[1].Value;
        return $"{name}[{port}{(state.Length > 0 ? "," + state : "")}]";
    }

    public void Dispose()
    {
        _timer.Dispose();
        _listener.Dispose();
    }
}

/// <summary>Assembly-wide: starts the pool sampler and tells it which test is running.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class PoolSamplerMarkAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest) =>
        PoolSampler.NoteTest($"{methodUnderTest.DeclaringType?.Name}.{methodUnderTest.Name}");
}
