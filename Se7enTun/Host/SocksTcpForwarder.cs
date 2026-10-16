using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Se7enTun;

/// <summary>
/// Blind TCP relay used when the caller's SOCKS5 server is not loopback: the untouched
/// engine always dials <c>socks5://127.0.0.1:{port}</c>, so this listener accepts those
/// connections locally and forwards the raw byte stream to the real upstream.
/// Only TCP is relayed (SOCKS5 CONNECT); the UDP associate path is not bridged.
/// </summary>
public sealed class SocksTcpForwarder : IAsyncDisposable
{
    private const int BufferSize = 16 * 1024;

    private readonly TcpListener _listener;
    private readonly string _remoteHost;
    private readonly int _remotePort;
    private readonly CancellationTokenSource _cts = new();
    private long _connections;

    public SocksTcpForwarder(string remoteHost, int remotePort, int localPort, string localHost = "127.0.0.1")
    {
        if (string.IsNullOrWhiteSpace(remoteHost))
            throw new ArgumentException("remoteHost must not be empty.", nameof(remoteHost));
        if (remotePort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(remotePort), remotePort, "remotePort must be 1-65535.");
        if (localPort is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(localPort), localPort, "localPort must be 0-65535.");

        _remoteHost = remoteHost;
        _remotePort = remotePort;

        if (!IPAddress.TryParse(localHost, out var localIp)) localIp = IPAddress.Loopback;
        LocalHost = localIp.ToString();
        _listener = new TcpListener(localIp, localPort);
    }

    public string LocalHost { get; }
    public int LocalPort { get; private set; }
    public string RemoteHost => _remoteHost;
    public int RemotePort => _remotePort;
    public long ConnectionsServed => Volatile.Read(ref _connections);
    public Action<string>? Diag { get; set; }

    public void Start()
    {
        _listener.Start();
        LocalPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { if (ct.IsCancellationRequested) return; continue; }

            _ = Task.Run(() => HandleAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        TcpClient? remote = null;
        try
        {
            using (client)
            {
                remote = new TcpClient();
                await remote.ConnectAsync(_remoteHost, _remotePort, ct);
                Interlocked.Increment(ref _connections);
                Diag?.Invoke($"bridge {client.Client.RemoteEndPoint} -> {_remoteHost}:{_remotePort}");

                var up = PumpAsync(client.GetStream(), remote.GetStream(), ct);
                var down = PumpAsync(remote.GetStream(), client.GetStream(), ct);
                await Task.WhenAny(up, down);

                try { client.Client.Shutdown(SocketShutdown.Both); } catch { }
                try { remote.Client.Shutdown(SocketShutdown.Both); } catch { }

                await Task.WhenAll(up, down);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Diag?.Invoke($"bridge error ({_remoteHost}:{_remotePort}): {ex.Message}");
        }
        finally
        {
            try { remote?.Dispose(); } catch { }
        }
    }

    private static async Task PumpAsync(Stream from, Stream to, CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await from.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read <= 0) break;
                await to.WriteAsync(buffer.AsMemory(0, read), ct);
                await to.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    public ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _cts.Dispose(); } catch { }
        return ValueTask.CompletedTask;
    }
}
