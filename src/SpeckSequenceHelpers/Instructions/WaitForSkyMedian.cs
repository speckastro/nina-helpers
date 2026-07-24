using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
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
