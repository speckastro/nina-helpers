# Speck Sequence Helpers

A [N.I.N.A.](https://nighttime-imaging.eu/) 3.x plugin with three advanced-sequencer instructions:

- **Dithered slew** — slews to the parent target's coordinates plus a small random offset, so
  rapid mosaic panel cycling gets its dither "for free" instead of paying for a separate guider
  dither after every slew. The offset radius is derived automatically from your guider dither
  settings (dither pixels × guider pixel scale), or set manually. Optional plate-solve centering
  on the offset coordinates.
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

Copy `SpeckSequenceHelpers.dll` into `%localappdata%\NINA\Plugins\3.0.0\SpeckSequenceHelpers\`
and restart NINA. Building the project on a Windows machine does this automatically via a
post-build step.

## Building

Requires the .NET 8 SDK. Builds on Windows **and** Linux (WPF cross-targeting is enabled):

    dotnet build src/SpeckSequenceHelpers -c Release
    dotnet test tests/SpeckSequenceHelpers.Core.Tests

The plugin references NINA through the `NINA.Plugin` NuGet package — no NINA installation is
needed to build.

## Publishing to the official plugin repository

1. Bump `AssemblyVersion`/`AssemblyFileVersion` in `src/SpeckSequenceHelpers/Properties/AssemblyInfo.cs`
   and update `CHANGELOG.md`.
2. Build in Release on Windows and zip the plugin DLL.
3. Host the archive at a stable URL (e.g. a GitHub release on this repo).
4. Follow the manifest instructions at <https://bitbucket.org/Isbeorn/nina.plugin.manifests>
   (PowerShell 7 tooling — works on Linux via `pwsh`). Do **not** rebuild after creating the
   manifest; the checksum must match the released DLL.

## License

MPL-2.0, matching NINA and its plugin ecosystem.
