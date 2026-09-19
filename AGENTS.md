# T3 Linkpearl agent guide

T3 Linkpearl is a Dalamud plugin that embeds the local T3 Code web client in FFXIV. It is a focused fork of Browsingway, not a general-purpose browser plugin.

## Product boundaries

- Keep `/t3` as the public command and `T3Linkpearl` as the assembly and Dalamud internal name.
- Preserve the focused linkpearl experience. Do not reintroduce Browsingway's generic multi-overlay UI without an explicit product decision.
- Treat the T3 HTTP service and SQLite database as external boundaries. A missing service or database must not crash the plugin or prevent settings from opening.
- Never send thread data, URLs, logs, or gameplay data to a remote service.
- Keep Windows as the supported release target. Linux/Wine support is useful but experimental.

## Architecture

- `Browsingway/` contains the Dalamud plugin, settings, T3 status monitors, input forwarding, and DirectX texture presentation.
- `Browsingway.Renderer/` is the separate off-screen Chromium process. Do not move CEF into the game process.
- `Browsingway.Common/` contains the shared IPC protocol.
- `Browsingway/DalamudPackager.targets` assembles `out/T3Linkpearl/latest.zip`.
- The Browsingway namespaces and project directory names are retained to keep upstream history legible.

The renderer and plugin use uniquely named IPC resources. Preserve that property: fixed shared-memory names caused renderer crashes during hot reload.

## Working safely

- Inspect the existing owner of a behaviour before changing it. Keep changes small and use existing patterns.
- Do not log message contents, thread titles, authentication data, or full local paths at normal log levels.
- Changes to CefSharp must update the managed packages and native CEF archive together. Their versions must match. Update and verify the pinned SHA-256 checksum.
- Do not claim Windows runtime support from compilation alone. CI proves that the package builds; an in-game Windows smoke test proves runtime behaviour.
- Preserve GPL-3.0 licensing, upstream copyright, and the notices in `NOTICE.md`.
- Keep the AI-development disclosure accurate. Do not remove or soften it to improve presentation.

## Build and verify

Use the locked dependency graph:

```bash
dotnet restore Browsingway/Browsingway.csproj --locked-mode -p:Platform=x64
dotnet build Browsingway/Browsingway.csproj --configuration Release --no-restore -p:Platform=x64
```

Before shipping, verify:

- The build has no errors or warnings.
- `scripts/Test-Package.ps1` passes on Windows PowerShell.
- The ZIP contains `T3Linkpearl.dll`, `T3Linkpearl.json`, renderer files, and root image assets.
- `/t3`, `/t3 config`, bubble dragging, focus/dimming, animation, badges, and renderer restart work in-game.
- The Windows GitHub Actions build passes.

Do not create a release tag unless its version matches the packaged manifest exactly.
