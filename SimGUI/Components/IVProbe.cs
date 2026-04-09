using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SimGUI
{
    public class IVProbe : Component 
    {
        public Brush ProbeColour = Brushes.Transparent;
        public string TargetComponentId = null;
        public int TargetPinNumber = -1;

        public IVProbe(Circuit parent, Point origin) : base(parent, origin)
        {
            ComponentType = "IVProbe";
            ID = parent.GetNextComponentName("IVP");
            ToolTip = "I/V Curve Probe (Device Characterizer)";
            
            ComponentValue = new Quantity("iv", "I/V Probe", "") { AllowZero = true, AllowNegative = true };
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
                
                // Note: Traces are refreshed on next run
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
                if (c == this || c is Probe || c is DiffProbe || c is CurrentProbe || c is XYProbe) continue;
                
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

    // 1. Determine the active color (Magenta if not yet assigned by the graph)
    Brush activeColor = (ProbeColour == Brushes.Transparent) ? Brushes.Magenta : ProbeColour;
    
    // 2. Create a soft fill color based on the active color
    Brush fillColor = (ProbeColour == Brushes.Transparent) 
        ? Brushes.Transparent 
        : new SolidColorBrush(Color.FromArgb(80, ((SolidColorBrush)activeColor).Color.R, ((SolidColorBrush)activeColor).Color.G, ((SolidColorBrush)activeColor).Color.B));

    // 3. Draw the Diamond
    Path ring = new Path();
    ring.Stroke = activeColor; // The outline now matches the graph!
    ring.StrokeThickness = 0.2;
    ring.Fill = fillColor;
    ring.Data = new RectangleGeometry(new Rect(-0.35, -0.35, 0.7, 0.7), 0.1, 0.1); 
    this.Children.Add(ring);

    // 4. Draw the Text
    TextBlock textLabel = new TextBlock
    {
        Text = "I/V",
        Foreground = (ProbeColour == Brushes.Transparent) ? Brushes.White : activeColor,
        FontSize = 0.35,
        FontWeight = FontWeights.Bold
    };
    
    Canvas.SetLeft(textLabel, -0.22);
    Canvas.SetTop(textLabel, -0.25);
    this.Children.Add(textLabel);
}

        public override string GenerateNetlist() { return ""; } 
    }
}