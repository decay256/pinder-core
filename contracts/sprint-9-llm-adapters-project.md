# Contract: Issue #205 — Pinder.LlmAdapters Project Scaffold

## Component
New project: `src/Pinder.LlmAdapters/` — external project that depends on `Pinder.Core` and `Newtonsoft.Json`.
New test project: `tests/Pinder.LlmAdapters.Tests/` — xUnit test project for adapter tests.

## Maturity: Prototype
## NFR: latency target — build time only, no runtime code in this issue

---

## 1. Project File

**File:** `src/Pinder.LlmAdapters/Pinder.LlmAdapters.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>8.0</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>Pinder.LlmAdapters</RootNamespace>
    <AssemblyName>Pinder.LlmAdapters</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Pinder.Core\Pinder.Core.csproj" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

**Constraint:** `Pinder.Core` MUST NOT gain a reference to `Pinder.LlmAdapters` or Newtonsoft.Json. The dependency is one-way: `LlmAdapters → Core`.

## 2. Test Project

**File:** `tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.6.0" />
    <PackageReference Include="xunit" Version="2.4.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.4.5">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Pinder.LlmAdapters\Pinder.LlmAdapters.csproj" />
  </ItemGroup>
</Project>
```

**Constraint:** `Pinder.Core.Tests` MUST NOT reference `Pinder.LlmAdapters`. Adapter tests go in `Pinder.LlmAdapters.Tests`.

## 3. Solution File Updates

Add both new projects to `Pinder.sln`:
- `Pinder.LlmAdapters` under `src` solution folder
- `Pinder.LlmAdapters.Tests` under `tests` solution folder

## 4. Anthropic DTOs

**Location:** `src/Pinder.LlmAdapters/Anthropic/Dto/`

### MessagesRequest.cs
```csharp
namespace Pinder.LlmAdapters.Anthropic.Dto
{
    public sealed class MessagesRequest
    {
        [JsonProperty("model")] public string Model { get; set; } = "";
        [JsonProperty("max_tokens")] public int MaxTokens { get; set; } = 1024;
        [JsonProperty("temperature")] public double Temperature { get; set; } = 0.9;
        [JsonProperty("system")] public ContentBlock[] System { get; set; } = Array.Empty<ContentBlock>();
        [JsonProperty("messages")] public Message[] Messages { get; set; } = Array.Empty<Message>();
    }
}
```

### ContentBlock.cs
```csharp
namespace Pinder.LlmAdapters.Anthropic.Dto
{
    public sealed class ContentBlock
    {
        [JsonProperty("type")] public string Type { get; set; } = "text";
        [JsonProperty("text")] public string Text { get; set; } = "";
        [JsonProperty("cache_control", NullValueHandling = NullValueHandling.Ignore)]
        public CacheControl? CacheControl { get; set; }
    }

    public sealed class CacheControl
    {
        [JsonProperty("type")] public string Type { get; set; } = "ephemeral";
    }

    public sealed class Message
    {
        [JsonProperty("role")] public string Role { get; set; } = "";
        [JsonProperty("content")] public string Content { get; set; } = "";
    }
}
```

### MessagesResponse.cs
```csharp
namespace Pinder.LlmAdapters.Anthropic.Dto
{
    public sealed class MessagesResponse
    {
        [JsonProperty("content")] public ResponseContent[] Content { get; set; } = Array.Empty<ResponseContent>();
        [JsonProperty("usage")] public UsageStats? Usage { get; set; }
        public string GetText() => Content.Length > 0 ? Content[0].Text : "";
    }

    public sealed class ResponseContent
    {
        [JsonProperty("type")] public string Type { get; set; } = "";
        [JsonProperty("text")] public string Text { get; set; } = "";
    }

    public sealed class UsageStats
    {
        [JsonProperty("input_tokens")] public int InputTokens { get; set; }
        [JsonProperty("output_tokens")] public int OutputTokens { get; set; }
        [JsonProperty("cache_creation_input_tokens")] public int CacheCreationInputTokens { get; set; }
        [JsonProperty("cache_read_input_tokens")] public int CacheReadInputTokens { get; set; }
    }
}
```

### AnthropicApiException.cs
```csharp
namespace Pinder.LlmAdapters.Anthropic
{
    public class AnthropicApiException : Exception
    {
        public int StatusCode { get; }
        public string ResponseBody { get; }
        public AnthropicApiException(int statusCode, string responseBody)
            : base($"Anthropic API error {statusCode}: {responseBody}")
        { StatusCode = statusCode; ResponseBody = responseBody; }
    }
}
```

### AnthropicOptions.cs
```csharp
namespace Pinder.LlmAdapters.Anthropic
{
    public sealed class AnthropicOptions
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "claude-sonnet-4-20250514";
        public int MaxTokens { get; set; } = 1024;
        public double Temperature { get; set; } = 0.9;
        public double? DialogueOptionsTemperature { get; set; }
        public double? DeliveryTemperature { get; set; }
        public double? OpponentResponseTemperature { get; set; }
        public double? InterestChangeBeatTemperature { get; set; }
    }
}
```

## 5. Acceptance Verification

- `dotnet build` succeeds with 0 errors, 0 warnings
- `dotnet test` on existing Pinder.Core.Tests passes (1118+ tests)
- No modifications to any file under `src/Pinder.Core/`

## Dependencies
None (first issue in the sprint)

## Consumers
- #206 (AnthropicClient)
- #207 (SessionDocumentBuilder)
- #208 (AnthropicLlmAdapter)
