using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SimGUI
{
    public partial class MainWindow : Window
    {
        private double CurrentZoomFactor = 1;
        private const double ZoomFactorDelta = 0.25;

        private System.Windows.Threading.DispatcherTimer UpdateTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render);
        private int NumberOfUpdates = 0;

        public string SelectedTool = "SELECT";

        public Circuit circuit;

        //Map breadboard IDs to the breadboard objects
        //Breadboard IDs are such that top left breadboard = 0, top right breadboard = 1, one below the top left = 2, etc
        public Dictionary<int, Breadboard> Breadboards = new Dictionary<int, Breadboard>();

        //Path to last opened file
        private string LastOpenedFile = null;
        private string LastCircuitState = "";

        public Simulator CurrentSimulator = new Simulator();
        
        // Hover Tooltips
        private Border CustomTooltip;
        private TextBlock CustomTooltipText;
        private Dictionary<string, string> GlobalNativeCache = new Dictionary<string, string>(); 
        
        private Point lastMousePos = new Point(-100, -100);
        private DateTime lastHoverTime = DateTime.MinValue;
        private string lastRenderedText = "";
        private bool isManuallyStopped = true; 
        private Rect lastHitBox = Rect.Empty; 
        
        
        //List of required files
        private readonly string[] ApplicationResources = {
                                               "res/simbe.exe",
                                               "res/app.ico",
                                               "res/about.txt",
                                               "res/actions/graph.png",
                                               "res/actions/open.png",
                                               "res/actions/run.png",
                                               "res/actions/redo.png",
                                               "res/actions/save.png",
                                               "res/actions/settings.png",
                                               "res/actions/stop.png",
                                               "res/actions/undo.png",
                                               "res/actions/zoomin.png",
                                               "res/actions/zoomout.png",
                                               "res/models/7seg.xml",
                                               "res/models/diodes.xml",
                                               "res/models/ics.xml",
                                               "res/models/leds.xml",
                                               "res/models/transistors.xml",
                                               "res/breadboard/breadboard.png",
                                               "res/breadboard/breadboard-holes.csv",
                                               "res/tools/component.cur",
                                               "res/tools/component.png",
                                               "res/tools/delete.cur",
                                               "res/tools/delete.png",
                                               "res/tools/interact.cur",
                                               "res/tools/interact.png",
                                               "res/tools/select.png",
                                               "res/tools/wire.cur",
                                               "res/tools/wire.png"
                                           };

        public GraphView CurrentGraph = null;
        public XYGraphView CurrentXYGraph = null;

        public MainWindow()
        {

            System.Diagnostics.Trace.WriteLine(Environment.Version);

            if (!System.IO.Directory.Exists("res/"))
            {
                System.IO.Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
                if (!System.IO.Directory.Exists("res/"))
                {
                    MessageBox.Show("The directory containing the resources needed by this application is missing.", "Application Error", MessageBoxButton.OK,MessageBoxImage.Error);
                    Application.Current.Shutdown();
                }
            }

            string missingResource = "";
            if (CheckForMissingResources(ref missingResource))
            {
                MessageBox.Show("The resource \"" + missingResource + "\" is missing.", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }

            circuit = new Circuit(this);
            App.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            InitializeComponent();
            
            Style hiddenToolTipStyle = new Style(typeof(ToolTip));
            hiddenToolTipStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            hiddenToolTipStyle.Setters.Add(new Setter(UIElement.OpacityProperty, 0.0));
            DrawArea.Resources.Add(typeof(ToolTip), hiddenToolTipStyle);
            
            CustomTooltipText = new TextBlock {
                Foreground = Brushes.Black,
                Margin = new Thickness(6),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            
            CustomTooltip = new Border {
                Background = new SolidColorBrush(Color.FromArgb(245, 255, 255, 230)), 
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                IsHitTestVisible = false, 
                Visibility = Visibility.Hidden,
                Child = CustomTooltipText
            };
            
            Canvas.SetZIndex(CustomTooltip, 99999);
            DrawArea.Children.Add(CustomTooltip);

            ToolTipService.SetIsEnabled(DrawArea, false);
            DrawArea.AddHandler(FrameworkElement.ToolTipOpeningEvent, new ToolTipEventHandler((s, e) => {
                e.Handled = true; 
            }), true);

            DrawArea.MouseMove += DrawArea_MouseMove;
            DrawArea.MouseLeave += (s, e) => {
                CustomTooltip.Visibility = Visibility.Hidden;
                lastHitBox = Rect.Empty;
            };

            PopulateMenu(Devices_Resistors, Constants.E6, 0, 7, "Ω Resistor", "Resistor");
            PopulateMenu(Devices_Capacitors, Constants.E3, -10, -7, "F Capacitor", "Capacitor");
            PopulateMenu(Devices_ElectroCapacitors, Constants.E3, -6, -3, "F Capacitor", "Electrolytic Capacitor");

            PopulateMenuWithModels(Devices_DigitalIC, "Integrated Circuit", "res/models/ics.xml", "Digital");
            PopulateMenuWithModels(Devices_AnalogIC, "Integrated Circuit", "res/models/ics.xml", "Analog");
            PopulateMenuWithModels(Devices_Diodes, "Diode", "res/models/diodes.xml");
            PopulateMenuWithModels(Devices_NPN, "NPN Transistor", "res/models/transistors.xml","npn");
            PopulateMenuWithModels(Devices_PNP, "PNP Transistor", "res/models/transistors.xml", "pnp");
            PopulateMenuWithModels(Devices_NMOS, "N-channel MOSFET", "res/models/transistors.xml", "nmos");
            PopulateMenuWithModels(Devices_Output, "LED", "res/models/leds.xml", null, " LED");
            PopulateMenuWithModels(Devices_Output, "7-Segment Display", "res/models/7seg.xml", null);

            ComponentData pot_10k = new ComponentData("Potentiometer", 10000, "10k Potentiometer");

            TreeViewItem pot_10k_item = new TreeViewItem();
            pot_10k_item.Header = pot_10k;
            Devices_Input.Items.Add(pot_10k_item);

            ComponentData ldr = new ComponentData("LDR", 0, "LDR");

            TreeViewItem ldr_item = new TreeViewItem();
            ldr_item.Header = ldr;
            Devices_Input.Items.Add(ldr_item);

            ComponentData function_generator = new ComponentData("Function Generator", 1.0, "Function Generator");

            TreeViewItem function_generator_item = new TreeViewItem();
            function_generator_item.Header = function_generator;
            Devices_Input.Items.Add(function_generator_item);

            ComponentData spdt_switch = new ComponentData("SPDT Switch", 0, "SPDT Switch");

            TreeViewItem spdt_switch_item = new TreeViewItem();
            spdt_switch_item.Header = spdt_switch;
            Devices_Input.Items.Add(spdt_switch_item);


            ComponentData push_switch = new ComponentData("Push Switch", 0, "Push Switch");

            TreeViewItem push_switch_item = new TreeViewItem();
            push_switch_item.Header = push_switch;
            Devices_Input.Items.Add(push_switch_item);

            ComponentData probe = new ComponentData("Probe", 0, "Oscilloscope Probe");

            TreeViewItem probe_item = new TreeViewItem();
            probe_item.Header = probe;

            DevicePicker.Items.Add(probe_item);

            ComponentData diffProbe = new ComponentData("DiffProbe", 0, "Differential Voltage Probe");

            TreeViewItem diffProbe_item = new TreeViewItem();
            diffProbe_item.Header = diffProbe;
            DevicePicker.Items.Add(diffProbe_item);

            ComponentData currentProbe = new ComponentData("CurrentProbe", 0, "Current Probe (Ammeter)");

            TreeViewItem currentProbe_item = new TreeViewItem();
            currentProbe_item.Header = currentProbe;
            DevicePicker.Items.Add(currentProbe_item);

            ComponentData xyProbe = new ComponentData("XYProbe", 0, "XY Transfer Probe");
            TreeViewItem xyProbe_item = new TreeViewItem();
            xyProbe_item.Header = xyProbe;
            DevicePicker.Items.Add(xyProbe_item);

            ComponentData ivProbe = new ComponentData("IVProbe", 0, "I/V Curve Probe");
            TreeViewItem ivProbe_item = new TreeViewItem();
            ivProbe_item.Header = ivProbe;
            DevicePicker.Items.Add(ivProbe_item);
            
            SetNumberOfBreadboards(1);

            // PERFORMANCE FIX: Decreased from 50ms to 16ms to achieve a smooth 60 FPS refresh rate
            UpdateTimer.Interval = TimeSpan.FromMilliseconds(16);
            UpdateTimer.Tick += SimUpdate;

            SelectTool("SELECT");

            string[] args = Environment.GetCommandLineArgs();
            
            if (args.Length > 1 && System.IO.File.Exists(args[1]))
            {
                circuit.ClearUndoQueue();
                circuit.LoadCircuit(args[1]);
                LastOpenedFile = args[1];
                Title = "Breadboard Simulator (MI) - " + System.IO.Path.GetFileNameWithoutExtension(args[1]);
            }
            
            PopulateSamplesMenu(File_Samples, "res/samples");
        }

        private string GetPinName(Component c, int pinNumber)
        {
            string typeName = c.GetType().Name.ToLower();
            string compName = c.ComponentType.ToLower();
            string model = c.ComponentModel != null ? c.ComponentModel.ToLower() : "";

            if (typeName.Contains("transistor") || compName.Contains("transistor"))
            {
                if (compName.Contains("mosfet")) {
                    if (pinNumber == 1) return "Source";
                    if (pinNumber == 2) return "Gate";
                    if (pinNumber == 3) return "Drain";
                } 
                else if (model.ToLower().Contains("bc639")) { 
                    if (pinNumber == 1) return "Emitter";   
                    if (pinNumber == 2) return "Collector"; 
                    if (pinNumber == 3) return "Base";      
                } 
                else {
                    if (pinNumber == 1) return "Emitter";
                    if (pinNumber == 2) return "Base";
                    if (pinNumber == 3) return "Collector";
                }
            }
            else if (typeName.Contains("potentiometer") || compName.Contains("potentiometer"))
            {
                if (pinNumber == 1) return "Terminal 1";
                if (pinNumber == 2) return "Wiper";
                if (pinNumber == 3) return "Terminal 2";
            }
            else if (typeName.Contains("diode") || compName.Contains("diode") || typeName.Contains("led"))
            {
                if (pinNumber == 1) return "Anode";
                if (pinNumber == 2) return "Cathode";
            }
            else if (typeName.Contains("capacitor") || compName.Contains("capacitor"))
            {
                if (compName.Contains("electrolytic")) {
                    if (pinNumber == 1) return "Anode (+)";
                    if (pinNumber == 2) return "Cathode (-)";
                }
            }
            else if (typeName.Contains("function") || compName.Contains("function"))
            {
                if (pinNumber == 1) return "Positive (+)";
                if (pinNumber == 2) return "Negative (-)";
            }
            else if (model.Contains("555"))
            {
                switch (pinNumber) {
                    case 1: return "Ground";
                    case 2: return "Trigger";
                    case 3: return "Output";
                    case 4: return "Reset";
                    case 5: return "Control";
                    case 6: return "Threshold";
                    case 7: return "Discharge";
                    case 8: return "VCC";
                }
            }
            else if (model.Contains("741") || model.Contains("opamp"))
            {
                // Standard Single Op-Amp Pinout (DIP-8)
                switch(pinNumber) {
                    case 2: return "Inv. Input (-)";
                    case 3: return "Non-Inv. Input (+)";
                    case 4: return "V-";
                    case 6: return "Output";
                    case 7: return "V+";
                }
            }
            else if (model.Contains("lm358"))
            {
                // Standard Dual Op-Amp Pinout (DIP-8)
                switch(pinNumber) {
                    case 1: return "Output A";
                    case 2: return "Inv. Input A (-)";
                    case 3: return "Non-Inv. Input A (+)";
                    case 4: return "V- / Ground";
                    case 5: return "Non-Inv. Input B (+)";
                    case 6: return "Inv. Input B (-)";
                    case 7: return "Output B";
                    case 8: return "V+";
                }
            }
            else if (model.Contains("lm324"))
            {
                // Standard Quad Op-Amp Pinout (DIP-14)
                switch(pinNumber) {
                    case 1: return "Output 1";
                    case 2: return "Inv. Input 1 (-)";
                    case 3: return "Non-Inv. Input 1 (+)";
                    case 4: return "V+";
                    case 5: return "Non-Inv. Input 2 (+)";
                    case 6: return "Inv. Input 2 (-)";
                    case 7: return "Output 2";
                    case 8: return "Output 3";
                    case 9: return "Inv. Input 3 (-)";
                    case 10: return "Non-Inv. Input 3 (+)";
                    case 11: return "V- / Ground";
                    case 12: return "Non-Inv. Input 4 (+)";
                    case 13: return "Inv. Input 4 (-)";
                    case 14: return "Output 4";
                }
            }

            return "Pin " + pinNumber;
        }

        private string ExtractPercentage(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int pIndex = text.IndexOf('%');
            if (pIndex <= 0) return "";
            
            int start = pIndex - 1;
            while (start >= 0 && (char.IsDigit(text[start]) || text[start] == '.')) 
            {
                start--;
            }
            start++;
            if (start < pIndex) return text.Substring(start, pIndex - start + 1);
            return "";
        }

        private void DrawArea_MouseMove(object sender, MouseEventArgs e)
        {
            Point currentPos = e.GetPosition(DrawArea);
            
            if (Math.Abs(currentPos.X - lastMousePos.X) < 2 && Math.Abs(currentPos.Y - lastMousePos.Y) < 2) 
            {
                if ((DateTime.Now - lastHoverTime).TotalMilliseconds < 30) return;
            }
            
            lastMousePos = currentPos;
            lastHoverTime = DateTime.Now;

            UpdateHoverBox(currentPos);
        }

private void UpdateHoverBox(Point mousePos)
        {
            if (circuit == null) return;

            DependencyObject hitElement = null;
            VisualTreeHelper.HitTest(DrawArea, null, new HitTestResultCallback(result =>
            {
                if (result.VisualHit is FrameworkElement fe && fe.IsHitTestVisible)
                {
                    hitElement = result.VisualHit;
                    return HitTestResultBehavior.Stop;
                }
                return HitTestResultBehavior.Continue;
            }), new PointHitTestParameters(mousePos));

            if (hitElement == null)
            {
                CustomTooltip.Visibility = Visibility.Hidden;
                lastRenderedText = "";
                lastHitBox = Rect.Empty;
                return;
            }

            object rawTooltip = null;
            Component parentComponent = null;
            Wire parentWire = null;
            int pinNumber = -1;

            DependencyObject curr = hitElement;
            while (curr != null && curr != DrawArea)
            {
                if (curr is FrameworkElement fe)
                {
                    if (fe.ToolTip != null && rawTooltip == null) 
                    {
                        rawTooltip = fe.ToolTip; 
                    }
                    if (fe.Name != null && fe.Name.StartsWith("pin") && pinNumber == -1)
                    {
                        int.TryParse(fe.Name.Substring(3), out pinNumber); 
                    }
                }
                
                if (curr is Component c) parentComponent = c;
                if (curr is Wire w) parentWire = w;
                
                curr = VisualTreeHelper.GetParent(curr);
            }

            if ((parentComponent != null || parentWire != null) && hitElement is FrameworkElement hitFE && hitFE != DrawArea)
            {
                try {
                    lastHitBox = hitFE.TransformToAncestor(DrawArea).TransformBounds(new Rect(0, 0, hitFE.ActualWidth, hitFE.ActualHeight));
                } catch {
                    lastHitBox = Rect.Empty;
                }
            }
            else
            {
                lastHitBox = Rect.Empty;
            }

            string devTip = "";
            if (rawTooltip is string s) devTip = s;
            else if (rawTooltip is TextBlock tb) devTip = tb.Text;
            else if (rawTooltip is ToolTip tt)
            {
                if (tt.Content is string tts) devTip = tts;
                else if (tt.Content is TextBlock ttb) devTip = ttb.Text;
            }

            if (parentComponent is XYProbe) devTip = "[Parametric Trace]";

            string livePotPct = "";
            if (parentComponent != null && parentComponent.ComponentType.ToLower().Contains("potentiometer"))
            {
                object compTip = parentComponent.ToolTip;
                string compTipStr = "";
                if (compTip is string c_s) compTipStr = c_s;
                else if (compTip is TextBlock c_tb) compTipStr = c_tb.Text;
                else if (compTip is ToolTip c_tt)
                {
                    if (c_tt.Content is string c_tts) compTipStr = c_tts;
                    else if (c_tt.Content is TextBlock c_ttb) compTipStr = c_ttb.Text;
                }
                
                livePotPct = ExtractPercentage(compTipStr);
            }

            
            // We now strictly separate the Body cache from the Pin cache so hovering a pin 
            // doesn't overwrite the Transistor's main text.
            string cacheKey = "";
            if (parentComponent != null)
            {
                cacheKey = parentComponent.ID + (pinNumber == -1 ? "_body" : $"_pin_{pinNumber}");
            }

            if (CurrentSimulator.SimRunning && !string.IsNullOrEmpty(cacheKey) && !string.IsNullOrEmpty(devTip))
            {
                 GlobalNativeCache[cacheKey] = devTip;
            }

            if ((string.IsNullOrEmpty(devTip) || !CurrentSimulator.SimRunning) && !string.IsNullOrEmpty(cacheKey) && GlobalNativeCache.ContainsKey(cacheKey))
            {
                devTip = GlobalNativeCache[cacheKey];
                
                if (pinNumber == -1 && !string.IsNullOrEmpty(livePotPct))
                {
                    string oldPct = ExtractPercentage(devTip);
                    if (!string.IsNullOrEmpty(oldPct)) {
                        devTip = devTip.Replace(oldPct, livePotPct);
                    }
                }
            }

            string finalText = string.IsNullOrEmpty(devTip) && parentComponent != null ? parentComponent.ID : devTip;
            bool simHasData = CurrentSimulator != null && CurrentSimulator.Results != null && CurrentSimulator.GetNumberOfTicks() > 0;

            if (parentComponent != null)
            {
                if (parentComponent is DiffProbe dp)
                {
                    if (simHasData && dp.ConnectedNets.ContainsKey(1) && dp.ConnectedNets.ContainsKey(2))
                    {
                        int v1Id = CurrentSimulator.GetNetVoltageVarId(dp.ConnectedNets[1]);
                        int v2Id = CurrentSimulator.GetNetVoltageVarId(dp.ConnectedNets[2]);
                        if (v1Id != -1 && v2Id != -1)
                        {
                            double v1 = CurrentSimulator.GetValueOfVar(v1Id, 0);
                            double v2 = CurrentSimulator.GetValueOfVar(v2Id, 0);
                            
                            Quantity q1 = new Quantity("v", "V", "V") { Val = v1 };
                            Quantity q2 = new Quantity("v", "V", "V") { Val = v2 };
                            Quantity vDrop = new Quantity("v", "Voltage Drop", "V") { Val = v1 - v2 };
                            
                            finalText = $"Differential Probe\nRed Node (+): {q1.ToFixedString()}\nBlack Node (-): {q2.ToFixedString()}\nVoltage Drop: {vDrop.ToFixedString()}";
                        }
                    }
                    else
                    {
                        finalText = "Differential Probe\n(Connect both leads)";
                    }
                }
                else if (parentComponent is XYProbe xp) 
                {
                    if (simHasData && xp.ConnectedNets.ContainsKey(1) && xp.ConnectedNets.ContainsKey(2))
                    {
                        int vInId = CurrentSimulator.GetNetVoltageVarId(xp.ConnectedNets[1]);
                        int vOutId = CurrentSimulator.GetNetVoltageVarId(xp.ConnectedNets[2]);
                        if (vInId != -1 && vOutId != -1)
                        {
                            Quantity qIn = new Quantity("v", "V_in", "V") { Val = CurrentSimulator.GetValueOfVar(vInId, 0) };
                            Quantity qOut = new Quantity("v", "V_out", "V") { Val = CurrentSimulator.GetValueOfVar(vOutId, 0) };
                            
                            finalText = $"XY Transfer Probe\nX-Axis (Input): {qIn.ToFixedString()}\nY-Axis (Output): {qOut.ToFixedString()}";
                        }
                    }
                    else
                    {
                        finalText = "XY Transfer Probe\n(Connect both I and O leads)";
                    }
                }
                else if (parentComponent is CurrentProbe cp)
                {
                    if (simHasData && cp.TargetComponentId != null)
                    {
                        int cId = CurrentSimulator.GetComponentPinCurrentVarId(cp.TargetComponentId, cp.TargetPinNumber);
                        if (cId != -1)
                        {
                            double iVal = Math.Abs(CurrentSimulator.GetValueOfVar(cId, 0));
                            Quantity qI = new Quantity("i", "Current", "A") { Val = iVal };
                            finalText = $"Current Probe (-> {cp.TargetComponentId} Pin {cp.TargetPinNumber})\nCurrent: {qI.ToFixedString()}";
                        }
                    }
                    else
                    {
                        finalText = "Current Probe\n(Place on top of a component leg)";
                    }
                }
                else
                {
                    string lowerText = finalText.ToLower();
                    bool devProvidedVoltage = Regex.IsMatch(finalText, @"-?\d+(\.\d+)?\s*[kMmuµnp]?V", RegexOptions.IgnoreCase);
                    bool devProvidedCurrent = Regex.IsMatch(finalText, @"-?\d+(\.\d+)?\s*[kMmuµnp]?A", RegexOptions.IgnoreCase) || lowerText.Contains("current:");
                    string typeNameStr = parentComponent.GetType().Name.ToLower();
                    string compNameStr = parentComponent.ComponentType.ToLower();

                    if (pinNumber != -1)
                    {
                        string pName = GetPinName(parentComponent, pinNumber);
                        
                        bool devAlreadyNamedIt = lowerText.Contains("emitter") || lowerText.Contains("collector") || lowerText.Contains("base") || 
                                                 lowerText.Contains("anode") || lowerText.Contains("cathode") || lowerText.Contains("gate") || 
                                                 lowerText.Contains("drain") || lowerText.Contains("source") || lowerText.Contains("wiper") ||
                                                 lowerText.Contains("terminal");
                        
                        if (!devAlreadyNamedIt)
                        {
                            if (finalText.StartsWith("Pin " + pinNumber))
                            {
                                finalText = finalText.Substring(("Pin " + pinNumber).Length).Trim();
                            }
                            if (!lowerText.Contains(pName.ToLower()))
                            {
                                finalText = string.IsNullOrEmpty(finalText) ? $"[{pName}]" : $"[{pName}]\n" + finalText;
                            }
                        }
                    }

                    if (simHasData)
                    {
                        bool isPot = compNameStr.Contains("potentiometer");
                        double potR1 = 0.001, potR2 = 0.001;
                        double potV1 = 0, potVW = 0, potV2 = 0;
                        bool potValid = false;

                        if (isPot)
                        {
                            double rTotal = parentComponent.ComponentValue.Val;
                            double wiperRatio = 0.5;
                            
                            string pctSource = !string.IsNullOrEmpty(livePotPct) ? livePotPct : devTip;
                            Match mPct = Regex.Match(pctSource, @"(\d+(\.\d+)?)%");
                            if (mPct.Success) {
                                wiperRatio = double.Parse(mPct.Groups[1].Value) / 100.0;
                                if (wiperRatio < 0) wiperRatio = 0;
                                if (wiperRatio > 1) wiperRatio = 1;
                            }
                            
                            potR1 = Math.Max(rTotal * wiperRatio, 1e-3);
                            potR2 = Math.Max(rTotal * (1.0 - wiperRatio), 1e-3);
                            
                            if (parentComponent.ConnectedNets != null && 
                                parentComponent.ConnectedNets.ContainsKey(1) && 
                                parentComponent.ConnectedNets.ContainsKey(2) && 
                                parentComponent.ConnectedNets.ContainsKey(3))
                            {
                                int v1Id = CurrentSimulator.GetNetVoltageVarId(parentComponent.ConnectedNets[1]);
                                int vWId = CurrentSimulator.GetNetVoltageVarId(parentComponent.ConnectedNets[2]);
                                int v2Id = CurrentSimulator.GetNetVoltageVarId(parentComponent.ConnectedNets[3]);
                                
                                if (v1Id != -1 && vWId != -1 && v2Id != -1)
                                {
                                    potV1 = CurrentSimulator.GetValueOfVar(v1Id, 0);
                                    potVW = CurrentSimulator.GetValueOfVar(vWId, 0);
                                    potV2 = CurrentSimulator.GetValueOfVar(v2Id, 0);
                                    potValid = true;
                                }
                            }
                        }

                        bool isVoltageSource = typeNameStr.Contains("generator") || compNameStr.Contains("generator") || parentComponent is Probe;

                        if (parentComponent is Probe p && p.ConnectedNets.ContainsKey(1))
                        {
                            int varId = CurrentSimulator.GetNetVoltageVarId(p.ConnectedNets[1]);
                            if (varId != -1)
                            {
                                Quantity netVolt = new Quantity("v", "v", "V");
                                netVolt.Val = CurrentSimulator.GetValueOfVar(varId, 0);
                                finalText = $"Oscilloscope Probe\nVoltage: {netVolt.ToFixedString()}";
                            }
                        }
                        else
                        {
                            int pinCount = parentComponent.GetPinPositions().Count;
                            bool isSimple2Pin = (pinCount <= 2);

                            if (pinNumber != -1) 
                            {
                                if (!devProvidedVoltage && parentComponent.ConnectedNets != null && parentComponent.ConnectedNets.ContainsKey(pinNumber))
                                {
                                    int vId = CurrentSimulator.GetNetVoltageVarId(parentComponent.ConnectedNets[pinNumber]);
                                    if (vId != -1)
                                    {
                                        Quantity pVolt = new Quantity("v", "v", "V");
                                        pVolt.Val = CurrentSimulator.GetValueOfVar(vId, 0);
                                        if (!string.IsNullOrEmpty(finalText)) finalText += "\n";
                                        finalText += $"Voltage: {pVolt.ToFixedString()}";
                                    }
                                }

                                if (!devProvidedCurrent)
                                {
                                    int currentVarId = CurrentSimulator.GetComponentPinCurrentVarId(parentComponent.ID, pinNumber);
                                    if (currentVarId != -1)
                                    {
                                        Quantity compCurrent = new Quantity("i", "Current", "A");
                                        compCurrent.Val = Math.Abs(CurrentSimulator.GetValueOfVar(currentVarId, 0));
                                        if (!string.IsNullOrEmpty(finalText)) finalText += "\n";
                                        finalText += $"Current: {compCurrent.ToFixedString()}";
                                    }
                                    else if (isPot && potValid)
                                    {
                                        double iLeg = 0;
                                        if (pinNumber == 1) iLeg = (potV1 - potVW) / potR1;
                                        else if (pinNumber == 3) iLeg = (potV2 - potVW) / potR2;
                                        else if (pinNumber == 2) iLeg = -((potV1 - potVW) / potR1 + (potV2 - potVW) / potR2);
                                        
                                        Quantity compCurrent = new Quantity("i", "Current", "A");
                                        compCurrent.Val = Math.Abs(iLeg);
                                        if (!string.IsNullOrEmpty(finalText)) finalText += "\n";
                                        finalText += $"Current: {compCurrent.ToFixedString()}";
                                    }
                                    else if (compNameStr.Contains("integrated") || typeNameStr.Contains("ic"))
                                    {
                                        if (!string.IsNullOrEmpty(finalText)) finalText += "\n";
                                        finalText += "Current: N/A (IC Node)";
                                    }
                                }
                            }
                            else 
                            {
                                if (isSimple2Pin)
                                {
                                    string voltageDropText = "";
                                    if (!isVoltageSource && !(parentComponent is XYProbe) && parentComponent.ConnectedNets != null && parentComponent.ConnectedNets.ContainsKey(1) && parentComponent.ConnectedNets.ContainsKey(2))
                                    {
                                        int v1Id = CurrentSimulator.GetNetVoltageVarId(parentComponent.ConnectedNets[1]);
                                        int v2Id = CurrentSimulator.GetNetVoltageVarId(parentComponent.ConnectedNets[2]);
                                        if (v1Id != -1 && v2Id != -1)
                                        {
                                            double v1 = CurrentSimulator.GetValueOfVar(v1Id, 0);
                                            double v2 = CurrentSimulator.GetValueOfVar(v2Id, 0);
                                            Quantity vDrop = new Quantity("v", "Voltage Drop", "V");
                                            vDrop.Val = Math.Abs(v1 - v2);
                                            if (!finalText.Contains("Voltage Drop")) voltageDropText = $"\nVoltage Drop: {vDrop.ToFixedString()}";
                                        }
                                    }

                                    if (!devProvidedCurrent)
                                    {
                                        int currentVarId = CurrentSimulator.GetComponentPinCurrentVarId(parentComponent.ID, 1);
                                        if (currentVarId != -1)
                                        {
                                            Quantity compCurrent = new Quantity("i", "Current", "A");
                                            compCurrent.Val = Math.Abs(CurrentSimulator.GetValueOfVar(currentVarId, 0));
                                            if (!string.IsNullOrEmpty(finalText)) finalText += "\n";
                                            finalText += $"Current: {compCurrent.ToFixedString()}";
                                        }
                                    }
                                    finalText += voltageDropText; 
                                }
                                else if (pinCount <= 4)
                                {
                                if (isPot)
                                    {
                                        // Use pctSource instead of devTip to guarantee we have the live, animated percentage
                                        string pctSource = !string.IsNullOrEmpty(livePotPct) ? livePotPct : devTip;
                                        Match mPct = Regex.Match(pctSource, @"(\d+(\.\d+)?)%");
                                        
                                        if (mPct.Success) 
                                        {
                                            // Calculate the physical resistance split
                                            double wRatio = double.Parse(mPct.Groups[1].Value) / 100.0;
                                            if (wRatio < 0) wRatio = 0;
                                            if (wRatio > 1) wRatio = 1;
                                            
                                            double rTotal = parentComponent.ComponentValue.Val;
                                            
                                            Quantity r1Q = new Quantity("r", "R1", "Ω") { Val = rTotal * wRatio };
                                            Quantity r2Q = new Quantity("r", "R2", "Ω") { Val = rTotal * (1.0 - wRatio) };
                                            
                                            double p1 = wRatio * 100.0;
                                            double p2 = (1.0 - wRatio) * 100.0;

                                            // Only add the "Position:" line if it isn't already hiding at the top of the tooltip
                                            if (!finalText.Contains(mPct.Groups[0].Value)) {
                                                finalText += $"\nPosition: {mPct.Groups[0].Value}";
                                            }
                                            
                                            finalText += $"\nT1 ↔ Wiper: {r1Q.ToFixedString()} ({p1:0.#}%)";
                                            finalText += $"\nWiper ↔ T2: {r2Q.ToFixedString()} ({p2:0.#}%)";
                                        }

                                        if (potValid)
                                        {
                                            Quantity vDrop = new Quantity("v", "Voltage Drop (T1-T2)", "V");
                                            vDrop.Val = Math.Abs(potV1 - potV2);
                                            if (!finalText.Contains("Voltage Drop (T1-T2)")) finalText += $"\nVoltage Drop (T1-T2): {vDrop.ToFixedString()}";
                                        }
                                    }

                                    if (!devProvidedCurrent || isPot)
                                    {
                                        if (!finalText.Contains("--- Pin Data ---")) finalText += "\n\n--- Pin Data ---";
                                        
                                        foreach (int pNum in parentComponent.GetPinPositions().Keys)
                                        {
                                            string pName = GetPinName(parentComponent, pNum);
                                            string pData = $"\n{pName}:";
                                            
                                            if (parentComponent.ConnectedNets != null && parentComponent.ConnectedNets.ContainsKey(pNum))
                                            {
                                                int vId = CurrentSimulator.GetNetVoltageVarId(parentComponent.ConnectedNets[pNum]);
                                                if (vId != -1)
                                                {
                                                    Quantity pVolt = new Quantity("v", "v", "V");
                                                    pVolt.Val = CurrentSimulator.GetValueOfVar(vId, 0);
                                                    pData += $" {pVolt.ToFixedString()}";
                                                }
                                            }
                                            
                                            int currentVarId = CurrentSimulator.GetComponentPinCurrentVarId(parentComponent.ID, pNum);
                                            if (currentVarId != -1)
                                            {
                                                Quantity compCurrent = new Quantity("i", "Current", "A");
                                                compCurrent.Val = Math.Abs(CurrentSimulator.GetValueOfVar(currentVarId, 0));
                                                pData += $" | {compCurrent.ToFixedString()}";
                                            }
                                            else if (isPot && potValid)
                                            {
                                                double iLeg = 0;
                                                if (pNum == 1) iLeg = (potV1 - potVW) / potR1;
                                                else if (pNum == 3) iLeg = (potV2 - potVW) / potR2;
                                                else if (pNum == 2) iLeg = -((potV1 - potVW) / potR1 + (potV2 - potVW) / potR2);
                                                
                                                Quantity compCurrent = new Quantity("i", "Current", "A");
                                                compCurrent.Val = Math.Abs(iLeg);
                                                pData += $" | {compCurrent.ToFixedString()}";
                                            }
                                            else
                                            {
                                                pData += " | N/A (Internal)";
                                            }
                                            
                                            finalText += pData;
                                        }
                                    }
                                }
                                else
                                {
                                    if (!string.IsNullOrEmpty(finalText)) finalText += "\n";
                                    finalText += "(Hover specific pins to view details)";
                                }
                            }
                        }
                    }
                }
            }
            else if (parentWire != null)
            {
                finalText = "Wire";
                if (simHasData && parentWire.NetName != null)
                {
                    int varId = CurrentSimulator.GetNetVoltageVarId(parentWire.NetName);
                    if (varId != -1)
                    {
                        Quantity netVolt = new Quantity("v", "v", "V");
                        netVolt.Val = CurrentSimulator.GetValueOfVar(varId, 0);
                        finalText = $"Wire\nVoltage: {netVolt.ToFixedString()}"; 
                    }
                }
            }

            if (!string.IsNullOrEmpty(finalText))
            {
                string cleanText = finalText.Trim();
                
                if (lastRenderedText != cleanText)
                {
                    CustomTooltipText.Text = cleanText;
                    lastRenderedText = cleanText;
                }
                
                CustomTooltip.Visibility = Visibility.Visible;
                CustomTooltip.RenderTransform = new ScaleTransform(1.0 / CurrentZoomFactor, 1.0 / CurrentZoomFactor);
                Canvas.SetLeft(CustomTooltip, mousePos.X + (15.0 / CurrentZoomFactor));
                Canvas.SetTop(CustomTooltip, mousePos.Y + (15.0 / CurrentZoomFactor));
            }
            else
            {
                CustomTooltip.Visibility = Visibility.Hidden;
                lastRenderedText = "";
            }
        }
        private string GetCircuitState()
    {
    double valSum = 0;
    int geoHash = 0;
    foreach (var c in circuit.Components)
    {
        geoHash ^= Canvas.GetLeft(c).GetHashCode();
        geoHash ^= Canvas.GetTop(c).GetHashCode();
        geoHash ^= c.ID.GetHashCode();
        if (c.ComponentModel != null)
            geoHash ^= c.ComponentModel.GetHashCode();

        string type = c.GetType().Name;

        // Function generator parameters require a full sim restart to take effect
        // because simbe.exe has no live pipe command for waveform/frequency changes.
        // Potentiometer, LDR, and Switch are excluded because they use SendChangeMessage
        // and a restart would cause the lag/straight-line bug.
        if (type == "FunctionGenerator")
        {
            try
            {
                var t = c.GetType();
                string[] targetNames = { "Amplitude", "Offset", "Waveform", "Frequency", "DutyCycle" };
                foreach (string target in targetNames)
                {
                    var field = t.GetField(target, BindingFlags.Public | BindingFlags.Instance);
                    if (field != null)
                    {
                        var v = field.GetValue(c);
                        if (v != null) geoHash ^= v.ToString().GetHashCode();
                    }
                    var prop = t.GetProperty(target, BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null)
                    {
                        var v = prop.GetValue(c, null);
                        if (v != null) geoHash ^= v.ToString().GetHashCode();
                    }
                }
            }
            catch { }
        }

        if (type != "Switch" && type != "Potentiometer" && type != "LDR"
            && type != "CurrentProbe" && type != "DiffProbe" && type != "Probe" && type != "XYProbe")
        {
            valSum += c.ComponentValue.Val;
        }
    }
    foreach (var w in circuit.Wires)
    {
        if (w.NetName != null) geoHash ^= w.NetName.GetHashCode();
    }
    return $"{circuit.Components.Count}_{circuit.Wires.Count}_{valSum}_{geoHash}_{circuit.PositiveRailVoltage}";
}

        private void PopulateSamplesMenu(MenuItem rootItem, string rootPath)
        {
            foreach (string dir in System.IO.Directory.EnumerateDirectories(rootPath))
            {
                string dirName = System.IO.Path.GetFileName(dir);

                MenuItem subMenu = new MenuItem();
                subMenu.Header = dirName;
                PopulateSamplesMenu(subMenu, rootPath + "/" + dirName);
                rootItem.Items.Add(subMenu);
            }
            foreach (string file in System.IO.Directory.EnumerateFiles(rootPath,"*.bbrd"))
            {
                MenuItem menuItem = new MenuItem();
                menuItem.Header = System.IO.Path.GetFileNameWithoutExtension(file);
                menuItem.Click += sampleMenuItem_Click;
                rootItem.Items.Add(menuItem);
            }
        }

        private void PrepareForNewFile()
        {
            if (CurrentGraph != null)
            {
                CurrentGraph.Close();
                CurrentGraph = null;
            }
            if (CurrentXYGraph != null)
            {
                CurrentXYGraph.ForceClose = true;
                CurrentXYGraph.Close();
                CurrentXYGraph = null;
            }
            circuit.ClearUndoQueue();
            circuit.ClearCircuit(); 
            GlobalNativeCache.Clear(); 
            CustomTooltip.Visibility = Visibility.Hidden;
        }

        private void sampleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if(sender is MenuItem) 
            {
                StopSimulation.Command.Execute(null);
                if (DisplaySaveChangesDialog())
                {
                    MenuItem item = (MenuItem) sender;
                    string fileName = item.Header.ToString() + ".bbrd";
                    item = (MenuItem)item.Parent;
                    while (item != File_Samples)
                    {
                        fileName = item.Header.ToString() + "/" + fileName;
                        if (!(item.Parent is MenuItem)) break;
                        item = (MenuItem)item.Parent;
                    }
                    fileName = "res/samples/" + fileName;
                    LastOpenedFile = null;
                    
                    PrepareForNewFile();
                    circuit.LoadCircuit(fileName);
                    Title = "Breadboard Simulator (MI) - " + System.IO.Path.GetFileNameWithoutExtension(fileName);
                }
            }
        }

        private bool CheckForMissingResources(ref string missingResource)
        {
            foreach (string resource in ApplicationResources)
            {
                if (!System.IO.File.Exists(resource))
                {
                    missingResource = resource;
                    return true;
                }
            }
            return false;
        }

        private void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                e.Handled = true;
                MessageBoxResult r = MessageBox.Show("An internal error has occurred. Would you like to continue running?\r\n\r\n"
                    + e.Exception.Message, "Internal Error", MessageBoxButton.YesNo, MessageBoxImage.Error);

                if (r == MessageBoxResult.No)
                {
                    if (CurrentSimulator != null && CurrentSimulator.SimRunning)
                    {
                        try { CurrentSimulator.SimProcess.Kill(); }
                        catch { }
                    }
                    Application.Current.Shutdown();
                }
            }
            catch
            {
                if (CurrentSimulator != null && CurrentSimulator.SimRunning)
                {
                    try { CurrentSimulator.SimProcess.Kill(); }
                    catch { }
                }
                try { Application.Current.Shutdown(); }
                catch { }
            }
        }

        private void SimUpdate(object sender, EventArgs e)
        {
            string currentState = GetCircuitState();
            if (currentState != LastCircuitState && LastCircuitState != "")
            {
                StartSimulation(true); 
                return; 
            }

            if (CurrentSimulator.SimRunning)
            {
                int newLines = CurrentSimulator.Update();
                if (newLines > 0 && CurrentSimulator.Results != null && CurrentSimulator.GetNumberOfTicks() >= 5)
                {
                    if (CurrentGraph != null) CurrentGraph.PlotAll();
                    if (CurrentXYGraph != null) CurrentXYGraph.PlotAll();
                }

                NumberOfUpdates++;
                
                foreach (var component in circuit.Components)
                {
                    component.UpdateFromSimulation(NumberOfUpdates, CurrentSimulator,SimulationEvent.TICK);
                    
                    object tip = component.ToolTip;
                    string tipStr = "";
                    if (tip is string s) tipStr = s;
                    else if (tip is TextBlock tb) tipStr = tb.Text;
                    else if (tip is ToolTip tt)
                    {
                        if (tt.Content is string tts) tipStr = tts;
                        else if (tt.Content is TextBlock ttb) tipStr = ttb.Text;
                    }
                    if (!string.IsNullOrEmpty(tipStr))
                    {
                        GlobalNativeCache[component.ID + "_body"] = tipStr;
                    }
                }
                
                if (DrawArea.IsMouseOver)
                {
                    UpdateHoverBox(lastMousePos);
                }
                
                if ((NumberOfUpdates % 20) == 0)
                {
                    Quantity simulationTime = new Quantity("t", "Simulation Time", "s");
                    simulationTime.Val = CurrentSimulator.GetCurrentTime();
                    StatusText.Text = "Interactive Simulation Running | t=" + simulationTime.ToFixedString();
                }
            }
            else
            {
                if (isManuallyStopped)
                {
                    StatusText.Text = "Ready";
                    NumberOfUpdates = 0;
                    UpdateTimer.Stop();
                    CustomTooltip.Visibility = Visibility.Hidden;
                }
                else
                {
                    StatusText.Text = "Simulation Error (0Ω Short?). Adjust slider to auto-resume...";
                }
            }
            // PERFORMANCE FIX: Util.DoEvents() was completely removed from here. 
            // It caused layout/rendering stutter when placed inside a WPF Timer loop.
        }
        
        private void StartSimulation(bool seamless = false)
        {
            isManuallyStopped = false;
            double oldTime = 0;
            if (seamless && CurrentSimulator != null) 
            {
                oldTime = CurrentSimulator.GetCurrentTime();
            }

            if (CurrentSimulator.SimRunning) CurrentSimulator.Stop();
            if (!seamless && UpdateTimer.IsEnabled) UpdateTimer.Stop();
                
            string rawNetlist = circuit.GetNetlist();
            LastCircuitState = GetCircuitState(); 
            
            if (!seamless)
            {
                 NumberOfUpdates = 0;
                 GlobalNativeCache.Clear(); 
            }
            
            CurrentSimulator.Start(rawNetlist, circuit.SimulationSpeed.Val, seamless);
            
            if (CurrentSimulator.SimRunning)
            {
                while (!CurrentSimulator.VarNamesPopulated)
                {
                    CurrentSimulator.Update();
                    if (!CurrentSimulator.SimRunning) return;
                }
                
                StatusText.Text = "Interactive Simulation Running";
                
                if (CurrentGraph != null) 
                { 
                    if (!seamless) CurrentGraph.ResetAll(); 
                }
                else if (!seamless)
                {
                    CurrentGraph = new GraphView();
                    CurrentGraph.SecPerDiv.Val = circuit.SimulationSpeed.Val;
                }

                if (CurrentGraph != null)
                {
                    CurrentGraph.StartSim(CurrentSimulator);
                }

                foreach (Component c in circuit.Components)
                {
                    c.UpdateFromSimulation(0, CurrentSimulator, SimulationEvent.STARTED);
                }

                int numberOfTraces = 0;
                
                if (seamless && CurrentGraph != null) 
                {
                    foreach (Probe p in circuit.Components.OfType<Probe>()) 
                    {
                        int varId = p.ConnectedNets.ContainsKey(1) ? CurrentSimulator.GetNetVoltageVarId(p.ConnectedNets[1]) : -1;
                        CurrentGraph.UpdateTraceMapping(p.ID, varId);
                    }
                } 
                else 
                {
                    foreach (Probe p in circuit.Components.OfType<Probe>())
                    {
                        Trace t = new Trace();
                        int varId = p.ConnectedNets.ContainsKey(1) ? CurrentSimulator.GetNetVoltageVarId(p.ConnectedNets[1]) : -1;
                        
                        if (CurrentGraph != null && CurrentGraph.AddTrace(p.ID, varId, ref t))
                        {
                            numberOfTraces++; p.SetProbeColour(t.TraceBrush);
                        }
                    }
                }

                if (seamless && CurrentGraph != null) 
                {
                    foreach (DiffProbe dp in circuit.Components.OfType<DiffProbe>()) 
                    {
                        int v1 = dp.ConnectedNets.ContainsKey(1) ? CurrentSimulator.GetNetVoltageVarId(dp.ConnectedNets[1]) : -1;
                        int v2 = dp.ConnectedNets.ContainsKey(2) ? CurrentSimulator.GetNetVoltageVarId(dp.ConnectedNets[2]) : -1;
                        CurrentGraph.UpdateTraceMapping(dp.ID, v1, v2);
                    }
                } 
                else 
                {
                    foreach (DiffProbe dp in circuit.Components.OfType<DiffProbe>())
                    {
                        Trace t = new Trace();
                        int v1 = dp.ConnectedNets.ContainsKey(1) ? CurrentSimulator.GetNetVoltageVarId(dp.ConnectedNets[1]) : -1;
                        int v2 = dp.ConnectedNets.ContainsKey(2) ? CurrentSimulator.GetNetVoltageVarId(dp.ConnectedNets[2]) : -1;
                        Brush savedColor = dp.ProbeColour;
                        
                        if (CurrentGraph != null && CurrentGraph.AddTrace(dp.ID, v1, v2, ref t))
                        {
                            numberOfTraces++; 
                            if (savedColor != Brushes.Transparent) CurrentGraph.UpdateTraceMapping(dp.ID, v1, v2, savedColor);
                            else dp.SetProbeColour(t.TraceBrush); 
                        }
                    }
                }

                if (seamless && CurrentGraph != null) 
                {
                    foreach (CurrentProbe cp in circuit.Components.OfType<CurrentProbe>()) 
                    {
                        cp.BindTarget(circuit);
                        int varId = CurrentSimulator.GetComponentPinCurrentVarId(cp.TargetComponentId, cp.TargetPinNumber);
                        CurrentGraph.UpdateTraceMapping(cp.ID, varId);
                    }
                } 
                else 
                {
                    foreach (CurrentProbe cp in circuit.Components.OfType<CurrentProbe>())
                    {
                        cp.BindTarget(circuit);
                        Trace t = new Trace();
                        int varId = CurrentSimulator.GetComponentPinCurrentVarId(cp.TargetComponentId, cp.TargetPinNumber);
                        Brush savedColor = cp.ProbeColour;
                        
                        if (CurrentGraph != null && CurrentGraph.AddTrace(cp.ID, varId, ref t))
                        {
                            numberOfTraces++; 
                            if (savedColor != Brushes.Transparent) CurrentGraph.UpdateTraceMapping(cp.ID, varId, -1, savedColor);
                            else cp.SetProbeColour(t.TraceBrush); 
                        }
                    }
                }

              // ── Parametric (XY & IV) Probes ─────────────────────────────────────────
                var xyProbes = circuit.Components.OfType<XYProbe>().ToList();
                var ivProbes = circuit.Components.OfType<IVProbe>().ToList();

                if (xyProbes.Count > 0 || ivProbes.Count > 0)
                {
                    if (CurrentXYGraph == null)
                        CurrentXYGraph = new XYGraphView();
                    else if (!seamless)
                        CurrentXYGraph.ResetAll();

                    if (!seamless) CurrentXYGraph.StartSim(CurrentSimulator);

                    // 1. Process standard XY Probes (V/V)
                    foreach (XYProbe xp in xyProbes)
                    {
                        int inId = xp.ConnectedNets.ContainsKey(1) ? CurrentSimulator.GetNetVoltageVarId(xp.ConnectedNets[1]) : -1;
                        int outId = xp.ConnectedNets.ContainsKey(2) ? CurrentSimulator.GetNetVoltageVarId(xp.ConnectedNets[2]) : -1;
                        
                        if (seamless) {
                            CurrentXYGraph.UpdateXYTrace(xp.ID, inId, -1, outId);
                        } else {
                            Brush assignedColour = CurrentXYGraph.AddXYTrace(xp.ID, inId, -1, outId, xp.ProbeColour);
                            if (xp.ProbeColour == Brushes.Transparent) xp.SetProbeColour(assignedColour);
                            numberOfTraces++;
                        }
                    }

                    // 2. Process new IV Probes (I/V)
                    foreach (IVProbe ivp in ivProbes)
                    {
                        ivp.BindTarget(circuit);
                        
                        Component target = circuit.Components.FirstOrDefault(c => c.ID == ivp.TargetComponentId);
                        if (target != null)
                        {
                            int v1Id = target.ConnectedNets.ContainsKey(1) ? CurrentSimulator.GetNetVoltageVarId(target.ConnectedNets[1]) : -1;
                            int v2Id = target.ConnectedNets.ContainsKey(2) ? CurrentSimulator.GetNetVoltageVarId(target.ConnectedNets[2]) : -1;
                            int iId = CurrentSimulator.GetComponentPinCurrentVarId(ivp.TargetComponentId, ivp.TargetPinNumber);

                            bool invertY = (ivp.TargetPinNumber == 2); 

                            if (seamless) {
                                CurrentXYGraph.UpdateXYTrace(ivp.ID, v1Id, v2Id, iId);
                            } else {
                                Brush assignedColour = CurrentXYGraph.AddXYTrace(
                                    ivp.ID, v1Id, v2Id, iId, ivp.ProbeColour, 
                                    XYGraphView.AxisUnit.Volts, XYGraphView.AxisUnit.Amps, invertY
                                );
                                if (ivp.ProbeColour == Brushes.Transparent) ivp.SetProbeColour(assignedColour);
                                numberOfTraces++;
                            }
                        }
                    }

                    if (!seamless && !CurrentXYGraph.IsVisible) CurrentXYGraph.Show();
                }
                // ────────────────────────────────────────────────────────────────────────

                
                if(numberOfTraces > 0 && CurrentGraph != null && !CurrentGraph.IsVisible) CurrentGraph.Show();
                if (!UpdateTimer.IsEnabled) UpdateTimer.Start();
            }
        }

        //Populate a TreeViewItem with items following a preferred value series that represent a given component; between the magnitudes specified by magBegin and magEnd
        private void PopulateMenu(TreeViewItem root, double[] series, int magBegin, int magEnd, string suffix, string componentType)
        {
            for (int mag = magBegin; mag <= magEnd; mag++)
            {
                foreach (double seriesValue in series)
                {
                    double val = seriesValue * Math.Pow(10, mag);
                    Quantity q = new Quantity();
                    q.Val = val;
                    TreeViewItem newItem = new TreeViewItem();
                    newItem.Header = new ComponentData(componentType, val, q.ToString() + suffix);
                    root.Items.Add(newItem);
                }
            }
        }

        //Populate a TreeViewItem with items from a model database
        private void PopulateMenuWithModels(TreeViewItem root, string componentType, string modelFile, string category = null, string suffix = "")
        {
            List<string> modelNames = Component.GetModelNames(modelFile, category);
            foreach(string model in modelNames) {
                    TreeViewItem newItem = new TreeViewItem();
                    newItem.Header = new ComponentData(componentType, 0, model + suffix, model);
                    root.Items.Add(newItem);
            }
        }

        //Adds a given breadboard, identified by ID
        private void AddBreadboard(int breadboardNumber)
        {
            int col = breadboardNumber % Constants.BreadboardsPerRow;
            int row = breadboardNumber / Constants.BreadboardsPerRow;
            Breadboard newBb = new Breadboard(circuit);
            Canvas.SetLeft(newBb, col * Constants.BreadboardSpacingX + Constants.BreadboardStartX);
            Canvas.SetTop(newBb, row * Constants.BreadboardSpacingY + Constants.BreadboardStartY);
            DrawArea.Children.Add(newBb);
            Breadboards[breadboardNumber] = newBb;
        }

        //Removes a given breadboard, identified by ID
        private void RemoveBreadboard(int breadboardNumber)
        {
            if (Breadboards.ContainsKey(breadboardNumber))
            {
                DrawArea.Children.Remove(Breadboards[breadboardNumber]);
                Breadboards.Remove(breadboardNumber);
            }
        }

        //Modifies the set of breadboards such that there are a certain number of breadboards
        public void SetNumberOfBreadboards(int newNumberOfBreadboards)
        {
            int currentNumberOfBreadboards = Breadboards.Count;
            if (newNumberOfBreadboards > currentNumberOfBreadboards)
            {
                for (int i = currentNumberOfBreadboards; i < newNumberOfBreadboards; i++)
                {
                    AddBreadboard(i);
                }
            }
            else if (newNumberOfBreadboards < currentNumberOfBreadboards)
            {
                for (int i = currentNumberOfBreadboards - 1; i >= newNumberOfBreadboards; i--)
                {
                    RemoveBreadboard(i);
                }
            }
        }

        public int GetNumberOfBreadboards()
        {
            return Breadboards.Count;
        }

        private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            CurrentZoomFactor *= 1 + ZoomFactorDelta;
            DrawArea.LayoutTransform = new ScaleTransform(CurrentZoomFactor, CurrentZoomFactor);
            CScroll.ScrollToHorizontalOffset(CScroll.HorizontalOffset * (1 + ZoomFactorDelta));
            CScroll.ScrollToVerticalOffset(CScroll.VerticalOffset * (1 + ZoomFactorDelta));
        }

        private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            CurrentZoomFactor /= (1 + ZoomFactorDelta);
            DrawArea.LayoutTransform = new ScaleTransform(CurrentZoomFactor, CurrentZoomFactor);
            CScroll.ScrollToHorizontalOffset(CScroll.HorizontalOffset / (1 + ZoomFactorDelta));
            CScroll.ScrollToVerticalOffset(CScroll.VerticalOffset / (1 + ZoomFactorDelta));
        }
        
        private void ShowHideGraph_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (CurrentGraph == null)
            {
                CurrentGraph = new GraphView();
                CurrentGraph.SecPerDiv.Val = circuit.SimulationSpeed.Val;
                CurrentGraph.Show();
            }
            else
            {
                if (CurrentGraph.IsVisible)
                    CurrentGraph.Hide();
                else
                    CurrentGraph.Show();
            }
        }

        private void ShowSettings_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            CircuitProperties p = new CircuitProperties();
            p.SetProperties(GetNumberOfBreadboards(), circuit.SimulationSpeed.Val, circuit.PositiveRailVoltage, circuit.NegativeRailVoltage);
            p.ShowDialog();
            SetNumberOfBreadboards(p.GetSelectedNumberOfBreadboards());
            circuit.SimulationSpeed.Val = p.GetSelectedSimulationSpeed();
            circuit.PositiveRailVoltage = p.GetPositiveRailVoltage();
            circuit.NegativeRailVoltage = p.GetNegativeRailVoltage();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DisplaySaveChangesDialog())
            {
                CurrentSimulator.Stop();
                if (CurrentGraph != null)
                    CurrentGraph.Close();
                if (CurrentXYGraph != null)
                    CurrentXYGraph.Close();
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    circuit.PurgeUnfinished();
                    break;
                default:
                    if (circuit.GetSelectedComponent() != null)
                        circuit.GetSelectedComponent().Component_KeyDown(sender, e);
                    else if (circuit.GetSelectedWire() != null)
                        circuit.GetSelectedWire().Wire_KeyDown(sender, e);
                    break;
            }
        }

        public ComponentData GetSelectedComponent()
        {
            if (DevicePicker.SelectedItem is TreeViewItem ti && ti.Header is ComponentData cd) 
            {
                return cd;
            } 
            return null;
        }

        //Called when any of the tool items is clicked
        private void ToolChange_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            string CommandName = ((RoutedCommand)e.Command).Name;
            //Command name is of the form SelectToolTOOL
            SelectTool(CommandName.Substring(10));
            e.Handled = true;
        }

        public void UpdatePrompt()
        {
            switch (SelectedTool)
            {
                case "SELECT":
                    if (circuit.GetSelectedWire() != null)
                        Prompt.Text = "Wire selected";
                    else if (circuit.GetSelectedComponent() != null)
                    {
                        Component c = circuit.GetSelectedComponent();
                        Prompt.Text = c.ComponentType + " " + c.ID + " selected";
                        if (c.ComponentModel != "")
                            Prompt.Text += ", model " + c.ComponentModel;
                        if (c.ComponentValue.ID != "")
                            Prompt.Text += ", value " + c.ComponentValue.ToString();
                    }
                    else
                        Prompt.Text = "Select an object";
                    break;
                case "INTERACT":
                    Prompt.Text = "Click on a component to interact with it";
                    break;
                case "WIRE":
                    Prompt.Text = Breadboard.StartedWire ? "Click to finish placing wire" : "Click to start placing wire";
                    break;
                case "COMPONENT":
                    ComponentData selectedComponent = GetSelectedComponent();
                    if (selectedComponent != null)
                        Prompt.Text = Breadboard.StartedLeadedComponent ? "Click to finish placing " + selectedComponent.Label : "Click to place " + selectedComponent.Label;
                    else
                        Prompt.Text = "Select a component type from the panel on the left";
                    break;
                case "DELETE":
                    Prompt.Text = "Click on a component or wire to delete it";
                    break;
            }
        }

        //Selects a new tool
        public void SelectTool(string toolName)
        {
            circuit.PurgeUnfinished();
            circuit.DeselectAll();
            foreach (ToggleButton t in Toolbox.Items.OfType<ToggleButton>())
                t.IsChecked = false;
            SelectedTool = toolName.ToUpper();
            switch (SelectedTool)
            {
                case "SELECT":
                    Tool_SELECT.IsChecked = true;
                    DrawArea.Cursor = Cursors.Arrow;
                    break;
                case "INTERACT":
                    Tool_INTERACT.IsChecked = true;
                    DrawArea.Cursor = new Cursor(Environment.CurrentDirectory + "/res/tools/interact.cur");
                    break;
                case "COMPONENT":
                    Tool_COMPONENT.IsChecked = true;
                    DrawArea.Cursor = new Cursor(Environment.CurrentDirectory + "/res/tools/component.cur");
                    break;
                case "WIRE":
                    Tool_WIRE.IsChecked = true;
                    DrawArea.Cursor = new Cursor(Environment.CurrentDirectory + "/res/tools/wire.cur");
                    break;
                case "DELETE":
                    Tool_DELETE.IsChecked = true;
                    DrawArea.Cursor = new Cursor(Environment.CurrentDirectory + "/res/tools/delete.cur");
                    break;
            }
            UpdatePrompt();
        }

        private void DevicePicker_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (GetSelectedComponent() != null)
                SelectTool("COMPONENT");
        }
        
        private void RunSimulation_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            StartSimulation(CurrentSimulator.SimRunning);
        }
        
        //If necessary, displays a warning about unsaved changes. Returns false when cancelled.
        private bool DisplaySaveChangesDialog()
        {
            if (circuit.UndoQueueChangedFlag)
            {
                MessageBoxResult result = MessageBox.Show("There are unsaved changes to the breadboard. Would you like to save them now?",
                    "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    File_Save.Command.Execute(null);
                    return true;
                }
                else if (result == MessageBoxResult.No)
                    return true;
                else
                    return false;
            }
            else
                return true;
        }

        private void NewFile_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            isManuallyStopped = true;
            StopSimulation.Command.Execute(null);
            if (DisplaySaveChangesDialog())
            {
                LastOpenedFile = null;
                Title = "Breadboard Simulator (MI) - Untitled";
                PrepareForNewFile();
            }
        }

        private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            isManuallyStopped = true;
            StopSimulation.Command.Execute(null);
            if (DisplaySaveChangesDialog())
            {
                Microsoft.Win32.OpenFileDialog openDialog = new Microsoft.Win32.OpenFileDialog();
                openDialog.CheckFileExists = true;
                openDialog.Filter = "Breadboards (.bbrd)|*.bbrd";
                openDialog.DefaultExt = ".bbrd";
                if (openDialog.ShowDialog() == true)
                {
                    string filename = openDialog.FileName;
                    PrepareForNewFile();
                    circuit.LoadCircuit(filename);
                    LastOpenedFile = filename;
                    Title = "Breadboard Simulator (MI) - " + System.IO.Path.GetFileNameWithoutExtension(filename);
                }
            }
        }

        private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            bool? result = false;
            string filename;
            bool saveAsCommand = (e != null && ((RoutedCommand)e.Command).Name == "SaveAs");
            if (!saveAsCommand && (LastOpenedFile != null))
            {
                result = true;
                filename = LastOpenedFile;
            }
            else
            {
                Microsoft.Win32.SaveFileDialog saveDialog = new Microsoft.Win32.SaveFileDialog();
                saveDialog.Filter = "Breadboards (.bbrd)|*.bbrd";
                saveDialog.DefaultExt = ".bbrd";
                if (LastOpenedFile != null)
                {
                    saveDialog.InitialDirectory = System.IO.Path.GetDirectoryName(LastOpenedFile);
                    saveDialog.FileName = System.IO.Path.GetFileName(LastOpenedFile);
                }
                else
                    saveDialog.FileName = "Untitled";
                result = saveDialog.ShowDialog();
                filename = saveDialog.FileName;
            }
            if (result == true)
            {
                circuit.SaveCircuit(filename);
                circuit.UndoQueueChangedFlag = false;
                LastOpenedFile = filename;
                Title = "Breadboard Simulator (MI) - " + System.IO.Path.GetFileNameWithoutExtension(filename);
            }
        }

        private void StopSimulation_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            isManuallyStopped = true;
            CurrentSimulator.Stop();
            UpdateTimer.Stop();
            NumberOfUpdates = 0;
            StatusText.Text = "Ready";

            foreach (Component c in circuit.Components)
            {
                c.UpdateFromSimulation(0, CurrentSimulator, SimulationEvent.STOPPED);
            }

            if (DrawArea.IsMouseOver)
            {
                UpdateHoverBox(Mouse.GetPosition(DrawArea));
            }
        }

        private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            circuit.UndoLast();
        }

        private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            circuit.RedoLast();
        }

        private void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = circuit.CanRedoLast();
        }

        private void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = circuit.CanUndoLast();
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
        }

        private void HelpAbout_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            MessageBox.Show(System.IO.File.ReadAllText("res/about.txt").Replace("{version}",Assembly.GetExecutingAssembly().GetName().Version.ToString()),"About Breadboard Simulator");
        }

        private void HelpContents_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("res\\doc\\index.html");
        }

        private void HelpGS_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("res\\doc\\getting-started.html");
        }
    }

    //Commands that are not built in to WPF, but that are needed for this application
    public static class CustomCommands
    {
        public static readonly RoutedUICommand CircuitSettings = new RoutedUICommand("Settings","CircuitSettings",typeof(MainWindow),
          new InputGestureCollection { new KeyGesture(Key.F4) });
        public static readonly RoutedUICommand RunSimulation = new RoutedUICommand("Run", "RunSimulation", typeof(MainWindow),
            new InputGestureCollection { new KeyGesture(Key.F5) });
        public static readonly RoutedUICommand StopSimulation = new RoutedUICommand("Stop", "StopSimulation", typeof(MainWindow),
            new InputGestureCollection { new KeyGesture(Key.F6) });
        public static readonly RoutedUICommand ShowHideGraph = new RoutedUICommand("Show/Hide Graph", "ShowHideGraph", typeof(MainWindow),
            new InputGestureCollection { new KeyGesture(Key.G, ModifierKeys.Control) });

        //Tool selection commands
        public static readonly RoutedUICommand SelectToolSELECT = new RoutedUICommand("Select", "SelectToolSELECT", typeof(MainWindow),
            new InputGestureCollection { new KeyGesture(Key.Escape, ModifierKeys.Shift) });
        public static readonly RoutedUICommand SelectToolINTERACT = new RoutedUICommand("Interact", "SelectToolINTERACT", typeof(MainWindow),
            new InputGestureCollection { new KeyGesture(Key.I, ModifierKeys.Control) });
        public static readonly RoutedUICommand SelectToolCOMPONENT = new RoutedUICommand("Place Components", "SelectToolCOMPONENT", typeof(MainWindow),
            new InputGestureCollection { new KeyGesture(Key.A, ModifierKeys.Control) });
        public static readonly RoutedUICommand SelectToolWIRE = new RoutedUICommand("Place Wires", "SelectToolWIRE", typeof(MainWindow),
            new InputGestureCollection { new KeyGesture(Key.W, ModifierKeys.Control) });
        public static readonly RoutedUICommand SelectToolDELETE = new RoutedUICommand("Delete", "SelectToolDELETE", typeof(MainWindow),
            new InputGestureCollection { new KeyGesture(Key.Delete, ModifierKeys.Shift) });

        public static readonly RoutedUICommand HelpContents = new RoutedUICommand("Contents", "HelpContents", typeof(MainWindow), new InputGestureCollection { new KeyGesture(Key.F1) });
        public static readonly RoutedUICommand HelpGS = new RoutedUICommand("Getting Started", "HelpGS", typeof(MainWindow));
        public static readonly RoutedUICommand HelpAbout = new RoutedUICommand("About", "HelpAbout", typeof(MainWindow));
    }
}