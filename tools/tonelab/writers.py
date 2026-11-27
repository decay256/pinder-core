"""Writers write a character from the brief, then a chat between two of their own characters.

Profiles: each writer on the panel list is told only who they are, given the Pinder brief, and asked
to write one character for the game from a shuffled handful of the game's own fragments for that
character, as inspiration rather than requirements. No profile rules, no headings, no length. The
physique is left out: the anatomy and the physical-dysmorphia pair. So are the game's consolidated
texts (personality, backstory, diagnosis), which are its own synthesis of the same fragments.

Chats: the same writer gets the brief and the two characters they wrote, and writes how a chat
between them would go on Pinder, with no constraints: no dice, no turns, no length.

    python3 -I tools/tonelab/writers.py --key-stdin --only reuben          # profiles
    python3 -I tools/tonelab/writers.py --key-stdin --chat reuben,brick    # chats from the profiles
    python3 -I tools/tonelab/writers.py --key-stdin --only reuben --chat reuben,brick

Improvised chats on the frozen one-go prompt (v17, the default: no writers. Claude writes the two characters
once with no persona, from Pinder's premise v2 and the game's notes, and one chat is written from them on
template v2; Claude on the subscription login, no key):

    python3 -I tools/tonelab/writers.py --turns reuben,brick --mechanics docs/tone-lab/scratch/writers-v14/mechanics.json \
        --theme T42 --method M10 --out docs/tone-lab/scratch/writers-v22

v16 (TONELAB_WRITERS_VERSION=v16) is v15 on template v2, which says what the theme and the method are for,
with the pool entries told without their research context, each comedian's characters from --from.
v15 tells the writer the tone lab's balance instead of the game's interest, and ends the chat by its rule:
five moves in a row that land or backfire, else eleven improvisations and the balance. v14 is the same
with the game's interest and ending (TONELAB_WRITERS_VERSION=v14):

    python3 -I tools/tonelab/writers.py --turns reuben,brick --from docs/tone-lab/scratch/writers-v14 \
        --mechanics docs/tone-lab/scratch/writers-v14/mechanics.json --writers carr,booster,wong \
        [--theme T42 --method M10] [--print-turns] --out docs/tone-lab/scratch/writers-v17

The improvisation version before v14 (comedian personas on OpenRouter, own brief and characters) is
TONELAB_WRITERS_VERSION=v13 with --key-stdin, and is pinned as the tag tonelab-impro-v13.
"""
import argparse, json, os, random, re, sys, threading, time

REPO = "/root/projects/pinder-core"
sys.path.insert(0, os.path.join(REPO, "tools", "tonelab"))
import tonelab as tl  # noqa: E402
import yaml  # noqa: E402

MODEL = "google/gemini-4-argon"
SEED = 7

WRITERS = [
    ("armstrong-bain", "Jesse Armstrong and Sam Bain"),
    ("waller-bridge", "Phoebe Waller-Bridge"),
    ("coel", "Michaela Coel"),
    ("saunders", "George Saunders"),
    ("david", "Larry David"),
    ("leonard", "Elmore Leonard"),
    ("pinter", "Harold Pinter"),
    ("parker", "Dorothy Parker"),
    # run 4: comedians who work on dating, sex and identity, with run 3's prompts
    ("ansari", "Aziz Ansari"),
    ("wong", "Ali Wong"),
    ("martin", "Mae Martin"),
    ("booster", "Joel Kim Booster"),
    ("carr", "Jimmy Carr"),
    ("schumer", "Amy Schumer"),
]

PROMPTS = {
    "v1": {
        "system": "You are {name}.",
        "user": """Here's a brief.

{brief}

Write a character for this game: {char} ({pronouns}). Below is a random bunch of tidbits the game has generated for {obj}. Take them as inspiration, not requirements: use what you like, change it, or ignore it.

{tidbits}
""",
        "chat": """Here's a brief.

{brief}

Here are two characters you wrote for this game.

=== {a} ===

{a_profile}

=== {b} ===

{b_profile}

Now write how a chat between these two would go if they were texting on Pinder, without any constraints. {a} writes first. Write it the way you write.
""",
    },
    # v2: the writer is the author and the authority, and the game's material is theirs to throw away.
    "v2": {
        "system": """You are {name}. Not someone writing in your manner: you, with your own ear, your own taste and your own judgement.

The people making Pinder have asked you to write for it. On the characters and the dialogue you are the authority: you decide who these people are, what about them matters, how they talk and how they type. Nobody will hold you to anything else.""",
        "user": """Here's the brief the Pinder team wrote, so you know what the game is and what it's for.

{brief}

Now write a character for it: {char} ({pronouns}).

The game generated the list below about {obj}. It is raw material, nothing more, and most of it may be no use to you. Keep only what interests you, change anything, and throw the rest away. Nothing in it binds you: not the habits, not the history, not the way it says {obj} talks. If you'd rather invent {obj} from scratch, do.

Write the character in whatever form you'd actually hand over, as long or as short as you'd make it.

{tidbits}
""",
        "chat": """Here's the brief the Pinder team wrote.

{brief}

Here are the two characters you wrote for it.

=== {a} ===

{a_profile}

=== {b} ===

{b_profile}

Now write their chat on Pinder. {a} writes first. It's your chat: as long or as short as it should be, and typed the way these two would actually type.
""",
    },
}
# v3: v2 without the game's texting-style fragments, and one sentence asking the writer for their own.
PROMPTS["v3"] = dict(PROMPTS["v2"], user=PROMPTS["v2"]["user"].replace(
    "Write the character in whatever form you'd actually hand over, as long or as short as you'd make it.\n",
    "Write the character in whatever form you'd actually hand over, as long or as short as you'd make it.\n\n"
    "Develop your own texting style for this character, so the character is distinct.\n"))
assert PROMPTS["v3"]["user"] != PROMPTS["v2"]["user"]
# v4: v3 with the chat held to 1,000 words; the profiles are run 4's, unchanged.
PROMPTS["v4"] = dict(PROMPTS["v3"], chat=PROMPTS["v3"]["chat"].replace(
    "It's your chat: as long or as short as it should be,",
    "It's your chat: no longer than 1,000 words,"))
assert PROMPTS["v4"]["chat"] != PROMPTS["v3"]["chat"]
# v5: v3 with the chat held to 500 words; the profiles are run 4's, unchanged.
PROMPTS["v5"] = dict(PROMPTS["v3"], chat=PROMPTS["v3"]["chat"].replace(
    "It's your chat: as long or as short as it should be,",
    "It's your chat: no longer than 500 words,"))
