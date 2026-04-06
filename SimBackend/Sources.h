#pragma once
#include "Component.h"
#include <cmath>

class SineSource : public Component {
public:
    virtual std::string GetComponentType() override { return "V_SINE"; }
    virtual int GetNumberOfPins() override { return 2; }

    virtual void SetParameters(ParameterSet params) override;
    virtual double DCFunction(DCSolver *solver, int f) override;
    virtual double TransientFunction(TransientSolver *solver, int f) override;
    virtual double DCDerivative(DCSolver *solver, int f, VariableIdentifier var) override;
    virtual double TransientDerivative(TransientSolver *solver, int f, VariableIdentifier var) override;

private:
    double Amplitude = 2.0;
    double Frequency = 1.0;
    double Offset = 0.0;
    int WaveType = 0; // 0=Sine, 1=Square, 2=Triangle
    double InternalR = 0.1; 
};