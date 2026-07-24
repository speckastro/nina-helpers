# Rig verification checklist

Copy `src/SpeckSequenceHelpers/bin/Debug/net8.0-windows/SpeckSequenceHelpers.dll` to
`%localappdata%\NINA\Plugins\3.0.0\SpeckSequenceHelpers\` on the imaging machine and restart
NINA (or build the project on that machine — the post-build step installs it).

If you saved any sequences against the earlier pre-release "Wait for sky median" instruction,
NINA will show them as an unknown instruction after this rename — delete it and add "Wait for
sky brightness" in its place. This is a one-time, pre-release break.

## Load

- [ ] Plugin appears in Options > Plugins as "Speck Sequence Helpers" v1.0.0.1 with correct
      author/description, no load errors in the log (`%localappdata%\NINA\Logs`).
- [ ] All three instructions appear in the advanced sequencer under "Speck Sequence Helpers",
      with icons, and can be added, saved to a sequence file, reloaded, and duplicated
      (settings survive save/reload — exercises JSON round-trip and Clone).

## Dithered slew (simulator or sky)

- [ ] Outside a target container: validation issue "No target coordinates found...".
- [ ] Guider disconnected + auto radius: validation issue mentioning manual radius; enabling
      manual radius clears it.
- [ ] In a target container with mount connected: executes, log line shows offset within the
      expected radius; repeated runs show varying offsets; mount lands near target.
- [ ] With PHD2 connected: guiding stops before the slew and resumes after.
- [ ] "Center after slew" on: plate-solve centering runs and converges on offset coordinates.

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