assert PROMPTS["v5"]["chat"] != PROMPTS["v3"]["chat"]
# v6: a portrait of the writer in the system prompt; the brief first in both tasks; the game's
# fragments grouped under headings ("what we know about him"); the profile written knowing it is for
# a short chat; the chat written as a joke to be read, with a word limit (--words).
PORTRAITS = {
    "wong": """You made Baby Cobra and Hard Knock Wife pregnant, Don Wong married and Single Lady divorced and dating again. You co-wrote Always Be My Maybe and played Amy in Beef. Your material is sex, bodies, marriage and money, told without shame and in very specific physical detail. Your jokes say the thing nobody admits wanting, and the filthiest line always gives away something true about the person saying it. You like to flip who's winning: the one who looks like she's losing is running the deal.""",
    "booster": """You were born in Korea, adopted by white evangelical parents in the Midwest and homeschooled, and you found gay life with no map. Psychosexual is an hour about sex, apps, bodies and being wanted; you wrote and starred in Fire Island, Pride and Prejudice on a gay holiday, about who gets ranked where. Your jokes are vain and honest at once: you admit the shallow thing you want, name the hierarchies of bodies and race the apps run on, and let the hurt under it show for a second before the next joke covers it.""",
    "carr": """You write one-liners, and you've thought hard about how they work: you co-wrote The Naked Jape, a book about jokes. Your rule is the shortest route to the laugh: set up an assumption, cut every word that isn't needed, and put the word that springs the twist last. Your persona is a man with no shame saying the unsayable, deadpan, and the joke is often on you: your image, your vanity, your tax affair. You go dark on purpose, and the darkness is aimed, never random.""",
}
PROMPTS["v6"] = {
    "system": """You are {name}: you, with your own ear and your own taste, not someone imitating you.

{portrait}

You're writing for Pinder, a comedy game. Each time, you'll first read its brief: what the game is, what it wants to say about dating apps, and what a reader should be left with. Then you'll get one task.""",
    "user": """{brief}

# Your task

Write {char} ({pronouns}), one of the game's characters. Below is what we know about {obj}. Keep what interests you, change anything, throw the rest away. If you'd rather invent {obj} differently, do.

Later you'll write a short chat between {obj} and another character, working from what you write here and nothing else. So write {obj} the way you'd want {obj} in hand for that: as long as it needs to be, and no longer. Work out how {subj} types yourself, so {subj} is distinct.

{tidbits}
""",
    "chat": """{brief}

# Your task

Here are the two characters you wrote for Pinder.

## {a}

{a_profile}

## {b}

{b_profile}

# The chat

Write their chat on the app, no longer than {words} words. {a} writes first.

Write it as a joke. It will be read, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends. And write it in Pinder's spirit: put a finger in the wound of dating apps, through these two.
""",
}
TEXTING = {"v1": True, "v2": True, "v3": False, "v4": False, "v5": False, "v6": False}
# v7: v6, with the chat task asking first for the joke and why it is the right joke for these two.
PROMPTS["v7"] = dict(PROMPTS["v6"], chat=PROMPTS["v6"]["chat"].replace("""# The chat

Write their chat on the app, no longer than {words} words. {a} writes first.

Write it as a joke. It will be read, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends. And write it in Pinder's spirit: put a finger in the wound of dating apps, through these two.
""", """# The chat

You'll write their chat on the app as a joke. Before you write it, work the joke out.

First, under the heading "## The joke", say what the joke is and why it's the right joke for these two: the setup, where it turns, the punchline, and why it could only land on these two. A few sentences.

Then, under the heading "## The chat", write the chat: no longer than {words} words, {a} first, one message per line, as "{a}: message" or "{b}: message", and nothing else under that heading.

The chat is that joke, and it has to work without the explanation: the reader never sees it. It will be read, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends. And write it in Pinder's spirit: put a finger in the wound of dating apps, through these two.
"""))
assert PROMPTS["v7"]["chat"] != PROMPTS["v6"]["chat"]
TEXTING["v7"] = False
GROUPED = {"v6", "v7"}
# v8: the brief cut to its what and why (brief_short); the chat is one joke with one punchline. The
# joke heading holds the punchline word for word, the joke in one sentence, and why it is Pinder.
PROMPTS["v8"] = dict(PROMPTS["v7"], chat=PROMPTS["v7"]["chat"][:PROMPTS["v7"]["chat"].index("# The chat")] + """# The chat

You'll write their chat on the app as a joke: one joke, with one punchline. Work it out before you write.

First, under the heading "## The joke", write three things:

1. The punchline: the last message of the chat, word for word.
2. The joke, in one sentence: what the reader believes until that message, and what the message makes them see.
3. Why it's Pinder: in two or three sentences, what it says about dating apps and the people on them, and why it lands on these two.

Then, under the heading "## The chat", write the chat: no longer than {words} words, {a} first, one message per line, as "{a}: message" or "{b}: message", ending on the punchline, and nothing else under that heading. Use only what the joke needs.

The chat has to work without the explanation: the reader never sees it. It's a text chat, read on a phone, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends.
""")
assert PROMPTS["v8"]["chat"].startswith("{brief}")
TEXTING["v8"] = False
GROUPED = {"v6", "v7", "v8"}
SHORT_BRIEF = {"v8"}  # versions that get brief_short()
JOKE_FIRST = {"v7", "v8"}  # versions whose chat answer is "## The joke" then "## The chat", saved apart
# v9: the short brief for both tasks. The character task gives the game's notes shuffled per writer,
# with no headings and the claims apart from what's true; the writer states their own take first and
# picks the form. The chat is worked out as a bit under "## The bit" (the idea and how they found it,
# why it's funny, why it's Pinder), with no punchline written in advance and no required first speaker.
PROMPTS["v9"] = dict(PROMPTS["v8"], user="""{brief}

# Your task

Pinder needs a character: {char} ({pronouns}). Below are notes about {obj} from the game's files, in no particular order. Some are better than others, and a few contradict each other. They're raw material, not a spec: keep the two or three that make you laugh, change them, or ignore them all and invent {obj} yourself.

Start with your take on {obj}: in a few lines, what's funny about {obj} to you, the thing you'd build a bit on. Then write {obj} in whatever form you'd want in hand later: notes, a list, a monologue in {poss} voice, anything. Make it as long as it's useful to you, and no longer. Somewhere in it, say how {subj} types.

Later you'll write a chat between {obj} and another Pinder character, working from what you write here and nothing else.

## Notes on {char}

{tidbits}
""", chat=PROMPTS["v8"]["chat"][:PROMPTS["v8"]["chat"].index("# The chat")] + """# The chat

Write their chat on the app: a comedy bit to be read on a phone. One idea, played out, ending on the laugh. Use as much or as little of the characters as the bit needs.

First, under the heading "## The bit", work it out. This is for you; the reader never sees it.

1. The idea. What comic premise do you see in these two together, the thing you'd build a bit on? And how did you get there: what in the material caught you, what you're exaggerating or turning around, how you usually find a bit and how you found this one. Don't write the punchline here. Say what kind of turn it will be and why it follows from the premise.
2. Why it's funny. What does the reader expect, what do they get instead, and why does that gap make them laugh? "Because it's true" or "because it's sad" is not an answer: say where the laugh is.
3. Why it's Pinder. What about dating apps does this make fun of (a belief, a habit, a phrase people say with a straight face), and how do these two make it ridiculous? The reader should laugh at it first and recognise themselves second.

Then, under the heading "## The chat", write the chat: no longer than {words} words (shorter is fine), one message per line, as "{a}: message" or "{b}: message", and nothing else under that heading.

It's a text chat, read on a phone, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends.
""")
assert PROMPTS["v9"]["chat"].startswith("{brief}") and "{a} first" not in PROMPTS["v9"]["chat"]
TEXTING["v9"] = False
SHORT_BRIEF = {"v8", "v9"}
JOKE_FIRST = {"v7", "v8", "v9"}  # v9's heading is "## The bit"; it is saved as <writer>.joke.md like the joke
LOOSE = {"v9"}  # versions that give the notes ungrouped, shuffled per writer, claims apart from truths
# v10 (--pair): three sessions per writer. The writer first rewrites the short brief in their own words;
# that text is their brief from then on. Then one session sees both characters' notes and writes a
# short take on each (what's funny, what they're afraid of) and how the two clash or click. Then the
# chat, from their own brief and that text alone: one joke or a run of jokes, set up early and paid off
# at the end, chat only.
OWN_BRIEF = """Here's Pinder's brief, as you put it in your own words.

{own}
"""
PROMPTS["v10"] = dict(PROMPTS["v9"], brief_task="""{brief}

# Your task

Rewrite this brief in your own words.
""", user=OWN_BRIEF + """
# Your task

Two of Pinder's characters are about to match: {a} ({a_pron}) and {b} ({b_pron}). Below are notes about each of them from the game's files, in no particular order. Some are better than others, and a few contradict each other. They're raw material, not a spec: keep what makes you laugh, change it, or ignore it and invent.

Write your take on each of them: what's funny about them to you, and the one thing they're afraid of, in no more than about 150 words each. Then say how the two of them clash, or click, in a way nobody would expect: the thing you'd build a bit on. Use the headings "## {a}", "## {b}" and "## The two of them".

Keep it short. This is what you'll have in hand when you write their chat, and nothing else.

# Notes on {a}

{a_notes}

# Notes on {b}

{b_notes}
""", chat=OWN_BRIEF + """
# Your task

Here's what you wrote about the two characters.

{characters}

# The chat

Improvise their chat on the app: one joke, or a run of jokes, no longer than {words} words. Set something up early and pay it off at the end, so the last message picks up something from the beginning and the chat feels rounded. End on the laugh.

Write only the chat: one message per line, as "{a}: message" or "{b}: message".

It's a text chat, read on a phone, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends.
""")
TEXTING["v10"] = False
SHORT_BRIEF = {"v8", "v9", "v10"}
PAIRED = {"v10"}  # versions run with --pair: own brief, then both characters in one session, then the chat

# v11 (--turns): the writer's own brief and characters from a paired run, then the chat one turn at a
# time in one session. Each turn is assembled from the pieces below, picked by the seed's mechanics, so
# the same seed always gives the same text. The pieces say what happens in the game, not what to write.
PROMPTS["v11"] = dict(PROMPTS["v10"], chat=OWN_BRIEF + """
# Your task

Here's what you wrote about the two characters.

{characters}

# The chat

Improvise their chat on the app, one turn at a time: {turns} turns, no longer than {words} words in all. Set something up early and pay it off at the end, so the last message picks up something from the beginning and the chat feels rounded. End on the laugh.

It's a text chat, read on a phone, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends.

Each turn, I tell you how the game goes for {a}. You write {a}'s message and {b}'s reply, one message per line, as "{a}: message" or "{b}: message", and nothing else.

{frame}

# Turn 1

{turn}
""", turn="""# Turn {n}

{turn}
""")
# v12: as v11, but neither the intro nor the turn's closing piece fixes the turn at one message each.
PROMPTS["v12"] = dict(PROMPTS["v11"], chat=PROMPTS["v11"]["chat"].replace(
    'Each turn, I tell you how the game goes for {a}. You write {a}\'s message and {b}\'s reply, one message per line, as "{a}: message" or "{b}: message", and nothing else.',
    'Each turn, I tell you how the game goes for {a}. You write what {a} sends and what {b} sends back, however many messages that takes: one message per line, as "{a}: message" or "{b}: message", and nothing else.'))
# v13: improvisation instructions instead of game rules; see turn_texts13.
PROMPTS["v13"] = dict(PROMPTS["v12"], chat=OWN_BRIEF + """
# Your task

Here's what you wrote about the two characters.

{characters}

# The chat

Improvise their chat on the app, in {turns} improvisations, no longer than {words} words in all. Set something up early and pay it off at the end, so the last message picks up something from the beginning and the chat feels rounded. End on the laugh.

It's a text chat, read on a phone, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends.

For each improvisation, I tell you how it goes for {a}. You write what {a} sends and what {b} sends back, however many messages that takes: one message per line, as "{a}: message" or "{b}: message", and nothing else.

# Improvisation 1

{turn}
""", turn="""# Improvisation {n}

{turn}
""")
TURNS = {"v11", "v12", "v13"}  # versions run with --turns
_SING = dict(s="s", es="es", ies="ies", is_="is", has="has", does="does", s_="'s")
PRON = {"he/him": dict(he="he", him="him", his="his", himself="himself", **_SING),
        "she/her": dict(he="she", him="her", his="her", himself="herself", **_SING),
        "they/them": dict(he="they", him="them", his="their", himself="themselves",
                          s="", es="", ies="y", is_="are", has="have", does="do", s_="'re")}
P_FRAME = ("This chat runs on Dungeons & Dragons rules. Each turn, {A} picks one of {a_his} six stats (Charm, Rizz, Honesty, "
           "Chaos, Wit, Self-awareness) and rolls a twenty-sided die against a difficulty. How far {a_he} beats or misses it "
           "decides how well {a_his} message comes off.\n\n"
           "{B}'s interest in {a_him} runs from 0 to 25. At 25 {b_he} agrees to a date; at 0 {b_he} unmatches {a_him}.")
P_TIME = "It's {clock}. {heat}"
P_HEAT = [(1, 3, "Tonight the heat between them is low: neither of them is in a hurry."),
          (4, 6, "Tonight the heat between them is medium: there's something in the air."),
          (7, 8, "Tonight the heat between them is high: it's on both their minds."),
          (9, 99, "Tonight the heat between them is very high: neither of them is far from saying it.")]
P_START = "{A} and {B} have just matched, and the chat is empty. {B}'s interest is {n}. {band}"
P_BAND = [(0, 0, "{B_He} {b_has} unmatched {a_him}."),
          (1, 4, "{B_He}{b_s_} nearly gone: one more wrong message and {b_he} leave{b_s}."),
          (5, 9, "{B_He}{b_s_} curious but not convinced, and could drift away."),
          (10, 15, "{B_He}{b_s_} interested, leaning neither way yet."),
          (16, 20, "{B_He}{b_s_} drawn in, but still protecting {b_himself}."),
          (21, 24, "{B_He}{b_s_} close to saying yes to a date, not yet."),
          (25, 25, "{B_He} agree{b_s} to a date, and the chat ends there.")]
STAT_NAME = {"SelfAwareness": "Self-awareness"}
P_STAT = {"Charm": "Charm means being warm, easy and reassuring.",
          "Rizz": "Rizz means saying plainly that {a_he} wants {b_him}.",
          "Honesty": "Honesty means {a_he} hides nothing: {a_he} shows something real, or asks for it.",
          "Chaos": "Chaos means swerving somewhere unexpected that still makes sense.",
          "Wit": "Wit means being quick and funny.",
          "SelfAwareness": "Self-awareness means naming what's actually going on underneath."}
SHADOW_OF = {"Charm": "Fixation", "Rizz": "Despair", "Honesty": "Denial", "Chaos": "Madness", "Wit": "Overthinking",
             "SelfAwareness": "Dread"}
P_SHADOW = {"Fixation": "the warmth clings: needing to be seen back, too much and too soon.",
            "Despair": "the want gives up halfway and turns into proof it was never going to work.",
            "Denial": "the honest thing comes out as its own opposite, insisted on.",
            "Madness": "something not quite {a_him} leaks in: a detail that doesn't belong, said calmly.",
            "Overthinking": "it qualifies and explains itself until the joke or the point is dead.",
            "Dread": "{a_he} see{a_s} {a_himself} failing with perfect clarity and narrate{a_s} it while it happens."}
