# Wait For Sky Brightness — Design (supersedes the Wait For Sky Median section)

**Date:** 2026-07-24
**Status:** Approved pending user review
**Supersedes:** the "Instruction 3: Wait For Sky Median" section of
`2026-07-24-speck-sequence-helpers-design.md` (post-rig-feedback revision, before first release)

## Context

Rig feedback: configuring the flats gate as raw min/max ADU doesn't match how the user
already thinks about flat exposure — NINA's flat wizard expresses the goal as a **histogram
mean target** percentage with a **mean tolerance** percentage, and users know their numbers
in those terms. The instruction should adopt flat-wizard semantics exactly, while still
showing the equivalent ADU values so nobody has to do bit-depth arithmetic.

## Changes

### Rename (pre-release, non-breaking window)

- Class `WaitForSkyMedian` → **`WaitForSkyBrightness`** (namespace unchanged:
  `SpeckSequenceHelpers.Instructions` — frozen after first release).
- Display name "Wait for sky median" → **"Wait for sky brightness"**; icon key
  `Speck_WaitForSkyMedian_SVG` → `Speck_WaitForSkyBrightness_SVG`; mini-template key follows
  the class rename.
- Sequences saved with the old type (rig-testing only; nothing published) will show NINA's
  unknown-instruction placeholder and need the item re-added. Accepted.

### Measurement: histogram mean

The gate now evaluates the image **mean** (was: median), matching the flat wizard.

### Settings (JSON-persisted; replaces MinMedian/MaxMedian)

| Setting | Default | Meaning |
|---|---|---|
| `TargetPercent` | 50 | Histogram mean target, percent of full scale (flat wizard's "Histogram Mean Target") |
| `TolerancePercent` | 10 | Mean tolerance, percent **of the target** (flat wizard's "Mean Tolerance") |
| `Direction` | Brightening | Unchanged (Brightening = dawn, Dimming = dusk) |
| exposure settings | unchanged | ExposureTime, Gain, Offset, Binning |

Values are entered as 0–100 percentages (UI parity with the flat wizard) and divided by 100
at the point of use.

### ADU conversion — exact flat-wizard parity via NINA's `HistogramMath`

Use `NINA.Image.ImageAnalysis.HistogramMath` (public API), never reimplemented math:

- Target ADU = `HistogramMeanAndCameraBitDepthToAdu(TargetPercent/100, bitDepth)`
  (= pct × 2^bitDepth).
- Window = `GetLowerToleranceBoundInAdu` / `GetUpperToleranceBoundInAdu`
  (= targetAdu × (1 ∓ TolerancePercent/100)).
- **Runtime gating uses the captured image's own bit depth**
  (`imageData.Properties.BitDepth`), exactly as NINA's SkyFlat instruction does — the gate
  is self-consistent regardless of camera mode.

The computed [lower, upper] ADU window feeds the existing Core gate; the directional
Proceed/Wait/Fail logic (including first-reading overshoot failure and no built-in timeout)
is unchanged.

### ADU label (advisory display)

A read-only, non-persisted row label shows the equivalent window, e.g.
`≈ 32,768 ± 3,277 ADU (16-bit)`:

- Bit depth from the connected camera's reported info; when no camera is connected, show a
  "connect camera for ADU values" placeholder instead of guessing.
- Recomputed live when `TargetPercent`/`TolerancePercent` change and on validation passes
  (camera connect/disconnect reflected on the sequencer's normal validation cadence).
- Advisory only — runtime gating always derives from the actual captured image.

### Core

- `SkyMedianGate` → **`SkyBrightnessGate`** (Core is not serialization-frozen): identical
  constructor shape (minAdu, maxAdu, direction), identical state machine, messages say
  "Mean … ADU". Tests renamed and preserved, including boundary, non-finite, and
  first-reading-overshoot cases.
- Percent-window arithmetic stays OUT of Core — it belongs to NINA's `HistogramMath`
  (same adjudication as the coordinate-projection delegation in the parent spec).

### Validation

Camera connected; `TargetPercent` finite and in (0, 100]; `TolerancePercent` finite and in
**(0, 100]** — strictly positive, because a zero tolerance collapses the window to a single
ADU value, which the gate rejects (`min < max` invariant) and which no real exposure would
ever hit exactly. Exposure/interval checks unchanged. Non-finite hardening conventions from
the parent spec are preserved.

### Documentation ripples

README instruction description, rig-testing checklist section (steps rewritten in
percentage terms), and the parent spec section marked as superseded by this document.

## Testing

- Linux: renamed Core gate suite passes unchanged (32 tests); no new Core surface (the
  percent math is NINA's, exercised on the rig).
- Rig: updated checklist — label shows plausible ADU for the connected camera's bit depth;
  Brightening/Dimming/overshoot scenarios expressed via percentages.
