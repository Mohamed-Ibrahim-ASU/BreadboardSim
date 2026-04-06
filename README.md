# Breadboard Simulator

This is my take on a fork of Breadboard Simulator, I noticed the program was missing a few features I wanted to use in my Electronics Course, so I created this fork to add them.

# Additions
## Components
### Input Devices - Function Generator
 ※  The function generator can be used to generate sinusoidal, square, or triangle waves of any frequency and amplitude with a DC offset.
  
※ When using the function generator, make sure the function generator is grounded and the simulation speed is low enough to allow for proper generation of the functions. 

※ Ideally 5x the maximum frequency is what I recommend. 
※ 10x should be the absolute maximum; any faster and the shape will be significantly distorted.  
### New Components
**As used in our Electronics Lab:**

※ Added 1N4007 Diode

※ Added BC639 NPN Transistor

※ Added LM358 OP-AMP
    

## Probes
### Voltage Difference Probes
  ※ The voltage difference probes consist of a positive and a negative probe to measure the voltage drop between any two nodes on the breadboard and plots it on the graph; the usage is almost identical to the oscilloscope probe currently implemented. 
### Current Probe (BETA) 
 ※  The current probe can be used to measure the current going through a lead of most components. Due to SPICE treating wires as nodes, the current probe cannot measure the current flowing through a "wire".

※   To use the current probe, you should place it on an end lead/wire of a component (ie: a resistor). If it doesn't work, try again in another place.

## General Improvements
### Updated Wires 
 ※  Wires can now be placed diagonally, and the placement of wires should hopefully be easier.
### Updated Leaded Components 
  ※ Leaded components can also be placed diagonally.
### Updated Capacitors
 ※  Capacitors now have leads and as such also can be placed between any two nodes, not just two adjacent nodes.

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

![CE with Degeneration Breadboard with Function Generator Screenshot](https://mohamed-ibrahim-asu.github.io/bbsim/breadboard1.png)

![CE with Degeneration Graph Screenshot](https://mohamed-ibrahim-asu.github.io/bbsim/graphview1.png)
_____________________________

This interactive circuit simulator with a breadboard style user interface was created as my A-level Computing project. It is built using a C#+WPF GUI frontend and a C++ backend. Visual Studio 2015
is required to build it, I use the free Community edition.

More info and binaries: http://ds0.me/csim/

Screenshots of demo circuits:

![Digital Ramp ADC Screenshot](http://ds0.me/csim/bbsim1s.png "Digital Ramp ADC")

![Three-digit Counter Screenshot](http://ds0.me/csim/bbsim2s.png "Three-digit Counter")

![555 Astable Screenshot](http://ds0.me/csim/bbsim3s.png "555 Astable")
