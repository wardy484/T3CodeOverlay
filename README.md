# T3 Linkpearl

T3 Code inside Final Fantasy XIV. A draggable bubble opens the real T3 Code web client in an interactive, rounded Chromium overlay.

Features include:

- A draggable Messenger-style bubble with working and attention badges.
- An attached browser window with configurable size, zoom, frame rate, and idle opacity.
- A smooth bubble-to-window animation that can be disabled.
- Automatic T3 connection checks and cross-platform thread-state discovery.
- Native Windows support and experimental Linux/Wine support.

## Install

T3 Linkpearl is currently a testing release.

1. Install [XIVLauncher](https://goatcorp.github.io/).
2. Open Dalamud settings with `/xlsettings`, select **Experimental**, and add:

   ```text
   https://wardy484.github.io/T3Linkpearl/repo.json
   ```

3. Open `/xlplugins`, search for **T3 Linkpearl**, and install it.
4. Start T3 Code. The plugin expects `http://127.0.0.1:3773` by default.
5. Approve the one-time Chromium dependency download and click the T3 bubble.

The downloaded Chromium archive is supplied by Browsingway and verified against a pinned SHA-256 checksum before extraction.

## Commands

- `/t3` — show or hide T3 Code.
- `/t3 show` — show T3 Code.
- `/t3 config` — open settings.

## Thread badges

The plugin looks for `.t3/userdata/state.sqlite` beneath the current Windows user profile. It also supports:

- `T3_DATA_DIR`, pointing to `.t3`, `userdata`, or `state.sqlite`.
- A manual path under `/t3 config`.
- Wine installations where `.t3` is found by walking up from the Dalamud plugin configuration directory.

The browser overlay still works when thread-state data is unavailable.

## Development

Requirements:

- .NET 10 SDK.
- A current Dalamud development distribution.
- Windows, or Linux with an existing XIVLauncher.Core installation.

Build and package:

```bash
dotnet restore Browsingway/Browsingway.csproj --locked-mode -p:Platform=x64
dotnet build Browsingway/Browsingway.csproj --configuration Release --no-restore -p:Platform=x64
```

The dev-plugin entry point is `out/T3Linkpearl.dll`. The installable package is `out/T3Linkpearl/latest.zip`.

See [CONTRIBUTING.md](CONTRIBUTING.md) for local setup, architecture, and pull-request expectations.

## Development disclosure

T3 Linkpearl has been entirely vibe coded: Kim directed the product, tested it in FFXIV, and made the release decisions, while the implementation and documentation were written with AI coding agents. This is disclosed plainly so users and contributors can judge the project with the right context.

AI assistance does not lower the bar for changes. Maintainers are responsible for understanding what ships, reviewing contributions, testing user-visible behaviour, and responding to security reports.

## Support

Please use [GitHub Issues](https://github.com/wardy484/T3Linkpearl/issues). Include your OS, Dalamud version, plugin version, and the relevant lines from `dalamud.log`. Do not attach the complete log without checking it for private information.

## Attribution and licence

T3 Linkpearl is derived from [Browsingway](https://github.com/Styr1x/Browsingway), which itself builds on [BrowserHost](https://github.com/ackwell/BrowserHost). The Chromium renderer, DirectX shared-texture transport, and input forwarding originate from those projects.

It also relies on [CefSharp](https://github.com/cefsharp/CefSharp), the [Chromium Embedded Framework](https://github.com/chromiumembedded/cef), [Dalamud](https://github.com/goatcorp/Dalamud), and other dependencies listed in the locked NuGet manifests. Their own licences and notices apply.

The project remains licensed under [GPL-3.0](LICENSE). See [NOTICE.md](NOTICE.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for attribution. It is not affiliated with Square Enix or the Dalamud project.
