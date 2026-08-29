# Changelog

All notable changes to the Speck Sequence Helpers plugin are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
