"""Do punchlines land? The same check over every render's laugh points.

For each segment followed by an audience reaction whose text is a statement (questions are
left out: some should rise), the last word is found with faster-whisper word timestamps
and the pitch over its last ~100 ms of voicing is compared with the segment's median. A
landing is counted when it ends at least 1 semitone below; a lift is 1 semitone above.

usage: python compare_landing.py
Output: out/landing.json, and a table on stdout
"""
import json

import librosa
import numpy as np
import soundfile as sf
from faster_whisper import WhisperModel

from common import OUT, norm_words

HOP = 0.01
RENDERS = {
    "per-line clones": OUT / "lines" / "picks.json",
    "one-pass MOSS-TTSD": OUT / "ttsd" / "picks.json",
    "directed turns": OUT / "direct" / "picks.json",
}
fw = WhisperModel("small.en", device="cpu", compute_type="int8")


def landing(path, text):
    w, sr = sf.read(path, dtype="float32")
    w = w.mean(1) if w.ndim > 1 else w
    segs, _ = fw.transcribe(librosa.resample(w, orig_sr=sr, target_sr=16000), language="en",
                            word_timestamps=True, condition_on_previous_text=False)
    ws = [x for s in segs for x in s.words if norm_words(x.word)]
    if not ws:
        return None
    last_ref = norm_words(text)[-1]
    cand = [x for x in ws if norm_words(x.word)[-1] == last_ref] or ws
    lw = cand[-1]
    f0, vf, _ = librosa.pyin(w, fmin=65, fmax=520, sr=sr, frame_length=2048, hop_length=int(HOP * sr))
    f0 = np.where(vf, f0, np.nan)
    seg = f0[int(lw.start / HOP): int(lw.end / HOP) + 1]
    seg = seg[~np.isnan(seg)][-10:]
    if len(seg) < 3:
        return None
    return float(12 * np.log2(np.median(seg) / np.nanmedian(f0)))


res = {}
for name, pj in RENDERS.items():
    picks = json.loads(pj.read_text(encoding="utf-8"))
    rows = []
    for p in picks:
        if p["react"] == "none" or p["text"].rstrip().endswith("?"):
            continue
        path = p.get("path") or str(OUT / "lines" / p["id"] / p["take"])
        st = landing(path, p["text"])
        rows.append(dict(id=p["id"], text=p["text"][-40:], st=None if st is None else round(st, 1)))
    vals = [r["st"] for r in rows if r["st"] is not None]
    res[name] = dict(n=len(rows), measured=len(vals),
                     lands=sum(v <= -1 for v in vals), lifts=sum(v >= 1 for v in vals),
                     median_st=round(float(np.median(vals)), 1), rows=rows)
    print(f"{name:20s} punchlines {len(rows):3d}  measured {len(vals):3d}  land {res[name]['lands']:3d}  "
          f"lift {res[name]['lifts']:3d}  median {res[name]['median_st']:+.1f} st", flush=True)
(OUT / "landing.json").write_text(json.dumps(res, indent=1), encoding="utf-8")
