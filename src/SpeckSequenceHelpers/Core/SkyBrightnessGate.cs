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
