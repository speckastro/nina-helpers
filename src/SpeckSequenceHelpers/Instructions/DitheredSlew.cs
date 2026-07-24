using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
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

        private bool centerAfterSlew;

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
            if (!double.IsFinite(radiusArcsec) || radiusArcsec <= 0) {
                throw new SequenceEntityFailedException("Dithered slew: dither radius unavailable - guider reports no pixel scale and no manual radius is set");
            }

            var offset = DitherOffsetCalculator.Generate(radiusArcsec, Random.Shared);
            var target = context.Coordinates.Shift(offset.RaArcsec / 3600d, offset.DecArcsec / 3600d, 0);
            Logger.Info($"Dithered slew: offset {offset.RadiusArcsec:F1}\" of max {radiusArcsec:F1}\" (dRA {offset.RaArcsec:F1}\", dDec {offset.DecArcsec:F1}\"), slewing to {target}");
            progress?.Report(new ApplicationStatus() { Status = $"Dithered slew: offset {offset.RadiusArcsec:F1}\" (dRA {offset.RaArcsec:F1}\", dDec {offset.DecArcsec:F1}\")" });

            var stoppedGuiding = await guiderMediator.StopGuiding(token);

            var success = true;
            string failure = null;
            try {
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
            } catch {
                // A primary failure is propagating - restart guiding best-effort without masking it.
                if (stoppedGuiding && !token.IsCancellationRequested) {
                    try {
                        await guiderMediator.StartGuiding(false, progress, token);
                    } catch (Exception ex) {
                        Logger.Error("Dithered slew: failed to restart guiding after slew failure", ex);
                    }
                }
                throw;
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
                return double.IsFinite(ManualRadiusArcsec) && ManualRadiusArcsec > 0 ? ManualRadiusArcsec : 0;
            }
            var guiderInfo = guiderMediator.GetInfo();
            if (guiderInfo?.Connected != true || guiderInfo.PixelScale <= 0) {
                return 0;
            }
            var radius = profileService.ActiveProfile.GuiderSettings.DitherPixels * guiderInfo.PixelScale;
            return double.IsFinite(radius) ? radius : 0;
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
                if (!double.IsFinite(ManualRadiusArcsec) || ManualRadiusArcsec <= 0) {
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
