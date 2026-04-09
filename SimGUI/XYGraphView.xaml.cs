using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace SimGUI
{
    public partial class XYGraphView : Window
    {
        // ── Constants ─────────────────────────────────────────────────────────────
        private const int    MaxTraces        = 4;
        private const double GridSpacingPx    = 60;
        private const double RedrawIntervalMs = 120; 
        private const int    MinTicksToPlot   = 80;
        private const int    DrawWindowTicks  = 800;

        private static readonly Brush[] TraceColours =
        {
            Brushes.Red, Brushes.Blue, Brushes.Green, Brushes.DarkGoldenrod
        };

        // ── State ─────────────────────────────────────────────────────────────────
        public bool ForceClose = false; 
        private Simulator _sim;
        
        public enum AxisUnit { Volts, Amps }

        private AxisUnit _xUnit = AxisUnit.Volts;
        private struct XYTrace
        {
            public string ProbeName;
            public int    InputVarId1;
            public int    InputVarId2;
            public int    OutputVarId;
            public bool   InvertY;     
            public AxisUnit XUnit; 
            public AxisUnit YUnit; 
        }

        private readonly XYTrace[] _traces = new XYTrace[MaxTraces];
        private int _traceCount = 0;
        private int _epoch = 0;
        private int _pendingRenders = 0;

        private double _xMin = -5, _xMax = 5;
        private double _yMinV = -5, _yMaxV = 5; // Voltage Y-Axis
        private double _yMinA = -5, _yMaxA = 5; // Current Y-Axis
        private bool _hasVoltsY = false;
        private bool _hasAmpsY = false;

        private readonly Path[] _paths = new Path[MaxTraces];

        private bool     _hasAutoScaled = false;
        private DateTime _lastRedraw    = DateTime.MinValue;

        // Panning State
        private bool _isPanning = false;
        private Point _panStartMouse;
        private double _panStartXMin, _panStartXMax;
        private double _panStartYMinV, _panStartYMaxV, _panStartYMinA, _panStartYMaxA;

        // ── Construction ──────────────────────────────────────────────────────────
        public XYGraphView()
        {
            InitializeComponent();
            for (int i = 0; i < MaxTraces; i++)
            {
                _paths[i] = new Path
                {
                    Stroke             = TraceColours[i],
                    StrokeThickness    = 2.0, 
                    StrokeLineJoin     = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round, 
                    StrokeEndLineCap   = PenLineCap.Round, 
                    IsHitTestVisible   = false
                };
                TraceCanvas.Children.Add(_paths[i]);
            }
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.A) AutoScaleAndPlot();
            };
        }
        

        // ── Public API ────────────────────────────────────────────────────────────

        public void StartSim(Simulator sim) { _sim = sim; }

        public Brush AddXYTrace(string probeName, int inputVarId1, int inputVarId2, int outputVarId, Brush preferredColour = null, AxisUnit xUnit = AxisUnit.Volts, AxisUnit yUnit = AxisUnit.Volts, bool invertY = false)
        {
            if (_traceCount >= MaxTraces) return Brushes.Gray;

            Brush col = (preferredColour != null && preferredColour != Brushes.Transparent) 
                ? preferredColour : TraceColours[_traceCount];

            _traces[_traceCount] = new XYTrace
            {
                ProbeName   = probeName,
                InputVarId1 = inputVarId1,
                InputVarId2 = inputVarId2,
                OutputVarId = outputVarId,
                InvertY     = invertY,
                XUnit       = xUnit,
                YUnit       = yUnit
            };

            if (yUnit == AxisUnit.Volts) _hasVoltsY = true;
            if (yUnit == AxisUnit.Amps) _hasAmpsY = true;

            _paths[_traceCount].Stroke = col;
            _traceCount++;
            return col;
        }
        public void UpdateXYTrace(string probeName, int newInId1, int newInId2, int newOutId)
        {
            for (int i = 0; i < _traceCount; i++)
            {
                if (_traces[i].ProbeName != probeName) continue;
                _traces[i].InputVarId1 = newInId1;
                _traces[i].InputVarId2 = newInId2;
                _traces[i].OutputVarId = newOutId;
                return;
            }
        }

        public void UpdateTraceColour(string probeName, Brush colour)
        {
            for (int i = 0; i < _traceCount; i++)
            {
                if (_traces[i].ProbeName != probeName) continue;
                _paths[i].Stroke = colour;
                return;
            }
        }

        public void ResetAll()
        {
            _epoch++;
            _traceCount    = 0;
            _hasAutoScaled = false;
            for (int i = 0; i < MaxTraces; i++) _paths[i].Data = null; 
            VLabels.Children.Clear(); HLabels.Children.Clear();
            NoDataOverlay.Visibility = Visibility.Visible;
        }

        // ── Render Pipeline (UI Safe + Background GPU Geometry) ───────────────────

       public void PlotAll()
{
    if (_sim == null || _traceCount == 0) return;
    int totalTicks = _sim.GetNumberOfTicks();
    if (totalTicks < MinTicksToPlot) return;
    if ((DateTime.Now - _lastRedraw).TotalMilliseconds < RedrawIntervalMs) return;
    if (_pendingRenders > 0) return;
    _lastRedraw = DateTime.Now;
    
    if (!_hasAutoScaled && totalTicks >= 500)
    {
        if (ComputeAutoScale())
        {
            _hasAutoScaled = true;
            UpdateGrid();
        }
    }
    
    bool anyValid = false;
    for (int n = 0; n < _traceCount; n++)
        if (_traces[n].InputVarId1 >= 0 && _traces[n].OutputVarId >= 0) { anyValid = true; break; }

    NoDataOverlay.Visibility = anyValid ? Visibility.Collapsed : Visibility.Visible;
    if (!anyValid) return;

    int rawWindow  = Math.Min(totalTicks, DrawWindowTicks);
    int drawWindow = Math.Max(0, rawWindow - 10);

    double plotW = PlotArea.ActualWidth, plotH = PlotArea.ActualHeight;
    if (plotW < 1 || plotH < 1) return;

    double xMin = _xMin, xMax = _xMax;
    double xR = Math.Max(xMax - xMin, 1e-12);

    float[][] pxData = new float[_traceCount][];  
    bool[] active    = new bool[_traceCount];

    for (int n = 0; n < _traceCount; n++)
    {
        int inId1 = _traces[n].InputVarId1;
        int inId2 = _traces[n].InputVarId2;
        int outId = _traces[n].OutputVarId;
        if (inId1 < 0 || outId < 0) { _paths[n].Data = null; continue; }      
        
        double yMin = (_traces[n].YUnit == AxisUnit.Amps) ? _yMinA : _yMinV;
        double yMax = (_traces[n].YUnit == AxisUnit.Amps) ? _yMaxA : _yMaxV;
        double yR = Math.Max(yMax - yMin, 1e-12);
        
        active[n] = true;
        var buf = new float[drawWindow * 2];
        int write = 0;

        try
        {
            for (int i = 0; i < drawWindow; i++)
            {
                int tick = -(drawWindow - i);
                
                // DIFFERENTIAL MATH INJECTION
                double vIn = _sim.GetValueOfVar(inId1, tick);
                if (inId2 >= 0) vIn -= _sim.GetValueOfVar(inId2, tick);
                
                double vOut = _sim.GetValueOfVar(outId, tick);
                
                // POLARITY INJECTION
                if (_traces[n].InvertY) vOut = -vOut;
                
                if (double.IsNaN(vIn) || double.IsInfinity(vIn) ||
                    double.IsNaN(vOut) || double.IsInfinity(vOut)) continue;

                if (vIn  < xMin - xR || vIn  > xMax + xR) continue;
                if (vOut < yMin - yR || vOut > yMax + yR) continue;
                
                buf[write]     = (float)((vIn  - xMin) / xR * plotW);
                buf[write + 1] = (float)(plotH - (vOut - yMin) / yR * plotH);
                write += 2;
            }
        }
        catch
        {
            _traces[n].InputVarId1 = -1;
            _traces[n].OutputVarId = -1;
            _paths[n].Data = null;
            active[n] = false;
        }
        if (write < buf.Length) Array.Resize(ref buf, write);
        pxData[n] = buf;
    }

    System.Threading.Interlocked.Increment(ref _pendingRenders);
    int threadEpoch = _epoch;

    Task.Factory.StartNew(() =>
    {
        var geometries = new StreamGeometry[_traceCount];
        for (int n = 0; n < _traceCount; n++)
        {
            if (!active[n] || pxData[n] == null || pxData[n].Length < 2) continue;

            float[] buf    = pxData[n];
            int pointCount = buf.Length / 2;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                bool first     = true;
                float lastPx   = float.NaN, lastPy = float.NaN;
                float clampMinX = -10f, clampMaxX = (float)(plotW  + 10);
                float clampMinY = -10f, clampMaxY = (float)(plotH  + 10);

                for (int i = 0; i < pointCount; i++)
                {
                    float px = buf[i * 2],     py = buf[i * 2 + 1];

                    if (px < clampMinX) px = clampMinX;
                    else if (px > clampMaxX) px = clampMaxX;
                    if (py < clampMinY) py = clampMinY;
                    else if (py > clampMaxY) py = clampMaxY;

                    if (!first)
                    {
                        float ddx = px - lastPx, ddy = py - lastPy;
                        if (ddx * ddx + ddy * ddy < 1f) continue;
                    }

                    var pt = new Point(px, py);
                    if (first)
                    {
                        ctx.BeginFigure(pt, false, false);
                        ctx.LineTo(new Point(px + 0.001, py), true, true);
                        first = false;
                    }
                    else
                    {
                        ctx.LineTo(pt, true, true);
                    }
                    lastPx = px; lastPy = py;
                }
            }
            geo.Freeze();
            geometries[n] = geo;
        }
        return geometries;
    })
    .ContinueWith(task =>
    {
        System.Threading.Interlocked.Decrement(ref _pendingRenders);
        if (!task.IsFaulted && threadEpoch == _epoch)
        {
            for (int n = 0; n < _traceCount; n++)
                if (active[n] && task.Result[n] != null)
                    _paths[n].Data = task.Result[n];
        }
    }, TaskScheduler.FromCurrentSynchronizationContext());
}

        // ── Auto-scale ────────────────────────────────────────────────────────────

        private bool ComputeAutoScale()
{
    if (_sim == null || _traceCount == 0) return false;
    int total = _sim.GetNumberOfTicks();
    if (total < 2) return false;

    double xlo = double.MaxValue, xhi = double.MinValue;
    double yloV = double.MaxValue, yhiV = double.MinValue;
    double yloA = double.MaxValue, yhiA = double.MinValue;
    bool got = false;

    int boundsWindow = Math.Min(total, DrawWindowTicks);

    for (int n = 0; n < _traceCount; n++)
    {
        int inId1 = _traces[n].InputVarId1;
        int inId2 = _traces[n].InputVarId2;
        int outId = _traces[n].OutputVarId;
        if (inId1 < 0 || outId < 0) continue;
        
        try
        {
            for (int i = 0; i < boundsWindow; i++)
            {
                int tick = -(boundsWindow - i); 
                double vIn = _sim.GetValueOfVar(inId1, tick);
                if (inId2 >= 0) vIn -= _sim.GetValueOfVar(inId2, tick);
                
                double vOut = _sim.GetValueOfVar(outId, tick);
                if (_traces[n].InvertY) vOut = -vOut;

                if (double.IsNaN(vIn) || double.IsNaN(vOut) || double.IsInfinity(vIn)) continue;
                
                if (vIn < xlo) xlo = vIn;  
                if (vIn > xhi) xhi = vIn;

                if (_traces[n].YUnit == AxisUnit.Volts) {
                    if (vOut < yloV) yloV = vOut; 
                    if (vOut > yhiV) yhiV = vOut;
                } else {
                    if (vOut < yloA) yloA = vOut; 
                    if (vOut > yhiA) yhiA = vOut;
                }
                got = true;
            }
        }
        catch { }
    }
    if (!got) return false;

    double w = PlotArea.ActualWidth, h = PlotArea.ActualHeight;
    if (w < 1 || h < 1) return false;

    double xDivs = w / GridSpacingPx;
    double yDivs = h / GridSpacingPx;

    // Scale X
    double xMid = (xlo + xhi) / 2;
    double xVoltsPerDiv = NiceVoltsPerDiv(Math.Max(xhi - xlo, 1e-9) / (0.6 * xDivs));
    _xMin = xMid - xVoltsPerDiv * xDivs / 2;
    _xMax = xMid + xVoltsPerDiv * xDivs / 2;

    // Scale Y (Volts)
    if (_hasVoltsY && yhiV >= yloV) {
        double yMidV = (yloV + yhiV) / 2;
        double yVoltsPerDiv = NiceVoltsPerDiv(Math.Max(yhiV - yloV, 1e-9) / (0.6 * yDivs));
        _yMinV = yMidV - yVoltsPerDiv * yDivs / 2;
        _yMaxV = yMidV + yVoltsPerDiv * yDivs / 2;
    }

    // Scale Y (Amps)
    if (_hasAmpsY && yhiA >= yloA) {
        double yMidA = (yloA + yhiA) / 2;
        double yAmpsPerDiv = NiceVoltsPerDiv(Math.Max(yhiA - yloA, 1e-9) / (0.6 * yDivs));

        // Start with standard isolated scale
        _yMinA = yMidA - yAmpsPerDiv * yDivs / 2;
        _yMaxA = yMidA + yAmpsPerDiv * yDivs / 2;

        // ── GRID LOCK ALGORITHM ──
        if (_hasVoltsY) {
            // 1. Get the exact physical pixel spacing of the Master (Volts) grid
            double yVoltsPerPx = (_yMaxV - _yMinV) / h;
            double yGridPxV = NiceVoltsPerDiv(60 * yVoltsPerPx) / yVoltsPerPx;
            double yCenterPxV = h - (0 - _yMinV) / yVoltsPerPx;

            // 2. FORCE the Guest (Amps) scale to match this exact pixel spacing
            double newYAmpsPerPx = yAmpsPerDiv / yGridPxV;
            _yMinA = yMidA - newYAmpsPerPx * (h / 2);
            _yMaxA = yMidA + newYAmpsPerPx * (h / 2);

            // 3. Shift the Guest bounds so 0A snaps exactly onto a Master grid line
            double yCenterPxA = h - (0 - _yMinA) / newYAmpsPerPx;
            double targetCenterA = yCenterPxV + Math.Round((yCenterPxA - yCenterPxV) / yGridPxV) * yGridPxV;
            double shiftPx = targetCenterA - yCenterPxA;

            _yMinA -= shiftPx * newYAmpsPerPx;
            _yMaxA -= shiftPx * newYAmpsPerPx;
        }
    }

    return true;
}

        private static double NiceVoltsPerDiv(double raw)
        {
            if (raw <= 0) return 0.000000001; // 1nV floor
            
            double[] steps = { 
                0.000000001, 0.000000002, 0.000000005, // 1nV, 2nV, 5nV
                0.00000001,  0.00000002,  0.00000005,  // 10nV...
                0.0000001,   0.0000002,   0.0000005,   // 100nV...
                0.000001,    0.000002,    0.000005,    // 1uV...
                0.00001,     0.00002,     0.00005,     // 10uV...
                0.0001,      0.0002,      0.0005,      // 100uV...
                0.001, 0.002, 0.005, 0.01, 0.02, 0.05,
                0.1,   0.2,   0.5,   1,    2,    5,   10, 20, 50, 100 
            };
    
            foreach (double v in steps) if (v >= raw) return v;
            return 100;
        }

        private void AutoScaleAndPlot()
        {
            if (_sim == null) return;
            _hasAutoScaled = true;
            if (ComputeAutoScale()) { UpdateGrid(); _lastRedraw = DateTime.MinValue; PlotAll(); }
        }

        // ── Window events ─────────────────────────────────────────────────────────

        private void Window_Loaded(object sender, RoutedEventArgs e) { UpdateGrid(); }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        { UpdateGrid(); _lastRedraw = DateTime.MinValue; PlotAll(); }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        { 
            if (!ForceClose) { e.Cancel = true; Hide(); } 
        }

        private void AutoScaleButton_Click(object sender, RoutedEventArgs e) { AutoScaleAndPlot(); }
        private void PinToggle_Changed(object sender, RoutedEventArgs e)     { Topmost = PinToggle.IsChecked == true; }

        private void HoverToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (HoverToggle.IsChecked != true)
                CrosshairV.Visibility = CrosshairH.Visibility = HoverTooltip.Visibility = Visibility.Hidden;
        }

        // ── Mouse & Panning Logic ─────────────────────────────────────────────────

        private void HoverOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && e.LeftButton == MouseButtonState.Pressed)
            {
                AutoScaleAndPlot();
                return;
            }
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                _isPanning = true;
                _panStartMouse = e.GetPosition(HoverOverlay);
                _panStartXMin = _xMin; _panStartXMax = _xMax;
                _panStartYMinV = _yMinV; _panStartYMaxV = _yMaxV;
                _panStartYMinA = _yMinA; _panStartYMaxA = _yMaxA;
                HoverOverlay.CaptureMouse();
                _hasAutoScaled = true; 
            }
        }

        private void HoverOverlay_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Released && _isPanning)
            {
                _isPanning = false;
                HoverOverlay.ReleaseMouseCapture();
            }
        }

        private void HoverOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(HoverOverlay);
            double w = HoverOverlay.ActualWidth, h = HoverOverlay.ActualHeight;
            if (w < 1 || h < 1) return;

            if (_isPanning)
            {
                double dx = pos.X - _panStartMouse.X;
                double dy = pos.Y - _panStartMouse.Y;

                double xVoltsPerPx = (_panStartXMax - _panStartXMin) / w;
                _xMin = _panStartXMin - (dx * xVoltsPerPx);
                _xMax = _panStartXMax - (dx * xVoltsPerPx);

                if (_hasVoltsY) {
                    double yVoltsPerPx = (_panStartYMaxV - _panStartYMinV) / h;
                    _yMinV = _panStartYMinV + (dy * yVoltsPerPx); 
                    _yMaxV = _panStartYMaxV + (dy * yVoltsPerPx);
                }
                if (_hasAmpsY) {
                    double yAmpsPerPx = (_panStartYMaxA - _panStartYMinA) / h;
                    _yMinA = _panStartYMinA + (dy * yAmpsPerPx); 
                    _yMaxA = _panStartYMaxA + (dy * yAmpsPerPx);
                }

                _epoch++; // Increment epoch to drop pending render threads during active panning
                UpdateGrid();
                _lastRedraw = DateTime.MinValue; 
                PlotAll();
                return;
            }

            if (HoverToggle.IsChecked != true) return;
            
            CrosshairV.X1 = pos.X; CrosshairV.Y1 = 0;   CrosshairV.X2 = pos.X; CrosshairV.Y2 = h;
            CrosshairH.X1 = 0;     CrosshairH.Y1 = pos.Y; CrosshairH.X2 = w;   CrosshairH.Y2 = pos.Y;
            CrosshairV.Visibility = CrosshairH.Visibility = Visibility.Visible;

            double vIn = _xMin + pos.X / w * (_xMax - _xMin);
            string hoverStr = $"X-Axis: {FormatQuantity(vIn, _xUnit)}\n";
            if (_hasVoltsY) hoverStr += $"Y (Volts): {FormatQuantity(_yMaxV - pos.Y / h * (_yMaxV - _yMinV), AxisUnit.Volts)}\n";
            if (_hasAmpsY) hoverStr += $"Y (Amps): {FormatQuantity(_yMaxA - pos.Y / h * (_yMaxA - _yMinA), AxisUnit.Amps)}\n";
            HoverText.Text = hoverStr.TrimEnd();
            
            HoverTooltip.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            double dynamicTipW = HoverTooltip.DesiredSize.Width;
            double dynamicTipH = HoverTooltip.DesiredSize.Height;

            HoverTooltip.Visibility = Visibility.Visible;
            Canvas.SetLeft(HoverTooltip, pos.X + 14 + dynamicTipW > w ? pos.X - dynamicTipW - 6 : pos.X + 14);
            Canvas.SetTop (HoverTooltip, pos.Y + 14 + dynamicTipH > h ? pos.Y - dynamicTipH - 6 : pos.Y + 14);
        }

        private void HoverOverlay_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isPanning)
            {
                CrosshairV.Visibility = CrosshairH.Visibility = HoverTooltip.Visibility = Visibility.Hidden;
            }
        }

        private void HoverOverlay_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _hasAutoScaled = true; 
            _epoch++; // Drop old renders while zooming

            double factor = e.Delta > 0 ? 0.82 : 1.0 / 0.82;
            double mx = e.GetPosition(HoverOverlay).X, my = e.GetPosition(HoverOverlay).Y;
            double w  = HoverOverlay.ActualWidth,       h  = HoverOverlay.ActualHeight;
            bool ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (ctrl && !shift)
            { 
                double p = _xMin + mx/w*(_xMax-_xMin); _xMin=p-(p-_xMin)*factor; _xMax=p+(_xMax-p)*factor; 
            }
            else if (shift && !ctrl)
            { 
                if (_hasVoltsY) { double p = _yMaxV - my/h*(_yMaxV-_yMinV); _yMinV=p-(p-_yMinV)*factor; _yMaxV=p+(_yMaxV-p)*factor; }
                if (_hasAmpsY) { double p = _yMaxA - my/h*(_yMaxA-_yMinA); _yMinA=p-(p-_yMinA)*factor; _yMaxA=p+(_yMaxA-p)*factor; }
            }
            else // Plain scroll = uniform zoom around cursor
            {
                double px = _xMin + mx/w*(_xMax-_xMin);
                _xMin = px-(px-_xMin)*factor; _xMax = px+(_xMax-px)*factor;
                if (_hasVoltsY) { double py = _yMaxV - my/h*(_yMaxV-_yMinV); _yMinV=py-(py-_yMinV)*factor; _yMaxV=py+(_yMaxV-py)*factor; }
                if (_hasAmpsY) { double py = _yMaxA - my/h*(_yMaxA-_yMinA); _yMinA=py-(py-_yMinA)*factor; _yMaxA=py+(_yMaxA-py)*factor; }
            }

            UpdateGrid(); _lastRedraw = DateTime.MinValue; PlotAll();
        }

        // ── Grid / Axis ───────────────────────────────────────────────────────────

        private void UpdateGrid()
        {
            VLabels.Children.Clear();
            HLabels.Children.Clear();
            if (CurrentVLabels != null) CurrentVLabels.Children.Clear();

            double w = PlotArea.ActualWidth;
            double h = PlotArea.ActualHeight;
            if (w < 1 || h < 1) return;

            var gridLines = new GeometryGroup();
            var originLines = new GeometryGroup();

            double xVoltsPerPx = (_xMax - _xMin) / w;
            double niceXDiv = NiceVoltsPerDiv(60 * xVoltsPerPx);
            double xGridPx = niceXDiv / xVoltsPerPx;
            double xCenterPx = (0 - _xMin) / (_xMax - _xMin) * w;

            // ── Draw X Axis Origin & Grid ──
            if (xCenterPx >= 0 && xCenterPx <= w)
            {
                originLines.Children.Add(new LineGeometry(new Point(xCenterPx, 0), new Point(xCenterPx, h)));
                AddHLabel(FormatQuantity(0, _xUnit), xCenterPx, bold: true);
            }

            if (xGridPx >= 1)
            {
                int sXStart = xCenterPx > w ? (int)((xCenterPx - w) / xGridPx) : 1;
                int sXEnd   = xCenterPx < 0 ? (int)(-xCenterPx / xGridPx) : 1;
                for (int s = sXStart; s <= 500; s++) {
                    double px = xCenterPx + s * xGridPx; if (px > w) break;
                    gridLines.Children.Add(new LineGeometry(new Point(px, 0), new Point(px, h)));
                    AddHLabel(FormatQuantity(s * niceXDiv, _xUnit), px, false);
                }
                for (int s = sXEnd; s <= 500; s++) {
                    double px = xCenterPx - s * xGridPx; if (px < 0) break;
                    gridLines.Children.Add(new LineGeometry(new Point(px, 0), new Point(px, h)));
                    AddHLabel(FormatQuantity(-(s * niceXDiv), _xUnit), px, false);
                }
            }

            // ── Draw Y-Axes with Grid Synchronization ──
            bool drawGridLines = true;

            // Draw Voltage Y-Axis (Left)
            if (_hasVoltsY)
            {
                double yVoltsPerPx = (_yMaxV - _yMinV) / h;
                double niceYDiv = NiceVoltsPerDiv(60 * yVoltsPerPx);
                double yGridPx = niceYDiv / yVoltsPerPx;
                double yCenterPx = h - (0 - _yMinV) / (_yMaxV - _yMinV) * h;

                if (yCenterPx >= 0 && yCenterPx <= h) {
                    originLines.Children.Add(new LineGeometry(new Point(0, yCenterPx), new Point(w, yCenterPx)));
                    AddVLabel(FormatQuantity(0, AxisUnit.Volts), yCenterPx, true, true);
                }
                if (yGridPx >= 1) {
                    for (int s = 1; s <= 500; s++) {
                        double py = yCenterPx - s * yGridPx; if (py < 0) break;
                        if (drawGridLines) gridLines.Children.Add(new LineGeometry(new Point(0, py), new Point(w, py)));
                        AddVLabel(FormatQuantity(s * niceYDiv, AxisUnit.Volts), py, false, true);
                    }
                    for (int s = 1; s <= 500; s++) {
                        double py = yCenterPx + s * yGridPx; if (py > h) break;
                        if (drawGridLines) gridLines.Children.Add(new LineGeometry(new Point(0, py), new Point(w, py)));
                        AddVLabel(FormatQuantity(-(s * niceYDiv), AxisUnit.Volts), py, false, true);
                    }
                }
                drawGridLines = false; // Grid is drawn, Amps will perfectly overlay it
                AddAxisTitle(VLabels, "V_out", VerticalAlignment.Top, HorizontalAlignment.Left, new Thickness(2, 4, 0, 0));
            }

            // Draw Amps Y-Axis (Right)
            if (_hasAmpsY)
            {
                double yAmpsPerPx = (_yMaxA - _yMinA) / h;
                double niceYDiv = NiceVoltsPerDiv(60 * yAmpsPerPx);
                double yGridPx = niceYDiv / yAmpsPerPx;
                double yCenterPx = h - (0 - _yMinA) / (_yMaxA - _yMinA) * h;

                if (yCenterPx >= 0 && yCenterPx <= h) {
                    // Always draw the 0A origin line so both zero-axes are completely clear
                    originLines.Children.Add(new LineGeometry(new Point(0, yCenterPx), new Point(w, yCenterPx))); 
                    AddVLabel(FormatQuantity(0, AxisUnit.Amps), yCenterPx, true, false); 
                }
                if (yGridPx >= 1) {
                    for (int s = 1; s <= 500; s++) {
                        double py = yCenterPx - s * yGridPx; if (py < 0) break;
                        if (drawGridLines) gridLines.Children.Add(new LineGeometry(new Point(0, py), new Point(w, py)));
                        AddVLabel(FormatQuantity(s * niceYDiv, AxisUnit.Amps), py, false, false);
                    }
                    for (int s = 1; s <= 500; s++) {
                        double py = yCenterPx + s * yGridPx; if (py > h) break;
                        if (drawGridLines) gridLines.Children.Add(new LineGeometry(new Point(0, py), new Point(w, py)));
                        AddVLabel(FormatQuantity(-(s * niceYDiv), AxisUnit.Amps), py, false, false);
                    }
                }
                if (CurrentVLabels != null) AddAxisTitle(CurrentVLabels, "Current (I)", VerticalAlignment.Top, HorizontalAlignment.Left, new Thickness(5, 4, 0, 0));
            }

            GraphDividers.Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 180));
            GraphDividers.Data = gridLines;
            GraphOriginAxes.Data = originLines;

            AddAxisTitle(HLabels, "V_in", VerticalAlignment.Top, HorizontalAlignment.Right, new Thickness(0, -16, 4, 0));
        }

        private void AddAxisTitle(Grid parent, string text, VerticalAlignment va, HorizontalAlignment ha, Thickness margin)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = Brushes.DimGray,
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                FontWeight = FontWeights.Bold,
                Margin = margin,
                VerticalAlignment = va,
                HorizontalAlignment = ha
            };
            parent.Children.Add(tb);
        }

        private void AddVLabel(string text, double py, bool bold, bool alignLeft)
        {
            var tb = new TextBlock { Text = text, FontSize = 11, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = Brushes.DimGray };
            if (alignLeft) {
                tb.RenderTransform = new TranslateTransform(55 - (text.Length * 7), py - 8); 
                VLabels.Children.Add(tb);
            } else {
                tb.RenderTransform = new TranslateTransform(5, py - 8); // Clean 5px padding from the graph edge
                if (CurrentVLabels != null) CurrentVLabels.Children.Add(tb);
            }
        }

        private void AddHLabel(string text, double px, bool bold)
        {
            var tb = new TextBlock { Text = text, FontSize = 11, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = Brushes.DimGray };
            tb.RenderTransform = new TranslateTransform(px - (text.Length * 3.5), 5); // Dynamic center-align
            HLabels.Children.Add(tb);
        }
        
        private string FormatQuantity(double val, AxisUnit unit)
        {
            var q = new Quantity { Val = val }; // Using your existing Quantity class!
            return q.ToFixedString() + (unit == AxisUnit.Amps ? "A" : "V");
        }
        
        private static TextBlock MakeLabel(string text, Brush fg, double size, bool bold)
        {
            return new TextBlock
            {
                Text       = text,
                Foreground = fg,
                FontSize   = size,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal
            };
        }

        // ── CSV Export ────────────────────────────────────────────────────────────

        private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sim == null || _traceCount == 0 || _sim.GetNumberOfTicks() == 0) return;
            var dlg = new SaveFileDialog { FileName="XY_Transfer", DefaultExt=".csv", Filter="CSV Files (.csv)|*.csv" };
            if (dlg.ShowDialog() != true) return;
    
            int total = _sim.GetNumberOfTicks();
            using (var wr = new System.IO.StreamWriter(dlg.FileName))
            {
                string hdr = "Time (s)";
                for (int n = 0; n < _traceCount; n++)
                {
                    string xU = _traces[n].XUnit == AxisUnit.Amps ? "A" : "V";
                    string yU = _traces[n].YUnit == AxisUnit.Amps ? "A" : "V";
                    hdr += $",{_traces[n].ProbeName}_X ({xU}),{_traces[n].ProbeName}_Y ({yU})";
                }
                wr.WriteLine(hdr);
        
                for (int i = -(total-1); i <= 0; i++)
                {
                    string line = _sim.GetCurrentTime(i).ToString("G5", CultureInfo.InvariantCulture);
                    for (int n = 0; n < _traceCount; n++)
                    {
                        if (_traces[n].InputVarId1 < 0 || _traces[n].OutputVarId < 0) 
                        {
                            line += ",NaN,NaN"; 
                            continue; 
                        }
                        try
                        {
                            double vIn = _sim.GetValueOfVar(_traces[n].InputVarId1, i);
                            if (_traces[n].InputVarId2 >= 0) vIn -= _sim.GetValueOfVar(_traces[n].InputVarId2, i);
                    
                            double vOut = _sim.GetValueOfVar(_traces[n].OutputVarId, i);
                            if (_traces[n].InvertY) vOut = -vOut;

                            line += "," + vIn.ToString("G5", CultureInfo.InvariantCulture);
                            line += "," + vOut.ToString("G5", CultureInfo.InvariantCulture);
                        }
                        catch
                        {
                            line += ",NaN,NaN";
                        }
                    }
                    wr.WriteLine(line);
                }
            }
        }
    }
}