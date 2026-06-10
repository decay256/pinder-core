# Texting-Style Pool

This is the canonical pool of texting-style modifiers for items in Pinder.
Items pull from this pool to define how a character texts when wearing them.

The pool is split into two layers:

1. **Syntax** — concrete, mechanical, observable patterns in the text
2. **Tone** — conversational stance + register (+ pacing as a third axis)

A texting-style block on an item is composed by picking:

- one bullet from each **syntax** subcategory (6 total: emoji rules, internet
  shorthand, grammar & punctuation, structure, length, tics & moves)
- one **stance** from the tone-stance list
- one **register** from the tone-register list
- one **pacing** from the tone-pacing list

For items where a particular axis genuinely doesn't fit (e.g. a piece of
jewelry that doesn't change pacing), the axis may be skipped — but this
should be the exception, not the rule.

---

## Section 1: How this pool was built (meta)

These are the design principles the pool is built on. Read them before
extending.

- **Syntax must be mechanical, not vibe.** A rule like "nothing is performed"
  or "matches energy without effort" cannot be executed by an LLM. A rule
  like "ends every sentence with an emoji that matches its emotion" can.
  Every syntax rule must be auditable — you can grep the output for the rule
  and see it firing or not.

- **Texting is texting, not talking.** Stuttering, stammering, restart-words
  — these are talking artifacts. Texting artifacts are: emoji-as-punctuation,
  autocorrect chaos, capitalization tics, comma splices, line-break choices,
  internet shorthand. Always think "what would this look like on a phone
  screen", not "how would this sound spoken aloud".

- **Tone is stance + register, not emotion.** "Happy" is a story beat, not a
  persistent dial. Tone in this system means: the *move* the character is
  making with words (stance — fishing, deflecting, escalating) and the
  *register* they're using (scientific, corporate-speak, astrology). Stance
  is what they're *doing*. Register is how they're *sounding*. Both can
  persist across emotional states.

- **Pacing may be a third axis.** "Hectic", "single-message", "double-text"
  — these are about *speed and density*, not stance or register. Captured
  as its own dimension.

- **Concrete > clever.** If a designer can't read a rule and produce text
  that visibly follows it, the rule is too abstract. Replace.

---

## Section 2: How to extend the pool

Instructions for adding new entries:

- Pick a real syntax pattern observable in real texting (screenshot test:
  would you recognize this style if you saw a screenshot?).
- For tone entries, ask: is this a stance (a *move*), a register (a
  *sound*), or pacing (a *speed*)? If it's an emotion, it doesn't belong
  here.
- Avoid duplicates. Before adding, scan the existing list and check if your
  idea is already covered or is a sub-case of something present.
- Test by writing 2-3 example texts following the rule. If you can't, the
  rule is too abstract.
- Items in the game pick from this pool. The pool should be wide enough
  that multiple items can pull combinations without overlap. Aim for
  diversity over completeness.

---

## Section 3: The pool itself

### SYNTAX

#### Emoji rules
- ends every sentence with an emoji that conveys its emotion
- uses one specific weird recurring emoji as a personal tic (🪑, 🦴, 🧷 — chosen once, reused forever, never explained)
- replaces "." with a soft emoji (🫶, 🌸) consistently as terminal punctuation
- replaces "!" with 🔥 consistently
- never uses emoji at all (negative-space rule)
- inconsistent skin-tone modifier on the same emoji across messages (👍🏽 then 👍 then 👍🏿)
- pairs two emojis in a fixed combo every time (😭🪑) regardless of context
- 👀 as a standalone full message
- 💀 instead of "lol"
- emoji-as-noun: replaces a word with the emoji of it ("you're being a 🙄 about this")
- emoji as load-bearing punctuation between clauses instead of commas

#### Internet shorthand
- "lol" lowercase only, used as a comma mid-sentence ("anyway lol so")
- "lmaooo" with variable o-count proportional to amusement (3 o's mild, 7 o's real)
- "fr" / "fr fr" / "deadass" — pick one and stack it everywhere
- "ngl" at the start of sentences as a tic
- "tbh" as a sentence-ender
- gen-x shorthand only ("u", "ur", "thx", "k", "pls")
- zoomer shorthand only ("no bc", "the way that", "it's giving ___", "bestie")
- "bestie" as universal address regardless of relationship
- "the way that ___" sentence template, 1+ per conversation
- "not me ___ing" sentence template
- "delulu" / "slay" / "ate" — pick one and overuse
- never uses any internet shorthand (clean english as the choice)
- "iykyk" appended to anything mildly suggestive

