using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
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
