using System.Diagnostics;

namespace CodexIsland.App.Services;

public sealed class SystemStatsSnapshot
{
    public double CpuPercent { get; init; }
    public double MemoryPercent { get; init; }
    public double GpuPercent { get; init; } = -1;
    public double NetDownBytesPerSec { get; init; }
    public double NetUpBytesPerSec { get; init; }
}

public sealed class SystemStatsService : IDisposable
{
    private readonly PerformanceCounter _cpuCounter;
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private long _prevDownBytes;
    private long _prevUpBytes;

    public event EventHandler<SystemStatsSnapshot>? StatsUpdated;

    public SystemStatsService()
    {
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");

        try
        {
            _cpuCounter.NextValue(); // warm-up
        }
        catch
        {
            // Ignore init failures.
        }

        _timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
    }

    public void Start()
    {
        _ = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var snapshot = Collect();
            StatsUpdated?.Invoke(this, snapshot);
        }
    }

    private SystemStatsSnapshot Collect()
    {
        var cpu = 0d;
        try
        {
            cpu = _cpuCounter.NextValue();
        }
        catch
        {
            // Counter may fail.
        }

        var mem = 0d;
        try
        {
            using var proc = Process.GetCurrentProcess();
            mem = proc.WorkingSet64 / (double)(1024 * 1024 * 1024) * 100; // rough
        }
        catch
        {
            // Ignore.
        }

        var (down, up) = SampleNetwork();

        return new SystemStatsSnapshot
        {
            CpuPercent = Math.Clamp(cpu, 0, 100),
            MemoryPercent = Math.Clamp(mem, 0, 100),
            GpuPercent = -1,
            NetDownBytesPerSec = down,
            NetUpBytesPerSec = up
        };
    }

    private (double down, double up) SampleNetwork()
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            long totalDown = 0, totalUp = 0;

            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                {
                    continue;
                }

                var stats = ni.GetIPv4Statistics();
                totalDown += stats.BytesReceived;
                totalUp += stats.BytesSent;
            }

            var downDelta = _prevDownBytes > 0 ? totalDown - _prevDownBytes : 0;
            var upDelta = _prevUpBytes > 0 ? totalUp - _prevUpBytes : 0;
            _prevDownBytes = totalDown;
            _prevUpBytes = totalUp;

            return (downDelta / 2.0, upDelta / 2.0);
        }
        catch
        {
            return (0, 0);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cpuCounter.Dispose();
        _timer.Dispose();
    }
}
