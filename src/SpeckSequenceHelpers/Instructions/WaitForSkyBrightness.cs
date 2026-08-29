using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment.MyCamera;
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
            CameraInfo = cameraMediator?.GetInfo();
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

        private CameraInfo cameraInfo;

        /// <summary>Latest camera state, refreshed on validation; the row binds to it for defaults and capabilities.</summary>
        public CameraInfo CameraInfo {
            get => cameraInfo;
            private set {
                cameraInfo = value;
                RaisePropertyChanged();
            }
        }

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
        /// Shallowest bit depth any camera plausibly reports. Validating the window here is the
        /// strictest case: a shallower depth scales the target ADU down, and only a smaller target
        /// can lose the tolerance to floating-point rounding, so a window that holds at 8-bit holds
        /// at every deeper one.
        /// </summary>
        private const int ShallowestBitDepth = 8;

        /// <summary>
        /// The accepted ADU window for a given bit depth, from NINA's own flat-wizard math, so the
        /// gate, the validation check, and the advisory label can never disagree.
        /// </summary>
        private (double LowerAdu, double UpperAdu) GetAduWindow(int bitDepth) {
            var targetFraction = TargetPercent / 100d;
            var toleranceFraction = TolerancePercent / 100d;
            return (
                HistogramMath.GetLowerToleranceBoundInAdu(targetFraction, bitDepth, toleranceFraction),
                HistogramMath.GetUpperToleranceBoundInAdu(targetFraction, bitDepth, toleranceFraction));
        }

        /// <summary>
        /// Builds the ADU window for a given bit depth using NINA's own flat-wizard math, so the
        /// accepted range matches what the flat wizard would accept for the same percentages.
        /// </summary>
        private SkyBrightnessGate CreateGate(int bitDepth) {
            var (lowerAdu, upperAdu) = GetAduWindow(bitDepth);
            try {
                return new SkyBrightnessGate(lowerAdu, upperAdu, Direction);
            } catch (ArgumentException ex) {
                throw new SequenceEntityFailedException($"Wait for sky brightness: {ex.Message}");
            }
        }

        private void UpdateAduSummary() {
            var info = CameraInfo;
            if (info?.Connected != true || info.BitDepth <= 0) {
                AduSummary = "connect camera for ADU values";
                return;
            }
            if (!double.IsFinite(TargetPercent) || !double.IsFinite(TolerancePercent)) {
                AduSummary = string.Empty;
                return;
            }
            var targetAdu = HistogramMath.HistogramMeanAndCameraBitDepthToAdu(TargetPercent / 100d, info.BitDepth);
            var (lowerAdu, upperAdu) = GetAduWindow(info.BitDepth);
            AduSummary = $"≈ {targetAdu:N0} ADU, accepting {lowerAdu:N0} - {upperAdu:N0} ({info.BitDepth}-bit)";
        }

        public override TimeSpan GetEstimatedDuration() {
            return TimeSpan.FromSeconds(ExposureTime + IntervalSeconds);
        }

        public bool Validate() {
            var i = new List<string>();
            CameraInfo = cameraMediator.GetInfo();
            var info = CameraInfo;
            if (info?.Connected != true) {
                i.Add("Camera is not connected");
            } else {
                if (info.CanSetGain && Gain > -1 && (Gain < info.GainMin || Gain > info.GainMax)) {
                    i.Add($"Gain must be between {info.GainMin} and {info.GainMax}");
                }
                if (info.CanSetOffset && Offset > -1 && (Offset < info.OffsetMin || Offset > info.OffsetMax)) {
                    i.Add($"Offset must be between {info.OffsetMin} and {info.OffsetMax}");
                }
            }

            var targetValid = double.IsFinite(TargetPercent) && TargetPercent > 0 && TargetPercent <= 100;
            if (!targetValid) {
                i.Add("Target must be greater than 0 and at most 100 percent");
            }
            var toleranceValid = double.IsFinite(TolerancePercent) && TolerancePercent > 0 && TolerancePercent <= 100;
            if (!toleranceValid) {
                i.Add("Tolerance must be greater than 0 and at most 100 percent");
            }
            if (targetValid && toleranceValid) {
                // A tolerance small enough to vanish in double precision would collapse the window
                // and fail inside the gate at runtime - catch it here instead of after an exposure.
                // Checked at the shallowest plausible depth rather than the camera's, so the verdict
                // holds for whatever depth the captured image actually turns out to be.
                var (lowerAdu, upperAdu) = GetAduWindow(ShallowestBitDepth);
                if (!double.IsFinite(lowerAdu) || !double.IsFinite(upperAdu) || lowerAdu >= upperAdu) {
                    i.Add("Target and tolerance produce an empty brightness window - increase the tolerance");
                }
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
