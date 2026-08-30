using System.Reflection;
using System.Runtime.InteropServices;

[assembly: Guid("c66fa242-868a-47c0-ad62-54d3439af382")]

[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

[assembly: AssemblyTitle("Speck Sequence Helpers")]
[assembly: AssemblyDescription("Advanced sequencer helpers: dithered slew-and-center for mosaic panel cycling, plate-solved rotation checks, histogram-mean gating for flats, and an autofocus trigger for mounts that flip during a slew")]

[assembly: AssemblyCompany("Speck Astro")]
[assembly: AssemblyProduct("Speck Sequence Helpers")]
[assembly: AssemblyCopyright("Copyright © 2026 Speck Astro")]

[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]

[assembly: AssemblyMetadata("License", "MPL-2.0")]
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
[assembly: AssemblyMetadata("Repository", "https://github.com/speckastro/nina-helpers")]

[assembly: AssemblyMetadata("Tags", "Sequencer,Mosaic,Dither,Rotation,Flats,Autofocus,Meridian Flip")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/speckastro/nina-helpers/blob/main/CHANGELOG.md")]
[assembly: AssemblyMetadata("FeaturedImageURL", "")]
[assembly: AssemblyMetadata("ScreenshotURL", "")]
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
[assembly: AssemblyMetadata("LongDescription", @"Advanced sequencer instructions and triggers:

* Dithered slew and center - a drop-in replacement for the stock slew and center that aims at a point nudged slightly off the target by a random amount each run, so rapid mosaic panel cycling needs no separate dither. The offset radius comes from your guider dither settings automatically, or from a manual radius.
* Check rotation - plate solve and compare the measured position angle against the target's position angle. Reports the measurement unobtrusively; fails the instruction if a configured tolerance is exceeded.
* Wait for sky brightness - repeatedly take throwaway exposures until the histogram mean reaches your target, within tolerance. Target and tolerance are entered as percentages just like the flat wizard, and the equivalent ADU window is shown. Designed for dawn/dusk sky flats; fails if the brightness window is overshot.
* Autofocus after pier side change - a trigger that runs an autofocus before the next light exposure once the mount reports a change of pier side, for mounts that flip during a slew before the meridian flip trigger runs. Warns when NINA's own ""Autofocus after flip"" is also on.")]

[assembly: ComVisible(false)]
