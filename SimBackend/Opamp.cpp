#include "Opamp.h"
#include <cmath>

std::string Opamp::GetComponentType() {
   return "OPAMP";
}

int Opamp::GetNumberOfPins() {
   return 5;
}

void Opamp::SetParameters(ParameterSet params) {
   InputResistance = params.getDouble("rin", InputResistance);
   OpenLoopGain = params.getDouble("aol", OpenLoopGain);
   VosatP = params.getDouble("vosatp", VosatP);
   VosatN = params.getDouble("vosatn", VosatN);
   OutputResistance = params.getDouble("rout", OutputResistance);
   SlewRate = params.getDouble("slew", SlewRate);
   GBW = params.getDouble("gbw", GBW);

}

double Opamp::DCFunction(DCSolver *solver, int f) {
    double Vsp = solver->GetNetVoltage(PinConnections[4]);
    double Vsm = solver->GetNetVoltage(PinConnections[3]);
    double NinvInp = solver->GetNetVoltage(PinConnections[0]);
    double InvInp = solver->GetNetVoltage(PinConnections[1]);
    double Vout = solver->GetNetVoltage(PinConnections[2]);
    double Iinp = solver->GetPinCurrent(this, 0);
    double Iinn = solver->GetPinCurrent(this, 1);
    double Iout = solver->GetPinCurrent(this, 2);
    double Ignd = solver->GetPinCurrent(this, 3);

   double Vclip = (Vsp - Vsm - VosatP - VosatN) / 2.0;
   double Vmid = (Vsp + Vsm - VosatP + VosatN) / 2.0;
   double Vdiff = NinvInp - InvInp;
    
   double arg = OpenLoopGain * Vdiff;
   double arg_sq = arg * arg;
   
   // Using an Algebraic sigmoid in place of tanh: y = x / sqrt(1 + x^2)
   double curve = arg / std::sqrt(1.0 + arg_sq);
   
   double Vo = Vclip * curve + Vmid + Iout * OutputResistance;

   if (f == 0) return ((NinvInp - InvInp) / InputResistance) - Iinp;
   else if (f == 1) return (-(NinvInp - InvInp) / InputResistance) - Iinn;
   else if (f == 2) return Vout - Vo;
   else if (f == 3) {
       if (Iout > 0) return Ignd - (-Iout - Iq);
       else return Ignd - (-Iq);
   }
   return 0;
}

