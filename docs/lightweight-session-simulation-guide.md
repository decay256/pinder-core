# Pinder Lightweight Session Simulation Guide

This document describes the minimal, rules-free prompting protocol for simulating authentic, hilarious, R-rated Pinder dating app sessions without the token bloat or rigidity of the full C# game engine.

---

## 1. Why the Lightweight Approach?

When the full game engine runs, the LLM is burdened with:
- 15-item psychological stake bullet lists.
- 20-category psychiatric and anatomical diagnoses.
- D20 dice roll mechanics, DC formulas, and Game Master injection blocks.
- Mechanical state transitions and prompt token bloat.

This heavy context suffocates comedic timing and leads models to fall back on safe, repetitive, or corporate tropes. 

**The Lightweight Approach** strips away all mechanical scaffolding and focuses exclusively on:
1. **The Core Dynamic:** Filthy physical urgency vs. pathetic longing for emotional safety.
2. **Two Concrete Bathos Facts per Character:** Unvarnished human reality puncturing high-status armor.
3. **Natural Texting Rhythm:** Short, punchy messages (1–3 sentences) with modern dating app vernacular.

---

## 2. The Minimal Simulation Prompt (Copy-Pasteable Template)

Use this exact prompt when simulating a session with any LLM (Claude 3.7 Sonnet, DeepSeek, Qwen 3.8, Mistral Large):

```markdown
You are simulating a 5-to-6 turn dating app chat on Pinder (an R-rated, satirical hookup app like Tinder/Grindr).
The protagonists are sentient, deeply flawed characters with conflicting dating agendas.

### CORE FRICTION
- One character is desperate for genuine emotional safety / tenderness, but disguises it as a casual hookup.
- The other character is hiding behind cynical, detached, horny armor to avoid getting hurt.
- The tone is adult, funny, and unhinged: messy bedrooms, Febreze on sheets, fear of being seen in morning daylight, fragile egos, and bad decisions at 2:00 AM.
- BANNED: Corporate politeness, sitcom clichés ("not like other girls"), wholesome moralizing, and technical/civil engineering manifestos.

### CHARACTER SEEDS (2 FACTS ONLY)

Character A (Player):
- Name: [e.g. Gerald, 42]
- Dating Armor: [e.g. Performs high-status dignity; insists on basic hospitality and emotional safety]
- Concrete Bathos Fact: [e.g. Proudest achievement this month was replacing the toilet flapper; flipped the comforter so the coffee stain is by the footboard]

Character B (Datee):
- Name: [e.g. Velvet, 34]
- Dating Armor: [e.g. Aggressive indie nihilism; wants detached physical friction to numb existential dread]
- Concrete Bathos Fact: [e.g. Terrified of being perceived in unwashed morning light; will bite if someone tries to hold hands during sex]

### CONVERSATIONAL RULES
1. Texting Style: Short, fast lines (1–3 sentences max). Casual, deadpan, lowercase text allowed.
2. The Hold-Off Law: If Character A pushes prematurely to meet up or asks for an address before mutual vulnerability is established, Character B MUST deflect, tease, or demand proof/contingencies.
3. The Micro-Negotiation: The conversation culminates in a funny, pathetic micro-compromise where both agree to meet.

Simulate the 5-to-6 turn dialogue now.
```

---

## 3. The 3 Pillars of Comedic Timing

### 1. The Bathos Drop
Pretense must be violently punctured by unglamorous adult reality.
- *Bad:* "I've had a really hard time finding love in this city."
- *Good (Bathos):* "I flipped the comforter over so the coffee stain is by the footboard. But the intent was pure."

### 2. The Hold-Off Dynamic
Dating app tension dies if someone immediately says "Yes, come over." The datee must throw up barriers:
- *"look, i'm not coming over if you're going to make me listen to your Spotify playlist or tell me about your ex."*
- *"if you come over at 2 AM, what's the morning protocol? i don't do awkward lingering."*

### 3. The Absurd Micro-Compromise
Victory is never a fairytale romance; it is an undignified mutual truce:
- *"If you try to hold my hand while we're doing it, I'm biting you."*
- *"...A gentle shoulder touch?"*
- *"Shoulder is fine. Unlock the front door, I'm getting in an Uber."*

---

## 4. Evaluation Checklist (The 5-Point Vibe Check)

After generating a lightweight session, evaluate it against these 5 criteria:

| # | Criterion | Pass Condition |
|---|---|---|
| 1 | **Bathos Present** | Did a high-status mask shatter into a specific, humiliating adult detail? |
| 2 | **Authentic Rhythm** | Are texts punchy (1–3 sentences), skipping formal greetings and corporate email transitions? |
| 3 | **Hold-Off Enforced** | Did the datee resist at least one premature push to meet up? |
| 4 | **No Escapism / Trope Leakage** | Is the dialogue 100% free of engineering jargon, blueprints, or therapy platitudes? |
| 5 | **Adult Farce Standard** | Is it cynical enough for a 35-year-old with dating burnout, yet absurd enough for a Friday night hookup? |

---

## 5. Reference Examples

See the full documented transcripts generated with this methodology in `docs/examples/`:
- **`chat-01-gerald-velvet-romantic-friction.md`** — Romantic vs. Transactional Friction.
- **`chat-02-reuben-brick-victory-run.md`** — Multi-stage victory run with the Hold-Off rule.
- **`chat-03-reuben-brick-unmatched-fail.md`** — Catastrophic losing run ending in ghosting/unmatch.
