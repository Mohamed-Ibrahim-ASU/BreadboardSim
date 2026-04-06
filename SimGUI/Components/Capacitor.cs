using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SimGUI
{
    public class Capacitor : LeadedComponent 
    {
        public bool IsElectrolytic = false;
        private Canvas footprintCanvas = new Canvas();

        public Capacitor(Circuit parent, Point origin, bool electrolytic = false)
            : base(parent, origin)
        {
            IsElectrolytic = electrolytic;
            ComponentType = electrolytic ? "Electrolytic Capacitor" : "Capacitor";
            
            ComponentValue = new Quantity("cap", "Capacitance", "F");
            ComponentValue.AllowZero = false;
            ComponentValue.AllowNegative = false;
            ID = parent.GetNextComponentName("C");

            LoadFootprintFromXml(electrolytic ? "capacitor_elec" : "capacitor_np");
            
            UIElement[] elements = new UIElement[Children.Count];
            Children.CopyTo(elements, 0);
            Children.Clear();
            foreach (var el in elements) 
            {
                footprintCanvas.Children.Add(el);
            }
            
            footprintCanvas.RenderTransform = new ScaleTransform(Constants.ScaleFactor, Constants.ScaleFactor);
            Canvas.SetZIndex(footprintCanvas, 10); 
            
            UpdateText();
        }

        public override void UpdateText()
        {
            foreach (var textObject in footprintCanvas.Children.OfType<TextBlock>())
            {
                if (textObject.Name == "_Model") textObject.Text = ComponentModel;
                else if (textObject.Name == "_Value") textObject.Text = ComponentValue.ToString();
            }
        }

        public override void Render()
        {
            // Draw the stretchable diagonal leads from the LeadedComponent engine
            base.Render();

            if (Children.Count == 0) return;

            // Calculate the exact distance of the stretched leads
            double renderDist = Is2D ? 
                Math.Sqrt(Math.Pow(EndPoint.X - ComponentPosition.X, 2) + Math.Pow(EndPoint.Y - ComponentPosition.Y, 2)) / Constants.ScaleFactor 
                : Math.Abs(Length);

            if (renderDist < 0.1) return; 

            
            // Scan the raw XML shapes to find their true visual boundaries
            Rect bounds = Rect.Empty;
            foreach (var child in footprintCanvas.Children)
            {
                if (child is Path p && p.Data != null)
                {
                    if (bounds.IsEmpty) bounds = p.Data.Bounds;
                    else bounds.Union(p.Data.Bounds);
                }
            }

            // Failsafe in case the XML footprint paths are corrupted
            if (bounds.IsEmpty) bounds = new Rect(0, 0, 1, 1);

            // Calculate the exact mathematical center of the original XML drawing
            double originalCenterX = bounds.Left + (bounds.Width / 2.0);
            double originalCenterY = bounds.Top + (bounds.Height / 2.0);

            // Find the midpoint of the newly stretched leads
            double targetCenterX = (renderDist * Constants.ScaleFactor) / 2.0;
            double targetCenterY = 0; // Local Y is 0 because LeadedComponent rotates the entire Canvas!

            // Shift the Canvas so the original center perfectly aligns with the target center
            Canvas.SetLeft(footprintCanvas, targetCenterX - (originalCenterX * Constants.ScaleFactor));
            Canvas.SetTop(footprintCanvas, targetCenterY - (originalCenterY * Constants.ScaleFactor));
            
            if (!Children.Contains(footprintCanvas))
            {
                Children.Add(footprintCanvas);
            }
        }

        public override string GenerateNetlist()
        {
            return "CAP " + ID + " " + ConnectedNets[1] + " " + ConnectedNets[2] + " rser=0.001 cap=" + ComponentValue.Val.ToString();
        }
    }
}