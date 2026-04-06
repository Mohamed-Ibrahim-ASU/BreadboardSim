using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SimGUI
{
    public class DiffProbe : LeadedComponent
    {
        public Brush ProbeColour = Brushes.Transparent;

        public DiffProbe(Circuit parent, Point origin) : base(parent, origin)
        {
            ComponentType = "DiffProbe";
            MinLength = 1; 
            ID = parent.GetNextComponentName("VP"); 
            ToolTip = "Differential Voltage Probe"; 
            
            // Allow properties window to open
            ComponentValue = new Quantity("vdrop", "Voltage Drop", "V") { AllowZero = true, AllowNegative = true };
        }

        public void SetProbeColour(Brush colour)
        {
            ProbeColour = colour;
            Render();
        }

        protected override bool SetupPropertiesDialog(ComponentProperties dialog)
        {
            // Use our new Color Selection instead of the numeric box
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
            Render();
        }

        public override void Render()
        {
            base.Render(); 

            // DESTROY THE PHANTOM HITBOX:
            // This ensures the empty space between the probes allows clicks to pass through to the board
            this.Background = null;

            // Hide original grey metal leads and make them unclickable
            foreach (UIElement child in Children)
            {
                if (child is Path p) 
                {
                    p.Visibility = Visibility.Hidden;
                    p.IsHitTestVisible = false; // Prevents phantom wire hovering
                }
            }

            double renderLength = Math.Abs(Length);
            PinPositions[1] = new Point(0, 0);
            Point p2 = (orientation == Orientation.Horizontal) ? new Point(renderLength, 0) : new Point(0, renderLength);
            PinPositions[2] = p2;

            // --- RED RING (+) ---
            Children.Add(CreateProbeMarker(new Point(0, 0), Brushes.Red, "+", ProbeColour));

            // --- BLACK RING (-) ---
            Children.Add(CreateProbeMarker(p2, Brushes.Black, "-", ProbeColour));
        }

        private UIElement CreateProbeMarker(Point pos, Brush border, string sign, Brush fill)
        {
            Canvas marker = new Canvas();
            
            Path ring = new Path();
            ring.Stroke = border;
            ring.StrokeThickness = 0.15;
            ring.Fill = (fill == Brushes.Transparent) ? fill : new SolidColorBrush(Color.FromArgb(180, ((SolidColorBrush)fill).Color.R, ((SolidColorBrush)fill).Color.G, ((SolidColorBrush)fill).Color.B));
            ring.Data = new EllipseGeometry(new Point(0, 0), 0.35, 0.35);
            marker.Children.Add(ring);

            Path symbol = new Path();
            symbol.Stroke = Brushes.White;
            symbol.StrokeThickness = 0.08;
            symbol.StrokeStartLineCap = PenLineCap.Round;
            symbol.StrokeEndLineCap = PenLineCap.Round;

            if (sign == "+") symbol.Data = Geometry.Parse("M -0.15,0 L 0.15,0 M 0,-0.15 L 0,0.15");
            else symbol.Data = Geometry.Parse("M -0.15,0 L 0.15,0");

            marker.Children.Add(symbol);
            marker.RenderTransform = new ScaleTransform(Constants.ScaleFactor, Constants.ScaleFactor);
            Canvas.SetLeft(marker, pos.X * Constants.ScaleFactor);
            Canvas.SetTop(marker, pos.Y * Constants.ScaleFactor);
            
            return marker;
        }

        public override string GenerateNetlist() { return ""; }
    }
}