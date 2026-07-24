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
overrides `Execute` (to check dither preconditions) and `DoCenter` (to centre on a dithered
target).

**Revised during review — `DoCenter` does not delegate to `base.DoCenter`.** The original design
retargeted the inherited `Coordinates` and cleared `Inherited` for the duration of the call.
Both are `[JsonProperty]` bindable state, so that held a random offset and `Inherited = false`
in serialised state for the entire multi-minute centring run: a save at that moment — by the
user or by a plugin calling `ISequenceMediator.SaveContainer` — would persist the dithered
position and permanently detach the item from its parent target, which a `finally` cannot
prevent. `DoCenter` therefore keeps the dithered target in a local and reimplements the slew,
dome synchronisation and centring-solver call against it, verified line-for-line against NINA
3.2's `Center.DoCenter`.

Still inherited unchanged: `Execute`'s plate-solve status window (`PlateSolveStatusVM`) and
linked progress, guiding stop/restart, result checking and window close; `Validate`; the
`Coordinates`/`Inherited` machinery and their persistence; and the `Clone` shape.

Two deliberate improvements over the 3.2 base: dither preconditions run in the `Execute`
override *before* the base stops guiding (3.2's `Center` checks `AtPark` inside `DoCenter`,
after stopping guiding, and its throw path never restarts it), and the return value of
`SlewToCoordinatesAsync` is checked (3.2 ignores it; 3.3 checks it, and we follow 3.3).

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
- The `CenterAfterSlew`-era slew/guiding flow in `Execute` and `SlewAndCenter`. (The "no target
  coordinates found" validation issue is *retained* — see "Coordinate source" below.)

### Retained

`UseManualRadius` and `ManualRadiusArcsec`, and the radius resolution: automatic =
profile `GuiderSettings.DitherPixels` × guider `PixelScale`, else the manual radius; with the
finite-value guards and the accurate "check the guider pixel scale and profile dither amount"
failure message. Offset generation stays `Core.DitherOffsetCalculator` (uniform over a disc,
`Random.Shared`), applied via `Coordinates.Shift` in arcseconds.

### Coordinate source: parent target only

The instruction requires a parent target container and reads its coordinates, exactly as the
original Dithered slew did; `Validate` reports "No target coordinates found - place this
instruction inside a target container" otherwise.

Inheriting `Center` would in principle allow explicitly typed RA/Dec as well, and an earlier
revision of this design exposed them. That was withdrawn: NINA 3.2 ships no reusable coordinate
control, and binding `InputCoordinates`' sexagesimal fields directly produces silently wrong
declinations — `-0°30'` is inexpressible, and editing `+20°30'` to `-20` transiently yields
`-19°30'`, because the setter consults the current `NegativeDec` before recomputing it. The
capability was never required (mosaic panels always sit in a target container), so it was
removed rather than worked around. `Coordinates` and `Inherited` remain inherited and still
serialize; they are simply not editable from the row.

### `DoCenter` override

1. Recompute the dither radius and the un-dithered base coordinates from the parent target —
   both already validated in `Execute`.
2. Generate the offset and apply it with `Coordinates.Shift` into a **local** variable. The
   instruction's own `Coordinates` and `Inherited` are never written.
3. Slew to the local dithered target, failing the instruction if the slew reports failure.
4. Synchronise the dome when one is connected, can set azimuth, and is not already following;
   a sync failure warns and continues.
5. Build the solver and `CenterSolveParameter` from the profile's plate-solve settings — aimed
   at the dithered target — and return `solver.Center(...)`, passing `PlateSolveStatusVM.Progress`
   so the inherited status window displays solve results.

Each `Execute` produces one fresh offset. Instruction-level `Attempts` retries therefore
re-dither, which is the desired behaviour after a failed centring.

### `Execute` override

Checks the parked mount, the dither radius and the availability of base coordinates, then calls
`base.Execute`. These run here rather than in `DoCenter` because the base restarts guiding only
on the non-throwing path — a precondition raised later would leave guiding stopped.

### Validation

`base.Validate()` first, then append the dither-radius issues (manual radius > 0; or guider
connected, reporting a usable pixel scale, with a non-zero profile dither amount). 3.2's
`Center.Validate` contributes a telescope-connected check only — it does **not** check the
camera, so neither do we. Adding one would mean injecting `ICameraMediator` beyond `Center`'s
constructor; Center parity is the accepted posture, and a disconnected camera surfaces as a
plate-solve failure at run time.

### Logging

One `Logger.Info` per run recording the applied offset and the resulting dithered target,
before the slew. Centring progress and solve results are reported through the inherited
`PlateSolveStatusVM` and the progress passed down from `Execute`.

## Testing

- Linux: `Core` suite unchanged (32 tests) — the offset calculator is untouched.
- Rig: verify it centres on a visibly different point each run (log line plus the plate-solve
  status window's reported coordinates), that the status window now appears, that it refuses to
  run outside a target container, that the target's own coordinates never drift, and that
  guiding stops and resumes exactly as with the built-in Center.
