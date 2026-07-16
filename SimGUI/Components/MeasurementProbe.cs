using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SimGUI
{
    class Probe : Component
    {
        public Brush ProbeColour = Brushes.Transparent;

        public Probe(Circuit parent, Point origin)
            : base(parent, origin)
        {
            ComponentType = "Probe";
            LoadFootprintFromXml("probe");
            ID = parent.GetNextComponentName("P");
        }

        public override string GenerateNetlist()
        {
            return "";
        }

        protected override bool SetupPropertiesDialog(ComponentProperties dialog)
        {
            dialog.AddColorSelection(ProbeColour);
            return true;
        }

        protected override void AfterPropertiesDialog(ComponentProperties dialog)
        {
            if (dialog.ColorSelectionBox != null && dialog.ColorSelectionBox.SelectedItem != null)
            {
                var selectedPair = (KeyValuePair<string, Brush>)dialog.ColorSelectionBox.SelectedItem;
                SetProbeColour(selectedPair.Value);

                if (ParentCircuit.ParentWindow.CurrentGraph != null)
                {
                    ParentCircuit.ParentWindow.CurrentGraph.UpdateTraceMapping(ID, -1, -1, ProbeColour);
                }
            }
        }

        public void SetProbeColour(Brush b)
        {
            ProbeColour = b;
            foreach (Path p in Children.OfType<Path>())
            {
                if (p.Name == "ProbeBody")
                {
                    p.Fill = b;
                }
            }
        }
    }
}
