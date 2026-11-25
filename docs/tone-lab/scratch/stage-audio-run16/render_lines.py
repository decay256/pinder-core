"""Render every cue-sheet segment from the frozen clone prompts, several takes each, and pick one.

A take passes when Whisper hears the right words (WER <= 0.12) and its length is within
reach of the other takes (rejects runaway or truncated generations). Among passing takes
the one closest to the speaker's voice (mean of its four frozen deliveries) wins.

Output: out/lines/<nn>_<speaker>_<k>/take<j>.wav, out/lines/picks.json
"""
import json
import sys

import librosa
import numpy as np
import soundfile as sf
import torch
from qwen_tts.inference.qwen3_tts_model import VoiceClonePromptItem

from common import OUT, load_asr, load_tts, load_yaml, speaker_embedding, transcribe, wer

TAKES = int(sys.argv[1]) if len(sys.argv) > 1 else 6
cues = load_yaml("cues.yaml")
voices = load_yaml("voices.yaml")
ldir = OUT / "lines"
ldir.mkdir(parents=True, exist_ok=True)

prompts, centroid = {}, {}
for vname, v in voices.items():
    embs = []
    for mode in v["modes"]:
        d = torch.load(OUT / "voices" / vname / f"{mode}.prompt.pt", weights_only=False)
        prompts[(vname, mode)] = VoiceClonePromptItem(**d)
        e = d["ref_spk_embedding"].float().flatten()
        embs.append(e / e.norm())
    c = torch.stack(embs).mean(0)
    centroid[vname] = (c / c.norm()).cpu()

segments = []
for i, line in enumerate(cues["lines"]):
    for k, seg in enumerate(line["segments"]):
        segments.append(dict(id=f"{i:02d}_{line['speaker']}_{k}", speaker=line["speaker"], **seg))

base = load_tts("base")
for s in segments:
    d = ldir / s["id"]
    d.mkdir(exist_ok=True)
    if len(list(d.glob("take*.wav"))) >= TAKES:
        continue
    item = prompts[(s["speaker"], s["mode"])]
    wavs, sr = base.generate_voice_clone(
        text=[s["text"]] * TAKES, language=["English"] * TAKES, voice_clone_prompt=[item] * TAKES
    )
    for j, w in enumerate(wavs):
        sf.write(d / f"take{j}.wav", w, sr)
    print("rendered", s["id"], flush=True)

asr = load_asr()
picks = []
for s in segments:
    takes = []
    for p in sorted((ldir / s["id"]).glob("take*.wav")):
        w, sr = sf.read(p, dtype="float32")
        wt, _ = librosa.effects.trim(w, top_db=40)
        hyp = transcribe(asr, wt, sr)
        takes.append(dict(path=p, wer=wer(s["text"], hyp), dur=len(wt) / sr, hyp=hyp,
                          sim=float(speaker_embedding(base, wt, sr) @ centroid[s["speaker"]])))
    med = float(np.median([t["dur"] for t in takes]))
    for t in takes:
        t["ok"] = t["wer"] <= 0.12 and 0.7 * med <= t["dur"] <= 1.35 * med
    pool = [t for t in takes if t["ok"]] or sorted(takes, key=lambda t: t["wer"])[:2]
    best = max(pool, key=lambda t: t["sim"])
    picks.append(dict(s, take=best["path"].name, wer=round(best["wer"], 3), sim=round(best["sim"], 3),
                      dur=round(best["dur"], 2), asr=best["hyp"], passing=sum(t["ok"] for t in takes)))
    flag = "" if best["ok"] else "  <-- NO TAKE PASSED"
    print(f"{s['id']}: {best['path'].name} wer={best['wer']:.2f} sim={best['sim']:.3f} "
          f"passing={picks[-1]['passing']}/{len(takes)}{flag}", flush=True)

(ldir / "picks.json").write_text(json.dumps(picks, indent=2, default=str), encoding="utf-8")