P_ROLL = "{A} rolls {stat}. {means} {A_He} rolls {total} against {dc}, {by}. {grade}"
P_ROLL_NAT = "{A} rolls {stat}. {means} {A_He} rolls a natural {nat}. {grade}"
P_GRADE = {"success": "That's a success: it works.",
           "strong": "That's a strong success: it works better than {a_he} meant.",
           "critical": "That's a critical success: it comes out really, really well.",
           "exceptional": "That's an exceptional success: it's brilliant, the line {a_he}'d hoped to write.",
           "nat20": ("That's a natural 20, the best roll there is: by luck or by genius, {a_he} says exactly what {b_he} needed "
                     "to hear, the way {a_he} would at {a_his} very best."),
           "Fumble": "That's a fumble: nearly, but it slips, slightly.",
           "Misfire": "That's a misfire: it comes out wrong.",
           "TropeTrap": "That's a trope trap: it falls into a dating cliché, and the cliché sticks to {a_him}.",
           "Catastrophe": "That's a catastrophe: it comes out cringe, embarrassing or inappropriate, in a funny way.",
           "nat1": ("That's a natural 1, the worst roll there is: by bad luck or by instinct, {a_he} says exactly what {b_he} "
                    "least needed to hear, the way {a_he} would at {a_his} very worst.")}
P_MISS = "A miss goes wrong through the stat's shadow. {stat}'s shadow is {shadow}: {means}"
P_TRAP = "This sets a trap that lasts three turns: the {trap} trap. {acting}"
P_TRAP_ON = "The {trap} trap is still on. {acting}"
P_TRAP_STAGE = {1: "It starts, and {a_he} doesn't notice it yet.", 2: "It's the pattern now, and {B} can see it.",
                3: "It wears off after this turn."}
P_TRAP_ACT = {"cringe": ("{A} knows how {a_he}'s coming across, and it makes it worse: every message is a little over-explained "
                         "or undercuts itself, and the harder {a_he} tries to be charming, the harder {a_he} tries."),
              "creep": ("An accidental agenda leaks into everything {A} sends: when the move lands, one line that could be "
                        "read two ways; when it misses, the whole message seems to be leading somewhere uncomfortable."),
              "overshare": ("Personal details keep leaking into {A}'s messages, whatever the move: when it lands, one aside "
                            "that gives away something private; when it misses, several, and they derail the message."),
              "unhinged": ("{A}'s messages keep accelerating past where {a_he} meant to land: when the move lands, one extra "
                           "clause or tangent at the end; when it misses, the message starts controlled and ends somewhere else."),
              "pretentious": ("{A} can't stop explaining things nobody asked about: when the move lands, one unnecessary "
                              "clarification or reference; when it misses, the message turns into a lesson."),
              "spiral": ("{A} keeps narrating the conversation while inside it: when the move lands, one aside about how the "
                         "chat is going; when it misses, {a_he} narrates {a_his} own failure as it happens, and the narrating "
                         "is the failure.")}
P_SHADOW_CHECK = "{A_He} also fails to keep {a_his} {shadow} down, so it leaks into the message whatever the roll did: {means}"
P_HORNY = ("{A_He} also fails to control {a_his} horniness: the message slides toward sex, with something inappropriate for "
           "the moment. {tier} {reception}")
P_HORNY_TIER = {"Fumble": "It's a fumble: one word runs warm.", "Misfire": "It's a misfire: the temperature shifts mid-sentence.",
                "TropeTrap": "It's a trope trap: it turns into a horny dating-app cliché.",
                "Catastrophe": "It's a catastrophe: the whole message rewrites itself around it.",
                "Legendary": "It's a catastrophe: the whole message rewrites itself around it."}
P_HORNY_LOW, P_HORNY_HIGH, HORNY_HIGH_FROM = "At this point {B} isn't won over by it.", "{B}'s into {a_him} enough that {b_he} might flirt back.", 16
P_STEER = "{A_He} also rolls Steering and succeeds: {a_he} tries to take control of the conversation with a follow-up question."
P_COMBO = {"The Setup": "Wit, then Charm: the joke set up the charm, and {b_he} gains a little extra interest.",
           "The Triple": "Three different stats in three turns, and the third worked: {a_his} next roll gets a boost."}
P_SWING = "{B}'s interest goes from {x} to {y}: {b_he} {swing}, and {b_his} reply carries that. {band}"
P_FLAT = "{B}'s interest stays at {x}: {b_he} neither gained nor lost interest, and {b_his} reply carries that. {band}"
P_SWINGS = [(-99, -3, "lost a lot of interest"), (-2, -1, "lost a little interest"), (1, 2, "gained a little interest"),
            (3, 99, "gained a lot of interest")]
P_CLOSES = {"v11": "Your improvisation: {A}'s message and {B}'s reply.",
            "v12": "Your improvisation: what {A} sends this turn, and what {B} sends back."}


# v13's pieces. Where a piece is not here, v11's is used unchanged (time and heat, bands, shadow meanings,
# horniness tiers and reception).
I_STAT = {"Charm": "It means being warm, easy and reassuring.",
          "Rizz": "It means saying plainly that {a_he} want{a_s} {b_him}.",
          "Honesty": "It means {a_he} hide{a_s} nothing: {a_he} show{a_s} something real, or ask{a_s} for it.",
          "Chaos": "It means swerving somewhere unexpected that still makes sense.",
          "Wit": "It means being quick and funny.",
          "SelfAwareness": "It means naming what's actually going on underneath."}
I_CHOOSE = "{A} chooses {stat}. {means} {grade}"
I_GRADE = dict(P_GRADE,
               nat20=("That's the best it could possibly go: by luck or by genius, {a_he} say{a_s} exactly what {b_he} needed to "
                      "hear, the way {a_he} would at {a_his} very best."),
               nat1=("That's the worst it could possibly go: by bad luck or by instinct, {a_he} say{a_s} exactly what {b_he} "
                     "least needed to hear, the way {a_he} would at {a_his} very worst."))
I_MISS = "Specifically, {a_he} react{a_s} with {shadow}: {means}"
TRAP_LENGTH = 2  # the improvisation that sets the trap and the next one
I_TRAP_SET = "{A_He} {a_is_} going to do so the next time too."
I_TRAP_ON = "{A_He} still react{a_s} with {shadow}: {means} After this, it's gone."
TRAP_STAT = {"cringe": "Charm", "creep": "Rizz", "overshare": "Honesty", "unhinged": "Chaos", "pretentious": "Wit",
             "spiral": "SelfAwareness"}
I_SHADOW_CHECK = "{A_He} also can't keep {a_his} {shadow} down, so it leaks in whatever {a_he} {a_does}: {means}"
I_HORNY = ("{A_He} also can't control {a_his} horniness: the message slides toward sex, with something inappropriate for "
           "the moment. {tier} {reception}")
I_STEER = "{A_He} tr{a_ies} to take control of the conversation with a follow-up question."
I_COMBO = {"The Setup": "Wit, then Charm: the joke set up the charm, and {b_he} gain{b_s} a little extra interest.",
           "The Disarm": "Wit, then Honesty: the joke lowered {b_his} guard for the honest thing, and {b_he} gain{b_s} a little extra interest.",
           "The Triple": "Three different moves in a row, and the third one worked: {a_he} {a_has} momentum now."}
I_SWING = "{B} {swing}, and {b_his} reply carries that. {band}"
I_FLAT = "{B}'s interest doesn't move, and {b_his} reply carries that. {band}"
I_SWINGS = [(-99, -3, "loses a lot of interest"), (-2, -1, "loses a little interest"), (1, 2, "gains a little interest"),
            (3, 99, "gains a lot of interest")]
I_START = "{A} and {B} have just matched, and the chat is empty. {band}"
I_CLOSE = "Your improvisation: what {A} sends, and what {B} sends back."


def turn_texts13(m, a_name, a_pron, b_name, b_pron):
    """("", [improvisation text, ...]) for a pinder-mechanics/1 record, from v13's pieces alone."""
    pa, pb = PRON.get(a_pron, PRON["they/them"]), PRON.get(b_pron, PRON["they/them"])
    f = dict(A=a_name, B=b_name, **{f"a_{k}": v for k, v in pa.items()}, **{f"b_{k}": v for k, v in pb.items()},
             A_He=pa["he"].capitalize(), B_He=pb["he"].capitalize())
    fill = lambda s, **kw: s.format(**f, **kw)
    band = lambda n: fill(_pick(P_BAND, n))
    out, stage = [], {}
    for k, tn in enumerate(m["turns"]):
        r, stat = tn["roll"], tn["chosen_stat"]
        if r["nat20"]:
            grade = "nat20"
        elif r["nat1"]:
            grade = "nat1"
        elif r["success"]:
            d = r["total"] - r["dc"]
            grade = "exceptional" if d >= 15 else "critical" if d >= 10 else "strong" if d >= 5 else "success"
        else:
            grade = r["final_tier"]
            if grade not in I_GRADE:
                raise ValueError(f"turn {tn['turn']}: no piece for the result {grade}")
        ps = [fill(I_CHOOSE, stat=STAT_NAME.get(stat, stat), means=fill(I_STAT[stat]), grade=fill(I_GRADE[grade]))]
        if not r["success"]:
            sh = SHADOW_OF[stat]
            ps.append(fill(I_MISS, shadow=sh, means=fill(P_SHADOW[sh])))
        if r.get("activated_trap"):
            trap = r["activated_trap"].lower()
            stage[trap] = 1
            if r["success"]:  # no shadow line above to carry on: name it here
                sh = SHADOW_OF[TRAP_STAT[trap]]
                ps.append(fill(I_MISS, shadow=sh, means=fill(P_SHADOW[sh])))
            if k < len(m["turns"]) - 1:
                ps.append(fill(I_TRAP_SET))
        for trap in [x.lower() for x in tn.get("active_traps_before") or []]:
            if trap == (r.get("activated_trap") or "").lower():
                continue
            stage[trap] = stage.get(trap, 1) + 1
            if stage[trap] <= TRAP_LENGTH:
                sh = SHADOW_OF[TRAP_STAT[trap]]
                ps.append(fill(I_TRAP_ON, shadow=sh, means=fill(P_SHADOW[sh])))
        s = tn.get("shadow") or {}
        if s.get("miss"):
            ps.append(fill(I_SHADOW_CHECK, shadow=s["shadow"], means=fill(P_SHADOW[s["shadow"]])))
        h = tn.get("horniness") or {}
        if h.get("miss"):
            rec = P_HORNY_HIGH if tn["interest_before"] >= HORNY_HIGH_FROM else P_HORNY_LOW
            ps.append(fill(I_HORNY, tier=P_HORNY_TIER[h["tier"]], reception=fill(rec)))
        if (tn.get("steering") or {}).get("succeeded"):
            ps.append(fill(I_STEER))
        if tn.get("combo"):
            if tn["combo"] not in I_COMBO:
                raise ValueError(f"turn {tn['turn']}: no piece for the combo {tn['combo']}")
            ps.append(fill(I_COMBO[tn["combo"]]))
        x, y = tn["interest_before"], tn["interest_after"]
        ps.append(fill(I_FLAT, band=band(y)) if x == y else fill(I_SWING, swing=_pick(I_SWINGS, y - x), band=band(y)))
        ps.append(fill(I_CLOSE))
        if k == 0:
            ps = [fill(P_TIME, clock=m["clock"][11:16], heat=_pick(P_HEAT, m["session_horniness"])),
                  fill(I_START, band=band(tn["interest_before"]))] + ps
        out.append("\n\n".join(ps))
    return "", out


