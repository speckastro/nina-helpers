# Dithered Slew And Center — Design (supersedes the Dithered Slew section)

**Date:** 2026-07-24
**Status:** Approved pending user review
**Supersedes:** "Instruction 1: Dithered Slew" in `2026-07-24-speck-sequence-helpers-design.md`
(pre-release revision)

## Context

The instruction should be NINA's **Center** with one behavioural difference: the target it
centres on is displaced by a small random offset each run. The current implementation
reimplements a slew-then-optionally-centre flow with a `CenterAfterSlew` toggle, which both
duplicates NINA's logic and leaves the no-centring path as a second behaviour to reason
about. Centring is now unconditional and the instruction is named for what it does.

## Approach: inherit `Center`

`NINA.Sequencer.SequenceItem.Platesolving.Center` is public, non-sealed, and exposes
`protected virtual Task<PlateSolveResult> DoCenter(IProgress<ApplicationStatus>, CancellationToken)`;
`Coordinates` (`InputCoordinates`) and `Inherited` (`bool`) both have public setters — verified
by reflection against the pinned NINA 3.2.0.9001 assemblies. `DitheredSlewAndCenter : Center`
overrides only `DoCenter`, displacing the target before delegating to `base.DoCenter`.

Inherited unchanged, for free: guiding stop/restart, the plate-solve status window
(`PlateSolveStatusVM`), dome synchronisation, solver construction from profile settings,
retry/`ReattemptDelay` handling, parked-mount refusal, and coordinate inheritance from the
parent target.

Accepted cost: coupling to NINA's class hierarchy. `Center`'s base class changed between 3.2
and 3.3 (it derives from `CoordinatesInstruction` in 3.3), so a future NINA upgrade may
require a rebuild. `DoCenter`'s signature is unchanged across both, so the override itself is
expected to survive.

## Changes

### Rename (pre-release)

`DitheredSlew` → **`DitheredSlewAndCenter`**; display name **"Dithered slew and center"**;
icon key `Speck_DitheredSlew_SVG` → `Speck_DitheredSlewAndCenter_SVG`; mini-template key
follows. Sequences saved with the old type show NINA's unknown-instruction placeholder and
need the item re-added — same accepted pre-release break as the brightness rename.

### Removed

- `CenterAfterSlew` — centring is unconditional.
- The bespoke slew/guiding/centring flow in `Execute` and `SlewAndCenter`: `Center` owns it.
- The "no target coordinates found" validation issue: inheriting `Center` means explicit
  RA/Dec is now a supported mode (see below), so a parent target is no longer required.

### Retained

`UseManualRadius` and `ManualRadiusArcsec`, and the radius resolution: automatic =
profile `GuiderSettings.DitherPixels` × guider `PixelScale`, else the manual radius; with the
finite-value guards and the accurate "check the guider pixel scale and profile dither amount"
failure message. Offset generation stays `Core.DitherOffsetCalculator` (uniform over a disc,
`Random.Shared`), applied via `Coordinates.Shift` in arcseconds.

### New capability (consequence of inheriting)

The instruction now works **outside** a target container using explicitly typed RA/Dec, exactly
as `Center` does, in addition to inheriting the parent target's coordinates. The row shows the
coordinate inputs alongside the dither controls.

### `DoCenter` override

1. Snapshot `Inherited` and a clone of `Coordinates.Coordinates`.
2. Resolve the un-dithered base coordinates: the parent target's when `Inherited`, else the
   typed `Coordinates`.
3. Resolve the dither radius; fail with `SequenceEntityFailedException` if unresolvable.
4. Set `Coordinates.Coordinates` to the offset position and `Inherited = false` — the latter
   is required, because `base.DoCenter` re-applies the parent's coordinates when `Inherited`
   is true and would otherwise discard the offset.
5. `await base.DoCenter(...)` inside `try`, restoring the snapshot in `finally` so offsets
   never compound across runs and the UI does not show drifted coordinates.

Each `Execute` produces one fresh offset. Instruction-level `Attempts` retries therefore
re-dither, which is the desired behaviour after a failed centring.

### Validation

`base.Validate()` first, then append the dither-radius issues (manual radius > 0; or guider
connected, reporting a usable pixel scale, with a non-zero profile dither amount). The camera
and telescope checks come from `Center`.

### Logging

One `Logger.Info` per run recording the applied offset and the resulting target, before
delegating. Centring progress and results are `Center`'s to report.

## Testing

- Linux: `Core` suite unchanged (32 tests) — the offset calculator is untouched.
- Rig: verify it centres on a visibly different point each run (log line plus the plate-solve
  status window's reported coordinates), that the status window now appears, that it works
  both inside a target container and with typed coordinates, and that guiding stops and
  resumes exactly as with the built-in Center.
