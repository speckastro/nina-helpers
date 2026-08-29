using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Platesolving;
using NINA.Sequencer.Utility;
using SpeckSequenceHelpers.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace SpeckSequenceHelpers.Instructions {

    [ExportMetadata("Name", "Dithered slew and center")]
    [ExportMetadata("Description", "A drop-in replacement for the stock slew and center that aims at a point nudged slightly off the target by a random amount each run, so cycling mosaic panels dithers without a separate guider dither. Offset radius comes from the guider dither settings, or a manual radius.")]
    [ExportMetadata("Icon", "Speck_DitheredSlewAndCenter_SVG")]
    [ExportMetadata("Category", "Speck Sequence Helpers")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class DitheredSlewAndCenter : Center {
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
        /// Checks the dither preconditions before handing off to <see cref="Center.Execute"/>.
        /// They belong here rather than in <see cref="DoCenter"/> because Execute stops guiding
        /// before calling DoCenter and restarts it only on the non-throwing path, so a
        /// precondition raised later would leave guiding stopped.
        /// </summary>
        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (telescopeMediator.GetInfo().AtPark) {
                Notification.ShowError("Dithered slew and center: telescope is parked");
                throw new SequenceEntityFailedException("Dithered slew and center: telescope is parked");
            }
            var radiusArcsec = ResolveMaxRadiusArcsec();
            if (!double.IsFinite(radiusArcsec) || radiusArcsec <= 0) {
                throw new SequenceEntityFailedException("Dithered slew and center: dither radius unavailable - check the guider pixel scale and profile dither amount, or enable the manual radius");
            }
            if (ResolveBaseCoordinates() == null) {
                throw new SequenceEntityFailedException("Dithered slew and center: no target coordinates found - place this instruction inside a target container");
            }
            await base.Execute(progress, token);
        }

        /// <summary>
        /// Centres on the parent target's coordinates displaced by a fresh random
        /// offset. This deliberately does not delegate to <see cref="Center.DoCenter"/>: the base
        /// reads and rewrites the bindable <see cref="Center.Coordinates"/>, so aiming it at the
        /// dithered position would mean mutating serialized state for the whole centring run and
        /// risking a save that persists the offset. The dithered target stays local instead.
        /// Preconditions are validated in <see cref="Execute"/> so a failure cannot leave guiding stopped.
        /// </summary>
        protected override async Task<PlateSolveResult> DoCenter(IProgress<ApplicationStatus> progress, CancellationToken token) {
            // Preconditions are checked in Execute, before guiding is stopped.
            var radiusArcsec = ResolveMaxRadiusArcsec();
            var baseCoordinates = ResolveBaseCoordinates();

            var offset = DitherOffsetCalculator.Generate(radiusArcsec, Random.Shared);
            var dithered = baseCoordinates.Shift(offset.RaArcsec / 3600d, offset.DecArcsec / 3600d, 0);
            Logger.Info($"Dithered slew and center: offset {offset.RadiusArcsec:F1}\" of max {radiusArcsec:F1}\" (dRA {offset.RaArcsec:F1}\", dDec {offset.DecArcsec:F1}\"), centering on {dithered}");

            progress?.Report(new ApplicationStatus() { Status = "Slewing to dithered target" });
            if (!await telescopeMediator.SlewToCoordinatesAsync(dithered, token)) {
                throw new SequenceEntityFailedException("Dithered slew and center: slew failed");
            }

            var domeInfo = domeMediator.GetInfo();
            if (domeInfo.Connected && domeInfo.CanSetAzimuth && !domeFollower.IsFollowing) {
                progress?.Report(new ApplicationStatus() { Status = "Synchronizing dome" });
                if (!await domeFollower.TriggerTelescopeSync()) {
                    Notification.ShowWarning("Dithered slew and center: dome sync failed");
                    Logger.Warning("Dithered slew and center: dome sync failed");
                }
            }
            progress?.Report(new ApplicationStatus() { Status = string.Empty });

            var solveSettings = profileService.ActiveProfile.PlateSolveSettings;
            var plateSolver = plateSolverFactory.GetPlateSolver(solveSettings);
            var blindSolver = plateSolverFactory.GetBlindSolver(solveSettings);
            var solver = plateSolverFactory.GetCenteringSolver(plateSolver, blindSolver, imagingMediator, telescopeMediator, filterWheelMediator, domeMediator, domeFollower);
            var parameter = new CenterSolveParameter() {
                Attempts = solveSettings.NumberOfAttempts,
                Binning = solveSettings.Binning,
                Coordinates = dithered,
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
            return await solver.Center(seq, parameter, PlateSolveStatusVM.Progress, progress, token);
        }

        /// <summary>The un-dithered target: the parent container's coordinates.</summary>
        private Coordinates ResolveBaseCoordinates() {
            return ItemUtility.RetrieveContextCoordinates(Parent)?.Coordinates;
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
            if (ItemUtility.RetrieveContextCoordinates(Parent) == null) {
                i.Add("No target coordinates found - place this instruction inside a target container");
            }
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
