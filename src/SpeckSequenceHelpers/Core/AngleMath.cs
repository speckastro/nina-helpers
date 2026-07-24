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
