# LESSONS_LEARNED.md

PLAYER-PROFILE-IS-AUTHORIAL-CONTEXT:
> Context windows are shared by the LLM, not by the characters. Any time character A's profile is included in character B's prompt, an explicit guard MUST tell B what they know vs. don't know.
>
> Symptom: opponent LLM "knows" facts about the player that the player never typed in conversation (named ex's name, stake-only details, motivations only present in the assembled system prompt).
>
> Rule: per LLM surface where another character's data is in scope, include a CONTEXT BOUNDARY block telling the model what's authorial-context vs. character-knowledge.
>
> Detection: any time you add a new "share character data with the other character's prompt path" feature, audit for a corresponding boundary guard.
>
> Discovered in: #870 (2026-05-14).
