#!/bin/bash
# Run Core tests only (fast, < 15s)
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --filter "Category=Core" "$@"
