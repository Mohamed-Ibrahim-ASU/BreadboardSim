using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Globalization;

namespace SimGUI
{
    public struct Trace
    {
        public Trace(int _index, Brush _brush, int _varId, string _name, int _refVarId = -1, bool _isCurrent = false) {
            TraceIndex = _index;
            TraceBrush = _brush;
            VariableId = _varId;
            ReferenceVariableId = _refVarId;
            TraceName = _name;
            IsCurrent = _isCurrent;

            LocalPerDiv = _isCurrent ? 0.01 : 2.0;
            LocalOffset_px = 0;
        }
        public readonly int TraceIndex;
        public Brush TraceBrush;
        public int VariableId;
        public int ReferenceVariableId;
        public string TraceName;
        public bool IsCurrent;

        public double LocalPerDiv;
        public double LocalOffset_px;
    }

    public partial class GraphView : Window
    {
        //Vertical grid spacing in px
        private const double VerticalGridSpacing = 60;
        //Horizontal grid spacing in px
        private const double HorizontalGridSpacing = 60;

        private const int MaxNumberOfTraces = 8;

        public Quantity SecPerDiv, VoltsPerDiv, AmpsPerDiv;
        public double PanOffsetV_px = 0;
        public double PanOffsetI_px = 0;


        private Path GraphGrid;
        
        public Trace?[] Traces = new Trace?[MaxNumberOfTraces];
        private readonly Dictionary<string, Point> _perProbeMemory = new Dictionary<string, Point>();
        private Simulator CurrentSim = null;
        
        private double TriggerAnchorTime = -double.MaxValue; 
        private double CurrentRightEdgeTime = 0; 

        //List of colours
        private readonly Brush[] TraceColours = new Brush[] {
            Brushes.Red,
            Brushes.Blue,
            Brushes.Green,
            Brushes.DarkGoldenrod,
            Brushes.DarkCyan,
            Brushes.Magenta,
            Brushes.DarkOrange,
            Brushes.SaddleBrown
        };

        private readonly double[] StandardTimeSteps = new double[] {
            10.0, 1.0, 0.5, 0.2, 0.1, 0.05, 0.02, 0.01, 0.005, 0.002, 0.001,
            0.0005, 0.0002, 0.0001, 0.00005, 0.00002, 0.00001, 0.000005, 0.000002, 0.000001,
            0.0000005, 0.0000002, 0.0000001, 0.00000005, 0.00000002, 0.00000001, 0.000000005, 0.000000002, 0.000000001
        };

        private readonly double[] StandardVoltSteps = new double[] {
            100.0, 50.0, 20.0, 10.0, 5.0, 2.0, 1.0, 0.5, 0.2, 0.1, 0.05, 0.02, 0.01, 0.005, 0.002, 0.001, 0.0005, 0.0002, 0.0001
        };

        private readonly double[] StandardAmpsSteps = new double[] {
            10.0, 5.0, 2.0, 1.0, 0.5, 0.2, 0.1, 0.05, 0.02, 0.01, 0.005, 0.002, 0.001, 0.0005, 0.0002, 0.0001, 0.00005, 0.00002, 0.00001, 0.000005, 0.000002, 0.000001
        };

        private double? cursor1Offset = null;
        private double? cursor2Offset = null;
        private DateTime _lastHoverUpdate = DateTime.MinValue;

        public GraphView()
        {
            InitializeComponent();
            GraphGrid = new Path();
            GraphGrid.StrokeThickness = 0.5;
            GraphGrid.Stroke = Brushes.Gray;
            GraphArea.Children.Add(GraphGrid);

            SecPerDiv = new Quantity("spd", "Time per div", "s");
            SecPerDiv.Val = 1;
            VoltsPerDiv = new Quantity("vpd", "Volts per div", "V");
            VoltsPerDiv.Val = 2;
            AmpsPerDiv = new Quantity("apd", "Amps per div", "A");
            AmpsPerDiv.Val = 0.01;

            for (int i = 0; i < MaxNumberOfTraces; i++)
            {
                Traces[i] = null;
            }


        }

        public void UpdateTraceMapping(string traceName, int newVarId, int newRefVarId = -1, Brush manualBrush = null)
        {
            for (int i = 0; i < MaxNumberOfTraces; i++) 
            {
                if (Traces[i].HasValue && Traces[i].Value.TraceName == traceName) 
                {
                    Trace t = Traces[i].Value; 
                    if (newVarId != -1) t.VariableId = newVarId; 
                    if (newRefVarId != -1) t.ReferenceVariableId = newRefVarId;
                    
                    if (manualBrush != null) 
                    {
                        t.TraceBrush = manualBrush;
                        try { TraceArea.UpdateTraceBrush(i, manualBrush); } catch { }
                    }
                    Traces[i] = t;
                }
            }
        }

