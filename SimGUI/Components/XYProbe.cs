using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SimGUI
{
    public class XYProbe : LeadedComponent
    {
        public Brush ProbeColour = Brushes.Transparent;

        public XYProbe(Circuit parent, Point origin) : base(parent, origin)
        {
            ComponentType  = "XYProbe";
            MinLength      = 1;
            ID             = parent.GetNextComponentName("XY");
            ToolTip        = "XY Transfer Probe";
            // Required so right-click → Properties doesn't NullRef
            ComponentValue = new Quantity("val", "Probe", "V") { AllowZero = true, AllowNegative = true };
        }

        public void SetProbeColour(Brush colour)
        {
            ProbeColour = colour;
            Render();
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
                var pair = (KeyValuePair<string, Brush>)dialog.ColorSelectionBox.SelectedItem;
                ProbeColour = pair.Value;

                // Keep the XY graph trace colour in sync with the probe colour
                if (ParentCircuit.ParentWindow.CurrentXYGraph != null)
                    ParentCircuit.ParentWindow.CurrentXYGraph.UpdateTraceColour(ID, ProbeColour);
            }
            Render();
        }

        public override void Render()
        {
            base.Render();
            Background = null;

            // Hide paths drawn by base class
            foreach (UIElement child in Children)
                if (child is Path p) { p.Visibility = Visibility.Hidden; p.IsHitTestVisible = false; }

            double sc = Constants.ScaleFactor;

            PinPositions[1] = new Point(0, 0);

            // Calculate exact high-precision magnitude to bypass base class Length rounding
            double exactLength = Length;
            if (Is2D)
            {
                Point gridP2 = GetPinPositions()[2];
                exactLength = Math.Sqrt(gridP2.X * gridP2.X + gridP2.Y * gridP2.Y);
            }

            // Project strictly horizontally (or vertically) to let the base class RotateTransform handle the angle
            double p2X = (orientation == Orientation.Horizontal) ? exactLength : 0;
            double p2Y = (orientation == Orientation.Horizontal) ? 0 : exactLength;
            PinPositions[2] = new Point(p2X, p2Y);

            Point p1 = PinPositions[1];
            Point p2 = PinPositions[2];

            // Dashed stem
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
            if (Math.Sqrt(dx*dx + dy*dy) >= 0.5)
            {
                Children.Add(new Path
                {
                    Stroke           = new SolidColorBrush(Color.FromArgb(100, 90, 90, 90)),
                    StrokeThickness  = 0.06 * sc,
                    StrokeDashArray  = new DoubleCollection(new double[] { 2, 1.5 }),
                    Data             = new LineGeometry(
                        new Point(p1.X * sc, p1.Y * sc),
                        new Point(p2.X * sc, p2.Y * sc)),
                    IsHitTestVisible = false
                });
            }

            AddMarkerDirect(p1, Color.FromRgb(34, 160, 80),  isInput: true,  sc);
            AddMarkerDirect(p2, Color.FromRgb(220, 110, 20), isInput: false, sc);
        }

        // Draws directly onto 'this' Canvas — no child Canvas, no ScaleTransform
        private void AddMarkerDirect(Point pos, Color colour, bool isInput, double sc)
        {
            double cx = pos.X * sc;
            double cy = pos.Y * sc;
            double r  = 0.37 * sc;
            double si = 0.17 * sc;
            double sw = 0.07 * sc;
            double rd = 0.12 * sc;

            Color fill = (ProbeColour == null || ProbeColour == Brushes.Transparent)
                ? Color.FromArgb(60, colour.R, colour.G, colour.B)
                : Color.FromArgb(80,
                    ((SolidColorBrush)ProbeColour).Color.R,
                    ((SolidColorBrush)ProbeColour).Color.G,
                    ((SolidColorBrush)ProbeColour).Color.B);

            // Ring
            Children.Add(new Path
            {
                Stroke           = new SolidColorBrush(colour),
                StrokeThickness  = 0.12 * sc,
                Fill             = new SolidColorBrush(fill),
                Data             = new EllipseGeometry(new Point(cx, cy), r, r),
            });

            // Symbol
            if (isInput)
            {
                Children.Add(new Path
                {
                    Stroke             = Brushes.White,
                    StrokeThickness    = sw,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap   = PenLineCap.Round,
                    Data               = Geometry.Parse(
                        $"M {cx:F2},{(cy-si):F2} L {cx:F2},{(cy+si):F2} " +
                        $"M {(cx-sw*1.2):F2},{(cy-si):F2} L {(cx+sw*1.2):F2},{(cy-si):F2} " +
                        $"M {(cx-sw*1.2):F2},{(cy+si):F2} L {(cx+sw*1.2):F2},{(cy+si):F2}"),
                    IsHitTestVisible = false
                });
            }
            else
            {
                Children.Add(new Path
                {
                    Fill            = Brushes.White,
                    StrokeThickness = 0,
                    Data            = new EllipseGeometry(new Point(cx, cy), rd, rd),
                    
                });
            }
        }

        public override string GenerateNetlist() { return ""; }
    }
}
