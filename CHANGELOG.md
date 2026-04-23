Added Zener Diodes
Added slew rate and GBW to OPAMPs 
Switched tanh +EPSILON saturation in opamp model to x/sqrt(1+x^2) 
Added BC337 and BC327

Quite a few backend changes: 
Put the basis for a pnjlim implementation (if i need to do it later but I don't yet) 
Now instead of convergence error on the first offense, the calculator halves the time step and tries again, up to 15 tries. If it can't resolve it, there might be a problem with the circuit or the simulation speed is too fast. (Or it's too niche for the simulator's current state) 
On startup, if there are BJTs, the BJT nets start with Vcc/2 so the Jacobians dont start empty. 

Hopefully now our electronics project should be fully functional on this simulator.