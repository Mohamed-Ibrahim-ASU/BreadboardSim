using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SimGUI
{
    class FunctionGenerator : Component
    {
        public Quantity Amplitude = new Quantity("A", "Amplitude", "V") { Val = 2.0 };
        public Quantity Offset = new Quantity("Voff", "DC Magnitude", "V") { Val = 0.0 };
        // Can't use negative numbers, so just using a flag
        public Quantity InvertOffset = new Quantity("Inv", "Invert Offset (0=No, 1=Yes)", "") { Val = 0.0 };
        public string Waveform = "Sine";

        public FunctionGenerator(Circuit parent, Point origin) : base(parent, origin)
        {
            ComponentType = "Function Generator";
            ID = parent.GetNextComponentName("V"); 
            ComponentValue = new Quantity("f", "Frequency", "Hz") { Val = 1.0 }; 

            ModelFile = ""; 
            LoadFootprintFromXml("FunctionGenerator"); 
        }

        protected override bool SetupPropertiesDialog(ComponentProperties dialog)
        {
            dialog.AddModels(new List<string> { "Sine", "Square", "Triangle", "DC" });
            dialog.SelectModel(Waveform);
            dialog.AddQuantity(ComponentValue);
            dialog.AddQuantity(Amplitude);
            dialog.AddQuantity(Offset);
            dialog.AddQuantity(InvertOffset); 
            return true;
        }

        protected override void AfterPropertiesDialog(ComponentProperties dialog)
        {
            Waveform = dialog.SelectedModel;
            ComponentValue.Val = dialog.Parameters[0].Val;
            Amplitude.Val = dialog.Parameters[1].Val;
            Offset.Val = dialog.Parameters[2].Val;
            InvertOffset.Val = dialog.Parameters[3].Val;
            UpdateText();
            ParentCircuit.ParentWindow.UpdatePrompt();
        }

        public override Dictionary<string, string> SaveParameters()
        {
            var p = base.SaveParameters();
            p["amp"] = Amplitude.Val.ToString();
            p["off"] = Offset.Val.ToString();
            p["inv"] = InvertOffset.Val.ToString();
            p["wave"] = Waveform;
            return p;
        }

        public override void LoadParameters(Dictionary<string, string> parameters)
        {
            base.LoadParameters(parameters);
            if (parameters.ContainsKey("amp")) Amplitude.Val = double.Parse(parameters["amp"]);
            if (parameters.ContainsKey("off")) Offset.Val = double.Parse(parameters["off"]);
            if (parameters.ContainsKey("inv")) InvertOffset.Val = double.Parse(parameters["inv"]);
            if (parameters.ContainsKey("wave")) Waveform = parameters["wave"];
            UpdateText();
        }

        public override string GenerateNetlist()
        {
            string n1 = ConnectedNets.ContainsKey(1) ? ConnectedNets[1] : "0";
            string n2 = ConnectedNets.ContainsKey(2) ? ConnectedNets[2] : "0";

            int waveType = 0; 
            if (Waveform == "Square") waveType = 1;
            if (Waveform == "Triangle") waveType = 2;
            if (Waveform == "DC") waveType = 3;

            // If InvertOffset is 1 (or anything greater than 0), multiply by -1. Otherwise, keep it positive.
            double actualOffset = Offset.Val * (InvertOffset.Val > 0 ? -1 : 1);

            return $"V_SINE {ID} {n1} {n2} amp={Amplitude.Val} freq={ComponentValue.Val} off={actualOffset} type={waveType}";
        }

        private string FormatValue(double val, string unit)
        {
            if (val == 0) return "0" + unit;
            double absVal = Math.Abs(val);
            if (absVal >= 1e6) return (val / 1e6).ToString("0.##") + "M" + unit;
            if (absVal >= 1e3) return (val / 1e3).ToString("0.##") + "k" + unit;
            if (absVal >= 1) return val.ToString("0.##") + unit;
            if (absVal >= 1e-3) return (val * 1e3).ToString("0.##") + "m" + unit;
            if (absVal >= 1e-6) return (val * 1e6).ToString("0.##") + "u" + unit;
            return val.ToString("0.##") + unit;
        }

        public override void UpdateText()
        {
            base.UpdateText();

            foreach (var path in Children.OfType<System.Windows.Shapes.Path>())
            {
                if (path.Name == "icon_sine") path.Visibility = Waveform == "Sine" ? Visibility.Visible : Visibility.Hidden;
                if (path.Name == "icon_square") path.Visibility = Waveform == "Square" ? Visibility.Visible : Visibility.Hidden;
                if (path.Name == "icon_triangle") path.Visibility = Waveform == "Triangle" ? Visibility.Visible : Visibility.Hidden;
                if (path.Name == "icon_dc1" || path.Name == "icon_dc2") path.Visibility = Waveform == "DC" ? Visibility.Visible : Visibility.Hidden;
            }

            double actualOffset = Offset.Val * (InvertOffset.Val > 0 ? -1 : 1);

            foreach (var textObject in Children.OfType<TextBlock>())
            {
                if (textObject.Name == "_VoltText")
                {
                    textObject.Text = Waveform == "DC" ? FormatValue(actualOffset, "V") : FormatValue(Amplitude.Val, "V");
                }
                if (textObject.Name == "_FreqText")
                {
                    textObject.Text = Waveform == "DC" ? "DC" : FormatValue(ComponentValue.Val, "Hz");
                }
            }
        }
    }
}