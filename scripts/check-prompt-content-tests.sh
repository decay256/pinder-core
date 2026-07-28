#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

tmp_root="$(mktemp -d)"
trap 'rm -rf "$tmp_root"' EXIT
host_python=""
if command -v python3 >/dev/null 2>&1 && python3 - <<'PY' >/dev/null 2>&1
print("ok")
PY
then
    host_python="$(command -v python3)"
elif command -v python >/dev/null 2>&1 && python - <<'PY' >/dev/null 2>&1
print("ok")
PY
then
    host_python="$(command -v python)"
fi

make_repo() {
    local name="$1"
    local repo="$tmp_root/$name"
    mkdir -p "$repo/scripts" "$repo/src/Pinder.LlmAdapters" "$repo/data/prompts"
    cp "$REPO_ROOT/scripts/check-prompt-content.sh" "$repo/scripts/check-prompt-content.sh"
    sed -i 's/\r$//' "$repo/scripts/check-prompt-content.sh"
    chmod +x "$repo/scripts/check-prompt-content.sh"
    printf '%s\n' "$repo"
}

if [[ -n "$host_python" ]]; then
    shim_repo="$(make_repo shim)"
    cat > "$shim_repo/src/Pinder.LlmAdapters/AllowedFixture.cs" <<'CS'
namespace Pinder.LlmAdapters;

public sealed class AllowedFixture
{
    private const string PromptKey = "emotional-reaction-director";
}
CS
    fake_bin="$tmp_root/fake-bin"
    mkdir -p "$fake_bin"
    cat > "$fake_bin/python3" <<'SH'
#!/bin/sh
echo "WindowsApps python3 shim placeholder" >&2
exit 9009
SH
    cat > "$fake_bin/python" <<'SH'
#!/bin/sh
echo "WindowsApps python shim placeholder" >&2
exit 9009
SH
    cat > "$fake_bin/py" <<SH
#!/bin/sh
if [ "\$1" = "-3" ]; then
    shift
fi
exec "$host_python" "\$@"
SH
    chmod +x "$fake_bin/python3" "$fake_bin/python" "$fake_bin/py"
    (cd "$shim_repo" && PATH="$fake_bin:$PATH" bash scripts/check-prompt-content.sh > "$tmp_root/shim.out" 2>&1)
fi

bad_repo="$(make_repo bad)"
cat > "$bad_repo/src/Pinder.LlmAdapters/DirectorPerformanceFixture.cs" <<'CS'
namespace Pinder.LlmAdapters;

public sealed class DirectorPerformanceFixture
{
    public string DirectorPrompt()
    {
        string modelPrompt = "Produce one private emotional direction object for the DATEE response planner.";
        return modelPrompt;
    }

    public string PerformancePrompt(string primaryEmotion)
    {
        var wrapper = $"Use this private direction to shape the emotional movement of {primaryEmotion}.";
        return wrapper;
    }

    public string ReplyPrompt()
    {
        const string promptTemplate = "Write a reply that keeps the DATEE voice intact while showing the emotional consequence of the last player message.";
        return promptTemplate;
    }

    public string DiagnosisPrompt()
    {
        const string directorInstruction = "Given the diagnosis, biography, and current interest level, describe the DATEE's immediate emotional reaction.";
        return directorInstruction;
    }
}
CS

if (cd "$bad_repo" && bash scripts/check-prompt-content.sh > "$tmp_root/bad.out" 2>&1); then
    echo "FAIL: hardcoded director/performance prompt prose was accepted."
    cat "$tmp_root/bad.out"
    exit 1
fi

direct_sink_repo="$(make_repo direct-sink)"
cat > "$direct_sink_repo/src/Pinder.LlmAdapters/DirectCallFixture.cs" <<'CS'
namespace Pinder.LlmAdapters;

public sealed class DirectCallFixture
{
    public object Call(string tone)
    {
        return transport.SendAsync(
            "Treat the recipient's hesitation as protective rather than dismissive, and keep the subtext gentle.",
            $"Write a reply that preserves the established voice while showing {tone} through word choice and pacing.");
    }
}
CS

if (cd "$direct_sink_repo" && bash scripts/check-prompt-content.sh > "$tmp_root/direct-sink.out" 2>&1); then
    echo "FAIL: prompt prose passed directly to model transport was accepted."
    cat "$tmp_root/direct-sink.out"
    exit 1
fi

nearby_key_repo="$(make_repo nearby-key)"
cat > "$nearby_key_repo/src/Pinder.LlmAdapters/NearbyPromptKeyFixture.cs" <<'CS'
namespace Pinder.LlmAdapters;

public sealed class NearbyPromptKeyFixture
{
    private const string PromptKey = "emotional-reaction-director";
    private const string promptTemplate = "Produce one private emotional direction object for the DATEE response planner.";
}
CS

if (cd "$nearby_key_repo" && bash scripts/check-prompt-content.sh > "$tmp_root/nearby-key.out" 2>&1); then
    echo "FAIL: a nearby prompt key exempted hardcoded directive prose."
    cat "$tmp_root/nearby-key.out"
    exit 1
fi

good_repo="$(make_repo good)"
cat > "$good_repo/src/Pinder.LlmAdapters/AllowedFixture.cs" <<'CS'
namespace Pinder.LlmAdapters;

public sealed class AllowedFixture
{
    private const string PromptKey = "emotional-reaction-director";
    private const string PhaseLabel = "datee_response";
    private const string Diagnostic = "prompt-catalog: missing required runtime prompt key.";
    private const string PlayerVisibleCopy = "You received 3 XP.";

    public string RenderForUi(string name)
    {
        return $"Welcome back, {name}.";
    }

    public void LogDiagnostic(ILogger logger)
    {
        logger.LogWarning("Given the diagnosis prompt failure, the provider response could not be parsed; this message is diagnostic only.");
    }
}
CS
cat > "$good_repo/data/prompts/emotional-reactions.yaml" <<'YAML'
prompts:
  emotional-reaction-director:
    system_prompt: |-
      Produce one private emotional direction object for the DATEE response planner.
YAML

(cd "$good_repo" && bash scripts/check-prompt-content.sh > "$tmp_root/good.out" 2>&1)

echo "PASS: prompt-content gate regression fixtures behaved as expected."