# v15's pieces: where the game's interest was (how much it moved, where it stands), the tone lab's balance.
# Everything else is v13's, unchanged, except the two combos that gave extra interest and the horniness
# reception, which now goes by the balance.
B_START = "{A} and {B} have just matched, and the chat is empty."
B_RULE = ("Every move of {A}'s counts: in {a_his} favour when it lands, neither way when it does nothing, against {a_him} "
          "when it backfires. Five in a row that land and {B} agree{b_s} to a date; five in a row that backfire and "
          "{b_he} unmatch{b_es} {a_him}. Otherwise it's decided after the last improvisation: if the balance is in "
          "{A}'s favour, {B} agree{b_s} to a date; if it's even or against {a_him}, {b_he} unmatch{b_es} {a_him}. "
          "The balance starts even: {b_he}{b_s_} open, leaning neither way.")
B_MOVE = {1: "It lands: it counts in {a_his} favour.", 0: "It does nothing: it counts neither way.",
          -1: "It backfires: it counts against {a_him}."}
B_RUN = {1: "That's four in a row that landed: one more and {B} agree{b_s} to a date.",
         -1: "That's four in a row that backfired: one more and {B} unmatch{b_es} {a_him}."}
B_STATE = {1: "Overall the balance is in {a_his} favour: {b_he}{b_s_} warming to {a_him}, and {b_his} reply carries that.",
           0: "Overall the balance is even: {b_he}{b_s_} undecided, and {b_his} reply carries that.",
           -1: ("Overall the balance is against {a_him}: {b_he}{b_s_} going off {a_him}, though {b_he}{b_s_} still in the chat, "
                "and {b_his} reply carries that.")}
B_END = {("date", "streak"): "That's five in a row that landed: {B} agree{b_s} to a date, and the chat ends there.",
         ("unmatched", "streak"): "That's five in a row that backfired: {B} unmatch{b_es} {a_him}, and the chat ends there.",
         ("date", "last"): ("That was {a_his} last move, and the balance is in {a_his} favour: {B} agree{b_s} to a date, "
                            "and the chat ends there."),
         ("unmatched", "last"): ("That was {a_his} last move, and the balance is {where}: {B} unmatch{b_es} {a_him}, and the "
                                 "chat ends there.")}
B_COMBO = dict(I_COMBO, **{"The Setup": "Wit, then Charm: the joke set up the charm.",
                           "The Disarm": "Wit, then Honesty: the joke lowered {b_his} guard for the honest thing."})


def turn_texts15(m, a_name, a_pron, b_name, b_pron):
    """("", [improvisation text, ...]) for the improvisations the tone lab's rule plays of a record."""
    steps, ending, why = play15(m)
    pa, pb = PRON.get(a_pron, PRON["they/them"]), PRON.get(b_pron, PRON["they/them"])
    f = dict(A=a_name, B=b_name, **{f"a_{k}": v for k, v in pa.items()}, **{f"b_{k}": v for k, v in pb.items()},
             A_He=pa["he"].capitalize(), B_He=pb["he"].capitalize())
    fill = lambda s, **kw: s.format(**f, **kw)
    sign = lambda n: (n > 0) - (n < 0)
    out, stage, n = [], {}, len(steps)
    for k, tn in enumerate(m["turns"][:n]):
        r, stat = tn["roll"], tn["chosen_stat"]
        p, bal, run = steps[k]
        before = steps[k - 1][1] if k else 0
        if r["nat20"]:
            grade = "nat20"
        elif r["nat1"]:
            grade = "nat1"
        elif r["success"]:
            d = r["total"] - r["dc"]
            grade = "exceptional" if d >= 15 else "critical" if d >= 10 else "strong" if d >= 5 else "success"
        else:
            grade = r["final_tier"]
            if grade not in I_GRADE:
                raise ValueError(f"turn {tn['turn']}: no piece for the result {grade}")
        ps = [fill(I_CHOOSE, stat=STAT_NAME.get(stat, stat), means=fill(I_STAT[stat]), grade=fill(I_GRADE[grade]))]
        if not r["success"]:
            sh = SHADOW_OF[stat]
            ps.append(fill(I_MISS, shadow=sh, means=fill(P_SHADOW[sh])))
        if r.get("activated_trap"):
            trap = r["activated_trap"].lower()
            stage[trap] = 1
            if r["success"]:
                sh = SHADOW_OF[TRAP_STAT[trap]]
                ps.append(fill(I_MISS, shadow=sh, means=fill(P_SHADOW[sh])))
            if k < n - 1:
                ps.append(fill(I_TRAP_SET))
        for trap in [x.lower() for x in tn.get("active_traps_before") or []]:
            if trap == (r.get("activated_trap") or "").lower():
                continue
            stage[trap] = stage.get(trap, 1) + 1
            if stage[trap] <= TRAP_LENGTH:
                sh = SHADOW_OF[TRAP_STAT[trap]]
                ps.append(fill(I_TRAP_ON, shadow=sh, means=fill(P_SHADOW[sh])))
        s = tn.get("shadow") or {}
        if s.get("miss"):
            ps.append(fill(I_SHADOW_CHECK, shadow=s["shadow"], means=fill(P_SHADOW[s["shadow"]])))
        h = tn.get("horniness") or {}
        if h.get("miss"):
            ps.append(fill(I_HORNY, tier=P_HORNY_TIER[h["tier"]], reception=fill(P_HORNY_HIGH if before > 0 else P_HORNY_LOW)))
        if (tn.get("steering") or {}).get("succeeded"):
            ps.append(fill(I_STEER))
        if tn.get("combo"):
            if tn["combo"] not in B_COMBO:
                raise ValueError(f"turn {tn['turn']}: no piece for the combo {tn['combo']}")
            ps.append(fill(B_COMBO[tn["combo"]]))
        ps.append(fill(B_MOVE[p]))
        if k == n - 1:
            ps.append(fill(B_END[(ending, why)], where="even" if bal == 0 else "against " + pa["him"]))
        else:
            if abs(run) == STREAK - 1:
                ps.append(fill(B_RUN[sign(run)]))
            ps.append(fill(B_STATE[sign(bal)]))
        ps.append(fill(I_CLOSE))
        if k == 0:
            ps = [fill(P_TIME, clock=m["clock"][11:16], heat=_pick(P_HEAT, m["session_horniness"])),
                  fill(B_START), fill(B_RULE)] + ps
        out.append("\n\n".join(ps))
    return "", out


def texts_for(m, *a):
    return (turn_texts15 if VERSION in BALANCED else turn_texts13 if VERSION in ("v13", "v14") else turn_texts)(m, *a)


# v14 (--turns): the one-go prompt that worked by hand on 2026-11-26 (templates/chat-one-pair.v1.md,
# frozen) woven into the improvisations, as templates/chat-improvised.v1.md: Pinder's premise
# (templates/premise.v1.md) instead of the writer's own brief, one comedian's descriptions of the two
# characters from --from without their take on the pair ("The two of them"), three acts laid over the
# improvisations, one theme and one method from the writers' pool for the whole chat, and the frozen
# task. v13's improvisation pieces are used unchanged; each improvisation opens with the act it is in.
# The frozen prompt was written for and tested on Claude with no persona, so v14 is written by Claude
# through the subscription CLI, one resumed session per chat, and the comedian is only the author of
# the descriptions. The improvisation version before this one is pinned as the tag tonelab-impro-v13.
TEMPLATES = os.path.join(REPO, "tools", "tonelab", "templates")
POOL = os.path.join(REPO, "docs", "tone-lab", "scratch", "pool", "pool.json")
V14 = dict(template="chat-improvised.v1.md", premise="premise.v1.md", model="opus",
           system="You write chats for Pinder, a comedy game.")
PROMPTS["v14"] = dict(PROMPTS["v13"], system=V14["system"])
TURNS.add("v14")
# v15: v14 with the tone lab's balance in the improvisations and its rule for where the chat ends
PROMPTS["v15"] = PROMPTS["v14"]
TURNS.add("v15")
# v16: v15 on template v2 (what the theme and the method are for, the balance named in the setup), with the
# pool entries told without their research context
PROMPTS["v16"] = PROMPTS["v14"]
TURNS.add("v16")
BALANCED = {"v15", "v16"}  # versions on the tone lab's balance and its rule for the ending
TEMPLATE_OF = {"v16": "chat-improvised.v2.md"}  # else V14["template"]
STANDALONE = {"v16", "v17"}  # versions that tell the pool entries without their research context
# v17: v16 with no writers. Claude writes the two characters once, from Pinder's premise and the game's notes
# with no persona (v10's task for the two characters), and one chat is written from them; the premise is v2,
# without the lines in one comedian's voice.
PROMPTS["v17"] = PROMPTS["v14"]
TURNS.add("v17")
BALANCED.add("v17")
TEMPLATE_OF["v17"] = "chat-improvised.v2.md"
PREMISE_OF = {"v17": "premise.v2.md"}  # else V14["premise"]
NEUTRAL = {"v17"}  # versions with no writers: one set of characters by Claude, one chat
NEUTRAL_ID, NEUTRAL_NAME = "claude", "Claude"
CHARS17 = PROMPTS["v10"]["user"].replace("Here's Pinder's brief, as you put it in your own words.", "Here's Pinder's brief.").replace(
    "This is what you'll have in hand when you write their chat, and nothing else.", "Their chat will be written from this.")
assert "own words" not in CHARS17 and "in hand" not in CHARS17
# The tone lab's own score for the player's moves, not the game's: a move that lands is +1, one that does
# nothing is 0, one that backfires is -1. The balance starts at 0 and can go below it; where it stands after
# the last improvisation decides the ending: above 0 is a date, 0 or below is not.
LANDS = {"Fumble": 0}  # every success lands; a fumble (nearly, but it slips) does nothing; every other miss backfires


def tally(m):
    """([(points, balance), ...] per improvisation, whether it ends in a date) by the tone lab's rule."""
    out, bal = [], 0
    for tn in m["turns"]:
        r = tn["roll"]
        p = 1 if r["success"] else LANDS.get(r["final_tier"], -1)
        bal += p
        out.append((p, bal))
    return out, bal > 0


