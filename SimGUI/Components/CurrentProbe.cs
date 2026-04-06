using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SimGUI
{
    public class CurrentProbe : Component 
    {
        public Brush ProbeColour = Brushes.Transparent;
        public string TargetComponentId = null;
        public int TargetPinNumber = -1;

        public CurrentProbe(Circuit parent, Point origin) : base(parent, origin)
        {
            ComponentType = "CurrentProbe";
            ID = parent.GetNextComponentName("IP");
            ToolTip = "Current Probe (Ammeter)";
            
            ComponentValue = new Quantity("i", "Current", "A") { AllowZero = true, AllowNegative = true };
            PinPositions[1] = new Point(0, 0);
            DrawVisuals();
        }

        public void SetProbeColour(Brush colour)
        {
            ProbeColour = colour;
            DrawVisuals();
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
                ProbeColour = selectedPair.Value;
                
                if (ParentCircuit.ParentWindow.CurrentGraph != null)
                {
                    ParentCircuit.ParentWindow.CurrentGraph.UpdateTraceMapping(ID, -1, -1, ProbeColour);
                }
            }
            DrawVisuals();
        }

        public void BindTarget(Circuit circuit)
        {
            TargetComponentId = null;
            TargetPinNumber = -1;
            double minDistance = double.MaxValue;
            Point myPos = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));

            foreach (Component c in circuit.Components)
            {
                if (c == this || c is Probe || c is DiffProbe || c is CurrentProbe) continue;
                
                double compX = Canvas.GetLeft(c);
                double compTop = Canvas.GetTop(c);

                foreach (var pin in c.GetPinPositions())
                {
                    Point pinAbs = new Point(compX + pin.Value.X * Constants.ScaleFactor, compTop + pin.Value.Y * Constants.ScaleFactor);
                    double dist = Math.Sqrt(Math.Pow(myPos.X - pinAbs.X, 2) + Math.Pow(myPos.Y - pinAbs.Y, 2));
                    
                    if (dist < minDistance && dist < 60) 
                    {
                        minDistance = dist;
                        TargetComponentId = c.ID;
                        TargetPinNumber = pin.Key;
                    }
                }
            }
        }

        private void DrawVisuals()
        {
            this.Children.Clear();
            this.Background = Brushes.Transparent; 
            
            
            Path ring = new Path();
            ring.Stroke = Brushes.Gold; 
            ring.StrokeThickness = 0.15;
            ring.Fill = (ProbeColour == Brushes.Transparent) ? ProbeColour : new SolidColorBrush(Color.FromArgb(180, ((SolidColorBrush)ProbeColour).Color.R, ((SolidColorBrush)ProbeColour).Color.G, ((SolidColorBrush)ProbeColour).Color.B));
            ring.Data = new EllipseGeometry(new Point(0, 0), 0.35, 0.35); 
            this.Children.Add(ring);

            TextBlock textA = new TextBlock
            {
                Text = "A",
                Foreground = Brushes.White,
                FontSize = 0.5,
                FontWeight = FontWeights.Bold
            };
            
            Canvas.SetLeft(textA, -0.17);
            Canvas.SetTop(textA, -0.34);
            this.Children.Add(textA);
        }

        public override string GenerateNetlist() { return ""; } 
    }
}