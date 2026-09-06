# Pinder Comedy & Character Authoring Guide

This document is the authoritative standard for comedic voice, character generation, item fragments, and prompt authoring across Pinder.

---

## 1. The Core Vision: The Unhinged Farce of Hookup Apps

Pinder is not a polite, wholesome dating game. It is a satirical, high-stakes, R-rated farce about the visceral, chaotic reality of modern hookup culture (Tinder, Grindr, Hinge). 

### The Primal Drivers & The Core Conflict
The engine of Pinder runs on the friction between **filthy, cynical physical urgency** and the **pathetic, desperate hope for real romantic connection**:
- **The Hookup as a Consolation Prize:** Almost everyone on these apps secretly wants genuine romance, love, or emotional safety, but after months/years of rejection, they tell themselves: *"Whatever, I'll settle for getting laid tonight because I can't get anything else right now."*
- **The Asymmetric Priority Dance:**
  - One person opens with romantic testing (subtle bids for intimacy, checking if the match is 'safe' or 'real').
  - The other person counters with detached, horny transactionalism (trying to keep it purely physical to avoid getting hurt).
  - *The Absurd Inversion:* Sometimes **both** secretly want romance, but both pretend they only want a casual hookup to avoid vulnerability—and end up hooking up in defensive mutual despair.
- **Validation Addiction & App Sickness:** Swiping out of loneliness, late-night boredom, or a dare from friends, scratching the dopamine itch until it bleeds.
- **The Bodily Farce:** The protagonists are sentient penises—the premise is inherently embodied, sexual, awkward, and vulnerable.

---

## 2. The Unified Fragment Architecture (Items & Anatomy as Equals)

