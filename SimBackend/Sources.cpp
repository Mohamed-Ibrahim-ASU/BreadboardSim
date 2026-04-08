#include "Sources.h"
#include "TransientSolver.h"
#include "DCSolver.h"

void SineSource::SetParameters(ParameterSet params) {
    Amplitude = params.getDouble("amp", 2.0);
    Frequency = params.getDouble("freq", 1.0);
    Offset = params.getDouble("off", 0.0);
    WaveType = (int)params.getDouble("type", 0.0);
    
    // Catch the new flag sent from the C# UI
    StartupTransientFlag = params.getDouble("startup", 0.0);
}

double SineSource::DCFunction(DCSolver *solver, int f) {
    double v0 = solver->GetNetVoltage(PinConnections[0]);
    double v1 = solver->GetNetVoltage(PinConnections[1]);
    
    double targetV = Offset;
    if (StartupTransientFlag > 0.5) {
        // If the user wants a cold start, force the DC solver to see a dead battery (0V).
        // This forces every capacitor on the board to calculate a 0V resting state.
        targetV = 0.0; 
    }
    
    if (f == 0) return (v0 - v1 - targetV) / InternalR;
    if (f == 1) return (v1 - v0 + targetV) / InternalR;
    return 0;
}

double SineSource::TransientFunction(TransientSolver *solver, int f) {
    double v0 = solver->GetNetVoltage(PinConnections[0]);
    double v1 = solver->GetNetVoltage(PinConnections[1]);
    
    int tick = solver->GetCurrentTick();
    double time = solver->GetTimeAtTick(tick); 
    
    // Start with the DC offset baseline
    double targetV = Offset;
    
    // Only calculate and add the AC phase if it is NOT the DC mode (WaveType 3)
    if (WaveType != 3) {
        double period = 1.0 / Frequency;
        double phase = fmod(time, period) / period;
        
        if (WaveType == 0) {
            targetV += Amplitude * sin(6.28318530718 * Frequency * time);
        } 
        else if (WaveType == 1) {
            targetV += (phase < 0.5) ? Amplitude : -Amplitude;
        } 
        else if (WaveType == 2) {
            if (phase < 0.25) targetV += Amplitude * (4.0 * phase);
            else if (phase < 0.75) targetV += Amplitude * (2.0 - 4.0 * phase);
            else targetV += Amplitude * (4.0 * phase - 4.0);
        }
    }
    
    if (StartupTransientFlag > 0.5) {
        // Default to 1 millisecond for DC signals
        double RampDuration = 0.001; 
        
        // If it is an AC waveform, scale the ramp to exactly 5 full periods!
        if (WaveType != 3 && Frequency > 0.0) {
            RampDuration = 5.0 / Frequency; 
        }
        
        if (time < RampDuration) {
            // Smoothly multiply from 0.0 to 1.0 over the dynamic envelope
            targetV = targetV * (time / RampDuration);
        }
    }
    
    if (f == 0) return (v0 - v1 - targetV) / InternalR;
    if (f == 1) return (v1 - v0 + targetV) / InternalR;
    return 0;
}

double SineSource::DCDerivative(DCSolver *solver, int f, VariableIdentifier var) {
    if (var.type == VariableIdentifier::VariableType::NET) {
        if (f == 0) {
            if (var.net == PinConnections[0]) return 1.0 / InternalR;
            if (var.net == PinConnections[1]) return -1.0 / InternalR;
        }
        else if (f == 1) {
            if (var.net == PinConnections[0]) return -1.0 / InternalR;
            if (var.net == PinConnections[1]) return 1.0 / InternalR;
        }
    }
    return 0;
}

double SineSource::TransientDerivative(TransientSolver *solver, int f, VariableIdentifier var) {
    return DCDerivative((DCSolver*)solver, f, var);
}