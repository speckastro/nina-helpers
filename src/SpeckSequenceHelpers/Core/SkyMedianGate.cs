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
            if (!double.IsFinite(minMedian) || !double.IsFinite(maxMedian)) {
                throw new ArgumentException($"Median thresholds must be finite (min: {minMedian}, max: {maxMedian})");
            }
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
