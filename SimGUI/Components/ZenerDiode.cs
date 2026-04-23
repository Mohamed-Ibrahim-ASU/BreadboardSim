using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SimGUI
{
    class ZenerDiode : LeadedComponent
    {
        private const double BodyLength = 1.5;
        private readonly Color BodyColour = Color.FromRgb(180, 60, 60); 
        private bool isFirstRender = true;

        public Quantity TestCurrent = new Quantity("ibv", "Test Current (Izt)", "A");
        public Quantity ZenerResistance = new Quantity("rser", "Dynamic Resistance (Rz)", "Ω");
        public Quantity BreakdownIdeality = new Quantity("nbv", "Breakdown Ideality", "");

        public ZenerDiode(Circuit parent, Point origin, string model)
            : base(parent, origin)
        {
            ComponentType = "Zener Diode";
            MinLength = 3;
            ID = parent.GetNextComponentName("D");
            ModelFile = "res/models/zeners.xml";
            
            ComponentValue = new Quantity("bv", "Breakdown Voltage", "V");
            ComponentValue.AllowZero = false;
            ComponentValue.AllowNegative = false;
            ComponentValue.Val = 5.1; 
            
            TestCurrent.AllowZero = false;
            TestCurrent.AllowNegative = false;
            TestCurrent.SetValueWithPrefix(5, Quantity.Prefix.Milli);

            ZenerResistance.AllowZero = false;
            ZenerResistance.AllowNegative = false;
            ZenerResistance.Val = 15;

            BreakdownIdeality.AllowZero = false;
            BreakdownIdeality.AllowNegative = false;
            BreakdownIdeality.Val = 1.5;

            Dictionary<string, string> meta = LoadModel(model);
            SyncUIWithModel(meta);

            PinNames.Add(1, "Anode");
            PinNames.Add(2, "Cathode");
        }

        private void SyncUIWithModel(Dictionary<string, string> meta)
        {
            if (meta.ContainsKey("bv")) ComponentValue.Val = double.Parse(meta["bv"]);
            if (meta.ContainsKey("ibv")) TestCurrent.Val = double.Parse(meta["ibv"]);
            if (meta.ContainsKey("rser")) ZenerResistance.Val = double.Parse(meta["rser"]);
            if (meta.ContainsKey("nbv")) BreakdownIdeality.Val = double.Parse(meta["nbv"]);
        }

        public override void Render()
        {
            if (isFirstRender)
            {
                if (ComponentValue.Val == 0)
                {
                    Dictionary<string, string> meta = LoadModel(ComponentModel);
                    SyncUIWithModel(meta);
                }
                isFirstRender = false;
            }
            base.Render();
            double renderLength = Math.Abs(Length);

            if (renderLength >= MinLength)
            {
                Path diodeBody = new Path();
                diodeBody.StrokeThickness = 0.02;
                diodeBody.Stroke = new SolidColorBrush(BodyColour);
                diodeBody.Fill = new SolidColorBrush(BodyColour);
                if (orientation == Orientation.Horizontal)
                    diodeBody.Data = new RectangleGeometry(new Rect((renderLength / 2) - (BodyLength / 2), -0.4, BodyLength, 0.8));
                else
                    diodeBody.Data = new RectangleGeometry(new Rect(-0.4, (renderLength / 2) - (BodyLength / 2), 0.8, BodyLength));
                diodeBody.RenderTransform = new ScaleTransform(Constants.ScaleFactor, Constants.ScaleFactor);
                Children.Add(diodeBody);
                SetZIndex(diodeBody, 0);

                Path polarityBand = new Path();
                polarityBand.StrokeThickness = 0.1; 
                polarityBand.Stroke = Brushes.LightGray; 
                polarityBand.StrokeLineJoin = PenLineJoin.Miter;
                
                PathFigure wingFigure = new PathFigure();
                if (orientation == Orientation.Horizontal)
                {
                    double bandX = (renderLength / 2) - (BodyLength / 2) + 1.125;
                    wingFigure.StartPoint = new Point(bandX - 0.2, -0.4);
                    wingFigure.Segments.Add(new LineSegment(new Point(bandX, -0.4), true));
                    wingFigure.Segments.Add(new LineSegment(new Point(bandX, 0.4), true));
                    wingFigure.Segments.Add(new LineSegment(new Point(bandX + 0.2, 0.4), true));
                }
                else
                {
                    double bandY = (renderLength / 2) - (BodyLength / 2) + 1.125;
                    wingFigure.StartPoint = new Point(-0.4, bandY + 0.2);
                    wingFigure.Segments.Add(new LineSegment(new Point(-0.4, bandY), true));
                    wingFigure.Segments.Add(new LineSegment(new Point(0.4, bandY), true));
                    wingFigure.Segments.Add(new LineSegment(new Point(0.4, bandY - 0.2), true));
                }
                
                PathGeometry wingGeometry = new PathGeometry();
                wingGeometry.Figures.Add(wingFigure);
                polarityBand.Data = wingGeometry;
                
                polarityBand.RenderTransform = new ScaleTransform(Constants.ScaleFactor, Constants.ScaleFactor);
                SetZIndex(polarityBand, 1); 
                Children.Add(polarityBand);
            }
            
            PinPositions[1] = new Point(0, 0);
            if (orientation == Orientation.Horizontal)
            {
                PinPositions[2] = new Point(Length, 0);
            }
            else
            {
                PinPositions[2] = new Point(0, Length);
            }
        }

        protected override bool SetupPropertiesDialog(ComponentProperties dialog)
        {
            bool result = base.SetupPropertiesDialog(dialog);
            if (result)
            {
                dialog.AddQuantity(TestCurrent);
                dialog.AddQuantity(ZenerResistance);
                dialog.AddQuantity(BreakdownIdeality);
            }
            return result;
        }

        protected override void AfterPropertiesDialog(ComponentProperties dialog)
        {
            bool modelChanged = (dialog.SelectedModel != ComponentModel);

            base.AfterPropertiesDialog(dialog);

            if (modelChanged)
            {
                Dictionary<string, string> meta = LoadModel(ComponentModel);
                SyncUIWithModel(meta);
            }
            else
            {
                // dialog.Parameters[0] is handled by the base class (ComponentValue)
                // We read back indices 1, 2, and 3 based on the order we added them above
                if (dialog.Parameters.Count >= 4)
                {
                    TestCurrent.Val = dialog.Parameters[1].Val;
                    ZenerResistance.Val = dialog.Parameters[2].Val;
                    BreakdownIdeality.Val = dialog.Parameters[3].Val;
                }
            }
            Render();
        }

        public override Dictionary<string, string> SaveParameters()
        {
            Dictionary<string, string> parameters = base.SaveParameters();
            parameters.Add("ibv", TestCurrent.Val.ToString());
            parameters.Add("rser", ZenerResistance.Val.ToString());
            parameters.Add("nbv", BreakdownIdeality.Val.ToString());
            return parameters;
        }

        public override void LoadParameters(Dictionary<string, string> parameters)
        {
            base.LoadParameters(parameters);
            if (parameters.ContainsKey("ibv")) TestCurrent.Val = double.Parse(parameters["ibv"]);
            if (parameters.ContainsKey("rser")) ZenerResistance.Val = double.Parse(parameters["rser"]);
            if (parameters.ContainsKey("nbv")) BreakdownIdeality.Val = double.Parse(parameters["nbv"]);
        }

        public override string GenerateNetlist()
        {
            string netlist = base.GenerateNetlist(); 
            netlist = netlist.Replace("{bv}", ComponentValue.Val.ToString());
            netlist = netlist.Replace("{ibv}", TestCurrent.Val.ToString());
            netlist = netlist.Replace("{rser}", ZenerResistance.Val.ToString());
            netlist = netlist.Replace("{nbv}", BreakdownIdeality.Val.ToString());
            return netlist;
        }
    }
}