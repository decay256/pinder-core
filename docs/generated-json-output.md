# Generated JSON Output

Generated structured-output calls separate three responsibilities:

- Extraction: `Pinder.LlmAdapters.GeneratedJsonObjectExtractor` finds the first complete, syntactically valid JSON object in generated text. It owns bounded scanning, fenced/prose tolerance, escaped string braces, root-array rejection, and explicit failure codes. It does not retry, repair, validate domain fields, or fabricate fallback data.
- Validation: the call site owns the schema/contract check after extraction. For example, therapist diagnosis generation deserializes the extracted object and then validates the required cognitive-subtext fields. The private emotional director call validates its seven exact fields in `EmotionalDirectorContract` after native structured transport or local JSON extraction.
- Retry and terminal policy: `SemanticOutputRecoveryExecutor` owns retry attempts around semantic rejection. The domain caller maps the final rejection to the appropriate exception and diagnostic text.

Use the extractor when a model may return prose or Markdown around a JSON object. If a provider already guarantees a typed object, prefer that provider contract and keep this helper out of the provider transport layer.
