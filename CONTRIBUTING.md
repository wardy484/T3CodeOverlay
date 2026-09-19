# Contributing

Thanks for helping make T3 Code Overlay better.

## Set up

1. Install the .NET 10 SDK and XIVLauncher with Dalamud.
2. Clone the repository.
3. Build the release package:

   ```bash
   dotnet restore Browsingway/Browsingway.csproj --locked-mode -p:Platform=x64
   dotnet build Browsingway/Browsingway.csproj -c Release --no-restore -p:Platform=x64
   ```

4. Add the absolute path to `out/T3CodeOverlay.dll` under `/xlsettings` → **Experimental** → **Dev Plugin Locations**.
5. Enable **T3 Code Overlay** under `/xlplugins` → **Dev Tools**.

On Windows, Dalamud is expected at `%APPDATA%\XIVLauncher\addon\Hooks\dev`. Set `DALAMUD_HOME` when it lives elsewhere.

## Architecture

- `Browsingway/` contains the Dalamud plugin, bubble, settings, status monitors, and DirectX integration.
- `Browsingway.Renderer/` is the isolated CefSharp renderer process.
- `Browsingway.Common/` contains the shared-memory RPC contract used by both processes.
- `Browsingway/DalamudPackager.targets` assembles the renderer and plugin into `latest.zip`.

The renderer runs out of process so Chromium failures do not take down FFXIV. Each plugin load uses a unique IPC channel so hot reloads cannot collide with a renderer that Wine or Windows is still shutting down.

## Pull requests

- Keep changes focused and explain the user-visible result.
- Run a locked Release build before opening the PR.
- Test loading, opening, closing, focusing, dragging, and unloading the plugin when touching runtime behaviour.
- Never commit T3 data, Dalamud logs, credentials, downloaded Chromium files, or build output.
- Disclose meaningful AI assistance in the PR. This is required because releases may be submitted to Dalamud's official repository.

Windows CI must pass. Linux/Wine testing is welcome but does not replace the Windows build.