# v15 also ends the chat by the tone lab's rule: five moves in a row that land are a date, five in a row that
# backfire are an unmatch (a move that does nothing breaks a run); otherwise the chat has MOST improvisations,
# and the balance after the last one decides: above 0 a date, 0 or below an unmatch.
STREAK, MOST = 5, 11


def play15(m):
    """The tone lab's game over a record's moves: ([(points, balance, run), ...] for the improvisations
    played, the ending ("date" or "unmatched"), why ("streak" or "last")). run is the length of the
    current run of moves with the same result, signed: +3 is three in a row that landed."""
    steps, bal, run = [], 0, 0
    for tn in m["turns"]:
        r = tn["roll"]
        p = 1 if r["success"] else LANDS.get(r["final_tier"], -1)
        bal += p
        run = run + p if p and (run > 0) == (p > 0) and run else p
        steps.append((p, bal, run))
        if abs(run) == STREAK:
            return steps, "date" if run > 0 else "unmatched", "streak"
        if len(steps) == MOST:
            return steps, "date" if bal > 0 else "unmatched", "last"
    raise ValueError(f"the game's record ends ({m.get('outcome')}, {len(m['turns'])} moves) before the tone lab's "
                     f"rule does: play it with more turns or another seed")


ACTS = [(1, "the opening"), (2, "the match"), (3, "the closing")]
ACT_SHARE = (0.3, 0.5, 0.2)  # of the chat's words, as in the frozen one-go prompt
POOL_STAGES = {"act", "middle", "why"}


def pool_entries():
    """The writers' pool, each entry with its display id (T01.. for themes, M01.. for methods, counted in
    family order) and its kind."""
    out, counts = [], {"theme": 0, "method": 0}
    for fam in json.load(open(POOL, encoding="utf-8")):
        kind = fam["family"]["kind"]
        for e in fam["entries"]:
            counts[kind] += 1
            out.append(dict(e, uid=("T" if kind == "theme" else "M") + f"{counts[kind]:02d}", kind=kind))
    return out


def pool_eligible(kind):
    """Entries tagged for at least two of act / middle / why, and not for "any": only T01 has all three,
    and the "any" methods are craft rules rather than comic devices."""
    return [e for e in pool_entries() if e["kind"] == kind and "any" not in (e.get("stage") or [])
            and len(set(e.get("stage") or []) & POOL_STAGES) >= 2]


def pool_render(e, standalone=False):
    """An entry as the writer reads it. standalone (v16 on): the explanation without its research context, the
    entry's "standalone" text where the stored one cites its source; an entry that cites one without it is refused."""
    told = e.get("meaning") or e.get("how_it_works") or ""
    if standalone:
        cited = [a.split()[-1] for a in e.get("authors") or [] if a.split() and a.split()[-1] in told]
        if e.get("standalone"):
            told = e["standalone"]
        elif cited:
            raise ValueError(f"{e['uid']} {e['name']}: its explanation cites {', '.join(cited)} and it has no standalone text")
    lines = [f"**{e['name']}**", "", e["summary"], "",
             ("What it is: " if e["kind"] == "theme" else "How it works: ") + told,
             "", "In Pinder: " + e["in_pinder"]]
    if e.get("test"):
        lines += ["", "Test: " + e["test"]]
    if e.get("goes_wrong"):
        lines += ["", "Goes wrong when: " + e["goes_wrong"]]
    return "\n".join(lines)


def fill_template(text, values):
    missing = set(re.findall(r"\{([a-z0-9_]+)\}", text)) - values.keys()
    if missing:
        raise ValueError(f"template placeholders without a value: {sorted(missing)}")
    for k, v in values.items():
        text = text.replace("{" + k + "}", str(v))
    return text


def act_split(n):
    """How many of n improvisations each act gets: act 3 three (fewer for short chats), act 1 three
    eighths of the rest, as the frozen prompt's 4-5 / 6-8 / 3 exchanges."""
    a3 = 3 if n >= 9 else 2 if n >= 5 else 1
    a1 = max(1, round((n - a3) * 3 / 8))
    return [a1, n - a3 - a1, a3]


def act_lines(n):
    """(the length line of each act, the act line that opens each improvisation)."""
    spans, opening, k = [], [], 1
    for (num, title), size in zip(ACTS, act_split(n)):
        lo, hi = k, k + size - 1
        spans.append(f"improvisation {lo}" if lo == hi else f"improvisations {lo} to {hi}")
        for i in range(lo, hi + 1):
            opening.append(f"This is where act {num}, {title}, starts." if i == lo else f"Act {num}, {title}.")
        k = hi + 1
    opening[-1] += " It's the last improvisation: the chat ends with it."
    return spans, opening


def sheets_of(path):
    """A comedian's descriptions of the two characters, without their take on the pair."""
    text = open(path, encoding="utf-8").read()
    cut = text.find("\n## The two of them")
    return (text[:cut] if cut >= 0 else text).strip()


def draw14(a):
    rng = random.SystemRandom()
    by_uid = {e["uid"]: e for e in pool_entries()}
    theme = by_uid[a.theme] if a.theme else rng.choice(pool_eligible("theme"))
    method = by_uid[a.method] if a.method else rng.choice(pool_eligible("method"))
    return theme, method


def first14(sheets, an, bn, texts, theme, method, words):
    spans, opening = act_lines(len(texts))
    shares = {f"act{i}_words": round(words * s / 10) * 10 for i, s in enumerate(ACT_SHARE, 1)}
    values = dict(premise=open(os.path.join(TEMPLATES, PREMISE_OF.get(VERSION, V14["premise"])), encoding="utf-8").read().strip(),
                  characters=sheets, player=an, match=bn, improvisations=len(texts),
                  act1_span=spans[0], act2_span=spans[1], act3_span=spans[2], theme=pool_render(theme, VERSION in STANDALONE),
                  method=pool_render(method, VERSION in STANDALONE), words=words, max_words=round(words * 1.1),
                  first=opening[0] + "\n\n" + texts[0], **shares)
    return fill_template(open(os.path.join(TEMPLATES, TEMPLATE_OF.get(VERSION, V14["template"])), encoding="utf-8").read(),
                         values).strip() + "\n", opening


def call_claude_session(prompt, session, first, cwd):
    """One turn of a print-mode Claude CLI session on the subscription login: no tools, no settings
    files, a neutral working directory that holds the session; the first turn starts it with a given
    id, the others resume it."""
    import subprocess
    cmd = ["claude", "-p", "--model", V14["model"], "--output-format", "json", "--tools", "", "--setting-sources", "",
           "--system-prompt", V14["system"]] + (["--session-id", session] if first else ["--resume", session])
    started = time.time()
    res = subprocess.run(cmd, input=prompt, capture_output=True, text=True, cwd=cwd, env=tl.model_free_env(), timeout=1800)
    if res.returncode != 0:
        raise RuntimeError(f"claude exited {res.returncode}: {res.stderr[-1000:]}{res.stdout[-1000:]}")
    data = json.loads(res.stdout)
    if data.get("is_error"):
        raise RuntimeError("claude reported an error: " + str(data.get("result"))[:1000])
    return data, round(time.time() - started, 1)


def split_lines(text, line_re):
    """(chat lines, stray lines) from an answer: "Name: message" lines, and a line without a name right after
    one is another message from the same character (writers sometimes send a second message unnamed)."""
    lines, stray, who = [], [], None
    for ln in text.splitlines():
        mm = line_re.match(ln)
        if mm:
            who = mm.group(1)
            lines.append(f"{who}: {mm.group(2)}")
        elif not ln.strip():
            who = None if not lines else who
        elif who:
            lines.append(f"{who}: {ln.strip()}")
        else:
            stray.append(ln.strip())
    return lines, stray


def characters17(a, x, y, ds, premise):
    """v17: the two characters by Claude with no persona, from Pinder's premise and the game's notes (v10's task),
    written once into --out's characters/, or taken from --from's when it has them. The chat gets them without
    the take on the pair ("The two of them")."""
    import shutil, tempfile, uuid
    name = f"{NEUTRAL_ID}.full.md"
    for d in (a.src, a.out):
        if d and os.path.exists(os.path.join(d, "characters", name)):
            return sheets_of(os.path.join(d, "characters", name))
    items = {it["id"]: it for it in json.load(open(os.path.join(REPO, "data/items/starter-items.json"), encoding="utf-8"))}
    prompt = CHARS17.format(own=premise, a=ds[x]["name"], b=ds[y]["name"],
                            a_pron=ds[x].get("gender_identity", "they/them"), b_pron=ds[y].get("gender_identity", "they/them"),
                            a_notes=tidbits_loose(ds[x], items, "characters"), b_notes=tidbits_loose(ds[y], items, "characters"))
    session, cwd = str(uuid.uuid4()), tempfile.mkdtemp(prefix="tonelab-cwd-")
    try:
        data, seconds = call_claude_session(prompt, session, True, cwd)
    finally:
        shutil.rmtree(cwd, ignore_errors=True)
    text = (data.get("result") or "").strip()
    if f"## {ds[x]['name']}" not in text or f"## {ds[y]['name']}" not in text:
        raise SystemExit(f"characters: the answer has no section for each character:\n{text[:1500]}")
    folder = os.path.join(a.out, "characters")
    os.makedirs(folder, exist_ok=True)
    with open(os.path.join(folder, name), "w", encoding="utf-8") as f:
        f.write(text + "\n")
    with open(os.path.join(folder, f"{NEUTRAL_ID}.prompt.md"), "w", encoding="utf-8") as f:
        f.write(f"<!-- system prompt -->\n{V14['system']}\n\n<!-- user prompt -->\n{prompt}\n\n<!-- answer -->\n{text}\n")
    with open(os.path.join(folder, f"{NEUTRAL_ID}.json"), "w", encoding="utf-8") as f:
        json.dump({"by": NEUTRAL_NAME, "model": V14["model"], "system": V14["system"], "session": session, "seconds": seconds,
                   "cost": data.get("total_cost_usd"), "notes_seed": f"{SEED}-characters"}, f, indent=1)
    print(f"characters: written by {NEUTRAL_NAME}, {len(text.split())} words, {seconds}s", flush=True)
    return sheets_of(os.path.join(folder, name))


