# LLM Model Selection for Satirical Comedy & Hookup Dialogue

This document details the model evaluation and recommendations for authoring and running Pinder's satirical, bawdy, and cynical dating app content.

---

## 1. Required Model Capabilities for Pinder

Generating authentic, funny, and unsterilized hookup-app dialogue requires specific LLM strengths:
- **Sensitivity to Subtext & Bathos:** Grasping the comedic collapse between high-status posturing and humiliating physical/emotional reality.
- **Modern Dating App Vernacular:** Fluidity with lowercase deadpan, dry sarcasm, petty insults, and unhinged late-night texting.
- **Uncensored Adult Humor & Bawdiness:** Willingness to engage with horniness, physical insecurity, awkward anatomy, and messy dating friction without corporate HR sanitization or unsolicited moral lecturing.

---

## 2. Model Tier Rankings

### Tier 1: Gold Standards for Subtext, Timing & Banter

#### Anthropic Claude (Sonnet 3.7 / 3.5 / 4.5)
- **Strengths:** Undisputed industry benchmark for comedic timing, emotional nuance, and sharp dialogue. Masters the tragicomic gap between what a character claims and who they actually are.
- **Best For:** Content authoring, character synthesis pipeline, Datee system prompt evaluation, and golden dialogue reference generation.

#### Mistral Large 2 / Command R+
- **Strengths:** Exceptionally loose safety filters for adult humor, cynicism, and raw sexual banter. Natural European snark and deadpan delivery.
- **Best For:** Raw, chaotic characters, aggressive sparring, and unfiltered dating app exchanges.

---

### Tier 2: High-Performance Open-Weights & Fast Creative Engines

#### DeepSeek (V3 / V4 Flash / Pro)
- **Strengths:** Renowned in the open-weight RP community for unpredictable, high-energy, and creative dialogue risks. Extremely cost-efficient with zero corporate sterilization.
- **Best For:** High-volume content generation and fast runtime turn orchestration.

#### Google Gemma 4 & Meta Llama 3.3 (70B) / Llama 4 Scout
- **Strengths:** Excellent training on modern internet culture, slang, dry self-deprecation, and lowercase casual texting.
- **Best For:** Low-effort dating bios, punchy one-liners, and quick comedic interactions.

---

### Tier 3: Models to AVOID for this Genre

#### Standard OpenAI GPT-4o / GPT-4o-mini
- **Weaknesses:** Heavy "polite corporate assistant" bias. Prone to sanitizing dirty or cynical humor into wholesome sitcom clichés or inserting unsolicited therapist platitudes (*"communication is key"*).

---

## 3. Recommended Production Routing

| Pipeline Stage | Recommended Model Family | Key Characteristic |
| :--- | :--- | :--- |
| **Character Synthesis (Backstory, Stakes, Bio)** | **Claude 3.7 Sonnet / Mistral Large 2** | Deepest subtext, bathos, and character coherence. |
| **Runtime Datee Engine (Live Gameplay)** | **DeepSeek V4 Flash / Qwen 3.8 / Gemma 4** | Ultra-low latency, raw banter, and unhinged responsiveness. |
| **Batch Item / Content Generation** | **Claude 3.7 Sonnet / DeepSeek V3** | High variety, zero cliché repetition. |