        // --- 3-STATE LAYOUT ENGINE ---
        private void GetTraceLayout(int traceIndex, out double yMin, out double yMax, out double pixelCenter, out double perDiv)
        {
            Trace tr = Traces[traceIndex].Value;
            double h = GraphArea.ActualHeight;
            int mode = ViewModeSelector != null ? ViewModeSelector.SelectedIndex : 0;

            if (mode == 2) // Per-Probe Split (Isolated)
            {
                int activeCount = 0;
                int myActiveIndex = 0;
                for (int i = 0; i < MaxNumberOfTraces; i++) {
                    if (Traces[i].HasValue) {
                        if (i == traceIndex) myActiveIndex = activeCount;
                        activeCount++;
                    }
                }
                
                double laneH = h / Math.Max(1, activeCount);
                yMin = myActiveIndex * laneH;
                yMax = yMin + laneH;
                pixelCenter = yMin + (laneH / 2) + tr.LocalOffset_px;
                perDiv = tr.LocalPerDiv;
            }
            else if (mode == 1) // Domain Split (V / I)
            {
                if (tr.IsCurrent) {
                    yMin = h / 2; yMax = h;
                    pixelCenter = (h * 0.75) + PanOffsetI_px;
                    perDiv = AmpsPerDiv.Val;
                } else {
                    yMin = 0; yMax = h / 2;
                    pixelCenter = (h * 0.25) + PanOffsetV_px;
                    perDiv = VoltsPerDiv.Val;
                }
            }
            else // Combined (Locked)
            {
                yMin = 0; yMax = h;
                pixelCenter = (h * 0.5) + PanOffsetV_px; 
                perDiv = tr.IsCurrent ? AmpsPerDiv.Val : VoltsPerDiv.Val;
            }
        }

        public void UpdateGrid()
        {
            GeometryGroup gridLines = new GeometryGroup();
            GeometryGroup dividers = new GeometryGroup();
            int mode = ViewModeSelector != null ? ViewModeSelector.SelectedIndex : 0;
            
            HLabels.Children.Clear();
            VLabels.Children.Clear();
            CurrentVLabels.Children.Clear();

            // X-AXIS
            double lineX = 0; 
            int xIndex = 0;
            while (lineX >= -GraphArea.ActualWidth)
            {
                gridLines.Children.Add(new LineGeometry(new Point(GraphArea.ActualWidth + lineX, 0), new Point(GraphArea.ActualWidth + lineX, GraphArea.ActualHeight)));
                double tVal = -(xIndex * SecPerDiv.Val);
                Quantity currentTime = new Quantity { Val = tVal };
                string labelText = currentTime.ToString();
                if (labelText.Length > 8) labelText = tVal.ToString("G4") + "s";

                TextBlock label = new TextBlock();
                label.Text = labelText;
                label.HorizontalAlignment = HorizontalAlignment.Left;
                label.TextAlignment = TextAlignment.Center;
                label.RenderTransformOrigin = new Point(0.5, 0.5);
                label.RenderTransform = new TranslateTransform(lineX + GraphArea.ActualWidth - 10, 0);
                HLabels.Children.Add(label);
                lineX -= VerticalGridSpacing; 
                xIndex++;
            }

            double h = GraphArea.ActualHeight;

            // Y-AXIS & DIVIDERS
            if (mode == 2) 
            {
                int activeCount = 0;
                for (int i = 0; i < MaxNumberOfTraces; i++) if (Traces[i].HasValue) activeCount++;
        
                int currActive = 0;
                for (int i = 0; i < MaxNumberOfTraces; i++)
                {
                    if (Traces[i].HasValue)
                    {
                        // Declare variables before calling 'out'
                        double yMin, yMax, pCenter, pDiv;
                        GetTraceLayout(i, out yMin, out yMax, out pCenter, out pDiv);
                
                        string unit = Traces[i].Value.IsCurrent ? "A" : "V";
                        Grid targetLabelGrid = Traces[i].Value.IsCurrent ? CurrentVLabels : VLabels;
                
                        DrawYAxis(gridLines, targetLabelGrid, pCenter, yMin, yMax, pDiv, unit, Traces[i].Value.TraceBrush, Traces[i].Value.IsCurrent, true);
                
                        currActive++;
                        if (currActive < activeCount) dividers.Children.Add(new LineGeometry(new Point(0, yMax), new Point(GraphArea.ActualWidth, yMax)));
                    }
                }
            }
            else if (mode == 1) // Domain Split
            {
                dividers.Children.Add(new LineGeometry(new Point(0, h/2), new Point(GraphArea.ActualWidth, h/2)));
                
                DrawYAxis(gridLines, VLabels, (h * 0.25) + PanOffsetV_px, 0, h / 2, VoltsPerDiv.Val, "V", Brushes.Black, false, drawLines: true);
                DrawYAxis(gridLines, CurrentVLabels, (h * 0.75) + PanOffsetI_px, h / 2, h, AmpsPerDiv.Val, "A", Brushes.DarkGoldenrod, true, drawLines: true);
            }
            else // Combined
            {
                PanOffsetI_px = PanOffsetV_px; // Hardware Lock
                double center = (h * 0.5) + PanOffsetV_px;
                DrawYAxis(gridLines, VLabels, center, 0, h, VoltsPerDiv.Val, "V", Brushes.Black, false, drawLines: true);
                
                bool hasCurrent = false;
                for (int i = 0; i < MaxNumberOfTraces; i++) { if (Traces[i].HasValue && Traces[i].Value.IsCurrent) hasCurrent = true; }
    
                if (hasCurrent) DrawYAxis(gridLines, CurrentVLabels, center, 0, h, AmpsPerDiv.Val, "A", Brushes.DarkGoldenrod, true, drawLines: false);
            }

            GraphGrid.Data = gridLines;
            if (GraphDividers != null) GraphDividers.Data = dividers;
        }