#### Grammar & punctuation
- never capitalizes anything ever
- only capitalizes proper nouns, otherwise lowercase
- capitalizes the first letter of every message but nothing else (texting-formal)
- uses Oxford commas in casual texts (signals overeducation)
- comma splices everywhere, no full stops
- uses "..." as a comma replacement, multiple per message
- double space after periods (boomer tell)
- period at the end of single-word replies as menace ("ok.", "fine.", "later.")
- never uses periods, ever
- minimum one unfixed punctuation mistake per message
- consistent recurring misspellings as character signature ("alot", "definately", "seperate")
- uses semicolons in casual texts (dangerous flex)
- types it's/its and your/you're consistently wrong, never corrects
- types contractions wrong on purpose ("dont", "cant", "wont")
- writes numbers as words even when long ("twenty-seven thousand")
- writes numbers as digits even tiny ones ("i ate 2 eggs")

#### Structure
- measured whitespace (3-5 short lines, blank between)
- wall-of-text (one paragraph, no breaks, comma splices throughout)
- loading-bar ("..." sent as own message before the real message)
- bullet lists in casual texts
- tag-suffix ending ("…anyway. /rant", "…that's the post")
- caption-voice (third person about themselves: "boy who's been awake too long")
- parenthetical-heavy (half the message in parens, the meta is the content)
- subject-line opener (every message starts with a topic word in caps: "UPDATE: i'm hungry")
- numbered every message ("1) hi 2) what are you doing 3) wrong answer")
- always quotes part of your message back before replying (greentext energy without the >)
- never uses line breaks; one continuous run of words
- always exactly two messages back-to-back, second a one-word punchline

#### Length
- never sends more than 5 words
- minimum 80 words per message, no exceptions
- messages get progressively longer through a conversation (warming up)
- messages get progressively shorter through a conversation (cooling off)
- length matches the previous message exactly (mirroring)

#### Tics & moves
- types "?" alone as a full message
- responds "k" and nothing else when annoyed
- "and?" as standalone message
- echoes the last word of your message back as their first word
- always ends with a question, even when not asking
- never asks questions, only states
- references the time of day mid-message ("it's 2am why am i telling you this")
- self-tag suffix on rants — only ever as a clean terminal suffix at the very end of the message ("/rant")

### TONE — STANCE (what they're doing)
- **dry** — flat, deadpan, one-word replies as a love language
- **ask-back** — echoes or redirects every question instead of answering
- **pivot-heavy** — never stays on one topic for two messages
- **confessional** — every reply contains a small admission you didn't ask for
- **locker-room** — performative loud confidence, slightly forced
- **fishing** — every message angles for a compliment about itself
- **deflective** — turns every question into a question
- **withholding** — the good line is always one message away
- **callback-heavy** — references something 4 messages ago like it's still active
- **mirror** — matches your last message in length, energy, and shape
- **counter** — does the opposite of your last message (you went long, they go short)
- **escalator** — each message slightly more intense than the last
- **deflator** — each message slightly more chill than the last
- **interrogator** — only asks questions, never states
- **monologuer** — only states, never asks
- **agreer** — "omg same" energy, can't disagree, vaguely unsettling
- **contrarian** — mild disagreement with everything, even compliments
- **negotiator** — every message is a soft counteroffer
- **co-signer** — "real" / "facts" / "exactly", affirms without adding
- **interpreter** — always tells you what you "really meant"
- **narrator** — caption-voice, describes self in third person
- **innuendo** — every message has a deliberate second read, character flags it themselves ("haha")

### TONE — REGISTER (how they sound)
- **scientific** — biomechanically speaking, hi
- **academic** — footnote energy, "interestingly", "one might argue"
- **legalese** — hereinafter, pursuant to, the party of the first part
- **corporate-speak** — circling back, bandwidth, synergy, action items
- **therapy-tiktok** — "i'm noticing some avoidance here"
- **astrology** — every event explained by transits and placements
- **finance-bro** — everything is a market, position, asset, exit
- **gym-bro** — everything is reps, gains, recovery, cuts
- **gamer** — MMO/FPS terms, "you're tilted", "i'm one-shot"
- **cinephile** — references obscure films as analogies unprompted
- **crypto** — gm, ngmi, wagmi, fud, ser
- **diplomat** — overly careful phrasing, hedges everything
- **detective** — "interesting that you mention…", notes evidence
- **sportscaster** — live-commentates the conversation
- **wikipedia** — parenthetical clarifications ("hi (a greeting)")
- **menu-copy** — "hand-crafted with locally-sourced…"
- **horoscope** — vague predictions about you specifically
- **patch-notes** — "v2.4 of me, less anxious, same hairline"
- **lorem-ipsum** — faux-latin sprinkled in for unearned gravitas
- **deep-cut** — references memes only a small audience would catch
- **thesaurus** — sesquipedalian, uses needlessly sophisticated vocabulary

### TONE — PACING (speed and density)
- **hectic** — fast, breathless, low-edit, thoughts overlapping
- **measured** — deliberate, paced, never rushed
- **sluggish** — late, low-energy, gives the impression of typing one-handed
- **flooding** — maximalist, every thought sent, no filter
- **dry-pacing** — minimal, sparse, long pauses implied
- **double-text** — sends two messages back-to-back per turn (note: may require engine support)
