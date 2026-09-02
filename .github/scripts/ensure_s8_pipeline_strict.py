from __future__ import annotations

from pathlib import Path

PATH = Path(__file__).with_name("run_s8_candidate_pipeline.ps1")


def replace_once(text: str, before: str, after: str, label: str) -> str:
    if after in text:
        return text
    if before not in text:
        raise RuntimeError(f"Cannot harden S8 pipeline: missing {label} anchor")
    return text.replace(before, after, 1)


def main() -> int:
    text = PATH.read_text(encoding="utf-8")
    text = replace_once(
        text,
        "$ErrorActionPreference = 'Stop'\n",
        "$ErrorActionPreference = 'Stop'\n$PSNativeCommandUseErrorActionPreference = $true\n",
        "native-command preference",
    )
    old_commit = '''function Commit-IfNeeded([string]$Message) {
    git diff --cached --check
    Assert-LastCommand 'git diff --cached --check failed'
    git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        git commit -m $Message
        Assert-LastCommand "Failed to commit: $Message"
        return $true
    }
    return $false
}
'''
    new_commit = '''function Commit-IfNeeded([string]$Message) {
    git diff --cached --check
    $savedNativePreference = $PSNativeCommandUseErrorActionPreference
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        git diff --cached --quiet
        $diffExit = $LASTEXITCODE
    }
    finally {
        $PSNativeCommandUseErrorActionPreference = $savedNativePreference
    }
    if ($diffExit -ne 0) {
        git commit -m $Message
        return $true
    }
    return $false
}
'''
    text = replace_once(text, old_commit, new_commit, "Commit-IfNeeded")
    old_push = '''    git push origin "HEAD:$Branch"
    if ($LASTEXITCODE -eq 0) {
        $Pushed = $true
    }
    else {
        Start-Sleep -Seconds (2 * $attempt)
    }
'''
    new_push = '''    $savedNativePreference = $PSNativeCommandUseErrorActionPreference
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        git push origin "HEAD:$Branch"
        $pushExit = $LASTEXITCODE
    }
    finally {
        $PSNativeCommandUseErrorActionPreference = $savedNativePreference
    }
    if ($pushExit -eq 0) {
        $Pushed = $true
    }
    else {
        Start-Sleep -Seconds (2 * $attempt)
    }
'''
    text = replace_once(text, old_push, new_push, "push retry")
    PATH.write_text(text, encoding="utf-8", newline="\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
