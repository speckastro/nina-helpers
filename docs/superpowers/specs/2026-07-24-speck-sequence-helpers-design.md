# Speck Sequence Helpers — Design

**Date:** 2026-07-24
**Status:** Approved pending user review
**Target:** N.I.N.A. 3.x stable (.NET 8), published plugin (MPL-2.0)

## Context

The author shoots narrowband mosaics, cycling rapidly through panels (currently 2×5 min
Ha per panel per visit) to average out sky glow and seeing. Each panel visit today costs a
slew *plus* a separate guider dither, because slew error is biased and can't be trusted as a
dither. That dither overhead is significant across a night. Two further recurring needs:
verifying camera rotation against the target's position angle without touching anything,
and gating dawn/dusk flat runs on actual sky brightness.

This plugin adds three advanced-sequencer items to address these. It is developed on
Linux (cross-targeting `net8.0-windows`), validated on the author's Windows imaging rig,
and intended for eventual publication in the official NINA plugin repository.

## Plugin identity

- **Display name / sequencer category:** Speck Sequence Helpers
- **Project/assembly:** `SpeckSequenceHelpers` (single DLL)
- **License:** MPL-2.0 (NINA convention; required posture for publishing)
- **Minimum application version:** NINA 3.x stable; references via the `NINA.Plugin`
  NuGet package family (stable 3.x versions)

## Architecture

Single plugin assembly built from the official plugin template, with all decision logic in
pure, NINA-free C# so it is unit-testable on Linux ("Approach A").

```
nina-helpers/
├── src/SpeckSequenceHelpers/
│   ├── SpeckSequenceHelpersPlugin.cs      # PluginBase subclass, MEF metadata
│   ├── Instructions/                      # thin NINA-facing sequence items + WPF DataTemplates
│   │   ├── DitheredSlew.cs / .xaml
│   │   ├── CheckRotation.cs / .xaml
│   │   └── WaitForSkyMedian.cs / .xaml
│   └── Core/                              # pure logic, zero NINA/WPF dependencies
│       ├── DitherOffsetCalculator.cs      # random offset generation + cos(dec) correction
│       ├── AngleMath.cs                   # shortest-path angle diff, mod-180 equivalence
│       └── SkyMedianGate.cs               # wait/complete/fail state machine
├── tests/SpeckSequenceHelpers.Core.Tests/ # plain net8.0 xunit; <Compile Include> links
│   │                                      #   ../src/**/Core/*.cs — runs on Linux
├── manifest/                              # manifest generation for nina.plugin.manifests
├── docs/superpowers/specs/                # this document
└── .sandbox/setup.sh                      # user-level .NET 8 SDK install for the sandbox
```

Key csproj settings for the plugin project: `TargetFramework=net8.0-windows`,
`UseWPF=true`, `EnableWindowsTargeting=true`. No post-build copy step on Linux; a small
deploy script/instructions cover copying the DLL to `%localappdata%\NINA\Plugins` on the
rig.

Instructions reuse NINA's own services rather than reimplementing them: telescope
mediator for slews, NINA's centering/plate-solving pipeline for centering and rotation
measurement, imaging mediator + image statistics for median sampling.

## Instruction 1: Dithered Slew

Replaces "slew + separate dither" when cycling mosaic panels. Lives inside a target/panel
container and reads base coordinates from the parent target (same mechanism as the
built-in *Slew to target*).

- **Offset generation:** uniform random direction; radius uniform over a disc of radius R.
  Offset applied as ΔRA = r·cosθ / cos(dec) (guarded near the pole), ΔDec = r·sinθ.
- **Radius R (auto default):** profile `GuiderSettings.DitherPixels` × connected guider's
  pixel scale (arcsec/px) — i.e. the dither amplitude the user already tuned. NINA's
  "RA-only dither" setting is deliberately ignored (a slew moves both axes regardless).
- **Manual override:** checkbox + "radius (arcsec)" field for setups where the guider can't
  supply a pixel scale.
- **Center toggle:** off (default) → plain slew to the offset coordinates — the fast path.
  On → delegate to NINA's centering logic aimed at the offset coordinates (controlled
  dither at the cost of a solve cycle).
- **Feedback:** applied offset (arcsec + direction) in the instruction status text and log
  only — no toasts.
- **Validation issues:** telescope not connected; no parent target coordinates; auto radius
  selected but guider disconnected or pixel scale unavailable.

## Instruction 2: Check Rotation

A pure measurement — moves neither mount nor rotator.

- Captures one frame using the profile's **plate-solve settings** (exposure time, filter,
  binning, gain — exactly what centering uses), solves with the primary solver, and reads
  the solved position angle.
