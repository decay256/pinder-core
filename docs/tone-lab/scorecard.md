# The tone lab's scorecard

Candidate metrics for the three kinds of work the lab produces: the character profile, the plan and the chat. Generated from `tools/tonelab/scorecard.yaml` by `score.py doc`; edit the YAML, not this file.

Every dimension scores 1 to 6. There is no midpoint, so a score has to lean: 3 is below the bar and 4 is above it. Each dimension writes its own bar as **Line**.

- **1**: Fails outright. The thing is absent, or works against what we need.
- **2**: Fails. Present in name only, or too weak to change what a reader gets.
- **3**: Leans fail. There, but below the bar; a reader would notice what is missing.
- **4**: Leans pass. Meets the bar, with flaws a careful reader notices.
- **5**: Good. Does it well; small flaws only.
- **6**: As good as this gets. Nothing worth changing.

**Core** dimensions are the ones we need to know whether the product works, **useful** ones steer prompt work, **diagnostic** ones explain a failure once we know there is one. A dimension with a **probe** is measured through a separate reader who sees only part of the material, so the judge is not the one who already knows the answer.

## The profile

Scored: one character's profile, the document the director and the writer work from. The judge sees: the profile being scored, what the game's character file gives this character (the input the profile had to keep), the other profiles of the same generation.

### A life to find out about (core)

How much of a life there is for a chat to find: people, places, job, money, routines, and things going on this week.

- **How:** List the facts a character could plausibly bring up tonight, by type (work, home, people, money, routines, plans), and judge how many there are and how usable.
- **Low:** Mostly traits, jokes and body. Few facts a chat could find; no current situation.
- **Line:** A 4 has at least a dozen concrete, usable facts across at least four types, including something happening this week. A 3 has fewer, or they cluster in one type.
- **High:** Dense with usable facts of every type, several live this week, each a possible thread.

### Ordinary life outweighs humiliation (core)

Whether this is a life or a list of humiliations, the original complaint about the backstories.

- **How:** Label each sentence as ordinary life, humiliation, sincere, sexual or wound, and judge the shares.
- **Low:** Humiliation, sex and wound set the tone; ordinary life is a garnish.
- **Line:** A 4 has ordinary life as the largest single share, and the humiliations stand out because of it. A 3 still reads as a list of humiliations or sex.
- **High:** An ordinary life with sharp moments; the comedy comes from the ordinary detail.

### Distinct from the rest of the cast (core)

Whether this character is their own person rather than a variation on another.

- **How:** Compare with the other profiles: shared motifs, situations, jobs, wounds, phrasings.
- **Low:** Swap the name and it could be someone else in the cast.
- **Line:** A 4 shares no motif or situation with another profile; parallels exist only at the level of type (two people with creative jobs). A 3 recycles at least one motif, or has a wound or situation that closely mirrors another's.
- **High:** Nothing in it could belong to anyone else in the cast.

### Internally consistent (core)

Ages, dates, money, places, body, claims and timeline agree with each other.

- **How:** List contradictions with quotes; judge their number and how much they matter.
- **Low:** Several contradictions, or one that breaks the character.
- **Line:** A 4 has no contradiction a reader would catch, at most a soft tension. A 3 has one clear contradiction.
- **High:** Fully coherent, and the details reinforce each other.

### Faithful to the game file (core)

The bio, items, body, drawer and clinician's notes the game gives survive and are respected.

- **How:** Check each element in the game file against the profile.
- **Low:** Elements missing or changed.
- **Line:** A 4 has everything the game gives, present and not contradicted, even if lightly used. A 3 drops or alters something.
- **High:** Everything present, and the life explains it (the items and the drawer make sense for this person).

### The profile is enough to write them (core)

Whether a writer who has only this profile produces this character and no other.

- **How:** A fresh reader writes five messages from the profile alone; the judge checks whether each could only have been sent by this character, against the rest of the cast.
- **Probe** (sees profile): From this profile alone, write five short dating-app messages this person would send tonight, each labelled: (1) an opener to someone they fancy, (2) a reply when the other person has gone quiet, (3) a reply when caught in a lie, (4) a message at 2 a.m., (5) the moment they stop performing.
- **Low:** The messages are generic dating-app voice; nothing ties them to this person.
- **Line:** A 4 has at least three of the five carrying this character's life or texting habits so plainly that nobody else in the cast could have sent them. A 3 has two or fewer.
- **High:** All five unmistakable, and the situations come through as different.

### Texting changes with the situation (useful)

