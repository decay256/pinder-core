# Pi C# adoption

Pinder adopts Pi C# incrementally behind its existing game-owned boundaries.
The pinned package provenance is machine-readable in
`packages/pi-csharp/provenance.json` and enforced by
`scripts/verify-pi-packages.ps1`.

## H2 boundary

`Pinder.LlmAdapters.Pi.PiLlmTransport` implements the existing `ILlmTransport` port. It maps the
already-compiled Pinder system prompt and current user document to a Pi
`Context` without changing either string, maps temperature, token limit, and
cancellation to `ModelsSimpleStreamOptions`, and returns only assistant text.
Pi error, aborted, and textless responses remain failures rather than becoming
game output.

`PinderLlmAdapter` remains the sole production `ILlmAdapter`. Prompt building,
structured-output parsing, gameplay phases, emotional direction, and semantic
retry policy remain Pinder-owned.

H2 does not register `PiLlmTransport` in a production composition root. Provider
registration and model routing move to `Pi.AI` under H3. Canonical sessions,
forks, persistence, and transcript removal begin under H4 and H5 only after the
adapter boundary is proven.

## Local package policy

The repository vendors immutable `0.1.0-alpha.1` NuGet artifacts from Pi C#
commit `30e0a044dafd3cd3b0144632cabece943f9671de`. `NuGet.Config` resolves that
version from `packages/pi-csharp` and continues to use NuGet.org for existing
third-party dependencies. Updating Pi requires replacing the artifacts,
updating every SHA-256 value and the source commit in provenance, then running:

```powershell
./scripts/verify-pi-packages.ps1
dotnet restore Pinder.Core.sln
dotnet build Pinder.Core.sln --configuration Release --no-restore
```
