"""Comparison render: the whole exchange in one pass with MOSS-TTSD v1.0 (runs in third_party/venv-ttsd).

Speaker identities come from the same frozen reference clips as the per-line render
(Zyx = patter, Sable = deadpan), so the only difference is joint generation: the model
handles turn-taking itself, and nothing is directed per line. No audience.

Output: out/ttsd/run16-ttsd.wav
"""
import json
from pathlib import Path

import numpy as np
import soundfile as sf
import torch
import yaml
from huggingface_hub import snapshot_download
from transformers import AutoModel, AutoProcessor

torch.backends.cuda.enable_cudnn_sdp(False)  # per the model card
ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "out" / "ttsd"
OUT.mkdir(parents=True, exist_ok=True)

voices = yaml.safe_load((ROOT / "src" / "voices.yaml").read_text(encoding="utf-8"))
cues = yaml.safe_load((ROOT / "src" / "cues.yaml").read_text(encoding="utf-8"))
sel = json.loads((ROOT / "out" / "voices" / "selection.json").read_text(encoding="utf-8"))
REF = {"zyx": ("S1", "patter"), "sable": ("S2", "deadpan")}

model_dir = snapshot_download("OpenMOSS-Team/MOSS-TTSD-v1.0", local_dir=str(ROOT / "models" / "MOSS-TTSD-v1.0"))
processor = AutoProcessor.from_pretrained(model_dir, trust_remote_code=True)
# The audio tokenizer is a separate fp32 model; on the GPU next to the 8B LM it overflows
# 24 GB. It encodes the references on the CPU and moves to the GPU only for the decode.
tok_params = sum(p.numel() for p in processor.audio_tokenizer.parameters())
print(f"audio tokenizer {tok_params / 1e9:.2f}B params, {next(processor.audio_tokenizer.parameters()).dtype}", flush=True)
# transformers 5 renamed torch_dtype to dtype; with the old name the 8B model loads in fp32,
# overflows the 24 GB card into shared memory and generates ~100x slower
model = AutoModel.from_pretrained(model_dir, trust_remote_code=True, attn_implementation="sdpa",
                                  dtype=torch.bfloat16).to("cuda").eval()
print("model dtype", next(model.parameters()).dtype,
      f"{torch.cuda.memory_allocated() / 2**30:.1f} GiB allocated", flush=True)
target_sr = int(processor.model_config.sampling_rate)


def load_ref(vname):
    tag, mode = REF[vname]
    take = sel[vname]["modes"][mode]["take"]
    w, sr = sf.read(ROOT / "out" / "voices" / vname / mode / take, dtype="float32")
    w = torch.from_numpy(w if w.ndim == 1 else w.mean(1))[None]
    if sr != target_sr:
        import torchaudio.functional as AF
        w = AF.resample(w, sr, target_sr)
    return w, f"[{tag}] {voices[vname]['modes'][mode]['text']}"


w1, t1 = load_ref("zyx")
w2, t2 = load_ref("sable")
dialogue = " ".join(f"[{REF[l['speaker']][0]}] " + " ".join(s["text"] for s in l["segments"]) for l in cues["lines"])

refs = processor.encode_audios_from_wav([w1, w2], sampling_rate=target_sr)
prompt_audio = processor.encode_audios_from_wav([torch.cat([w1, w2], dim=-1)], sampling_rate=target_sr)[0]
conv = [[processor.build_user_message(text=f"{t1} {t2} {dialogue}", reference=refs),
         processor.build_assistant_message(audio_codes_list=[prompt_audio])]]

with torch.no_grad():
    batch = processor(conv, mode="continuation")
    outputs = model.generate(input_ids=batch["input_ids"].to("cuda"),
                             attention_mask=batch["attention_mask"].to("cuda"), max_new_tokens=6000)
outputs = outputs.cpu() if torch.is_tensor(outputs) else outputs
del model
torch.cuda.empty_cache()
processor.audio_tokenizer = processor.audio_tokenizer.to("cuda")
for k, message in enumerate(processor.decode(outputs)):
    audio = message.audio_codes_list[0].float().cpu().numpy()
    sf.write(OUT / f"run16-ttsd{'' if k == 0 else k}.wav", audio.T if audio.ndim > 1 else audio, target_sr)
    print("wrote", OUT / "run16-ttsd.wav", f"{audio.shape[-1] / target_sr / 60:.1f} min", flush=True)