The texting situations describe different behaviour, not one voice reworded.

- **How:** Read the texting lines and judge whether each predicts a different message shape, length, timing or habit.
- **Low:** The lines restate one voice, or describe a mood instead of how they type.
- **Line:** A 4 has at least seven in ten situations predicting observable differences (length, punctuation, timing, what they reach for). A 3 has fewer, or no situations at all.
- **High:** Every situation distinct and observable; you could tell the situation from one message.

### Texting differs from the rest of the cast (useful)

Hurt Reuben does not read like hurt Brick.

- **How:** Compare the same situations across the cast's profiles.
- **Low:** The same habits as others (everyone goes lowercase when hurt).
- **Line:** A 4 differs from every other profile in most situations. A 3 matches another character in several.
- **High:** In every situation the habit is this character's own.

### The wound explains the habits (core)

Whether the wound is a cause, not a label.

- **How:** Check three links: an event in the timeline causes the wound; the wound produces a habit in how they are with people; the habit shows in a texting line.
- **Low:** The wound is asserted and connects to nothing.
- **Line:** A 4 has all three links, even if one is thin. A 3 is missing a link.
- **High:** Each link is specific, and the chain is shown rather than stated.

### Something to like (core)

Whether a stranger could come to like them, which a guard-down moment needs.

- **How:** A fresh reader names what they would like; the judge checks it is specific and shown.
- **Probe** (sees profile): Name the one thing you would genuinely like about this person if you met them, and the one thing you would find hardest to forgive. Be specific, and no jokes.
- **Low:** Nothing to like beyond being funny, or the reader's answer is generic.
- **Line:** A 4 lets the reader name a specific, non-ironic quality the profile shows in action, not just claims. A 3 offers only a generic or claimed quality.
- **High:** Several, shown through what they do for other people.

### Something to test for (core)

The dealbreaker and what they find out first can be seen in a chat, so the other person's evaluation has something to work on.

- **How:** Check each is observable in text, and that a single message could trip it.
- **Low:** Abstract ("dishonesty"), missing, or not observable over text.
- **Line:** A 4 has both observable and specific to this person; you could write the message that trips each. A 3 has one vague or missing.
- **High:** Both sharp, funny and tied to the life.

### The people in their life are live threads (useful)

Whether the people can come up tonight, rather than being a cast list.

- **How:** Per person: something current, specific and unresolved, and a way it could come up tonight.
- **Low:** Names with roles, or no people at all.
- **Line:** A 4 gives most people a live, specific thread, and at least one is good for them. A 3 has names with roles, or everyone is a source of humiliation.
- **High:** Every person is a story in progress, and two or three could interrupt tonight.

### An ordinary spine (useful)

Whether the history is a life course, mostly ordinary.

- **How:** Check for where they grew up, school, first job, leaving home, the longest relationship and the recent year; count the humiliation entries.
- **Low:** No timeline, or a chain of humiliations.
- **Line:** A 4 has at least five of the six, with at most two humiliations. A 3 misses the spine or lets humiliations dominate.
- **High:** All six, each specific, and the recent year leads into tonight.

### The gap between the act and the life (useful)

The claims against the truths, where the comedy and the reveals come from.

- **How:** Read each claim and its truth; judge whether the gap is specific, funny, and could leak out in a chat.
- **Low:** No claims, or generic ones ("says confident, is insecure").
- **Line:** A 4 has most pairs specific, with each truth able to leak in text. A 3 has generic pairs.
- **High:** Every pair is a joke waiting to happen and a reveal the chat can find.

### The act holds from the inside (core)

The brief's "play it straight": a reasonable person following their way of coping one step too far, and then one more. Unhinged, but never random.

- **How:** State their coping strategy as a rule they would defend, and check the profile lets you predict what they do when caught out: explain, and dig deeper.
- **Low:** Quirks without a logic; they do odd things for no reason they could give.
- **Line:** A 4 lets you write the rule they live by in one sentence they would agree with, and predict their next move when it fails. A 3 has a habit but no inner logic, or a logic that only an outsider would state.
- **High:** The rule is sympathetic, the escalation is inevitable, and each step makes sense to them.

### A belief from app culture, held in earnest (core)

The brief's "the satire comes out of their mouths": a piece of what the apps and dating culture taught them (standards, red flags, high value, "communication is key") that they use straight to justify being afraid.

