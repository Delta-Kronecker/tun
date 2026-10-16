using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Se7enPro.Models;
using Se7enPro.Services;
using Xunit;

namespace Se7enTun.Tests;

public sealed class TunHostTests
{
    private const int FreeLocalPort = 47311;

    [Fact]
    public void Constructor_rejects_an_invalid_socks_port()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TunHost(new TunOptions { SocksPort = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TunHost(new TunOptions { SocksPort = 70000 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TunHost(new TunOptions { SocksPort = 1080, HttpProxyPort = 99999 }));
    }

    [Fact]
    public async Task Host_starts_idle_and_stays_off_until_asked_to_start()
    {
        await using var host = new TunHost(new TunOptions { SocksPort = 1080 });

        Assert.Equal(TunState.Off, host.State);
        Assert.Null(host.LastError);
        Assert.False(host.Settings.SystemWideTunneling);
        Assert.False(host.Settings.SetSystemProxy);
        Assert.Equal(1080, host.Core.SocksProxyPort);
        Assert.Null(host.Bridge);

        Assert.True(await host.StopAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(TunState.Off, host.State);
        Assert.False(host.Settings.SystemWideTunneling);
    }

    [Fact]
    public async Task Options_are_applied_to_the_shared_settings()
    {
        await using var host = new TunHost(new TunOptions
        {
            SocksPort = 1080,
            HttpProxyPort = 8080,
            SetSystemProxy = true,
        });

        Assert.True(host.Settings.SetSystemProxy);
        Assert.Equal(8080, host.Core.HttpProxyPort);

        host.Save();
        Assert.Equal(TunState.Off, host.State);
    }

    [Fact]
    public void Stub_helpers_reconfigure_the_upstream()
    {
        var host = new TunHost(new TunOptions { SocksPort = 1080 });

        try
        {
            host.SetSocksPort(1081);
            Assert.Equal(1081, host.Core.SocksProxyPort);

            host.SetRoute("198.51.100.7", "example.org");
            Assert.Equal("198.51.100.7", host.Core.CurrentRouteIp);
            Assert.Equal("example.org", host.Core.CurrentRouteSni);

            host.SetConnected(false);
            Assert.Equal(ConnectionState.Disconnected, host.Core.State);

            host.SetConnected(true);
            Assert.Equal(ConnectionState.Connected, host.Core.State);

            Assert.Throws<ArgumentOutOfRangeException>(() => host.SetSocksPort(0));
        }
        finally
        {
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void Stub_helpers_are_refused_when_the_host_supplies_its_own_core()
    {
        var host = new TunHost(
            new ForeignTunnelCore(),
            new TunSettingsService(),
            new TrackedChildProcessGuard(),
            new NoopSystemProxyService());

        try
        {
            Assert.IsType<ForeignTunnelCore>(host.Core);
            Assert.Throws<InvalidOperationException>(() => host.SetSocksPort(1081));
            Assert.Throws<InvalidOperationException>(() => host.SetConnected(false));
            Assert.Throws<InvalidOperationException>(() => host.SetRoute("1.2.3.4"));
        }
        finally
        {
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public async Task A_remote_upstream_gets_a_local_bridge()
    {
        await using var host = new TunHost(new TunOptions
        {
            SocksHost = "203.0.113.10",
            SocksPort = FreeLocalPort,
        });

        Assert.NotNull(host.Bridge);
        Assert.Equal(FreeLocalPort, host.Bridge!.LocalPort);
        Assert.Equal("203.0.113.10", host.Bridge.RemoteHost);
        Assert.Equal(FreeLocalPort, host.Core.SocksProxyPort);
    }

    [Fact]
    public void TunnelCoreStub_reports_progress_like_a_connected_tunnel()
    {
        var core = new TunnelCoreStub(1080, 8080);

        Assert.Equal(ConnectionState.Connected, core.State);
        Assert.Equal(100, core.ConnectProgressPercent);
        Assert.Empty(core.AvailableEgressRegions);
        Assert.Empty(core.RecentLog);
        Assert.Equal(0L, core.BytesSent);
        Assert.Equal(0L, core.BytesReceived);
    }

    private sealed class ForeignTunnelCore : ITunnelCoreManager
    {
        public ConnectionState State => ConnectionState.Connected;
        public int SocksProxyPort => 1080;
        public int HttpProxyPort => 1080;
        public string ClientRegion => "";
        public string ConnectedServerRegion => "";
        public string CurrentRouteIp => "";
        public string CurrentRouteSni => "";
        public IReadOnlyList<string> AvailableEgressRegions => Array.Empty<string>();
        public IReadOnlyList<string> RecentLog => Array.Empty<string>();
        public long BytesSent => 0;
        public long BytesReceived => 0;
        public int ConnectProgressPercent => 100;
        public string ConnectProgressText => "Connected";

#pragma warning disable CS0067
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
    }
}
