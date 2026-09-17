"""Tolerant .adofai loader. The format is JSON in principle and not always in practice:
   trailing commas, NaN/Infinity literals, and several encodings in the wild."""
import os, json, re, glob

def load_adofai(path):
    raw = open(path,'rb').read()
    txt = None
    for enc in ('utf-8-sig','utf-8','utf-16','cp949','latin-1'):
        try: txt = raw.decode(enc); break
        except Exception: continue
    if txt is None: return None
    txt = txt.replace('\r','')
    txt = re.sub(r',(\s*[}\]])', r'\1', txt)
    txt = re.sub(r'\bNaN\b', 'null', txt)
    txt = re.sub(r'\bInfinity\b', 'null', txt)
    try: return json.loads(txt)
    except Exception: pass
    m = re.search(r'"settings"\s*:\s*\{', txt)
    if not m: return None
    i = m.end()-1; depth = 0
    for j in range(i, len(txt)):
        if txt[j]=='{': depth += 1
        elif txt[j]=='}':
            depth -= 1
            if depth == 0:
                blob = re.sub(r',(\s*[}\]])', r'\1', txt[i:j+1])
                try: return {'settings': json.loads(blob), '_partial': True}
                except Exception: return None
    return None

def levels(root):
    out=[]
    for d in sorted(os.listdir(root)):
        p=os.path.join(root,d)
        if not os.path.isdir(p): continue
        out.extend(sorted(glob.glob(os.path.join(p,'*.adofai'))))
    return out
