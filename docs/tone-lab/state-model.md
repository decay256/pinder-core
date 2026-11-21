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
| Tracker, system prompt | Once; the session keeps it | What to report about the chat: what each offered, what is still unanswered, what came out, what each wants and hides, the upper hand, whether the chat moves, repeats, what a reader couldn't know, what the reader feels |
| Tracker, turn message | Before every turn but the first, and once after the last, in one session | The whole chat so far, nothing else |
| Director, system prompt | Once; the session keeps it | Who knows what; the feelings and how each usually shows in a text; its notes; the headings of its brief |
| Director, turn message | Every turn, in one session that keeps the plan and every earlier message and brief | The whole chat, the tracker's report, relationship, story position, what has repeated, this turn, what is required, length |
| Director, retry | When a brief broke a checked rule | The broken rules, in words |
| Writer, system prompt | Once; the session keeps it | Each character's sheet, cut from the plan without the origins, the accident, the setups, the collision or the endings; the craft; the review it edits its draft against |
| Writer, turn message | Every turn, in one session that keeps every earlier brief and turn | The whole chat, the director's brief, the habit rule, what has repeated |
| Writer, retry | When a turn broke a checked rule | The broken rules, in words |
| Reader and grader | Once, after the chat | The finished chat only; the grader also gets the plan |
| Attribution test | Once, after the chat | Every message, shuffled, with no names |
| Release check | Once, after the chat | The finished chat and what the director released each turn |

Nobody after the planner sees the character files or the mechanics. The director sees the plan,
the turn messages and its own earlier answers; the writer sees the character sheets, the chat and
the briefs; the tracker sees only the chat.

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

## Three roles: tracker, director, writer (v18)

Up to v17 one director session read the game, kept notes, drafted, reviewed and wrote every turn.
The chat was under a tenth of its context, scattered between its notes, drafts and reviews, and it
behaved like it: offers went unanswered (in v17's seed-23 chat Brick's career pause got no reaction
at all), the chat circled one subject, and the opener quoted a detail from the plan that the reader
never saw. Each turn now runs three sessions, in this order.

**The tracker** knows only the chat. Before every turn but the first, and once after the last, it
gets the whole chat and reports what is on the screen: what each did to the other in the last turn,
what each offered, everything still unanswered, what each has let out about themselves, what each
seems to want and to hide, the upper hand and the temperature, whether the chat moved, what
repeats, anything a reader couldn't know or follow, and what the reader feels. Not knowing the plan
is the point: it reads the way the reader will.

