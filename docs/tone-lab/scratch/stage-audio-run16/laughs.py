"""Generate the audience reaction bank with MOSS-SoundEffect v2. Runs in third_party/venv-sfx.

Output: out/laughs/<reaction>/take<k>.wav (48 kHz)
"""
import os
import sys
from pathlib import Path

os.environ.setdefault("TORCHDYNAMO_DISABLE", "1")  # no Triton on Windows

import soundfile as sf
import torch
import yaml
from moss_soundeffect_v2 import MossSoundEffectPipeline

ROOT = Path(__file__).resolve().parent.parent
TAKES = int(sys.argv[1]) if len(sys.argv) > 1 else 6
cfg = yaml.safe_load((ROOT / "src" / "laughs.yaml").read_text(encoding="utf-8"))["reactions"]

from huggingface_hub import snapshot_download

# a plain local folder: the HF cache needs symlinks, which this Windows account cannot create
model_dir = snapshot_download("OpenMOSS-Team/MOSS-SoundEffect-v2.0", local_dir=str(ROOT / "models" / "MOSS-SoundEffect-v2.0"))
pipe = MossSoundEffectPipeline.from_pretrained(model_dir, torch_dtype=torch.bfloat16, device="cuda")
for name, r in cfg.items():
    d = ROOT / "out" / "laughs" / name
    d.mkdir(parents=True, exist_ok=True)
    for k in range(TAKES):
        p = d / f"take{k}.wav"
        if p.exists():
            continue
        # the pipeline seeds its own noise (default 0), so each take needs its own seed
        audio = pipe(prompt=r["prompt"], seconds=r["seconds"], num_inference_steps=100, cfg_scale=4.0,
                     seed=1000 + k, negative_prompt="music, a single person speaking, narration, silence")
        wav = audio.detach().float().cpu()
        wav = wav[0] if wav.ndim == 3 else wav
        sf.write(p, wav.T.numpy(), int(pipe.sample_rate))  # torchaudio.save needs FFmpeg DLLs
    print("generated", name, flush=True)