def turns14(a, chosen):
    """v14: per comedian, their two character descriptions on the frozen prompt, played improvisation by
    improvisation by Claude in one session. Every comedian's chat gets the same theme, method and seed."""
    import shutil, tempfile, uuid
    x, y = a.turns.split(",")
    ds = {s: json.load(open(os.path.join(REPO, "data/characters", f"{s}.json"), encoding="utf-8")) for s in (x, y)}
    an, bn = ds[x]["name"], ds[y]["name"]
    mech = json.load(open(a.mechanics, encoding="utf-8"))
    if VERSION in BALANCED:  # the tone lab's rule decides how many improvisations there are and how it ends
        steps, ending, why = play15(mech)
        score, date = [(p, b) for p, b, _ in steps], ending == "date"
    else:
        score, date = tally(mech)
    if VERSION == "v14" and date != (mech.get("outcome") == "DateSecured"):  # the improvisations tell the game's ending
        raise SystemExit(f"{a.mechanics}: the balance ends at {score[-1][1]} ({'a date' if date else 'no date'}), "
                         f"but the game ended {mech.get('outcome')}; the last improvisation would tell the other ending")
    _, texts = texts_for(mech, an, ds[x].get("gender_identity", "they/them"), bn, ds[y].get("gender_identity", "they/them"))
    theme, method = draw14(a)
    premise = open(os.path.join(TEMPLATES, PREMISE_OF.get(VERSION, V14["premise"])), encoding="utf-8").read().strip()
    if VERSION in NEUTRAL:
        chosen = [(NEUTRAL_ID, NEUTRAL_NAME)]
        sheets = {NEUTRAL_ID: characters17(a, x, y, ds, premise)}
    else:
        sheets = {w: sheets_of(os.path.join(a.src, "characters", f"{w}.md")) for w, _ in chosen}
    label = (lambda n: n) if VERSION in NEUTRAL else (lambda n: f"{n}'s characters")
    if a.print_turns:
        prompt, opening = first14(sheets[chosen[0][0]], an, bn, texts, theme, method, a.words)
        print(prompt)
        for i, tx in enumerate(texts[1:], 2):
            print(PROMPTS[VERSION]["turn"].format(n=i, turn=opening[i - 1] + "\n\n" + tx))
        return True
    for folder in ("brief", "characters"):
        os.makedirs(os.path.join(a.out, folder), exist_ok=True)
    for w, _ in chosen:
        with open(os.path.join(a.out, "brief", f"{w}.md"), "w", encoding="utf-8") as f:
            f.write(premise + "\n")
        with open(os.path.join(a.out, "characters", f"{w}.md"), "w", encoding="utf-8") as f:
            f.write(sheets[w] + "\n")
    shutil.copy(a.mechanics, os.path.join(a.out, "mechanics.json"))
    out = os.path.join(a.out, f"chat-{x}-{y}")
    os.makedirs(out, exist_ok=True)
    line_re = re.compile(rf"^\s*\**\s*`?({re.escape(an)}|{re.escape(bn)})\s*\**\s*:\s*(.*?\S)`?\s*$")
    results = {}

    def run(w, n):
        prompt0, opening = first14(sheets[w], an, bn, texts, theme, method, a.words)
        session, cwd = str(uuid.uuid4()), tempfile.mkdtemp(prefix="tonelab-cwd-")
        log, chat, cost, secs = [], [], 0.0, 0.0
        for i, tx in enumerate(texts, 1):
            prompt = prompt0 if i == 1 else PROMPTS[VERSION]["turn"].format(n=i, turn=opening[i - 1] + "\n\n" + tx)
            try:
                data, seconds = call_claude_session(prompt, session, i == 1, cwd)
            except Exception as e:  # no retry: the session may already hold the turn
                results[w] = {"writer": label(n), "author": "Claude", "sheets_by": n, "session": session,
                              "error": f"improvisation {i}: {e}", "turns_done": i - 1}
                print(f"turns {w}: improvisation {i} failed: {e}", flush=True)
                break
            session = data.get("session_id") or session
            text = (data.get("result") or "").strip()
            cost += data.get("total_cost_usd") or 0
            secs += seconds
            lines, stray = split_lines(text, line_re)
            log.append({"turn": i, "prompt": prompt, "answer": text, "lines": lines, "stray": stray, "seconds": seconds,
                        "cost": data.get("total_cost_usd"), "model": list((data.get("modelUsage") or {}).keys()),
                        "from_a": sum(l.startswith(an + ":") for l in lines), "from_b": sum(l.startswith(bn + ":") for l in lines)})
            if not lines:
                results[w] = {"writer": label(n), "author": "Claude", "sheets_by": n, "session": session,
                              "error": f"improvisation {i}: no chat lines in the answer", "turns_done": i - 1}
                print(f"turns {w}: improvisation {i} gave no chat lines", flush=True)
                break
            chat += lines
            print(f"turns {w}: improvisation {i}, {len(lines)} lines, {len(' '.join(lines).split())} words, {seconds}s", flush=True)
        else:
            results[w] = {"writer": label(n), "author": "Claude", "sheets_by": n, "model": V14["model"],
                          "session": session, "seconds": round(secs, 1), "cost": round(cost, 4),
                          "words": len(" ".join(l.split(":", 1)[1] for l in chat).split()), "turns": len(log)}
        shutil.rmtree(cwd, ignore_errors=True)
        with open(os.path.join(out, f"{w}.turns.json"), "w", encoding="utf-8") as f:
            json.dump({"writer": label(n), "system": V14["system"], "session": session, "turns": log}, f, indent=1)
        with open(os.path.join(out, f"{w}.prompt.md"), "w", encoding="utf-8") as f:
            f.write(f"<!-- system prompt -->\n{V14['system']}\n\n" + "\n\n".join(
                f"<!-- user prompt, improvisation {e['turn']} -->\n{e['prompt']}\n\n<!-- answer, improvisation {e['turn']} -->\n{e['answer']}"
                for e in log))
        if "error" not in results[w]:
            with open(os.path.join(out, f"{w}.md"), "w", encoding="utf-8") as f:
                f.write("\n".join(chat) + "\n")
            print(f"turns {w}: done, {results[w]['words']} words, {results[w]['seconds']}s, ${results[w]['cost']}", flush=True)

    threads = [threading.Thread(target=run, args=c) for c in chosen]
    for th in threads:
        th.start()
        time.sleep(1)
    for th in threads:
        th.join()
    spans, _ = act_lines(len(texts))
    with open(os.path.join(out, "run.json"), "w", encoding="utf-8") as f:
        json.dump({"model": V14["model"], "runtime": "claude CLI, subscription login", "version": VERSION,
                   "system": V14["system"], "template": TEMPLATE_OF.get(VERSION, V14["template"]), "premise": PREMISE_OF.get(VERSION, V14["premise"]),
                   "from": a.src, "characters_by": NEUTRAL_NAME if VERSION in NEUTRAL else "each writer",
                   "words": a.words, "acts": spans,
                   "draw": {"theme": [theme["uid"], theme["name"]], "method": [method["uid"], method["name"]],
                            "theme_text": pool_render(theme, VERSION in STANDALONE), "method_text": pool_render(method, VERSION in STANDALONE)},
                   "mechanics": {k: v for k, v in mech.items() if k != "turns"},
                   "tally": dict({"points": [p for p, _ in score], "balance": [b for _, b in score], "date": date},
                                 **({"played": len(steps), "ending": ending, "why": why,
                                     "runs": [r for _, _, r in steps]} if VERSION in BALANCED else {})),
                   "writers": results}, f, indent=1)
    return not any("error" in r for r in results.values())


def _pick(table, n):
    return next(s for lo, hi, s in table if lo <= n <= hi)


def turn_texts(m, a_name, a_pron, b_name, b_pron):
    """(frame, [turn text, ...]) for a pinder-mechanics/1 record, from the pieces alone."""
    pa, pb = PRON.get(a_pron, PRON["they/them"]), PRON.get(b_pron, PRON["they/them"])
    f = dict(A=a_name, B=b_name, **{f"a_{k}": v for k, v in pa.items()}, **{f"b_{k}": v for k, v in pb.items()},
             A_He=pa["he"].capitalize(), B_He=pb["he"].capitalize())
    fill = lambda s, **kw: s.format(**f, **kw)
    band = lambda n: fill(_pick(P_BAND, n))
    clock = m["clock"][11:16]
    frame = fill(P_FRAME)
    out, stage = [], {}
    for k, tn in enumerate(m["turns"]):
        r, stat = tn["roll"], tn["chosen_stat"]
        sname = STAT_NAME.get(stat, stat)
        if r["nat20"]:
            grade = "nat20"
        elif r["nat1"]:
            grade = "nat1"
        elif r["success"]:
            d = r["total"] - r["dc"]
            grade = "exceptional" if d >= 15 else "critical" if d >= 10 else "strong" if d >= 5 else "success"
        else:
            grade = r["final_tier"]
            if grade not in P_GRADE:
                raise ValueError(f"turn {tn['turn']}: no piece for the result {grade}")
        g = fill(P_GRADE[grade])
        means = fill(P_STAT[stat])
        if r["nat20"] or r["nat1"]:
            ps = [fill(P_ROLL_NAT, stat=sname, means=means, nat=20 if r["nat20"] else 1, grade=g)]
        else:
            d = r["total"] - r["dc"]
            by = f"beating it by {d}" if d > 0 else "matching it exactly" if d == 0 else f"missing it by {-d}"
            ps = [fill(P_ROLL, stat=sname, means=means, total=r["total"], dc=r["dc"], by=by, grade=g)]
        if not r["success"]:
            sh = SHADOW_OF[stat]
            ps.append(fill(P_MISS, stat=sname, shadow=sh, means=fill(P_SHADOW[sh])))
        after = [x.lower() for x in tn.get("active_traps_after") or []]
        if r.get("activated_trap"):
            trap = r["activated_trap"].lower()
            stage[trap] = 1
            ps.append(fill(P_TRAP, trap=trap.capitalize(), acting=fill(P_TRAP_ACT[trap])) + " " + fill(P_TRAP_STAGE[1]))
        for trap in [x.lower() for x in tn.get("active_traps_before") or []]:
            if trap == (r.get("activated_trap") or "").lower():
                continue
            stage[trap] = stage.get(trap, 1) + 1
            st = 3 if trap not in after else min(stage[trap], 2)
            ps.append(fill(P_TRAP_ON, trap=trap.capitalize(), acting=fill(P_TRAP_ACT[trap])) + " " + fill(P_TRAP_STAGE[st]))
        s = tn.get("shadow") or {}
        if s.get("miss"):
            ps.append(fill(P_SHADOW_CHECK, shadow=s["shadow"], means=fill(P_SHADOW[s["shadow"]])))
        h = tn.get("horniness") or {}
        if h.get("miss"):
            rec = P_HORNY_HIGH if tn["interest_before"] >= HORNY_HIGH_FROM else P_HORNY_LOW
            ps.append(fill(P_HORNY, tier=P_HORNY_TIER[h["tier"]], reception=fill(rec)))
        if (tn.get("steering") or {}).get("succeeded"):
            ps.append(fill(P_STEER))
        if tn.get("combo"):
            if tn["combo"] not in P_COMBO:
                raise ValueError(f"turn {tn['turn']}: no piece for the combo {tn['combo']}")
            ps.append(fill(P_COMBO[tn["combo"]]))
        x, y = tn["interest_before"], tn["interest_after"]
        ps.append(fill(P_FLAT, x=x, band=band(y)) if x == y else
                  fill(P_SWING, x=x, y=y, swing=_pick(P_SWINGS, y - x), band=band(y)))
        ps.append(fill(P_CLOSES[VERSION]))
        if k == 0:
            ps = [fill(P_TIME, clock=clock, heat=_pick(P_HEAT, m["session_horniness"])),
                  fill(P_START, n=tn["interest_before"], band=band(tn["interest_before"]))] + ps
        out.append("\n\n".join(ps))
    return frame, out