In Pinder, **Items and Anatomy are 100% architecturally symmetrical**. They are not separate systems—they are both **Fragment Providers** evaluated identically by `CharacterAssembler`:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        UNIFIED FRAGMENT PROVIDER                       │
│                                                                        │
│   1. Item Source (Discrete Slot):                                      │
│      - 99 Items across Head, Face, Body, Arms, Hair, Special, Tattoo   │
│                                                                        │
│   2. Anatomy Source (Continuous Tier Bands):                           │
│      - 21 Sliders (Base, Tip, Veins, Curvature, Freckles, Smoothness)  │
│      - Each slider has 5–6 discrete Bands (0–5%, 20–50%, 95–100%)      │
│                                                                        │
│   BOTH PROVIDE THE EXACT SAME DATA TRIAD:                              │
│   ├── backstory_fragment     (Past humiliating incident / bathos)      │
│   ├── personality_fragment   (In-chat dating armor / defense reflex)   │
│   └── texting_style_fragment (Syntax, shorthand, tics, casing)         │
└────────────────────────────────────────────────────────────────────────┘
```

### The Symmetrical Authoring Doctrine
- **Items = Sartorial Armor:** What you choose to wear or ink onto yourself to project status, cope with past eras, or seduce a match.
- **Anatomy = Embodied Awkwardness:** The physical body you are stuck with—unavoidable physical quirks, asymmetry, circulation fails, embarrassing teen growth spurts, or unprovoked bodily dysmorphia.
- **Identical Rules:** Whether writing an item fragment for a *Chef Hat* or an anatomy band fragment for *High Curvature*, the writing rules, comedy guidelines, and Definition of Done are **identical**.

---

## 3. The Definition of Done (DoD) for All Fragments

Whether authoring an **Item** or an **Anatomy Band**, a fragment is **LOCKED & READY** only if it hits these 5 marks:

1. **Assumption A (Clean Separation):**
   - `backstory_fragment`: Houses the concrete, humiliating past incident (bathos: pretense meets undignified reality).
   - `personality_fragment`: Houses the active in-chat dating armor (how the character defends or weaponizes this on Tinder/Grindr).
2. **The Bathos Drop:** Pretense punctured by pathetic adult reality (names, dates, places, concrete screwups).
3. **Pure Hookup Reality (Zero Technical/Academic Escapism):** Strictly inside hookup/app domains (sex, ego, isolation, domestic mess). **ZERO** architecture, math manifestos, engineering jargon, or sci-fi.
4. **Adult R-Rated Comedy Spectrum:** Dumb enough for a horny 18-year-old on a Friday night; sharp enough for a divorced 35-year-old PhD with chronic dating burnout.
5. **Word Count Punch:** 25 to 45 words per fragment (punchy, fast, zero filler).

---

## 4. The Interest Meter (0–25) & Date Gating Rules

The datee's emotional availability is governed strictly by the **0–25 Interest Meter**:

```
[0: Ghosted] ──── [1-5: Bored] ──── [6-10: Curious] ──── [11-15: Engaged] ──── [16-20: Very Into It] ──── [21-24: Almost There] ──── [25: DATE SECURED]
```

### The "Interest < 25 Hold-Off" Law
- **Premature Date Pushes MUST Be Resisted:** If the player tries to close the date, ask for an address, or push for a meetup when `Interest < 25` (even at 21–24 `AlmostThere`), the datee **MUST HOLD OFF**.
- **The Hold-Off Dynamic:**
  - The datee deflects, teases, throws up one last barrier, or demands proof of sincerity/vulnerability (*"Not so fast"*, *"You haven't earned the address yet"*, *"Prove you won't get weird in the morning"*).
  - This creates delicious comedic and sexual tension: the player is inches away from closing, but one wrong move or failed roll can trigger a trap or cause a catastrophic slip.
- **Only at Exactly Interest = 25:** The datee officially drops their final barrier and agrees to the meetup.

---

## 5. The 4 Multi-Roll Systems & Gameplay Impact

Every turn in Pinder is a multi-layered check:
1. **Option Roll (d20 + Stat vs Datee DC):** The main conversational maneuver (`Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `SelfAwareness`).
2. **Horniness Check (Composure vs Thirst DC):** Determines if physical lust, clumsiness, or feral thirst corrupts the message delivery.
3. **Shadow Roll (Paired Neurosis Check):** Determines if psychological baggage (`Madness`, `Despair`, `Denial`, `Fixation`, `Dread`, `Overthinking`) triggers a defensive spiral.
4. **Steering Roll:** Allows tactical steering between casual hookup acceleration and deep romantic probing.
5. **Trope Traps (Single-Slot State):** Nat-1s or severe failures arm devastating traps (e.g. `Ghosting_Paranoia`, `Overshare_Spiral`) that force disadvantage or penalty states until cleared.

---

## 6. The Generator & Critic Pipeline (Claude Code CLI / Opus)

```
┌────────────────────────────────────────────────────────┐
│ 1. THE GENERATOR (Claude Opus / Sonnet)                │
│ Drops an unhinged, R-rated candidate fragment          │
└───────────────────────────┬────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────┐
│ 2. THE CRITIC (The Comedy Club Heckler)                │
│ Evaluates the fragment against the 5 Vibe Check marks  │
└───────────────────────────┬────────────────────────────┘
                            │
             ┌──────────────┴──────────────┐
             ▼                             ▼
       [ ALL PASS ]                  [ 1+ FAILS ]
             │                             │
             │                             ▼
             │                   ┌───────────────────────────────┐
             │                   │ 3. ONE PUNCH-UP TURN          │
             │                   │ One sharp, direct correction  │
             │                   └──────────────┬────────────────┘
             │                                  │
             │                     ┌────────────┴────────────┐
             │                     ▼                         ▼
             │               [ PASSES ]                 [ STILL FAILS ]
             │                     │                         │
             ▼                     ▼                         ▼
   ┌───────────────────┐ ┌───────────────────┐    ┌───────────────────────────┐
   │ COMMIT TO CONFIG  │ │ COMMIT TO CONFIG  │    │ KILL CONTEXT & RESTART    │
   │ (items / anatomy) │ │ (items / anatomy) │    │ Fresh blank context       │
   └───────────────────┘ └───────────────────┘    └───────────────────────────┘
```
