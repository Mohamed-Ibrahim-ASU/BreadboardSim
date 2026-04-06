using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Windows;
using System.Globalization;

namespace SimGUI
{
    public class Simulator
    {
        public Process SimProcess;
        private Thread LineReaderThread;
        public const double BufferSize = 50000;

        public List<string> VariableNames;
        public List<List<double>> Results;

        private object LineBufferLock = new object();
        
        private List<List<double>> ResultBuffer = new List<List<double>>();
        private List<string> EventBuffer = new List<string>();

        public bool SimRunning = false;
        private bool UpdatesPaused = false;
        public bool VarNamesPopulated = false;

        // Variables for native background stitching
        private double TimeOffset = 0;
        private int[] VarMap = new int[0];
        private int transientSkipCounter = 0;
        private bool IsSeamlessTransition = false;

        public Simulator() { }

        void SimulatorProcess_Exited(object sender, EventArgs e)
        {
            SimRunning = false;
            try { LineReaderThread.Abort(); } catch { }
        }

        public void LineReader()
        {
            while (true)
            {
                string line = SimProcess.StandardOutput.ReadLine();
                if (line == null) continue;

                string[] splitLine = line.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (splitLine.Length >= 2)
                {
                    if (splitLine[0] == "RESULT")
                    {
                        string[] splitData = splitLine[1].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        List<double> values = new List<double>(splitData.Length);
                        bool parseError = false;
                        
                        for (int i = 0; i < splitData.Length; i++)
                        {
                            if (Double.TryParse(splitData[i], NumberStyles.Any, CultureInfo.InvariantCulture, out double val)) 
                            {
                                values.Add(val);
                            } 
                            else 
                            {
                                parseError = true; 
                                break;
                            }
                        }
                        
                        lock (LineBufferLock)
                        {
                            if (parseError) EventBuffer.Add("PARSE_ERROR");
                            else ResultBuffer.Add(values);
                        }
                    }
                    else
                    {
                        lock (LineBufferLock)
                        {
                            EventBuffer.Add(line); 
                        }
                    }
                }
            }
        }

        public void Start(string netlist, double speed = 1, bool seamless = false)
        {
            IsSeamlessTransition = seamless;

            if (seamless) 
            {
                TimeOffset = GetCurrentTime();
                transientSkipCounter = 50;
                if (Results != null && VariableNames != null && VariableNames.Count > 0)
                {
                    List<double> sentinel = new List<double>(VariableNames.Count);
                    for (int i = 0; i < VariableNames.Count; i++)
                        sentinel.Add(i == 0 ? TimeOffset : double.NaN);
                    Results.Add(sentinel);
                }
            } 
            else 
            {
                Results = new List<List<double>>();
                VariableNames = new List<string>();
                TimeOffset = 0;
                transientSkipCounter = 0;
            }

            SimProcess = new Process();
            SimProcess.StartInfo.UseShellExecute = false;
            SimProcess.StartInfo.FileName = "res/simbe.exe";
            SimProcess.StartInfo.CreateNoWindow = true;
            SimProcess.StartInfo.RedirectStandardInput = true;
            SimProcess.StartInfo.RedirectStandardOutput = true;

            ResultBuffer = new List<List<double>>();
            EventBuffer = new List<string>();
            
            LineReaderThread = new Thread(new ThreadStart(LineReader));
            VarNamesPopulated = false;
            SimProcess.Exited += SimulatorProcess_Exited;

            SimProcess.StartInfo.Arguments = speed.ToString();
            SimProcess.Start();
            SimProcess.PriorityBoostEnabled = true;
            SimProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
            SimProcess.StandardInput.WriteLine(netlist);
            SimProcess.StandardInput.WriteLine("START " + speed.ToString());

            SimRunning = true;
            UpdatesPaused = false;
            LineReaderThread.Start();
        }

