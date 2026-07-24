# Wait For Sky Brightness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Wait For Sky Median instruction's raw min/max ADU configuration with flat-wizard-parity histogram-mean-target and mean-tolerance percentages, showing the equivalent ADU window in the UI.

**Architecture:** The Core gate keeps its ADU-window state machine (renamed `SkyBrightnessGate`); the instruction converts percentages to an ADU window using NINA's own `HistogramMath` — the exact code the flat wizard uses — with the captured image's bit depth at runtime and the connected camera's bit depth for the advisory label. No percent arithmetic is reimplemented in `Core/`.

**Tech Stack:** .NET 8 (`net8.0-windows`, WPF, MEF), NINA.Plugin 3.2.0.9001, xunit, Newtonsoft.Json.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-24-wait-for-sky-brightness-design.md` (supersedes the "Instruction 3" section of the parent spec).
- Exported instruction namespace stays `SpeckSequenceHelpers.Instructions` (frozen after first release; the class rename is allowed only because nothing is published yet).
- `src/SpeckSequenceHelpers/Core/**` must remain BCL-only: no NINA, no WPF, no Newtonsoft.
- Percent→ADU math must come from `NINA.Image.ImageAnalysis.HistogramMath`; never reimplement it.
- All three gates must pass before each commit: `dotnet format SpeckSequenceHelpers.sln --verify-no-changes`, `dotnet build SpeckSequenceHelpers.sln -v q` (0 errors; analyzers are warnings-as-errors, NU1701 exempted), `dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q` (32/32 through this plan — no test count change).
- NINA code style: 4-space indent, same-line braces, `RaisePropertyChanged()` for INPC, `Logger` for logs, `Notification` for toasts.
- Implementers do NOT commit; the controller commits after an external review gate.

### Verified NINA API (do not re-derive)

- `NINA.Image.ImageAnalysis.HistogramMath` (public static):
  - `HistogramMeanAndCameraBitDepthToAdu(double histogramMeanPercentage, double cameraBitDepth)` → `pct * 2^bitDepth`
  - `GetLowerToleranceBoundInAdu(double histogramMeanPercentage, double cameraBitDepth, double tolerance)` → `targetAdu * (1 - tolerance)`
  - `GetUpperToleranceBoundInAdu(double histogramMeanPercentage, double cameraBitDepth, double tolerance)` → `targetAdu * (1 + tolerance)`
  - All percentages are **fractions** (0.5 = 50%), so divide the stored 0–100 values by 100 at the call site.
- `IImageData.Properties` → `ImageProperties.BitDepth` (`int`).
- `CameraInfo.BitDepth` (`int`), via `cameraMediator.GetInfo()`.
- `IImageStatistics.Mean` (`double`) — alongside the existing `Median`.

---

### Task 1: Rename the Core gate to SkyBrightnessGate

**Files:**
- Create: `src/SpeckSequenceHelpers/Core/SkyBrightnessGate.cs`
- Delete: `src/SpeckSequenceHelpers/Core/SkyMedianGate.cs`
- Create: `tests/SpeckSequenceHelpers.Core.Tests/SkyBrightnessGateTests.cs`
- Delete: `tests/SpeckSequenceHelpers.Core.Tests/SkyMedianGateTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces (used by Task 2): `SpeckSequenceHelpers.Core.SkyBrightnessGate(double minAdu, double maxAdu, GateDirection direction)` with `GateVerdict Evaluate(double meanAdu)`; unchanged `GateDirection { Brightening, Dimming }`, `GateAction { Proceed, Wait, Fail }`, `GateVerdict { GateAction Action; string Reason }`. Constructor throws `ArgumentException` for non-finite bounds or `minAdu >= maxAdu`.

Note: `src/SpeckSequenceHelpers/Instructions/WaitForSkyMedian.cs` still references `SkyMedianGate` after this task, so the solution build will fail until Task 2 lands. That is expected and acceptable: the test project compiles `Core/` only via linked sources, so the Core test suite (the gate's real verification) still runs green. Run the plugin build in Task 2, not here.

- [ ] **Step 1: Write the renamed test file**

Create `tests/SpeckSequenceHelpers.Core.Tests/SkyBrightnessGateTests.cs` with exactly this content:

```csharp
using SpeckSequenceHelpers.Core;
using System;
using Xunit;

namespace SpeckSequenceHelpers.Core.Tests {

    public class SkyBrightnessGateTests {

        [Theory]
        [InlineData(GateDirection.Brightening, 1500, GateAction.Proceed)]  // in range
        [InlineData(GateDirection.Brightening, 1000, GateAction.Proceed)]  // == min boundary
        [InlineData(GateDirection.Brightening, 5000, GateAction.Proceed)]  // == max boundary
        [InlineData(GateDirection.Brightening, 999, GateAction.Wait)]      // below min: dawn, keep waiting
        [InlineData(GateDirection.Brightening, 5001, GateAction.Fail)]     // above max: dawn overshot
        [InlineData(GateDirection.Dimming, 1500, GateAction.Proceed)]      // in range
        [InlineData(GateDirection.Dimming, 1000, GateAction.Proceed)]   // == min boundary
        [InlineData(GateDirection.Dimming, 5000, GateAction.Proceed)]   // == max boundary
        [InlineData(GateDirection.Dimming, 5001, GateAction.Wait)]         // above max: dusk, keep waiting
        [InlineData(GateDirection.Dimming, 999, GateAction.Fail)]          // below min: dusk overshot
        public void Evaluate_AppliesDirectionalWindow(GateDirection direction, double mean, GateAction expected) {
            var gate = new SkyBrightnessGate(1000, 5000, direction);
            Assert.Equal(expected, gate.Evaluate(mean).Action);
        }

        [Fact]
        public void Evaluate_FirstReadingCanFail() {
            // dawn gate, sky already too bright on the very first exposure
            var gate = new SkyBrightnessGate(1000, 5000, GateDirection.Brightening);
            Assert.Equal(GateAction.Fail, gate.Evaluate(60000).Action);
        }

        [Fact]
        public void Evaluate_ReasonMentionsMean() {
            var gate = new SkyBrightnessGate(1000, 5000, GateDirection.Brightening);
            Assert.Contains("812", gate.Evaluate(812).Reason);
        }

        [Fact]
        public void Constructor_MinNotBelowMax_Throws() {
            Assert.Throws<ArgumentException>(() => new SkyBrightnessGate(5000, 5000, GateDirection.Brightening));
            Assert.Throws<ArgumentException>(() => new SkyBrightnessGate(6000, 5000, GateDirection.Brightening));
        }

        [Fact]
        public void Constructor_NonFiniteThresholds_Throw() {
            Assert.Throws<ArgumentException>(() => new SkyBrightnessGate(double.NaN, 5000, GateDirection.Brightening));
            Assert.Throws<ArgumentException>(() => new SkyBrightnessGate(1000, double.NaN, GateDirection.Brightening));
            Assert.Throws<ArgumentException>(() => new SkyBrightnessGate(1000, double.PositiveInfinity, GateDirection.Dimming));
        }
    }
}
```

Then delete the old file:

```bash
rm tests/SpeckSequenceHelpers.Core.Tests/SkyMedianGateTests.cs
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -5`
Expected: build FAILS with `CS0246` — `SkyBrightnessGate` does not exist.

- [ ] **Step 3: Write the renamed gate**

Create `src/SpeckSequenceHelpers/Core/SkyBrightnessGate.cs` with exactly this content:

```csharp
using System;

namespace SpeckSequenceHelpers.Core {

    public enum GateDirection {
        Brightening,
        Dimming
    }

    public enum GateAction {
        Proceed,
        Wait,
        Fail
    }

    public readonly struct GateVerdict {

        public GateVerdict(GateAction action, string reason) {
            Action = action;
            Reason = reason;
        }

        public GateAction Action { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// Decides whether a measured sky brightness (histogram mean, in ADU) means the sequence
    /// can proceed, should keep waiting, or has overshot the usable brightness window
    /// (dawn = Brightening, dusk = Dimming). The window bounds are supplied in ADU; the
    /// caller owns any percentage-to-ADU conversion.
    /// </summary>
    public class SkyBrightnessGate {
        private readonly double minAdu;
        private readonly double maxAdu;
        private readonly GateDirection direction;

        public SkyBrightnessGate(double minAdu, double maxAdu, GateDirection direction) {
            if (!double.IsFinite(minAdu) || !double.IsFinite(maxAdu)) {
                throw new ArgumentException($"Brightness bounds must be finite (min: {minAdu}, max: {maxAdu})");
            }
            if (minAdu >= maxAdu) {
                throw new ArgumentException($"Min brightness ({minAdu}) must be less than max brightness ({maxAdu})");
            }
            this.minAdu = minAdu;
            this.maxAdu = maxAdu;
            this.direction = direction;
        }

        public GateVerdict Evaluate(double meanAdu) {
            if (meanAdu >= minAdu && meanAdu <= maxAdu) {
                return new GateVerdict(GateAction.Proceed, $"Mean {meanAdu:F0} ADU within [{minAdu:F0}, {maxAdu:F0}]");
            }
            if (direction == GateDirection.Brightening) {
                return meanAdu < minAdu
                    ? new GateVerdict(GateAction.Wait, $"Mean {meanAdu:F0} ADU below {minAdu:F0}, waiting for sky to brighten")
                    : new GateVerdict(GateAction.Fail, $"Mean {meanAdu:F0} ADU exceeds {maxAdu:F0}, brightness window overshot");
            }
            return meanAdu > maxAdu
                ? new GateVerdict(GateAction.Wait, $"Mean {meanAdu:F0} ADU above {maxAdu:F0}, waiting for sky to dim")
                : new GateVerdict(GateAction.Fail, $"Mean {meanAdu:F0} ADU below {minAdu:F0}, brightness window overshot");
        }
    }
}
```

Then delete the old file:

```bash
rm src/SpeckSequenceHelpers/Core/SkyMedianGate.cs
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 32`.

- [ ] **Step 5: Report (do not commit)**

Report the test output. The plugin project will not build until Task 2 replaces the instruction — do not attempt to fix that here.

---

### Task 2: Replace WaitForSkyMedian with WaitForSkyBrightness

**Files:**
- Create: `src/SpeckSequenceHelpers/Instructions/WaitForSkyBrightness.cs`
- Delete: `src/SpeckSequenceHelpers/Instructions/WaitForSkyMedian.cs`
- Modify: `src/SpeckSequenceHelpers/Instructions/InstructionTemplates.xaml` (the WaitForSkyMedian icon + two DataTemplates at the end of the file; leave the DitheredSlew and CheckRotation resources untouched)

**Interfaces:**
- Consumes: `SkyBrightnessGate(double minAdu, double maxAdu, GateDirection direction)` / `Evaluate(double meanAdu)` from Task 1.
- Produces: exported instruction `SpeckSequenceHelpers.Instructions.WaitForSkyBrightness` with JSON-persisted `ExposureTime`, `Gain`, `Offset`, `Binning`, `IntervalSeconds`, `TargetPercent`, `TolerancePercent`, `Direction`; non-persisted `AduSummary`; static `DirectionChoices`.

- [ ] **Step 1: Write the new instruction**

Create `src/SpeckSequenceHelpers/Instructions/WaitForSkyBrightness.cs` with exactly this content:

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.ImageAnalysis;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using SpeckSequenceHelpers.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace SpeckSequenceHelpers.Instructions {

    [ExportMetadata("Name", "Wait for sky brightness")]
    [ExportMetadata("Description", "Repeatedly takes throwaway exposures and waits until the histogram mean reaches the target, within tolerance - the same target/tolerance percentages the flat wizard uses. Brightening = dawn flats, Dimming = dusk flats. Fails when the window is overshot. Exposures are never saved.")]
    [ExportMetadata("Icon", "Speck_WaitForSkyBrightness_SVG")]
    [ExportMetadata("Category", "Speck Sequence Helpers")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class WaitForSkyBrightness : SequenceItem, IValidatable {
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;

        [ImportingConstructor]
        public WaitForSkyBrightness(ICameraMediator cameraMediator, IImagingMediator imagingMediator) {
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            UpdateAduSummary();
        }

        private WaitForSkyBrightness(WaitForSkyBrightness cloneMe) : this(cloneMe.cameraMediator, cloneMe.imagingMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            var clone = new WaitForSkyBrightness(this) {
                ExposureTime = ExposureTime,
                Gain = Gain,
                Offset = Offset,
                Binning = Binning,
                IntervalSeconds = IntervalSeconds,
                TargetPercent = TargetPercent,
                TolerancePercent = TolerancePercent,
                Direction = Direction
            };
            clone.UpdateAduSummary();
            return clone;
        }

        public static GateDirection[] DirectionChoices { get; } = { GateDirection.Brightening, GateDirection.Dimming };

        private double exposureTime = 1;

        [JsonProperty]
        public double ExposureTime {
            get => exposureTime;
            set {
                exposureTime = value;
                RaisePropertyChanged();
            }
        }

        private int gain = -1;

        [JsonProperty]
        public int Gain {
            get => gain;
            set {
                gain = value;
                RaisePropertyChanged();
            }
        }

        private int offset = -1;

        [JsonProperty]
        public int Offset {
            get => offset;
            set {
                offset = value;
                RaisePropertyChanged();
            }
        }

        private short binning = 1;

        [JsonProperty]
        public short Binning {
            get => binning;
            set {
                binning = value;
                RaisePropertyChanged();
            }
        }

        private double intervalSeconds = 30;

        [JsonProperty]
        public double IntervalSeconds {
            get => intervalSeconds;
            set {
                intervalSeconds = value;
                RaisePropertyChanged();
            }
        }

        private double targetPercent = 50;

        /// <summary>Histogram mean target, in percent of full scale - the flat wizard's "Histogram Mean Target".</summary>
        [JsonProperty]
        public double TargetPercent {
            get => targetPercent;
            set {
                targetPercent = value;
                RaisePropertyChanged();
                UpdateAduSummary();
            }
        }

        private double tolerancePercent = 10;

        /// <summary>Accepted deviation from the target, in percent of the target - the flat wizard's "Mean Tolerance".</summary>
        [JsonProperty]
        public double TolerancePercent {
            get => tolerancePercent;
            set {
                tolerancePercent = value;
                RaisePropertyChanged();
                UpdateAduSummary();
            }
        }

        private GateDirection direction = GateDirection.Brightening;

        [JsonProperty]
        [JsonConverter(typeof(StringEnumConverter))]
        public GateDirection Direction {
            get => direction;
            set {
                direction = value;
                RaisePropertyChanged();
            }
        }

        private string aduSummary = string.Empty;

        /// <summary>
        /// Advisory display of the configured window in ADU for the connected camera's bit depth.
        /// Not persisted: gating always derives its window from the captured image's own bit depth.
        /// </summary>
        public string AduSummary {
            get => aduSummary;
            private set {
                aduSummary = value;
                RaisePropertyChanged();
            }
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var attempt = 0;
            while (true) {
                token.ThrowIfCancellationRequested();
                attempt++;

                var seq = new CaptureSequence(
                    ExposureTime,
                    CaptureSequence.ImageTypes.SNAPSHOT,
                    null,
                    new BinningMode(Binning, Binning),
                    1) {
                    Gain = Gain,
                    Offset = Offset
                };
                var exposure = await imagingMediator.CaptureImage(seq, token, progress);
                var imageData = await exposure.ToImageData(progress, token);
                var stats = await imageData.Statistics;
                var bitDepth = imageData.Properties.BitDepth;

                var gate = CreateGate(bitDepth);
                var verdict = gate.Evaluate(stats.Mean);
                Logger.Info($"Wait for sky brightness: attempt {attempt}, mean {stats.Mean:F0} ADU at {bitDepth}-bit -> {verdict.Action} ({verdict.Reason})");

                switch (verdict.Action) {
                    case GateAction.Proceed:
                        progress?.Report(new ApplicationStatus() { Status = string.Empty });
                        return;

                    case GateAction.Fail:
                        Notification.ShowError($"Wait for sky brightness: {verdict.Reason}");
                        throw new SequenceEntityFailedException($"Wait for sky brightness: {verdict.Reason}");

                    default:
                        await CoreUtil.Wait(TimeSpan.FromSeconds(IntervalSeconds), true, token, progress, $"Attempt {attempt}: {verdict.Reason}");
                        break;
                }
            }
        }

        /// <summary>
        /// Builds the ADU window for a given bit depth using NINA's own flat-wizard math, so the
        /// accepted range matches what the flat wizard would accept for the same percentages.
        /// </summary>
        private SkyBrightnessGate CreateGate(int bitDepth) {
            var targetFraction = TargetPercent / 100d;
            var toleranceFraction = TolerancePercent / 100d;
            var lowerAdu = HistogramMath.GetLowerToleranceBoundInAdu(targetFraction, bitDepth, toleranceFraction);
            var upperAdu = HistogramMath.GetUpperToleranceBoundInAdu(targetFraction, bitDepth, toleranceFraction);
            try {
                return new SkyBrightnessGate(lowerAdu, upperAdu, Direction);
            } catch (ArgumentException ex) {
                throw new SequenceEntityFailedException($"Wait for sky brightness: {ex.Message}");
            }
        }

        private void UpdateAduSummary() {
            var info = cameraMediator?.GetInfo();
            if (info?.Connected != true || info.BitDepth <= 0) {
                AduSummary = "connect camera for ADU values";
                return;
            }
            if (!double.IsFinite(TargetPercent) || !double.IsFinite(TolerancePercent)) {
                AduSummary = string.Empty;
                return;
            }
            var targetAdu = HistogramMath.HistogramMeanAndCameraBitDepthToAdu(TargetPercent / 100d, info.BitDepth);
            var deltaAdu = targetAdu * (TolerancePercent / 100d);
            AduSummary = $"≈ {targetAdu:N0} ± {deltaAdu:N0} ADU ({info.BitDepth}-bit)";
        }

        public override TimeSpan GetEstimatedDuration() {
            return TimeSpan.FromSeconds(ExposureTime + IntervalSeconds);
        }

        public bool Validate() {
            var i = new List<string>();
            if (!cameraMediator.GetInfo().Connected) {
                i.Add("Camera is not connected");
            }
            if (!double.IsFinite(TargetPercent) || TargetPercent <= 0 || TargetPercent > 100) {
                i.Add("Target must be greater than 0 and at most 100 percent");
            }
            if (!double.IsFinite(TolerancePercent) || TolerancePercent <= 0 || TolerancePercent > 100) {
                i.Add("Tolerance must be greater than 0 and at most 100 percent");
            }
            if (!double.IsFinite(ExposureTime) || ExposureTime <= 0) {
                i.Add("Exposure time must be greater than 0");
            }
            if (!double.IsFinite(IntervalSeconds) || IntervalSeconds < 0) {
                i.Add("Interval must be 0 or greater");
            }
            UpdateAduSummary();
            Issues = i;
            return i.Count == 0;
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(WaitForSkyBrightness)}, ExposureTime: {ExposureTime}, Interval: {IntervalSeconds}, Target: {TargetPercent}%, Tolerance: {TolerancePercent}%, Direction: {Direction}";
        }
    }
}
```

Then delete the old file:

```bash
rm src/SpeckSequenceHelpers/Instructions/WaitForSkyMedian.cs
```

- [ ] **Step 2: Update the XAML resources**

In `src/SpeckSequenceHelpers/Instructions/InstructionTemplates.xaml`, replace the final three resources (the `Speck_WaitForSkyMedian_SVG` GeometryGroup, the `local:WaitForSkyMedian` DataTemplate, and the `..._Mini` DataTemplate) with exactly this — the DitheredSlew and CheckRotation resources above them stay untouched:

```xml
    <GeometryGroup x:Key="Speck_WaitForSkyBrightness_SVG">
        <PathGeometry Figures="M 2,16 L 2,10 L 5,10 L 5,16 Z M 7,16 L 7,3 L 10,3 L 10,16 Z M 12,16 L 12,7 L 15,7 L 15,16 Z" />
        <PathGeometry Figures="M 0,18 L 17,18" />
    </GeometryGroup>

    <DataTemplate DataType="{x:Type local:WaitForSkyBrightness}">
        <nina:SequenceBlockView>
            <nina:SequenceBlockView.SequenceItemContent>
                <StackPanel Orientation="Horizontal">
                    <TextBlock VerticalAlignment="Center" Text="Direction" />
                    <ComboBox Margin="5,0,0,0"
                              ItemsSource="{Binding Source={x:Static local:WaitForSkyBrightness.DirectionChoices}}"
                              SelectedItem="{Binding Direction}" />
                    <TextBlock VerticalAlignment="Center" Margin="10,0,0,0" Text="Exposure (s)" />
                    <TextBox MinWidth="35" Margin="5,0,0,0" TextAlignment="Right" Text="{Binding ExposureTime}" />
                    <TextBlock VerticalAlignment="Center" Margin="10,0,0,0" Text="Gain" />
                    <TextBox MinWidth="35" Margin="5,0,0,0" TextAlignment="Right" Text="{Binding Gain}" />
                    <TextBlock VerticalAlignment="Center" Margin="10,0,0,0" Text="Offset" />
                    <TextBox MinWidth="35" Margin="5,0,0,0" TextAlignment="Right" Text="{Binding Offset}" />
                    <TextBlock VerticalAlignment="Center" Margin="10,0,0,0" Text="Bin" />
                    <TextBox MinWidth="25" Margin="5,0,0,0" TextAlignment="Right" Text="{Binding Binning}" />
                    <TextBlock VerticalAlignment="Center" Margin="10,0,0,0" Text="Interval (s)" />
                    <TextBox MinWidth="35" Margin="5,0,0,0" TextAlignment="Right" Text="{Binding IntervalSeconds}" />
                    <TextBlock VerticalAlignment="Center" Margin="10,0,0,0" Text="Target (%)" />
                    <TextBox MinWidth="35" Margin="5,0,0,0" TextAlignment="Right" Text="{Binding TargetPercent}" />
                    <TextBlock VerticalAlignment="Center" Margin="10,0,0,0" Text="Tolerance (%)" />
                    <TextBox MinWidth="35" Margin="5,0,0,0" TextAlignment="Right" Text="{Binding TolerancePercent}" />
                    <TextBlock VerticalAlignment="Center" Margin="10,0,0,0" FontStyle="Italic" Text="{Binding AduSummary}" />
                </StackPanel>
            </nina:SequenceBlockView.SequenceItemContent>
        </nina:SequenceBlockView>
    </DataTemplate>

    <DataTemplate x:Key="SpeckSequenceHelpers.Instructions.WaitForSkyBrightness_Mini">
        <mini:MiniSequenceItem />
    </DataTemplate>
```

- [ ] **Step 3: Verify no stale references remain**

Run: `grep -rn "SkyMedian\|MinMedian\|MaxMedian" src/ tests/ || echo NO-STALE-REFS`
Expected: `NO-STALE-REFS`.

- [ ] **Step 4: Run the build**

Run: `dotnet build SpeckSequenceHelpers.sln -v q 2>&1 | tail -5`
Expected: `0 Error(s)`. If `HistogramMath` fails to resolve, confirm the using is `NINA.Image.ImageAnalysis` — it is in the `NINA.Image` assembly, which the NINA.Plugin package already brings in transitively (the project has no direct reference to add). Do not restructure the code to avoid it; report BLOCKED with the error instead.

- [ ] **Step 5: Run the tests and the format gate**

Run: `dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 32`.

Run: `dotnet format SpeckSequenceHelpers.sln --verify-no-changes && echo FORMAT-CLEAN`
Expected: `FORMAT-CLEAN`.

- [ ] **Step 6: Report (do not commit)**

---

### Task 3: Update the user-facing documentation

**Files:**
- Modify: `README.md` (the "Wait for sky median" bullet in the instruction list)
- Modify: `docs/rig-testing.md` (the "Wait for sky median" section)

**Interfaces:**
- Consumes: the shipped behavior from Task 2 (percent settings, ADU label, rename).
- Produces: nothing code-facing.

- [ ] **Step 1: Update the README bullet**

In `README.md`, replace the third instruction bullet (currently starting `- **Wait for sky median** —`) with exactly:

```markdown
- **Wait for sky brightness** — repeatedly takes throwaway exposures (never saved) and waits
  until the histogram mean reaches a target, within tolerance. Target and tolerance are
  entered as percentages exactly like NINA's flat wizard ("Histogram Mean Target" / "Mean
  Tolerance"), and the instruction shows the equivalent ADU window for the connected camera.
  Direction-aware for dawn (Brightening) or dusk (Dimming) flats; fails when the brightness
  window is overshot.
```

- [ ] **Step 2: Update the rig-testing section**

In `docs/rig-testing.md`, replace the entire `## Wait for sky median (simulator camera is fine)` section — its heading and all five checklist items — with exactly:

```markdown
## Wait for sky brightness (simulator camera is fine)

- [ ] With the camera connected, the row shows an ADU label matching the camera's bit depth
      (e.g. target 50% / tolerance 10% on a 16-bit camera reads `≈ 32,768 ± 3,277 ADU (16-bit)`),
      and it updates as the target/tolerance percentages change.
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
```

- [ ] **Step 3: Verify no stale references remain**

Run: `grep -rn "sky median\|WaitForSkyMedian" README.md docs/rig-testing.md || echo NO-STALE-DOCS`
Expected: `NO-STALE-DOCS`.

- [ ] **Step 4: Re-run the gates**

Run: `dotnet format SpeckSequenceHelpers.sln --verify-no-changes && echo FORMAT-CLEAN`
Expected: `FORMAT-CLEAN` (markdown is not formatted by the tool; this confirms nothing else drifted).

- [ ] **Step 5: Report (do not commit)**

---

## Plan self-review notes

- Spec coverage: rename → Tasks 1 and 2; mean statistic → Task 2 (`stats.Mean`); percent settings + defaults → Task 2; HistogramMath parity with the image's own bit depth → Task 2 `CreateGate`; ADU label from camera bit depth with disconnected placeholder → Task 2 `UpdateAduSummary`; Core rename with preserved tests → Task 1; validation ranges (tolerance strictly positive) → Task 2 `Validate`; documentation ripples → Task 3; parent-spec supersession marker → already committed in `557e02c`.
- Test count stays 32 by design: the new logic is NINA's `HistogramMath` (not ours to unit-test) plus WPF/mediator glue that only the rig can exercise — which is what Task 3's checklist items cover.
- Type consistency checked: `SkyBrightnessGate(minAdu, maxAdu, direction)` / `Evaluate(meanAdu)` / `GateVerdict.Action`+`.Reason` used identically in Tasks 1 and 2; `TargetPercent`/`TolerancePercent`/`AduSummary`/`DirectionChoices` named identically in the C# and the XAML bindings.
