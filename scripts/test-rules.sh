#!/bin/bash
# Run Rules pipeline tests (requires ANTHROPIC_API_KEY for LLM diff check)
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --filter "Category=Rules" "$@"
