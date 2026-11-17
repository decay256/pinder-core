# Game state in the tone lab: what describes it, and how the models use it

This is the one definition of the game state the tone lab works from. It lists every piece
of information that describes a moment in a Pinder chat. For each one it says what it means,
which call is told it and in what words, what the director does with it, and what the code
checks. It also defines the emotional state: how each character's feelings are read,
carried and checked.

Every text named here lives in the situation table (`tools/tonelab/variants/live/situations.yaml`,
rendered at `docs/tone-lab/situations.html`). The table's page shows how those texts are
assembled into each prompt. The code that applies these rules is `tools/tonelab/live.py`.

## The principle

The game produces numbers: stats, dice, a meter, trap counters. The writer needs meaning:
what this message is, how it lands, what the other one feels, what the story owes.
So each value the game has is either **interpreted in words** or **left out as technical**.
A value is technical when it changes the numbers but not what anyone writes. Nothing reaches
the director as a raw number, except the interest level, which comes with its meaning.

Two rules keep the interpretation usable:

1. **Each thing is said once, from one source.** When two texts describe the same thing, one of
   them is removed. Otherwise they eventually disagree and the director has to pick a side. (The
   v14 review found nine such cases; the rules they produced are in "Consistency rules" below.)
2. **The turn message is the state at the start of the turn, then what this turn did.** Anything
   this turn causes is told under "This turn", never as if it had been true before.

## Who is told what

| Call | When | State it gets |
|---|---|---|
| Plan | Once, before the chat | Session, both characters |
| Director, system prompt | Once; the session keeps it | The feelings and how each usually shows in a text; the notes, the checklist and the review it works through |
| Director, turn message | Every turn, in one session that keeps every earlier turn and note | Relationship, story position, what has repeated, this turn, what is required, length |
| Director, retry | When a turn broke a checked rule | The broken rules, in words |
| Reader and grader | Once, after the chat | The finished chat only; the grader also gets the plan |
| Attribution test | Once, after the chat | Every message, shuffled, with no names |

The director never sees the character files or the mechanics. It sees only the plan, the turn
messages, and its own earlier answers.

## 1. Session: in the plan only

| Game value | Meaning | How it is told | Table |
|---|---|---|---|
| Time of day | The register of the whole chat | "It's late at night." | `session.times` |
| Session horniness, 1–10 | How near the surface the heat sits tonight | One sentence per band: low (0–3), ordinary (4–6), high (7–8), very high (9–10) | `session.heat` |

## 2. Characters: in the plan only

Each character card is built from the game's character file and the table. The plan turns it
into what the director works from. Nobody downstream sees the card.

| Game value | Meaning | How it is told | Table |
|---|---|---|---|
| Display name, pronouns, bio, body | What the other one can see on the profile | As in the file; the body as "only the extremes", on a 0–100 scale | `prompts.character` |
| Effective stats | Which moves come off for this character and which don't | Strong (12 or more), weak (2 or less), middling, each with what the move means for them | `stats`, `moves.*.strong`, `moves.*.weak` |
| Shadows (`allocation.shadows`) | What each move turns into when it goes wrong for them, and how far each has grown | Strongest first: the shadow, its value, the move it belongs to, what it does | `character.shadow_line`, `shadows.*.means` |
| Texting style, personality, backstory | Who they are and how they type | As in the file | `prompts.character` |
| Diagnosis (wound, defence, safe connection) | What hurts them, how they defend, what makes them feel safe | As the clinician wrote it, with the order to translate it into a person | `prompts.character` |
| Backstory pair, drawer lines | Tonight's material: the brag and the truth to build the secret on, a few private specifics | Drawn fresh for each match from the file | `character.focus`, `character.drawer_line` |

