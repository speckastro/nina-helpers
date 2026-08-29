using SpeckSequenceHelpers.Core;
using Xunit;

namespace SpeckSequenceHelpers.Core.Tests {

    public class PierSideChangeDetectorTests {

        [Fact]
        public void FirstKnownSide_SetsBaseline_WithoutFiring() {
            var d = new PierSideChangeDetector();
            Assert.False(d.Observe(ObservedPierSide.East));
            Assert.Equal(ObservedPierSide.East, d.LastSeen);
        }

        [Fact]
        public void Unknown_BeforeBaseline_IsIgnored() {
            var d = new PierSideChangeDetector();
            Assert.False(d.Observe(ObservedPierSide.Unknown));
            Assert.Null(d.LastSeen);
        }

        [Fact]
        public void Unknown_AfterBaseline_IsIgnored() {
            var d = new PierSideChangeDetector();
            d.Observe(ObservedPierSide.West);
            Assert.False(d.Observe(ObservedPierSide.Unknown));
            Assert.Equal(ObservedPierSide.West, d.LastSeen);
        }

        [Fact]
        public void SameSide_DoesNotFire() {
            var d = new PierSideChangeDetector();
            d.Observe(ObservedPierSide.East);
            Assert.False(d.Observe(ObservedPierSide.East));
        }

        [Fact]
        public void Change_FiresOnce_AndUpdatesBaseline() {
            var d = new PierSideChangeDetector();
            d.Observe(ObservedPierSide.East);
            Assert.True(d.Observe(ObservedPierSide.West));
            Assert.Equal(ObservedPierSide.West, d.LastSeen);
            Assert.False(d.Observe(ObservedPierSide.West));
        }

        [Fact]
        public void FlipBack_FiresAgain() {
            var d = new PierSideChangeDetector();
            d.Observe(ObservedPierSide.East);
            d.Observe(ObservedPierSide.West);
            Assert.True(d.Observe(ObservedPierSide.East));
        }

        [Fact]
        public void Reset_ClearsBaseline() {
            var d = new PierSideChangeDetector();
            d.Observe(ObservedPierSide.East);
            d.Reset();
            Assert.Null(d.LastSeen);
            Assert.False(d.Observe(ObservedPierSide.West));
        }
    }
}
