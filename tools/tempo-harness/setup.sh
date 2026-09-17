#!/bin/bash
# Rebuild the harness environment. The scratch directory this was first built in is temporary
# and has been wiped twice mid-project, so the venv lives next to the code instead.
set -e
cd "$(dirname "$0")"
python3 -m venv .venv
.venv/bin/pip install -q --upgrade pip setuptools wheel
.venv/bin/pip install -q numpy scipy
echo "numpy/scipy installed"

# madmom 0.16.1 predates Python 3.10 and NumPy 1.20. It needs its build deps present (no
# isolation), an old setuptools for pkg_resources, ~20 source files patched for imports that
# moved, and the removed NumPy aliases put back at runtime for its COMPILED modules — see
# mm_shim.py. Optional: the harness works without it, minus the madmom baseline.
.venv/bin/pip install -q "setuptools<81" Cython || true
.venv/bin/pip install -q --no-build-isolation madmom 2>/dev/null && {
  .venv/bin/python - <<'PY'
import re, pathlib, sysconfig
mm = pathlib.Path(sysconfig.get_paths()['purelib']) / 'madmom'
pats = [
 (re.compile(r'from collections import (.*\b(?:MutableSequence|Iterable|Callable|Mapping)\b.*)'),
  r'from collections.abc import \1'),
 (re.compile(r'\bnp\.float\b(?!\d|_|3|6)'), 'float'),
 (re.compile(r'\bnp\.int\b(?!\d|_|8|1|3|6)'), 'int'),
 (re.compile(r'\bnp\.bool\b(?!_|8)'), 'bool'),
 (re.compile(r'\bnp\.object\b(?!_)'), 'object'),
 (re.compile(r'inspect\.getargspec'), 'inspect.getfullargspec'),
]
n = 0
for f in mm.rglob('*.py'):
    s = f.read_text(encoding='utf-8', errors='ignore'); o = s
    for p, r in pats: s = p.sub(r, s)
    if s != o: f.write_text(s, encoding='utf-8'); n += 1
print('madmom installed, %d files patched' % n)
PY
} || echo "madmom unavailable - harness still works without it"
echo "done. ffmpeg/ffprobe must be on PATH."