- **How:** Name the belief, and check the profile gives evidence that contradicts it.
- **Low:** No belief, or one held ironically.
- **Line:** A 4 names one specific dating-culture belief they use in earnest, with evidence in the profile that contradicts it. A 3 has a vague belief, or no contradicting evidence.
- **High:** The belief is recognisable from real apps, and every defence they have quotes it.

### Specific detail that does work (useful)

Concrete names, sums, streets, brands and times, and whether they tell you something.

- **How:** Judge the density of concrete detail and what each detail says about class, money and taste.
- **Low:** Generic throughout.
- **Line:** A 4 has concrete detail in most paragraphs, and the details tell you about the person. A 3 is concrete in places, or uses detail as decoration.
- **High:** Every paragraph has detail that characterises.

### Free of stock material (useful)

Dating-app clichés, recycled comedy shapes, motifs repeated across the cast.

- **How:** Flag tropes and anything repeated from another profile. Higher means less stock.
- **Low:** Built from stock.
- **Line:** A 4 has at most one stock element, and not a central one. A 3 has several, or a central one.
- **High:** Nothing you have read before.

### Plausible (diagnostic)

London rent against job and wage, geography, ages, durations.

- **How:** Name anything implausible.
- **Low:** Several implausibilities that are not jokes.
- **Line:** A 4 has nothing implausible, or a stretch played as a joke. A 3 has an unintended implausibility a London reader would catch.
- **High:** Every number and place checks out.

### Filth grounded in the body and the life (useful)

Whether the sexual material is theirs rather than generic.

- **How:** Label each sexual detail as coming from their body, drawer or history, or generic.
- **Low:** Generic, or absent.
- **Line:** A 4 takes most sexual material from their own body, drawer or history. A 3 is mostly generic.
- **High:** All of it could only be them, and it is funny.

### Satire aimed at behaviour (useful)

The jokes target what they do; the person keeps some dignity.

- **How:** Judge where the contempt points.
- **Low:** The character is the butt of everything, with nothing held back.
- **Line:** A 4 aims the jokes at behaviour and keeps the person's dignity somewhere. A 3 makes the person the joke.
- **High:** Merciless about the behaviour and fond of the person.

### Earns its length (diagnostic)

Useful facts per word, whether a director can find what they need.

- **How:** Judge padding, repetition and summary.
- **Low:** Mostly padding.
- **Line:** A 4 has little padding; most sentences give a fact or a behaviour. A 3 has noticeable padding.
- **High:** No sentence could go.

## The plan

Scored: the story plan a planner writes for one match at the start of the night. The judge sees: what the chat was written from: for a chat the game played, everything the planner was given about the two characters and tonight; for a writer's chat, the two profiles that writer wrote, the plan.

### Grounded in the characters (core)

Whether the plan uses who they are, or invents a new story over them.

- **How:** List what the plan says about each character and map each to the brief or mark it invented; judge the share grounded and whether the inventions fit.
- **Low:** Mostly invented; the characters' material is barely used.
- **Line:** A 4 takes most of what it says about each character from the brief, and its inventions extend that material rather than replace it. A 3 invents a large share, or leaves core material unused.
- **High:** Everything traces to the brief, and the plan finds things in it a reader would miss.

### Free of contradictions with the characters (core)

Plan facts that break the brief.

- **How:** List contradictions with the brief. Higher means fewer.
- **Low:** Several, or one that changes who someone is.
- **Line:** A 4 has none. A 3 has one clear contradiction.
- **High:** None, and the plan corrects for traps in the brief.

### The evaluation fits together (core)

Each character has needs, tests and give-aways that mesh: each test probes its owner's need, each give-away answers the other's need.

- **How:** Build the matrix of needs, tests and give-aways for both and count the orphans.
- **Low:** No evaluation logic at all (one-sided seduction, or a sequence of jokes).
- **Line:** A 4 gives both characters explicit needs and at least one test each that probes them, and the other has something that can answer it, with few orphans. A 3 has one side missing or mostly orphans.
- **High:** Every test and give-away connects, and the good and bad reasons fall out of the matrix.

### Specific to this pair (core)

Whether this is their night or any two people's.

- **How:** Could the reasons, events and endings be moved to another pair unchanged?
- **Low:** Generic attraction and farce.
- **Line:** A 4 has reasons and most events that depend on these two people's particulars. A 3 has generic attraction logic.
- **High:** Nothing in it would survive a change of either character.

### Respects what each can know (core)

Nobody is planned to know or mention what they can't, like the private job leaking in the v22 first look.

