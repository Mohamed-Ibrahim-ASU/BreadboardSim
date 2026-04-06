using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SimGUI
{
    class TraceDisplay : FrameworkElement
    {
        private List<Brush> TraceBrushes = new List<Brush>();
        private List<Pen> TracePens = new List<Pen>(); 
        private List<bool> TraceIsCurrent = new List<bool>(); // Tracks the domain
        private VisualCollection visualChildren;

        public TraceDisplay()
        {
            visualChildren = new VisualCollection(this);
        }

        public void Reset()
        {
            visualChildren.Clear();
            TraceBrushes.Clear();
            TracePens.Clear();
            TraceIsCurrent.Clear();
        }

        public void AddTrace(Brush brush, bool isCurrent = false)
        {
            visualChildren.Add(new DrawingVisual());
            TraceBrushes.Add(brush);
            TraceIsCurrent.Add(isCurrent);
            
            Pen newPen = CreatePen(brush, isCurrent);
            TracePens.Add(newPen);
        }
        
        public void UpdateTraceBrush(int traceNumber, Brush newBrush)
        {
            if (traceNumber >= 0 && traceNumber < TraceBrushes.Count)
            {
                TraceBrushes[traceNumber] = newBrush;
                Pen updatedPen = CreatePen(newBrush, TraceIsCurrent[traceNumber]);
                TracePens[traceNumber] = updatedPen;
            }
        }

        // Helper method to generate the distinct styles
        private Pen CreatePen(Brush baseBrush, bool isCurrent)
        {
            Pen p;
            if (isCurrent)
            {
                // Current: Thick, slightly translucent, rounded "marker" style
                Brush currentBrush = baseBrush.Clone();
                currentBrush.Opacity = 0.55; 
                currentBrush.Freeze();
                
                p = new Pen(currentBrush, 2.5);
            }
            else
            {
                // Voltage: Crisp, solid, thin line
                p = new Pen(baseBrush, 2.0);
            }

            // Rounding the caps and joins makes signal spikes look much better
            p.StartLineCap = PenLineCap.Round;
            p.EndLineCap = PenLineCap.Round;
            p.LineJoin = PenLineJoin.Round;
            p.Freeze();
            
            return p;
        }

        public void SetTracePoints(int traceNumber, List<Point> points)
        {
            if (traceNumber < visualChildren.Count)
            {
                DrawingVisual traceVisual = (DrawingVisual)visualChildren[traceNumber];

                using (DrawingContext traceContext = traceVisual.RenderOpen())
                {
                    if (points.Count > 1)
                    {
                        StreamGeometry streamGeo = new StreamGeometry();
                        using (StreamGeometryContext ctx = streamGeo.Open())
                        {
                            bool figureOpen = false;

                            for (int i = 0; i < points.Count; i++)
                            {
                                Point p = points[i];

                                // NaN sentinel = restart boundary, lift the pen
                                if (double.IsNaN(p.X) || double.IsNaN(p.Y))
                                {
                                    figureOpen = false;
                                    continue;
                                }

                                // Clamp to left edge as before
                                if (p.X < 0)
                                    p = new Point(0, p.Y);

                                if (!figureOpen)
                                {
                                    ctx.BeginFigure(p, false, false);
                                    figureOpen = true;
                                }
                                else
                                {
                                    ctx.LineTo(p, true, false);
                                }

                                // Stop drawing past the left edge
                                if (p.X <= 0)
                                    break;
                            }
                        }

                        streamGeo.Freeze();
                        traceContext.DrawGeometry(null, TracePens[traceNumber], streamGeo);
                    }
                }
            }
        }
        protected override int VisualChildrenCount
        {
            get { return visualChildren.Count; }
        }

        protected override Visual GetVisualChild(int index)
        {
            return visualChildren[index];
        }
    }
}