def split_joke(text):
    """("joke", "chat") from an answer with a "## The joke" and a "## The chat" heading, else None."""
    m = re.search(r"^(?:#{1,4}\s*|\*\*)The chat(?:\*\*)?:?\s*$", text, re.I | re.M)
    if not m:
        return None
    joke = re.sub(r"^\s*(?:#{1,4}\s*|\*\*)The (?:joke|bit)(?:\*\*)?:?\s*\n", "", text[:m.start()], flags=re.I).strip()
    chat = text[m.end():].strip()
    return (joke, chat) if joke and chat else None  # versions that give the fragments under headings instead of one shuffled list  # whether the game's texting-style fragments are in the list
VERSION = os.environ.get("TONELAB_WRITERS_VERSION", "v17")
SYSTEM, USER, CHAT = (PROMPTS[VERSION][k] for k in ("system", "user", "chat"))

OBJ = {"she/her": "her", "he/him": "him", "they/them": "them"}
SUBJ = {"she/her": "she", "he/him": "he", "they/them": "they"}
POSS = {"she/her": "her", "he/him": "his", "they/them": "their"}


def system_for(wid, name):
    return SYSTEM.format(name=name, portrait=PORTRAITS.get(wid, "").strip())


def tidbits_grouped(d, items):
    """The game's fragments under headings, each group shuffled with the seed; no texting fragments."""
    pron = d.get("gender_identity", "they/them")
    s, o, p = SUBJ.get(pron, "they"), OBJ.get(pron, "them"), POSS.get(pron, "their")
    rnd = random.Random(SEED)
    who, past, claims, fear = [], [], [], []
    for iid in d["items"]:
        it = items[iid]
        if (it.get("personality_fragment") or "").strip():
            who.append(it["personality_fragment"].strip())
        if (it.get("backstory_fragment") or "").strip():
            past.append(it["backstory_fragment"].strip())
    for cat, v in d["backstory_categories"].items():
        if cat == "physical_dysmorphia":
            continue
        claims.append(f"{s.capitalize()} says: {v['bio_lie'].strip()}  \n  In fact: {v['tragic_reality'].strip()}")
    for line in d.get("psychological_stake", "").splitlines():
        line = line.strip().lstrip("- ").strip()
        if line and "about my body" not in line:
            fear.append(line)
    fear += [x.strip() for x in d.get("stake_lines", []) if x.strip()]
    out = [f"## What {p} profile says\n\n{d['bio'].strip()}"]
    for head, xs in ((f"Who {s} is", who), (f"What happened to {o}", past),
                     (f"What {s} claims, and what's actually true", claims), (f"What {s} keeps to {o}self", fear)):
        if xs:
            rnd.shuffle(xs)
            out.append(f"## {head}\n\n" + "\n".join("- " + x for x in xs))
    return "\n\n".join(out)


def brief_full():
    y = yaml.safe_load(open(os.path.join(REPO, "tools/tonelab/variants/live/situations.yaml"), encoding="utf-8"))
    return y["prompts"]["bible"].strip()


def brief_short():
    """The brief's what and why only: the opening, "What Pinder is" and "What the reader should feel".
    Everything from "How the reader comes to feel it" on (the how, the medium, how they write, your
    job) is cut, and so are the opening's promise of the how and the reading time."""
    b = brief_full()
    b = b[:b.index("\n## How the reader comes to feel it")].rstrip()
    for a, c in (("This brief tells you what Pinder is, what we want a reader to feel, and how a chat makes them feel it.",
                  "This brief tells you what Pinder is and what we want a reader to feel."),
                 ("Someone reads a whole chat on their phone in about five minutes. In that time they get to know two people.",
                  "Someone reads a whole chat on their phone. In that time they get to know two people.")):
        assert b.count(a) == 1, a
        b = b.replace(a, c)
    return b


def brief():
    return brief_short() if VERSION in SHORT_BRIEF else brief_full()


def tidbits_loose(d, items, wid):
    """The game's notes as one list with no headings, shuffled with a seed of the writer's own: the app
    profile, personality and backstory fragments, each claim and each truth as separate notes, and the
    fears. No texting fragments and no physique."""
    pron = d.get("gender_identity", "they/them")
    p = POSS.get(pron, "their")
    out = [f"{p.capitalize()} profile on the app: {d['bio'].strip()}"]
    for iid in d["items"]:
        it = items[iid]
        for k in ("personality_fragment", "backstory_fragment"):
            if (it.get(k) or "").strip():
                out.append(it[k].strip())
    for cat, v in d["backstory_categories"].items():
        if cat == "physical_dysmorphia":
            continue
        out += [v["bio_lie"].strip(), v["tragic_reality"].strip()]
    for line in d.get("psychological_stake", "").splitlines():
        line = line.strip().lstrip("- ").strip()
        if line and "about my body" not in line:
            out.append(line)
    out += [x.strip() for x in d.get("stake_lines", []) if x.strip()]
    random.Random(f"{SEED}-{wid}").shuffle(out)
    return "\n".join("- " + x for x in out)


def tidbits(d, items, texting=None):
    if texting is None and VERSION in GROUPED:
        return tidbits_grouped(d, items)
    if texting is None:
        texting = TEXTING[VERSION]
    out = [f"Profile bio: {d['bio']}"]
    for iid in d["items"]:
        it = items[iid]
        for k in ("personality_fragment", "backstory_fragment"):
            if it.get(k):
                out.append(it[k].strip())
        t = (it.get("texting_style_fragment") or "").replace("SYNTAX:", "").strip() if texting else ""
        for line in t.splitlines():
            line = line.strip().lstrip("- ").strip()
            if line:
                out.append("Texting: " + line.split(":", 1)[-1].strip())
    for cat, v in d["backstory_categories"].items():
        if cat == "physical_dysmorphia":
            continue
        out.append(f"Claims: {v['bio_lie']} In fact: {v['tragic_reality']}")
    for line in d.get("psychological_stake", "").splitlines():
        line = line.strip().lstrip("- ").strip()
        if line and "about my body" not in line:
            out.append(line)
    out += [s.strip() for s in d.get("stake_lines", []) if s.strip()]
    random.Random(SEED).shuffle(out)
    return "\n".join("- " + t for t in out)


def fan_out(jobs, key, out, label):
    """jobs: [(writer id, writer name, user prompt)]. One call per writer, in parallel."""
    os.makedirs(out, exist_ok=True)
    results = {}

    def run(wid, name, user):
        try:
            data, seconds = tl.call_openrouter(system_for(wid, name), user, MODEL, key,
                                               {"reasoning": {"effort": "high"}})
        except (SystemExit, Exception) as e:
            results[wid] = {"writer": name, "error": str(e)}
            print(f"{label} {wid}: failed: {e}", flush=True)
            return
        msg = ((data.get("choices") or [{}])[0].get("message") or {})
        text = (msg.get("content") or "").strip()
        usage = data.get("usage") or {}
        results[wid] = {"writer": name, "model": data.get("model"), "seconds": seconds,
                        "cost": usage.get("cost"), "usage": usage, "words": len(text.split())}
        if label == "chat" and VERSION in JOKE_FIRST:
            with open(os.path.join(out, f"{wid}.raw.md"), "w", encoding="utf-8") as f:
                f.write(text + "\n")
            parts = split_joke(text)
            if not parts:
                results[wid]["error"] = "no \"## The joke\" and \"## The chat\" headings in the answer (kept in .raw.md)"
                print(f"{label} {wid}: no bit/chat headings; answer kept in {wid}.raw.md", flush=True)
                return
            joke, text = parts
            results[wid].update(words=len(text.split()), joke_words=len(joke.split()))
            lines = [ln.split(":", 1)[1].strip() for ln in text.splitlines() if ":" in ln]
            if lines and VERSION == "v8":  # only v8 states its punchline word for word
                norm = lambda s: re.sub(r"[^a-z0-9]+", " ", s.lower()).strip()
                results[wid]["punchline_is_last"] = norm(lines[-1]) in norm(joke)
            with open(os.path.join(out, f"{wid}.joke.md"), "w", encoding="utf-8") as f:
                f.write(joke + "\n")
        with open(os.path.join(out, f"{wid}.md"), "w", encoding="utf-8") as f:
            f.write(text + "\n")
        with open(os.path.join(out, f"{wid}.prompt.md"), "w", encoding="utf-8") as f:
            f.write(f"<!-- system prompt -->\n{system_for(wid, name)}\n\n<!-- user prompt -->\n{user}")
        print(f"{label} {wid}: {len(text.split())} words, {seconds}s, ${usage.get('cost')}"
              + (f", joke {results[wid]['joke_words']} words" if "joke_words" in results[wid] else ""), flush=True)

    threads = [threading.Thread(target=run, args=j) for j in jobs]
    for t in threads:
        t.start()
        time.sleep(1)
    for t in threads:
        t.join()
    for wid, r in results.items():
        if "error" not in r and not (r.get("usage") or {}).get("completion_tokens"):
            r["error"] = "no completion tokens reported: the response was cut off"
            print(f"{label} {wid}: cut off ({r['words']} words, no usage)", flush=True)
    path = os.path.join(out, "run.json")
    old = json.load(open(path, encoding="utf-8")).get("writers", {}) if os.path.exists(path) else {}
    old.update(results)
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"model": MODEL, "seed": SEED, "reasoning": "high", "version": VERSION, "system": SYSTEM,
                   "portraits": {w: PORTRAITS[w] for w in results if w in PORTRAITS} or None,
                   "writers": old}, f, indent=1)
    return not any("error" in r for r in results.values())


def pair(a, chosen, key):
    """v10: per writer, their own brief, then both characters in one session, then the chat."""
    x, y = a.pair.split(",")
    items = {it["id"]: it for it in json.load(open(os.path.join(REPO, "data/items/starter-items.json"), encoding="utf-8"))}
    ds = {s: json.load(open(os.path.join(REPO, "data/characters", f"{s}.json"), encoding="utf-8")) for s in (x, y)}
    P = PROMPTS[VERSION]
    ok = fan_out([(w, n, P["brief_task"].format(brief=brief())) for w, n in chosen], key, os.path.join(a.out, "brief"), "brief")

    def done(folder, w):
        p = os.path.join(a.out, folder, f"{w}.md")
        return open(p, encoding="utf-8").read().strip() if os.path.exists(p) else None

    jobs = []
    for w, n in chosen:
        own = done("brief", w)
        if not own:
            print(f"characters {w}: no brief of their own, skipped", flush=True)
            ok = False
            continue
        jobs.append((w, n, P["user"].format(own=own, a=ds[x]["name"], b=ds[y]["name"],
                                            a_pron=ds[x].get("gender_identity", "they/them"),
                                            b_pron=ds[y].get("gender_identity", "they/them"),
                                            a_notes=tidbits_loose(ds[x], items, w), b_notes=tidbits_loose(ds[y], items, w))))
    ok = fan_out(jobs, key, os.path.join(a.out, "characters"), "characters") and ok
    jobs = []
    for w, n in chosen:
        own, chars = done("brief", w), done("characters", w)
        if not (own and chars):
            print(f"chat {w}: missing their brief or characters, skipped", flush=True)
            ok = False
            continue
        jobs.append((w, n, P["chat"].format(own=own, characters=chars, a=ds[x]["name"], b=ds[y]["name"], words=a.words)))
    return fan_out(jobs, key, os.path.join(a.out, f"chat-{x}-{y}"), "chat") and ok


