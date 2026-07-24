using NINA.Plugin;
using NINA.Plugin.Interfaces;
using System.ComponentModel.Composition;

namespace SpeckSequenceHelpers {

    /// <summary>
    /// Plugin manifest. All metadata is read from Properties/AssemblyInfo.cs by PluginBase.
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class SpeckSequenceHelpersPlugin : PluginBase {

        [ImportingConstructor]
        public SpeckSequenceHelpersPlugin() {
        }
    }
}
