using SpeckSequenceHelpers.Core;
using Xunit;

namespace SpeckSequenceHelpers.Core.Tests {

    public class AngleMathTests {

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(10, 350, 20)]
        [InlineData(350, 10, 20)]
        [InlineData(90, 270, 180)]
        [InlineData(359.5, 0.5, 1)]
        [InlineData(725, 5, 0)]
        public void SmallestDifference_HandlesWrap(double a, double b, double expected) {
            Assert.Equal(expected, AngleMath.SmallestDifference(a, b), 9);
        }

        [Theory]
        [InlineData(90, 270, false, 180)]
        [InlineData(90, 270, true, 0)]
        [InlineData(0, 100, false, 100)]
        [InlineData(0, 100, true, 80)]
        [InlineData(123.4, 123.0, true, 0.4)]
        [InlineData(303.4, 123.0, true, 0.4)]
        public void RotationDelta_HonorsFlipEquivalence(double measured, double target, bool flipEqual, double expected) {
            Assert.Equal(expected, AngleMath.RotationDelta(measured, target, flipEqual), 9);
        }
    }
}
