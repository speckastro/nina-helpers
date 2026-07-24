# Rig verification checklist

Copy `src/SpeckSequenceHelpers/bin/Debug/net8.0-windows/SpeckSequenceHelpers.dll` to
`%localappdata%\NINA\Plugins\3.0.0\SpeckSequenceHelpers\` on the imaging machine and restart
NINA (or build the project on that machine — the post-build step installs it).

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

## Wait for sky median (simulator camera is fine)

- [ ] Brightening, min below current median, max above: completes on first attempt.
- [ ] Brightening, min above current median: loops with countdown status text between attempts;
      cancelling the sequence interrupts promptly mid-wait.
- [ ] Brightening, max below current median: fails immediately with red notification.
- [ ] Dimming, min above... max below current median (window overshot): fails immediately.
- [ ] Confirm no images from this instruction appear in the image save folder.
