using System.ComponentModel.Composition;
using System.Windows;

namespace SpeckSequenceHelpers.Instructions {

    [Export(typeof(ResourceDictionary))]
    public partial class InstructionTemplates : ResourceDictionary {

        public InstructionTemplates() {
            InitializeComponent();
        }
    }
}
