# Dithered Slew And Center Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Dithered Slew instruction with `DitheredSlewAndCenter`, which inherits NINA's `Center` and displaces the centring target by a random dither offset — centring is unconditional and the `CenterAfterSlew` toggle is gone.

**Architecture:** Subclass `NINA.Sequencer.SequenceItem.Platesolving.Center` and override only `protected virtual DoCenter`. The override retargets `Coordinates` to the dithered position, clears `Inherited` so the base cannot overwrite it, delegates to `base.DoCenter`, and restores the originals in `finally`. Guiding stop/restart, the plate-solve status window, dome sync, solver construction and retries are all inherited.

**Tech Stack:** .NET 8 (`net8.0-windows`, WPF, MEF), NINA.Plugin 3.2.0.9001, xunit, Newtonsoft.Json.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-24-dithered-slew-and-center-design.md`.
- Exported namespace stays `SpeckSequenceHelpers.Instructions` (frozen after first release; this rename is allowed only because nothing is published).
- `src/SpeckSequenceHelpers/Core/**` stays BCL-only and is **not modified by this plan**.
- All three gates must pass before each commit: `dotnet format SpeckSequenceHelpers.sln --verify-no-changes`, `dotnet build SpeckSequenceHelpers.sln -v q` (0 errors; analyzers are warnings-as-errors, NU1701 exempted), `dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q` (**32/32 — unchanged; this plan adds no Core surface**).
- NINA code style: 4-space indent, same-line braces, `RaisePropertyChanged()` for INPC, `Logger` for logs.
- Implementers do NOT commit; the controller commits after an external review gate.

### Verified NINA 3.2.0.9001 API (confirmed by reflection against the pinned assemblies — do not re-derive)

- `Center` is public, **not sealed**, base type `SequenceItem`, in `NINA.Sequencer.SequenceItem.Platesolving`.
- `protected virtual Task<PlateSolveResult> DoCenter(IProgress<ApplicationStatus> progress, CancellationToken token)`.
- Virtual/overridable on `Center`: `Clone`, `Execute`, `Validate`, `AfterParentChanged`, `ToString`, `get_Issues`.
- Public properties on `Center`: `PlateSolvingStatusVM PlateSolveStatusVM` (get only), `bool Inherited` (get/**set**), `InputCoordinates Coordinates` (get/**set**), `IList<string> Issues` (get/set).
- `Center`'s constructor takes, in order: `IProfileService, ITelescopeMediator, IImagingMediator, IFilterWheelMediator, IGuiderMediator, IDomeMediator, IDomeFollower, IPlateSolverFactory, IWindowServiceFactory`.
- `NINA.Astrometry.InputCoordinates` public members: `Coordinates Coordinates`, `int RAHours`, `int RAMinutes`, `double RASeconds`, `bool NegativeDec`, `int DecDegrees`, `int DecMinutes`, `double DecSeconds`.
- **`NINA.Sequencer.Logic.CoordinatesControl` does NOT exist in 3.2** (3.3 only) — the XAML must build coordinate inputs from the `InputCoordinates` members above.
- `Coordinates.Shift(deltaXDegrees, deltaYDegrees, rotation)` applies a projected offset; arcsec→degrees is `/3600d`.

---

### Task 1: Replace DitheredSlew with DitheredSlewAndCenter

**Files:**
- Create: `src/SpeckSequenceHelpers/Instructions/DitheredSlewAndCenter.cs`
- Delete: `src/SpeckSequenceHelpers/Instructions/DitheredSlew.cs`
- Modify: `src/SpeckSequenceHelpers/Instructions/InstructionTemplates.xaml` — replace the three DitheredSlew resources (the `Speck_DitheredSlew_SVG` GeometryGroup, the `local:DitheredSlew` DataTemplate, and the `..._Mini` DataTemplate) at the TOP of the file. Leave the CheckRotation and WaitForSkyBrightness resources untouched.

**Interfaces:**
- Consumes: `SpeckSequenceHelpers.Core.DitherOffsetCalculator.Generate(double maxRadiusArcsec, Random random)` → `OffsetVector { RaArcsec, DecArcsec, RadiusArcsec }` (unchanged).
- Produces: exported instruction `SpeckSequenceHelpers.Instructions.DitheredSlewAndCenter` with JSON-persisted `UseManualRadius`, `ManualRadiusArcsec` (plus `Coordinates` and `Inherited` inherited from `Center`).

- [ ] **Step 1: Write the new instruction**

Create `src/SpeckSequenceHelpers/Instructions/DitheredSlewAndCenter.cs` with exactly this content:

```csharp
using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Platesolving;
using NINA.Sequencer.Utility;
using NINA.WPF.Base.Interfaces.ViewModel;
using SpeckSequenceHelpers.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace SpeckSequenceHelpers.Instructions {

    [ExportMetadata("Name", "Dithered slew and center")]
    [ExportMetadata("Description", "NINA's Center, but the target is displaced by a small random offset each run, so cycling mosaic panels dithers without a separate guider dither. Offset radius comes from the guider dither settings, or a manual radius.")]
    [ExportMetadata("Icon", "Speck_DitheredSlewAndCenter_SVG")]
    [ExportMetadata("Category", "Speck Sequence Helpers")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class DitheredSlewAndCenter : Center {
        private readonly IProfileService profileService;
        private readonly IGuiderMediator guiderMediator;

        [ImportingConstructor]
        public DitheredSlewAndCenter(IProfileService profileService,
                                     ITelescopeMediator telescopeMediator,
                                     IImagingMediator imagingMediator,
                                     IFilterWheelMediator filterWheelMediator,
                                     IGuiderMediator guiderMediator,
                                     IDomeMediator domeMediator,
                                     IDomeFollower domeFollower,
                                     IPlateSolverFactory plateSolverFactory,
                                     IWindowServiceFactory windowServiceFactory)
            : base(profileService, telescopeMediator, imagingMediator, filterWheelMediator,
                   guiderMediator, domeMediator, domeFollower, plateSolverFactory, windowServiceFactory) {
            this.profileService = profileService;
            this.guiderMediator = guiderMediator;
        }

        private DitheredSlewAndCenter(DitheredSlewAndCenter cloneMe)
            : this(cloneMe.profileService, cloneMe.telescopeMediator, cloneMe.imagingMediator,
                   cloneMe.filterWheelMediator, cloneMe.guiderMediator, cloneMe.domeMediator,
                   cloneMe.domeFollower, cloneMe.plateSolverFactory, cloneMe.windowServiceFactory) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new DitheredSlewAndCenter(this) {
                Coordinates = Coordinates.Clone(),
                Inherited = Inherited,
                UseManualRadius = UseManualRadius,
                ManualRadiusArcsec = ManualRadiusArcsec
            };
        }

        private bool useManualRadius;

        [JsonProperty]
        public bool UseManualRadius {
            get => useManualRadius;
            set {
                useManualRadius = value;
                RaisePropertyChanged();
            }
        }

        private double manualRadiusArcsec = 30;

        [JsonProperty]
        public double ManualRadiusArcsec {
            get => manualRadiusArcsec;
            set {
                manualRadiusArcsec = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Displaces the centring target by a fresh random offset, then hands off to NINA's
        /// Center. Inherited is cleared for the duration because the base re-applies the parent
        /// target's coordinates when it is set, which would discard the offset; both it and the
        /// original coordinates are restored afterwards so offsets never compound.
        /// </summary>
        protected override async Task<PlateSolveResult> DoCenter(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var radiusArcsec = ResolveMaxRadiusArcsec();
            if (!double.IsFinite(radiusArcsec) || radiusArcsec <= 0) {
                throw new SequenceEntityFailedException("Dithered slew and center: dither radius unavailable - check the guider pixel scale and profile dither amount, or enable the manual radius");
            }

            var baseCoordinates = ResolveBaseCoordinates();
            if (baseCoordinates == null) {
                throw new SequenceEntityFailedException("Dithered slew and center: no coordinates to dither around");
            }

            var offset = DitherOffsetCalculator.Generate(radiusArcsec, Random.Shared);
            var dithered = baseCoordinates.Shift(offset.RaArcsec / 3600d, offset.DecArcsec / 3600d, 0);
            Logger.Info($"Dithered slew and center: offset {offset.RadiusArcsec:F1}\" of max {radiusArcsec:F1}\" (dRA {offset.RaArcsec:F1}\", dDec {offset.DecArcsec:F1}\"), centering on {dithered}");

            var originalInherited = Inherited;
            var originalCoordinates = Coordinates.Coordinates.Clone();
            try {
                Coordinates.Coordinates = dithered;
                Inherited = false;
                return await base.DoCenter(progress, token);
            } finally {
                Coordinates.Coordinates = originalCoordinates;
                Inherited = originalInherited;
            }
        }

        /// <summary>The un-dithered target: the parent container's when inherited, else the typed coordinates.</summary>
        private Coordinates ResolveBaseCoordinates() {
            if (Inherited) {
                return ItemUtility.RetrieveContextCoordinates(Parent)?.Coordinates;
            }
            return Coordinates?.Coordinates;
        }

        /// <summary>Max dither radius in arcseconds; 0 when unresolvable.</summary>
        public double ResolveMaxRadiusArcsec() {
            if (UseManualRadius) {
                return double.IsFinite(ManualRadiusArcsec) && ManualRadiusArcsec > 0 ? ManualRadiusArcsec : 0;
            }
            var guiderInfo = guiderMediator.GetInfo();
            if (guiderInfo?.Connected != true || !double.IsFinite(guiderInfo.PixelScale) || guiderInfo.PixelScale <= 0) {
                return 0;
            }
            var radius = profileService.ActiveProfile.GuiderSettings.DitherPixels * guiderInfo.PixelScale;
            return double.IsFinite(radius) ? radius : 0;
        }

        public override bool Validate() {
            var valid = base.Validate();
            var i = new List<string>(Issues);
            if (UseManualRadius) {
                if (!double.IsFinite(ManualRadiusArcsec) || ManualRadiusArcsec <= 0) {
                    i.Add("Manual dither radius must be greater than 0");
                }
            } else {
                var guiderInfo = guiderMediator.GetInfo();
                if (guiderInfo?.Connected != true) {
                    i.Add("Guider is not connected - connect a guider or enable the manual dither radius");
                } else if (!double.IsFinite(guiderInfo.PixelScale) || guiderInfo.PixelScale <= 0) {
                    i.Add("Guider does not report a pixel scale - enable the manual dither radius");
                } else {
                    var ditherPixels = profileService.ActiveProfile.GuiderSettings.DitherPixels;
                    if (!double.IsFinite(ditherPixels) || ditherPixels <= 0) {
                        i.Add("Profile guider dither amount is 0 - set dither pixels in the guider settings or enable the manual dither radius");
                    }
                }
            }
            Issues = i;
            return valid && i.Count == 0;
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(DitheredSlewAndCenter)}, UseManualRadius: {UseManualRadius}, ManualRadiusArcsec: {ManualRadiusArcsec}";
        }
    }
}
```

Then delete the old file:

```bash
rm src/SpeckSequenceHelpers/Instructions/DitheredSlew.cs
```

**If the private copy-constructor does not compile** because `Center`'s injected dependencies are private rather than protected in 3.2, store the nine constructor arguments in private readonly fields on this class and use those in the copy-constructor instead. Change nothing else, and record the compiler error in your report.

- [ ] **Step 2: Replace the DitheredSlew XAML resources**

In `src/SpeckSequenceHelpers/Instructions/InstructionTemplates.xaml`, replace the three DitheredSlew resources with exactly this. The `Speck_DitheredSlewAndCenter_SVG` geometry is unchanged from the old icon apart from its key:

```xml
    <GeometryGroup x:Key="Speck_DitheredSlewAndCenter_SVG">
        <EllipseGeometry Center="9,9" RadiusX="8" RadiusY="8" />
        <EllipseGeometry Center="9,9" RadiusX="0.8" RadiusY="0.8" />
        <EllipseGeometry Center="13.5,5.5" RadiusX="1.6" RadiusY="1.6" />
        <PathGeometry Figures="M 9,9 L 12.5,6.5" />
    </GeometryGroup>

    <DataTemplate DataType="{x:Type local:DitheredSlewAndCenter}">
        <nina:SequenceBlockView>
            <nina:SequenceBlockView.SequenceItemContent>
                <StackPanel Orientation="Horizontal">
                    <TextBlock VerticalAlignment="Center" Text="RA" />
                    <TextBox MinWidth="28" Margin="5,0,0,0" TextAlignment="Right" Text="{Binding Coordinates.RAHours}" />
                    <TextBlock VerticalAlignment="Center" Margin="2,0,0,0" Text="h" />
                    <TextBox MinWidth="28" Margin="2,0,0,0" TextAlignment="Right" Text="{Binding Coordinates.RAMinutes}" />
                    <TextBlock VerticalAlignment="Center" Margin="2,0,0,0" Text="m" />
                    <TextBox MinWidth="34" Margin="2,0,0,0" TextAlignment="Right" Text="{Binding Coordinates.RASeconds}" />
                    <TextBlock VerticalAlignment="Center" Margin="2,0,0,0" Text="s" />

                    <TextBlock VerticalAlignment="Center" Margin="10,0,0,0" Text="Dec" />
                    <CheckBox VerticalAlignment="Center" Margin="5,0,0,0" IsChecked="{Binding Coordinates.NegativeDec}" />
                    <TextBlock VerticalAlignment="Center" Margin="2,0,0,0" Text="-" />
                    <TextBox MinWidth="28" Margin="5,0,0,0" TextAlignment="Right" Text="{Binding Coordinates.DecDegrees}" />
                    <TextBlock VerticalAlignment="Center" Margin="2,0,0,0" Text="°" />
                    <TextBox MinWidth="28" Margin="2,0,0,0" TextAlignment="Right" Text="{Binding Coordinates.DecMinutes}" />
                    <TextBlock VerticalAlignment="Center" Margin="2,0,0,0" Text="'" />
                    <TextBox MinWidth="34" Margin="2,0,0,0" TextAlignment="Right" Text="{Binding Coordinates.DecSeconds}" />
                    <TextBlock VerticalAlignment="Center" Margin="2,0,0,0" Text="&quot;" />

                    <CheckBox VerticalAlignment="Center" Margin="15,0,0,0" IsChecked="{Binding UseManualRadius}" />
                    <TextBlock VerticalAlignment="Center" Margin="5,0,0,0" Text="Manual radius (arcsec)" />
                    <TextBox MinWidth="40" Margin="5,0,0,0" TextAlignment="Right"
                             Text="{Binding ManualRadiusArcsec}" IsEnabled="{Binding UseManualRadius}" />
                </StackPanel>
            </nina:SequenceBlockView.SequenceItemContent>
        </nina:SequenceBlockView>
    </DataTemplate>

    <DataTemplate x:Key="SpeckSequenceHelpers.Instructions.DitheredSlewAndCenter_Mini">
        <mini:MiniSequenceItem />
    </DataTemplate>
```

- [ ] **Step 3: Verify no stale references**

Run: `grep -rn "DitheredSlew\b\|CenterAfterSlew\|Speck_DitheredSlew_SVG" src/ || echo NO-STALE-REFS`
Expected: `NO-STALE-REFS` (note `DitheredSlewAndCenter` must NOT match — the `\b` after `DitheredSlew` ensures that).

- [ ] **Step 4: Run the build**

Run: `dotnet build SpeckSequenceHelpers.sln -v q 2>&1 | tail -5`
Expected: `0 Error(s)`. If `Center` or `IWindowServiceFactory` fails to resolve, confirm the usings are `NINA.Sequencer.SequenceItem.Platesolving` and `NINA.WPF.Base.Interfaces.ViewModel` respectively; both assemblies come in transitively via the NINA.Plugin package. Do not restructure the class hierarchy to work around a resolution error — report BLOCKED with the error.

- [ ] **Step 5: Run the tests and the format gate**

Run: `dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 32`.

Run: `dotnet format SpeckSequenceHelpers.sln --verify-no-changes && echo FORMAT-CLEAN`
Expected: `FORMAT-CLEAN`.

- [ ] **Step 6: Report (do not commit)**

---

### Task 2: Update the user-facing documentation

**Files:**
- Modify: `README.md` (the first instruction bullet)
- Modify: `docs/rig-testing.md` (the migration note and the Dithered slew section)

**Interfaces:**
- Consumes: the shipped behaviour from Task 1.
- Produces: nothing code-facing.

- [ ] **Step 1: Update the README bullet**

In `README.md`, replace the first instruction bullet (currently starting `- **Dithered slew** —`) with exactly:

```markdown
- **Dithered slew and center** — NINA's Center with one difference: the target is displaced by
  a small random offset every run, so rapid mosaic panel cycling gets its dither "for free"
  instead of paying for a separate guider dither after every slew. The offset radius is
  derived automatically from your guider dither settings (dither pixels × guider pixel scale),
  or set manually. Everything else — plate-solve centering, the solve status window, guiding
  stop/restart, dome sync — is NINA's own Center behaviour.
```

- [ ] **Step 2: Update the rig-testing migration note**

In `docs/rig-testing.md`, replace the existing migration paragraph (the one beginning "If you saved any sequences against the earlier pre-release") with exactly:

```markdown
If you saved any sequences against the earlier pre-release instructions, NINA will show them as
unknown instructions after these renames — delete and re-add them. "Wait for sky median" is now
"Wait for sky brightness"; "Dithered slew" is now "Dithered slew and center". This is a
one-time, pre-release break.
```

- [ ] **Step 3: Replace the Dithered slew rig section**

In `docs/rig-testing.md`, replace the entire `## Dithered slew (simulator or sky)` section — heading and all its checklist items — with exactly:

```markdown
## Dithered slew and center (sky; camera + solver required)

- [ ] Guider disconnected with the automatic radius: validation reports the guider issue;
      ticking "manual radius" clears it.
- [ ] Inside a target container with mount and camera connected: the plate-solve status window
      appears (as it does for the built-in Center), centering converges, and the log line
      reports an offset within the expected radius.
- [ ] Run it several times on the same panel: each run logs a different offset, and the solved
      centre moves by roughly the dither radius between runs.
- [ ] Confirm the target container's own coordinates are unchanged after several runs — the
      offsets must not accumulate.
- [ ] With PHD2 connected: guiding stops before the slew and resumes after, exactly as with the
      built-in Center.
- [ ] Outside a target container with coordinates typed into the row: it centers on those
      coordinates plus the offset.
```

- [ ] **Step 4: Verify and run the format gate**

Run: `grep -rn "Dithered slew\b" README.md docs/rig-testing.md || echo NO-STALE-DOCS`
Expected: `NO-STALE-DOCS`.

Run: `dotnet format SpeckSequenceHelpers.sln --verify-no-changes && echo FORMAT-CLEAN`
Expected: `FORMAT-CLEAN`.

- [ ] **Step 5: Report (do not commit)**

---

## Plan self-review notes

- Spec coverage: inherit `Center` + override `DoCenter` only → Task 1 Step 1; rename incl. icon/mini keys → Task 1 Steps 1–2; removal of `CenterAfterSlew`, the bespoke slew flow, and the parent-target validation issue → Task 1 Step 1; retained radius options and hardening → `ResolveMaxRadiusArcsec`/`Validate` carried over verbatim; typed-coordinate capability → the XAML coordinate inputs (necessary because 3.2 ships no coordinate control); snapshot/restore so offsets never compound → `DoCenter` `finally`, with a rig step to confirm it; documentation ripples → Task 2.
- Test count stays 32: `Core` is untouched, and everything new is either NINA's own inherited behaviour or WPF/mediator glue that only the rig can exercise — hence the added rig steps for offset variation, non-accumulation, and the status window.
- Type consistency checked: `ResolveMaxRadiusArcsec`, `UseManualRadius`, `ManualRadiusArcsec` keep their existing names and semantics; XAML bindings match those property names and the `InputCoordinates` members listed in Global Constraints.
