# Development

Developer notes for Speck Sequence Helpers. For installation and usage, see the
[README](../README.md).

## Building

Requires the .NET 8 SDK. Builds on Windows and on Linux, since WPF cross-targeting is
enabled:

    dotnet build src/SpeckSequenceHelpers -c Release
    dotnet test tests/SpeckSequenceHelpers.Core.Tests

The plugin references NINA through the `NINA.Plugin` NuGet package, so no NINA installation
is needed to build.

`scripts/build.sh` runs the build, the tests, and stages the plugin folder that the installer
packages, into `artifacts/SpeckSequenceHelpers/`.

On Windows the `DeployToNina` target copies the built DLL into
`%localappdata%\NINA\Plugins\3.0.0\SpeckSequenceHelpers\` after every build, so a local build
installs itself. It is skipped when `CI` is set.

## Project layout

    src/SpeckSequenceHelpers/          plugin assembly (WPF, NINA references)
      Core/                            logic with no NINA dependencies, unit tested
      Instructions/                    the sequencer instructions and their XAML templates
    tests/SpeckSequenceHelpers.Core.Tests/
    installer/installer.iss            Inno Setup script
    scripts/build.sh

`Core/` holds the parts worth testing on their own: `DitherOffsetCalculator`, `AngleMath`,
and `SkyBrightnessGate`. They use no NINA types, so the test suite runs on Linux.

## Releasing

GitHub Actions runs the Linux and Windows test suites, then builds the per-user Inno Setup
installer on the Windows runner. Every build uploads the installer and the plugin folder as
workflow artifacts. Pushing a `v*` tag also attaches the installer to a GitHub Release, with
the release notes taken from the matching `CHANGELOG.md` section.

To cut a release:

1. Bump `AssemblyVersion` and `AssemblyFileVersion` in
   `src/SpeckSequenceHelpers/Properties/AssemblyInfo.cs`.
2. Add the matching `## [<version>]` section to `CHANGELOG.md`. The tag job fails if it is
   missing.
3. Tag `v<version>` and push.

## Publishing to the official plugin repository

1. Cut a tagged release as above.
2. Zip the plugin DLL from that release's build, taken from the
   `SpeckSequenceHelpers-plugin-folder-*` workflow artifact.
3. Host the archive at a stable URL, such as a GitHub release on this repo.
4. Follow the manifest instructions at
   <https://bitbucket.org/Isbeorn/nina.plugin.manifests>. The tooling is PowerShell 7 and
   works on Linux via `pwsh`.

Do not rebuild after creating the manifest. The checksum has to match the released DLL.
