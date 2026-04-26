# Building & Distributing PiggyBank

PiggyBank ships as a Windows desktop installer produced by [Velopack](https://velopack.io/). A single `setup.exe` installs the app per-user under `%LocalAppData%\PiggyBank` (no UAC prompt) and self-updates from GitHub Releases.

This document covers:
1. Prerequisites
2. Producing a local installer (smoke test before tagging)
3. Publishing a release via GitHub Actions
4. Auto-update at runtime (current state and trade-offs)

---

## 1. Prerequisites

- **.NET 10 SDK** — same version used for the rest of the solution.
- **Velopack CLI (`vpk`)** — installed as a global dotnet tool:
  ```
  dotnet tool install -g vpk
  ```
  The version must match the `Velopack` NuGet referenced by `src/PiggyBank.App/PiggyBank.App.csproj` (currently `0.0.1298`). If you upgrade one, upgrade the other in the same commit.
- **Windows** — `vpk` only produces Windows installers from a Windows host.

Verify with:
```
dotnet --version
vpk --help
```

---

## 2. Producing a local installer

From the repo root:

```
dotnet publish src/PiggyBank.App/PiggyBank.App.csproj -c Release -r win-x64 --self-contained -o publish/PiggyBank
vpk pack -u PiggyBank -v 0.1.0 -p publish/PiggyBank -e PiggyBank.App.exe
```

The flags:

| Flag | Meaning |
| ---- | ------- |
| `-u PiggyBank`             | Package id. Must stay constant across releases — this is how Velopack matches old installs to new packages. |
| `-v 0.1.0`                 | Semver version. Increment per release; lower versions are rejected. |
| `-p publish/PiggyBank`     | Folder containing the published, self-contained app. |
| `-e PiggyBank.App.exe`     | Main entry-point exe (relative to `-p`). Velopack sets this as the launch target and Start-menu shortcut target. |

After packing you'll find a `Releases/` folder in the repo root containing:

- `PiggyBank-Setup.exe` — the bootstrapper to ship to users (or upload to GitHub Releases).
- `RELEASES` — manifest used by the auto-updater to discover the latest version.
- `PiggyBank-<version>-full.nupkg` — the full app payload.
- (On subsequent versions) `PiggyBank-<version>-delta.nupkg` — binary diff against the previous release for bandwidth-friendly updates.

**Smoke test:** double-click `PiggyBank-Setup.exe`. It should install silently to `%LocalAppData%\PiggyBank` and launch the app. Uninstall via Add/Remove Programs to roll back.

> **Don't commit `Releases/` or `publish/`.** Both are build artefacts.

---

## 3. Publishing a release via GitHub Actions

The `.github/workflows/release.yml` workflow runs on a tag push that matches `v*` (e.g. `v0.1.0`). It:

1. Checks out the tagged commit.
2. Sets up .NET 10.
3. Installs `vpk` globally.
4. Publishes a self-contained `win-x64` build of `PiggyBank.App`.
5. Strips the leading `v` from the tag to form the Velopack version.
6. Runs `vpk pack` with that version, writing artefacts to `Releases/`.
7. Creates a GitHub Release at the tag and uploads everything in `Releases/` as release assets.

To cut a release:

```
git tag v0.1.0
git push origin v0.1.0
```

The workflow uses `secrets.GITHUB_TOKEN` (auto-provisioned by GitHub) — no extra setup required.

**Tag style:** semver only. `v0.1.0`, `v0.2.0-rc.1` etc. The `v` prefix is mandatory because the workflow trigger is `tags: ['v*']`.

---

## 4. Auto-update at runtime — current state

The app calls `Velopack.VelopackApp.Build().Run()` at the very top of `App.OnStartup`. This is the half of Velopack's runtime that **must** be wired for installs to work — it lets the bootstrapper short-circuit normal startup when invoked with `--firstrun`, `--install`, `--uninstall`, etc. **Without this line, `setup.exe` cannot complete an install.** It's a one-liner, always safe, no network calls.

What's **not** wired yet:

- A periodic / startup `UpdateManager.CheckForUpdatesAsync(...)` against `new GithubSource("https://github.com/Yokomoko/PiggyBank", null, false)` to pull and stage updates while the app runs.

**Trade-off:** v1 ships without runtime polling. Users get updates by either (a) re-running `setup.exe` or (b) downloading the latest installer from GitHub Releases. The Velopack "delta-update on next launch" experience is *not* yet enabled. We accepted this for v1 because:

- It removes a moving part from the first release (no fire-and-forget background task, no "update check failed" telemetry to design for).
- The setup.exe path is well-tested; the runtime polling path adds an extra surface to validate.
- Wiring it later is small: a single `UpdateChecker` service, registered as a singleton, kicked off from `MainWindow`'s constructor on a try/catch-wrapped `Task.Run`.

When we wire it, the rough shape will be:

```csharp
// new file: src/PiggyBank.App/Updates/UpdateChecker.cs
public sealed class UpdateChecker
{
    private const string RepoUrl = "https://github.com/Yokomoko/PiggyBank";

    public async Task CheckAsync(CancellationToken ct)
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null) return;
            await mgr.DownloadUpdatesAsync(info, cancelToken: ct).ConfigureAwait(false);
            // Apply on next launch — don't yank the app out from under the user.
        }
        catch
        {
            // Never let an update check crash the app.
        }
    }
}
```

`MainWindow` then does `_ = updateChecker.CheckAsync(CancellationToken.None);` once the shell is fully up.

---

## Common gotchas

- **Don't skip `vpk pack`**: a raw `dotnet publish` produces a folder, not an installer. `vpk` is what wraps it into `setup.exe`.
- **Don't downgrade the version**: Velopack rejects releases whose version is lower than an existing one in `Releases/`. If you need to redo a release, bump the patch number (`0.1.0` -> `0.1.1`).
- **`-r win-x64 --self-contained` is required**: framework-dependent publishes won't run on a fresh PC without the correct runtime installed.
- **Don't commit a copy of `vpk`**: it's a global dotnet tool. The workflow installs it fresh per run; locally each developer installs it once.
