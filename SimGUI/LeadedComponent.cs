using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SimGUI
{
    public class LeadedComponent : Component
    {
        public int Length = 0;
        
        public int MinLength 
        { 
            get { return 1; } 
            set { /* Silently ignore any attempts to make this longer! */ } 
        }
        
        public Orientation orientation;
        public bool IsTemporary = false;

        public Point EndPoint;
        public bool Is2D = false;

        public LeadedComponent(Circuit parent, Point origin) : base(parent, origin)
        {
            EndPoint = origin; 
        }

        public virtual void Render()
        {
            Children.Clear();
            double renderDist = Math.Abs(Length);
            double angle = 0;

            if (Is2D)
            {
                double dx = EndPoint.X - ComponentPosition.X;
                double dy = EndPoint.Y - ComponentPosition.Y;
                double absDist = Math.Sqrt(dx * dx + dy * dy);
                
                renderDist = absDist / Constants.ScaleFactor;
                angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
                
                Length = (int)Math.Round(renderDist);
                orientation = Orientation.Horizontal; 
            }
            else
            {
                if (Length < 0) angle = 180;
            }

            // 1. DRAG PREVIEW SHADOWS
            double shadowSize = 0.22 * Constants.ScaleFactor;
            if (IsTemporary)
            {
                Path startShadow = new Path { Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), Data = new EllipseGeometry(new Point(0, 0), shadowSize, shadowSize), IsHitTestVisible = false };
                Children.Add(startShadow);

                if (renderDist >= 0.1)
                {
                    Path endShadow = new Path { Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), Data = new EllipseGeometry(new Point(renderDist * Constants.ScaleFactor, 0), shadowSize, shadowSize), IsHitTestVisible = false };
                    Children.Add(endShadow);
                }
            }

            // 2. THE GREY LEADS
            Path lead1 = new Path { Name = "pin1", StrokeThickness = 0.02, Stroke = Brushes.Gray, Fill = Brushes.Gray, IsHitTestVisible = false };
            Path lead2 = new Path { Name = "pin2", StrokeThickness = 0.02, Stroke = Brushes.Gray, Fill = Brushes.Gray, IsHitTestVisible = false };

            if (Is2D || orientation == Orientation.Horizontal)
            {
                lead1.Data = new RectangleGeometry(new Rect(-0.2, -0.15, renderDist / 2.0 + 0.2, 0.3));
                lead2.Data = new RectangleGeometry(new Rect(renderDist / 2.0, -0.15, renderDist / 2.0 + 0.2, 0.3));
            }
            else
            {
                lead1.Data = new RectangleGeometry(new Rect(-0.15, -0.2, 0.3, renderDist / 2.0 + 0.2));
                lead2.Data = new RectangleGeometry(new Rect(-0.15, renderDist / 2.0, 0.3, renderDist / 2.0 + 0.2));
            }

            lead1.RenderTransform = new ScaleTransform(Constants.ScaleFactor, Constants.ScaleFactor);
            lead2.RenderTransform = new ScaleTransform(Constants.ScaleFactor, Constants.ScaleFactor);

            Canvas.SetZIndex(lead1, -1); 
            Canvas.SetZIndex(lead2, -1);
            Children.Add(lead1);
            Children.Add(lead2);

            // 3. METALLIC PINS
            double pinSize = 0.1 * Constants.ScaleFactor; 
            Path startPin = new Path { Fill = Brushes.LightGray, Stroke = Brushes.DimGray, StrokeThickness = 0.03 * Constants.ScaleFactor, Data = new EllipseGeometry(new Point(0, 0), pinSize, pinSize), IsHitTestVisible = false };
            Children.Add(startPin);

            if (renderDist >= 0.1)
            {
                Path endPin = new Path { Fill = Brushes.LightGray, Stroke = Brushes.DimGray, StrokeThickness = 0.03 * Constants.ScaleFactor, Data = new EllipseGeometry(new Point(renderDist * Constants.ScaleFactor, 0), pinSize, pinSize), IsHitTestVisible = false };
                Children.Add(endPin);
                
                // 4. INVISIBLE HITBOX
                double gap = 0.4 * Constants.ScaleFactor;
                Point hitStart = renderDist * Constants.ScaleFactor > gap * 2 ? new Point(gap, 0) : new Point(0, 0);
                Point hitEnd = renderDist * Constants.ScaleFactor > gap * 2 ? new Point(renderDist * Constants.ScaleFactor - gap, 0) : new Point(renderDist * Constants.ScaleFactor, 0);

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

            RenderTransform = new RotateTransform(angle);
        }

        public override Dictionary<int, Point> GetPinPositions()
        {
            Dictionary<int, Point> realPinPositions = new Dictionary<int, Point>();
            realPinPositions[1] = new Point(0, 0); 
            
            if (Is2D) 
            {
                double dx = (EndPoint.X - ComponentPosition.X) / Constants.ScaleFactor;
                double dy = (EndPoint.Y - ComponentPosition.Y) / Constants.ScaleFactor;
                realPinPositions[2] = new Point(dx, dy);
            }
            else 
            {
                if (orientation == Orientation.Horizontal) realPinPositions[2] = new Point(Length, 0);
                else realPinPositions[2] = new Point(0, Length);
                
            }
            return realPinPositions;
        }

        public void MakeTemporary()
        {
            IsTemporary = true;
            IsHitTestVisible = false;
            Opacity = 0.5;
            Render();
        }

        public void MakePermanent()
        {
            IsTemporary = false;
            IsHitTestVisible = true;
            Opacity = 1;
            Render();
        }

        public override Dictionary<string, string> SaveParameters()
        {
            Dictionary<string, string> parameters = base.SaveParameters();
            if (Is2D) 
            {
                parameters["endX"] = EndPoint.X.ToString();
                parameters["endY"] = EndPoint.Y.ToString();
            }
            parameters["orientation"] = (orientation == Orientation.Horizontal) ? "horiz" : "vert";
            parameters["length"] = Length.ToString();
            return parameters;
        }

        public override void LoadParameters(Dictionary<string, string> parameters)
        {
            base.LoadParameters(parameters);
            
            if (parameters.ContainsKey("endX") && parameters.ContainsKey("endY")) 
            {
                Is2D = true;
                EndPoint = new Point(double.Parse(parameters["endX"]), double.Parse(parameters["endY"]));
            } 
            else 
            {
                Is2D = false;
            }
            
            if (parameters.ContainsKey("orientation") && parameters.ContainsKey("length"))
            {
                // Modern save file.
                orientation = (parameters["orientation"] == "horiz") ? Orientation.Horizontal : Orientation.Vertical;
                Length = int.Parse(parameters["length"]);
            }
            else
            {
                // Old legacy save file.
                int legacyAngle = parameters.ContainsKey("angle") ? int.Parse(parameters["angle"]) : 0;

                if (legacyAngle == 90 || legacyAngle == 270) 
                    orientation = Orientation.Vertical;
                else 
                    orientation = Orientation.Horizontal;

                if (PinPositions != null && PinPositions.ContainsKey(2))
                {
                    double dx = PinPositions[2].X;
                    double dy = PinPositions[2].Y;
                    Length = (int)Math.Round(Math.Max(Math.Abs(dx), Math.Abs(dy)));
                }
                else
                {
                    Length = 1; 
                }

                if (legacyAngle == 180 || legacyAngle == 270) Length = -Length;
            }
            
            Render();
        }
    }
}