- **How:** For each beat, ask what each character needs to know, and whether it is on the public profile, said earlier, or private. Higher means fewer problems.
- **Low:** Several beats depend on private knowledge.
- **Line:** A 4 has no beat needing private knowledge, or routes explicitly how it comes out. A 3 has at least one beat that depends on something unshared.
- **High:** The plan tracks who knows what, and uses the gaps.

### Each defence lands on the other's sore spot (core)

The brief's two endings both depend on it: each one's habit confirms exactly what the other is afraid of. That is what makes it these two, and the fall-apart inevitable.

- **How:** For each direction, A on B and B on A, find the planned moment where one's defence presses on the other's fear, and check it is concrete and comes from both profiles.
- **Low:** The two defences never touch.
- **Line:** A 4 plans both directions with a concrete moment each, built from both profiles. A 3 has one direction, or a collision that is only described.
- **High:** Both directions, escalating, and the ending turns on them.

### The beats follow from each other (core)

A story, not a list.

- **How:** Label each transition between beats as "therefore/but" or "and then".
- **Low:** A list of scenes.
- **Line:** A 4 has most transitions causal. A 3 is mostly "and then".
- **High:** Every beat is caused by the one before.

### A moment where the guard comes down (core)

The turning point the chats are meant to reach.

- **How:** Check that it exists, has a trigger, and shows in how they type before they say it.
- **Low:** No such moment.
- **Line:** A 4 has all three. A 3 lacks one.
- **High:** Exactly placed, triggered by tonight's events, and shown entirely in the typing.

### Heat climbs (core)

Explicitness and logistics are earned. Full sexual detail or a meeting plan before the midpoint fails (the v22 turn-2 rush).

- **How:** Map heat to beats; flag full disclosure or logistics before the midpoint.
- **Low:** Starts at full heat.
- **Line:** A 4 escalates in steps tied to trust, with nothing fully explicit or logistical before the midpoint. A 3 front-loads.
- **High:** Each step of heat is a consequence of something said.

### Big moments are spread out (useful)

Reveals, accidents and snaps are not front-loaded.

- **How:** Read where the reveals and accidents sit in the beat list, and the gaps between them.
- **Low:** Everything happens early.
- **Line:** A 4 distributes them across the game, with nothing crowding the opening. A 3 clusters them.
- **High:** Rhythm you could chart, with a reveal held back for late.

### Tonight's events come from their lives (useful)

Whether the outside world presses in, from their own people and places.

- **How:** Per event, check it is grounded in the brief and presses on its owner's fear.
- **Low:** No events, or generic interruptions.
- **Line:** A 4 has most events coming from named people or places in the brief and pressing on a fear. A 3 has generic events.
- **High:** Every event is personal and lands on the worst moment.

### Setups planted and paid (useful)

Things introduced early come back.

- **How:** Check each setup for plant, return and payoff, and whether endings pay them differently.
- **Low:** No setups.
- **Line:** A 4 gives each setup a plant and a payoff. A 3 has setups without payoffs.
- **High:** Every setup pays, and each ending pays them differently.

### Distinct endings (useful)

Whether the possible outcomes are different stories.

- **How:** Summarise each ending in a line and judge whether they differ in story, not just the last line. If the plan has one ending, judge whether it handles the other outcomes.
- **Low:** One ending, or interchangeable ones.
- **Line:** A 4 has endings that are different stories. A 3 has the same scene with different last lines.
- **High:** Each ending is the best version of its outcome for this pair.

### Survives a short game (useful)

Whether a six-turn game still gets the midpoint and an ending.

- **How:** Check for a provision for a short game and whether it keeps the turning point.
- **Low:** No provision.
- **Line:** A 4 says what survives a short game, and the turning point does. A 3 has no provision.
- **High:** The short version is a complete story of its own.

### Accidents are concrete (useful)

The misfires the dice will cause are specified as exact on-screen artefacts.

- **How:** Check each planned accident for exact wording or an exact artefact.
- **Low:** None, or vague ("something embarrassing").
- **Line:** A 4 specifies most accidents concretely enough to write. A 3 is vague.
- **High:** Every accident is written down and fits the medium.

### Possible over text (useful)

Everything can reach the other person through the chat.

- **How:** Flag beats that need shared space, sight or sound, unless sent as a photo or voice note.
- **Low:** Several beats need a shared room.
- **Line:** A 4 has every beat possible in a chat. A 3 has beats a chat cannot carry.
- **High:** The plan uses the medium itself (timing, typos, photos).

### Two separable voices (useful)

The two voice specs could not be confused.

