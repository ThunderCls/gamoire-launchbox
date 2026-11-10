# Gamoire for LaunchBox

A [LaunchBox](https://www.launchbox-app.com) plugin that sends your library to your [Gamoire](https://gamoire.app)
account: play time, status, favourites and ratings for every game, whichever launcher it came from.

## Install

1. Download `Gamoire-LaunchBox-<version>.zip` from the latest [GitHub Release](../../releases).
2. Close LaunchBox, then unzip the `Gamoire` folder into `LaunchBox\Plugins`.
3. Start LaunchBox.

Needs LaunchBox 14.1 or newer.

## Connect

1. In LaunchBox open **Tools > Gamoire**.
2. Leave **Server** at `https://gamoire.app`, or enter your own Gamoire address. Only `https://` is accepted, plus
   `http://` for `localhost`, `127.0.0.1` and `[::1]`.
3. Click **Connect**. The window shows an 8-letter code (`XXXX-XXXX`) and opens Gamoire in your browser.
4. Sign in to Gamoire if asked, type the code, check the PC name and username, and click **Allow**.
5. The plugin finishes pairing and runs a first sync.

Pairing happens in the LaunchBox desktop window only. Once paired, Big Box syncs in the background too.
Only approve a code you just saw in LaunchBox yourself.

## What syncs

Sent for each game: LaunchBox game id, title, storefront (Steam, Epic, GOG, ...) and store game id, release year,
platform, play time, last played, added and modified dates, play status, favourite and star rating.

- A full sync runs when LaunchBox starts, when you click **Sync now**, right after pairing and every hour while
  LaunchBox runs.
- A single game is sent a moment after you stop playing it.
- LaunchBox counts only the play time of games it launched itself; time played elsewhere is not in the figures it
  sends.
- **Skip hidden games** (on by default): hidden games never leave this PC. Hidden games already sent stay in Gamoire
  if you turn this on later.
- Emulated games sync once you've played, favourited, rated or given them a status, so a big ROM collection doesn't
  fill your library.
- If Gamoire can't be reached, the plugin retries quietly with a growing delay (up to 30 minutes). Problems you must
  act on show as a LaunchBox notification (not in Big Box).

What Gamoire does with the data (which fields it applies, adding missing games) is switched in Gamoire under
**Settings > Integrations**, not here.

## Disconnect

**Disconnect** in the window revokes this PC in Gamoire and deletes the local tokens. You can also remove a PC in
Gamoire under **Settings > Integrations**; the window then shows "Disconnected by Gamoire - Connect again".
Disconnect before changing the **Server**.

## Privacy and security

- Tokens are stored with Windows DPAPI (current user) in `%LOCALAPPDATA%\Gamoire\LaunchBox`, one file per server
  address, next to `settings.json`. DPAPI does not stop malware that runs as the same Windows user.
- The plugin trusts nothing the server sends: it builds the browser address itself, never follows redirects and shows
  server messages as plain text.
- Game names, device names and tokens are never written to LaunchBox's log.
- Gamoire is sent the PC's name (the Windows computer name) so you can tell your PCs apart.

## Build

```bash
dotnet build Gamoire.LaunchBox.sln -c Release
dotnet test Gamoire.LaunchBox.sln -c Release
```

`lib/Unbroken.LaunchBox.Plugins.dll` is the compile-time SDK reference copied from LaunchBox (see `lib/README.md`);
`scripts/Verify-Lib.ps1` checks it against `lib/SHA256SUMS`. It is never shipped. To make the zip after a Release
build, run `./scripts/Pack.ps1 -Version <version>`; it writes `artifacts/Gamoire-LaunchBox-<version>.zip` and fails
unless the output is only `GamoireLaunchBox.dll` and its `.deps.json`.

## Releasing

The version in `src/GamoireLaunchBox/GamoireLaunchBox.csproj` is the one source. To release, in the source repository:

```powershell
./scripts/Set-Version.ps1 -Version 1.0.1
```

`./scripts/Set-Version.ps1 -Check` verifies the csproj version (the PR pipeline runs it); `-Check v1.0.1` also requires
a tag to match. Commit that; when it reaches `main`, the mirror pushes the code and then the tag `v1.0.1` (if GitHub
lacks it), and `.github/workflows/release.yml` checks the tag, verifies `lib`, builds, tests, packs the zip and creates
the release with it attached. A change without a version bump is only mirrored.

Failed release: if it failed after the release was created, the workflow already deleted that release and its tag; fix
the cause, then re-run the mirror pipeline (or merge the fix). If it failed earlier (check, build, test, pack), delete
the tag on GitHub first.

## License

MIT, see [LICENSE](LICENSE).