        private void DrawYAxis(GeometryGroup gridLines, Grid labelGrid, double pixelCenter, double yMin, double yMax, double perDiv, string unit, Brush color, bool isCurrentLabel, bool drawLines = true)
        {
            HorizontalAlignment align = isCurrentLabel ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            double labelOffsetX = isCurrentLabel ? 5 : -5; 

            if (pixelCenter >= yMin && pixelCenter <= yMax)
            {
                
                if (drawLines) gridLines.Children.Add(new LineGeometry(new Point(0, pixelCenter), new Point(GraphArea.ActualWidth, pixelCenter)));
                TextBlock tb0 = new TextBlock { Text = "0" + unit, Foreground = color, FontWeight = FontWeights.Bold, HorizontalAlignment = align };
                tb0.RenderTransform = new TranslateTransform(labelOffsetX, pixelCenter - 10);
                labelGrid.Children.Add(tb0);
            }

            int step = 1;
            while (true)
            {
                double py = pixelCenter - (step * HorizontalGridSpacing);
                if (py < yMin) break;
                if (py <= yMax)
                {
                    if (drawLines) gridLines.Children.Add(new LineGeometry(new Point(0, py), new Point(GraphArea.ActualWidth, py)));
                    Quantity q = new Quantity { Val = step * perDiv };
                    TextBlock tb = new TextBlock { Text = q.ToString() + unit, Foreground = color, Opacity = 0.7, HorizontalAlignment = align };
                    tb.RenderTransform = new TranslateTransform(labelOffsetX, py - 10);
                    labelGrid.Children.Add(tb);
                }
                step++;
            }

            step = 1;
            while (true)
            {
                double py = pixelCenter + (step * HorizontalGridSpacing);
                if (py > yMax) break;
                if (py >= yMin)
                {
                    if (drawLines) gridLines.Children.Add(new LineGeometry(new Point(0, py), new Point(GraphArea.ActualWidth, py)));
                    Quantity q = new Quantity { Val = -step * perDiv };
                    TextBlock tb = new TextBlock { Text = q.ToString() + unit, Foreground = color, Opacity = 0.7, HorizontalAlignment = align };
                    tb.RenderTransform = new TranslateTransform(labelOffsetX, py - 10);
                    labelGrid.Children.Add(tb);
                }
                step++;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {           
            UpdateGrid();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {          
            UpdateGrid();
            PlotAll();
            UpdateCursors();
        }

        //Add a trace given name and variable ID
        //Returns a bool indicating success or failure
        //If successful a Trace struct will be set containing the ID
        public bool AddTrace(string name, int varId, ref Trace traceData)
        {
            return AddTrace(name, varId, -1, ref traceData);
        }

        public bool AddTrace(string name, int varId, int refVarId, ref Trace traceData)
        {
            for (int i = 0; i < MaxNumberOfTraces; i++)
            {
                if (!Traces[i].HasValue)
                {
                    bool isCurrent = name.StartsWith("IP"); 
                    Trace t = new Trace(i, TraceColours[i], varId, name, refVarId, isCurrent); 
            
                    // Check memory first, if not found, use the global defaults
                    if (_perProbeMemory.ContainsKey(name)) 
                    { 
                        t.LocalPerDiv = _perProbeMemory[name].X; 
                        t.LocalOffset_px = _perProbeMemory[name].Y; 
                    }
                    else 
                    {
                        t.LocalPerDiv = isCurrent ? AmpsPerDiv.Val : VoltsPerDiv.Val;
                        t.LocalOffset_px = 0;
                    }

                    traceData = t; 
                    Traces[i] = t; 
                    TraceArea.AddTrace(TraceColours[i], isCurrent); 
                    return true;
                }
            }
            return false;
        }
        //Delete all traces
        public void ResetAll()
        {
            TraceArea.Reset();
            for (int i = 0; i < MaxNumberOfTraces; i++)
            {
                Traces[i] = null;
            }

        }

        //Decode a set of (t, v) coordinates into a position to plot on the graph canvas
        private Point GetPoint(int traceIndex, double t, double v)
        {
            double x = GraphArea.ActualWidth + VerticalGridSpacing * (t / SecPerDiv.Val);

            GetTraceLayout(traceIndex, out double yMin, out double yMax, out double pixelCenter, out double perDiv);

            double py = pixelCenter - HorizontalGridSpacing * (v / perDiv);
            py = Math.Max(yMin, Math.Min(yMax, py));

            return new Point(x, py);
        }

        private void UpdateTriggerEngine()
        {
            if (CurrentSim == null || CurrentSim.GetNumberOfTicks() == 0) return;
            
            double currentTime = CurrentSim.GetCurrentTime();
            double screenTimeWidth = (GraphArea.ActualWidth / VerticalGridSpacing) * SecPerDiv.Val;
            double preTriggerTime = 2.0 * SecPerDiv.Val; 
            double postTriggerTime = screenTimeWidth - preTriggerTime;

            if (TriggerModeToggle != null && TriggerModeToggle.IsChecked == true)
            {
                int primaryTrace = -1;
                for (int i = 0; i < MaxNumberOfTraces; i++) 
                {
                    if (Traces[i].HasValue && Traces[i].Value.VariableId != -1) { primaryTrace = i; break; }
                }

                if (primaryTrace != -1)
                {
                    int varId = Traces[primaryTrace].Value.VariableId;
                    int refVarId = Traces[primaryTrace].Value.ReferenceVariableId;
                    
                    int totalTicks = CurrentSim.GetNumberOfTicks();
                    double minV = double.MaxValue; 
                    double maxV = double.MinValue;
                    double maxSearchTime = currentTime - screenTimeWidth - preTriggerTime;

                    for (int i = 0; i > -totalTicks; i--)
                    {
                        double t = CurrentSim.GetCurrentTime(i);
                        if (t < maxSearchTime) break; 
                        double v = CurrentSim.GetValueOfVar(varId, i);
                        if (refVarId != -1) v -= CurrentSim.GetValueOfVar(refVarId, i);
                        if (v < minV) minV = v; 
                        if (v > maxV) maxV = v;
                    }

                    if (maxV - minV > 0.05) 
                    {
                        double threshold = (maxV + minV) / 2.0;

                        for (int i = 0; i > -totalTicks + 1; i--)
                        {
                            double edgeCandidateTime = CurrentSim.GetCurrentTime(i);
                            if (edgeCandidateTime < maxSearchTime) break;
                            if (edgeCandidateTime + postTriggerTime <= currentTime)
                            {
                                double vNewer = CurrentSim.GetValueOfVar(varId, i);
                                if (refVarId != -1) vNewer -= CurrentSim.GetValueOfVar(refVarId, i);
                                double vOlder = CurrentSim.GetValueOfVar(varId, i - 1);
                                if (refVarId != -1) vOlder -= CurrentSim.GetValueOfVar(refVarId, i - 1);

                                if (vOlder <= threshold && vNewer > threshold)
                                {
                                    TriggerAnchorTime = edgeCandidateTime;
                                    CurrentRightEdgeTime = TriggerAnchorTime + postTriggerTime;
                                    return; 
                                }
                            }
                        }
                    }
                }
            }
            else CurrentRightEdgeTime = currentTime; 
        }
        
    // Plot a specific trace (specify by zero-based index)
    public void PlotTrace(int n) {
    if (!Traces[n].HasValue || Traces[n].Value.VariableId == -1) return;
    int totalTicks = CurrentSim.GetNumberOfTicks();
    if (totalTicks == 0) return;

    // 1. Capture all UI/Layout variables here (Main Thread)
    GetTraceLayout(n, out double yMin, out double yMax, out double pCenter, out double pDiv);
    double actualWidth = GraphArea.ActualWidth;
    double timeScale = VerticalGridSpacing / SecPerDiv.Val;
    double valScale = HorizontalGridSpacing / pDiv;
    double scW = actualWidth * SecPerDiv.Val / VerticalGridSpacing;
    double leftT = CurrentRightEdgeTime - scW;
    double currentRightEdge = CurrentRightEdgeTime;

    int varId = Traces[n].Value.VariableId; 
    int refId = Traces[n].Value.ReferenceVariableId;

    int startIdx = GetTickFromAbsoluteTime(CurrentRightEdgeTime); 
    if (startIdx < 0) startIdx += 1;
    int endIdx = GetTickFromAbsoluteTime(leftT - SecPerDiv.Val);
    
    int ticksInWindow = startIdx - endIdx; 
    int stepSize = (ticksInWindow > 8000) ? ticksInWindow / 8000 : 1;

    // 2. Start the background math
    Task.Factory.StartNew(() => 
    {
        List<Point> bgPts = new List<Point>((ticksInWindow / stepSize) * 2 + 10);

        for (int i = startIdx; i >= endIdx; i -= stepSize) 
        {
            if (i < -totalTicks + 1) break;

            if (stepSize > 2) 
            {
                double minV = double.MaxValue; double maxV = double.MinValue;
                double tMid = CurrentSim.GetCurrentTime(i - (stepSize/2)); 
                int chunkEnd = Math.Max(i - stepSize, endIdx);
                for (int j = i; j > chunkEnd; j--) 
                {
                    double v = CurrentSim.GetValueOfVar(varId, j);
                    if (refId != -1) v -= CurrentSim.GetValueOfVar(refId, j);
                    if (double.IsNaN(v)) continue;
                    if (v < minV) minV = v; if (v > maxV) maxV = v;
                }
                double rawX = actualWidth + (tMid - currentRightEdge) * timeScale;
                double pyMax = pCenter - (maxV * valScale);
                double pyMin = pCenter - (minV * valScale);
                if (pyMax < yMin) pyMax = yMin; else if (pyMax > yMax) pyMax = yMax;
                if (pyMin < yMin) pyMin = yMin; else if (pyMin > yMax) pyMin = yMax;
                bgPts.Add(new Point(rawX, pyMax)); bgPts.Add(new Point(rawX, pyMin));
            }
            else 
            {
                double t = CurrentSim.GetCurrentTime(i);
                double v = CurrentSim.GetValueOfVar(varId, i);
                if (refId != -1) v -= CurrentSim.GetValueOfVar(refId, i);
    
                // NaN sentinel = restart boundary. Add a gap marker and skip this point.
                if (double.IsNaN(v))
                {
                    bgPts.Add(new Point(double.NaN, double.NaN));
                    continue;
                }
    
                double rawX = actualWidth + (t - currentRightEdge) * timeScale;
                double py = pCenter - (v * valScale);
                if (py < yMin) py = yMin; else if (py > yMax) py = yMax;
                bgPts.Add(new Point(rawX, py));
            }
        }
        return bgPts;
    })
    .ContinueWith(t => 
    {
        // Draw the points on the UI Thread
        TraceArea.SetTracePoints(n, t.Result);
    }, TaskScheduler.FromCurrentSynchronizationContext()); 
}
        
        //Called when a simulation is started
        public void StartSim(Simulator s)
        {
            CurrentSim = s;
        }

        //Replot all traces
        public void PlotAll()
        {
            if (CurrentSim == null) return;
            if (CurrentSim.GetNumberOfTicks() >= 5)
            {
                UpdateTriggerEngine();
                for (int i = 0; i < MaxNumberOfTraces; i++)
                {
                    if (Traces[i].HasValue)
                    {
                        PlotTrace(i);
                    }
                }
            }
            GraphArea.InvalidateVisual();
            UpdateCursors();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            GraphSettings gs = new GraphSettings();
            double legacyVOffset = -PanOffsetV_px * (VoltsPerDiv.Val / HorizontalGridSpacing);
            double legacyIOffset = -PanOffsetI_px * (AmpsPerDiv.Val / HorizontalGridSpacing);
            
            gs.SetSettings(VoltsPerDiv.Val, legacyVOffset, SecPerDiv.Val, AmpsPerDiv.Val, legacyIOffset);
            
            if (gs.ShowDialog() == true) 
            { 
                SecPerDiv = gs.GetSecPerDiv(); 
                VoltsPerDiv = gs.GetVoltsPerDiv(); 
                AmpsPerDiv = gs.GetAmpsPerDiv();
                PanOffsetV_px = -gs.GetVoltsOffset() * (HorizontalGridSpacing / VoltsPerDiv.Val);
                PanOffsetI_px = -gs.GetAmpsOffset() * (HorizontalGridSpacing / AmpsPerDiv.Val);
                UpdateGrid(); PlotAll(); 
            }
        }
        private void HelpToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (FloatingHelpHUD != null)
            {
                FloatingHelpHUD.Visibility = (HelpToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void PinToggle_Changed(object sender, RoutedEventArgs e)
        {
            this.Topmost = (PinToggle.IsChecked == true);
        }
        private void AdvancedToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (AdvancedPanel != null)
            {
                AdvancedPanel.Visibility = (AdvancedToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ViewModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                UpdateGrid();
                PlotAll();
            }
        }

        private double GetNextStandardStep(double currentVal, double[] steps, bool zoomIn)
        {
            int closestIndex = 0; double minDiff = double.MaxValue;
            for (int i = 0; i < steps.Length; i++) 
            { 
                double diff = Math.Abs(steps[i] - currentVal); 
                if (diff < minDiff) { minDiff = diff; closestIndex = i; } 
            }
            int nextIndex = zoomIn ? closestIndex + 1 : closestIndex - 1;
            if (nextIndex < 0) nextIndex = 0; 
            if (nextIndex >= steps.Length) nextIndex = steps.Length - 1;
            return steps[nextIndex];
        }

        // --- CONTEXT-AWARE CONTROLS ---
        private void HoverOverlay_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            int mode = ViewModeSelector != null ? ViewModeSelector.SelectedIndex : 0;
            bool zoomIn = e.Delta > 0; 
            
            double mouseY = e.GetPosition(HoverOverlay).Y;
            double h = HoverOverlay.ActualHeight;

            if (mode == 2) // Per-Probe Smart Targeting
            {
                int activeCount = 0;
                for (int i = 0; i < MaxNumberOfTraces; i++) if (Traces[i].HasValue) activeCount++;
                if (activeCount == 0) return;
                
                int hoverLane = (int)(mouseY / (h / activeCount));
                if (hoverLane >= activeCount) hoverLane = activeCount - 1;
                
                int targetTrace = -1;
                int curr = 0;
                for (int i = 0; i < MaxNumberOfTraces; i++) {
                    if (Traces[i].HasValue) {
                        if (curr == hoverLane) { targetTrace = i; break; }
                        curr++;
                    }
                }
                
                if (targetTrace != -1)
                {
                    Trace t = Traces[targetTrace].Value;
                    if (Keyboard.Modifiers == ModifierKeys.Control) {
                        SecPerDiv.Val = GetNextStandardStep(SecPerDiv.Val, StandardTimeSteps, zoomIn);
                    }
                    else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) || Keyboard.Modifiers == ModifierKeys.Shift) {
                        
                        // Calculate mouse-centered zoom offset
                        double oldPerDiv = t.LocalPerDiv;
                        double[] steps = t.IsCurrent ? StandardAmpsSteps : StandardVoltSteps;
                        double newPerDiv = GetNextStandardStep(oldPerDiv, steps, zoomIn);

                        if (oldPerDiv != newPerDiv)
                        {
                            double laneH = h / activeCount;
                            double yMin = hoverLane * laneH;
                            double oldCenter = yMin + (laneH / 2.0) + t.LocalOffset_px;

                            // Shift the offset to anchor the trace exactly at the mouse Y coordinate
                            t.LocalOffset_px += (oldCenter - mouseY) * ((oldPerDiv / newPerDiv) - 1.0);
                            t.LocalPerDiv = newPerDiv;
                        }
                    }
                    else {
                        t.LocalOffset_px += zoomIn ? 20 : -20;
                    }
                    Traces[targetTrace] = t;
                    _perProbeMemory[t.TraceName] = new Point(t.LocalPerDiv, t.LocalOffset_px);
                }
            }
            else // Combined & Domain Split
            {
                bool mouseOnBottom = (mode == 1) && (mouseY > h / 2);

                if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) {
                    VoltsPerDiv.Val = GetNextStandardStep(VoltsPerDiv.Val, StandardVoltSteps, zoomIn);
                }
                else if (Keyboard.Modifiers == ModifierKeys.Shift) {
                    AmpsPerDiv.Val = GetNextStandardStep(AmpsPerDiv.Val, StandardAmpsSteps, zoomIn);
                }
                else if (Keyboard.Modifiers == ModifierKeys.Control) {
                    SecPerDiv.Val = GetNextStandardStep(SecPerDiv.Val, StandardTimeSteps, zoomIn);
                }
                else { 
                    double panStep = zoomIn ? 20 : -20; 
                    if (mode == 1 && mouseOnBottom) PanOffsetI_px += panStep;
                    else PanOffsetV_px += panStep;
                }
            }
            
            UpdateGrid(); PlotAll();
        }
        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentSim == null || CurrentSim.GetNumberOfTicks() == 0) return;
            SaveFileDialog dlg = new SaveFileDialog { FileName = "SimulationData", DefaultExt = ".csv", Filter = "CSV Files (.csv)|*.csv" };
            if (dlg.ShowDialog() == true)
            {
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(dlg.FileName))
                {
                    string header = "Time (s)";
                    for (int n = 0; n < MaxNumberOfTraces; n++) 
                    {
                        if (Traces[n].HasValue && Traces[n].Value.VariableId != -1) 
                        {
                            string unit = Traces[n].Value.IsCurrent ? "A" : "V";
                            header += $", {Traces[n].Value.TraceName} ({unit})";
                        }
                    }
                    writer.WriteLine(header);
                    
                    double screenTimeWidth = (GraphArea.ActualWidth / VerticalGridSpacing) * SecPerDiv.Val;
                    double leftEdgeTime = CurrentRightEdgeTime - screenTimeWidth;
                    
                    int startTick = GetTickFromAbsoluteTime(leftEdgeTime);
                    int endTick = GetTickFromAbsoluteTime(CurrentRightEdgeTime);

                    int minTick = Math.Min(startTick, endTick);
                    int maxTick = Math.Max(startTick, endTick);

                    for (int i = minTick; i <= maxTick; i++)
                    {
                        if (i > 0) continue;
                        string line = (CurrentSim.GetCurrentTime(i)).ToString("G5", CultureInfo.InvariantCulture);
                        for (int n = 0; n < MaxNumberOfTraces; n++) 
                        {
                            if (Traces[n].HasValue && Traces[n].Value.VariableId != -1) 
                            {
                                double v = CurrentSim.GetValueOfVar(Traces[n].Value.VariableId, i);
                                if (Traces[n].Value.ReferenceVariableId != -1) v -= CurrentSim.GetValueOfVar(Traces[n].Value.ReferenceVariableId, i);
                                line += $", {v.ToString("G5", CultureInfo.InvariantCulture)}";
                            }
                        }
                        writer.WriteLine(line);
                    }
                }
            }
        }

        private int GetTickFromAbsoluteTime(double targetAbsoluteTime)
        {
            if (CurrentSim == null) return 0;
            int totalTicks = CurrentSim.GetNumberOfTicks();
            if (totalTicks == 0) return 0;
            
            int low = 0;
            int high = totalTicks - 1;
            int closestTick = 0;
            double smallestDiff = double.MaxValue;

            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                double midTime = CurrentSim.GetCurrentTime(-mid);
                
                double diff = Math.Abs(midTime - targetAbsoluteTime);
                if (diff < smallestDiff)
                {
                    smallestDiff = diff;
                    closestTick = -mid;
                }

                if (midTime == targetAbsoluteTime) return -mid;
                if (midTime < targetAbsoluteTime) high = mid - 1;
                else low = mid + 1;
            }
            return closestTick;
        }

        private void HoverModeToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (HoverModeToggle.IsChecked != true && CrosshairV != null)
            {
                CrosshairV.Visibility = Visibility.Hidden;
                HoverTooltip.Visibility = Visibility.Hidden;
            }
        }

        private void HoverOverlay_MouseEnter(object sender, MouseEventArgs e)
        {
            if (HoverModeToggle.IsChecked == true)
            {
                CrosshairV.Visibility = Visibility.Visible;
                HoverTooltip.Visibility = Visibility.Visible;
            }
        }

        private void HoverOverlay_MouseLeave(object sender, MouseEventArgs e)
        {
            CrosshairV.Visibility = Visibility.Hidden;
            HoverTooltip.Visibility = Visibility.Hidden;
        }

        private void HoverOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (HoverModeToggle.IsChecked != true) return;
            
            
            
            Point pos = e.GetPosition(HoverOverlay);

            
            
            // Crosshair position is cheap, update every frame
            CrosshairV.X1 = pos.X;
            CrosshairV.X2 = pos.X;
            CrosshairV.Y1 = 0;
            CrosshairV.Y2 = HoverOverlay.ActualHeight;
            Canvas.SetLeft(HoverTooltip, pos.X + 15);
            Canvas.SetTop(HoverTooltip, pos.Y + 15);

          // Text rebuild is expensive, throttle to 30Hz
            if ((DateTime.Now - _lastHoverUpdate).TotalMilliseconds < 33) return;
            _lastHoverUpdate = DateTime.Now;

            double targetTime = CurrentRightEdgeTime + (pos.X - HoverOverlay.ActualWidth) * (SecPerDiv.Val / VerticalGridSpacing);
            Quantity tQ = new Quantity { Val = targetTime };

            HoverText.Inlines.Clear(); 
            HoverText.Inlines.Add(new Run($"Time: {tQ.ToString()}\n") { Foreground = Brushes.Black });

            if (CurrentSim != null && CurrentSim.GetNumberOfTicks() > 0)
            {
                int bestTick = GetTickFromAbsoluteTime(targetTime);
                for (int n = 0; n < MaxNumberOfTraces; n++)
                {
                    if (Traces[n].HasValue && Traces[n].Value.VariableId != -1)
                    {
                        double v = CurrentSim.GetValueOfVar(Traces[n].Value.VariableId, bestTick);
                        if (Traces[n].Value.ReferenceVariableId != -1) v -= CurrentSim.GetValueOfVar(Traces[n].Value.ReferenceVariableId, bestTick);
                
                        string unit = Traces[n].Value.IsCurrent ? "A" : "V";
                        Quantity vQ = new Quantity { Val = v };
                        HoverText.Inlines.Add(new Run($"{Traces[n].Value.TraceName}: {vQ.ToString()}{unit}\n") { Foreground = Traces[n].Value.TraceBrush, FontWeight = FontWeights.Bold });
                    }
                }
            }
        }

        private void CursorModeToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (CursorModeToggle.IsChecked != true && Cursor1Line != null)
            {
                cursor1Offset = null;
                cursor2Offset = null;
                Cursor1Line.Visibility = Visibility.Hidden;
                Cursor2Line.Visibility = Visibility.Hidden;
                CursorTooltip.Visibility = Visibility.Hidden;
            }
        }

        private void HoverOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (CursorModeToggle.IsChecked != true) return;
            Point pos = e.GetPosition(HoverOverlay);
            double targetOffset = (pos.X - HoverOverlay.ActualWidth) * (SecPerDiv.Val / VerticalGridSpacing);
            if (cursor1Offset == null || (cursor1Offset != null && cursor2Offset != null)) { cursor1Offset = targetOffset; cursor2Offset = null; } else cursor2Offset = targetOffset; 
            UpdateCursors();
        }

        private void UpdateCursors()
        {
            if (cursor1Offset == null) return;
            
            double x1 = GraphArea.ActualWidth + VerticalGridSpacing * (cursor1Offset.Value / SecPerDiv.Val);
            Cursor1Line.X1 = x1;
            Cursor1Line.X2 = x1;
            Cursor1Line.Y1 = 0;
            Cursor1Line.Y2 = HoverOverlay.ActualHeight;
            Cursor1Line.Visibility = Visibility.Visible;

            if (cursor2Offset == null)
            {
                Cursor2Line.Visibility = Visibility.Hidden;
                CursorTooltip.Visibility = Visibility.Hidden;
                return;
            }

            double x2 = GraphArea.ActualWidth + VerticalGridSpacing * (cursor2Offset.Value / SecPerDiv.Val);
            Cursor2Line.X1 = x2;
            Cursor2Line.X2 = x2;
            Cursor2Line.Y1 = 0;
            Cursor2Line.Y2 = HoverOverlay.ActualHeight;
            Cursor2Line.Visibility = Visibility.Visible;

            double dt = Math.Abs(cursor2Offset.Value - cursor1Offset.Value);
            Quantity dtQ = new Quantity { Val = dt }; 
            Quantity fQ = new Quantity { Val = dt > 0 ? 1.0 / dt : 0 };

            CursorText.Inlines.Clear();
            CursorText.Inlines.Add(new Run($"ΔT: {dtQ.ToString()}  |  Freq: {fQ.ToString()}\n") { FontWeight = FontWeights.Bold, Foreground = Brushes.White });

            if (CurrentSim != null && CurrentSim.GetNumberOfTicks() > 0)
            {
                double absTime1 = CurrentRightEdgeTime + cursor1Offset.Value;
                double absTime2 = CurrentRightEdgeTime + cursor2Offset.Value;
                int startTick = Math.Min(GetTickFromAbsoluteTime(absTime1), GetTickFromAbsoluteTime(absTime2)); 
                int endTick = Math.Max(GetTickFromAbsoluteTime(absTime1), GetTickFromAbsoluteTime(absTime2));

                for (int n = 0; n < MaxNumberOfTraces; n++)
                {
                    if (Traces[n].HasValue && Traces[n].Value.VariableId != -1)
                    {
                        double minV = double.MaxValue; double maxV = double.MinValue;
                        int varId = Traces[n].Value.VariableId;
                        int refVarId = Traces[n].Value.ReferenceVariableId;
                        string unit = Traces[n].Value.IsCurrent ? "A" : "V";

                        double v1 = CurrentSim.GetValueOfVar(varId, GetTickFromAbsoluteTime(absTime1));
                        if (refVarId != -1) v1 -= CurrentSim.GetValueOfVar(refVarId, GetTickFromAbsoluteTime(absTime1));
                        
                        double v2 = CurrentSim.GetValueOfVar(varId, GetTickFromAbsoluteTime(absTime2));
                        if (refVarId != -1) v2 -= CurrentSim.GetValueOfVar(refVarId, GetTickFromAbsoluteTime(absTime2));

                        for (int i = startTick; i <= endTick; i++) 
                        { 
                            if (i > 0) continue; 
                            double v = CurrentSim.GetValueOfVar(varId, i); 
                            if (refVarId != -1) v -= CurrentSim.GetValueOfVar(refVarId, i);
                            if (v < minV) minV = v; if (v > maxV) maxV = v; 
                        }

                        Quantity dvQ = new Quantity { Val = Math.Abs(v2 - v1) }; 
                        Quantity maxQ = new Quantity { Val = maxV }; 
                        Quantity minQ = new Quantity { Val = minV }; 
                        Quantity vppQ = new Quantity { Val = maxV - minV };

                        CursorText.Inlines.Add(new Run($"\n{Traces[n].Value.TraceName}:\n") { Foreground = Traces[n].Value.TraceBrush, FontWeight = FontWeights.Bold });
                        CursorText.Inlines.Add(new Run($"  Δ{unit}: {dvQ.ToString()}{unit} | {unit}pp: {vppQ.ToString()}{unit}\n  Max:{maxQ.ToString()}{unit} | Min:{minQ.ToString()}{unit}\n") { Foreground = Brushes.LightGray });
                    }
                }
            }
            CursorTooltip.Visibility = Visibility.Visible;
        }
    }
}