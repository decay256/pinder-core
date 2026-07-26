# Silent Fallbacks Resolution

The duplicate-property U1 finding was fixed in #1341 before integration.
`EmotionalDirectorContract` now loads JSON with
`DuplicatePropertyNameHandling.Error`, so ambiguous duplicate fields fail
semantic validation and enter the existing bounded retry path. A focused
regression covers duplicate `primary_emotion` fields.
