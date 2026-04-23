# Breadboard Simulator

This is my take on a fork of Breadboard Simulator, I noticed the program was missing a few features I wanted to use in my Electronics Course, so I created this fork to add them.

## [Download Here](https://github.com/Mohamed-Ibrahim-ASU/BreadboardSim/releases/latest/download/BreadboardSim-MI.zip)

# Additions
## Components
### Input Devices - Function Generator
 ※  The function generator can be used to generate sinusoidal, square, or triangle waves of any frequency and amplitude with a DC offset.
 
 ※ Included in the function generator is a "Power-On Transient" Property that allows the circuit to start from 0 and ramp up to the steady state voltage across 5 periods.
 
 ※ The function generator size is perfect to fit in the **middle** of the breadboard. ***PLEASE*** don't short the function generator.
  
※ When using the function generator, make sure the function generator is grounded and the simulation speed is low enough to allow for proper generation of the functions. 

※ Ideally 5x the maximum frequency is what I recommend. 

※ 10x should be the absolute maximum; any faster and the shape will be significantly distorted.  
### New Components
**As used in our Electronics Lab:**

※ Added 1N4007 Diode

※ Added customisable Zener Diodes

※ Added BC639 NPN Transistor
※ Added BC337 NPN Transistor
※ Added BC327 PNP Transistor


※ Added LM358 OP-AMP
    
	
## Probes
### Voltage Difference Probes
  ※ The voltage difference probes consist of a positive and a negative probe to measure the voltage drop between any two nodes on the breadboard and plots it on the graph; the usage is almost identical to the oscilloscope probe currently implemented. 
### Current Probe (BETA) 
 ※  The current probe can be used to measure the current going through a lead of most components. Due to SPICE treating wires as nodes, the current probe cannot measure the current flowing through a "wire".

※   To use the current probe, you should place it on an end lead/wire of a component (ie: a resistor). If it doesn't work, try again in another place.

### XY Transfer Probe (BETA)
※ The XY Transfer Probes can be used to plot the Input/Output Characteristics between any two points of voltage on the circuit.
※ To use the XY Transfer Probes, you select the probe, then select the input node, then the output node. Upon running the simulator, the new XY Graph will popup with the Vout/Vin plot. 

### IV Probe
※ The IV Probe can be used to plot the Current/Voltage characteristics of any component in the circuit with respect to the voltages and currents going through it in that specific configuration. 
※ To use the IV Probe, just select the probe and place it on one end of the component you want to analyse. Upon running the XY Graph will popup with the IV characteristics of the components.
## XY Graph
※ To make sure the XY and IV Probes are plotted properly, I introduced an XY Graph. 
※ Upon first running, the graph may need some time to calibrate and autoscale, make sure your simulation speed is fast enough if you would like to see the overall shape of the graph, or slow it down to see it work in real time. 
※ The graph automatically chooses the "best" starting point for the 0A and 0V axes separately to ensure as much information is displayed as possible, so it is normal that they do not line up most times. It may need some getting used to, but it is intentional.
## General Improvements
### Updated Wires 
 ※  Wires can now be placed diagonally, and the placement of wires should hopefully be easier.
### Updated Leaded Components 
  ※ Leaded components can also be placed diagonally.
### Updated Capacitors
 ※  Capacitors now have leads and as such also can be placed between any two nodes, not just two adjacent nodes.
 ※  Capacitors now have an Initial Voltage Property to allow for the functioning of circuits like astable multivibrators.
 ※  Setting the initial voltage to 0 will let the simulator skip the "transient" stage and apply the proper DC voltage on the capacitor automatically. 
 ※  If you want to start the simulation with 0V, set the capacitor voltage to 1pV. 
### Updated Op-Amps
 ※ Slew rate implementation for OP-Amps, as well as GBW (Gain-Bandwidth). 
 ※ Added the respective slew rates and GBWs to the already existing models. 

### Hover Tooltips
 ※  Hover over any component on the breadboard during simulation to see the current/voltage as well as a summarized tooltip for ICs and other multi-pin components.

## Updated Graph
### Always on top Graph option [📌 Pin]
### Scroll to move offset
### Ctrl Scroll to change time scale 
### Shift Scroll to change current scale
### Shift Ctrl Scroll to change Voltage scale
### Performance Improvements (hopefully)

## Advanced Graph Tools: 
### Hover Cursor
 ※  Live crosshair that shows the exact reading on the graph.
### Measurement Cursors
 ※  Place two cursors on the graph to measure the minimum, maximum, peak-to-peak voltages/currents.
### Split Graphs:
 **Combined Graphs | Domain Split (V/I) | Per Probe Split**
### Edge-Triggered Graph (Stable Graphs) 
### Export graph CSV

# Wait I forgot the documentation emojis
# 📊🚀🔍😎🤔😧😹⚡

## Screenshots of some of the things I added:

![CE with Degeneration Breadboard with Function Generator Screenshot](https://mohamed-ibrahim-asu.github.io/bbsim/BreadBoardSim1.png)

![CE with Degeneration and bypass capacitor Breadboard with Function Generator Screenshot](https://mohamed-ibrahim-asu.github.io/bbsim/BreadBoardSim2.png)

![Simple LPF and Simple Diode Limiter circuit Breadboard showing the XY/IV Characteristics](https://mohamed-ibrahim-asu.github.io/bbsim/BreadBoardSim3.png)

# Known Bugs:

## ※ Moving LeadedComponents results in a messed up circuit.
### ※ Should be fixed now, please update to the latest version.
## ※ Removing Probes does not automatically delete them from the graph. 
### Workaround: Switch the graph mode and then switch back to Per-Probe
## ※ Some Op-Amp Pin Names are kinda messed up
### ※ Should be fixed now, please update to the latest version.
## ※ Astable Multivibrators using Op-Amps don't work
### ※ Should be fixed now, please update to the latest version.
## ※ XY Graph is really laggy on zooming in.
### Workaround: Make sure to only zoom in around the 0V point (ie: down to 1nV), but dont zoom in much at 5.012V - it's not that accurate anyways. Just enjoy the shape
## ※ XY Graph takes too long to "warm up"
### Workaround: Try to lower the simulation speed so less ticks are plotted.
## ※ No way to reopen XY Graph if you close it
### Workaround: You need to stop the simulation and rerun it. Sorry

# Future Updates if I ever get around to them
### ※ Curve Tracer Graph
### ※ Proper Transient Graphing
### ※ pnjlim
### ※ Better documentation and instructions
_____________________________

This interactive circuit simulator with a breadboard style user interface was created as my A-level Computing project. It is built using a C#+WPF GUI frontend and a C++ backend. Visual Studio 2015
is required to build it, I use the free Community edition.

More info and binaries: http://ds0.me/csim/

Screenshots of demo circuits:

![Digital Ramp ADC Screenshot](http://ds0.me/csim/bbsim1s.png "Digital Ramp ADC")

![Three-digit Counter Screenshot](http://ds0.me/csim/bbsim2s.png "Three-digit Counter")

![555 Astable Screenshot](http://ds0.me/csim/bbsim3s.png "555 Astable")