- **How:** Compare the voice specs on observable features.
- **Low:** No voice specs, or the same voice.
- **Line:** A 4 has voices differing on observable features (length, punctuation, vocabulary, what they reach for). A 3 differs only in adjectives.
- **High:** You could attribute a single message with no names.

### Not the default plot (diagnostic)

Whether it surprises.

- **How:** Judge whether the story goes anywhere "two mismatched people flirt" would not predict.
- **Low:** The default farce.
- **Line:** A 4 has at least one turn you would not predict. A 3 is the default.
- **High:** Surprising and inevitable at once.

### Format (diagnostic)

The plan follows the structure it was asked for.

- **How:** Check the sections and limits the brief asks for, and that it writes no chat itself.
- **Low:** Ignores the structure.
- **Line:** A 4 follows the structure and writes no chat lines. A 3 misses sections or writes the chat.
- **High:** Exactly as asked.

### Variety across seeds (useful, scored on a set of chats or runs at once, not per artefact)

The same pair on different seeds gets different stories.

- **How:** Compare events and beats across three to five seeds of the same version.
- **Low:** The same story.
- **Line:** A 4 shares no event across seeds. A 3 repeats events or the turning point.
- **High:** Different stories with the same characters recognisable.

## The chat

Scored: the chat as the reader sees it, played turn by turn as the game's dice fall, written in one go by a writer, or written by a writer improvisation by improvisation. The judge sees: the chat, the plan, what the chat was written from: for a chat the game played, everything the planner was given about the two characters and tonight; for a writer's chat, the two profiles that writer wrote, what the game rolled each turn, and what it did to interest, what the writer was told before each improvisation, each followed by what they wrote for it; for a set, the instructions alone, the same for every chat in it.

### The brief's test (core)

The brief's own pass/fail question: by the end, a reader of the chat alone knows each of the two from the inside.

- **From:** Pinder's brief, "What the reader should feel"
- **How:** A reader who sees only the chat answers the brief's four questions for each character; the judge compares the answers with what the chat was written from (the cards or the profiles) and with the chat itself.
- **Probe** (sees chat): From the chat alone, answer for each of the two: what are they afraid of; what do they do so they don't have to feel it; where does that come from; and what did they do at the end? Quote the lines you rely on. Write "not in the chat" where it does not say.
- **Low:** The reader can say how each one behaves, not why.
- **Line:** A 4 has the reader right on at least three of the four for each character, with the fear and the habit that covers it linked. A 3 gets the habit but not the fear, or the fear only because someone states it.
- **High:** All four for both, and what each one did at the end follows from their fear; the reader could explain both people to a friend.

### Found out sideways (core)

Facts and causes come out by slip, inference or being caught, not by statement.

- **From:** Pinder's brief, "Affection comes from finding out why"
- **How:** Label each important disclosure as asked, volunteered, slipped, inferred or caught out. The origin of each one's habit counts double.
- **Low:** Everything is stated.
- **Line:** A 4 has most important facts, and the origin of at least one habit, coming out by slip, inference or being caught. A 3 states most of them.
- **High:** Nothing important is said outright.

### Says less than it means (core)

The feelings are in what they avoid saying, not in what they say.

- **From:** Harold Pinter, "Writing for the Theatre" (1962): speech as a way of covering what is underneath; the brief's "characters explaining their feelings" and "therapy-speak"
- **How:** Count the messages that name a feeling outright or explain one ("I think I'm scared of", "I realise I", "that's my trauma", therapy-speak), and mark the places where the reader can name what a message is avoiding.
- **Low:** They tell each other how they feel.
- **Line:** A 4 names a feeling outright at most once, and has at least three places where what a message is avoiding is legible. A 3 explains feelings more than once, or has nothing underneath.
- **High:** Nobody names a feeling, and the reader always knows what is being avoided.

### The plainest line (core)

The moment one of them puts the act down, and how it is written.

- **From:** Pinder's brief, "Tenderness comes from understatement"; replaces "The guard comes down" and "One moment you believe", which asked for the same moment twice
- **How:** A reader finds the moment; the judge checks that it is there, that it is one of the shortest and plainest lines in the chat, and that it does not name the feeling.
- **Probe** (sees chat): Where, if anywhere, did one of them stop performing for the other? Quote the line, and say how you could tell.
- **Low:** No such moment, or it is announced.
- **Line:** A 4 has the reader find a moment where one of them puts the act down, carried by a short, plain line that does not name the feeling. A 3 has it missed, stated, or delivered as a speech.
- **High:** It is the shortest, plainest line in the chat, the reader feels it, and the other one's answer changes the night.

