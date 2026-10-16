# Se7en TUN

The complete **WinTUN + tun2socks subsystem of [Se7en Pro](https://github.com/Delta-Kronecker/Se7en-Pro)**
extracted into its own standalone **class library (.dll)**.

Everything under `Se7enTun/Services/**` and `Se7enTun/Models/**` is a *verbatim copy* of the
original application sources — no engine logic was edited. The only additions are the thin
`Se7enTun/Host/*.cs` façade (which supplies the four collaborators the engine expects and a
start/stop API) and the test project.

```
Host application  ──►  TunHost  ──►  WintunTunManager (unchanged engine code)
                                        ├─ tun2socks.exe + wintun.dll  → se7en_tun adapter
                                        ├─ WintunRouteApi   (iphlpapi: routes, IPs, TCP table)
                                        ├─ WintunDnsShell   (netsh: adapter DNS)
                                        ├─ SocksDnsForwarder(127.0.0.1:53 → SOCKS5)
                                        └─ SplitRules       (domain / IP / per-app split tunnelling)
```

## What is in here

| Path | Origin |
|---|---|
| `Se7enTun/Services/Tun/*.cs` (17 files) | `Se7enPro/Services/Tun/*` — WintunTunManager (partial class), SocksDnsForwarder (partial class), SplitRules, WintunRouteApi, WintunDnsShell |
| `Se7enTun/Services/ITunManager.cs`, `ITunnelCoreManager.cs`, `ISettingsService.cs`, `IChildProcessGuard.cs`, `ISystemProxyService.cs` | contracts the engine is built against |
| `Se7enTun/Services/{FileCacheHelper,AdminElevation,ConnectionMethod,LocalizedItem,Loc,DnsResolverPolicy,DnsSettings}.cs` | direct engine dependencies |
| `Se7enTun/Models/{UserSettings,Notice}.cs` | settings model (`SystemWideTunneling`, `SplitTunnel*`, `CustomDns*`) and `ConnectionState` |
| `Se7enTun/Resources/tun2socks/{tun2socks.exe,wintun.dll}` | data plane binaries (GPLv3 / GPLv2) |
| `Se7enTun/Resources/Strings.{en,ru,zh}.xaml` | original localisation tables (optional, see below) |
| `Se7enTun/Host/*.cs` | **new** façade: `TunHost`, `TunOptions`, and the default collaborators |
| `Se7enTun.Tests/*.cs` | **new** unit tests (xunit) |

Not included, on purpose: the connection engines (Psiphon / Xray / sing-box / Tor / Aether /
Shard), the WPF UI and the settings persistence — the TUN engine only needs a *local SOCKS5
port*, which the host supplies.

## Requirements

* Windows 10/11 (build 19041+), .NET SDK 8.0
* The **host process must run as Administrator** at runtime — Wintun adapter creation and
  route installation need it. `TunHost.IsElevated` reports the current state; without
  elevation the engine reports `TunState.Error` with the localised message.

## Build & test

```powershell
# library (x64)
dotnet build Se7enTun\Se7enTun.csproj -c Release -r win-x64 --self-contained false

# library (x86)
dotnet build Se7enTun\Se7enTun.csproj -c Release -r win-x86 --self-contained false

# everything (library + tests)
dotnet build Se7enTun.sln -c Release

# unit tests
dotnet test Se7enTun.Tests\Se7enTun.Tests.csproj -c Release
```

Output: `Se7enTun\bin\Release\net8.0-windows10.0.19041.0\Se7enTun.dll`

Continuous integration runs on every push/PR (`.github/workflows/ci.yml`):

1. **hygiene** — no credential/secret material may be tracked,
2. **build** — library for `win-x64` and `win-x86` with `-warnaserror`, then the whole solution,
3. **test** — the xunit suite.

## Usage

```csharp
using Se7enTun;
using Se7enPro.Services;   // TunState

// Your SOCKS5 upstream is already listening on 127.0.0.1:1080.
await using var tun = new TunHost(new TunOptions
{
    SocksPort = 1080,
    Log = line => Console.WriteLine(line),   // optional engine diagnostics
});

tun.StateChanged += (_, _) => Console.WriteLine($"TUN {tun.State} {tun.LastError}");

if (!tun.IsElevated)
{
    Console.Error.WriteLine("run me elevated");
    return;
}

bool running = await tun.StartAsync(TimeSpan.FromSeconds(60));
// ...
await tun.StopAsync();
```

Split tunnelling and DNS resolvers are plain settings, published with `Save()`:

```csharp
tun.Settings.SplitTunnelEnabled = true;
tun.Settings.SplitTunnelMode = "exclude";                  // or "include"
tun.Settings.SplitTunnelEntries.Add(new SplitTunnelEntry { Kind = "ip", Value = "8.8.8.8/32" });
tun.Settings.SplitTunnelEntries.Add(new SplitTunnelEntry { Kind = "domain", Value = "example.com" });
tun.Settings.SplitTunnelEntries.Add(new SplitTunnelEntry { Kind = "app", Value = "chrome" });
tun.Settings.CustomDnsUdp = "9.9.9.9";
tun.Save();                                                // live re-apply, no restart
```

Other helpers: `SetSocksPort(int)`, `SetConnected(bool)`, `SetRoute(ip, sni)`,
`TunHost.DetectUnderlyingDnsServers()`, `TunHost.HasGlobalIpv6()` and
`TunHost.UnderlyingDnsServersOverride` (test seam, mirrors the original).

If you already own those services (dependency injection), use the advanced constructor:

```csharp
var tun = new TunHost(myTunnelCore, mySettings, myChildGuard, mySystemProxy, myLogger);
```

### SOCKS5 upstream on another host

`tun2socks` is always pointed at `socks5://127.0.0.1:{SocksPort}` (unchanged engine).
When `TunOptions.SocksHost` is *not* a loopback address, `TunHost` starts a local TCP
bridge (`SocksTcpForwarder`) on that port and relays to the real upstream. The bridge is
TCP-only, so keep the SOCKS5 listener local if you need UDP-based traffic (QUIC).

### Shipping the data plane

`tun2socks.exe` and `wintun.dll` are `Content` items with `CopyToOutputDirectory`, so they
are copied into your application's output as `Resources\tun2socks\*` — exactly where the
engine looks (`AppContext.BaseDirectory\Resources\tun2socks`). The engine then caches them
to `%LOCALAPPDATA%\Se7en\tun2socks\Se7enPro.Tun.exe` and writes its session log to
`%LOCALAPPDATA%\Se7en\logs\tun2socks.log` (32 MB cap).

### Localisation (optional)

English works with zero setup (`Loc.Of(...)` falls back to the English source string).
For `ru` / `zh`, merge this library's table into your `App.xaml` and apply the language
early, exactly like Se7en Pro does:

```xml
<ResourceDictionary Source="pack://application:,,,/Se7enTun;component/Resources/Strings.ru.xaml" />
```

```csharp
Loc.Apply("ru");   // or "zh" — also sets the UI culture
```

## Notes

* The original Se7en Pro repository is **not modified** by this extraction; both trees can
  evolve independently. The engine files are byte-identical copies, so re-syncing is a
  plain file copy.
* `tun2socks.exe` (GPLv3, xjasonlyu/tun2socks) and `wintun.dll` (GPLv2, wintun.net) keep
  their original licenses.
* The elevated end-to-end integration harness from the parent project
  (`tools/TunEngineTest`) is not ported here: it drives a real adapter and therefore
  cannot run on a CI worker.
