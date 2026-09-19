---
name: t3-linkpearl
description: Build, debug, package, or release the T3 Linkpearl Dalamud plugin, including its separate Chromium renderer and T3 integration.
---

# T3 Linkpearl

Read the repository-root `AGENTS.md` before changing code.

Work from the existing architecture: the Dalamud plugin presents a shared DirectX texture, while `Browsingway.Renderer` owns off-screen Chromium in a separate process. Keep T3 unavailable states graceful and keep private T3/thread data local.

For ordinary changes:

1. Trace the behaviour across plugin, renderer, and common IPC before editing.
2. Make the smallest coherent change using existing patterns.
3. Restore with the lock file and build the Release x64 package using the commands in `AGENTS.md`.
4. Check the produced ZIP when packaging changed.
5. Report compilation and in-game verification separately.

When changing CefSharp or CEF, treat the managed NuGet packages, native renderer archive, archive checksum, and Wine/Windows smoke tests as one coordinated upgrade. Never bump only the NuGet dependency.

When releasing, require a green Windows package workflow and a real in-game smoke test. Preserve the GPL/upstream notices and the README's AI-development disclosure.
