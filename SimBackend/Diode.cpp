#include "DiscreteSemis.h"
#include "Math.h"

std::string Diode::GetComponentType() {
    return "D";
}

int Diode::GetNumberOfPins() {
    return 2;
}

void Diode::SetParameters(ParameterSet params) {
    SaturationCurrent = params.getDouble("is", SaturationCurrent);
    IdealityFactor = params.getDouble("n", IdealityFactor);
    SeriesResistance = params.getDouble("rser", SeriesResistance);
    
    // Zener specific parameters
    BreakdownVoltage = params.getDouble("bv", 0);
    BreakdownCurrent = params.getDouble("ibv", 1e-3);
    // If nbv isn't defined, it defaults to the standard ideality factor 'n'
    BreakdownIdeality = params.getDouble("nbv", IdealityFactor); 
}

// f0: Is * (e ^ ((Vd - IRs)/(n*Vt)) - 1) - I
// f0: Ifwd + Izen - I = 0
double Diode::DCFunction(DCSolver *solver, int f) {
    if (f == 0) {
       // Calculate internal diode voltage: Va - Vk - (I * Rser)
       double V_int = (solver->GetNetVoltage(PinConnections[0]) - solver->GetNetVoltage(PinConnections[1])) - SeriesResistance * solver->GetPinCurrent(this, 0);
       
       double L = SaturationCurrent * (Math::exp_safe(V_int / (IdealityFactor * Math::vTherm)) - 1);
       
       // Zener continuous breakdown term (only calculates if Bv is explicitly defined > 0)
       if (BreakdownVoltage > 0) {
           L -= BreakdownCurrent * Math::exp_safe((-V_int - BreakdownVoltage) / (BreakdownIdeality * Math::vTherm));
       }
       
       double R = solver->GetPinCurrent(this, 0);
       return L - R;
    }
    return 0;
}

double Diode::TransientFunction(TransientSolver *solver, int f) {
    if (f == 0) {
       double V_int = (solver->GetNetVoltage(PinConnections[0]) - solver->GetNetVoltage(PinConnections[1])) - SeriesResistance * solver->GetPinCurrent(this, 0);
       
       double L = SaturationCurrent * (Math::exp_safe(V_int / (IdealityFactor * Math::vTherm)) - 1);
       
       if (BreakdownVoltage > 0) {
           L -= BreakdownCurrent * Math::exp_safe((-V_int - BreakdownVoltage) / (BreakdownIdeality * Math::vTherm));
       }
       
       double R = solver->GetPinCurrent(this, 0);
       return L - R;
    }
    return 0;
}

double Diode::DCDerivative(DCSolver *solver, int f, VariableIdentifier var) {
    if (f == 0) {
       double V_int = (solver->GetNetVoltage(PinConnections[0]) - solver->GetNetVoltage(PinConnections[1])) - SeriesResistance * solver->GetPinCurrent(this, 0);
       
       // Forward partial derivative term
       double Vt_n = IdealityFactor * Math::vTherm;
       double fwd_deriv = SaturationCurrent * (1 / Vt_n) * Math::exp_deriv(V_int / Vt_n);
       
       // Zener partial derivative term
       double zener_deriv = 0;
       if (BreakdownVoltage > 0) {
           double Vt_nbv = BreakdownIdeality * Math::vTherm;
           zener_deriv = (BreakdownCurrent / Vt_nbv) * Math::exp_deriv((-V_int - BreakdownVoltage) / Vt_nbv);
       }

       if (var.type == VariableIdentifier::VariableType::NET) {
          if (var.net == PinConnections[0]) {
             return fwd_deriv + zener_deriv;
          }
          else if (var.net == PinConnections[1]) {
             return -fwd_deriv - zener_deriv;
          }
       }
       else {
          if ((var.component == this) && (var.pin == 0)) {
             return (fwd_deriv + zener_deriv) * (-SeriesResistance) - 1;
          }
       }  
    }
    return 0;
}

double Diode::TransientDerivative(TransientSolver *solver, int f, VariableIdentifier var) {
    if (f == 0) {
       double V_int = (solver->GetNetVoltage(PinConnections[0]) - solver->GetNetVoltage(PinConnections[1])) - SeriesResistance * solver->GetPinCurrent(this, 0);
       
       double Vt_n = IdealityFactor * Math::vTherm;
       double fwd_deriv = SaturationCurrent * (1 / Vt_n) * Math::exp_deriv(V_int / Vt_n);
       
       double zener_deriv = 0;
       if (BreakdownVoltage > 0) {
           double Vt_nbv = BreakdownIdeality * Math::vTherm;
           zener_deriv = (BreakdownCurrent / Vt_nbv) * Math::exp_deriv((-V_int - BreakdownVoltage) / Vt_nbv);
       }

       if (var.type == VariableIdentifier::VariableType::NET) {
          if (var.net == PinConnections[0]) {
             return fwd_deriv + zener_deriv;
          }
          else if (var.net == PinConnections[1]) {
             return -fwd_deriv - zener_deriv;
          }
       }
       else {
          if ((var.component == this) && (var.pin == 0)) {
             return (fwd_deriv + zener_deriv) * (-SeriesResistance) - 1;
          }
       }
    }
    return 0;
}