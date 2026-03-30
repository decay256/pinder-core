# Spec: Issue #205 — Create Pinder.LlmAdapters Project (csproj, Newtonsoft.Json, DTOs)

## Overview

This issue creates a new .NET Standard 2.0 project, `Pinder.LlmAdapters`, that will host all concrete `ILlmAdapter` implementations. The project references `Pinder.Core` (one-way dependency) and adds `Newtonsoft.Json 13.0.3` for Anthropic API serialization. This issue delivers the project scaffold, all Anthropic API DTO classes, `AnthropicOptions`, `AnthropicApiException`, and updates the solution file. No game logic in `Pinder.Core` is modified.

## Function Signatures

### Namespace: `Pinder.LlmAdapters.Anthropic.Dto`

All DTO classes use `Newtonsoft.Json` attributes for serialization.

#### `MessagesRequest`

```csharp
namespace Pinder.LlmAdapters.Anthropic.Dto
{
    public sealed class MessagesRequest
    {
        [JsonProperty("model")]
        public string Model { get; set; }             // default: ""

        [JsonProperty("max_tokens")]
        public int MaxTokens { get; set; }             // default: 1024

        [JsonProperty("temperature")]
        public double Temperature { get; set; }        // default: 0.9

        [JsonProperty("system")]
        public ContentBlock[] System { get; set; }     // default: Array.Empty<ContentBlock>()

        [JsonProperty("messages")]
        public Message[] Messages { get; set; }        // default: Array.Empty<Message>()
    }
}
```

#### `ContentBlock`

```csharp
public sealed class ContentBlock
{
    [JsonProperty("type")]
    public string Type { get; set; }                   // default: "text"

    [JsonProperty("text")]
    public string Text { get; set; }                   // default: ""

    [JsonProperty("cache_control", NullValueHandling = NullValueHandling.Ignore)]
    public CacheControl? CacheControl { get; set; }    // default: null (omitted from JSON when null)
}
```

#### `CacheControl`

```csharp
public sealed class CacheControl
{
    [JsonProperty("type")]
    public string Type { get; set; }                   // default: "ephemeral"
}
```

#### `Message`

```csharp
public sealed class Message
{
    [JsonProperty("role")]
    public string Role { get; set; }                   // default: ""

    [JsonProperty("content")]
    public string Content { get; set; }                // default: ""
}
```

#### `MessagesResponse`

```csharp
public sealed class MessagesResponse
{
    [JsonProperty("content")]
    public ResponseContent[] Content { get; set; }     // default: Array.Empty<ResponseContent>()

    [JsonProperty("usage")]
    public UsageStats? Usage { get; set; }

    public string GetText()
    // Returns Content[0].Text if Content.Length > 0, otherwise returns ""
}
```

#### `ResponseContent`

```csharp
public sealed class ResponseContent
{
    [JsonProperty("type")]
    public string Type { get; set; }                   // default: ""

    [JsonProperty("text")]
    public string Text { get; set; }                   // default: ""
}
```

#### `UsageStats`

```csharp
public sealed class UsageStats
{
    [JsonProperty("input_tokens")]
    public int InputTokens { get; set; }

    [JsonProperty("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonProperty("cache_creation_input_tokens")]
    public int CacheCreationInputTokens { get; set; }

    [JsonProperty("cache_read_input_tokens")]
    public int CacheReadInputTokens { get; set; }
}
```

### Namespace: `Pinder.LlmAdapters.Anthropic`

#### `AnthropicApiException`

```csharp
public class AnthropicApiException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }

    public AnthropicApiException(int statusCode, string responseBody)
    // Message format: "Anthropic API error {statusCode}: {responseBody}"
}
```

#### `AnthropicOptions`

```csharp
public sealed class AnthropicOptions
{
    public string ApiKey { get; set; }                              // default: ""
    public string Model { get; set; }                               // default: "claude-sonnet-4-20250514"
    public int MaxTokens { get; set; }                              // default: 1024
    public double Temperature { get; set; }                         // default: 0.9
    public double? DialogueOptionsTemperature { get; set; }         // default: null
    public double? DeliveryTemperature { get; set; }                // default: null
    public double? OpponentResponseTemperature { get; set; }        // default: null
    public double? InterestChangeBeatTemperature { get; set; }      // default: null
}
```

The per-method temperature overrides (`DialogueOptionsTemperature`, etc.) allow fine-tuning creativity for each `ILlmAdapter` method. When null, the general `Temperature` value is used.

## Input/Output Examples

### MessagesRequest Serialization

