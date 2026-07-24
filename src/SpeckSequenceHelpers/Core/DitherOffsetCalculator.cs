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
            ArgumentOutOfRangeException.ThrowIfNegative(maxRadiusArcsec);
            ArgumentNullException.ThrowIfNull(random);

            var radius = maxRadiusArcsec * Math.Sqrt(random.NextDouble());
            var theta = 2d * Math.PI * random.NextDouble();
            return new OffsetVector(radius * Math.Cos(theta), radius * Math.Sin(theta));
        }
    }
}