**The director** knows the plan and the game. It gets the whole chat, the tracker's report and the
game state for the turn (sections 3 to 5 above), and writes notes (the chat now, the plan, each
one's Feeling, what the player character meant), a brief for the writer, and the notes for the game.
The brief is intent, never lines, and in plain words, never the game's terms: what this turn answers
first, what each feels, what the player character is trying and how it comes out, how the match
takes it, what comes out of the plan this turn (with every fact the writer needs), what stays away
(named only as a subject), how much each gives, what the reader should get, and what to avoid. The
rewriter's orders for a roll ("scan the last messages") are for the director, who turns them into
what happens between these two people from what is on the screen.

The director may change the plan (move a piece, swap two, take the other ending, rewrite a setup)
and says so in its Plan note. It may not leave the plan unused: by the end every origin piece, every
setup and the collision is spent on the screen or retired with a RETIRED note that says why. On the
last turn the game lists what is still open, and a direction that leaves any of it open is sent
back.

**The writer** knows the characters: a sheet for each, cut from the plan by the code (what they
show, where they are tonight, fear, habit, lie, snap, feelings, shadow, background, voice, break),
without the origins, the accident, the setups, the collision or the endings. It gets the whole chat
and the brief, drafts the turn, edits the draft against ten instructions (each line could only be
its sender's; each feeling shows the way that character's Feelings say; each one first answers what
the other last sent; every line acts on the other; subtext; no repeats; no first-thought joke; the
shape changes and each gives what the brief says; one thing per message, ending on its strongest
word; nothing leans on what the reader hasn't seen), and writes the turn as sent. It never sees the
dice, the move, the meter or the notes for the game.

Every role's message carries the whole chat, and each session keeps only its own messages, so no
role reads another's working. The checks: the director's notes, brief headings, required notes,
feelings and the end-of-chat accounting; no game terms in the brief; for the writer, who writes, the
habit rule, the chat rules and stray lines. Each role is sent back once when it breaks one.

The run record measures what the roles are for: where each piece of the plan ended (spent, retired
with the reason, or neither); how many offers the tracker saw unanswered before each turn and at the
end; a release check, in which a reader who sees only the chat says whether each turn's release
reached the screen; and, as before, what the writer's review changed between draft and sent.

## The director owns the arc (v19)

Up to v18 the game's story clock chose the beat of every turn from the dice and the interest, and
the director carried it out: "What this turn is for" and "What the chat needs" came with every
turn, and the clock required origin pieces, setups and the collision by turn number. Two bad rolls
in a row gave two catastrophes in a row, every origin piece was out by turn 8 of 11, and nothing in
the loop could say "too soon". The director's own rules added to it: a turn that walks past what
the other one said was the worst turn there is, and nothing said a dodge was an answer, so every
direct question was answered by telling. And with its reasoning at medium it spent no reasoning
tokens after turn 2.

In v19:

- **The plan is a story.** It adds tonight's events, three or four things that happen to one of
  them while they type, each taken from the character files (a person from the backstory, the job,
  the money, the place, what they're on), and a Story of seven to nine beats from the opener to the
  ending, without turn numbers, that places every origin piece, both accidents, both snaps, the
  setups, the collision and the events, with room between the big ones. Then a midpoint, a short
  version for a six-turn chat, and the two endings as what happens, never the words.
- **The director owns the arc.** On the first turn it builds its arc from the plan's Story; every
  turn it writes it under ARC (the beat it plays now, the beats still to come, what is held and
  until when), and the game hands it back at the top of the next turn. Its Story note says which
  beat the turn plays and why now, not earlier.
- **The game reports and no longer directs.** It tells the director what the dice did, how the match
  takes it and how the interest moved. It no longer says what a turn is for, nothing is called due,
  and nothing is required before the last turn, where it says how the chat ends and lists what of
  the plan is still unspent. The clock still runs; its choice is recorded next to the beat the
  director played, in the run record and the report.
- **Pacing, the dice and answering.** One beat a turn at most, no two big beats back to back, the
  part each would never say on the last beats. A bad roll is an embarrassment the size of the moment
  unless the arc is at the accident; a shadow or a trap shows in one message and one line of it, not
  the whole turn. A question always gets a reaction, but a dodge the reader can see is a good
  answer, and what the arc holds stays held until its beat.
- **Snaps are the director's call.** A director who has a character snap says so (SNAP), and that
  character's habit may show in every message of the turn.
- **The writer and the tracker see none of the story.** The writer's sheets stop at the collision, as
  before; the events reach the chat only through the brief's Released.
- **The director reasons at high effort.**

## The game holds the pace (v20)

v19 gave the director the arc and told it, in prose, how to pace it: one beat a turn at most, a
beat can take two turns, never two big beats back to back, the part each would never say on the
last beats. On seed 23 it played one beat every turn regardless, merged two on a good roll, and
ran the big beats from turn 3 to turn 7 without a breath: both accidents on turns 3 and 4, the
collision on 5, both deepest confessions on 6 and 7. A rule in the prompt lost to the concrete
signal in front of it every turn. In v20 the pace is code:

- **Worked out before the director writes.** Each turn the game takes the chat's estimated length
  from the meter (the average of the turn-by-turn estimates so far: on seed 23 those jump between
  eight and fourteen, which would open and shut the gates) and the arc the director left last turn,
  and tells the director, under The pace:
  how long the chat looks like running, the furthest beat this turn may play, whether anything big
  may come out, and from which turn the part each would never say may.
- **The beats are numbered and played in order.** A turn stays on its beat or moves to the next;
  and the furthest beat follows the chat's length: beat k of an arc of B beats not before k-1 of
  B-1 of the way through the estimated chat. A beat added to the arc counts from the next turn.
- **Big is read from the notes for the game,** not from the director's say-so: piece 2 or 3 of an
  origin (piece 2 is what the accident spends), the collision, a payoff, a snap. Nothing big in the
  first two turns; two at most in one turn; never on the turn after one. Piece 3 not before seven
  tenths of the way through the estimated chat.
- **Checked and sent back.** A director answer that breaks the pace goes back with what it broke,
  twice at most; what is still broken after that is recorded in the run, per turn, and shown in the
  report's arc table beside the estimated length, the furthest beat allowed and what came out.
- **The last turn is free.** The chat ends there; whatever is left comes out or is retired.

## Finding out, and the two reasons (v21)

Up to v20 a character was a fear, a habit and a backstory released piece by piece. The chat had
the facts in it, but they answered nothing: nobody was trying to find anything out, so the banter
had nothing underneath it, and the reader never got to know either of them. A real dating chat is
two people evaluating each other. For a night: is this one safe, and will it be fun. For a date:
do we vibe, what kind of person is this. Nobody asks that straight out; they find out sideways,
from how the other talks about the ex or the boss, what they joke about, what they skip.

In v21:

- **Each character wants something tonight** (a night, a date, or something worse: to be wanted,
  to get even), and what they say they want, if it differs; **needs to know** two or three things
  before saying yes; has **tests**, the sideways ways they find out; and **gives away** three or
  four things from their life without meaning to, each answering one of the other's questions.
  The writer's sheet keeps what each wants and needs to know; the tests and the give-aways are the
  director's, like the origin.
- **Two reasons.** The plan gives the good reason these two could be good for each other, and the
  bad one: the collusion (Jürg Willi's *Kollusion*), where each one's damage fits the other's and
  the yes is real while the reason is rotten. If the match agrees to meet, the last turn plays the
  ending for the reason the chat has earned.
- **The story turns on the guard coming down.** First half, each tests the other through the act;
  the midpoint is the guard dropping, in how they type before in what they tell; the second half
  is what they do with it.
- **Every turn is for finding out.** The director's notes say what each is trying to find out and
  what they learn; the brief tells the writer; the tracker reports what each could fairly know
  about the other so far.
- **The reader is asked their questions:** would you go home with each one, safe and fun; would
  you date them, what kind of person are they; why do these two want each other, for a good reason
  or a bad one. The score counts every answer but the last two, so it is no longer out of 9.

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

## The characters have lives (v22)

Up to v21 a live card was the character file as it stands: the bio, seventeen body readings, a merged list of texting habits, and a personality and backstory made of humiliations (brag-and-truth pairs, drawer items, fragments from items and body bands). Nothing in it was a life: no people, no places, no job, no history, nothing loved or wanted. A chat is two strangers finding out about each other sideways, and a card like that leaves the other one nothing to find out, so the planner filled the chat with the body.

The bundled files on this branch also share whole passages. The reconsolidation of 2026-09-07 spread one character's fragments into all six files (the cropped family portrait, the drafty house, the replica dating app, the brochure smile); before it, and on main, they were each their own. That is a data accident on this branch, not something the game's generator does, but the generator's own limit is real: its backstory prompt forbids inventing names, places, dates, jobs and relationships, so it can only rearrange the fragments.

v22 writes each character a profile, once, with `tools/tonelab/profiles.py` and the prompt in `tools/tonelab/profiles/generator.yaml`. The profile keeps the bio word for word, what they own, the body, the drawer and the clinician's notes, and invents the rest in a fixed shape: what the match sees (the body in two or three plain lines), their life now, four to six named people, six to eight moments from childhood to this year, what they love and are good at, how they are with people, what they want (from tonight, from someone, the dealbreaker, what they test for first), four to six brags with the truth behind each, and how they text in ten situations: baseline, into them, losing them, waiting for a reply, hurt or embarrassed, horny, late at night, on something, lying, and guard down. Passages a character's file shares with another character's are listed as shared and kept out, unless they come from that character's own fragments; each profile is told the names and lives the earlier ones already use. Everyone lives in London (the generator's `city`), each in a neighbourhood of their own, so a chat that ends in "come over" can end in a meeting. The tool checks every answer (the headings, the bio, every situation, the counts, the length, nothing copied from a shared passage) and sends the problems back, twice at most.

With `"profiles": true` in variant.json, the card is the profile (without its brags), the stats, the shadows, one brag from the profile as tonight's focus, the drawer sample and the clinician's notes. The writer, who never sees the cards, is also given each character's texting by situation; the director's Feeling note and what just happened say which one applies. The game's character files and its generator are unchanged: the profiles are the tone lab's, and they go back into the game only once the chats show they work. `preview.py --old-cards` builds the cards the old way, for a comparison on the same dice.

## The work is scored (v23)

Until v22 the lab judged its chats by reading them, plus a reader check and an attribution check
at the end of a live run. v23 adds a scorecard: candidate metrics for the three kinds of work the
lab produces, the character profile, the plan and the chat, in `tools/tonelab/scorecard.yaml`
and in prose in `docs/tone-lab/scorecard.md`. Every dimension scores 1 to 6 with no midpoint,
so a score has to lean: each dimension writes its own bar as the line between 3 and 4.

`tools/tonelab/score.py` has a language model score the work against it, a different model
family from the writer (the subscription Claude CLI by default). Every score comes with the
reason, the quotes it rests on (checked against the material, so an invented quote shows), and
the change that would raise it. Where the judge would be the wrong reader because it already
knows the answer, a probe call sees only what a reader would see (the chat alone, the chat with
the names hidden, the chat without its last turn, the profile alone) and the judge compares that
reader's answer with the truth. Scores are kept until the dimension, the material or the judge
changes. `docs/tone-lab/scores/index.html` is the explorer: a grid per kind of work, the plans'
trend across versions, and every score's reasoning.

### The focus, and the dashboard that stays open

Not every dimension matters equally for the next step. `tools/tonelab/focus.yaml` names the
current experiment (two characters written from Pinder's brief alone, a profile each and one
plan for the pair, chats off until both pass), puts each profile and plan dimension in a tier
with its reason (critical drives the generator and the gate, watch is scored and shown, off is
not scored here), and sets the gate: every critical dimension at 4 or more. Quote `"off"`: bare,
YAML reads it as false.

`score.py page` writes two pages: `docs/tone-lab/scores/index.html`, the full explorer with every
score's reasoning and every text (about a megabyte, for a browser), and `focus.html`, the
experiment's view, with reasoning and text only for its own work and its references and numbers
for the rest (small enough for the app's file viewer). In both, each column has a read button
that opens the profile or plan beside the grid, with the quotes behind a selected score marked,
and a prompt button that shows exactly what was sent to make it: for a v22 profile the
generator's system prompt, the request and every retry with the problems the check found, as
`tools/tonelab/profiles/generation.json` recorded them; for a plan the planner's system prompt
(checked against the hash the run recorded) and the brief.

The focus view is also kept as a private claude.ai page that stays open. `score.py hosted --out
FILE` writes it without the document shell the host adds; after each scoring run it is rebuilt
and republished to the same address, and an open copy picks up the new version.
