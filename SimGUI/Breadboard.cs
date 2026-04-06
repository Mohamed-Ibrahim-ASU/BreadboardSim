using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SimGUI
{
    public class Breadboard : Canvas
    {
        private static List<Point> BreadBoardHolePositions;
        private static Dictionary<Point, string> BreadBoardNets;
        
        private static Random WireRNG = new Random();
        private static int LastWireColorIndex = -1;

        public int Number = 0;
        private Circuit ParentCircuit;  
        
        public Breadboard(Circuit parent)
        {   //Obtain a list of valid hole positions
            ParentCircuit = parent; 
            SetZIndex(this, -10); //Breadboards are always the backmost element
            if (BreadBoardHolePositions == null)
            {
                BreadBoardHolePositions = new List<Point>();
                BreadBoardNets = new Dictionary<Point, string>();
                System.IO.StreamReader file = new System.IO.StreamReader("res/breadboard/breadboard-holes.csv");
                string line;
                while ((line = file.ReadLine()) != null)
                {
                    try
                    {
                        string[] splitLine = line.Split(new char[] { ',' });
                        BreadBoardHolePositions.Add(new Point(Double.Parse(splitLine[0]), Double.Parse(splitLine[1])));
                        BreadBoardNets.Add(new Point(Double.Parse(splitLine[0]), Double.Parse(splitLine[1])), splitLine[2].Trim());
                    }
                    catch { }
                }
                file.Close();
            }
            
            Image breadboardImage = new Image { Source = new BitmapImage(new Uri("res/breadboard/breadboard.png",UriKind.Relative)), Width = 742 };
            Children.Add(breadboardImage);
            
            MouseUp += HandleMouseUp;
            MouseMove += HandleMouseMove;
            
            StartedWire = false;
            StartedLeadedComponent = false;
        }

        public static bool StartedWire; 
        public static Wire NewWire; 
        public static Point WirePointA; 

        public static bool StartedLeadedComponent;
        public static LeadedComponent NewLeadedComponent;
        public static Point ComponentPointA;

        public void HandleMouseMove(object sender, MouseEventArgs e)
        {
            if ((e.LeftButton == MouseButtonState.Pressed) && (ParentCircuit.ParentWindow.SelectedTool == "SELECT"))
            {
                ParentCircuit.HandleComponentDrag(sender, e);
            }
            
            if (StartedWire)
            {
                Point actualCoord = GetAbsolutePosition(GetHoleCoord(e.GetPosition(this)));
                NewWire.EndPoint = actualCoord; 
                NewWire.Render();
            }
            else if (StartedLeadedComponent)
            {
                Point actualCoord = GetAbsolutePosition(GetHoleCoord(e.GetPosition(this)));
                NewLeadedComponent.EndPoint = actualCoord;
                NewLeadedComponent.Is2D = true;
                NewLeadedComponent.Render();
            }
        }

        public void HandleMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return;

            ParentCircuit.DeselectAll();
            bool acted = false;

            // Allows user to cancel wire or component placement
            if (e.ChangedButton == MouseButton.Right)
            {
                if (StartedWire)
                {
                    ParentCircuit.RemoveWire(NewWire);
                    StartedWire = false;
                    acted = true;
                }
                else if (StartedLeadedComponent)
                {
                    ParentCircuit.RemoveComponent(NewLeadedComponent);
                    StartedLeadedComponent = false;
                    acted = true;
                }
                
                if (acted)
                {
                    ParentCircuit.ParentWindow.UpdatePrompt();
                    e.Handled = true;
                }
                return;
            }

            // Only process left clicks for placement
            if (e.ChangedButton != MouseButton.Left) return;

            if (ParentCircuit.ParentWindow.SelectedTool == "WIRE")
            {
                if (StartedWire)
                {
                    // Using the rounded coordinate means it's extremely forgiving to click
                    Point actualCoord = GetHoleCoord(e.GetPosition(this), true);
                    if (BreadBoardHolePositions.Contains(actualCoord))
                    {
                        NewWire.EndPoint = GetAbsolutePosition(actualCoord);
                        NewWire.MakePermanentWire();
                        StartedWire = false;
                        ParentCircuit.UpdateWireColours();
                        ParentCircuit.AddUndoAction(new AddAction(NewWire, ParentCircuit));
                        acted = true;
                    }
                    // No "else" abort here so wire stays persistent.
                }
                else
                {
                    // Forgiving Start Point
                    Point actualCoord = GetHoleCoord(e.GetPosition(this), true);
                    if (BreadBoardHolePositions.Contains(actualCoord))
                    {
                        WirePointA = GetAbsolutePosition(actualCoord);
                        StartedWire = true;
                        
                        Color smartColor;
                        string startNet = GetNetAtPoint(actualCoord, ""); 
                        if (startNet.Contains("V+")) smartColor = Color.FromRgb(231, 76, 60); 
                        else if (startNet.Contains("GND")) smartColor = Color.FromRgb(44, 62, 80); 
                        else if (startNet.Contains("V-")) smartColor = Color.FromRgb(52, 152, 219); 
                        else 
                        {
                            int newIndex;
                            do { newIndex = WireRNG.Next(Constants.RandomWireColours.Length); } 
                            while (newIndex == LastWireColorIndex && Constants.RandomWireColours.Length > 1);
                            LastWireColorIndex = newIndex;
                            smartColor = Constants.RandomWireColours[newIndex];
                        }

                        NewWire = new Wire(ParentCircuit, WirePointA, WirePointA, smartColor);
                        NewWire.MakeTemporaryWire();
                        ParentCircuit.AddWire(NewWire);
                        acted = true;
                    }
                }
            }
            else if (ParentCircuit.ParentWindow.SelectedTool == "COMPONENT")
            {
                if (StartedLeadedComponent)
                {
                    Point actualCoord = GetHoleCoord(e.GetPosition(this), true);
                    if (BreadBoardHolePositions.Contains(actualCoord))
                    {
                        actualCoord = GetAbsolutePosition(actualCoord);
                        
                        double dx = actualCoord.X - ComponentPointA.X;
                        double dy = actualCoord.Y - ComponentPointA.Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy) / Constants.ScaleFactor;

                        // If it's too short, simply ignore the click.
                        if (dist >= NewLeadedComponent.MinLength)
                        {
                            NewLeadedComponent.EndPoint = actualCoord;
                            NewLeadedComponent.Is2D = true;
                            NewLeadedComponent.MakePermanent();
                            ParentCircuit.UpdateNetConnectivity();
                            StartedLeadedComponent = false;
                            ParentCircuit.AddUndoAction(new AddAction(NewLeadedComponent, ParentCircuit));
                            acted = true;
                        }
                    }
                }
                else
                {
                    ComponentData selectedComponentType = ParentCircuit.ParentWindow.GetSelectedComponent();
                    if (selectedComponentType != null)
                    {
                        // Forgiving Start Point
                        Point actualCoord = GetHoleCoord(e.GetPosition(this), true);
                        if (BreadBoardHolePositions.Contains(actualCoord))
                        {
                            Component newComponent = Component.CreateComponent(ParentCircuit, GetAbsolutePosition(actualCoord), selectedComponentType);
                            if (newComponent != null)
                            {
                                if (newComponent is LeadedComponent)
                                {
                                    ComponentPointA = GetAbsolutePosition(actualCoord);
                                    StartedLeadedComponent = true;
                                    NewLeadedComponent = (LeadedComponent)newComponent;
                                    NewLeadedComponent.MakeTemporary();
                                }
                                else ParentCircuit.AddUndoAction(new AddAction(newComponent, ParentCircuit));
                                
                                ParentCircuit.AddComponent(newComponent);
                                ParentCircuit.UpdateNetConnectivity();
                                acted = true;
                            }
                        }
                    }
                }
            }
            ParentCircuit.ParentWindow.UpdatePrompt();
            
            if (acted) e.Handled = true;
        } 

        private Point GetAbsolutePosition(Point hole)
        {
            return new Point(hole.X * Constants.ScaleFactor + Constants.OffsetX + Canvas.GetLeft(this), hole.Y * Constants.ScaleFactor + Constants.OffsetY + Canvas.GetTop(this));
        }

        private static Point GetHoleCoord(Point positionRelToBreadboard, bool rounded = true)
        {
            Point holePos = new Point();
            holePos.X = (positionRelToBreadboard.X - (double)Constants.OffsetX) / Constants.ScaleFactor;
            holePos.Y = (positionRelToBreadboard.Y - (double)Constants.OffsetY) / Constants.ScaleFactor;
            if (rounded)
            {
                holePos.X = Math.Round(holePos.X);
                holePos.Y = Math.Round(holePos.Y);
            }
            return holePos;
        }

        public static string GetNetAtPoint(Point positionRelToBreadboard, string breadBoardReference = "")
        {
            Point holeCoord = GetHoleCoord(positionRelToBreadboard);
            if (BreadBoardNets.ContainsKey(holeCoord)) return BreadBoardNets[holeCoord].Replace("%B", breadBoardReference);
            return "_invalid_" + breadBoardReference + "," + holeCoord.X + "," + holeCoord.Y;
        }

        public static Point GetPositionOnBreadboard(Point absPosition, ref int breadboardId)
        {
            int breadBoardRefX = ((int)absPosition.X - Constants.BreadboardStartX) / Constants.BreadboardSpacingX;
            int breadBoardRefY = ((int)absPosition.Y - Constants.BreadboardStartY) / Constants.BreadboardSpacingY;
            breadboardId = breadBoardRefX + breadBoardRefY * Constants.BreadboardsPerRow;
            return new Point(((int)absPosition.X - Constants.BreadboardStartX) % Constants.BreadboardSpacingX, ((int)absPosition.Y - Constants.BreadboardStartY) % Constants.BreadboardSpacingY);
        }
    }
}