Given a `MessagesRequest`:
```
Model = "claude-sonnet-4-20250514"
MaxTokens = 1024
Temperature = 0.9
System = [ ContentBlock { Type = "text", Text = "You are a character.", CacheControl = CacheControl { Type = "ephemeral" } } ]
Messages = [ Message { Role = "user", Content = "Generate 4 dialogue options." } ]
```

Expected JSON output:
```json
{
  "model": "claude-sonnet-4-20250514",
  "max_tokens": 1024,
  "temperature": 0.9,
  "system": [
    {
      "type": "text",
      "text": "You are a character.",
      "cache_control": { "type": "ephemeral" }
    }
  ],
  "messages": [
    { "role": "user", "content": "Generate 4 dialogue options." }
  ]
}
```

### ContentBlock with null CacheControl

When `CacheControl` is null, the `cache_control` key must be **omitted** from JSON (not serialized as `null`). This is enforced by `NullValueHandling.Ignore` on the `[JsonProperty]` attribute.

```json
{
  "type": "text",
  "text": "Hello"
}
```

### MessagesResponse Deserialization

Given API JSON:
```json
{
  "content": [
    { "type": "text", "text": "Here are your options:\n1. ..." }
  ],
  "usage": {
    "input_tokens": 1500,
    "output_tokens": 350,
    "cache_creation_input_tokens": 1200,
    "cache_read_input_tokens": 0
  }
}
```

Calling `response.GetText()` returns `"Here are your options:\n1. ..."`.

### MessagesResponse with empty content

Given:
```json
{ "content": [], "usage": null }
```

`response.GetText()` returns `""`.

### AnthropicApiException

```csharp
var ex = new AnthropicApiException(429, "{\"error\":{\"type\":\"rate_limit_error\"}}");
// ex.Message == "Anthropic API error 429: {\"error\":{\"type\":\"rate_limit_error\"}}"
// ex.StatusCode == 429
// ex.ResponseBody == "{\"error\":{\"type\":\"rate_limit_error\"}}"
```

## Acceptance Criteria

### AC1: Pinder.LlmAdapters.csproj created, references Pinder.Core and Newtonsoft.Json 13.x

The project file must be located at `src/Pinder.LlmAdapters/Pinder.LlmAdapters.csproj` with the following properties:

- `TargetFramework`: `netstandard2.0`
- `LangVersion`: `8.0`
- `Nullable`: `enable`
- `ProjectReference` to `../Pinder.Core/Pinder.Core.csproj`
- `PackageReference` to `Newtonsoft.Json` version `13.0.3`

No other dependencies are allowed. The project must not introduce any other NuGet packages.

### AC2: All DTO types created with correct [JsonProperty] attributes

The following files must exist under `src/Pinder.LlmAdapters/Anthropic/Dto/`:

| File | Classes |
|------|---------|
| `MessagesRequest.cs` | `MessagesRequest` |
| `ContentBlock.cs` | `ContentBlock`, `CacheControl`, `Message` |
| `MessagesResponse.cs` | `MessagesResponse`, `ResponseContent`, `UsageStats` |

All public properties must have `[JsonProperty("snake_case_name")]` attributes mapping to the Anthropic Messages API JSON field names as specified in the Function Signatures section.

`ContentBlock.CacheControl` must use `NullValueHandling = NullValueHandling.Ignore` so the `cache_control` key is omitted from serialized JSON when the value is null.

`MessagesResponse.GetText()` must return `Content[0].Text` when `Content.Length > 0`, or `""` when `Content` is empty.

All default values must match those specified in Function Signatures (e.g., `MaxTokens = 1024`, `Temperature = 0.9`, `Type = "text"`, etc.).

### AC3: AnthropicOptions created

File: `src/Pinder.LlmAdapters/Anthropic/AnthropicOptions.cs`

Must contain all 8 properties listed in Function Signatures with the specified defaults. The `Model` default must be `"claude-sonnet-4-20250514"`.

### AC4: AnthropicApiException created

File: `src/Pinder.LlmAdapters/Anthropic/AnthropicApiException.cs`

Must extend `System.Exception`. Constructor takes `(int statusCode, string responseBody)`. The `Message` (inherited from `Exception`) must follow the format: `"Anthropic API error {statusCode}: {responseBody}"`. Both `StatusCode` and `ResponseBody` must be exposed as read-only properties.

### AC5: Solution file updated to include new project

The `Pinder.Core.sln` solution file must include the `Pinder.LlmAdapters` project. It should be nested under the `src` solution folder, matching the existing `Pinder.Core` project organization. Running `dotnet build Pinder.Core.sln` must resolve the project correctly.

