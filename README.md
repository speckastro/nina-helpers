# Speck Sequence Helpers

A plugin for [N.I.N.A.](https://nighttime-imaging.eu/) 3.x. It adds the following instructions
and triggers to the advanced sequencer, all under the **Speck Sequence Helpers** category:

- **Dithered slew and center** — a drop-in replacement for the stock slew and center that
  offsets the target by a small random amount each run, so mosaic panel cycling gets its
  dither without a separate guider dither after each slew.
- **Check rotation** — plate solves and compares the measured position angle against the
  target's, failing the instruction if it is out of tolerance.
- **Wait for sky brightness** — takes throwaway exposures until the sky reaches a target
  histogram mean, for dawn and dusk sky flats.
- **Autofocus after pier side change** — a trigger that runs an autofocus before the next
  light exposure once the mount reports a change of pier side, for mounts that flip during a
  slew before NINA's meridian flip trigger gets a chance to.

## Install

Download `SpeckSequenceHelpers-Setup-<version>.exe` from the [releases page][releases] and run
it.

**Close N.I.N.A. first.** Plugin files cannot be replaced while it is running.

The installer is per-user and needs no elevation, the same way NINA installs plugins itself.
It writes to `%localappdata%\NINA\Plugins\3.0.0\SpeckSequenceHelpers\`. Uninstall through
Windows' Apps & features.

This is an interim channel until the plugin ships via the official N.I.N.A. plugin repository.

To install by hand, copy `SpeckSequenceHelpers.dll` into that same folder and restart NINA.

[releases]: https://github.com/speckastro/nina-helpers/releases

## Dithered slew and center

A drop-in replacement for the stock slew and center. It slews to the target and centers on it
with a plate solve, but aims at a point nudged slightly off the target rather than at the
target itself. The offset is redrawn every run, picked evenly from a small disc around the
target. That disc is sized from your profile's guider dither settings, so the shift matches
the dither you would otherwise ask the guider for.

Centering uses your profile's plate-solve settings and tolerance, and shows the usual
plate-solve status window while it works. Guiding stops for the slew and restarts afterwards,
and the dome follows.

This is aimed at mosaics. If your sequence cycles through panels quickly, each return to a
panel already lands on slightly different pixels, so you do not need to pay for a guider
dither after every slew.

| Setting | Default | Notes |
| --- | --- | --- |
| Use manual radius | off | When off, the radius comes from your guider dither settings |
| Manual radius (arcsec) | 30 | Only editable when the checkbox is on |

The radius is your profile's dither amount in pixels multiplied by the guider's reported pixel
scale. It therefore needs a connected guider that reports a pixel scale, and a dither amount
above zero in the profile's guider settings. If any of those is missing, the instruction
reports a validation issue and asks you to turn on the manual radius instead.

Place the instruction inside a target container; it takes its coordinates from the parent
target. It fails if the mount is parked.

## Check rotation

Takes a plate-solve exposure using your profile's plate-solve settings, then compares the
measured position angle against the parent target's position angle.

Within tolerance, you get an info notification with the measurement. Out of tolerance, the
instruction fails and shows a red notification; the sequence itself carries on. Either way
the measurement is shown on the instruction row.

This instruction only measures. It never moves the mount or the rotator.

| Setting | Default | Notes |
| --- | --- | --- |
| Tolerance (°) | 1.0 | Maximum accepted difference |
| Treat 180° flip as equal | on | Folds the comparison to [0°, 90°] |

Leave the flip option on unless you care about the frame's absolute orientation rather than
its framing. With it off, a camera rotated by exactly 180° reads as 180° of error instead of
none.

Needs a connected camera and a parent target container.

## Wait for sky brightness

Repeatedly exposes and measures the histogram mean until the sky is bright enough (or dim
enough) to shoot flats, then lets the sequence continue. The exposures are throwaway and are
never saved.

Target and tolerance are entered as percentages, exactly as in NINA's flat wizard, where they
are called "Histogram Mean Target" and "Mean Tolerance". The instruction shows the equivalent
ADU window for the connected camera next to the fields.

| Setting | Default | Notes |
| --- | --- | --- |
| Direction | Brightening | Brightening for dawn, Dimming for dusk |
| Exposure (s) | 1 | Length of each throwaway exposure |
| Gain | blank | Blank uses your profile's default gain, shown dimmed in the box |
| Offset | blank | Blank uses your profile's default offset, shown dimmed in the box |
| Bin | 1 | Applied to both axes |
| Interval (s) | 30 | Wait between measurements |
| Target (%) | 50 | Histogram mean target, percent of full scale |
| Tolerance (%) | 10 | Accepted deviation, percent of the target |

Cameras that report a fixed list of gains get a dropdown instead of a box, with the default
as the first entry in parentheses. Offset is hidden when the connected camera cannot set
it. The dimmed hint reads "(Camera)" when the profile itself has no default and the
camera's own setting applies.

Direction tells the instruction which way the sky is moving, so a measurement outside the
window can be read as either too early or too late. Set to Brightening, a measurement below
the window waits and one above it fails, since at dawn the sky only gets brighter and the
chance has gone. Dimming reverses both for dusk.

Needs a connected camera.

## Autofocus after pier side change

A trigger. Some mounts flip during an ordinary slew, before NINA's meridian flip
trigger would have fired. NINA's flip workflow, and the autofocus it would have run, then
never happen. On a scope with mirror flop, such as an SCT, the focus shift after that flip
is large enough to matter.

The trigger watches the pier side the mount reports. When it changes, an autofocus runs
before the next light exposure, using your profile's autofocus settings. It only fires
before lights, never before flats, darks, or plate-solve exposures, and it holds off while
the safety monitor reports unsafe or while NINA's meridian flip is too close to fit an
autofocus in. The row shows the last pier side it saw.

It fires on every pier side change, including the one NINA's own meridian flip makes. If
"Autofocus after flip" is turned on in NINA's meridian flip settings, that flip may
autofocus twice; the trigger shows a warning on its row when it sees that setting on. Turn
the NINA option off and let this trigger handle post-flip focus in both cases. NINA runs
triggers in the order they are listed, and child containers' triggers run before their
parent's, so for this to cover NINA's own flip the trigger must be placed after the
Meridian Flip trigger — in the same container below it, or in a container above the one
holding it. Placed before it, the change is only noticed after the first post-flip light,
which is shot with pre-flip focus.

With "Use side of pier" turned on in the meridian flip settings, a flip during a slew leaves
NINA's flip trigger still expecting a flip at the meridian, and this trigger holds off until
that time has passed, so the autofocus can be delayed by a few minutes. With "Use side of
pier" off, NINA will still perform its own flip at the meridian, so holding off is what its
stock autofocus triggers do too.

Needs a connected camera, focuser, and mount. There are no settings.

## Troubleshooting

**The instructions or the trigger do not appear in the sequencer.** Check that NINA was
fully closed during install, then look under Plugins for load errors.

**A validation issue is shown on an instruction or the trigger.** Hover it. The messages name
the missing piece directly, most often a disconnected camera, guider, focuser, or mount, or
an instruction placed outside a target container.

**Check rotation fails with "plate solve failed".** The exposure is taken with your profile's
plate-solve settings, so anything that would break a normal plate solve applies here: wrong
focal length or pixel size in the profile, an unreachable solver, or too short an exposure.

**Autofocus after pier side change never fires.** Check NINA's mount panel for the reported
pier side. If it shows Unknown, the driver does not report it and the trigger has nothing to
watch.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## Developing

Build instructions, project layout, and the release process are in
[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## License

MPL-2.0, matching NINA and its plugin ecosystem.
