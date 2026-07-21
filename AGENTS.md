# Pinder Core Agent Guide

This repo is the backend-agnostic C# game engine for Pinder. It is consumed directly by `pinder-web` as a git submodule.

## Operating Rules

- Preserve the pure engine boundary in `src/Pinder.Core`; do not add service or web concerns there.
- Keep changes scoped to the requested mechanic, rules pipeline, adapter, or tool.
- If gameplay-affecting code, data, schemas, or templates change, check whether `Directory.Build.props` needs a strictly greater SemVer version.
- Preserve YAML/JSON data shape and avoid broad mechanical rewrites of rules or game data.
- Do not commit secrets, generated local caches, or agent scratch logs.
- If a change affects the API surface consumed by `pinder-web`, mention the required submodule bump or downstream update.

## Local Tooling

- Requires .NET 8 SDK. On this machine, prefer `C:\Users\decay\.dotnet\dotnet.exe` if plain `dotnet` resolves to SDK 7.
- Python 3.12 is available for rules tooling.
- `uv` is installed at `C:\Users\decay\AppData\Roaming\Python\Python312\Scripts\uv.exe`.

## Validation Location

Default to compiling and running tests on the Linux server or Linux VM, not on Windows. Use this Windows checkout for editing/orchestration and only run Windows-local validation when the task is specifically about a Windows toolchain, path casing, or local desktop behavior.

Expected server checkout:

```bash
cd /root/projects/pinder-core
```

SSH from Windows must use the OpenClaw key from the prior setup thread, not the default `~/.ssh/id_ed25519`:

```powershell
ssh -i C:\Users\decay\Documents\Codex\2026-07-07\to-start-coding-on-the-pinder\work\ssh\codex_temp2_ed25519 -o UserKnownHostsFile=C:\Users\decay\Documents\Codex\2026-07-07\to-start-coding-on-the-pinder\work\ssh\known_hosts root@104.248.27.154
```

For one-off remote validation from this Windows workspace, use:

```powershell
C:\Users\decay\Documents\Codex\2026-07-07\i-a\outputs\remote-validation.ps1 -Repo pinder-core -RemoteCommand "dotnet test Pinder.Core.sln"
```

Runtime/deploy checks on `pinder-prod` should tunnel through OpenClaw:

```bash
ssh root@100.68.94.112
```

## Commands

```powershell
# restore
$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet restore Pinder.Core.sln

# build
$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build Pinder.Core.sln

# all tests
$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet test Pinder.Core.sln

# focused tests
$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet test --filter "Category=Core"
$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet test --filter "Category=Rules"
$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet test --filter "Category=LlmAdapters"

# rules pipeline checks
python rules/tools/rules_pipeline.py check
uv run pytest rules/tools/test_check_version_bump.py
```

Server equivalents:

```bash
cd /root/projects/pinder-core
export PATH="$HOME/.local/bin:$PATH"
dotnet restore Pinder.Core.sln
dotnet build Pinder.Core.sln
dotnet test Pinder.Core.sln
python3 rules/tools/rules_pipeline.py check
uv run pytest rules/tools/test_check_version_bump.py
```

## Layout

- `src/Pinder.Core/`: dependency-free domain kernel.
- `src/Pinder.Rules/`: YAML rule resolution.
- `src/Pinder.LlmAdapters/`: prompt assembly and LLM adapter code.
- `session-runner/`: .NET 8 CLI harness for automated playtesting.
- `tests/`: test projects per assembly.
- `data/`: YAML and JSON game data.
- `rules/tools/`: Python rule conversion and validation tooling.

## Handoff Checklist

- Report changed engine/data/API surfaces.
- Run the narrowest relevant tests, and broaden to `dotnet test Pinder.Core.sln` for shared behavior.
- For gameplay-affecting changes, report whether the version bump check passes or why it was not relevant.
