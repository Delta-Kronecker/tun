using System;
using Microsoft.Extensions.Logging;
using Se7enPro.Services;

namespace Se7enTun;

/// <summary>
/// Everything a host application has to tell the TUN engine.
/// The engine itself only ever sees a loopback SOCKS5 endpoint
/// (<c>socks5://127.0.0.1:{SocksPort}</c>), exactly like in Se7en Pro.
/// </summary>
public sealed class TunOptions
{
    /// <summary>Loopback SOCKS5 port the tunnel is forwarded to. Required, 1-65535.</summary>
    public int SocksPort { get; set; }

    /// <summary>
    /// Upstream SOCKS5 host. Defaults to <c>127.0.0.1</c>. When it is not a loopback
    /// address a local TCP bridge (see <see cref="SocksTcpForwarder"/>) is started on
    /// <c>127.0.0.1:SocksPort</c> so the untouched engine keeps working. Note that the
    /// bridge only relays TCP, so UDP-based traffic (QUIC) through a *remote* SOCKS5
    /// server is not supported — keep the SOCKS5 listener local for full functionality.
    /// </summary>
    public string SocksHost { get; set; } = "127.0.0.1";

    /// <summary>
    /// HTTP proxy port advertised for system-proxy restoration.
    /// Only used when <see cref="SetSystemProxy"/> is enabled. Defaults to 0 (never restored).
    /// </summary>
    public int HttpProxyPort { get; set; }

    /// <summary>
    /// When true the engine clears the system proxy while the TUN owns traffic and
    /// restores <see cref="HttpProxyPort"/> afterwards. Defaults to false: a library
    /// never touches machine-wide proxy settings unless asked to.
    /// </summary>
    public bool SetSystemProxy { get; set; }

    /// <summary>
    /// Optional UI language (<c>en</c>, <c>ru</c>, <c>zh</c>). Defaults to null, which
    /// keeps the engine on English and never touches WPF culture metadata. Non-English
    /// languages additionally require the caller's application to merge
    /// <c>Resources/Strings.*.xaml</c> from this library (see README).
    /// </summary>
    public string? Language { get; set; }

    /// <summary>Optional sink for engine diagnostics (also written to the session log file).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Optional pre-built logger; takes precedence over <see cref="Log"/>.</summary>
    public ILogger<WintunTunManager>? Logger { get; set; }

    /// <summary>Optional system proxy implementation; defaults to a no-op one.</summary>
    public ISystemProxyService? SystemProxy { get; set; }

    internal void Validate()
    {
        if (SocksPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SocksPort), SocksPort, "TunOptions.SocksPort must be a TCP port between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(SocksHost))
        {
            throw new ArgumentException("TunOptions.SocksHost must not be empty.", nameof(SocksHost));
        }

        if (HttpProxyPort is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HttpProxyPort), HttpProxyPort, "TunOptions.HttpProxyPort must be 0 or a TCP port.");
        }
    }
}
