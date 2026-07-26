# External-Call Robustness Resolution

The native structured-response size U1 finding was fixed in #1341 before
integration. `EmotionalDirectorContract` now applies the shared 64 KiB generated
JSON input bound before either native parsing or fallback extraction. Focused
regressions cover both native structured and plain transport responses.
