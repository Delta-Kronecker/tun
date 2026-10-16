using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Se7enPro.Models;
using Se7enPro.Services;

namespace Se7enTun;

/// <summary>
/// Standalone host for the Se7en Pro Wintun TUN engine.
///
/// The engine code under <c>Services/Tun</c> is byte-identical to the original app;
/// this class only supplies the four collaborators the engine expects
/// (<see cref="ITunnelCoreManager"/>, <see cref="ISettingsService"/>,
/// <see cref="IChildProcessGuard"/>, <see cref="ISystemProxyService"/>) and exposes a
/// small start/stop surface for the caller.
///
/// The host process must run elevated (Wintun adapter + routes need Administrator) —
/// exactly like Se7en Pro's own TUN mode.
/// </summary>
public sealed class TunHost : IAsyncDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ITunnelCoreManager _core;
    private readonly TunnelCoreStub? _stub;
    private readonly WintunTunManager _manager;
    private readonly IChildProcessGuard _childGuard;
    private readonly TrackedChildProcessGuard? _trackedGuard;
    private readonly SocksTcpForwarder? _forwarder;

    /// <summary>
    /// Convenience constructor: builds every collaborator from <paramref name="options"/>.
    /// Nothing is started until <see cref="Start"/> / <see cref="StartAsync"/> is called.
    /// </summary>
    public TunHost(TunOptions? options = null)
    {
        options ??= new TunOptions();
        options.Validate();

        if (options.Language is { Length: > 0 } language)
        {
            try { Loc.Apply(language); }
            catch { /* WPF metadata already sealed or no string table available; keep English */ }
        }

        _trackedGuard = new TrackedChildProcessGuard();
        _childGuard = _trackedGuard;

        if (!IsLoopback(options.SocksHost))
        {
            var forwarder = new SocksTcpForwarder(options.SocksHost, options.SocksPort, options.SocksPort);
            try
            {
                forwarder.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Cannot bridge 127.0.0.1:{options.SocksPort} to {options.SocksHost}:{options.SocksPort}: {ex.Message}",
                    ex);
            }
            _forwarder = forwarder;
        }

        var settings = new TunSettingsService();
        settings.Settings.SetSystemProxy = options.SetSystemProxy;

        var stub = new TunnelCoreStub(options.SocksPort, options.HttpProxyPort);
        _settingsService = settings;
        _core = stub;
        _stub = stub;

        ILogger<WintunTunManager> logger =
            options.Logger
            ?? (options.Log is null
                ? NullLogger<WintunTunManager>.Instance
                : new ActionLogger<WintunTunManager>(options.Log));

        _manager = new WintunTunManager(
            logger, _core, _settingsService, _childGuard, options.SystemProxy ?? new NoopSystemProxyService());
    }

    /// <summary>
    /// Advanced constructor for hosts that already own those services (dependency
    /// injection). Pass a <see cref="TunnelCoreStub"/> as <paramref name="tunnel"/> if you
    /// still want the <see cref="SetSocksPort"/> style helpers.
    /// </summary>
    public TunHost(
        ITunnelCoreManager tunnel,
        ISettingsService settings,
        IChildProcessGuard childGuard,
        ISystemProxyService systemProxy,
        ILogger<WintunTunManager>? logger = null)
    {
        _core = tunnel ?? throw new ArgumentNullException(nameof(tunnel));
        _settingsService = settings ?? throw new ArgumentNullException(nameof(settings));
        _childGuard = childGuard ?? throw new ArgumentNullException(nameof(childGuard));
        _trackedGuard = childGuard as TrackedChildProcessGuard;
        _stub = tunnel as TunnelCoreStub;
        _manager = new WintunTunManager(
            logger ?? NullLogger<WintunTunManager>.Instance,
            _core,
            _settingsService,
            _childGuard,
            systemProxy ?? new NoopSystemProxyService());
    }

    /// <summary>Shared settings; split-tunnel rules and DNS resolvers live here.</summary>
    public UserSettings Settings => _settingsService.Settings;

    /// <summary>Low-level engine (state, last error, state changes).</summary>
    public ITunManager Manager => _manager;

    /// <summary>The upstream description the engine is following.</summary>
    public ITunnelCoreManager Core => _core;

    public TunState State => _manager.State;

    public string? LastError => _manager.LastError;

    /// <summary>True when the current process runs elevated (required for TUN to start).</summary>
    public bool IsElevated => AdminElevation.IsAdministrator();

    /// <summary>Local TCP bridge in use, when the upstream SOCKS is not loopback.</summary>
    public SocksTcpForwarder? Bridge => _forwarder;

    public event EventHandler? StateChanged
    {
        add => _manager.StateChanged += value;
        remove => _manager.StateChanged -= value;
    }

    /// <summary>Replaces the engine's underlying-DNS autodetection (test/diagnostic seam).</summary>
    public static Func<IReadOnlyList<string>>? UnderlyingDnsServersOverride
    {
        get => WintunTunManager.UnderlyingDnsServersOverride;
        set => WintunTunManager.UnderlyingDnsServersOverride = value;
    }

    /// <summary>IPv4 resolvers the host machine is actually using (honours the override).</summary>
    public static IReadOnlyList<string> DetectUnderlyingDnsServers() =>
        WintunTunManager.DetectUnderlyingDnsServersV4();

    /// <summary>True when the machine has a global IPv6 address (drives IPv6 catch-all routes).</summary>
    public static bool HasGlobalIpv6() => WintunTunManager.HasGlobalIPv6();

    /// <summary>Publishes pending <see cref="Settings"/> changes to the engine.</summary>
    public void Save() => _settingsService.Save();

    /// <summary>Turns system-wide tunnelling on (requires elevation to actually start).</summary>
    public void Start()
    {
        Settings.SystemWideTunneling = true;
        _settingsService.Save();
    }

    /// <summary>Turns system-wide tunnelling off and tears the adapter down.</summary>
    public void Stop()
    {
        Settings.SystemWideTunneling = false;
        _settingsService.Save();
    }

    /// <summary>Starts the engine and waits until it reports <see cref="TunState.Running"/>.</summary>
    public Task<bool> StartAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        Start();
        return WaitForAsync(static s => s == TunState.Running, timeout ?? TimeSpan.FromSeconds(60), cancellationToken);
    }

    /// <summary>Stops the engine and waits until it reports <see cref="TunState.Off"/>.</summary>
    public Task<bool> StopAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        Stop();
        return WaitForAsync(static s => s == TunState.Off, timeout ?? TimeSpan.FromSeconds(30), cancellationToken);
    }

    /// <summary>Points the engine at a different local SOCKS5 port (live restart).</summary>
    public void SetSocksPort(int socksPort)
    {
        if (socksPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(socksPort), socksPort, "SOCKS port must be 1-65535.");
        RequireStub().Configure(socksPort, _core.State, _core.CurrentRouteIp, _core.CurrentRouteSni);
    }

    /// <summary>Reports the upstream as connected/disconnected; the engine starts on Connected.</summary>
    public void SetConnected(bool connected) =>
        RequireStub().Configure(
            _core.SocksProxyPort,
            connected ? ConnectionState.Connected : ConnectionState.Disconnected,
            _core.CurrentRouteIp,
            _core.CurrentRouteSni);

    /// <summary>Optional host route the engine pins through the tunnel (server IP / SNI).</summary>
    public void SetRoute(string routeIp, string routeSni = "") =>
        RequireStub().Configure(_core.SocksProxyPort, _core.State, routeIp, routeSni);

    private TunnelCoreStub RequireStub() =>
        _stub ?? throw new InvalidOperationException(
            "This TunHost was built with a custom ITunnelCoreManager; update that instance instead.");

    private async Task<bool> WaitForAsync(Func<TunState, bool> predicate, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var state = _manager.State;
            if (predicate(state)) return true;
            if (state == TunState.Error) return false;
            if (DateTime.UtcNow >= deadline) return predicate(state);

            try { await Task.Delay(100, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return predicate(_manager.State); }
        }
    }

    private static bool IsLoopback(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }

    public async ValueTask DisposeAsync()
    {
        try { await _manager.DisposeAsync().ConfigureAwait(false); }
        catch { }

        try { _trackedGuard?.KillAll(); }
        catch { }

        if (_forwarder is not null)
        {
            try { await _forwarder.DisposeAsync().ConfigureAwait(false); }
            catch { }
        }
    }
}