- **Expected value:** the parent target's configured position angle.
- **Comparison:** shortest-path angular difference; checkbox **"treat 180° flip as
  equivalent"** (default on) compares mod 180, since flipped framing is photographically
  identical.
- **Tolerance:** degrees, default 1.0°.
- **In tolerance:** info toast + status text, e.g. "PA measured 123.4°, target 123.0°,
  Δ 0.4°". Also logged.
- **Out of tolerance:** throws a sequence-entity failure → red error notification,
  instruction marked failed, **sequence continues** (idiomatic NINA fail-and-continue).
- **Validation issues:** camera not connected; no parent target; no plate solver configured.

## Instruction 3: Wait For Sky Median

Gates the sequence on measured sky brightness — designed for dawn/dusk sky flats.

- **Config:** exposure time (s), gain, offset, binning (filter = whatever is currently
  selected),
  interval (s) between attempts, min median (ADU), max median (ADU), direction:
  **Brightening** (dawn) or **Dimming** (dusk).
- **Loop:** every attempt captures an unsaved frame and reads NINA's image-statistics
  median (raw ADU — the same numbers NINA's statistics panel shows), then:

| Reading        | Brightening (dawn)    | Dimming (dusk)        |
|----------------|-----------------------|-----------------------|
| In [min, max]  | complete              | complete              |
| Below min      | wait interval, retry  | **fail** (overshot)   |
| Above max      | **fail** (overshot)   | wait interval, retry  |

- Overshoot on the **first** reading fails immediately (e.g. dawn sky already too bright).
- Failure = sequence-entity failure (red notification, fail-and-continue), same as Check
  Rotation.
- **Status text** updates per attempt: "attempt 7: median 812 ADU (waiting for ≥ 1500)".
- **No built-in timeout** — the overshoot bound catches the natural failure mode, and NINA
  loop conditions can bound it externally if desired.
- **Validation issues:** camera not connected; min ≥ max.

## Cross-cutting requirements

- Proper `Clone` support (sequence templates duplicate instructions).
- Settings serialize with the saved sequence (JSON), and — once published — exported
  class namespaces/type names are frozen (NINA deserializes saved sequences by fully
  qualified type name).
- `IValidatable` with human-readable issues on every instruction.
- Cancellation tokens respected everywhere (sequence stop must interrupt mid-wait,
  mid-exposure, mid-slew promptly).
- English-only strings initially.

## Testing & verification

- **Linux (every iteration):** xunit over `Core/` — offset distribution bounds and
  cos(dec) scaling, RA wrap, near-pole guard, shortest-path/mod-180 angle math, median
  gate state machine including first-reading overshoot and both directions.
- **Build check on Linux:** `dotnet build` of the full plugin (cross-targeted) must pass.
- **Windows rig (per milestone):** copy DLL to `%localappdata%\NINA\Plugins`, confirm
  plugin loads, run a smoke sequence per instruction (simulator camera where possible;
  sky tests for rotation/median).
- **CI (once remote exists):** GitHub Actions `windows-latest` build + full test run.

## Implementation notes (post-review amendments, 2026-07-24)

Three deliberate deviations from the text above, adjudicated during the final
whole-branch review:

1. **Coordinate projection is delegated to NINA, not implemented in `Core/`.**
   `DitherOffsetCalculator` generates a tangent-plane offset vector; the instruction applies
   it via NINA's `Coordinates.Shift` (gnomonic projection), which handles the cos(dec)
   correction, RA wrap, and pole proximity with NINA's own battle-tested WCS math (the same
   code path the framing assistant uses). Reimplementing spherical trigonometry in `Core/`
   solely to unit-test it on Linux would test code the plugin doesn't ship.
2. **"No plate solver configured" is not a validation issue on Check Rotation.** NINA's own
   `Center`/`Solve and rotate` instructions do not validate solver configuration either —
   there is no reliable API for it; an unconfigured solver surfaces as a solve failure at
   execution, which this instruction reports as a failed check.
3. **Dithered Slew's parked-mount error shows a toast** even though the instruction's normal
   feedback is toast-free — this mirrors NINA's built-in `Slew to Ra/Dec` behavior verbatim,
   and built-in parity wins for error surfaces.

## To verify during implementation (API details, not design questions)

- Exact property names/types: `GuiderSettings.DitherPixels`, guider pixel-scale property,
  parent-target coordinate retrieval utility, solved position-angle property on the plate
  solve result, image statistics median access via the imaging mediator, NINA 3 plugin
  install subfolder.
- Whether plain mediator slews stop guiding themselves in NINA 3.x (match built-in *Slew
  to target* semantics exactly, whatever they are).
- Stable-3.x `NINA.Plugin` package version to pin.
