namespace SpeckSequenceHelpers.Core {

    /// <summary>Pier side as reported by the mount, without NINA types.</summary>
    public enum ObservedPierSide {
        Unknown,
        East,
        West
    }

    /// <summary>
    /// Tracks the last known pier side and reports when it changes. Unknown readings are
    /// ignored. The first known reading becomes the baseline without counting as a change.
    /// </summary>
    public class PierSideChangeDetector {

        public ObservedPierSide? LastSeen { get; private set; }

        /// <summary>Records a reading. Returns true when it differs from the last known side.</summary>
        public bool Observe(ObservedPierSide side) {
            if (side == ObservedPierSide.Unknown) {
                return false;
            }
            if (LastSeen == null) {
                LastSeen = side;
                return false;
            }
            if (LastSeen == side) {
                return false;
            }
            LastSeen = side;
            return true;
        }

        /// <summary>Forgets the last known side; the next known reading becomes a new baseline.</summary>
        public void Reset() {
            LastSeen = null;
        }
    }
}
