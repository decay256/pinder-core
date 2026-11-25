"""Cut the one-pass MOSS-TTSD render into the cue-sheet segments, so the same stage mix
can be laid over it, and check that each segment came out in the right voice.

Whisper word timestamps (ttsd_words.py) are aligned to the script words; each segment is cut halfway
between its last word and the next segment's first word. Speaker attribution: each cut
is compared with both voices' centroids (Qwen3-TTS speaker embedding).

Output: out/ttsd/segments/<id>.wav, out/ttsd/picks.json
"""
import difflib
import json

import soundfile as sf
import torch

from common import OUT, load_tts, load_yaml, norm_words, speaker_embedding

cues = load_yaml("cues.yaml")
voices = load_yaml("voices.yaml")
picks = json.loads((OUT / "lines" / "picks.json").read_text(encoding="utf-8"))
w, sr = sf.read(OUT / "ttsd" / "run16-ttsd.wav", dtype="float32")
w = w.mean(1) if w.ndim > 1 else w

# word timestamps from ttsd_words.py (faster-whisper, CPU)
hyp_words, hyp_times = [], []
for text, s, e in json.loads((OUT / "ttsd" / "words.json").read_text(encoding="utf-8")):
    for tok in norm_words(text):
        hyp_words.append(tok)
        hyp_times.append((s, e))

ref_words, ref_seg = [], []
for k, p in enumerate(picks):
    for tok in norm_words(p["text"]):
        ref_words.append(tok)
        ref_seg.append(k)

# map each script word to a recognised word
match = {}
for a, b, n in difflib.SequenceMatcher(None, ref_words, hyp_words, autojunk=False).get_matching_blocks():
    for i in range(n):
        match[a + i] = b + i
first, last = {}, {}
for i, k in enumerate(ref_seg):
    if i in match:
        first.setdefault(k, hyp_times[match[i]][0])
        e = hyp_times[match[i]][1]
        last[k] = e if e is not None else hyp_times[match[i]][0]

bounds = []
for k in range(len(picks)):
    s = first.get(k)
    e = last.get(k)
    nxt = first.get(k + 1)
    if s is None or e is None:
        raise SystemExit(f"segment {picks[k]['id']} could not be aligned")
    bounds.append([s, e if nxt is None else (e + nxt) / 2])
for k in range(1, len(bounds)):
    bounds[k][0] = bounds[k - 1][1]
bounds[0][0] = max(0.0, first[0] - 0.15)

base = load_tts("base")
cent = {}
for vname, v in voices.items():
    es = [torch.load(OUT / "voices" / vname / f"{m}.prompt.pt", weights_only=False)["ref_spk_embedding"].float().flatten()
          for m in v["modes"]]
    c = torch.stack([e / e.norm() for e in es]).mean(0)
    cent[vname] = (c / c.norm()).cpu()

sdir = OUT / "ttsd" / "segments"
sdir.mkdir(parents=True, exist_ok=True)
out, right = [], 0
for p, (s, e) in zip(picks, bounds):
    seg = w[int(s * sr): int(e * sr)]
    sf.write(sdir / f"{p['id']}.wav", seg, sr)
    emb = speaker_embedding(base, seg, sr)
    who = max(cent, key=lambda v: float(emb @ cent[v]))
    right += who == p["speaker"]
    out.append(dict(p, path=str(sdir / f"{p['id']}.wav"), dur=round(e - s, 2), heard_as=who,
                    sim_zyx=round(float(emb @ cent["zyx"]), 3), sim_sable=round(float(emb @ cent["sable"]), 3)))
(OUT / "ttsd" / "picks.json").write_text(json.dumps(out, indent=2), encoding="utf-8")
print(f"speaker attribution: {right}/{len(picks)} segments in the right voice")
for o in out:
    if o["heard_as"] != o["speaker"]:
        print("  wrong voice:", o["id"], o["text"][:60], o["sim_zyx"], o["sim_sable"])
