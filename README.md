# Speck Sequence Helpers

A [N.I.N.A.](https://nighttime-imaging.eu/) 3.x plugin with three advanced-sequencer instructions:

- **Dithered slew and center** — NINA's Center with one difference: the target is displaced by
  a small random offset every run, so rapid mosaic panel cycling gets its dither "for free"
  instead of paying for a separate guider dither after every slew. The offset radius is
  derived automatically from your guider dither settings (dither pixels × guider pixel scale),
  or set manually. It builds on NINA's Center: the same centering solver and profile settings,
  the same plate-solve status window, and the same guiding stop/restart and dome sync.
- **Check rotation** — takes a plate-solve exposure (using your profile's plate-solve settings)
  and compares the measured position angle against the parent target's position angle. In
  tolerance: an info notification with the measurement. Out of tolerance: the instruction fails
  (red notification; the sequence continues). Moves neither mount nor rotator.
- **Wait for sky brightness** — repeatedly takes throwaway exposures (never saved) and waits
  until the histogram mean reaches a target, within tolerance. Target and tolerance are
  entered as percentages exactly like NINA's flat wizard ("Histogram Mean Target" / "Mean
  Tolerance"), and the instruction shows the equivalent ADU window for the connected camera.
  Direction-aware for dawn (Brightening) or dusk (Dimming) flats; fails when the brightness
  window is overshot.

All three instructions appear under the **Speck Sequence Helpers** category in the advanced sequencer.

## Install

Download `SpeckSequenceHelpers-Setup-<version>.exe` from the [releases page][releases] and run
it. It is a per-user install into `%localappdata%\NINA\Plugins\3.0.0\SpeckSequenceHelpers\` —
no elevation, matching how NINA installs plugins itself. **Close N.I.N.A. before installing**;
plugin files cannot be replaced while it is running. Uninstall through Windows' Apps &
features. This is an interim channel until the plugin ships via the official N.I.N.A. plugin
repository.

To install by hand instead, copy `SpeckSequenceHelpers.dll` into that same folder and restart
NINA. Building the project on a Windows machine does this automatically via a post-build step.

[releases]: https://github.com/speckastro/nina-helpers/releases

## Building

Requires the .NET 8 SDK. Builds on Windows **and** Linux (WPF cross-targeting is enabled):

    dotnet build src/SpeckSequenceHelpers -c Release
    dotnet test tests/SpeckSequenceHelpers.Core.Tests

The plugin references NINA through the `NINA.Plugin` NuGet package — no NINA installation is
needed to build.

`scripts/build.sh` does the whole loop — build, tests, and staging the plugin folder the
installer packages into `artifacts/SpeckSequenceHelpers/`.

## Releasing

CI (GitHub Actions) runs the Linux and Windows test suites, then builds a per-user Inno Setup
installer on the Windows runner. Every build uploads the installer and the plugin folder as
workflow artifacts; pushing a `v*` tag additionally attaches the installer to a GitHub Release,
with the release notes taken from the matching `CHANGELOG.md` section.

To cut a release: bump `AssemblyVersion`/`AssemblyFileVersion` in
`src/SpeckSequenceHelpers/Properties/AssemblyInfo.cs`, add the matching `## [<version>]` section
to `CHANGELOG.md` (the tag job fails if it is missing), then tag `v<version>` and push.

## Publishing to the official plugin repository

1. Cut a tagged release as above.
2. Zip the plugin DLL from that release's build (the `SpeckSequenceHelpers-plugin-folder-*`
   workflow artifact).
3. Host the archive at a stable URL (e.g. a GitHub release on this repo).
4. Follow the manifest instructions at <https://bitbucket.org/Isbeorn/nina.plugin.manifests>
   (PowerShell 7 tooling — works on Linux via `pwsh`). Do **not** rebuild after creating the
   manifest; the checksum must match the released DLL.

## License

MPL-2.0, matching NINA and its plugin ecosystem.