def turns(a, chosen, key):
    """v11: per writer, one session: their own brief and characters (from --from), then the chat turn by
    turn, each turn assembled from the pieces by the seed's mechanics. The writer answers each turn with
    the player's message and the match's reply; the chat is their answers' lines, in order."""
    import shutil
    x, y = a.turns.split(",")
    ds = {s: json.load(open(os.path.join(REPO, "data/characters", f"{s}.json"), encoding="utf-8")) for s in (x, y)}
    an, bn = ds[x]["name"], ds[y]["name"]
    mech = json.load(open(a.mechanics, encoding="utf-8"))
    frame, texts = texts_for(mech, an, ds[x].get("gender_identity", "they/them"), bn, ds[y].get("gender_identity", "they/them"))
    P = PROMPTS[VERSION]
    for folder in ("brief", "characters"):
        os.makedirs(os.path.join(a.out, folder), exist_ok=True)
        for w, n in chosen:
            shutil.copy(os.path.join(a.src, folder, f"{w}.md"), os.path.join(a.out, folder, f"{w}.md"))
    shutil.copy(a.mechanics, os.path.join(a.out, "mechanics.json"))
    out = os.path.join(a.out, f"chat-{x}-{y}")
    os.makedirs(out, exist_ok=True)
    line_re = re.compile(rf"^\s*\**\s*({re.escape(an)}|{re.escape(bn)})\s*\**\s*:\s*(.*\S)\s*$")
    results = {}

    def run(w, n):
        rd = lambda f: open(os.path.join(a.out, f, f"{w}.md"), encoding="utf-8").read().strip()
        own, chars = rd("brief"), rd("characters")
        history, log, chat, cost, secs = [], [], [], 0.0, 0.0
        for i, tx in enumerate(texts, 1):
            prompt = (P["chat"].format(own=own, characters=chars, a=an, b=bn, turns=len(texts), words=a.words, frame=frame, turn=tx)
                      if i == 1 else P["turn"].format(n=i, turn=tx))
            try:
                data, seconds = tl.call_openrouter(system_for(w, n), prompt, MODEL, key, {"reasoning": {"effort": "high"}}, history)
            except (SystemExit, Exception) as e:
                results[w] = {"writer": n, "error": f"turn {i}: {e}", "turns_done": i - 1}
                print(f"turns {w}: turn {i} failed: {e}", flush=True)
                break
            msg = ((data.get("choices") or [{}])[0].get("message") or {})
            text = (msg.get("content") or "").strip()
            usage = data.get("usage") or {}
            cost += usage.get("cost") or 0
            secs += seconds
            lines, stray = [], []
            for ln in text.splitlines():
                mm = line_re.match(ln)
                if mm:
                    lines.append(f"{mm.group(1)}: {mm.group(2)}")
                elif ln.strip():
                    stray.append(ln.strip())
            log.append({"turn": i, "prompt": prompt, "answer": text, "lines": lines, "stray": stray, "seconds": seconds,
                        "cost": usage.get("cost"), "completion_tokens": usage.get("completion_tokens"),
                        "from_a": sum(l.startswith(an + ":") for l in lines), "from_b": sum(l.startswith(bn + ":") for l in lines)})
            if not usage.get("completion_tokens") or not lines:
                results[w] = {"writer": n, "error": f"turn {i}: no chat lines in the answer", "turns_done": i - 1}
                print(f"turns {w}: turn {i} gave no chat lines", flush=True)
                break
            history += [{"role": "user", "content": prompt}, {"role": "assistant", "content": text}]
            chat += lines
            print(f"turns {w}: turn {i}, {len(lines)} lines, {len(' '.join(lines).split())} words, {seconds}s", flush=True)
        else:
            results[w] = {"writer": n, "model": MODEL, "seconds": round(secs, 1), "cost": round(cost, 4),
                          "words": len("\n".join(chat).split()), "turns": len(log)}
        with open(os.path.join(out, f"{w}.turns.json"), "w", encoding="utf-8") as f:
            json.dump({"writer": n, "system": system_for(w, n), "turns": log}, f, indent=1)
        with open(os.path.join(out, f"{w}.prompt.md"), "w", encoding="utf-8") as f:
            f.write(f"<!-- system prompt -->\n{system_for(w, n)}\n\n" + "\n\n".join(
                f"<!-- user prompt, turn {e['turn']} -->\n{e['prompt']}\n\n<!-- answer, turn {e['turn']} -->\n{e['answer']}" for e in log))
        if "error" not in results[w]:
            with open(os.path.join(out, f"{w}.md"), "w", encoding="utf-8") as f:
                f.write("\n".join(chat) + "\n")
            print(f"turns {w}: done, {results[w]['words']} words, {results[w]['seconds']}s, ${results[w]['cost']}", flush=True)

    threads = [threading.Thread(target=run, args=c) for c in chosen]
    for th in threads:
        th.start()
        time.sleep(1)
    for th in threads:
        th.join()
    with open(os.path.join(out, "run.json"), "w", encoding="utf-8") as f:
        json.dump({"model": MODEL, "reasoning": "high", "version": VERSION, "system": SYSTEM, "from": a.src,
                   "mechanics": {k: v for k, v in mech.items() if k != "turns"},
                   "portraits": {w: PORTRAITS[w] for w in results if w in PORTRAITS} or None, "writers": results}, f, indent=1)
    return not any("error" in r for r in results.values())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--key-stdin", action="store_true")
    ap.add_argument("--only", default="", help="write this character's profiles")
    ap.add_argument("--chat", default="", help="a,b: each writer writes a chat between their a and b")
    ap.add_argument("--pair", default="", help="a,b: v10's three sessions per writer (own brief, both characters, chat)")
    ap.add_argument("--turns", default="", help="a,b: v11's chat one turn at a time, from --from's own briefs and characters")
    ap.add_argument("--from", dest="src", default="", help="--turns: the paired run whose brief/ and characters/ to use")
    ap.add_argument("--mechanics", default="", help="--turns: the seed's mechanics.json (pinder-mechanics/1)")
    ap.add_argument("--print-turns", action="store_true", help="--turns: print the assembled turn texts and stop")
    ap.add_argument("--writers", default="")
    ap.add_argument("--words", type=int, default=500, help="the chat's word limit, for prompt versions that have one")
    ap.add_argument("--out", default=os.path.join(REPO, "docs/tone-lab/scratch/writers"))
    ap.add_argument("--theme", default="", help="v14: the pool theme for the whole chat (T01..), else drawn")
    ap.add_argument("--method", default="", help="v14: the pool method for the whole chat (M01..), else drawn")
    a = ap.parse_args()
    if a.turns and VERSION in ("v14", "v15", "v16", "v17"):  # Claude on the subscription login: no key
        if VERSION in NEUTRAL and a.writers:
            tl.die(f"writers: {VERSION} has no writers (--writers)")
        chosen = [w for w in WRITERS if not a.writers or w[0] in a.writers.split(",")]
        sys.exit(0 if turns14(a, chosen) else 1)
    if a.turns and a.print_turns:
        x, y = a.turns.split(",")
        ds = {s: json.load(open(os.path.join(REPO, "data/characters", f"{s}.json"), encoding="utf-8")) for s in (x, y)}
        frame, texts = texts_for(json.load(open(a.mechanics, encoding="utf-8")), ds[x]["name"], ds[x].get("gender_identity", "they/them"),
                                  ds[y]["name"], ds[y].get("gender_identity", "they/them"))
        print(frame)
        for i, tx in enumerate(texts, 1):
            print(f"\n# {'Improvisation' if VERSION == 'v13' else 'Turn'} {i}\n\n{tx}")
        return
    if not a.key_stdin:
        tl.die("writers: the key comes on stdin (--key-stdin)")
    key = sys.stdin.readline().strip().lstrip("﻿").strip()
    chosen = [w for w in WRITERS if not a.writers or w[0] in a.writers.split(",")]
    ok = True
    if a.turns:
        if VERSION not in TURNS:
            tl.die(f"writers: --turns needs a turn-by-turn prompt version, not {VERSION}")
        ok = turns(a, chosen, key) and ok
    if a.pair:
        if VERSION not in PAIRED:
            tl.die(f"writers: --pair needs a paired prompt version, not {VERSION}")
        ok = pair(a, chosen, key)
    if a.only:
        items = {it["id"]: it for it in json.load(open(os.path.join(REPO, "data/items/starter-items.json"), encoding="utf-8"))}
        slugs = a.only.split(",")
        done = {}

        def one(slug):
            d = json.load(open(os.path.join(REPO, "data/characters", f"{slug}.json"), encoding="utf-8"))
            pron = d.get("gender_identity", "they/them")
            fill = dict(brief=brief(), char=d["name"], pronouns=pron, obj=OBJ.get(pron, "them"),
                        subj=SUBJ.get(pron, "they"), poss=POSS.get(pron, "their"))
            if VERSION in LOOSE:  # each writer gets the notes in an order of their own
                jobs = [(w, n, USER.format(tidbits=tidbits_loose(d, items, w), **fill)) for w, n in chosen]
            else:
                user = USER.format(tidbits=tidbits(d, items), **fill)
                jobs = [(w, n, user) for w, n in chosen]
            done[slug] = fan_out(jobs, key, os.path.join(a.out, slug), slug)

        ts = [threading.Thread(target=one, args=(s,)) for s in slugs]
        for t in ts:
            t.start()
        for t in ts:
            t.join()
        ok = all(done.get(s) for s in slugs) and ok
    if a.chat:
        x, y = a.chat.split(",")
        names = {s: json.load(open(os.path.join(REPO, "data/characters", f"{s}.json"), encoding="utf-8"))["name"] for s in (x, y)}
        jobs = []
        for w, n in chosen:
            px, py = (os.path.join(a.out, s, f"{w}.md") for s in (x, y))
            if not (os.path.exists(px) and os.path.exists(py)):
                print(f"chat {w}: missing a profile, skipped", flush=True)
                ok = False
                continue
            jobs.append((w, n, CHAT.format(brief=brief(), a=names[x], b=names[y], words=a.words,
                                           a_profile=open(px, encoding="utf-8").read().strip(),
                                           b_profile=open(py, encoding="utf-8").read().strip())))
        ok = fan_out(jobs, key, os.path.join(a.out, f"chat-{x}-{y}"), "chat") and ok
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
