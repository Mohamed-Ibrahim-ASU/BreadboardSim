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

            double sc = Constants.ScaleFactor;
            PinPositions[1] = new Point(0, 0);

            // Calculate exact high-precision magnitude to bypass base class Length truncation
            double exactLength = Length;
            if (Is2D)
            {
                Point gridP2 = GetPinPositions()[2];
                exactLength = Math.Sqrt(gridP2.X * gridP2.X + gridP2.Y * gridP2.Y);
            }

            // Project strictly horizontally (or vertically) for base class RotateTransform
            double p2X = (orientation == Orientation.Horizontal) ? exactLength : 0;
            double p2Y = (orientation == Orientation.Horizontal) ? 0 : exactLength;
            PinPositions[2] = new Point(p2X, p2Y);

            Point p1 = PinPositions[1];
            Point p2 = PinPositions[2];

            // --- RED RING (+) ---
            AddMarkerDirect(p1, Brushes.Red, "+", ProbeColour, sc);

            // --- BLACK RING (-) ---
            AddMarkerDirect(p2, Brushes.Black, "-", ProbeColour, sc);
        }

        private void AddMarkerDirect(Point pos, Brush border, string sign, Brush fill, double sc)
        {
            double cx = pos.X * sc;
            double cy = pos.Y * sc;
            double r  = 0.35 * sc;
            double sw = 0.08 * sc;
            double sl = 0.15 * sc; // half-length of the symbol lines

            Brush ringFill = (fill == Brushes.Transparent) ? fill : new SolidColorBrush(Color.FromArgb(180, ((SolidColorBrush)fill).Color.R, ((SolidColorBrush)fill).Color.G, ((SolidColorBrush)fill).Color.B));

            // Ring (Clickable hit-box)
            Children.Add(new Path
            {
                Stroke = border,
                StrokeThickness = 0.15 * sc,
                Fill = ringFill,
                Data = new EllipseGeometry(new Point(cx, cy), r, r)
            });

            // Symbol
            Path symbol = new Path
            {
                Stroke = Brushes.White,
                StrokeThickness = sw,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false // Keep the inner cross/line unclickable
            };

            if (sign == "+") 
            {
                symbol.Data = Geometry.Parse($"M {(cx - sl):F2},{cy:F2} L {(cx + sl):F2},{cy:F2} M {cx:F2},{(cy - sl):F2} L {cx:F2},{(cy + sl):F2}");
            }
            else 
            {
                symbol.Data = Geometry.Parse($"M {(cx - sl):F2},{cy:F2} L {(cx + sl):F2},{cy:F2}");
            }

            Children.Add(symbol);
        }

        public override string GenerateNetlist() { return ""; }
    }
}