        public int Update()
        {
            int numberOfLines = 0;
            
            if (!UpdatesPaused)
            {
                List<List<double>> newResults = null;
                List<string> newEvents = null;

                lock (LineBufferLock)
                {
                    if (ResultBuffer.Count > 0)
                    {
                        newResults = ResultBuffer;
                        ResultBuffer = new List<List<double>>(); 
                    }
                    if (EventBuffer.Count > 0)
                    {
                        newEvents = EventBuffer;
                        EventBuffer = new List<string>();
                    }
                }

                if (newEvents != null)
                {
                    foreach (string line in newEvents)
                    {
                        if (line == "PARSE_ERROR")
                        {
                            UpdatesPaused = true;
                            MessageBox.Show("The simulator returned invalid data during simulation. The simulation will now stop.", "Simulation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            Stop(); return 0;
                        }

                        string[] splitLine = line.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        string[] splitData = splitLine[1].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                        if (splitLine[0] == "VARS")
                        {
                            if (IsSeamlessTransition) 
                            {
                                VarMap = new int[splitData.Length];
                                for (int i = 0; i < splitData.Length; i++) 
                                {
                                    if (i == 0) 
                                    { 
                                        VarMap[i] = 0; 
                                        continue; 
                                    } 
                                    
                                    int existingIndex = VariableNames.IndexOf(splitData[i]);
                                    if (existingIndex != -1) 
                                    {
                                        VarMap[i] = existingIndex;
                                    } 
                                    else 
                                    {
                                        VariableNames.Add(splitData[i]);
                                        VarMap[i] = VariableNames.Count - 1;
                                    }
                                }
                            } 
                            else 
                            {
                                VariableNames = new List<string>(splitData);
                                VarMap = new int[splitData.Length];
                                for (int i = 0; i < splitData.Length; i++) 
                                {
                                    VarMap[i] = i;
                                }
                            }
                            VarNamesPopulated = true;
                        }
                        else if (splitLine[0] == "ERROR")
                        {
                            bool recoverable = (int.Parse(splitData[0]) == 1);
                            UpdatesPaused = true;
                            if (recoverable)
                            {
                                MessageBoxResult result = MessageBoxResult.No;
                                if (splitData[1] == "CONVERGENCE")
                                    result = MessageBox.Show("The simulator encountered a convergence failure... Would you like to continue?", "Simulation Error", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                                else
                                    result = MessageBox.Show("The simulator encountered an error... Would you like to continue?", "Simulation Error", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                                
                                if (result == MessageBoxResult.Yes) { SimProcess.StandardInput.WriteLine("CONTINUE"); UpdatesPaused = false; }
                                else Stop();
                            }
                            else
                            {
                                MessageBox.Show("The simulator encountered a fatal error...", "Simulation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                Stop();
                            }
                        }
                    }
                }

                if (newResults != null && newResults.Count > 0)
                {
                    foreach (var rawVals in newResults) 
                    {
                        if (transientSkipCounter > 0) 
                        {
                            transientSkipCounter--;
                            continue; // Toss out the ugly spikes!
                        }
                        
                        List<double> mappedVals = new List<double>(VariableNames.Count);
                        for (int i = 0; i < VariableNames.Count; i++) 
                        {
                            mappedVals.Add(0.0); 
                        }
                        
                        for (int i = 0; i < rawVals.Count; i++) 
                        {
                            if (i == 0) 
                            {
                                mappedVals[0] = rawVals[0] + TimeOffset;
                            }
                            else if (i < VarMap.Length) 
                            {
                                int targetIdx = VarMap[i];
                                if (targetIdx < mappedVals.Count) 
                                {
                                    mappedVals[targetIdx] = rawVals[i];
                                }
                            }
                        }
                        Results.Add(mappedVals);
                        numberOfLines++;
                    }

                    int over = Results.Count - (int)BufferSize;
                    if (over > 0)
                    {
                        Results.RemoveRange(0, over);
                    }
                }
            }
            return numberOfLines;
        }

        public double GetCurrentTime(int tick = 0)
        {
            if ((Results.Count - 1 + tick) > 0) return Results[Results.Count - 1 + tick][0];
            else return 0;
        }

        public int GetNumberOfTicks() { return Results.Count; }

        public int GetNetVoltageVarId(string netName)
        {
            string varName = "V(" + netName + ")";
            if (VariableNames.Contains(varName)) return VariableNames.IndexOf(varName);
            else return -1;
        }

        public int GetComponentPinCurrentVarId(string componentName, int pin)
        {
            string varName = "I(" + componentName + "." + (pin - 1) + ")";
            if (VariableNames.Contains(varName)) return VariableNames.IndexOf(varName);
            else return -1;
        }

        public double GetValueOfVar(int varId, int tick)
        {
            if (((Results.Count - 1 + tick) > 0) && (varId >= 0))
            {
                if (Results[Results.Count - 1 + tick].Count > varId) return Results[Results.Count - 1 + tick][varId];
                else return 0;
            }
            else return 0;
        }

        public void SendChangeMessage(string message)
        {
            if (SimProcess != null && !SimProcess.HasExited) SimProcess.StandardInput.WriteLine(message);
        }

        public void Stop()
        {
            if (LineReaderThread != null && LineReaderThread.IsAlive) LineReaderThread.Abort();
            if (SimRunning)
            {
                if (!SimProcess.HasExited) SimProcess.Kill();
                SimRunning = false;
            }
        }

        ~Simulator()
        {
            try { if (SimProcess != null && !SimProcess.HasExited) SimProcess.Kill(); } catch { }
            try { if (LineReaderThread != null && LineReaderThread.IsAlive) LineReaderThread.Abort(); } catch { }
        }
    }
}