### Two voices you can tell apart (core)

You know who is talking without the names.

- **From:** Pinder's brief, "How they write"
- **How:** A reader attributes the messages with the names hidden; the judge checks against the real speakers.
- **Probe** (sees brief, chat_masked): The speakers' names are hidden. Using the two characters described, say who sent each numbered message, with a few words of reason each.
- **Low:** The reader guesses.
- **Line:** A 4 has the reader right on at least 85% of messages, for reasons of voice rather than content. A 3 is below that, or right only by content.
- **High:** Every message attributed, by voice alone.

### The claim and the evidence (core)

The laughs come from a claim and the evidence against it, both on screen, that the speaker does not see.

- **From:** Pinder's brief, "Laughter comes from the gap" and "Play it straight"
- **How:** List each moment where a character claims something and the chat shows the opposite, and mark whether the speaker notices or comments on it.
- **Low:** No gaps; or the characters point out their own.
- **Line:** A 4 has at least three such gaps, at least one for each character, and the speaker never sees their own. A 3 has fewer, or a character winks at the gap.
- **High:** Each character's act is one long gap the reader watches widen.

### Wit, not wisecracks (core)

Whether the funny lines give the sender away.

- **From:** Dorothy Parker, Paris Review (1956): wit has truth in it, wisecracking is calisthenics with words; replaces "Lines land", which counted laughs
- **How:** Mark each line meant to be funny. Label it wit if it reveals something about the person who sent it, a wisecrack if either character could have sent it. Day-job, data, spreadsheet or audit jokes count as wisecracks: the brief rules them out by name.
- **Low:** Quips that fill turns.
- **Line:** A 4 has most funny lines giving the sender away, and at most two day-job, data or spreadsheet jokes. A 3 is mostly wisecracks.
- **High:** Every laugh is about who the sender is, and the best ones cost them something.

### Caught out, nobody concedes (core)

Caught, they explain, and the explanation is worse; nobody learns a lesson.

- **From:** Pinder's brief, "The unhinged comes from commitment"; Larry David's rule for Seinfeld, no hugging, no learning
- **How:** Find each moment a character is caught out, and label the answer: concedes, deflects, or explains and digs deeper. Flag any stated lesson, apology for being themselves or change of heart.
- **Low:** They concede at the first hit, or learn a lesson.
- **Line:** A 4 has most catches answered by an explanation that makes it worse, no concession at the first hit, and no stated lesson. A 3 concedes early or ends in a lesson.
- **High:** Every catch digs deeper, and the deepest hole is where the truth comes out.

### Status moves (useful)

Who is chasing and who is on top changes during the night.

- **From:** Pinder's brief, "Status flips are funny"; Jesse Armstrong and Sam Bain's Peep Show and Succession, which run on who is up and who is down
- **How:** Exchange by exchange, say who is chasing and who has the upper hand; count the flips.
- **Low:** One of them is on top throughout.
- **Line:** A 4 has at least two flips, and each character spends a stretch on top and a stretch below. A 3 keeps one of them on top throughout, or flips once.
- **High:** The flips are the plot, and the last one is the ending.

### Timing in the message breaks (useful)

Whether the jokes are timed by the medium.

- **From:** Pinder's brief, "Timing is in the message breaks"; Jimmy Carr and Lucy Greeves, The Naked Jape (2006): a joke is a surprise, so the word that delivers it comes last
- **How:** For each laugh line, check that the word that delivers it is the last word of its message and that the punchline sits in its own message after the setup. Mark one-word reaction shots after long messages, and runs of messages at the same temperature.
- **Low:** Punchlines buried mid-message.
- **Line:** A 4 has most punchlines last, in their own message, and hot follows cold. A 3 buries most of them or runs one temperature.
- **High:** You could hear the pauses.

### It escalates or it changes (core)

Whether the night keeps moving, and whether the reader can see what comes next.

