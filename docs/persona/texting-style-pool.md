# Texting-Style Pool

This is the authoring reference for item and anatomy
`texting_style_fragment` values. Texting style shapes how a message is
expressed; it does not replace the character's personality, emotional state,
meaning, or response to the current conversation.

## Canonical model

The system has nine canonical axes in this order:

1. `emoji`
2. `shorthand`
3. `grammar`
4. `structure`
5. `length`
6. `tics`
7. `directness`
8. `affect`
9. `rhythm`

The first six axes describe observable syntax. The final three describe
expression:

- `directness`: how openly or indirectly the character communicates intent.
- `affect`: how much emotional warmth, intensity, or restraint is visible.
- `rhythm`: the pace, density, and cadence of the message.

These are tendencies, not a checklist. A selected fragment may have no visible
effect on a turn when it would fight the message's meaning, the character's
emotional reaction, or natural variation.

## Fragment shape

New content uses `SYNTAX:` and `EXPRESSION:` sections:

```text
SYNTAX:
- emoji:
  - may use a soft emoji as terminal punctuation when warmth is visible
  - tends to use a recurring unusual emoji as a personal tic
- shorthand: tends to use "ngl" when introducing a candid thought
- grammar: is comfortable leaving casual messages uncapitalized
- structure: tends to split layered thoughts into a few short lines
- length: usually keeps a reply compact without cutting off needed meaning
- tics: may echo one important word before answering
EXPRESSION:
- directness: tends to imply attraction before naming it directly
- affect: is comfortable letting guarded warmth show through dry phrasing
- rhythm: tends to begin measured and accelerate when emotionally engaged
```

An axis may contain one candidate or a pool of candidates. A pool uses one
axis bullet followed by indented candidate bullets, as shown for `emoji`.
Core selects exactly one candidate from that source and axis using a stable
hash of the character seed, source identity, axis, and ordered candidate list.
The same inputs select the same candidate across sessions. Changing the seed,
source metadata, candidate order, or candidate text may change the selection.

Do not expect every axis written on an item to reach the aggregate. Each item
slot owns one syntax axis, and each anatomy parameter contributes only to its
expression group. See [texting-style-aggregation.md](texting-style-aggregation.md)
for the active mappings and voting rule.

`TONE:` and the old `stance`, `register`, and `pacing` keys remain parser
aliases for existing data only. New content must use `EXPRESSION:` with
`directness`, `affect`, and `rhythm`.

## Authoring principles

- **Write soft influence.** Prefer "may", "tends to", "often", "usually",
  and "is comfortable with". Reserve an absolute rule for a deliberately
  strict signature quirk, label it `strict`, and use it sparingly.
- **Keep syntax observable.** A reviewer should be able to point to casing,
  punctuation, line breaks, length, shorthand, emoji, or a recurring move.
- **Keep expression separate from emotion.** An affect tendency describes how
  emotion becomes visible; it does not prescribe that the character is happy,
  angry, or attracted on every turn.
- **Preserve message intent.** A style fragment must not force the model to
  contradict the game state or flatten a character-specific emotional
  reaction.
- **Author one idea per candidate.** Candidate pools provide bounded variety;
  they are not a way to combine several mandatory instructions in one line.
- **Avoid duplicates and contradictions.** Search the pool and conflict
  catalog before adding a candidate. Register genuinely incompatible pairs in
  `data/persona/texting-style-conflicts.yaml`.
- **Write for texting.** Prefer artifacts visible on a phone screen over
  spoken-performance directions such as stuttering or vocal pitch.

## Candidate pool

These examples are starting points. Adapt them into a specific, concise
tendency rather than copying several into one fragment.

### SYNTAX - emoji

- may use an emoji as terminal punctuation when it reinforces the emotion
- tends to reuse one unusual emoji as a recognizable personal tic
- may send an eyes emoji as a complete reply when testing the other person
- tends to use emoji between clauses instead of conventional punctuation
- usually leaves emoji out, making their appearance emotionally significant

### SYNTAX - shorthand

- tends to use "lol" as a softener inside a sentence
- may stretch "lmaooo" in proportion to genuine amusement
- often introduces candid thoughts with "ngl"
- is comfortable using concise forms such as "u", "ur", and "pls"
- usually writes words out, using shorthand only when emotionally rushed

### SYNTAX - grammar

- is comfortable writing casual messages in lowercase
- tends to join related thoughts with comma splices rather than full stops
- may use an ellipsis as a hesitant clause break
- tends to omit apostrophes from contractions when typing quickly
- usually writes with careful punctuation, which can make short replies feel
  deliberate

### SYNTAX - structure

- tends to use a few short lines with breathing room between ideas
- may send a dense single paragraph when a thought gathers momentum
- is comfortable using parenthetical asides for self-aware commentary
- may open with a compact topic label such as "UPDATE:"
- tends to send a follow-up fragment as a punchline when the moment supports it

### SYNTAX - length

- usually keeps replies compact while retaining the necessary emotional beat
- tends to expand as trust and engagement increase
- may become terse when cooling or protecting themself
- is comfortable sending a layered paragraph when genuinely absorbed
- tends to mirror the conversational weight of the previous message without
  mechanically matching its word count

### SYNTAX - tics

- may use a standalone question mark to challenge an unclear claim
- tends to echo one important word before responding
- may end a rant with a restrained self-tag such as `/rant`
- is comfortable asking a question when curiosity is the natural next move
- may reference the time of day when the conversation becomes unexpectedly
  intimate

### EXPRESSION - directness

- tends to state interest plainly once trust is established
- may imply what they want and leave room for the other person to meet them
- is comfortable challenging an evasion without becoming clinical
- tends to redirect personal questions until the exchange feels reciprocal
- may turn a disagreement into a playful counteroffer

### EXPRESSION - affect

- tends to let warmth show through restrained, dry phrasing
- is comfortable sounding openly enthusiastic when something lands
- may soften vulnerability with humor without erasing the admission
- tends to keep strong feeling controlled until the other person feels safe
- may show suspicion through cooler wording rather than direct accusation

### EXPRESSION - rhythm

- tends to write in a measured cadence with deliberate pauses
- may become fast and overlapping when excited or unsettled
- is comfortable sending two linked messages when one thought triggers another
- tends to leave more space when guarded and become denser when engaged
- may build from a short opening into a more revealing second beat

## Review checklist

- The fragment uses only canonical axis names for new content.
- Each candidate is a tendency and leaves room for the immediate moment.
- Syntax is observable; expression describes delivery rather than a fixed mood.
- Candidate indentation follows the supported pool shape.
- A strict quirk, if present, is intentional, labeled, and narrowly scoped.
- The fragment does not contradict another selected style without a conflict
  catalog entry.