double Opamp::TransientFunction(TransientSolver *solver, int f) {
    double Vsp = solver->GetNetVoltage(PinConnections[4]);
    double Vsm = solver->GetNetVoltage(PinConnections[3]);
    double NinvInp = solver->GetNetVoltage(PinConnections[0]);
    double InvInp = solver->GetNetVoltage(PinConnections[1]);
    double Vout = solver->GetNetVoltage(PinConnections[2]);
    double Iinp = solver->GetPinCurrent(this, 0);
    double Iinn = solver->GetPinCurrent(this, 1);
    double Iout = solver->GetPinCurrent(this, 2);
    double Ignd = solver->GetPinCurrent(this, 3);

    double Vclip = (Vsp - Vsm - VosatP - VosatN) / 2.0;
    double Vmid = (Vsp + Vsm - VosatP + VosatN) / 2.0;
    double Vdiff = NinvInp - InvInp;

   
   double calcSlew = (SlewRate < 1.0) ? 1e9 : SlewRate; 
   double calcGBW = (GBW < 1.0) ? 1e12 : GBW;
   double tau_leak = OpenLoopGain/ (2.0 * Math::pi * calcGBW);
   double V_T = calcSlew / (2.0 * Math::pi * calcGBW);
   
    double arg_in = Vdiff / V_T;
    double arg_in_sq = arg_in * arg_in;
    double curve_in = arg_in / std::sqrt(1.0 + arg_in_sq);

    double Vint_rel = 0.0;

    int tick = solver->GetCurrentTick();
    if (tick == 0) {
       double Vout_dc = solver->GetNetVoltage(PinConnections[2], 0);
       double Iout_dc = solver->GetPinCurrent(this, 2, 0);
       double Vprev_rel = (Vout_dc - Iout_dc * OutputResistance) - Vmid;
       double y = Vprev_rel / Vclip;
       if (y > 0.95) y = 0.95;
       if (y < -0.95) y = -0.95;
       Vint_rel = (y / std::sqrt(1.0 - y * y)) * Vclip;
    } else {
        double dt = solver->GetTimeAtTick(tick) - solver->GetTimeAtTick(tick - 1);
        if (dt < 1e-15) dt = 1e-15;
        
        double Vprev = solver->GetNetVoltage(PinConnections[2], tick - 1);
        double Iout_prev = solver->GetPinCurrent(this, 2, tick - 1);
        double Vprev_rel = (Vprev - Iout_prev * OutputResistance) - Vmid;
        
        // 1. INVERSE CLIP: Recover true internal state from the physical output
        double y = Vprev_rel / Vclip;
        
        // Guard floating point drift to prevent sqrt(negative)
        if (y > 0.95) y = 0.95;
        if (y < -0.95) y = -0.95;
        
        double Vprev_int_rel = (y / std::sqrt(1.0 - y * y)) * Vclip;
        
        // 2. INTEGRATE: Safely add the Slew step to the un-clipped state
        Vint_rel = (Vprev_int_rel + dt * calcSlew * curve_in) / (1.0 + dt / tau_leak);
    }

    double arg_clip = Vint_rel / Vclip;
    double arg_clip_sq = arg_clip * arg_clip;
    double curve_clip = arg_clip / std::sqrt(1.0 + arg_clip_sq);
    
    double Vo = Vclip * curve_clip + Vmid + Iout * OutputResistance;

    if (f == 0) return ((NinvInp - InvInp) / InputResistance) - Iinp;
    else if (f == 1) return (-(NinvInp - InvInp) / InputResistance) - Iinn;
    else if (f == 2) return Vout - Vo;
    else if (f == 3) {
        if (Iout > 0) return Ignd - (-Iout - Iq);
        else return Ignd - (-Iq);
    }
    return 0;
}double Opamp::DCDerivative(DCSolver *solver, int f, VariableIdentifier var) {
    double Vsp = solver->GetNetVoltage(PinConnections[4]);
    double Vsm = solver->GetNetVoltage(PinConnections[3]);
    double NinvInp = solver->GetNetVoltage(PinConnections[0]);
    double InvInp = solver->GetNetVoltage(PinConnections[1]);

   double Vclip = (Vsp - Vsm - VosatP - VosatN) / 2.0;
   double Vdiff = NinvInp - InvInp;
    
   double arg = OpenLoopGain * Vdiff;
   double arg_sq = arg * arg;
   double curve = arg / std::sqrt(1.0 + arg_sq);
    
   // Algebraic sigmoid derivative: d/dx = 1 / (1 + x^2)^1.5
   // Calculated as 1 / ((1 + x^2) * sqrt(1 + x^2)) to avoid slow pow() calls.
   double dVo_dVdiff = Vclip * OpenLoopGain / ((1.0 + arg_sq) * std::sqrt(1.0 + arg_sq));

    if (f == 0) {
       if (var.type == var.NET && var.net == PinConnections[0]) return 1.0 / InputResistance;
       if (var.type == var.NET && var.net == PinConnections[1]) return -1.0 / InputResistance;
       if (var.type == var.COMPONENT && var.component == this && var.pin == 0) return -1.0;
    }
    else if (f == 1) {
       if (var.type == var.NET && var.net == PinConnections[0]) return -1.0 / InputResistance;
       if (var.type == var.NET && var.net == PinConnections[1]) return 1.0 / InputResistance;
       if (var.type == var.COMPONENT && var.component == this && var.pin == 1) return -1.0;
    }
    else if (f == 2){
       if (var.type == var.NET && var.net == PinConnections[2]) return 1.0;
       if (var.type == var.NET && var.net == PinConnections[0]) return -dVo_dVdiff;
       if (var.type == var.NET && var.net == PinConnections[1]) return dVo_dVdiff;

       if (var.type == var.NET && var.net == PinConnections[3]) return 0.5 * curve - 0.5;
       if (var.type == var.NET && var.net == PinConnections[4]) return -0.5 * curve - 0.5;
       
       if (var.type == var.COMPONENT && var.component == this && var.pin == 2) return -OutputResistance;
    }
    else if (f == 3) {
       if (solver->GetPinCurrent(this, 2) > 0) {
          if (var.type == var.COMPONENT && var.component == this && var.pin == 3) return 1.0;
          if (var.type == var.COMPONENT && var.component == this && var.pin == 2) return 1.0;
       }
       else {
          if (var.type == var.COMPONENT && var.component == this && var.pin == 3) return 1.0;
       }
    }
    return 0;
}

