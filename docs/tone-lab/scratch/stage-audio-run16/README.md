# Run 16 on stage: audio experiment

Tone-lab run 16 (Jimmy Carr's Zyx_444 × Sable_xo chat, `writers-v16/chat-zyx-sable/carr.md`)
performed as a two-hander in a comedy club: two invented voices, a generated audience,
a room. Experiment only, rendered on the workstation RTX 4090 at Daniel's instruction
(25 Nov 2026); nothing here runs in the game.

## Pipeline

1. `cues.yaml`: Carr's words unchanged except what only reads on a screen (currency,
   percentages, units, em-dash asides). Each message is split where a laugh lands, and
   every segment gets a delivery mode, an audience reaction and an optional beat.
2. `design_voices.py` + `voices.yaml`: Qwen3-TTS VoiceDesign renders 8 takes of each
   voice in four deliveries from one fixed description. Takes Whisper mishears are
   dropped; of the rest, the four with the highest minimum pairwise speaker-embedding
   similarity are frozen as ICL clone prompts. Qwen3-TTS accepts acting instructions
   only in VoiceDesign, not for clones, so delivery comes from which frozen reference a
   line is routed to.
3. `render_lines.py`: 6 clone takes per segment (57 segments). A take passes when
   Whisper hears the script (WER ≤ 0.12, digits spelled out) and its length is near the
   median; the passing take closest to the voice's centroid wins. All 57 pass.
4. `laughs.py` + `laughs.yaml`: MOSS-SoundEffect v2 (Apache-2.0, 48 kHz) generates 6
   takes per reaction (titter, laugh, big, huge, ooh, groan, aww, applause, murmur).
   The pipeline seeds its own noise; each take needs `seed=`.
5. `mix.py`: CLAP (laion/larger_clap_general) ranks each reaction's takes against the
   prompt; two different takes, one each side, make a mono reaction into a wide room.
   The performer waits a fixed stage hold per reaction size; generated laughs decay far
   more slowly than a real room, so the tail is faded under the next line and the crowd
   is ducked under speech. Mic chain (high-pass, presence, slight PA drive), synthetic
   club IR, murmur bed, loudness -16 LUFS.
6. `ttsd_compare.py`: the whole exchange in one pass with MOSS-TTSD v1.0 from the same
   frozen references (Zyx patter, Sable deadpan), no direction, no audience, as the
   comparison. `ttsd_words.py` (faster-whisper word timestamps, CPU) and
   `ttsd_segment.py` cut it into the same 57 segments and check each segment's voice;
   `mix.py out/ttsd/picks.json run16-ttsd-stage` stages it with the same audience.

7. Directed iteration (Daniel's listening notes: acting does not hold even in one pass,
   two messages from one speaker sound like two restarts, pacing and tone are off, pauses
   and intonation are what a comedian works with):
   - `performance.yaml`: 28 turns, consecutive messages from one speaker merged into one
     turn, each with an acting note written the way a comedian would play it and markup
     for beats (`/ // ///`), the operative word (`*word*`), laugh points (`{laugh}`) and
     genuinely rising questions (`^`). `direct.py` checks the words still equal
     `cues.yaml` exactly.
   - `direct.py`: Qwen3-TTS VoiceDesign renders each turn as one utterance from the
     voice description plus the acting note (VoiceDesign is the only mode that takes
     direction; clones ignore it), 14 takes per turn. Gates: Whisper WER ≤ 0.1, and
     speaker embedding at least 0.008 closer to the frozen voice than to the other one.
     Among passing takes the winner is chosen on measured delivery: phrase breaks where
     the script asks for a beat, articulation rate against the turn's pace, pitch at the
     end of each landing phrase (falling, or rising for `^`), and stress on operative
     words. The winner's silences are then set to the marked beat lengths at
     word boundaries, and it is cut at the laugh points for the stage mix.
   - `mix.py out/direct/picks.json run16-directed-stage` stages it; `--dry` gives room
     and mic with no audience and a half-second beat at each laugh point.
   - `compare_landing.py`: the same landing check over all three renders' punchlines.

Outputs (workstation `out/`, not committed): `run16-directed-dry.mp3` (5.3 min),
`run16-directed-stage.mp3` (7.1 min), `run16-stage.mp3` (per-line, 6.7 min),
`run16-ttsd-stage.mp3` (one-pass, staged, 6.6 min), `run16-ttsd-raw.mp3` (one-pass,
no audience, 4.3 min), `voice-audition.mp3`.

## What the checks showed

- Intelligibility in the mix is the binding constraint. With holds taken from laugh
  length the next line was buried after big laughs (22 of 57 segments above 10% WER,
  Whisper hearing applause as "thank you"). Fixed stage holds plus fading the tail:
  1 of 57, a single word ("cling-film") under a laugh tail.
- Waiting for generated laughs to decay naturally stretched the set to 8 minutes;
  stage holds (titter 0.9 s … huge 4.8 s) bring it to 6.7.
- The one-pass render says the whole script (1.1% WER over 772 words), puts all 57
  segments in the right voice (speaker-embedding check), and staged the same way has 3
  segments with one masked word each, all directly under a big laugh.
- MOSS-TTSD's audio tokenizer is a separate 1.77B fp32 model (~6.6 GB). Kept on the GPU
  beside the 8B LM it overflows 24 GB and generation drops from ~19 to ~0.3 tokens/s;
  it encodes on the CPU and moves to the GPU only for the final decode. Transformers 5
  also ignores `torch_dtype` (fp32 load); it takes `dtype`.
- CLAP scores the "huge laugh with applause" takes lowest of all reactions (≈0.26 vs
  ≈0.5); that category is the weakest output of the sound-effect model.

- Directed iteration: the delivery of a punchline varies enormously between takes of
  the same instruction. In one pilot turn, "retail" ended anywhere from 10 semitones up to
  10 semitones down across 8 takes, so choosing among takes does most of the directing;
  the instruction only moves the distribution. Statement punchlines ending
  ≥1 semitone below their phrase: directed 41 of 44 measured, per-line clones 32 of 40,
  one-pass 30 of 39; lifts 1, 4 and 6. The directed number is partly circular, since takes
  were chosen on this measure. Its value is that no punchline now goes up at the end
  unnoticed. Slow turns still come out faster than a stand-up delivers them (Sable's
  "slow" turns average about 2.7 words/s against 2.4 asked), so the beats are set in the
  edit rather than left to the model. About a third of takes fail the voice gate
  (VoiceDesign re-imagines the voice from the description on every call). Staged, no
  segment is masked by the crowd; 6 of 57 segments are above 10% WER, all word-level
  slips that are in the dry mix too ("knob gag" heard as one word, "ring light" as
  "ring like").

## Environments (workstation, `third_party/`, not committed)

`venv-tts` torch 2.9 + qwen-tts 0.1.1 + faster-whisper (design, render, direct, mix); `venv-sfx`
MOSS-SoundEffect v2; `venv-ttsd` MOSS-TTS with transformers 5. The older
qwen3-tts-server venv picks up miniconda's torch 2.3, which breaks the Qwen3-TTS
decoder under transformers 4.57. Models download to plain folders (`models/`) because
the HF cache needs symlinks this Windows account cannot create.
