# Se7en TUN

The complete **WinTUN + tun2socks subsystem of [Se7en Pro](https://github.com/Delta-Kronecker/Se7en-Pro)**
extracted into its own standalone **class library (.dll)**.

> **What this is:** a TUN (virtual network adapter) engine. You supply a local **SOCKS5 port**
> (from sing-box / xray / v2rayN / your own proxy), this library routes the machine's traffic
> into it through a Wintun adapter named `se7en_tun`. **This library is not a VPN client and
> does not include any proxy server.**

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

## Contents

| Path | Origin |
|---|---|
| `Se7enTun/Services/Tun/*.cs` (17 files) | `Se7enPro/Services/Tun/*` — WintunTunManager (partial class), SocksDnsForwarder (partial class), SplitRules, WintunRouteApi, WintunDnsShell |
| `Se7enTun/Services/ITunManager.cs`, `ITunnelCoreManager.cs`, `ISettingsService.cs`, `IChildProcessGuard.cs`, `ISystemProxyService.cs` | contracts the engine is built against |
| `Se7enTun/Services/{FileCacheHelper,AdminElevation,ConnectionMethod,LocalizedItem,Loc,DnsResolverPolicy,DnsSettings}.cs` | direct engine dependencies |
| `Se7enTun/Models/{UserSettings,Notice}.cs` | settings model (`SystemWideTunneling`, `SplitTunnel*`, `CustomDns*`) and `ConnectionState` |
| `Se7enTun/Resources/tun2socks/{tun2socks.exe,wintun.dll}` | data plane binaries (GPLv3 / GPLv2) |
| `Se7enTun/Resources/Strings.{en,ru,zh}.xaml` | original localisation tables (copied by the host app, see [Localisation](#localisation)) |
| `Se7enTun/Host/*.cs` | **new** façade: `TunHost`, `TunOptions`, and the default collaborators |
| `Se7enTun.Tests/*.cs` | **new** unit tests (xunit) |

Not included, on purpose: the connection engines (Psiphon / Xray / sing-box / Tor / Aether /
Shard), the WPF UI and the settings persistence — the TUN engine only needs a *local SOCKS5
port*, which the host supplies.

---

## Requirements

* Windows 10/11 build 19041+ (the Wintun adapter and `netsh`/`iphlpapi` calls)
* .NET 8 runtime (the library targets `net8.0-windows10.0.19041.0`)
* The **host process must run as Administrator** — adapter creation and route installation
  need it. `TunHost.IsElevated` reports the current state; without elevation the engine
  goes to `TunState.Error` with the message *"System-wide tunneling needs Administrator
  privileges…"*
* Your own **local SOCKS5 endpoint** (e.g. `127.0.0.1:1080`)

---

## Referencing the library

### Option A — project reference (recommended)

```xml
<ItemGroup>
  <!-- wherever this repository sits relative to your application -->
  <ProjectReference Include="..\tun\Se7enTun\Se7enTun.csproj" />
</ItemGroup>
```

Everything (the DLL, `Microsoft.Extensions.Logging.Abstractions.dll` and the
`Resources\tun2socks\*` data plane) is wired into your output automatically.

### Option B — prebuilt DLL (no source checkout)

Every green CI run uploads two artifacts — **`Se7enTun-win-x64`** and
**`Se7enTun-win-x86`** — downloadable from the
[Actions tab](https://github.com/Delta-Kronecker/tun/actions). Each zip contains:

```
Se7enTun.dll              (x64 in one artifact, x86 in the other)
Se7enTun.pdb
Se7enTun.deps.json
Resources/tun2socks/tun2socks.exe
Resources/tun2socks/wintun.dll
```

Wire it into your application like this:

```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
  <UseWPF>true</UseWPF>   <!-- required, see note below -->
</PropertyGroup>

<ItemGroup>
  <Reference Include="Se7enTun">
    <HintPath>libs\Se7enTun.dll</HintPath>
  </Reference>
  <!-- the engine logs through Microsoft.Extensions.Logging; needed at runtime -->
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.1" />

  <!-- and the data plane must sit next to your executable: -->
  <Content Include="libs\Resources\tun2socks\**\*" Link="Resources\tun2socks\%(RecursiveDir)%(Filename)%(Extension)">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

> **`<UseWPF>true</UseWPF>` is required** in the consuming project. The engine's
> diagnostics (`Loc`) and connection-method code use WPF types; without the Windows Desktop
> framework they fail exactly when an error message should be printed.

### Where the files must end up

The engine looks for its binaries in **`<your exe folder>\Resources\tun2socks\`**
(`AppContext.BaseDirectory`), never in the DLL's own folder:

```
YourApp.exe
YourApp.dll
Se7enTun.dll
Microsoft.Extensions.Logging.Abstractions.dll
Resources\
  tun2socks\
    tun2socks.exe      ← copied to %LOCALAPPDATA%\Se7en\tun2socks\Se7enPro.Tun.exe on first start
    wintun.dll         ← same cache folder
```

The bundled `tun2socks.exe` / `wintun.dll` are **x64** and run as a *separate process*, so an
x86 host application drives them without any problem; only `Se7enTun.dll` itself should match
your application's platform (x64 artifact → x64 app, x86 artifact → x86 app; a `ProjectReference`
build matches automatically).

---

## Usage

### Minimal console host

```csharp
using System;
using System.Threading.Tasks;
using Se7enTun;
using Se7enPro.Services;      // TunState

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // port of your local SOCKS5 server (sing-box, xray, v2rayN, …)
        var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 1080;

        await using var tun = new TunHost(new TunOptions
        {
            SocksPort = port,
            Log = line => Console.WriteLine(line),      // engine diagnostics
        });

        tun.StateChanged += (_, _) =>
            Console.WriteLine($"[tun] {tun.State}" +
                              (tun.LastError is null ? "" : $" — {tun.LastError}"));

        if (!tun.IsElevated)
        {
            Console.Error.WriteLine("Administrator privileges required.");
            return 2;
        }

        if (!await tun.StartAsync(TimeSpan.FromSeconds(60)))
        {
            Console.Error.WriteLine($"TUN failed to start: {tun.LastError}");
            return 1;
        }

        Console.WriteLine("TUN is up (adapter se7en_tun). Press Enter to stop.");
        Console.ReadLine();

        await tun.StopAsync();
        return 0;
    }
}
```

That is the whole contract:

1. **Build** a `TunHost` with a `SocksPort` that already has a SOCKS5 server listening.
2. **`StartAsync()`** → returns `true` when the adapter is `Running`, `false` (with
   `LastError` set) on error/timeout.
3. **`StopAsync()`** / `await using` → tears the adapter down and kills `tun2socks.exe`.

The default stub reports `ConnectionState.Connected`, so the tunnel comes up without any
extra wiring. Advanced hosts may call `SetConnected(bool)`, `SetSocksPort(int)` and
`SetRoute(ip, sni)` to drive the engine from their own connection lifecycle.

### WPF toggle button

```csharp
using System;
using System.Windows;
using Se7enTun;
using Se7enPro.Services;

public partial class MainWindow : Window
{
    private readonly TunHost _tun = new(new TunOptions { SocksPort = 1080 });

    public MainWindow()
    {
        InitializeComponent();
        _tun.StateChanged += (_, _) => Dispatcher.Invoke(() =>
            Status.Text = $"{_tun.State}{(_tun.LastError is null ? "" : " — " + _tun.LastError)}");
    }

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        if (_tun.State is TunState.Running or TunState.Starting)
            await _tun.StopAsync();
        else if (!await _tun.StartAsync())
            MessageBox.Show(_tun.LastError ?? "TUN failed to start.");
    }
}
```

### `TunOptions`

| Property | Default | Meaning |
|---|---|---|
| `SocksPort` | *(required, 1–65535)* | local SOCKS5 port the tunnel forwards to |
| `SocksHost` | `127.0.0.1` | upstream SOCKS5 host. If it is **not** a loopback address, a local TCP bridge is started on `127.0.0.1:SocksPort` (see below) |
| `SetSystemProxy` | `false` | clear the system proxy while the TUN is up, restore it afterwards (opt-in — a library never touches machine-wide proxy settings unasked) |
| `HttpProxyPort` | `0` | port advertised for system-proxy restoration when `SetSystemProxy` is on |
| `Language` | `null` | `en` / `ru` / `zh`; `null` keeps English and never touches culture |
| `Log` | `null` | `Action<string>` sink for engine diagnostics (also written to the session log file) |
| `Logger` | `null` | pre-built `ILogger<WintunTunManager>`; wins over `Log` |
| `SystemProxy` | no-op | your own `ISystemProxyService` implementation |

### `TunHost` at a glance

| Member | Description |
|---|---|
| `new TunHost(TunOptions)` | builds all collaborators; nothing runs until `Start*` |
| `new TunHost(core, settings, guard, proxy, logger?)` | DI constructor for hosts that own those services |
| `Start()` / `Stop()` | flip `Settings.SystemWideTunneling` and publish (`Save()`) |
| `StartAsync(timeout = 60s, ct)` / `StopAsync(timeout = 30s, ct)` | start/stop and wait for `Running` / `Off`; `false` on `Error` or timeout |
| `Save()` | publish pending `Settings` changes to the engine |
| `Settings` | the `UserSettings` instance (split rules, DNS lists, …) |
| `State` / `LastError` / `StateChanged` | `Off → Starting → Running → Stopping → Off`, plus `Error` |
| `IsElevated` | current process is Administrator |
| `SetSocksPort(port)` | live upstream port change (restarts the data plane) |
| `SetConnected(bool)` | reports upstream connection state; the engine only starts on `Connected` |
| `SetRoute(ip, sni = "")` | optional host route pinned through the tunnel |
| `Manager` / `Core` | low-level `ITunManager` / `ITunnelCoreManager` |
| `Bridge` | the local TCP bridge, when `SocksHost` is remote |
| `DetectUnderlyingDnsServers()` / `HasGlobalIpv6()` | static helpers the engine also uses |
| `UnderlyingDnsServersOverride` | static test seam replacing resolver autodetection |
| `await using` | stops the engine, kills `tun2socks.exe`, closes the bridge |

### How start/stop actually works

`StartAsync()` sets `Settings.SystemWideTunneling = true` and publishes it. The engine
reconciles on every publish and spawns `tun2socks.exe` **only when all of these hold**:

* `SystemWideTunneling == true`
* the upstream reports `ConnectionState.Connected` (the built-in stub always does)
* `SocksProxyPort > 0`
* the process is elevated

Split-rule or DNS edits after that are re-applied **live** (routes only, the adapter stays
up); changing the SOCKS port restarts the data plane. `StopAsync()` flips the flag back and
waits for `TunState.Off`.

### Elevation

Pick one:

```xml
<!-- app.manifest of your application -->
<security>
  <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
</security>
```

or relaunch on demand from code (helper copied from Se7en Pro, relays `--autostart` /
`--minimized` only):

```csharp
using Se7enPro.Services;

if (!tun.IsElevated && AdminElevation.TryRestartElevated())
    return;   // the elevated instance is starting now
```

`TryRestartElevated()` works when the library is hosted in an `.exe`; libraries inside
services/other processes should do their own relaunch (`ProcessStartInfo.Verb = "runas"`).

### Split tunnelling

```csharp
using Se7enPro.Models;   // SplitTunnelEntry

var s = tun.Settings;
s.SplitTunnelEnabled = true;
s.SplitTunnelMode = "exclude";            // "include" = only these go through the tunnel

s.SplitTunnelEntries.Clear();
s.SplitTunnelEntries.Add(new SplitTunnelEntry { Kind = "domain", Value = "*.example.com" });
s.SplitTunnelEntries.Add(new SplitTunnelEntry { Kind = "ip",     Value = "8.8.8.8/32" });
s.SplitTunnelEntries.Add(new SplitTunnelEntry { Kind = "app",    Value = "chrome" });

tun.Save();                               // published to the engine immediately
```

| `Kind` | `Value` examples | Normalisation |
|---|---|---|
| `domain` | `example.com`, `*.example.com`, `https://www.Example.com/path?q=1` | scheme/path/port stripped, lowercased, `*.`/`.` trimmed (`www.` is **kept**) |
| `ip` | `8.8.8.8`, `8.8.8.8/32`, `2001:db8::/32` | CIDR kept as-is |
| `app` | `chrome` (name without `.exe`) or `C:\Program Files\…\chrome.exe` | full paths are matched by path, names by process name |

### Custom DNS resolvers

Each list is a separate transport; entries are separated by `,` `;` space or newline and
**at most 8 entries per list** are used.

```csharp
var s = tun.Settings;
s.CustomDnsUdp  = "9.9.9.9, 149.112.112.112";              // plain DNS (port 53, ":5353" allowed)
s.CustomDnsDot  = "tls://dns.google";                      // DNS over TLS (port 853)
s.CustomDnsDoh  = "https://cloudflare-dns.com/dns-query";  // DNS over HTTPS (port 443)
s.CustomDnsStrict = false;   // true = refuse any resolver you did not list (default: degrade gracefully)

tun.Save();
```

Validation helpers for building a settings UI live in `Se7enPro.Services.DnsSettings`
(`ParseList`, `ValidateEntry`, `Summarise`, `DnsTransport`).

### SOCKS5 upstream on another host

`tun2socks` is always pointed at `socks5://127.0.0.1:{SocksPort}` (unchanged engine).
When `TunOptions.SocksHost` is **not** a loopback address, `TunHost` starts a local TCP
bridge on that port and relays to the real upstream:

```csharp
await using var tun = new TunHost(new TunOptions
{
    SocksHost = "192.168.1.20",   // SOCKS5 lives on your NAS / another VM
    SocksPort = 1080,             // bridge listens on 127.0.0.1:1080 and forwards there
});
```

The bridge is **TCP-only** — keep the SOCKS5 listener local if you need UDP-based traffic
(QUIC, DNS over UDP). A bridge that cannot bind the port fails fast with
*"Cannot bridge 127.0.0.1:…"*.

### Shipping the data plane

`tun2socks.exe` and `wintun.dll` are `Content` items with `CopyToOutputDirectory`, so they
land in your application's output as `Resources\tun2socks\*` — exactly where the engine
looks. On first start the engine caches them to
`%LOCALAPPDATA%\Se7en\tun2socks\Se7enPro.Tun.exe` (a newer source copy replaces the cache
automatically) and writes the session log to `%LOCALAPPDATA%\Se7en\logs\tun2socks.log`
(32 MB cap).

### Localisation

English works with zero setup (`Loc.Of(...)` falls back to the English source string, and
`TunHost` never fails if a table is missing).

For `ru` / `zh` the string tables must live in **your application** (the engine resolves
`pack://application:,,,/Resources/Strings.{lang}.xaml` against the *entry assembly* — that
is how Se7en Pro does it):

1. Copy `Se7enTun/Resources/Strings.ru.xaml` (and/or `Strings.zh.xaml`) into your project's
   `Resources\` folder — the WPF SDK compiles `Resources\*.xaml` as `Page` by default.
2. Apply the language early:

```csharp
// applied automatically when the host is created:
await using var tun = new TunHost(new TunOptions
{
    SocksPort = 1080,
    Language = "ru",
});

// or from anywhere else, early during startup:
Se7enPro.Services.Loc.Apply("ru");   // also sets the UI culture (ru-RU / zh-CN)
```

### Diagnostics

* `TunOptions.Log` / `TunOptions.Logger` — every engine line (also goes to the session log).
* `tun.LastError` — the human-readable reason behind `TunState.Error`.
* Session log: `%LOCALAPPDATA%\Se7en\logs\tun2socks.log` (tun2socks stdout/stderr).
* Adapter: `netsh interface show interface` → **`se7en_tun`**.

---

## Troubleshooting

| Symptom / `LastError` | Cause | Fix |
|---|---|---|
| *System-wide tunneling needs Administrator privileges…* | not elevated | relaunch as Administrator (see [Elevation](#elevation)) |
| *Bundled tun2socks resources not found next to the app.* | `Resources\tun2socks\` missing next to your exe | copy the two files, check the `<Content>` items |
| *Bundled tun2socks resource missing: {file}* | only one of the two files present | ship both `tun2socks.exe` and `wintun.dll` |
| *Adapter 'se7en_tun' never appeared within Ns.* | permission/antivirus/Wintun issue | run elevated, inspect `%LOCALAPPDATA%\Se7en\logs\tun2socks.log`, check `netsh interface show interface` |
| *Se7enPro.Tun.exe exited (code=…)* | data plane crashed | read the tail of the session log (it is printed in `LastError`) |
| *The TUN core keeps exiting (≥N times in M min)…* | crash loop | same log; usually a bad SOCKS port or a broken upstream |
| *TUN startup failed: …* | exception inside the engine | `LastError` carries the message; enable `TunOptions.Log` |
| `StartAsync()` returns `false`, no `LastError` | still `Starting` when the timeout hit | raise the timeout, or your process is not elevated |
| *Cannot bridge 127.0.0.1:…* | local SOCKS port already taken | free the port or choose another one |
| DNS/QUIC broken with a remote `SocksHost` | bridge relays TCP only | run the SOCKS5 server locally |
| Tunnel up but no traffic | your SOCKS5 server is not running/reachable | verify `127.0.0.1:{SocksPort}` answers first (`curl --proxy socks5h://127.0.0.1:1080 https://example.com`) |

---

## Build & test

```powershell
# everything (library + tests), AnyCPU
dotnet build Se7enTun.sln -c Release

# library only, platform-specific
dotnet build Se7enTun\Se7enTun.csproj -c Release -p:Platform=x64
dotnet build Se7enTun\Se7enTun.csproj -c Release -p:Platform=x86

# or with a runtime identifier (matches the CI artifacts)
dotnet build Se7enTun\Se7enTun.csproj -c Release -r win-x64 --self-contained false

# unit tests
dotnet test Se7enTun.Tests\Se7enTun.Tests.csproj -c Release
```

Outputs:

* `Se7enTun\bin\Release\net8.0-windows10.0.19041.0\Se7enTun.dll` (AnyCPU build)
* `Se7enTun\bin\Release\net8.0-windows10.0.19041.0\win-x64\…` (RID build, as in CI artifacts)

Continuous integration runs on every push/PR (`.github/workflows/ci.yml`):

1. **hygiene** — no credential/secret material may be tracked,
2. **build** — library for `win-x64` and `win-x86` with `-warnaserror`, then the whole
   solution, and uploads both outputs as artifacts,
3. **test** — the xunit suite (engine rules, DNS parsing, SOCKS bridge, façade, shipping
   layout). Tests never bring up a real adapter, so they run without Administrator.

## Notes

* The original Se7en Pro repository is **not modified** by this extraction; both trees can
  evolve independently. The engine files are byte-identical copies, so re-syncing is a
  plain file copy.
* `tun2socks.exe` (GPLv3, xjasonlyu/tun2socks) and `wintun.dll` (GPLv2, wintun.net) keep
  their original licenses.
* The elevated end-to-end integration harness from the parent project
  (`tools/TunEngineTest`) is not ported here: it drives a real adapter and therefore
  cannot run on a CI worker.