The plan writes, for each character: what the profile shows, where they are tonight, their
**fear**, their **habit** (with its literal marker), the **lie** that justifies it, the
**origin** in three pieces (the hook, what happened, the part they'd never say), the
**accident** that spills piece 2, the **snap**, their **Feelings** (what sets off each feeling
in them, and how it changes the way they type), how their strongest **shadow** shows, their
**background** (generation, schooling, money, vices: the life that shapes their words), their **voice**
(what never changes in how they type) and its **break** (how it drops at the biggest moment). It also writes the
collision between the two, three setups, both endings, and where the heat starts. The plan is
the story. Everything that follows is measured against it.

Shadow growth during the chat is technical. A shadow matters to the writer only when it takes
over a message, and the turn says when that happens (group 4).

## 3. Relationship: where they stand as the turn starts

| Game value | Meaning | How it is told | Table |
|---|---|---|---|
| Interest, 0–25, and its level | How the match stands toward the player character right now | "Brick_haus's interest is 19 out of 25, Very Into It:" and one sentence about the match by name, saying what the level means between them (not what the story should do) | `turn.relationship.interest`, `interest[].means` |
| Each trap in force | How the player character acts while it holds, and how far into it they are | The acting, and its turn: the second ("it's the pattern now, and the other one can see it") or the third and last ("it's wearing off") | `turn.relationship.trap`, `trap_stage`, `traps.*.acting` |

The interest levels: Unmatched 0, Bored 1–4, Lukewarm 5–9, Interested 10–15, Very Into It 16–20,
Almost There 21–24, Date Secured 25.

Every trap lasts exactly three turns, and the turn it fires counts as the first (Pinder.Core
`TrapState.FixedDurationTurns = 3`). So a trap is never listed here on its first turn: on that
turn it is a consequence of the roll, told under "This turn".

Left out as technical: momentum, ghost risk (the d4 rolled only at Bored), the pending triple
bonus, combos, and a trap's clearing rule.

## 4. This turn: what the game did

The game has already played the turn: the player picked a move, and the dice landed. Four
checks shape the message. A fifth die, the d100 for reply timing, is not used.

| Game value | Meaning | How it is told | Table |
|---|---|---|---|
| The move picked | What kind of message it is, and whether it is this character's strength | "Reuben_404 plays Charm, a move they're middling at: the message is warm, easy and reassuring." | `turn.event.picked`, `turn.event.strength`, `moves.*.means` |
| The main roll | How well it comes off; on a miss, which shadow it goes wrong through | In words, never as a number: "it works well, a little better than meant"; "as badly as it could go, short of the worst. It goes wrong through Reuben_404's Dread: …" | `turn.event.roll_hit`, `roll_miss`, `results.*.name`, `shadows.*.means` |
| What the roll does to the message | The game's own rewriter instruction for this move and result | As the game wrote it, as orders to a rewriter. The direct prompt tells the director to read them as what happens to the message | `moves.*.tiers.*.message` |
| A trap the roll sets off | From this message on, how the player character acts, for this turn and the next two | "This turn sets off the Spiral trap…"; when it fires again just as it ran out, it is "back", and the match has seen it before | `turn.event.trap_new`, `trap_again`, `traps.*.acting` |
| The shadow check | When it misses, a shadow leaks into the message | The shadow, how bad, and its taint | `turn.event.shadow`, `shadows.*` |
| The horniness check | When it misses, a thirsty question is added, and the match takes it differently at high interest (16 or more) | The slip, how the match takes it, and the mood | `turn.event.thirst`, `thirst.*` |
| The steering check | When it succeeds, the message ends with a question that pulls toward meeting | One sentence | `turn.event.steering` |
| How the match takes it | The game's own reaction to this move and result, written to the match | "How Brick_haus takes it (written to them): …" | `turn.event.received`, `moves.*.tiers.*.received` |
| Interest change | How far the match moved, and in which direction | Before and after, the swing (big loss ≤ −3, loss, flat, gain, big gain ≥ 3) and what it does to the reply. Under a beat about the match, only the numbers, because the beat says how they reply | `turn.event.swing`, `swing_bare`, `swings` |
| Why it moved | The cause, when the cause shows in the message, so the match can react to it: the thirsty slip (whose overlay halves the change), a shadow slip, a trap in force | "Why: the thirsty slip; Brick_haus is reacting to the thirst as much as to the move." | `turn.event.why`, `turn.event.cause.*` |
| A new interest level | When the change crosses a level, what the new one means | As in group 3 | `turn.event.band` |
| Meeting | At 21 or more, the match will not agree yet. At the end, the outcome: date, unmatch or ghost | One sentence | `turn.event.not_yet`, `turn.event.endings` |
| The first turn | There is nothing from the match yet, which the game's rewriter texts assume there is | One sentence | `turn.event.first` |
| What this turn is for | The beat (group 5) | The beat's template | `turn.event.job`, `beats.*.story` |

Left out as technical: raw dice, the DC and the margin, the risk tier, the options not picked,
the outcome before the last turn.

## 5. Story position: where each of them is in the story

The story position is the plan, played forward: what is out, what is owed, and how each of
them feels about where it has got to.

| Value | Where it comes from | How it is told | Table |
|---|---|---|---|
| Where the chat is | The turn and the estimated turns left (the meter's pace over the chat, so it stays steady) | "The chat is in the middle": opener, early (turns 2–3), middle, late (three or fewer left), last | `turn.story.phase`, `turn.story.phases` |
| What has happened, what is still to come | The beats spent so far | "Already happened: turn 4: the breakthrough…; Still to come: the ending." | `clock.so_far` |
| Which origin pieces the reader knows | The director's TOLD notes | Per character: what is known, what is next, whether it is due this turn. Piece 3 is saved for the end; on the secret turn, piece 2 comes out now | `clock.step.*` |
| Whether the habits have collided | The COLLIDED note | Not yet, due now (the plan's Collision), or done. A won ending doesn't stage it | `clock.step.*` |
| The setups | PLANTED, CALLBACK and PAID notes | Each one: not planted, planted, back, paid off, and when the next step is due | `clock.step.setup_*` |
| Each one's feeling, last turn | The director's own Feeling note (see "The emotional state") | "Last turn you read Brick_haus as: hurt, strong, at Reuben_404, …" | `turn.story.feeling` |
| What the chat needs at this point | The point in the chat, whether the breakthrough and the secret have happened, and the interest. Not on a big beat or an ending, where the beat is the template | Composed from parts | `turn.story.needs`, `needs.*` |
| Hooks still hanging | The HOOK notes from the last two turns | Who dropped what | `clock.exchange` |
| The habit rule | The habit markers in the plan, and how often they have shown | Who may show their habit this turn and who may not | `habits.*` |
| This turn's job | The beat the story clock picks | Under "This turn" (group 4) | `beats` |

**How the beat is picked**, in priority order, from what has happened and what this turn's dice did:

- the ending on the last turn (won, lost, ghosted or open);
- the opener on turn 1;
- the match's reveal the turn after the secret;
- the player character's snap on a shadow miss;
- the catastrophe on a bad miss while the secret is still in, which spills it through the plan's accident;
- the breakthrough on a hit that is honest or perceptive, or a gain of 3 or more;
- the match's snap on a loss of 2 or more from 15 or higher;
- a forced breakthrough or secret when the chat runs late without one;
- a setup early on;
- a callback;
- otherwise getting to know each other, warming or close, by interest.

The story beats fire once each; the getting-to-know beats repeat. A beat's flags say whether
it is about the match, whether it is big, and whether one message may run long.

The full list of what each has told the other is not re-sent. The director's session already
holds every turn it wrote.

## The emotional state

Each character has an emotional state that drives how they answer. The director names it
explicitly before it writes, because a reply follows from what someone feels, not from the
dice. It works in four steps.

**1. What can set each feeling off, and how it shows: the plan.** For each character the plan writes their
Feelings: what sets off each feeling in them, from their fear and their habit. For example:
anger at thirst that treats her as a body; shame at his own failure on the screen. The plan also says how
each feeling changes the way that character types, and the two never change the same way: one rants
when angry, the other goes down to one-word questions. This is the register the director writes to,
seeded from the character file's "may … when …" texting traits; the file's "tends to" traits seed the
voice that never changes.

**2. Which way the match's feeling goes: the game.** The interest change sets the direction for
the match.

- When interest falls, the match's feeling is a negative one. When it rises, it is a positive one.
- A big swing makes it strong.
- When interest rises after something that would usually set off a bad feeling in them, the
  trigger didn't fire this time, and the surprise shows.

The player character's feeling isn't tied to the meter. It comes from the chat.

**3. What they feel, and why: the director, from the chat.** Before writing, in the Feeling
note, the director names for each character:

- one feeling from the list;
- mild or strong;
- at whom or what;
- what in the chat caused it.

The cause can build over several exchanges. A back-and-forth that wears someone's patience thin
is named as impatience before it turns into anger on the screen.

The feelings, with how each usually shows in a text. This is the default the director falls back on where
the plan says nothing for a character:

| Negative | How it usually shows | Positive | How it usually shows |
|---|---|---|---|
| anger | shorter and harder; questions turn into statements and the punctuation sharpens | joy | longer, brighter and quicker, their enthusiasm in their own way |
| fear | hedged and quick to take things back; an exit named before it's needed | relief | the guard drops a notch; a plainer line than usual |
| shame | over-explained, or suddenly quiet; a joke arrives to cover it | desire | warmer, closer and more specific about bodies |
| guilt | apologetic; making it up to them, offering more than was asked | | |
| hurt | short and flat; they stop giving and stop asking | | |
| contempt | cool, precise and a little superior; the dating-app wisdom comes out | | |

Cynicism counts as contempt. Pride and amusement count as forms of joy. A feeling always shows
in the character's own voice, never named in the message.

**4. The feeling drives the reply, and carries over.**

- The checklist's last question asks whether the match's answer shows the feeling named. The review asks
  again, of the draft, whether each one's feeling shows the way the plan says it shows in them.
- The next turn's story block shows the director its own reading: "Last turn you read
  Brick_haus as: …". That way the next feeling is read against the last, and the swings
  between turns are visible.
- The run record keeps every turn's feelings.

**What the code checks:**

- each character has a feeling from the list;
- the match's feeling agrees with the direction of the interest change (no joy on a loss);
- a turn that fails either check is sent back once.

Strength and cause are not checked.

## Length

Nothing about length is checked. Length is guidance, in three parts:

- **One length line for each character**, from one source:
  - the player character: the beat's line when the beat is about them (catastrophe, snap,
    breakthrough, a lost or ghosted ending), otherwise the move and roll's line;
  - the match: the beat's line when the beat is about the match (opener, reveal, snap, discovery,
    won ending), otherwise the swing's. The swing's lines say how much of themselves the match gives,
    not what shape that takes; the shape comes from the match's Feelings in the plan.
- **Where the chat stands:** the words so far against the rough size of a whole chat (about 1,250
  words). Running over is fine. It is not an average to hit each turn: the director writes what the
  turn needs to sound like the character and land what it is for.
- **How each has written lately:** the message count and words of each character's last three
  turns, counted by the code and told as information, so a shape that repeats is visible.

The code checks only who writes: each of them sends something, except a match who ghosts, who sends
nothing.

Two run measures show whether this gives variety without losing the voices. The run record keeps
each character's spread of message lengths and their messages per turn. A blind attribution test
gives a reader every message, shuffled and without names, to sort into two senders: 0.5 is chance,
and a score near 1 means the voices are distinct.

## How the director writes a turn

Each turn's answer has five parts, in order: the notes (where they are, what each knows, what is
hanging, what worked, what is stale, what in the plan is still unused, last turn's weakest point,
each one's Feeling, what the player character meant); the checklist, answered before writing; a
draft; the review, which edits the draft; and the turn as sent, which is the draft with every
replacement the review named and nothing else changed.

The review is a list of instructions, not questions. Asked questions about its own draft, the
director described the draft and passed it (in v16 the turn as sent differed from the draft in 4
of 11 turns). For each instruction it now writes "holds", or quotes the words that break it and
what replaces them. The instructions are adapted from the dialogue rubric and the "dialogue as
combat" rules of the novel engine (swarm-temporal, `projects/novel-engine`):

- every line could only have come from its sender, and the two don't sound alike in the turn;
- each feeling shows the way the plan says it shows in that character, in the shape of the message;
- every line does something to the other one, and the upper hand moves;
- subtext: no named feelings, explanations or therapy talk;
- nothing repeats a topic, theme, joke, prop, phrase, pet word or opener the chat has had, unless it
  is a callback the plan set up that means something new; a repeat is replaced with something from
  the plan nobody has spent yet, from the notes' Unused line;
- no joke is the first one anyone would think of;
- the turn's shape differs from the last turns, and each message is as long as the moment needs;
- each message carries one thing and ends on its strongest word.

The game counts what has repeated and tells the director every turn: each character's message
openers used three times or more, and the words (not common ones, not names) that came up in three
turns or more. The plan's Voice treats pet words and openers as seasoning that turns up now and
then, so a voice trait does not harden into a tic.

The code checks that the draft and the review are there, not what they say. The run record keeps
what the review did: per turn, how many words differ between the draft and the turn as sent, and
how many replacements the review named.

## Consistency rules

These came from reading every turn message exactly as sent (v14). Each rule removes a way for
two lines of one message to disagree.

- One length line per character, from one source (above).
- The relationship block is the state at the start of the turn. A trap the turn sets off is told
  under this turn and is never given as the cause of the match's reaction that turn.
- Interest levels say what the level means about the match. The story block and the beat carry
  what the story should do.
- Under a beat about the match, the swing gives only the numbers.
- The story clock agrees with the beat. On the secret turn it says piece 2 comes out now. The
  "how they live with it" guidance runs for three turns after the secret. A won ending doesn't
  demand the collision.
- The first turn says there is nothing from the match yet.

## Not in this model yet

- Reply timing from the d100.
- A check that the strength of a feeling fits the size of the swing.