double Opamp::TransientDerivative(TransientSolver *solver, int f, VariableIdentifier var) {
    double Vsp = solver->GetNetVoltage(PinConnections[4]);
    double Vsm = solver->GetNetVoltage(PinConnections[3]);
    double NinvInp = solver->GetNetVoltage(PinConnections[0]);
    double InvInp = solver->GetNetVoltage(PinConnections[1]);

    double Vclip = (Vsp - Vsm - VosatP - VosatN) / 2.0;
    double Vmid = (Vsp + Vsm - VosatP + VosatN) / 2.0;
    double Vdiff = NinvInp - InvInp;

   
   double calcSlew = (SlewRate < 1.0) ? 1e9 : SlewRate; 
   double calcGBW = (GBW < 1.0) ? 1e12 : GBW;
   double tau_leak = OpenLoopGain/ (2.0 * Math::pi * calcGBW);
   double V_T = calcSlew / (2.0 * Math::pi * calcGBW);
   
    double arg_in = Vdiff / V_T;
    double arg_in_sq = arg_in * arg_in;
    double curve_in = arg_in / std::sqrt(1.0 + arg_in_sq);
    double dCurveIn_dVdiff = (1.0 / V_T) / ((1.0 + arg_in_sq) * std::sqrt(1.0 + arg_in_sq));

    double Vint_rel = 0.0;
    double dVint_rel_dVdiff = 0.0;

    int tick = solver->GetCurrentTick();
    if (tick == 0) {
       double Vout_dc = solver->GetNetVoltage(PinConnections[2], 0);
       double Iout_dc = solver->GetPinCurrent(this, 2, 0);
       double Vprev_rel = (Vout_dc - Iout_dc * OutputResistance) - Vmid;
       double y = Vprev_rel / Vclip;
       if (y > 0.95) y = 0.95;
       if (y < -0.95) y = -0.95;
       Vint_rel = (y / std::sqrt(1.0 - y * y)) * Vclip;
        dVint_rel_dVdiff = 0.0;
    } else {
        double dt = solver->GetTimeAtTick(tick) - solver->GetTimeAtTick(tick - 1);
        if (dt < 1e-15) dt = 1e-15;
        
        double Vprev = solver->GetNetVoltage(PinConnections[2], tick - 1);
        double Iout_prev = solver->GetPinCurrent(this, 2, tick - 1);
        double Vprev_rel = (Vprev - Iout_prev * OutputResistance) - Vmid;
        
        double y = Vprev_rel / Vclip;
        if (y > 0.95) y = 0.95;
        if (y < -0.95) y = -0.95;
        
        double Vprev_int_rel = (y / std::sqrt(1.0 - y * y)) * Vclip;
        
        Vint_rel = (Vprev_int_rel + dt * calcSlew * curve_in) / (1.0 + dt / tau_leak);
        dVint_rel_dVdiff = (dt * calcSlew * dCurveIn_dVdiff) / (1.0 + dt / tau_leak);
    }

    double arg_clip = Vint_rel / Vclip;
    double arg_clip_sq = arg_clip * arg_clip;
    double curve_clip = arg_clip / std::sqrt(1.0 + arg_clip_sq);
    double dCurveClip_dInt = (1.0 / Vclip) / ((1.0 + arg_clip_sq) * std::sqrt(1.0 + arg_clip_sq));

    double dVo_dVdiff = Vclip * dCurveClip_dInt * dVint_rel_dVdiff;

    if (f == 0) {
       if (var.type == var.NET && var.net == PinConnections[0]) return 1.0 / InputResistance;
       if (var.type == var.NET && var.net == PinConnections[1]) return -1.0 / InputResistance;
       if (var.type == var.COMPONENT && var.component == this && var.pin == 0) return -1.0;
    }
    else if (f == 1) {
       if (var.type == var.NET && var.net == PinConnections[0]) return -1.0 / InputResistance;
       if (var.type == var.NET && var.net == PinConnections[1]) return 1.0 / InputResistance;
       if (var.type == var.COMPONENT && var.component == this && var.pin == 1) return -1.0;
    }
    else if (f == 2){
       if (var.type == var.NET && var.net == PinConnections[2]) return 1.0;
       if (var.type == var.NET && var.net == PinConnections[0]) return -dVo_dVdiff;
       if (var.type == var.NET && var.net == PinConnections[1]) return dVo_dVdiff;

       if (var.type == var.NET && var.net == PinConnections[3]) return 0.5 * curve_clip - 0.5;
       if (var.type == var.NET && var.net == PinConnections[4]) return -0.5 * curve_clip - 0.5;
       
       if (var.type == var.COMPONENT && var.component == this && var.pin == 2) return -OutputResistance;
    }
    else if (f == 3) {
       if (solver->GetPinCurrent(this, 2) > 0) {
          if (var.type == var.COMPONENT && var.component == this && var.pin == 3) return 1.0;
          if (var.type == var.COMPONENT && var.component == this && var.pin == 2) return 1.0;
       }
       else {
          if (var.type == var.COMPONENT && var.component == this && var.pin == 3) return 1.0;
       }
    }
    return 0;
}