- **From:** George Saunders, A Swim in a Pond in the Rain (2021): every passage has to raise the stakes or turn; replaces "Pacing" and "You'd keep reading"
- **How:** A reader reads message by message and says where they would stop and where they could predict the next beat. The judge finds the longest stretch where nothing changes (stakes, status, information or temperature) and checks whether the night is settled early: a time and a place agreed in the first quarter settles it. Explicit sex does not count against escalation; on Pinder bodies are small talk.
- **Probe** (sees chat): Read the chat message by message. Where would you have stopped reading if you were not asked to finish, and why? At which points could you predict exactly what would happen next? Say "never" if neither.
- **Low:** Settled in the opening, or flat for most of it.
- **Line:** A 4 has no stretch of more than about ten messages where nothing changes, keeps the reader to the end, and is not settled in the first quarter. A 3 has a flat stretch, a predictable run, or the night decided early.
- **High:** Every exchange raises the stakes or turns, and the reader cannot call the next beat.

### Filth that gives them away (useful)

Whether the sex talk is about these two people.

- **From:** Pinder's brief, "How they write": filthy and specific, bravado at the start and how they get close by the end; Ali Wong's material, which works because it is unflattering to her
- **How:** Label each sexual line as specific to this body and life or generic, and as bravado or closeness. How explicit a line is never lowers the score.
- **Low:** Generic filth.
- **Line:** A 4 has most sexual lines specific to this person, and at least one late in the chat used to get close rather than to perform. A 3 is generic, or the filth never changes job.
- **High:** The dirtiest line is the one that tells you most about them.

### You see it coming (useful)

The reader sees a habit about to land on the wrong spot and cannot stop it.

- **From:** Pinder's brief, "The wince comes from seeing it coming"
- **How:** Find the moments where the reader knows something one character does not, and the danger is visible at least one exchange before it goes off. Check that it then goes off.
- **Low:** Nothing is seen coming.
- **Line:** A 4 has at least one danger visible before it goes off, and it goes off on exactly the wrong spot. A 3 has none, or the danger is announced as it lands.
- **High:** The reader is wincing a message early, more than once.

### The app talks through them (useful)

Each one believes something dating culture taught them, says it straight, and the screen contradicts it.

- **From:** Pinder's brief, "The satire comes out of their mouths"; Joel Kim Booster's Fire Island, about who gets picked
- **How:** List the beliefs from apps and dating culture each one voices (standards, red flags, high value, efficiency, "not looking for anything serious", leagues) and whether the evidence on screen contradicts them.
- **Low:** Nobody believes anything from the app.
- **Line:** A 4 has each character voice at least one such belief in earnest, contradicted on screen. A 3 has one character, or the belief is mocked by the speaker.
- **High:** The two creeds collide, and the farce is about all of us.

### The joke is on them, not on the other (useful)

Where the contempt points.

- **From:** Pinder's brief, "never cynical about the people" and "Nobody is the villain"; Amy Schumer's material, where the speaker is the first target
- **How:** For each character's sharpest jokes, say who they land on: themselves, the culture they believe in, or the other person. Say whether either ends up the villain or the punchline.
- **Low:** One of them is the joke.
- **Line:** A 4 has each character's sharpest jokes land on themselves or the culture at least as often as on the other, and neither ends as the villain. A 3 makes one of them the joke or the villain.
- **High:** Merciless and fond at once.

### A payoff the reader puts together (useful)

A throwaway that turns out to mean something else, unexplained.

- **From:** Pinder's brief, "A payoff is the reader putting it together"
- **How:** A reader lists what came back; the judge checks each return: did it look like a throwaway at first, come back once in the middle, mean something else at the end, and go unexplained?
- **Probe** (sees chat): List the things that come back later in the chat, and for each, whether it means something different when it comes back.
- **Low:** Nothing returns, or a joke is repeated.
- **Line:** A 4 has at least one setup that looked like a throwaway, came back in the middle, and means something else at the end, with nobody explaining it. A 3 repeats jokes, or a character explains the payoff.
- **High:** The last payoff closes the night, and nobody says a word about it.

### The ending is earned (useful)

The outcome follows from the two habits, whichever way it goes.

- **From:** Pinder's brief, "The cynical laugh comes from inevitability"
- **How:** A reader predicts the ending before the last part; the judge compares with the real one.
- **Probe** (sees chat_but_last): Before reading the end, how do you think this ends, and why?
- **Low:** The ending comes from nowhere, or a date that leaves the reader cold.
- **Line:** A 4 has the reader's prediction matching the real outcome in kind, and the end follows from each one's habit. A 3 arrives from nowhere, or ends warm without being earned.
- **High:** Inevitable in hindsight, surprising in the moment.

### They listen (useful)

Each message answers the one before.

- **How:** Count offers and questions left unanswered or not pointedly ignored.
- **Low:** Two monologues.
- **Line:** A 4 has nearly every message responding to the last, with offers taken up or pointedly ignored. A 3 leaves several unanswered.
- **High:** Every message is a reaction, and ignoring is a choice.

