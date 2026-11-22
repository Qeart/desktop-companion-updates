# Desktop Companion

Aurum Meter for AION 2: a Windows desktop overlay with party/force DPS and healing,
pet progress, a pet atlas, Hidden Cube guidance and PvE equipment/Arcana advice.
This is an independent fork, with no affiliation to NCSOFT or Kisk.

## Install

Open [Releases](https://github.com/Qeart/desktop-companion-updates/releases), choose
the latest stable release and download its Windows **Setup.exe**. The application
is self-contained: a separate .NET installation and Npcap are not required for PktMon.
Windows requests administrator permission when PktMon capture starts.
OCR features use a Windows text recognition language (English recommended).
If installing over an older portable copy, close that copy from its tray menu first.

Use the round floating menu or Ctrl+Shift+A. Ctrl+Shift+M toggles the meter;
Ctrl+Shift+G opens the pet atlas. Hold Ctrl over an equipment tooltip for advice.

## Updates and local data

The ↻ button in the meter footer and **Aktualizacje** in the tray menu open the updater.
The default channel is **Stabilny**. **Testowy** also considers beta releases.
Checks run on startup and once an hour when enabled. Downloads do not restart the
application; use **Uruchom ponownie** after combat to apply the verified package.
Failed downloads leave the current application running. No GitHub credentials are
bundled or required for this public repository.

Installed copies keep settings, character caches, pet progress and fights in
`%LocalAppData%\DesktopCompanionData`, outside the application directory replaced
by updates. Portable development copies retain their adjacent `user-data` folder.
The updates window can import data from an older `user-data` folder. Existing data
is preserved; restart the application to load imported values.
No player data or captures are uploaded by the application.

## Build

On Windows x64, install the .NET 10 SDK, then run:

```powershell
dotnet test Aurum.Tests/Aurum.Tests.csproj -c Release
./scripts/Build-Release.ps1 -Version 0.2.0 -Channel stable
```

The script restores pinned Velopack 1.2.161 tooling, tests, publishes a self-contained
application, builds an installer/update feed and includes a corresponding source ZIP
and SHA-256 checksums. Output is in `artifacts/releases/stable`.
Development/debug builds are not installed Velopack applications and cannot apply updates.

## Publish a version

1. Commit the final code and edit `RELEASE-NOTES.md` for that version.
2. Open **Actions → Prepare release → Run workflow**.
3. Choose a new version number and `stable` or `beta` (for example `0.3.0-beta.1`).
4. Wait for the tests/build and inspect the **draft** release and its installer/source assets.
5. Publish the draft when ready. Only published releases are visible to users' updaters.

Existing published version numbers are not reused. Ordinary commits do not publish updates.
The workflow reuses the previous channel package for delta generation when available.
An actual second-computer installation and in-game check are required before marking
a release stable. Code signing is not configured until a publisher certificate/service
is provided; see the release notes for the signing status of a build.

## Licence and attribution

The combined application is **GPL-3.0**. See [LICENSE](LICENSE), [NOTICE.txt](NOTICE.txt),
[data/NOTICE.md](data/NOTICE.md), and the files under `licenses/`.
The combat parser/engine is a modified AionMeter.Core fork from
[cyberbadger6969/aion2-dps-meter](https://github.com/cyberbadger6969/aion2-dps-meter).
The UI and local extensions are documented in the notices; this is not an exclusively
authored replacement for that upstream work. Game artwork/data retain their original
ownership. Every distributed binary release includes corresponding sources and notices.
