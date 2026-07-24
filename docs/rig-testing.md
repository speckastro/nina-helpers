# Rig verification checklist

Copy `src/SpeckSequenceHelpers/bin/Debug/net8.0-windows/SpeckSequenceHelpers.dll` to
`%localappdata%\NINA\Plugins\3.0.0\SpeckSequenceHelpers\` on the imaging machine and restart
NINA (or build the project on that machine — the post-build step installs it).

If you saved any sequences against the earlier pre-release instructions, NINA will show them as
unknown instructions after these renames — delete and re-add them. "Wait for sky median" is now
"Wait for sky brightness"; "Dithered slew" is now "Dithered slew and center". This is a
one-time, pre-release break.

## Load

- [ ] Plugin appears in Options > Plugins as "Speck Sequence Helpers" v1.0.0.1 with correct
      author/description, no load errors in the log (`%localappdata%\NINA\Logs`).
- [ ] All three instructions appear in the advanced sequencer under "Speck Sequence Helpers",
      with icons, and can be added, saved to a sequence file, reloaded, and duplicated
      (settings survive save/reload — exercises JSON round-trip and Clone).

## Dithered slew and center (sky; mount + camera + plate solver required)

Every run needs a connected mount, camera and configured plate solver, plus either a connected
guider reporting a pixel scale or "manual radius" ticked.

- [ ] Guider disconnected with the automatic radius: validation reports the guider issue;
      ticking "manual radius" clears it.
- [ ] Inside a target container, manual radius 60": the plate-solve status window appears (as it
      does for the built-in Center), centering converges, and the log line reports an offset
      within 60".
- [ ] Run it several times on the same panel: each run logs a different offset, and each solved
      centre matches the panel's coordinates displaced by *that run's* logged offset. Do not
      compare consecutive runs against each other — two independent draws inside the disc can
      legitimately land almost on top of each other or nearly twice the radius apart.
- [ ] Confirm the target container's own coordinates are unchanged after several runs — the
      offsets must not accumulate.
- [ ] With PHD2 connected **and actively guiding** before the item runs: guiding stops before
      the slew and resumes after, exactly as with the built-in Center. (Starting from a
      not-guiding state proves nothing — the base only restarts guiding it actually stopped.)
- [ ] Outside a target container, with coordinates typed into the row and manual radius ticked:
      it centers on those coordinates plus the offset.
- [ ] **While centering is still running**, save the sequence to a file, then open that file: the
      item's coordinates must be the undithered originals with "inherited" still set. Saving
      after the run finishes would not catch a regression here — the point is that the dithered
      position is never written to the item, not even mid-run.
- [ ] With the telescope simulator parked and the guider simulator actively guiding: the run
      fails immediately with a red notification **and guiding is still running afterwards** —
      the parked check runs before guiding is stopped.
- [ ] If you have a dome: connected, controllable, with dome-following disabled — confirm it
      synchronises after the slew. If (and only if) you can force a sync failure — e.g. with the
      dome simulator, or by disconnecting it mid-run — confirm the failure warns and the run
      continues rather than aborting.

## Check rotation (sky, camera + solver required)

- [ ] In a target container with rotation set to the current camera angle and tolerance 1°:
      completes with info toast "Rotation: measured ... Δ ...°"; measurement also shown in the
      instruction row and the log.
- [ ] Set target rotation ~5° off: instruction fails with red notification, sequence continues
      to the next instruction.
- [ ] "Treat 180° flip as equal" on, target rotation = measured + 180: passes.

## Wait for sky brightness (simulator camera is fine)

- [ ] With the camera connected, the row shows an ADU label matching the camera's bit depth
      (target 50% / tolerance 10% on a 16-bit camera reads
      `≈ 32,768 ADU, accepting 29,491 - 36,045 (16-bit)`), and it updates as the
      target/tolerance percentages change. With no camera connected it reads
      `connect camera for ADU values`.
- [ ] Set target and tolerance to distinctive non-default values (e.g. 37% and 4%), save the
      sequence, reload it, and confirm both come back exactly as typed. Leaving them at the
      defaults would mask a missing `[JsonProperty]`, since defaults reappear either way.
- [ ] Brightening, target set so the current sky mean falls inside the tolerance window:
      completes on the first attempt.
- [ ] Brightening, target well above the current sky mean: loops with countdown status text
      between attempts; cancelling the sequence interrupts promptly mid-wait.
- [ ] Brightening, target well below the current sky mean (sky already brighter than the
      window): fails immediately with a red notification.
- [ ] Dimming, target well above the current sky mean (sky already dimmer than the window):
      fails immediately with a red notification.
- [ ] Tolerance set to 0: validation reports "Tolerance must be greater than 0 and at most
      100 percent" and the instruction refuses to run.
- [ ] Confirm no images from this instruction appear in the image save folder.