### AC6: `dotnet build` passes with 0 errors, 0 warnings

Running `dotnet build src/Pinder.LlmAdapters/Pinder.LlmAdapters.csproj` must complete with 0 errors and 0 warnings. Running `dotnet build Pinder.Core.sln` must also pass with 0 errors and 0 warnings (including existing Pinder.Core and test projects).

### AC7: Pinder.Core is NOT modified

No files under `src/Pinder.Core/` may be added, removed, or modified. No changes to `Pinder.Core.csproj`. The existing 1118+ tests must continue to pass without any modification.

## Edge Cases

### Default property values

All DTO properties use mutable setters with defaults. A freshly constructed `MessagesRequest` (no properties set) must serialize to valid JSON with all defaults populated:
```json
{
  "model": "",
  "max_tokens": 1024,
  "temperature": 0.9,
  "system": [],
  "messages": []
}
```

### Empty arrays vs null

`System` and `Messages` on `MessagesRequest` default to `Array.Empty<T>()`, not `null`. They must serialize as `[]`, not be omitted.

`Content` on `MessagesResponse` defaults to `Array.Empty<ResponseContent>()`. `GetText()` must handle this gracefully by returning `""`.

### GetText() with multiple content blocks

The Anthropic API may return multiple content blocks. `GetText()` returns only `Content[0].Text` — it does not concatenate. This is by design for this sprint; concatenation may be added later.

### UsageStats nullable

`MessagesResponse.Usage` may be null (if the API response omits it or on deserialization of incomplete responses). Consumers must check for null before accessing usage fields.

### CacheControl omission

When `ContentBlock.CacheControl` is `null`, the JSON key `cache_control` must not appear at all in the serialized output. This is critical for the Anthropic API, which may reject requests with `"cache_control": null`.

### AnthropicOptions temperature overrides

When a per-method temperature (e.g., `DialogueOptionsTemperature`) is `null`, downstream code (not in this issue) should fall back to the general `Temperature` value. The `AnthropicOptions` class itself does not implement this fallback logic — it is purely a configuration carrier.

### LangVersion 8.0 constraints

No C# 9+ features allowed: no `record` types, no `init` setters, no top-level statements. Use `sealed class` with `{ get; set; }` properties. Nullable reference types ARE enabled via `<Nullable>enable</Nullable>`.

## Error Conditions

### AnthropicApiException

- `statusCode` can be any HTTP status code (400, 401, 403, 429, 500, 529, etc.)
- `responseBody` can be any string including empty string
- The exception message format is fixed: `"Anthropic API error {statusCode}: {responseBody}"`
- This exception does not validate inputs — any int and any string are accepted

### Build errors

If `Newtonsoft.Json` version `13.0.3` is unavailable (network issue during restore), `dotnet build` will fail at the NuGet restore step. This is an environment issue, not a code issue.

### Namespace mismatches

All Anthropic DTOs must be in `Pinder.LlmAdapters.Anthropic.Dto`. `AnthropicOptions` and `AnthropicApiException` must be in `Pinder.LlmAdapters.Anthropic`. Incorrect namespaces will cause build failures in downstream issues (#206, #207, #208) that reference these types.

## Dependencies

### Upstream (this issue depends on)

- **Pinder.Core** (existing, unchanged) — provides the types referenced by `ProjectReference`. Must be buildable. No modifications required.
- **Newtonsoft.Json 13.0.3** (NuGet) — external package for JSON serialization attributes and runtime.

### Downstream (depend on this issue)

- **#206** (AnthropicClient) — uses `MessagesRequest`, `MessagesResponse`, `AnthropicApiException`, `AnthropicOptions`
- **#207** (SessionDocumentBuilder) — uses `ContentBlock`, `CacheControl`, `Message`
- **#208** (AnthropicLlmAdapter) — uses all DTOs plus `AnthropicOptions`
- **#210** (Integration test) — depends on buildable project

### File Layout

After this issue is complete, the following files must exist:

```
src/Pinder.LlmAdapters/
├── Pinder.LlmAdapters.csproj
└── Anthropic/
    ├── AnthropicApiException.cs
    ├── AnthropicOptions.cs
    └── Dto/
        ├── MessagesRequest.cs
        ├── ContentBlock.cs        (also contains CacheControl and Message)
        └── MessagesResponse.cs    (also contains ResponseContent and UsageStats)
```

The solution file `Pinder.Core.sln` must be updated to include the new project.
