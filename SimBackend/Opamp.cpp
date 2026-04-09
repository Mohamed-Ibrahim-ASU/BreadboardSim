#include "Opamp.h"
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

   // TANH WITH ASYMPTOTIC LEAKAGE
   double Vclip = (Vsp - Vsm - VosatP - VosatN) / 2.0;
   double Vmid = (Vsp + Vsm - VosatP + VosatN) / 2.0;
   double Vdiff = NinvInp - InvInp;
    
   double arg = OpenLoopGain * Vdiff;
   double curve = tanh(arg);
   const double EPSILON = 1e-6; // Prevents singular matrix crashes (zero-derivative)
    
   // Calculate output with the microscopic linear leakage term added
   double Vo = Vclip * curve + Vmid + (EPSILON * Vdiff) + Iout * OutputResistance;

    if (f == 0) {
       return ((NinvInp - InvInp) / InputResistance) - Iinp;
    }
    else if (f == 1) {
       return (-(NinvInp - InvInp) / InputResistance) - Iinn;
    }
    else if (f == 2){
       return Vout - Vo;
    }
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

   // TANH WITH ASYMPTOTIC LEAKAGE
   double Vclip = (Vsp - Vsm - VosatP - VosatN) / 2.0;
   double Vmid = (Vsp + Vsm - VosatP + VosatN) / 2.0;
   double Vdiff = NinvInp - InvInp;
    
   double arg = OpenLoopGain * Vdiff;
   double curve = tanh(arg);
   const double EPSILON = 1e-6; // Prevents singular matrix crashes (zero-derivative)
    
   // Calculate output with the microscopic linear leakage term added
   double Vo = Vclip * curve + Vmid + (EPSILON * Vdiff) + Iout * OutputResistance;

    if (f == 0) {
       return ((NinvInp - InvInp) / InputResistance) - Iinp;
    }
    else if (f == 1) {
       return (-(NinvInp - InvInp) / InputResistance) - Iinn;
    }
    else if (f == 2){
       return Vout - Vo;
    }
    else if (f == 3) {
       if (Iout > 0) return Ignd - (-Iout - Iq);
       else return Ignd - (-Iq);
    }
    return 0;
}

double Opamp::DCDerivative(DCSolver *solver, int f, VariableIdentifier var) {
    double Vsp = solver->GetNetVoltage(PinConnections[4]);
    double Vsm = solver->GetNetVoltage(PinConnections[3]);
    double NinvInp = solver->GetNetVoltage(PinConnections[0]);
    double InvInp = solver->GetNetVoltage(PinConnections[1]);

   // TANH WITH ASYMPTOTIC LEAKAGE
   double Vclip = (Vsp - Vsm - VosatP - VosatN) / 2.0;
   double Vdiff = NinvInp - InvInp;
    
   double arg = OpenLoopGain * Vdiff;
   double curve = tanh(arg);
   const double EPSILON = 1e-6; // Prevents singular matrix crashes
    
   // The safely computed derivative: d(tanh(x))/dx = 1 - tanh^2(x)
   double dVo_dVdiff = Vclip * OpenLoopGain * (1.0 - curve * curve) + EPSILON;

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
    // The exact same safe derivatives apply to the Transient state
    double Vsp = solver->GetNetVoltage(PinConnections[4]);
    double Vsm = solver->GetNetVoltage(PinConnections[3]);
    double NinvInp = solver->GetNetVoltage(PinConnections[0]);
    double InvInp = solver->GetNetVoltage(PinConnections[1]);

   // TANH WITH ASYMPTOTIC LEAKAGE
   double Vclip = (Vsp - Vsm - VosatP - VosatN) / 2.0;
   double Vdiff = NinvInp - InvInp;
    
   double arg = OpenLoopGain * Vdiff;
   double curve = tanh(arg);
   const double EPSILON = 1e-6; // Prevents singular matrix crashes
    
   // The safely computed derivative: d(tanh(x))/dx = 1 - tanh^2(x)
   double dVo_dVdiff = Vclip * OpenLoopGain * (1.0 - curve * curve) + EPSILON;

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