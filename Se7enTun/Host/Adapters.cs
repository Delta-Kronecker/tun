using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Se7enPro.Models;
using Se7enPro.Services;

namespace Se7enTun;

/// <summary>
/// In-memory <see cref="ISettingsService"/>. Persistence is the host application's
/// job: mutate <see cref="Settings"/> and call <see cref="Save"/> (or
/// <c>TunHost.Save()</c>) to make the engine reconcile.
/// </summary>
public sealed class TunSettingsService : ISettingsService
{
    public UserSettings Settings { get; } = new();

    public event EventHandler? SettingsChanged;

    public void Load() { }

    public void Save() => SettingsChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Minimal <see cref="ITunnelCoreManager"/>: it only carries the three values the TUN
/// engine reads (state, SOCKS port, current route IP) plus the events it subscribes to.
/// </summary>
public sealed class TunnelCoreStub : ITunnelCoreManager
{
    public TunnelCoreStub(int socksProxyPort, int httpProxyPort = 0, ConnectionState state = ConnectionState.Connected)
    {
        SocksProxyPort = socksProxyPort;
        HttpProxyPort = httpProxyPort;
        State = state;
    }

    public ConnectionState State { get; private set; }
    public int SocksProxyPort { get; private set; }
    public int HttpProxyPort { get; private set; }
    public string ClientRegion => "";
    public string ConnectedServerRegion => "";
    public string CurrentRouteIp { get; private set; } = "";
    public string CurrentRouteSni { get; private set; } = "";
    public IReadOnlyList<string> AvailableEgressRegions => Array.Empty<string>();
    public IReadOnlyList<string> RecentLog => Array.Empty<string>();
    public long BytesSent => 0;
    public long BytesReceived => 0;
    public int ConnectProgressPercent => 100;
    public string ConnectProgressText => "Connected";

#pragma warning disable CS0067 // subscribed by the engine, raised by Configure()
    public event EventHandler? RouteChanged;
    public event EventHandler<ConnectionState>? StateChanged;
    public event EventHandler<Notice>? NoticeReceived;
    public event EventHandler<string>? LogLineAppended;
    public event EventHandler<bool>? ConnectionIntentChanged;
    public event EventHandler? BytesTransferredChanged;
    public event EventHandler? LogCleared;
    public event EventHandler? ConnectProgressChanged;
#pragma warning restore CS0067

    public Task StartAsync() => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public Task RestartAsync() => Task.CompletedTask;

    public void CancelInFlightConnection() { }

    /// <summary>
    /// Publishes a new upstream description. Always raises <see cref="StateChanged"/> so
    /// the engine reconciles even when only the port or route IP changed.
    /// </summary>
    public void Configure(
        int socksProxyPort,
        ConnectionState state,
        string? routeIp = "",
        string? routeSni = "",
        int httpProxyPort = -1)
    {
        SocksProxyPort = socksProxyPort;
        State = state;
        if (routeIp is not null) CurrentRouteIp = routeIp;
        if (routeSni is not null) CurrentRouteSni = routeSni;
        if (httpProxyPort >= 0) HttpProxyPort = httpProxyPort;

        RouteChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, state);
    }
}

/// <summary>
/// <see cref="IChildProcessGuard"/> that remembers adopted children so the host can
/// reap leftovers on shutdown (the engine already kills its own child on stop).
/// </summary>
public sealed class TrackedChildProcessGuard : IChildProcessGuard
{
    private readonly object _lock = new();
    private readonly List<Process> _adopted = new();

    public int AdoptedCount
    {
        get { lock (_lock) return _adopted.Count; }
    }

    public void Adopt(Process process)
    {
        if (process is null) return;
        lock (_lock)
        {
            _adopted.RemoveAll(p =>
            {
                try { return p.HasExited; }
                catch { return true; }
            });
            _adopted.Add(process);
        }
    }

    /// <summary>Kills every still-running adopted child tree. Returns how many were killed.</summary>
    public int KillAll()
    {
        Process[] snapshot;
        lock (_lock)
        {
            snapshot = _adopted.ToArray();
            _adopted.Clear();
        }

        var killed = 0;
        foreach (var p in snapshot)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    killed++;
                }
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }

        return killed;
    }
}

/// <summary>
/// <see cref="ISystemProxyService"/> that does nothing. The engine only calls it when
/// <c>Settings.SetSystemProxy</c> is enabled, and a standalone library should not
/// rewrite machine-wide proxy settings on its own.
/// </summary>
public sealed class NoopSystemProxyService : ISystemProxyService
{
    public bool IsApplied { get; private set; }

    public void Set(int httpProxyPort) => IsApplied = true;

    public void Clear() => IsApplied = false;

    public void RestoreIfCrashed() { }
}

/// <summary>Thin <see cref="ILogger{T}"/> that forwards formatted lines to a callback.</summary>
internal sealed class ActionLogger<T> : ILogger<T>
{
    private readonly Action<string> _sink;

    public ActionLogger(Action<string> sink) => _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        try
        {
            var line = $"[{logLevel}] {formatter(state, exception)}";
            if (exception is not null) line += $" | {exception.GetType().Name}: {exception.Message}";
            _sink(line);
        }
        catch { }
    }
}