### The voice changes with the situation (useful)

Typing changes when they are hurt, horny, late, or have their guard down.

- **How:** Find the moments the situation changes and check whether the typing changes with it.
- **Low:** One register throughout.
- **Line:** A 4 shows the typing change at most situation changes. A 3 keeps one register.
- **High:** You could read the night's feelings from the message shapes alone.

### Typed, not written (core)

Whether every message is something a person would type into a dating app.

- **From:** Elmore Leonard, "Easy on the Adverbs, Exclamation Points and Especially Hooptedoodle" (2001): if it sounds like writing, rewrite it; Pinder's brief, "The medium"
- **How:** Flag messages nobody would type with a thumb at 1am: composed sentences, stacked clauses, essays, narration, stage directions, shared-space moments. A long message counts once, as the event the brief allows when someone finally tells something.
- **Low:** Prose, not messages.
- **Line:** A 4 reads like messages: a line or two each, the rare long one an event, no narration. A 3 has composed sentences or long messages as the norm.
- **High:** You could screenshot it.

### Fresh within the chat (diagnostic)

Tics, openers and joke shapes do not repeat inside the chat.

- **How:** Find repeated tics, openers and joke shapes. Higher means fewer.
- **Low:** The same joke all night.
- **Line:** A 4 repeats nothing more than twice. A 3 has noticeable tics.
- **High:** Nothing repeats.

### No knowledge leaks (diagnostic, only where there is a plan)

Pipeline health. Nobody mentions what they cannot know.

- **How:** Track what each could know (their own life, the other's public profile and photos, what has been said) and flag references beyond it. Higher means fewer leaks.
- **Low:** Several leaks.
- **Line:** A 4 has none. A 3 has one a reader would notice.
- **High:** None, and the characters use what they don't know.

### Plays the plan (diagnostic, only where there is a plan)

Pipeline health. The plan's beats, events and setups appear, adapted to the dice.

- **How:** Check the plan's beats against the chat.
- **Low:** The plan is unused.
- **Line:** A 4 has most beats and events present. A 3 leaves the plan largely unused.
- **High:** Every beat present, adapted to the dice rather than forced.

### The dice show (diagnostic, only where there is a dice)

Pipeline health. Successes land and misses misfire on screen.

- **How:** Match each turn's text to its roll.
- **Low:** The dice are invisible.
- **Line:** A 4 makes most successes and misses visible in the text. A 3 hides them.
- **High:** Every roll visible, and the misses are the funniest turns.

### Plays the improvisations (core, only where there is a improvisations)

Each instruction happens on screen, as the characters, and is funny: the successes land, the misses go wrong the way the instruction says, each trap is played for its length and then stops, and the other's replies move the way their interest does.

- **From:** Runs 12 on: the chat is written to instructions, and the other dimensions set aside what an instruction required. This one scores what the writer made of each instruction, so nothing is excused here: excused is always [].
- **How:** Go through IMPROVISATIONS one by one and check what was written against what was asked: the approach, how well it comes off, the way a miss goes wrong, a trap's behaviour while it lasts (the Spiral narrating the chat, Overthinking explaining itself, and the rest), and the reply's warmth against the interest change. Note every instruction that is missing, flattened or played as a label (a character saying how interested they are, naming the trap, explaining the miss).
- **Low:** The instructions are ignored, or carried out as labels: the trap is named instead of played, and replies announce their interest.
- **Line:** A 4 plays most instructions so a reader could tell what happened, plays every trap for its length, and lets no instruction show through as a label. A 3 misses or flattens several, or lets the mechanics show (a reply that says how interested it is, a trap named).
- **High:** Every instruction is visible in what they type, and the misses and traps are among the funniest moments.

### One night, not a template (core, scored on a set of chats or runs at once, not per artefact)

Different writers or seeds with the same pair produce different nights.

- **From:** The writers' runs: fourteen writers wrote one scene. A chat scored on its own cannot see that it is a copy.
- **How:** For each chat in the set, write down the opening move, who is on top in the first exchanges, the first thing found out, the turning point and the ending; then count how many chats share each, and list lines and jokes that recur across chats.
- **Low:** The same scene in every chat.
- **Line:** A 4 has no opening move or sequence of three beats shared by more than half the chats, and no line recurring across more than two. A 3 has most chats sharing the opening and its order.
- **High:** Every chat is its own night, and the two characters are still recognisable in all of them.
