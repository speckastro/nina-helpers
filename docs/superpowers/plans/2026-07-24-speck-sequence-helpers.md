# Speck Sequence Helpers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A publishable NINA 3.x plugin ("Speck Sequence Helpers") providing three advanced-sequencer instructions: Dithered Slew, Check Rotation, and Wait For Sky Median.

**Architecture:** Single plugin DLL built from the official template pattern (`net8.0-windows` + WPF + MEF exports), with all decision logic in NINA-free classes under `Core/` that a plain `net8.0` xunit project compiles via linked sources and runs on Linux. Instructions are thin adapters that call NINA mediators/services, mirroring the exact patterns of NINA's built-in instructions (verified against NINA source 3.3.0.1050 and template repo — clones in the session scratchpad under `nina/` and `nina.plugin.template/`).

**Tech Stack:** .NET 8 SDK (installed at `~/.dotnet`, on PATH), NINA.Plugin 3.2.0.9001 (NuGet), xunit, Newtonsoft.Json (ships with NINA), WPF (cross-compiled with `EnableWindowsTargeting`).

## Global Constraints

- Target framework `net8.0-windows`, `UseWPF=true`, `EnableWindowsTargeting=true`; all builds must pass on this Linux machine via `dotnet build`.
- NINA package pin: `NINA.Plugin` **3.2.0.9001** (stable; matches the user's NINA 3.x stable install). `MinimumApplicationVersion` = `3.2.0.9001`.
- Plugin display name / sequencer category: **"Speck Sequence Helpers"**. Assembly/root namespace: `SpeckSequenceHelpers`. Author: **Speck Astro**. Repo URL: `https://github.com/speckastro/nina-helpers`. License: **MPL-2.0**. Plugin GUID: `c66fa242-868a-47c0-ad62-54d3439af382`.
- Exported instruction classes live in namespace `SpeckSequenceHelpers.Instructions` — **frozen forever once published** (NINA saved sequences deserialize by fully-qualified type name).
- Files under `src/SpeckSequenceHelpers/Core/` must have **zero** dependencies beyond the BCL (no NINA, no WPF, no Newtonsoft) — they are compiled into the Linux test project via linked sources.
- Follow NINA code style in instruction files: 4-space indent, braces on same line, `RaisePropertyChanged()` for INPC, `Logger` for logs, `Notification` for toasts.
- Commit after every green task. Commit messages end with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- If a `using` directive or member name in the instruction tasks doesn't compile, resolve it against the NINA source clone at `/tmp/claude-1000/-home-mwarren-src-nina-helpers/fd463a25-c07b-46f4-9abf-1f81c27df6e2/scratchpad/nina` (grep note: many NINA `.cs` files contain a non-UTF-8 byte, so `grep` may treat them as binary — use `grep -a` or read files directly).

### Key NINA API facts (verified against source, for all tasks)

- `SequenceItem` (NINA.Sequencer.SequenceItem): abstract `object Clone()` and `Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token)`; copy-ctor + `CopyMetaData(cloneMe)` pattern; `AfterParentChanged()` virtual.
- `IValidatable` is in namespace `NINA.Sequencer.Validations`: `IList<string> Issues { get; }` + `bool Validate()`.
- `SequenceEntityFailedException` is in `NINA.Core.Model`. Throwing it from `Execute` fails the instruction; with default `ErrorBehavior = ContinueOnError` the sequence continues.
- `ItemUtility.RetrieveContextCoordinates(Parent)` (NINA.Sequencer.Utility) walks up parents and returns `ContextCoordinates { Coordinates Coordinates, double PositionAngle, ... }` or `null`.
- Built-in slew pattern: `var stopped = await guiderMediator.StopGuiding(token); await telescopeMediator.SlewToCoordinatesAsync(coords, token); if (stopped) await guiderMediator.StartGuiding(false, progress, token);`. `SlewToCoordinatesAsync` returns `Task<bool>` (false = not connected/parked), waits for slew + mount settle itself, does NOT touch guiding.
- `Coordinates.Shift(deltaXDegrees, deltaYDegrees, rotation)` returns properly projected offset coordinates (handles cos(dec)); arcsec→degrees is `/3600d`.
- Guider: `guiderMediator.GetInfo()` → `GuiderInfo { bool Connected, double PixelScale /* arcsec/px */ }`; profile dither amount: `profileService.ActiveProfile.GuiderSettings.DitherPixels` (double, guide-camera px).
- Plate solving: `plateSolverFactory.GetPlateSolver(profile.PlateSolveSettings)` / `GetBlindSolver(...)`; `GetCaptureSolver(plateSolver, blindSolver, imagingMediator, filterWheelMediator).Solve(seq, captureSolverParameter, solveProgress, progress, token)` → `PlateSolveResult { bool Success, double PositionAngle /* 0..360 */, ... }`; `GetCenteringSolver(plateSolver, blindSolver, imagingMediator, telescopeMediator, filterWheelMediator, domeMediator, domeFollower).Center(seq, centerSolveParameter, solveProgress, progress, token)` → `PlateSolveResult`. `solveProgress` params are null-safe (`IProgress<PlateSolveProgress>`).
- Capture + median: `var exp = await imagingMediator.CaptureImage(captureSequence, token, progress); var data = await exp.ToImageData(progress, token); var stats = await data.Statistics; double median = stats.Median;` — captured snapshots are NOT saved (saving only happens via ImageSaveMediator, which we never call).
- `CaptureSequence(double exposureTime, string imageType /* CaptureSequence.ImageTypes.SNAPSHOT */, FilterInfo filter /* null = keep current */, BinningMode binning, int count)`; `Gain`/`Offset` int props, `-1` = camera default.
- Waits: `CoreUtil.Wait(TimeSpan, bool countDown, CancellationToken, IProgress<ApplicationStatus>, string status)` (NINA.Core.Utility) — 100 ms granularity, cancellation-safe, reports progress.
- MEF: `[ExportMetadata("Name"/"Description"/"Icon"/"Category", ...)]` + `[Export(typeof(ISequenceItem))]` + `[JsonObject(MemberSerialization.OptIn)]`; non-`Lbl_`-prefixed metadata strings are displayed verbatim. `Icon` names a `GeometryGroup` key in an exported ResourceDictionary.
- DataTemplates: one ResourceDictionary with code-behind `[Export(typeof(ResourceDictionary))]`; detail template matched by `DataType`; mini template keyed `"<FullyQualifiedTypeName>_Mini"`.
- Toasts: `Notification.ShowInformation(string)` / `ShowError(string)` (NINA.Core.Utility.Notification). Logs: `Logger.Info/Warning/Error` (NINA.Core.Utility).

---

### Task 1: Solution and plugin project scaffold

**Files:**
- Create: `SpeckSequenceHelpers.sln` (via `dotnet new sln`)
- Create: `src/SpeckSequenceHelpers/SpeckSequenceHelpers.csproj`
- Create: `src/SpeckSequenceHelpers/Properties/AssemblyInfo.cs`
- Create: `src/SpeckSequenceHelpers/SpeckSequenceHelpersPlugin.cs`
- Create: `src/SpeckSequenceHelpers/Instructions/InstructionTemplates.xaml`
- Create: `src/SpeckSequenceHelpers/Instructions/InstructionTemplates.xaml.cs`
- Create: `.gitignore`, `LICENSE`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: a building plugin assembly; `InstructionTemplates.xaml` ResourceDictionary that Tasks 5–7 add templates into; project + solution that Task 2 adds the test project to.

- [ ] **Step 1: Create solution, gitignore, license**

```bash
cd /home/mwarren/src/nina-helpers
dotnet new sln -n SpeckSequenceHelpers
printf 'bin/\nobj/\n*.user\n.vs/\n' > .gitignore
curl -sL https://www.mozilla.org/MPL/2.0/index.txt -o LICENSE
head -1 LICENSE
```

Expected: `head -1 LICENSE` prints `Mozilla Public License Version 2.0`. If the URL fails, fetch the canonical text from `https://raw.githubusercontent.com/spdx/license-list-data/main/text/MPL-2.0.txt`.

- [ ] **Step 2: Write the project file**

`src/SpeckSequenceHelpers/SpeckSequenceHelpers.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <OutputType>Library</OutputType>
    <RootNamespace>SpeckSequenceHelpers</RootNamespace>
    <AssemblyName>SpeckSequenceHelpers</AssemblyName>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <UseWPF>true</UseWPF>
    <ImportWindowsDesktopTargets>true</ImportWindowsDesktopTargets>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NINA.Plugin" Version="3.2.0.9001" />
  </ItemGroup>
  <Target Name="DeployToNina" AfterTargets="PostBuildEvent" Condition="'$(OS)' == 'Windows_NT'">
    <Exec Command="xcopy &quot;$(TargetPath)&quot; &quot;%localappdata%\NINA\Plugins\3.0.0\$(TargetName)&quot; /h/i/c/k/e/r/y" />
  </Target>
</Project>
```

(The `DeployToNina` target auto-installs the DLL when building on the Windows rig; it is skipped on Linux.)

- [ ] **Step 3: Write AssemblyInfo.cs**

`src/SpeckSequenceHelpers/Properties/AssemblyInfo.cs`:

```csharp
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: Guid("c66fa242-868a-47c0-ad62-54d3439af382")]

[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]

[assembly: AssemblyTitle("Speck Sequence Helpers")]
[assembly: AssemblyDescription("Advanced sequencer helpers: dithered slews for mosaic panel cycling, plate-solved rotation checks, and sky-median gating for flats")]

[assembly: AssemblyCompany("Speck Astro")]
[assembly: AssemblyProduct("Speck Sequence Helpers")]
[assembly: AssemblyCopyright("Copyright © 2026 Speck Astro")]

[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]

[assembly: AssemblyMetadata("License", "MPL-2.0")]
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
[assembly: AssemblyMetadata("Repository", "https://github.com/speckastro/nina-helpers")]

[assembly: AssemblyMetadata("Tags", "Sequencer,Mosaic,Dither,Rotation,Flats")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/speckastro/nina-helpers/blob/main/CHANGELOG.md")]
[assembly: AssemblyMetadata("FeaturedImageURL", "")]
[assembly: AssemblyMetadata("ScreenshotURL", "")]
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
[assembly: AssemblyMetadata("LongDescription", @"Three advanced sequencer instructions:

* Dithered slew - slew to the parent target with a small random offset, so rapid mosaic panel cycling needs no separate dither. Offset amplitude comes from your guider dither settings automatically, or a manual radius. Optional plate-solve centering on the offset coordinates.
* Check rotation - plate solve and compare the measured position angle against the target's position angle. Reports the measurement unobtrusively; fails the instruction if a configured tolerance is exceeded.
* Wait for sky median - repeatedly take throwaway exposures until the image median enters a configured range. Designed for dawn/dusk sky flats; fails if the brightness window is overshot.")]

[assembly: ComVisible(false)]
```

- [ ] **Step 4: Write the plugin manifest class**

`src/SpeckSequenceHelpers/SpeckSequenceHelpersPlugin.cs`:

```csharp
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using System.ComponentModel.Composition;

namespace SpeckSequenceHelpers {

    /// <summary>
    /// Plugin manifest. All metadata is read from Properties/AssemblyInfo.cs by PluginBase.
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class SpeckSequenceHelpersPlugin : PluginBase {

        [ImportingConstructor]
        public SpeckSequenceHelpersPlugin() {
        }
    }
}
```

- [ ] **Step 5: Write the (empty) shared templates ResourceDictionary**

`src/SpeckSequenceHelpers/Instructions/InstructionTemplates.xaml`:

```xml
<ResourceDictionary
    x:Class="SpeckSequenceHelpers.Instructions.InstructionTemplates"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="clr-namespace:SpeckSequenceHelpers.Instructions"
    xmlns:nina="clr-namespace:NINA.View.Sequencer;assembly=NINA.Sequencer"
    xmlns:mini="clr-namespace:NINA.View.Sequencer.MiniSequencer;assembly=NINA.Sequencer">
</ResourceDictionary>
```

`src/SpeckSequenceHelpers/Instructions/InstructionTemplates.xaml.cs`:

```csharp
using System.ComponentModel.Composition;
using System.Windows;

namespace SpeckSequenceHelpers.Instructions {

    [Export(typeof(ResourceDictionary))]
    public partial class InstructionTemplates : ResourceDictionary {

        public InstructionTemplates() {
            InitializeComponent();
        }
    }
}
```

- [ ] **Step 6: Add project to solution and build**

```bash
cd /home/mwarren/src/nina-helpers
dotnet sln add src/SpeckSequenceHelpers
dotnet build src/SpeckSequenceHelpers -v q 2>&1 | tail -5
```

Expected: `0 Error(s)` (NU1701 warnings about `VVVV.FreeImage` are normal — NINA's own transitive baggage). This step also proves WPF XAML markup compilation works on Linux; if it fails here, stop and report — do not work around silently.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "Scaffold Speck Sequence Helpers plugin project

net8.0-windows + WPF cross-compiled from Linux via EnableWindowsTargeting,
pinned to NINA.Plugin 3.2.0.9001.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Test project + DitherOffsetCalculator (TDD)

**Files:**
- Create: `tests/SpeckSequenceHelpers.Core.Tests/SpeckSequenceHelpers.Core.Tests.csproj`
- Create: `tests/SpeckSequenceHelpers.Core.Tests/DitherOffsetCalculatorTests.cs`
- Create: `src/SpeckSequenceHelpers/Core/DitherOffsetCalculator.cs`

**Interfaces:**
- Consumes: solution from Task 1.
- Produces: `SpeckSequenceHelpers.Core.DitherOffsetCalculator.Generate(double maxRadiusArcsec, Random random)` → `OffsetVector { double RaArcsec; double DecArcsec; double RadiusArcsec }` (used by Task 5). Test project that Tasks 3–4 add test files to.

- [ ] **Step 1: Create the test project**

`tests/SpeckSequenceHelpers.Core.Tests/SpeckSequenceHelpers.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="../../src/SpeckSequenceHelpers/Core/**/*.cs" LinkBase="Core" />
  </ItemGroup>
</Project>
```

```bash
cd /home/mwarren/src/nina-helpers && dotnet sln add tests/SpeckSequenceHelpers.Core.Tests
```

- [ ] **Step 2: Write the failing tests**

`tests/SpeckSequenceHelpers.Core.Tests/DitherOffsetCalculatorTests.cs`:

```csharp
using System;
using SpeckSequenceHelpers.Core;
using Xunit;

namespace SpeckSequenceHelpers.Core.Tests {

    public class DitherOffsetCalculatorTests {

        [Fact]
        public void Generate_StaysWithinMaxRadius() {
            var random = new Random(42);
            for (var i = 0; i < 10_000; i++) {
                var offset = DitherOffsetCalculator.Generate(30, random);
                Assert.True(offset.RadiusArcsec <= 30.0 + 1e-9, $"radius {offset.RadiusArcsec} exceeded max");
            }
        }

        [Fact]
        public void Generate_ProducesVaryingOffsets() {
            var random = new Random(42);
            var a = DitherOffsetCalculator.Generate(30, random);
            var b = DitherOffsetCalculator.Generate(30, random);
            Assert.False(a.RaArcsec == b.RaArcsec && a.DecArcsec == b.DecArcsec);
        }

        [Fact]
        public void Generate_CoversAllQuadrants() {
            var random = new Random(1);
            int posRa = 0, negRa = 0, posDec = 0, negDec = 0;
            for (var i = 0; i < 1000; i++) {
                var o = DitherOffsetCalculator.Generate(10, random);
                if (o.RaArcsec > 0) { posRa++; } else { negRa++; }
                if (o.DecArcsec > 0) { posDec++; } else { negDec++; }
            }
            Assert.True(posRa > 100 && negRa > 100 && posDec > 100 && negDec > 100,
                $"quadrant counts: +RA {posRa}, -RA {negRa}, +Dec {posDec}, -Dec {negDec}");
        }

        [Fact]
        public void Generate_ZeroRadius_ReturnsZeroOffset() {
            var o = DitherOffsetCalculator.Generate(0, new Random(7));
            Assert.Equal(0, o.RadiusArcsec, 12);
        }

        [Fact]
        public void Generate_NegativeRadius_Throws() {
            Assert.Throws<ArgumentOutOfRangeException>(() => DitherOffsetCalculator.Generate(-1, new Random(7)));
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -5
```

Expected: build FAILS with `CS0103`/`CS0246` — `DitherOffsetCalculator` does not exist yet.

- [ ] **Step 4: Write the implementation**

`src/SpeckSequenceHelpers/Core/DitherOffsetCalculator.cs`:

```csharp
using System;

namespace SpeckSequenceHelpers.Core {

    public readonly struct OffsetVector {

        public OffsetVector(double raArcsec, double decArcsec) {
            RaArcsec = raArcsec;
            DecArcsec = decArcsec;
        }

        public double RaArcsec { get; }
        public double DecArcsec { get; }
        public double RadiusArcsec => Math.Sqrt(RaArcsec * RaArcsec + DecArcsec * DecArcsec);
    }

    public static class DitherOffsetCalculator {

        /// <summary>
        /// Generates a random offset uniformly distributed over a disc of the given radius.
        /// r = R*sqrt(u) makes the distribution uniform by area rather than clustered at the center.
        /// </summary>
        public static OffsetVector Generate(double maxRadiusArcsec, Random random) {
            if (maxRadiusArcsec < 0) { throw new ArgumentOutOfRangeException(nameof(maxRadiusArcsec)); }
            if (random == null) { throw new ArgumentNullException(nameof(random)); }

            var radius = maxRadiusArcsec * Math.Sqrt(random.NextDouble());
            var theta = 2d * Math.PI * random.NextDouble();
            return new OffsetVector(radius * Math.Cos(theta), radius * Math.Sin(theta));
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -3
```

Expected: `Passed! - Failed: 0, Passed: 5`. Also run `dotnet build src/SpeckSequenceHelpers -v q 2>&1 | tail -3` → `0 Error(s)` (the plugin picks the file up automatically).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Add dither offset calculator with Linux-runnable tests

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: AngleMath (TDD)

**Files:**
- Create: `tests/SpeckSequenceHelpers.Core.Tests/AngleMathTests.cs`
- Create: `src/SpeckSequenceHelpers/Core/AngleMath.cs`

**Interfaces:**
- Consumes: test project from Task 2.
- Produces: `SpeckSequenceHelpers.Core.AngleMath.RotationDelta(double measuredDegrees, double targetDegrees, bool treatFlippedAsEqual)` → `double` (used by Task 6).

- [ ] **Step 1: Write the failing tests**

`tests/SpeckSequenceHelpers.Core.Tests/AngleMathTests.cs`:

```csharp
using SpeckSequenceHelpers.Core;
using Xunit;

namespace SpeckSequenceHelpers.Core.Tests {

    public class AngleMathTests {

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(10, 350, 20)]
        [InlineData(350, 10, 20)]
        [InlineData(90, 270, 180)]
        [InlineData(359.5, 0.5, 1)]
        [InlineData(725, 5, 0)]
        public void SmallestDifference_HandlesWrap(double a, double b, double expected) {
            Assert.Equal(expected, AngleMath.SmallestDifference(a, b), 9);
        }

        [Theory]
        [InlineData(90, 270, false, 180)]
        [InlineData(90, 270, true, 0)]
        [InlineData(0, 100, false, 100)]
        [InlineData(0, 100, true, 80)]
        [InlineData(123.4, 123.0, true, 0.4)]
        [InlineData(303.4, 123.0, true, 0.4)]
        public void RotationDelta_HonorsFlipEquivalence(double measured, double target, bool flipEqual, double expected) {
            Assert.Equal(expected, AngleMath.RotationDelta(measured, target, flipEqual), 9);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -5
```

Expected: build FAILS — `AngleMath` does not exist.

- [ ] **Step 3: Write the implementation**

`src/SpeckSequenceHelpers/Core/AngleMath.cs`:

```csharp
using System;

namespace SpeckSequenceHelpers.Core {

    public static class AngleMath {

        /// <summary>Shortest angular distance between two angles in degrees, result in [0, 180].</summary>
        public static double SmallestDifference(double aDegrees, double bDegrees) {
            var diff = Math.Abs(aDegrees - bDegrees) % 360d;
            return diff > 180d ? 360d - diff : diff;
        }

        /// <summary>
        /// Rotation error between a measured and target position angle. With
        /// treatFlippedAsEqual, a 180-degree flip counts as identical framing, so the
        /// result is the distance mod 180 (range [0, 90]).
        /// </summary>
        public static double RotationDelta(double measuredDegrees, double targetDegrees, bool treatFlippedAsEqual) {
            var delta = SmallestDifference(measuredDegrees, targetDegrees);
            return treatFlippedAsEqual && delta > 90d ? 180d - delta : delta;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -3
```

Expected: `Passed! - Failed: 0, Passed: 17` (5 from Task 2 + 12 theory cases).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Add angle math for rotation tolerance checks

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: SkyMedianGate (TDD)

**Files:**
- Create: `tests/SpeckSequenceHelpers.Core.Tests/SkyMedianGateTests.cs`
- Create: `src/SpeckSequenceHelpers/Core/SkyMedianGate.cs`

**Interfaces:**
- Consumes: test project from Task 2.
- Produces (used by Task 7): `SpeckSequenceHelpers.Core.GateDirection { Brightening, Dimming }`, `GateAction { Proceed, Wait, Fail }`, `SkyMedianGate(double minMedian, double maxMedian, GateDirection direction)` with `GateVerdict Evaluate(double median)` where `GateVerdict { GateAction Action; string Reason }`. Ctor throws `ArgumentException` when `minMedian >= maxMedian`.

- [ ] **Step 1: Write the failing tests**

`tests/SpeckSequenceHelpers.Core.Tests/SkyMedianGateTests.cs`:

```csharp
using System;
using SpeckSequenceHelpers.Core;
using Xunit;

namespace SpeckSequenceHelpers.Core.Tests {

    public class SkyMedianGateTests {

        [Theory]
        [InlineData(GateDirection.Brightening, 1500, GateAction.Proceed)]  // in range
        [InlineData(GateDirection.Brightening, 1000, GateAction.Proceed)]  // == min boundary
        [InlineData(GateDirection.Brightening, 5000, GateAction.Proceed)]  // == max boundary
        [InlineData(GateDirection.Brightening, 999, GateAction.Wait)]      // below min: dawn, keep waiting
        [InlineData(GateDirection.Brightening, 5001, GateAction.Fail)]     // above max: dawn overshot
        [InlineData(GateDirection.Dimming, 1500, GateAction.Proceed)]      // in range
        [InlineData(GateDirection.Dimming, 5001, GateAction.Wait)]         // above max: dusk, keep waiting
        [InlineData(GateDirection.Dimming, 999, GateAction.Fail)]          // below min: dusk overshot
        public void Evaluate_AppliesDirectionalWindow(GateDirection direction, double median, GateAction expected) {
            var gate = new SkyMedianGate(1000, 5000, direction);
            Assert.Equal(expected, gate.Evaluate(median).Action);
        }

        [Fact]
        public void Evaluate_FirstReadingCanFail() {
            // dawn gate, sky already too bright on the very first exposure
            var gate = new SkyMedianGate(1000, 5000, GateDirection.Brightening);
            Assert.Equal(GateAction.Fail, gate.Evaluate(60000).Action);
        }

        [Fact]
        public void Evaluate_ReasonMentionsMedian() {
            var gate = new SkyMedianGate(1000, 5000, GateDirection.Brightening);
            Assert.Contains("812", gate.Evaluate(812).Reason);
        }

        [Fact]
        public void Constructor_MinNotBelowMax_Throws() {
            Assert.Throws<ArgumentException>(() => new SkyMedianGate(5000, 5000, GateDirection.Brightening));
            Assert.Throws<ArgumentException>(() => new SkyMedianGate(6000, 5000, GateDirection.Brightening));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -5
```

Expected: build FAILS — `SkyMedianGate`/`GateDirection`/`GateAction` do not exist.

- [ ] **Step 3: Write the implementation**

`src/SpeckSequenceHelpers/Core/SkyMedianGate.cs`:

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
    /// Decides whether a measured sky median means the sequence can proceed, should keep
    /// waiting, or has overshot the usable brightness window (dawn = Brightening, dusk = Dimming).
    /// </summary>
    public class SkyMedianGate {
        private readonly double minMedian;
        private readonly double maxMedian;
        private readonly GateDirection direction;

        public SkyMedianGate(double minMedian, double maxMedian, GateDirection direction) {
            if (minMedian >= maxMedian) {
                throw new ArgumentException($"Min median ({minMedian}) must be less than max median ({maxMedian})");
            }
            this.minMedian = minMedian;
            this.maxMedian = maxMedian;
            this.direction = direction;
        }

        public GateVerdict Evaluate(double median) {
            if (median >= minMedian && median <= maxMedian) {
                return new GateVerdict(GateAction.Proceed, $"Median {median:F0} ADU within [{minMedian:F0}, {maxMedian:F0}]");
            }
            if (direction == GateDirection.Brightening) {
                return median < minMedian
                    ? new GateVerdict(GateAction.Wait, $"Median {median:F0} ADU below min {minMedian:F0}, waiting for sky to brighten")
                    : new GateVerdict(GateAction.Fail, $"Median {median:F0} ADU exceeds max {maxMedian:F0}, brightness window overshot");
            }
            return median > maxMedian
                ? new GateVerdict(GateAction.Wait, $"Median {median:F0} ADU above max {maxMedian:F0}, waiting for sky to dim")
                : new GateVerdict(GateAction.Fail, $"Median {median:F0} ADU below min {minMedian:F0}, brightness window overshot");
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -3
```

Expected: `Passed! - Failed: 0, Passed: 28`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Add sky median gate state machine for flat brightness windows

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Dithered Slew instruction

**Files:**
- Create: `src/SpeckSequenceHelpers/Instructions/DitheredSlew.cs`
- Modify: `src/SpeckSequenceHelpers/Instructions/InstructionTemplates.xaml` (add icon + templates inside the root element)

**Interfaces:**
- Consumes: `DitherOffsetCalculator.Generate(double, Random)` → `OffsetVector { RaArcsec, DecArcsec, RadiusArcsec }` (Task 2); ResourceDictionary shell (Task 1).
- Produces: exported sequence instruction `SpeckSequenceHelpers.Instructions.DitheredSlew` (namespace frozen once published).

- [ ] **Step 1: Write the instruction class**

`src/SpeckSequenceHelpers/Instructions/DitheredSlew.cs`:

```csharp
using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.PlateSolving;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Utility;
using NINA.Sequencer.Validations;
using SpeckSequenceHelpers.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace SpeckSequenceHelpers.Instructions {

    [ExportMetadata("Name", "Dithered slew")]
    [ExportMetadata("Description", "Slews to the parent target's coordinates plus a small random offset, replacing a separate dither when cycling mosaic panels. Offset radius comes from the guider dither settings, or a manual radius. Optionally plate-solves and centers on the offset coordinates.")]
    [ExportMetadata("Icon", "Speck_DitheredSlew_SVG")]
    [ExportMetadata("Category", "Speck Sequence Helpers")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class DitheredSlew : SequenceItem, IValidatable {
        private static readonly Random random = new Random();

        private readonly IProfileService profileService;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IDomeMediator domeMediator;
        private readonly IDomeFollower domeFollower;
        private readonly IPlateSolverFactory plateSolverFactory;

        [ImportingConstructor]
        public DitheredSlew(IProfileService profileService,
                            ITelescopeMediator telescopeMediator,
                            IGuiderMediator guiderMediator,
                            ICameraMediator cameraMediator,
                            IImagingMediator imagingMediator,
                            IFilterWheelMediator filterWheelMediator,
                            IDomeMediator domeMediator,
                            IDomeFollower domeFollower,
                            IPlateSolverFactory plateSolverFactory) {
            this.profileService = profileService;
            this.telescopeMediator = telescopeMediator;
            this.guiderMediator = guiderMediator;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.domeMediator = domeMediator;
            this.domeFollower = domeFollower;
            this.plateSolverFactory = plateSolverFactory;
        }

        private DitheredSlew(DitheredSlew cloneMe) : this(cloneMe.profileService,
                                                          cloneMe.telescopeMediator,
                                                          cloneMe.guiderMediator,
                                                          cloneMe.cameraMediator,
                                                          cloneMe.imagingMediator,
                                                          cloneMe.filterWheelMediator,
                                                          cloneMe.domeMediator,
                                                          cloneMe.domeFollower,
                                                          cloneMe.plateSolverFactory) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new DitheredSlew(this) {
                UseManualRadius = UseManualRadius,
                ManualRadiusArcsec = ManualRadiusArcsec,
                CenterAfterSlew = CenterAfterSlew
            };
        }

        private bool useManualRadius = false;

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

        private bool centerAfterSlew = false;

        [JsonProperty]
        public bool CenterAfterSlew {
            get => centerAfterSlew;
            set {
                centerAfterSlew = value;
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
            var context = ItemUtility.RetrieveContextCoordinates(Parent);
            if (context == null) {
                throw new SequenceEntityFailedException("Dithered slew: no target coordinates found in any parent container");
            }

            if (telescopeMediator.GetInfo().AtPark) {
                Notification.ShowError("Dithered slew: telescope is parked");
                throw new SequenceEntityFailedException("Dithered slew: telescope is parked");
            }

            var radiusArcsec = ResolveMaxRadiusArcsec();
            if (radiusArcsec <= 0) {
                throw new SequenceEntityFailedException("Dithered slew: dither radius unavailable - guider reports no pixel scale and no manual radius is set");
            }

            var offset = DitherOffsetCalculator.Generate(radiusArcsec, random);
            var target = context.Coordinates.Shift(offset.RaArcsec / 3600d, offset.DecArcsec / 3600d, 0);
            Logger.Info($"Dithered slew: offset {offset.RadiusArcsec:F1}\" of max {radiusArcsec:F1}\" (dRA {offset.RaArcsec:F1}\", dDec {offset.DecArcsec:F1}\"), slewing to {target}");
            progress?.Report(new ApplicationStatus() { Status = $"Dithered slew: offset {offset.RadiusArcsec:F1}\" (dRA {offset.RaArcsec:F1}\", dDec {offset.DecArcsec:F1}\")" });

            var stoppedGuiding = await guiderMediator.StopGuiding(token);

            var success = true;
            string failure = null;
            if (CenterAfterSlew) {
                var result = await SlewAndCenter(target, progress, token);
                if (result?.Success != true) {
                    success = false;
                    failure = "Dithered slew: plate-solve centering failed";
                }
            } else {
                if (!await telescopeMediator.SlewToCoordinatesAsync(target, token)) {
                    success = false;
                    failure = "Dithered slew: slew failed";
                }
            }

            if (stoppedGuiding) {
                await guiderMediator.StartGuiding(false, progress, token);
            }

            if (!success) {
                throw new SequenceEntityFailedException(failure);
            }
        }

        private async Task<PlateSolveResult> SlewAndCenter(Coordinates target, IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (!await telescopeMediator.SlewToCoordinatesAsync(target, token)) {
                return new PlateSolveResult() { Success = false };
            }

            var solveSettings = profileService.ActiveProfile.PlateSolveSettings;
            var plateSolver = plateSolverFactory.GetPlateSolver(solveSettings);
            var blindSolver = plateSolverFactory.GetBlindSolver(solveSettings);
            var solver = plateSolverFactory.GetCenteringSolver(plateSolver, blindSolver, imagingMediator, telescopeMediator, filterWheelMediator, domeMediator, domeFollower);
            var parameter = new CenterSolveParameter() {
                Attempts = solveSettings.NumberOfAttempts,
                Binning = solveSettings.Binning,
                Coordinates = target,
                DownSampleFactor = solveSettings.DownSampleFactor,
                FocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength,
                MaxObjects = solveSettings.MaxObjects,
                PixelSize = profileService.ActiveProfile.CameraSettings.PixelSize,
                ReattemptDelay = TimeSpan.FromMinutes(solveSettings.ReattemptDelay),
                Regions = solveSettings.Regions,
                SearchRadius = solveSettings.SearchRadius,
                Threshold = solveSettings.Threshold,
                NoSync = profileService.ActiveProfile.TelescopeSettings.NoSync,
                BlindFailoverEnabled = solveSettings.BlindFailoverEnabled
            };
            var seq = new CaptureSequence(
                solveSettings.ExposureTime,
                CaptureSequence.ImageTypes.SNAPSHOT,
                solveSettings.Filter,
                new BinningMode(solveSettings.Binning, solveSettings.Binning),
                1) {
                Gain = solveSettings.Gain
            };
            return await solver.Center(seq, parameter, default, progress, token);
        }

        /// <summary>Max dither radius in arcseconds; 0 when unresolvable.</summary>
        public double ResolveMaxRadiusArcsec() {
            if (UseManualRadius) {
                return ManualRadiusArcsec > 0 ? ManualRadiusArcsec : 0;
            }
            var guiderInfo = guiderMediator.GetInfo();
            if (guiderInfo?.Connected != true || guiderInfo.PixelScale <= 0) {
                return 0;
            }
            return profileService.ActiveProfile.GuiderSettings.DitherPixels * guiderInfo.PixelScale;
        }

        public bool Validate() {
            var i = new List<string>();
            if (!telescopeMediator.GetInfo().Connected) {
                i.Add("Telescope is not connected");
            }
            if (ItemUtility.RetrieveContextCoordinates(Parent) == null) {
                i.Add("No target coordinates found - place this instruction inside a target container");
            }
            if (UseManualRadius) {
                if (ManualRadiusArcsec <= 0) {
                    i.Add("Manual dither radius must be greater than 0");
                }
            } else {
                var guiderInfo = guiderMediator.GetInfo();
                if (guiderInfo?.Connected != true) {
                    i.Add("Guider is not connected - connect a guider or enable the manual dither radius");
                } else if (guiderInfo.PixelScale <= 0) {
                    i.Add("Guider does not report a pixel scale - enable the manual dither radius");
                }
            }
            if (CenterAfterSlew && !cameraMediator.GetInfo().Connected) {
                i.Add("Camera must be connected to center after slew");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(DitheredSlew)}, UseManualRadius: {UseManualRadius}, ManualRadiusArcsec: {ManualRadiusArcsec}, CenterAfterSlew: {CenterAfterSlew}";
        }
    }
}
```

Notes for the implementer:
- `CenterSolveParameter` lives in `NINA.PlateSolving`; if the compiler can't find a member (e.g. `Threshold`), check `NINA.Platesolving/CenterSolveParameter.cs` in the source clone and adjust — do not delete parameters that exist.
- `GuiderInfo.PixelScale` is arcsec per guide-camera pixel; `DitherPixels` × `PixelScale` reproduces the sky displacement a native dither would command.
- We deliberately ignore the profile's "RA-only dither" flag (a slew moves both axes regardless) — this is in the spec.

- [ ] **Step 2: Add icon + DataTemplates to the ResourceDictionary**

Add inside the root `<ResourceDictionary>` element of `src/SpeckSequenceHelpers/Instructions/InstructionTemplates.xaml`:

```xml
    <GeometryGroup x:Key="Speck_DitheredSlew_SVG">
        <EllipseGeometry Center="9,9" RadiusX="8" RadiusY="8" />
        <EllipseGeometry Center="9,9" RadiusX="0.8" RadiusY="0.8" />
        <EllipseGeometry Center="13.5,5.5" RadiusX="1.6" RadiusY="1.6" />
        <PathGeometry Figures="M 9,9 L 12.5,6.5" />
    </GeometryGroup>

    <DataTemplate DataType="{x:Type local:DitheredSlew}">
        <nina:SequenceBlockView>
            <nina:SequenceBlockView.SequenceItemContent>
                <StackPanel Orientation="Horizontal">
                    <CheckBox VerticalAlignment="Center" IsChecked="{Binding UseManualRadius}" />
                    <TextBlock VerticalAlignment="Center" Margin="5,0,0,0" Text="Manual radius (arcsec)" />
                    <TextBox MinWidth="40" Margin="5,0,0,0" TextAlignment="Right"
                             Text="{Binding ManualRadiusArcsec}" IsEnabled="{Binding UseManualRadius}" />
                    <CheckBox VerticalAlignment="Center" Margin="15,0,0,0" IsChecked="{Binding CenterAfterSlew}" />
                    <TextBlock VerticalAlignment="Center" Margin="5,0,0,0" Text="Center after slew" />
                </StackPanel>
            </nina:SequenceBlockView.SequenceItemContent>
        </nina:SequenceBlockView>
    </DataTemplate>

    <DataTemplate x:Key="SpeckSequenceHelpers.Instructions.DitheredSlew_Mini">
        <mini:MiniSequenceItem />
    </DataTemplate>
```

- [ ] **Step 3: Build**

```bash
dotnet build src/SpeckSequenceHelpers -v q 2>&1 | tail -5
```

Expected: `0 Error(s)`. Fix any member-name mismatches against the NINA source clone (see Global Constraints) — the architecture must not change.

- [ ] **Step 4: Run the full test suite (regression)**

```bash
dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -3
```

Expected: `Passed! - Failed: 0, Passed: 28`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Add Dithered slew instruction

Random disc offset on the parent target's coordinates, sized from the
profile's guider dither settings (or manual override), with optional
plate-solve centering on the offset coordinates.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Check Rotation instruction

**Files:**
- Create: `src/SpeckSequenceHelpers/Instructions/CheckRotation.cs`
- Modify: `src/SpeckSequenceHelpers/Instructions/InstructionTemplates.xaml` (add icon + templates)

**Interfaces:**
- Consumes: `AngleMath.RotationDelta(double, double, bool)` (Task 3); ResourceDictionary (Task 1).
- Produces: exported instruction `SpeckSequenceHelpers.Instructions.CheckRotation`.

- [ ] **Step 1: Write the instruction class**

`src/SpeckSequenceHelpers/Instructions/CheckRotation.cs`:

```csharp
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.PlateSolving;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Utility;
using NINA.Sequencer.Validations;
using SpeckSequenceHelpers.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace SpeckSequenceHelpers.Instructions {

    [ExportMetadata("Name", "Check rotation")]
    [ExportMetadata("Description", "Takes a plate-solve exposure and compares the measured position angle against the parent target's position angle. Shows the measurement as an info notification; fails the instruction when the difference exceeds the tolerance. Moves nothing.")]
    [ExportMetadata("Icon", "Speck_CheckRotation_SVG")]
    [ExportMetadata("Category", "Speck Sequence Helpers")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CheckRotation : SequenceItem, IValidatable {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IPlateSolverFactory plateSolverFactory;

        [ImportingConstructor]
        public CheckRotation(IProfileService profileService,
                             ICameraMediator cameraMediator,
                             IImagingMediator imagingMediator,
                             IFilterWheelMediator filterWheelMediator,
                             IPlateSolverFactory plateSolverFactory) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.plateSolverFactory = plateSolverFactory;
        }

        private CheckRotation(CheckRotation cloneMe) : this(cloneMe.profileService,
                                                            cloneMe.cameraMediator,
                                                            cloneMe.imagingMediator,
                                                            cloneMe.filterWheelMediator,
                                                            cloneMe.plateSolverFactory) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new CheckRotation(this) {
                ToleranceDegrees = ToleranceDegrees,
                TreatFlippedAsEqual = TreatFlippedAsEqual
            };
        }

        private double toleranceDegrees = 1.0;

        [JsonProperty]
        public double ToleranceDegrees {
            get => toleranceDegrees;
            set {
                toleranceDegrees = value;
                RaisePropertyChanged();
            }
        }

        private bool treatFlippedAsEqual = true;

        [JsonProperty]
        public bool TreatFlippedAsEqual {
            get => treatFlippedAsEqual;
            set {
                treatFlippedAsEqual = value;
                RaisePropertyChanged();
            }
        }

        private string lastMeasurement = "";

        /// <summary>Human-readable result of the last run, shown in the instruction row. Not persisted.</summary>
        public string LastMeasurement {
            get => lastMeasurement;
            private set {
                lastMeasurement = value;
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
            var context = ItemUtility.RetrieveContextCoordinates(Parent);
            if (context == null) {
                throw new SequenceEntityFailedException("Check rotation: no target found in any parent container");
            }

            var solveSettings = profileService.ActiveProfile.PlateSolveSettings;
            var plateSolver = plateSolverFactory.GetPlateSolver(solveSettings);
            var blindSolver = plateSolverFactory.GetBlindSolver(solveSettings);
            var captureSolver = plateSolverFactory.GetCaptureSolver(plateSolver, blindSolver, imagingMediator, filterWheelMediator);
            var parameter = new CaptureSolverParameter() {
                Attempts = 1,
                ReattemptDelay = TimeSpan.Zero,
                Binning = solveSettings.Binning,
                Coordinates = context.Coordinates,
                DownSampleFactor = solveSettings.DownSampleFactor,
                FocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength,
                MaxObjects = solveSettings.MaxObjects,
                PixelSize = profileService.ActiveProfile.CameraSettings.PixelSize,
                Regions = solveSettings.Regions,
                SearchRadius = solveSettings.SearchRadius,
                BlindFailoverEnabled = solveSettings.BlindFailoverEnabled
            };
            var seq = new CaptureSequence(
                solveSettings.ExposureTime,
                CaptureSequence.ImageTypes.SNAPSHOT,
                solveSettings.Filter,
                new BinningMode(solveSettings.Binning, solveSettings.Binning),
                1) {
                Gain = solveSettings.Gain
            };

            var result = await captureSolver.Solve(seq, parameter, default, progress, token);
            if (result?.Success != true) {
                Notification.ShowError("Check rotation: plate solve failed");
                throw new SequenceEntityFailedException("Check rotation: plate solve failed");
            }

            var delta = AngleMath.RotationDelta(result.PositionAngle, context.PositionAngle, TreatFlippedAsEqual);
            var message = $"Rotation: measured {result.PositionAngle:F2}°, target {context.PositionAngle:F2}°, Δ {delta:F2}°";
            LastMeasurement = message;
            Logger.Info($"Check rotation: {message} (tolerance {ToleranceDegrees:F2}°, flip-equivalent: {TreatFlippedAsEqual})");

            if (delta > ToleranceDegrees) {
                var error = $"{message} exceeds tolerance {ToleranceDegrees:F2}°";
                Notification.ShowError($"Check rotation: {error}");
                throw new SequenceEntityFailedException($"Check rotation: {error}");
            }
            Notification.ShowInformation($"Check rotation: {message}");
        }

        public bool Validate() {
            var i = new List<string>();
            if (!cameraMediator.GetInfo().Connected) {
                i.Add("Camera is not connected");
            }
            if (ItemUtility.RetrieveContextCoordinates(Parent) == null) {
                i.Add("No target found - place this instruction inside a target container");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(CheckRotation)}, ToleranceDegrees: {ToleranceDegrees}, TreatFlippedAsEqual: {TreatFlippedAsEqual}";
        }
    }
}
```

Note: `Attempts = 1` on purpose — a check should be a single deterministic measurement; use the instruction's own `Attempts` property (built into every NINA instruction) to retry the whole check if desired.

- [ ] **Step 2: Add icon + DataTemplates to the ResourceDictionary**

Add inside the root element of `InstructionTemplates.xaml`:

```xml
    <GeometryGroup x:Key="Speck_CheckRotation_SVG">
        <PathGeometry Figures="M 9,1 A 8,8 0 1 1 1.6,6 M 1.6,6 L 0,1.5 M 1.6,6 L 6,4.5" />
        <EllipseGeometry Center="9,9" RadiusX="2" RadiusY="2" />
    </GeometryGroup>

    <DataTemplate DataType="{x:Type local:CheckRotation}">
        <nina:SequenceBlockView>
            <nina:SequenceBlockView.SequenceItemContent>
                <StackPanel Orientation="Horizontal">
                    <TextBlock VerticalAlignment="Center" Text="Tolerance (°)" />
                    <TextBox MinWidth="40" Margin="5,0,0,0" TextAlignment="Right" Text="{Binding ToleranceDegrees}" />
                    <CheckBox VerticalAlignment="Center" Margin="15,0,0,0" IsChecked="{Binding TreatFlippedAsEqual}" />
                    <TextBlock VerticalAlignment="Center" Margin="5,0,0,0" Text="Treat 180° flip as equal" />
                    <TextBlock VerticalAlignment="Center" Margin="15,0,0,0" FontStyle="Italic" Text="{Binding LastMeasurement}" />
                </StackPanel>
            </nina:SequenceBlockView.SequenceItemContent>
        </nina:SequenceBlockView>
    </DataTemplate>

    <DataTemplate x:Key="SpeckSequenceHelpers.Instructions.CheckRotation_Mini">
        <mini:MiniSequenceItem />
    </DataTemplate>
```

- [ ] **Step 3: Build**

```bash
dotnet build src/SpeckSequenceHelpers -v q 2>&1 | tail -5
```

Expected: `0 Error(s)`.

- [ ] **Step 4: Run tests (regression)**

```bash
dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -3
```

Expected: `Passed! - Failed: 0, Passed: 28`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Add Check rotation instruction

Plate solves with the profile's solver settings and compares the measured
position angle to the parent target's, with optional 180-degree flip
equivalence. Info toast in tolerance; fail-and-continue beyond it.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Wait For Sky Median instruction

**Files:**
- Create: `src/SpeckSequenceHelpers/Instructions/WaitForSkyMedian.cs`
- Modify: `src/SpeckSequenceHelpers/Instructions/InstructionTemplates.xaml` (add icon + templates)

**Interfaces:**
- Consumes: `SkyMedianGate`, `GateDirection`, `GateAction`, `GateVerdict` (Task 4); ResourceDictionary (Task 1).
- Produces: exported instruction `SpeckSequenceHelpers.Instructions.WaitForSkyMedian`.

- [ ] **Step 1: Write the instruction class**

`src/SpeckSequenceHelpers/Instructions/WaitForSkyMedian.cs`:

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using SpeckSequenceHelpers.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace SpeckSequenceHelpers.Instructions {

    [ExportMetadata("Name", "Wait for sky median")]
    [ExportMetadata("Description", "Repeatedly takes throwaway exposures and waits until the image median enters the configured range. Brightening = dawn flats, Dimming = dusk flats. Fails when the window is overshot. Exposures are never saved.")]
    [ExportMetadata("Icon", "Speck_WaitForSkyMedian_SVG")]
    [ExportMetadata("Category", "Speck Sequence Helpers")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class WaitForSkyMedian : SequenceItem, IValidatable {
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;

        [ImportingConstructor]
        public WaitForSkyMedian(ICameraMediator cameraMediator, IImagingMediator imagingMediator) {
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
        }

        private WaitForSkyMedian(WaitForSkyMedian cloneMe) : this(cloneMe.cameraMediator, cloneMe.imagingMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new WaitForSkyMedian(this) {
                ExposureTime = ExposureTime,
                Gain = Gain,
                Offset = Offset,
                Binning = Binning,
                IntervalSeconds = IntervalSeconds,
                MinMedian = MinMedian,
                MaxMedian = MaxMedian,
                Direction = Direction
            };
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

        private double minMedian = 1000;

        [JsonProperty]
        public double MinMedian {
            get => minMedian;
            set {
                minMedian = value;
                RaisePropertyChanged();
            }
        }

        private double maxMedian = 30000;

        [JsonProperty]
        public double MaxMedian {
            get => maxMedian;
            set {
                maxMedian = value;
                RaisePropertyChanged();
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

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            SkyMedianGate gate;
            try {
                gate = new SkyMedianGate(MinMedian, MaxMedian, Direction);
            } catch (ArgumentException ex) {
                throw new SequenceEntityFailedException($"Wait for sky median: {ex.Message}");
            }

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
                var verdict = gate.Evaluate(stats.Median);
                Logger.Info($"Wait for sky median: attempt {attempt}, median {stats.Median:F0} ADU -> {verdict.Action} ({verdict.Reason})");

                switch (verdict.Action) {
                    case GateAction.Proceed:
                        progress?.Report(new ApplicationStatus() { Status = string.Empty });
                        return;

                    case GateAction.Fail:
                        Notification.ShowError($"Wait for sky median: {verdict.Reason}");
                        throw new SequenceEntityFailedException($"Wait for sky median: {verdict.Reason}");

                    default:
                        await CoreUtil.Wait(TimeSpan.FromSeconds(IntervalSeconds), true, token, progress, $"Attempt {attempt}: {verdict.Reason}");
                        break;
                }
            }
        }

        public override TimeSpan GetEstimatedDuration() {
            return TimeSpan.FromSeconds(ExposureTime + IntervalSeconds);
        }

        public bool Validate() {
            var i = new List<string>();
            if (!cameraMediator.GetInfo().Connected) {
                i.Add("Camera is not connected");
            }
            if (MinMedian >= MaxMedian) {
                i.Add("Min median must be less than max median");
            }
            if (ExposureTime <= 0) {
                i.Add("Exposure time must be greater than 0");
            }
            if (IntervalSeconds < 0) {
                i.Add("Interval must be 0 or greater");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(WaitForSkyMedian)}, ExposureTime: {ExposureTime}, Interval: {IntervalSeconds}, Min: {MinMedian}, Max: {MaxMedian}, Direction: {Direction}";
        }
    }
}
```

Notes: `FilterType = null` keeps whatever filter is currently selected (switch filters with the standard instruction beforehand). Captured snapshots are never saved — nothing calls the image-save mediator.

- [ ] **Step 2: Add icon + DataTemplates to the ResourceDictionary**

Add inside the root element of `InstructionTemplates.xaml`:

```xml
    <GeometryGroup x:Key="Speck_WaitForSkyMedian_SVG">
        <PathGeometry Figures="M 2,16 L 2,10 L 5,10 L 5,16 Z M 7,16 L 7,3 L 10,3 L 10,16 Z M 12,16 L 12,7 L 15,7 L 15,16 Z" />
        <PathGeometry Figures="M 0,18 L 17,18" />
    </GeometryGroup>

    <DataTemplate DataType="{x:Type local:WaitForSkyMedian}">
        <nina:SequenceBlockView>
            <nina:SequenceBlockView.SequenceItemContent>
                <StackPanel Orientation="Horizontal">
                    <TextBlock VerticalAlignment="Center" Text="Direction" />
                    <ComboBox Margin="5,0,0,0"
                              ItemsSource="{Binding Source={x:Static local:WaitForSkyMedian.DirectionChoices}}"
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
                    <TextBlock VerticalAlignment="Center" Margin="10,0,0,0" Text="Median min" />
                    <TextBox MinWidth="50" Margin="5,0,0,0" TextAlignment="Right" Text="{Binding MinMedian}" />
                    <TextBlock VerticalAlignment="Center" Margin="10,0,0,0" Text="max" />
                    <TextBox MinWidth="50" Margin="5,0,0,0" TextAlignment="Right" Text="{Binding MaxMedian}" />
                </StackPanel>
            </nina:SequenceBlockView.SequenceItemContent>
        </nina:SequenceBlockView>
    </DataTemplate>

    <DataTemplate x:Key="SpeckSequenceHelpers.Instructions.WaitForSkyMedian_Mini">
        <mini:MiniSequenceItem />
    </DataTemplate>
```

- [ ] **Step 3: Build**

```bash
dotnet build src/SpeckSequenceHelpers -v q 2>&1 | tail -5
```

Expected: `0 Error(s)`.

- [ ] **Step 4: Run tests (regression)**

```bash
dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -3
```

Expected: `Passed! - Failed: 0, Passed: 28`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Add Wait for sky median instruction

Loops throwaway snapshots until the median enters [min, max]; direction-
aware overshoot detection fails the instruction (dawn/dusk flats gating).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Documentation and CI

**Files:**
- Create: `README.md`
- Create: `CHANGELOG.md`
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: everything prior (documents it).
- Produces: user/developer docs; CI that Task 9's rig testing complements.

- [ ] **Step 1: Write README.md**

```markdown
# Speck Sequence Helpers

A [N.I.N.A.](https://nighttime-imaging.eu/) 3.x plugin with three advanced-sequencer instructions:

- **Dithered slew** — slews to the parent target's coordinates plus a small random offset, so
  rapid mosaic panel cycling gets its dither "for free" instead of paying for a separate guider
  dither after every slew. The offset radius is derived automatically from your guider dither
  settings (dither pixels × guider pixel scale), or set manually. Optional plate-solve centering
  on the offset coordinates.
- **Check rotation** — takes a plate-solve exposure (using your profile's plate-solve settings)
  and compares the measured position angle against the parent target's position angle. In
  tolerance: an info notification with the measurement. Out of tolerance: the instruction fails
  (red notification; the sequence continues). Moves neither mount nor rotator.
- **Wait for sky median** — repeatedly takes throwaway exposures (never saved) and waits until
  the image median enters a configured ADU range. Direction-aware for dawn (Brightening) or dusk
  (Dimming) flats; fails when the brightness window is overshot.

All three instructions appear under the **Speck Sequence Helpers** category in the advanced sequencer.

## Install

Copy `SpeckSequenceHelpers.dll` into `%localappdata%\NINA\Plugins\3.0.0\SpeckSequenceHelpers\`
and restart NINA. Building the project on a Windows machine does this automatically via a
post-build step.

## Building

Requires the .NET 8 SDK. Builds on Windows **and** Linux (WPF cross-targeting is enabled):

    dotnet build src/SpeckSequenceHelpers -c Release
    dotnet test tests/SpeckSequenceHelpers.Core.Tests

The plugin references NINA through the `NINA.Plugin` NuGet package — no NINA installation is
needed to build.

## Publishing to the official plugin repository

1. Bump `AssemblyVersion`/`AssemblyFileVersion` in `src/SpeckSequenceHelpers/Properties/AssemblyInfo.cs`
   and update `CHANGELOG.md`.
2. Build in Release on Windows and zip the plugin DLL.
3. Host the archive at a stable URL (e.g. a GitHub release on this repo).
4. Follow the manifest instructions at <https://bitbucket.org/Isbeorn/nina.plugin.manifests>
   (PowerShell 7 tooling — works on Linux via `pwsh`). Do **not** rebuild after creating the
   manifest; the checksum must match the released DLL.

## License

MPL-2.0, matching NINA and its plugin ecosystem.
```

- [ ] **Step 2: Write CHANGELOG.md**

```markdown
# Changelog

## 1.0.0.1 (unreleased)

- Initial release: Dithered slew, Check rotation, Wait for sky median.
```

- [ ] **Step 3: Write the CI workflow**

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  build-and-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Build
        run: dotnet build SpeckSequenceHelpers.sln -c Release
      - name: Format check
        run: dotnet format SpeckSequenceHelpers.sln --verify-no-changes
      - name: Test
        run: dotnet test tests/SpeckSequenceHelpers.Core.Tests -c Release --no-build
      - name: Upload plugin DLL
        uses: actions/upload-artifact@v4
        with:
          name: SpeckSequenceHelpers
          path: src/SpeckSequenceHelpers/bin/Release/net8.0-windows/SpeckSequenceHelpers.dll
```

(The workflow runs once the repo has a GitHub remote; creating the remote is the user's call.)

- [ ] **Step 4: Verify build + tests still green, then commit**

```bash
dotnet build src/SpeckSequenceHelpers -v q 2>&1 | tail -3 && dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q 2>&1 | tail -3
git add -A && git commit -m "Add README, changelog, and Windows CI workflow

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

Expected: `0 Error(s)` and `Passed!`.

---

### Task 9: Windows rig verification checklist

**Files:**
- Create: `docs/rig-testing.md`

**Interfaces:**
- Consumes: the built plugin DLL.
- Produces: the manual verification protocol the user runs on the imaging rig; its completion is the project's definition of done.

- [ ] **Step 1: Write docs/rig-testing.md**

```markdown
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
```

- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "Add rig verification checklist

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 10: Code hygiene enforcement (added 2026-07-24 at user request; execute after Task 7, before Task 8)

**Files:**
- Create: `.editorconfig`
- Create: `Directory.Build.props`
- Modify: whatever `dotnet format` and the analyzers flag (document each fix)

**Interfaces:**
- Consumes: the full codebase from Tasks 1–7.
- Produces: an enforceable style/lint baseline — `dotnet format SpeckSequenceHelpers.sln --verify-no-changes` and `dotnet build` (warnings-as-errors) both pass; Task 8's CI includes the format gate; the controller runs the format check before every subsequent commit.

- [ ] **Step 1: Create `.editorconfig`**

```ini
root = true

[*]
charset = utf-8
insert_final_newline = true
trim_trailing_whitespace = true
indent_style = space
indent_size = 4

[*.{csproj,props,targets,json,yml,yaml}]
indent_size = 2

[*.cs]
# NINA-style: braces on the same line
csharp_new_line_before_open_brace = none
csharp_new_line_before_else = false
csharp_new_line_before_catch = false
csharp_new_line_before_finally = false
csharp_new_line_before_members_in_object_initializers = false
csharp_new_line_before_members_in_anonymous_types = false

# usings sorted alphabetically, System NOT first (matches the NINA plugin convention used here)
dotnet_sort_system_directives_first = false

csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = true:suggestion
```

- [ ] **Step 2: Create `Directory.Build.props`** (root — applies to both projects)

```xml
<Project>
  <PropertyGroup>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsNotAsErrors>NU1701</WarningsNotAsErrors>
  </PropertyGroup>
</Project>
```

(NU1701 is the known NINA legacy-transitive-package restore warning — exempted, everything else fails the build.)

- [ ] **Step 3: Format sweep**

Run: `dotnet format SpeckSequenceHelpers.sln 2>&1 | tail -5` then `dotnet format SpeckSequenceHelpers.sln --verify-no-changes && echo FORMAT-CLEAN`
Expected: `FORMAT-CLEAN`. Review what the first run changed — it should be whitespace/using-order only.

- [ ] **Step 4: Analyzer sweep**

Run: `dotnet build SpeckSequenceHelpers.sln -v q 2>&1 | tail -15`
Expected: `0 Error(s)` after fixing any analyzer findings. Fix findings properly (not by suppression); a rule may be downgraded in `.editorconfig` only with a written justification in the report. Then `dotnet test tests/SpeckSequenceHelpers.Core.Tests -v q` → 28/28.

- [ ] **Step 5: Controller commits after dual review** (message: `Add .editorconfig, analyzers, and format enforcement`)

## Plan self-review notes

- Spec coverage: dithered slew (auto radius from guider settings + manual override + center toggle + parent-target coords) → Task 5; rotation check (profile solve settings, parent PA, flip toggle, info toast, fail-and-continue) → Task 6; median wait (exposure/gain/offset/binning, interval, min/max, direction, first-reading overshoot, no timeout, unsaved exposures) → Task 7; Core logic + Linux tests → Tasks 2–4; MPL-2.0/metadata/publishing posture → Tasks 1 and 8; rig verification → Task 9. Sandbox setup (`.sandbox/setup.sh`, .NET SDK) was already done during planning and is committed.
- Instruction code was written against verified NINA 3.3 source signatures; the pinned package is 3.2.0.9001, so minor member differences may surface at build time — the build steps say how to resolve them (check the source clone, keep the architecture).
- Type consistency checked: `OffsetVector.RaArcsec/DecArcsec/RadiusArcsec`, `GateDirection/GateAction/GateVerdict`, `AngleMath.RotationDelta` are used with identical names in Tasks 5–7 as defined in Tasks 2–4.
