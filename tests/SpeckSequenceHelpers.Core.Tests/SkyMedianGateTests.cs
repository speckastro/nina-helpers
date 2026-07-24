using System;
using SpeckSequenceHelpers.Core;
using Xunit;

namespace SpeckSequenceHelpers.Core.Tests {

    public class SkyMedianGateTests {

        [Theory]
        [InlineData(GateDirection.Brightening, 1500, GateAction.Proceed)]  // in range
        [InlineData(GateDirection.Brightening, 1000, GateAction.Proceed)]  // == min boundary
        [InlineData(GateDirection.Brightening, 5000, GateAction.Proceed)]  // == max boundary
        [InlineData(GateDirection.Brightening, 999, GateAction.Wait)]      // below min: dawn, keep waiting
        [InlineData(GateDirection.Brightening, 5001, GateAction.Fail)]     // above max: dawn overshot
        [InlineData(GateDirection.Dimming, 1500, GateAction.Proceed)]      // in range
        [InlineData(GateDirection.Dimming, 5001, GateAction.Wait)]         // above max: dusk, keep waiting
        [InlineData(GateDirection.Dimming, 999, GateAction.Fail)]          // below min: dusk overshot
        public void Evaluate_AppliesDirectionalWindow(GateDirection direction, double median, GateAction expected) {
            var gate = new SkyMedianGate(1000, 5000, direction);
            Assert.Equal(expected, gate.Evaluate(median).Action);
        }

        [Fact]
        public void Evaluate_FirstReadingCanFail() {
            // dawn gate, sky already too bright on the very first exposure
            var gate = new SkyMedianGate(1000, 5000, GateDirection.Brightening);
            Assert.Equal(GateAction.Fail, gate.Evaluate(60000).Action);
        }

        [Fact]
        public void Evaluate_ReasonMentionsMedian() {
            var gate = new SkyMedianGate(1000, 5000, GateDirection.Brightening);
            Assert.Contains("812", gate.Evaluate(812).Reason);
        }

        [Fact]
        public void Constructor_MinNotBelowMax_Throws() {
            Assert.Throws<ArgumentException>(() => new SkyMedianGate(5000, 5000, GateDirection.Brightening));
            Assert.Throws<ArgumentException>(() => new SkyMedianGate(6000, 5000, GateDirection.Brightening));
        }
    }
}
