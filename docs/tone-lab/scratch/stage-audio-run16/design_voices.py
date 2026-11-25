"""Design each voice in each delivery mode, then freeze the most mutually consistent takes.

Output: out/voices/<voice>/<mode>/take<k>.wav, out/voices/<voice>/<mode>.prompt.pt,
        out/voices/selection.json
"""
import itertools
import json
import sys

import soundfile as sf
import torch

from common import OUT, load_asr, load_tts, load_yaml, speaker_embedding, transcribe, wer

TAKES = int(sys.argv[1]) if len(sys.argv) > 1 else 8
voices = load_yaml("voices.yaml")
vdir = OUT / "voices"

# 1. design takes
design = load_tts("design")
for vname, v in voices.items():
    for mode, m in v["modes"].items():
        d = vdir / vname / mode
        d.mkdir(parents=True, exist_ok=True)
        if len(list(d.glob("take*.wav"))) >= TAKES:
            continue
        instruct = f"{v['base']} {m['direction']}"
        wavs, sr = design.generate_voice_design(
            text=[m["text"]] * TAKES, instruct=[instruct] * TAKES, language=["English"] * TAKES
        )
        for k, w in enumerate(wavs):
            sf.write(d / f"take{k}.wav", w, sr)
        print(f"designed {vname}/{mode}: {TAKES} takes", flush=True)
del design
torch.cuda.empty_cache()

# 2. score takes: words right (ICL needs an exact transcript) and speaker embedding
asr = load_asr()
base = load_tts("base")
selection = {}
for vname, v in voices.items():
    cands = {}
    for mode, m in v["modes"].items():
        cands[mode] = []
        for p in sorted((vdir / vname / mode).glob("take*.wav")):
            w, sr = sf.read(p, dtype="float32")
            hyp = transcribe(asr, w, sr)
            e = wer(m["text"], hyp)
            dur = len(w) / sr
            ok = e <= 0.08 and 3.0 <= dur <= 16.0
            cands[mode].append(dict(path=p, wer=e, dur=dur, ok=ok, emb=speaker_embedding(base, w, sr), hyp=hyp))
        if not any(c["ok"] for c in cands[mode]):
            print(f"WARNING {vname}/{mode}: no take passed the word check; using all", flush=True)
            for c in cands[mode]:
                c["ok"] = True

    # 3. pick one take per mode maximising the minimum pairwise speaker similarity
    modes = list(cands)
    pools = [[c for c in cands[mode] if c["ok"]] for mode in modes]
    best, best_score = None, -1.0
    for combo in itertools.product(*pools):
        sims = [float(a["emb"] @ b["emb"]) for a, b in itertools.combinations(combo, 2)]
        score = min(sims) + 0.25 * sum(sims) / len(sims)
        if score > best_score:
            best, best_score = combo, score
    selection[vname] = {"score": best_score, "modes": {}}
    for mode, c in zip(modes, best):
        w, sr = sf.read(c["path"], dtype="float32")
        item = base.create_voice_clone_prompt(ref_audio=(w, sr), ref_text=v["modes"][mode]["text"])[0]
        torch.save(item.__dict__, vdir / vname / f"{mode}.prompt.pt")
        selection[vname]["modes"][mode] = {
            "take": c["path"].name, "wer": round(c["wer"], 3), "dur": round(c["dur"], 2), "asr": c["hyp"],
            "passing_takes": sum(x["ok"] for x in cands[mode]),
        }
    print(vname, json.dumps(selection[vname], indent=1), flush=True)

(vdir / "selection.json").write_text(json.dumps(selection, indent=2), encoding="utf-8")
