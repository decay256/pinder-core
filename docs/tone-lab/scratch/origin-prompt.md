# The first chat prompt, for reference

This branch rebuilds how profiles and plans are made, from nothing. The scorecard, the runner, the
mechanics, the situation table and the dashboard are kept from `tone-lab`; the profile generator,
the plan prompts and everything they produced are not the starting point.

The starting point is the brief that opened every system prompt of the live tone lab, restored in
`brief.md`: what Pinder is, what the reader should feel, how a chat makes them feel it, the medium and
how the characters write. This file keeps the first chat prompt the tone lab sent (commit 652b7ec,
15 November 2026, variant `baseline`) for reference.

## system.md

```
You write dialogue for Pinder, an adult comedy dating game. You follow the brief exactly and return only the chat.
```

## prompt.md

```
Write a complete chat on Pinder, a dating app, between ${player_name} (the player) and ${datee_name} (the match), in one pass.

The game has already been played. The dice, the interest meter and the outcome below are fixed. Your job is to write the messages that make those results feel inevitable and funny. Do not change any result.

The goal: an adult reading this silently should laugh out loud from recognition and cringe. They recognise these people, and they cringe at the gap between what each one performs and what is true about them.

## A. The two characters

${player_card}

${datee_card}

## B. Ground rules

1. They are strangers texting. Nobody overshares early. A secret leaks only through a failed roll, or in small amounts once interest is high.
2. Every failed roll is visible in the delivered message. ${player_name} meant to send something decent and sent something worse, in the way the failure tier describes. A success lands.
3. This is texting, not stand-up. Short messages, written the way each of them would actually type. No setups and punchlines, no lines written for an audience.
4. Each character sounds only like themselves. If you swapped the names, the chat should stop making sense.
5. ${datee_name}'s replies follow the interest change of that turn. A drop shows, and so does a rise.

## C. The arc

Interest over the chat: ${interest_path} (low ${low_point}, high ${high_point}). ${outcome_line}
The chat follows that curve. While interest is below 25, ${datee_name} does not agree to meet, however well the last message landed.

## D. The turns, as the game played them

${turns}

## E. Output

Plain text only: no tables, no headings, no stage directions, no commentary. For each turn write exactly:

Turn N
${player_name}: the message as delivered
${datee_name}: the reply

After the last turn, stop.
```

## character.md (each character card)

```
### ${name} (${role}, ${pronouns})
Profile bio: "${bio}"
Stats: ${stats}

Who they are: ${personality}

What they hide (what they claim, and the truth). None of this is said early. It leaks only through failed rolls, or in small amounts once interest is high:
${secrets}

What they are quietly afraid of right now:
${stake_lines}

Their defence when it gets close: ${defence}

How they take each kind of message:
${reactions}
```

## turn.md (each turn in section D)

```
Turn ${n}. ${player_name} plays ${stat} (the options were ${options}).
Roll: ${die} + ${modifier} = ${total} against DC ${dc}. ${result}
${checks}
Interest ${interest_before} → ${interest_after} (${delta}), now ${state_after}.
```
