"""Put back the NumPy aliases madmom's COMPILED modules still reference.

   The .py files can be patched (setup.sh does), but madmom.ml.hmm and friends are Cython
   extensions built against NumPy < 1.20 and reach for np.int at runtime. Import this before
   importing madmom."""
import numpy as np
for _n, _t in (('int', int), ('float', float), ('bool', bool),
               ('object', object), ('complex', complex)):
    if not hasattr(np, _n): setattr(np, _n, _t)
