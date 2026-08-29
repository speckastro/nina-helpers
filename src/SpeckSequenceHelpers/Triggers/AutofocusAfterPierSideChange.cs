using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Autofocus;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Utility;
using NINA.Sequencer.Validations;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using SpeckSequenceHelpers.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpeckSequenceHelpers.Triggers {

    [ExportMetadata("Name", "Autofocus after pier side change")]
    [ExportMetadata("Description", "Runs an autofocus before the next light exposure after the mount reports a change of pier side, for mounts that flip during a slew before the meridian flip trigger runs.")]
    [ExportMetadata("Icon", "Speck_AutofocusAfterPierSideChange_SVG")]
    [ExportMetadata("Category", "Speck Sequence Helpers")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class AutofocusAfterPierSideChange : SequenceTrigger, IValidatable {
        private readonly IProfileService profileService;
        private readonly IImageHistoryVM history;
        private readonly ICameraMediator cameraMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IAutoFocusVMFactory autoFocusVMFactory;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly ISafetyMonitorMediator safetyMonitorMediator;

        private readonly PierSideChangeDetector detector = new PierSideChangeDetector();
        private bool pending;

        [ImportingConstructor]
        public AutofocusAfterPierSideChange(IProfileService profileService,
                                            IImageHistoryVM history,
                                            ICameraMediator cameraMediator,
                                            IFilterWheelMediator filterWheelMediator,
                                            IFocuserMediator focuserMediator,
                                            IAutoFocusVMFactory autoFocusVMFactory,
                                            ITelescopeMediator telescopeMediator,
                                            ISafetyMonitorMediator safetyMonitorMediator) : base() {
            this.profileService = profileService;
            this.history = history;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.focuserMediator = focuserMediator;
            this.autoFocusVMFactory = autoFocusVMFactory;
            this.telescopeMediator = telescopeMediator;
            this.safetyMonitorMediator = safetyMonitorMediator;
            TriggerRunner.Add(new RunAutofocus(profileService, history, cameraMediator, filterWheelMediator, focuserMediator, autoFocusVMFactory));
        }

        private AutofocusAfterPierSideChange(AutofocusAfterPierSideChange cloneMe) : this(cloneMe.profileService,
                                                                                          cloneMe.history,
                                                                                          cloneMe.cameraMediator,
                                                                                          cloneMe.filterWheelMediator,
                                                                                          cloneMe.focuserMediator,
                                                                                          cloneMe.autoFocusVMFactory,
                                                                                          cloneMe.telescopeMediator,
                                                                                          cloneMe.safetyMonitorMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            var clone = new AutofocusAfterPierSideChange(this);
            clone.TriggerRunner = (SequentialContainer)TriggerRunner.Clone();
            return clone;
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = ImmutableList.CreateRange(value);
                RaisePropertyChanged();
            }
        }

        private string lastSeenPierSide = "—";

        /// <summary>Last known pier side for the sequencer row: "East", "West", or "—".</summary>
        public string LastSeenPierSide {
            get => lastSeenPierSide;
            private set {
                if (lastSeenPierSide == value) { return; }
                lastSeenPierSide = value;
                RaisePropertyChanged();
            }
        }

        private string warning = string.Empty;

        /// <summary>Non-blocking advisory shown on the row; empty when nothing to say.</summary>
        public string Warning {
            get => warning;
            private set {
                if (warning == value) { return; }
                warning = value;
                RaisePropertyChanged();
            }
        }

        public override void Initialize() {
            detector.Reset();
            pending = false;
            Sample();
        }

        public override void SequenceBlockStarted() {
            RefreshWarning();
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            Sample();
            if (!pending) { return false; }
            if (nextItem == null) { return false; }
            if (!(nextItem is IExposureItem exposureItem)) { return false; }
            if (exposureItem.ImageType != "LIGHT") { return false; }
            if (safetyMonitorMediator.GetInfo() is { Connected: true, IsSafe: false }) {
                Logger.Info("Autofocus after pier side change - pier side changed but safety monitor reports unsafe; deferring");
                return false;
            }

            var autofocusDuration = TriggerRunner.GetItemsSnapshot().First().GetEstimatedDuration();
            if (ItemUtility.IsTooCloseToMeridianFlip(Parent, autofocusDuration + nextItem.GetEstimatedDuration())) {
                Logger.Warning("Autofocus after pier side change - autofocus should run, however the meridian flip is too close; deferring");
                return false;
            }

            pending = false;
            return true;
        }

        public override bool ShouldTriggerAfter(ISequenceItem previousItem, ISequenceItem nextItem) {
            // ShouldTrigger already samples before every item; sampling after each item as
            // well halves the gap between readings, so a flip and flip back across two
            // items is less likely to slip between samples.
            Sample();
            return false;
        }

        public override async Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            await TriggerRunner.Run(progress, token);
        }

        private void Sample() {
            var info = telescopeMediator.GetInfo();
            var side = info != null && info.Connected ? Map(info.SideOfPier) : ObservedPierSide.Unknown;
            if (detector.Observe(side)) {
                Logger.Info($"Autofocus after pier side change - pier side changed to {side}; autofocus pending before next light");
                pending = true;
            }
            LastSeenPierSide = detector.LastSeen switch {
                ObservedPierSide.East => "East",
                ObservedPierSide.West => "West",
                _ => "—"
            };
        }

        private static ObservedPierSide Map(PierSide side) {
            switch (side) {
                case PierSide.pierEast: return ObservedPierSide.East;
                case PierSide.pierWest: return ObservedPierSide.West;
                default: return ObservedPierSide.Unknown;
            }
        }

        private void RefreshWarning() {
            Warning = profileService.ActiveProfile.MeridianFlipSettings.AutoFocusAfterFlip
                ? "NINA's 'Autofocus after flip' is on; a meridian flip may autofocus twice."
                : string.Empty;
        }

        public override void AfterParentChanged() {
            base.AfterParentChanged();
            Validate();
        }

        public bool Validate() {
            var i = new List<string>();
            if (!cameraMediator.GetInfo().Connected) {
                i.Add("Camera is not connected");
            }
            if (!focuserMediator.GetInfo().Connected) {
                i.Add("Focuser is not connected");
            }
            if (!telescopeMediator.GetInfo().Connected) {
                i.Add("Mount is not connected");
            }
            RefreshWarning();
            Issues = i;
            return i.Count == 0;
        }

        public override string ToString() {
            return $"Trigger: {nameof(AutofocusAfterPierSideChange)}";
        }
    }
}
