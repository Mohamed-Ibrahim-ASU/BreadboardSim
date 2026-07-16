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
        private const int    MaxPersistentPoints = 30000;
        private const int    MaxPersistentRenderPoints = 6000;
        private const int    MaxPersistentSegments = 96;
        private const int    MinPersistentSegmentPoints = 4;
        private const float  PlotSegmentBreakDistancePx = 95f;
        private const double PersistentJumpThreshold = 0.08;
        private const double PersistentAxisJumpThreshold = 0.06;
        private const double PersistentDirectionThreshold = 0.004;

        private static readonly Brush[] TraceColours =
        {
            Brushes.Red, Brushes.Blue, Brushes.Green, Brushes.DarkGoldenrod
        };

        // ── State ─────────────────────────────────────────────────────────────────
        public bool ForceClose = false; 
        private Simulator _sim;
        
        public enum AxisUnit { Volts, Amps }

        private AxisUnit _xUnit = AxisUnit.Volts;
        public sealed class GraphSourceOption
        {
            public string Key { get; set; }
            public string DisplayName { get; set; }
            public AxisUnit Unit { get; set; }
            public int VarId1 { get; set; }
            public int VarId2 { get; set; }
            public bool Invert { get; set; }
            public Brush SourceBrush { get; set; }

            public override string ToString()
            {
                return DisplayName + " (" + (Unit == AxisUnit.Amps ? "A" : "V") + ")";
            }
        }

        private sealed class TraceOption
        {
            public int Index { get; set; }
            public string DisplayName { get; set; }
            public Brush SourceBrush { get; set; }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private struct PersistedPoint
        {
            public double X;
            public double Y;
        }

        private sealed class PersistentSegment
        {
            public readonly List<PersistedPoint> Points = new List<PersistedPoint>();
            public int Direction;
        }

        private struct XYTrace
        {
            public string ProbeName;
            public int    InputVarId1;
            public int    InputVarId2;
            public int    OutputVarId;
            public int    OutputVarId2;
            public bool   InvertX;
            public bool   InvertY;
            public AxisUnit XUnit; 
            public AxisUnit YUnit; 
        }

        private readonly XYTrace[] _traces = new XYTrace[MaxTraces];
        private readonly List<PersistentSegment>[] _persistentSegments = new List<PersistentSegment>[MaxTraces];
        private readonly int[] _persistentPointCounts = new int[MaxTraces];
        private readonly int[] _lastPersistedTickCount = new int[MaxTraces];
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
        private readonly List<GraphSourceOption> _sourceOptions = new List<GraphSourceOption>();
        private bool _updatingSourceSelectors = false;
        private bool _updatingTraceSelector = false;
        private int _selectedTraceIndex = 0;
        public const string DefaultUserTraceName = "Trace 1";
        private const string UserTraceNamePrefix = "Trace ";

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
                _persistentSegments[i] = new List<PersistentSegment>();
                _lastPersistedTickCount[i] = -1;
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

        public void SetGraphSourceOptions(IEnumerable<GraphSourceOption> sourceOptions)
        {
            _updatingSourceSelectors = true;
            XAxisSourceCombo.Items.Clear();
            YAxisSourceCombo.Items.Clear();
            _sourceOptions.Clear();

            if (sourceOptions != null)
            {
                foreach (GraphSourceOption sourceOption in sourceOptions)
                {
                    if (sourceOption == null || string.IsNullOrEmpty(sourceOption.DisplayName)) continue;
                    _sourceOptions.Add(sourceOption);
                    XAxisSourceCombo.Items.Add(sourceOption);
                    YAxisSourceCombo.Items.Add(sourceOption);
                }
            }

            SelectSourcesForTrace(_selectedTraceIndex);
            _updatingSourceSelectors = false;
        }

        private void RefreshTraceSelector()
        {
            _updatingTraceSelector = true;
            TraceSelectorCombo.Items.Clear();

            for (int i = 0; i < _traceCount; i++)
            {
                TraceSelectorCombo.Items.Add(new TraceOption
                {
                    Index = i,
                    DisplayName = _traces[i].ProbeName,
                    SourceBrush = _paths[i].Stroke
                });
            }

            if (_traceCount == 0)
            {
                _selectedTraceIndex = 0;
            }
            else
            {
                if (_selectedTraceIndex < 0 || _selectedTraceIndex >= _traceCount) _selectedTraceIndex = 0;
                TraceSelectorCombo.SelectedIndex = _selectedTraceIndex;
            }

            _updatingTraceSelector = false;
        }

        public Brush AddXYTrace(string probeName, int inputVarId1, int inputVarId2, int outputVarId, Brush preferredColour = null, AxisUnit xUnit = AxisUnit.Volts, AxisUnit yUnit = AxisUnit.Volts, bool invertY = false, bool invertX = false, int outputVarId2 = -1)
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
                OutputVarId2 = outputVarId2,
                InvertX     = invertX,
                InvertY     = invertY,
                XUnit       = xUnit,
                YUnit       = yUnit
            };

            _xUnit = xUnit;
            if (yUnit == AxisUnit.Volts) _hasVoltsY = true;
            if (yUnit == AxisUnit.Amps) _hasAmpsY = true;

            _paths[_traceCount].Stroke = col;
            _traceCount++;
            if (_traceCount == 1) _selectedTraceIndex = 0;
            RefreshTraceSelector();
            SelectSourcesForTrace(_selectedTraceIndex);
            return col;
        }

        private void AddTraceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_traceCount >= MaxTraces) return;

            GraphSourceOption xSource = XAxisSourceCombo.SelectedItem as GraphSourceOption;
            GraphSourceOption ySource = YAxisSourceCombo.SelectedItem as GraphSourceOption;

            if ((xSource == null || ySource == null) && _sourceOptions.Count >= 2)
            {
                xSource = _sourceOptions.FirstOrDefault(s => s.Unit == AxisUnit.Volts) ?? _sourceOptions[0];
                ySource = _sourceOptions.FirstOrDefault(s => s.Unit == AxisUnit.Amps && s != xSource) ??
                          _sourceOptions.FirstOrDefault(s => s != xSource);
            }

            if (xSource == null || ySource == null) return;

            Brush traceBrush = ySource.SourceBrush != null && ySource.SourceBrush != Brushes.Transparent
                ? ySource.SourceBrush
                : null;

            int newIndex = _traceCount;
            AddXYTrace(GetNextUserTraceName(), xSource.VarId1, xSource.VarId2, ySource.VarId1,
                traceBrush, xSource.Unit, ySource.Unit, ySource.Invert, xSource.Invert, ySource.VarId2);

            _selectedTraceIndex = newIndex;
            RefreshTraceSelector();
            SelectSourcesForTrace(_selectedTraceIndex);
            _hasAutoScaled = false;
            _lastRedraw = DateTime.MinValue;
            PlotAll();
        }

        private void RemoveTraceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_traceCount == 0) return;
            RemoveTraceAt(_selectedTraceIndex);
        }

        private string GetNextUserTraceName()
        {
            for (int i = 1; i <= MaxTraces; i++)
            {
                string name = UserTraceNamePrefix + i.ToString(CultureInfo.InvariantCulture);
                if (!TraceNameExists(name)) return name;
            }

            return UserTraceNamePrefix + (_traceCount + 1).ToString(CultureInfo.InvariantCulture);
        }

        private bool TraceNameExists(string name)
        {
            for (int i = 0; i < _traceCount; i++)
                if (_traces[i].ProbeName == name) return true;
            return false;
        }

        private void RemoveTraceAt(int index)
        {
            if (index < 0 || index >= _traceCount) return;

            for (int i = index; i < _traceCount - 1; i++)
            {
                _traces[i] = _traces[i + 1];
                _paths[i].Stroke = _paths[i + 1].Stroke;
                _paths[i].Data = _paths[i + 1].Data;

                _persistentSegments[i].Clear();
                _persistentSegments[i].AddRange(_persistentSegments[i + 1]);
                _persistentPointCounts[i] = _persistentPointCounts[i + 1];
                _lastPersistedTickCount[i] = _lastPersistedTickCount[i + 1];
            }

            _traceCount--;
            _paths[_traceCount].Data = null;
            _persistentSegments[_traceCount].Clear();
            _persistentPointCounts[_traceCount] = 0;
            _lastPersistedTickCount[_traceCount] = -1;

            if (_selectedTraceIndex >= _traceCount) _selectedTraceIndex = _traceCount - 1;
            if (_selectedTraceIndex < 0) _selectedTraceIndex = 0;

            RecalculateYAxisUnits();
            ClearPersistentData();
            RefreshTraceSelector();
            SelectSourcesForTrace(_selectedTraceIndex);
            _hasAutoScaled = false;
            UpdateGrid();
            _lastRedraw = DateTime.MinValue;
            PlotAll();
        }
        public void UpdateXYTrace(string probeName, int newInId1, int newInId2, int newOutId)
        {
            for (int i = 0; i < _traceCount; i++)
            {
                if (_traces[i].ProbeName != probeName) continue;
                _traces[i].InputVarId1 = newInId1;
                _traces[i].InputVarId2 = newInId2;
                _traces[i].OutputVarId = newOutId;
                _traces[i].OutputVarId2 = -1;
                ClearPersistentData();
                SelectSourcesForTrace(_selectedTraceIndex);
                return;
            }
        }

        public bool UpdateXYTraceSource(string probeName, int newInId1, int newInId2, int newOutId, AxisUnit xUnit, AxisUnit yUnit, bool invertY, bool invertX, int newOutId2 = -1)
        {
            for (int i = 0; i < _traceCount; i++)
            {
                if (_traces[i].ProbeName != probeName) continue;
                _traces[i].InputVarId1 = newInId1;
                _traces[i].InputVarId2 = newInId2;
                _traces[i].OutputVarId = newOutId;
                _traces[i].OutputVarId2 = newOutId2;
                _traces[i].InvertX = invertX;
                _traces[i].InvertY = invertY;
                _traces[i].XUnit = xUnit;
                _traces[i].YUnit = yUnit;
                _xUnit = xUnit;
                RecalculateYAxisUnits();
                ClearPersistentData();
                SelectSourcesForTrace(_selectedTraceIndex);
                return true;
            }
            return false;
        }

        public void UpdateTraceColour(string probeName, Brush colour)
        {
            for (int i = 0; i < _traceCount; i++)
            {
                if (_traces[i].ProbeName != probeName) continue;
                _paths[i].Stroke = colour;
                RefreshTraceSelector();
                return;
            }
        }

        private void ClearPersistentData()
        {
            for (int i = 0; i < MaxTraces; i++)
            {
                if (_persistentSegments[i] != null)
                    _persistentSegments[i].Clear();
                _persistentPointCounts[i] = 0;
                _lastPersistedTickCount[i] = -1;
            }
        }

        private void PersistenceToggle_Changed(object sender, RoutedEventArgs e)
        {
            ClearPersistentData();
            _lastRedraw = DateTime.MinValue;
            PlotAll();
        }

        private void ClearPersistenceButton_Click(object sender, RoutedEventArgs e)
        {
            ClearPersistentData();
            for (int i = 0; i < _traceCount; i++) _paths[i].Data = null;
            _lastRedraw = DateTime.MinValue;
            PlotAll();
        }

        private void AppendPersistentSamples(int traceIndex, int totalTicks)
        {
            if (PersistenceToggle.IsChecked != true) return;
            if (_sim == null || traceIndex < 0 || traceIndex >= _traceCount) return;

            int lastTotal = _lastPersistedTickCount[traceIndex];
            int newSamples = lastTotal < 0 ? Math.Min(totalTicks, DrawWindowTicks) : totalTicks - lastTotal;
            if (newSamples <= 0) return;
            if (newSamples > DrawWindowTicks) newSamples = DrawWindowTicks;

            for (int i = newSamples; i >= 1; i--)
            {
                int tick = -i;
                double x, y;
                if (!TryReadTraceValue(traceIndex, tick, out x, out y)) continue;
                AddPersistentPoint(traceIndex, new PersistedPoint { X = x, Y = y });
            }

            PrunePersistentTrace(traceIndex);
            _lastPersistedTickCount[traceIndex] = totalTicks;
        }

        private void AddPersistentPoint(int traceIndex, PersistedPoint point)
        {
            List<PersistentSegment> segments = _persistentSegments[traceIndex];
            PersistentSegment segment = segments.Count == 0 ? null : segments[segments.Count - 1];
            if (segment == null || ShouldStartPersistentSegment(traceIndex, segment, point))
            {
                segment = new PersistentSegment();
                segments.Add(segment);
                if (segments.Count > MaxPersistentSegments)
                {
                    _persistentPointCounts[traceIndex] -= segments[0].Points.Count;
                    segments.RemoveAt(0);
                }
            }

            if (segment.Points.Count > 0)
            {
                PersistedPoint last = segment.Points[segment.Points.Count - 1];
                double xRange = Math.Max(_xMax - _xMin, 1e-12);
                double dxNorm = (point.X - last.X) / xRange;
                if (Math.Abs(dxNorm) >= PersistentDirectionThreshold)
                    segment.Direction = dxNorm > 0 ? 1 : -1;
            }

            segment.Points.Add(point);
            _persistentPointCounts[traceIndex]++;
        }

        private bool ShouldStartPersistentSegment(int traceIndex, PersistentSegment segment, PersistedPoint point)
        {
            if (segment.Points.Count == 0) return false;

            PersistedPoint last = segment.Points[segment.Points.Count - 1];
            double xRange = Math.Max(_xMax - _xMin, 1e-12);
            double yRange = Math.Max(GetTraceYMax(traceIndex) - GetTraceYMin(traceIndex), 1e-12);
            double dxNorm = (point.X - last.X) / xRange;
            double dyNorm = (point.Y - last.Y) / yRange;
            double absDx = Math.Abs(dxNorm);
            double absDy = Math.Abs(dyNorm);

            if (absDx > PersistentAxisJumpThreshold || absDy > PersistentAxisJumpThreshold)
                return true;
            if (Math.Sqrt(absDx * absDx + absDy * absDy) > PersistentJumpThreshold)
                return true;

            if (segment.Direction != 0 && absDx >= PersistentDirectionThreshold && segment.Points.Count >= MinPersistentSegmentPoints)
            {
                int direction = dxNorm > 0 ? 1 : -1;
                if (direction != segment.Direction) return true;
            }

            return false;
        }

        private void PrunePersistentTrace(int traceIndex)
        {
            List<PersistentSegment> segments = _persistentSegments[traceIndex];
            while (_persistentPointCounts[traceIndex] > MaxPersistentPoints && segments.Count > 0)
            {
                int excess = _persistentPointCounts[traceIndex] - MaxPersistentPoints;
                PersistentSegment first = segments[0];
                if (first.Points.Count <= excess || first.Points.Count <= MinPersistentSegmentPoints)
                {
                    _persistentPointCounts[traceIndex] -= first.Points.Count;
                    segments.RemoveAt(0);
                    continue;
                }

                int remove = Math.Min(excess, Math.Min(1000, first.Points.Count - MinPersistentSegmentPoints));
                first.Points.RemoveRange(0, remove);
                _persistentPointCounts[traceIndex] -= remove;
            }
        }

        private double GetTraceYMin(int traceIndex)
        {
            return _traces[traceIndex].YUnit == AxisUnit.Amps ? _yMinA : _yMinV;
        }

        private double GetTraceYMax(int traceIndex)
        {
            return _traces[traceIndex].YUnit == AxisUnit.Amps ? _yMaxA : _yMaxV;
        }

        private static bool ShouldBreakProjectedSegment(float px, float py, float lastPx, float lastPy)
        {
            if (float.IsNaN(lastPx) || float.IsNaN(lastPy)) return false;

            float dx = px - lastPx;
            float dy = py - lastPy;
            return dx * dx + dy * dy > PlotSegmentBreakDistancePx * PlotSegmentBreakDistancePx;
        }

        private bool TryReadTraceValue(int traceIndex, int tick, out double x, out double y)
        {
            x = 0;
            y = 0;
            if (traceIndex < 0 || traceIndex >= _traceCount) return false;

            int inId1 = _traces[traceIndex].InputVarId1;
            int inId2 = _traces[traceIndex].InputVarId2;
            int outId = _traces[traceIndex].OutputVarId;
            int outId2 = _traces[traceIndex].OutputVarId2;
            if (inId1 < 0 || outId < 0) return false;

            try
            {
                x = _sim.GetValueOfVar(inId1, tick);
                if (inId2 >= 0) x -= _sim.GetValueOfVar(inId2, tick);
                if (_traces[traceIndex].InvertX) x = -x;

                y = _sim.GetValueOfVar(outId, tick);
                if (outId2 >= 0) y -= _sim.GetValueOfVar(outId2, tick);
                if (_traces[traceIndex].InvertY) y = -y;
            }
            catch
            {
                return false;
            }

            return !(double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y));
        }

        private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingSourceSelectors || _traceCount == 0) return;
            if (_selectedTraceIndex < 0 || _selectedTraceIndex >= _traceCount) return;

            GraphSourceOption xSource = XAxisSourceCombo.SelectedItem as GraphSourceOption;
            GraphSourceOption ySource = YAxisSourceCombo.SelectedItem as GraphSourceOption;
            if (xSource == null || ySource == null) return;

            _traces[_selectedTraceIndex].InputVarId1 = xSource.VarId1;
            _traces[_selectedTraceIndex].InputVarId2 = xSource.VarId2;
            _traces[_selectedTraceIndex].OutputVarId = ySource.VarId1;
            _traces[_selectedTraceIndex].OutputVarId2 = ySource.VarId2;
            _traces[_selectedTraceIndex].InvertX = xSource.Invert;
            _traces[_selectedTraceIndex].InvertY = ySource.Invert;
            _traces[_selectedTraceIndex].XUnit = xSource.Unit;
            _traces[_selectedTraceIndex].YUnit = ySource.Unit;

            if (ySource.SourceBrush != null && ySource.SourceBrush != Brushes.Transparent)
            {
                _paths[_selectedTraceIndex].Stroke = ySource.SourceBrush;
                RefreshTraceSelector();
            }

            _xUnit = xSource.Unit;
            RecalculateYAxisUnits();
            _hasAutoScaled = false;
            _epoch++;
            ClearPersistentData();
            UpdateGrid();
            _lastRedraw = DateTime.MinValue;
            PlotAll();
        }

        private void TraceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingTraceSelector) return;

            TraceOption selectedTrace = TraceSelectorCombo.SelectedItem as TraceOption;
            if (selectedTrace == null) return;

            _selectedTraceIndex = selectedTrace.Index;
            SelectSourcesForTrace(_selectedTraceIndex);
        }

        private void SelectSourcesForTrace(int traceIndex)
        {
            if (_traceCount == 0 || _sourceOptions.Count == 0) return;
            if (traceIndex < 0 || traceIndex >= _traceCount) return;

            _updatingSourceSelectors = true;
            SelectMatchingSource(XAxisSourceCombo, _traces[traceIndex].InputVarId1, _traces[traceIndex].InputVarId2, _traces[traceIndex].InvertX, _traces[traceIndex].XUnit);
            SelectMatchingSource(YAxisSourceCombo, _traces[traceIndex].OutputVarId, _traces[traceIndex].OutputVarId2, _traces[traceIndex].InvertY, _traces[traceIndex].YUnit);
            _updatingSourceSelectors = false;
        }

        private static void SelectMatchingSource(ComboBox combo, int varId1, int varId2, bool invert, AxisUnit unit)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                GraphSourceOption option = combo.Items[i] as GraphSourceOption;
                if (option == null) continue;
                if (option.VarId1 == varId1 && option.VarId2 == varId2 && option.Invert == invert && option.Unit == unit)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void RecalculateYAxisUnits()
        {
            _hasVoltsY = false;
            _hasAmpsY = false;
            for (int i = 0; i < _traceCount; i++)
            {
                if (_traces[i].YUnit == AxisUnit.Volts) _hasVoltsY = true;
                if (_traces[i].YUnit == AxisUnit.Amps) _hasAmpsY = true;
            }
        }

        public void ResetAll()
        {
            _epoch++;
            _traceCount    = 0;
            _selectedTraceIndex = 0;
            _hasAutoScaled = false;
            _hasVoltsY = false;
            _hasAmpsY = false;
            _xUnit = AxisUnit.Volts;
            for (int i = 0; i < MaxTraces; i++) _paths[i].Data = null; 
            ClearPersistentData();
            RefreshTraceSelector();
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

    List<float[]>[] pxData = new List<float[]>[_traceCount];
    bool[] active    = new bool[_traceCount];
    bool persistenceEnabled = PersistenceToggle != null && PersistenceToggle.IsChecked == true;

    for (int n = 0; n < _traceCount; n++)
    {
        int inId1 = _traces[n].InputVarId1;
        int outId = _traces[n].OutputVarId;
        if (inId1 < 0 || outId < 0) { _paths[n].Data = null; continue; }      
        
        double yMin = GetTraceYMin(n);
        double yMax = GetTraceYMax(n);
        double yR = Math.Max(yMax - yMin, 1e-12);
        
        active[n] = true;
        if (persistenceEnabled) AppendPersistentSamples(n, totalTicks);

        pxData[n] = new List<float[]>();

        try
        {
            if (persistenceEnabled)
            {
                int persistentStride = 1;
                if (_persistentPointCounts[n] > MaxPersistentRenderPoints)
                    persistentStride = (int)Math.Ceiling((double)_persistentPointCounts[n] / MaxPersistentRenderPoints);

                List<PersistentSegment> segments = _persistentSegments[n];
                var buf = new float[Math.Max(1, (_persistentPointCounts[n] + persistentStride - 1) / persistentStride) * 2];
                int write = 0;
                float lastPx = float.NaN;
                float lastPy = float.NaN;

                for (int s = 0; s < segments.Count; s++)
                {
                    List<PersistedPoint> points = segments[s].Points;

                    for (int i = 0; i < points.Count; i += persistentStride)
                    {
                        double vIn = points[i].X;
                        double vOut = points[i].Y;

                        if (vIn  < xMin - xR || vIn  > xMax + xR) continue;
                        if (vOut < yMin - yR || vOut > yMax + yR) continue;

                        float px = (float)((vIn  - xMin) / xR * plotW);
                        float py = (float)(plotH - (vOut - yMin) / yR * plotH);

                        if (ShouldBreakProjectedSegment(px, py, lastPx, lastPy) && write >= 4)
                        {
                            if (write < buf.Length) Array.Resize(ref buf, write);
                            pxData[n].Add(buf);
                            buf = new float[Math.Max(4, (_persistentPointCounts[n] + persistentStride - 1) / persistentStride) * 2];
                            write = 0;
                        }

                        buf[write]     = px;
                        buf[write + 1] = py;
                        write += 2;
                        lastPx = px;
                        lastPy = py;
                    }
                }

                if (write >= 4)
                {
                    if (write < buf.Length) Array.Resize(ref buf, write);
                    pxData[n].Add(buf);
                }
            }
            else
            {
                var buf = new float[drawWindow * 2];
                int write = 0;
                for (int i = 0; i < drawWindow; i++)
                {
                    int tick = -(drawWindow - i);
                    double vIn, vOut;
                    if (!TryReadTraceValue(n, tick, out vIn, out vOut)) continue;

                    if (vIn  < xMin - xR || vIn  > xMax + xR) continue;
                    if (vOut < yMin - yR || vOut > yMax + yR) continue;

                    float px = (float)((vIn  - xMin) / xR * plotW);
                    float py = (float)(plotH - (vOut - yMin) / yR * plotH);

                    buf[write]     = px;
                    buf[write + 1] = py;
                    write += 2;
                }

                if (write >= 4)
                {
                    if (write < buf.Length) Array.Resize(ref buf, write);
                    pxData[n].Add(buf);
                }
            }
        }
        catch
        {
            _traces[n].InputVarId1 = -1;
            _traces[n].OutputVarId = -1;
            _paths[n].Data = null;
            active[n] = false;
        }

        if (active[n] && pxData[n].Count == 0)
        {
            _paths[n].Data = null;
            active[n] = false;
        }
    }

    System.Threading.Interlocked.Increment(ref _pendingRenders);
    int threadEpoch = _epoch;

    Task.Factory.StartNew(() =>
    {
        var geometries = new StreamGeometry[_traceCount];
        for (int n = 0; n < _traceCount; n++)
        {
            if (!active[n] || pxData[n] == null || pxData[n].Count == 0) continue;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                float clampMinX = -10f, clampMaxX = (float)(plotW  + 10);
                float clampMinY = -10f, clampMaxY = (float)(plotH  + 10);

                for (int segmentIndex = 0; segmentIndex < pxData[n].Count; segmentIndex++)
                {
                    float[] buf = pxData[n][segmentIndex];
                    int pointCount = buf.Length / 2;
                    if (pointCount < 2) continue;

                    bool first = true;
                    float lastPx = float.NaN, lastPy = float.NaN;

                    for (int i = 0; i < pointCount; i++)
                    {
                        float px = buf[i * 2], py = buf[i * 2 + 1];

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
            {
                if (active[n] && task.Result[n] != null)
                    _paths[n].Data = task.Result[n];
            }
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
        int outId2 = _traces[n].OutputVarId2;
        if (inId1 < 0 || outId < 0) continue;
        
        try
        {
            for (int i = 0; i < boundsWindow; i++)
            {
                int tick = -(boundsWindow - i); 
                double vIn = _sim.GetValueOfVar(inId1, tick);
                if (inId2 >= 0) vIn -= _sim.GetValueOfVar(inId2, tick);
                if (_traces[n].InvertX) vIn = -vIn;
                
                double vOut = _sim.GetValueOfVar(outId, tick);
                if (outId2 >= 0) vOut -= _sim.GetValueOfVar(outId2, tick);
                if (_traces[n].InvertY) vOut = -vOut;

                if (double.IsNaN(vIn) || double.IsNaN(vOut) || double.IsInfinity(vIn) || double.IsInfinity(vOut)) continue;
                
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
    ExpandDegenerateRange(ref xlo, ref xhi, _xUnit);
    double xMid = (xlo + xhi) / 2;
    double xVoltsPerDiv = NiceVoltsPerDiv(Math.Max(xhi - xlo, 1e-9) / (0.6 * xDivs));
    _xMin = xMid - xVoltsPerDiv * xDivs / 2;
    _xMax = xMid + xVoltsPerDiv * xDivs / 2;

    // Scale Y (Volts)
    if (_hasVoltsY && yhiV >= yloV) {
        ExpandDegenerateRange(ref yloV, ref yhiV, AxisUnit.Volts);
        double yMidV = (yloV + yhiV) / 2;
        double yVoltsPerDiv = NiceVoltsPerDiv(Math.Max(yhiV - yloV, 1e-9) / (0.6 * yDivs));
        _yMinV = yMidV - yVoltsPerDiv * yDivs / 2;
        _yMaxV = yMidV + yVoltsPerDiv * yDivs / 2;
    }

    // Scale Y (Amps)
    if (_hasAmpsY && yhiA >= yloA) {
        ExpandDegenerateRange(ref yloA, ref yhiA, AxisUnit.Amps);
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

        private static void ExpandDegenerateRange(ref double lo, ref double hi, AxisUnit unit)
        {
            if (hi < lo) return;

            double span = hi - lo;
            double mid = (lo + hi) / 2.0;
            double minSpan = unit == AxisUnit.Amps
                ? Math.Max(Math.Abs(mid) * 0.2, 0.001)
                : Math.Max(Math.Abs(mid) * 0.2, 1.0);

            if (span >= minSpan) return;

            double halfSpan = minSpan / 2.0;
            lo = mid - halfSpan;
            hi = mid + halfSpan;
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
                AddAxisTitle(VLabels, "Voltage (V)", VerticalAlignment.Top, HorizontalAlignment.Left, new Thickness(2, 4, 0, 0));
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

            AddAxisTitle(HLabels, _xUnit == AxisUnit.Amps ? "Current (I)" : "Voltage (V)", VerticalAlignment.Top, HorizontalAlignment.Right, new Thickness(0, -16, 4, 0));
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
                            if (_traces[n].InvertX) vIn = -vIn;
                    
                            double vOut = _sim.GetValueOfVar(_traces[n].OutputVarId, i);
                            if (_traces[n].OutputVarId2 >= 0) vOut -= _sim.GetValueOfVar(_traces[n].OutputVarId2, i);
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
