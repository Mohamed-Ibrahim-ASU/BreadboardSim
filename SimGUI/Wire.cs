using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SimGUI
{
    public class Wire : Canvas
    {
        public Point WirePosition; 
        public Point EndPoint;     
        public Color WireColour;
        
        public string NetName = "";
        public bool IsSelected = false;
        public bool IsTemporary = false;

        private Circuit ParentCircuit;

        public Wire(Circuit parent, Point origin, Point endPoint, Color newColour)
        {
            ParentCircuit = parent;
            WirePosition = origin;
            EndPoint = endPoint;
            WireColour = newColour;
            
            SetZIndex(this, 5);
            
            // Added tracking for all mouse events so we can forward them
            MouseDown += Wire_MouseDown;
            MouseUp += Wire_MouseUp;
            MouseMove += Wire_MouseMove;
            KeyDown += Wire_KeyDown;
        }

        public void Wire_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                ParentCircuit.RemoveWire(this);
                ParentCircuit.AddUndoAction(new DeleteAction(this, ParentCircuit));
            }
        }

        // --- EVENT FORWARDING LOGIC ---
        // These pass your mouse coordinates straight through the wire and into the breadboard logic

        private void Wire_MouseMove(object sender, MouseEventArgs e)
        {
            if (ParentCircuit.ParentWindow.SelectedTool == "WIRE" || ParentCircuit.ParentWindow.SelectedTool == "COMPONENT")
            {
                foreach (var child in ParentCircuit.ParentWindow.DrawArea.Children)
                {
                    if (child is Breadboard bb) bb.HandleMouseMove(this, e);
                }
            }
        }

        private void Wire_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (ParentCircuit.ParentWindow.SelectedTool == "WIRE" || ParentCircuit.ParentWindow.SelectedTool == "COMPONENT")
            {
                foreach (var child in ParentCircuit.ParentWindow.DrawArea.Children)
                {
                    if (child is Breadboard bb) 
                    {
                        bb.HandleMouseUp(this, e);
                        if (e.Handled) break; // Stop checking if we successfully hit a hole
                    }
                }
            }
        }

        private void Wire_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ParentCircuit.ParentWindow.SelectedTool == "WIRE" || ParentCircuit.ParentWindow.SelectedTool == "COMPONENT") return;

            ParentCircuit.DeselectAll();
            if (e.ChangedButton == MouseButton.Left)
            {
                if (ParentCircuit.ParentWindow.SelectedTool == "SELECT") Select();
                else if (ParentCircuit.ParentWindow.SelectedTool == "DELETE") ParentCircuit.RemoveWire(this);
            }
            ParentCircuit.ParentWindow.UpdatePrompt();
            e.Handled = true; 
        }

        private Color ActualColour;
        public void MakeTemporaryWire()
        {
            IsTemporary = true;
            IsHitTestVisible = false;
            ActualColour = WireColour;
            WireColour = Color.FromArgb(127, 100, 100, 100);
            Render();
        }

        public void MakePermanentWire()
        {
            IsTemporary = false;
            IsHitTestVisible = true;
            WireColour = ActualColour;
            Render(); 
        }

        public string[] GetConnectedBreadboardNets()
        {
            int startBbId = 0, endBbId = 0;
            Point startPtOnBb = Breadboard.GetPositionOnBreadboard(WirePosition, ref startBbId);
            Point endPtOnBb = Breadboard.GetPositionOnBreadboard(EndPoint, ref endBbId);
            return new string[] {
                Breadboard.GetNetAtPoint(startPtOnBb, startBbId.ToString()),
                Breadboard.GetNetAtPoint(endPtOnBb, endBbId.ToString())
            };
        }

        public void Select()
        {
            ParentCircuit.DeselectAll();
            IsSelected = true;
            Opacity = 0.5;
        }

        public void Deselect()
        {
            IsSelected = false;
            Opacity = 1;
        }

        public void Render()
        {
            Children.Clear();
            Canvas.SetLeft(this, WirePosition.X);
            Canvas.SetTop(this, WirePosition.Y);

            Point relEnd = new Point(EndPoint.X - WirePosition.X, EndPoint.Y - WirePosition.Y);
            double distance = Math.Sqrt(relEnd.X * relEnd.X + relEnd.Y * relEnd.Y);
            
            double pinSize = 0.1 * Constants.ScaleFactor; 
            double shadowSize = 0.22 * Constants.ScaleFactor;

            if (IsTemporary)
            {
                Path startShadow = new Path { Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), Data = new EllipseGeometry(new Point(0, 0), shadowSize, shadowSize), IsHitTestVisible = false };
                Children.Add(startShadow);

                if (distance >= 0.1)
                {
                    Path endShadow = new Path { Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), Data = new EllipseGeometry(relEnd, shadowSize, shadowSize), IsHitTestVisible = false };
                    Children.Add(endShadow);
                }
            }

            if (distance >= 0.1)
            {
                Path visualWire = new Path
                {
                    Stroke = new SolidColorBrush(WireColour),
                    StrokeThickness = 0.28 * Constants.ScaleFactor,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = new LineGeometry(new Point(0, 0), relEnd),
                    IsHitTestVisible = false 
                };
                Children.Add(visualWire);
            }

            Path startPin = new Path { Fill = Brushes.LightGray, Stroke = Brushes.DimGray, StrokeThickness = 0.03 * Constants.ScaleFactor, Data = new EllipseGeometry(new Point(0, 0), pinSize, pinSize), IsHitTestVisible = false };
            Children.Add(startPin);

            if (distance >= 0.1)
            {
                Path endPin = new Path { Fill = Brushes.LightGray, Stroke = Brushes.DimGray, StrokeThickness = 0.03 * Constants.ScaleFactor, Data = new EllipseGeometry(relEnd, pinSize, pinSize), IsHitTestVisible = false };
                Children.Add(endPin);

                double gap = 0.4 * Constants.ScaleFactor;
                Point hitStart = distance > gap * 2 ? new Point((relEnd.X / distance) * gap, (relEnd.Y / distance) * gap) : new Point(0, 0);
                Point hitEnd = distance > gap * 2 ? new Point(relEnd.X - (relEnd.X / distance) * gap, relEnd.Y - (relEnd.Y / distance) * gap) : relEnd;

                Path hitBox = new Path
                {
                    Stroke = Brushes.Transparent, 
                    StrokeThickness = 0.6 * Constants.ScaleFactor, 
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = new LineGeometry(hitStart, hitEnd),
                    IsHitTestVisible = true 
                };
                Children.Add(hitBox);
            }
        }

        public virtual Dictionary<string, string> SaveParameters()
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters["startX"] = WirePosition.X.ToString();
            parameters["startY"] = WirePosition.Y.ToString();
            parameters["endX"] = EndPoint.X.ToString();
            parameters["endY"] = EndPoint.Y.ToString();
            parameters["colour"] = WireColour.ToString();
            
            parameters["length"] = "0";
            parameters["orientation"] = "horiz"; 
            return parameters;
        }

        public static Wire CreateFromParameters(Circuit parent, Dictionary<string, string> parameters)
        {
            Point origin = new Point(double.Parse(parameters["startX"]), double.Parse(parameters["startY"]));
            Point endPoint;

            if (parameters.ContainsKey("endX") && parameters.ContainsKey("endY"))
            {
                endPoint = new Point(double.Parse(parameters["endX"]), double.Parse(parameters["endY"]));
            }
            else
            {
                int length = int.Parse(parameters["length"]);
                endPoint = origin;
                if (parameters["orientation"] == "horiz") endPoint.X += length * Constants.ScaleFactor;
                else endPoint.Y += length * Constants.ScaleFactor;
            }

            Color colour = (Color)ColorConverter.ConvertFromString(parameters["colour"]);
            Wire newWire = new Wire(parent, origin, endPoint, colour);
            newWire.Render();
            return newWire;
        }

        public void ResetFromParameters(Dictionary<string, string> parameters)
        {
            WirePosition = new Point(double.Parse(parameters["startX"]), double.Parse(parameters["startY"]));
            EndPoint = new Point(double.Parse(parameters["endX"]), double.Parse(parameters["endY"]));
            WireColour = (Color)ColorConverter.ConvertFromString(parameters["colour"]);
            Render();
        }
    }
}