"""Near-duplicate filter shared by the passes: the same chart saved twice (re-downloads, remakes
with a handful of edits) has an equal tile count and an event count within 1%."""

def unique(charts):
    keep, seen = [], {}
    for m in sorted(charts, key=lambda m: -m['events']):
        if any(abs(o['events'] - m['events']) <= max(5, 0.01 * m['events']) for o in seen.get(m['tiles'], [])):
            continue
        seen.setdefault(m['tiles'], []).append(m); keep.append(m)
    return keep
