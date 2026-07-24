using System;
using SpeckSequenceHelpers.Core;
using Xunit;

namespace SpeckSequenceHelpers.Core.Tests {

    public class DitherOffsetCalculatorTests {

        [Fact]
        public void Generate_StaysWithinMaxRadius() {
            var random = new Random(42);
            for (var i = 0; i < 10_000; i++) {
                var offset = DitherOffsetCalculator.Generate(30, random);
                Assert.True(offset.RadiusArcsec <= 30.0 + 1e-9, $"radius {offset.RadiusArcsec} exceeded max");
            }
        }

        [Fact]
        public void Generate_ProducesVaryingOffsets() {
            var random = new Random(42);
            var a = DitherOffsetCalculator.Generate(30, random);
            var b = DitherOffsetCalculator.Generate(30, random);
            Assert.False(a.RaArcsec == b.RaArcsec && a.DecArcsec == b.DecArcsec);
        }

        [Fact]
        public void Generate_CoversAllQuadrants() {
            var random = new Random(1);
            int q1 = 0, q2 = 0, q3 = 0, q4 = 0;
            for (var i = 0; i < 1000; i++) {
                var o = DitherOffsetCalculator.Generate(10, random);
                if (o.RaArcsec > 0 && o.DecArcsec > 0) { q1++; } else if (o.RaArcsec <= 0 && o.DecArcsec > 0) { q2++; } else if (o.RaArcsec <= 0 && o.DecArcsec <= 0) { q3++; } else { q4++; }
            }
            Assert.True(q1 > 100 && q2 > 100 && q3 > 100 && q4 > 100,
                $"quadrant counts: Q1 {q1}, Q2 {q2}, Q3 {q3}, Q4 {q4}");
        }

        [Fact]
        public void Generate_ZeroRadius_ReturnsZeroOffset() {
            var o = DitherOffsetCalculator.Generate(0, new Random(7));
            Assert.Equal(0, o.RadiusArcsec, 12);
        }

        [Fact]
        public void Generate_NegativeRadius_Throws() {
            Assert.Throws<ArgumentOutOfRangeException>(() => DitherOffsetCalculator.Generate(-1, new Random(7)));
        }
    }
}
