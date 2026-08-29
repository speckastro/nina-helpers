# Changelog

All notable changes to the Speck Sequence Helpers plugin are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Autofocus after pier side change** — trigger that runs an autofocus before the next
  light exposure after the mount reports a change of pier side, for mounts that flip during a
  slew before the meridian flip trigger runs.

### Changed

- **Wait for sky brightness** — Gain and Offset are left blank to use the profile default,
  which is shown dimmed in the box the way NINA's Take Exposure does, instead of entering
  `-1`. Cameras with a fixed gain list get a dropdown. Saved sequences load unchanged.

## [1.0.0.1] - 2026-08-29

Initial release.

### Added

- **Dithered slew and center** — N.I.N.A.'s Center with the target displaced by a small
  random offset each run, so rapid mosaic panel cycling needs no separate dither.
- **Check rotation** — plate solve and compare the measured position angle against the
  target's position angle, failing the instruction if a tolerance is exceeded.
- **Wait for sky brightness** — take throwaway exposures until the histogram mean reaches
  a target percentage, for dawn/dusk sky flats.
- Inno Setup installer and CI (Linux + Windows test matrix).
