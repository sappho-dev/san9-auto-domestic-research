from __future__ import annotations

import base64
import gzip
import hashlib
import json
import os
import py_compile
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Sequence

ROOT = Path(__file__).resolve().parents[2]
SCRIPT_DIR = ROOT / ".github" / "scripts"
PATCH_DIR = ROOT / ".github" / "patches"
HISTORICAL_COMMIT = "1549d62"
HISTORICAL_BLOB = ".github/scripts/apply_s8_fix.py.gz.b64"
LEGACY_PATH = SCRIPT_DIR / "apply_s8_fix_legacy_1549d62.py"
DRIVER_PATH = SCRIPT_DIR / "apply_s8_fix_driver.py"
FINAL_DRIVER_PATH = SCRIPT_DIR / "apply_s8_fix.py"
PATCH_PATH = PATCH_DIR / "s8-cross-step-bound-city.patch"
MANIFEST_PATH = PATCH_DIR / "s8-cross-step-bound-city.manifest.json"
AUDIT_PATH = PATCH_DIR / "s8-cross-step-bound-city.audit.json"

MANDATORY_SOURCE_TARGETS = {
    "native/San9BridgeP1EasyPingM2b/include/s5_current_context.h",
    "native/San9BridgeP1EasyPingM2b/src/s5_current_context.c",
    "native/San9BridgeP1EasyPingM2b/src/controller.c",
    "native/San9BridgeP1EasyPingM2b/src/bridge_dll.c",
    "native/San9BridgeP1EasyPingM2b/src/offline_selftest.c",
    "src/San9AutoDomestic.UI/NativeControllerClient.cs",
    "tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs",
}

ATTR_LINES = (
    "/build.ps1 text eol=lf diff=powershell",
    "/STATE.md text eol=lf",
    "/.github/scripts/apply_s8_fix.py text eol=lf diff=python",
    "/.github/scripts/apply_s8_fix_driver.py text eol=lf diff=python",
    "/.github/scripts/apply_s8_fix_legacy_1549d62.py text eol=lf diff=python",
    "/.github/scripts/prepare_s8_function_patch.py text eol=lf diff=python",
    "/.github/patches/s8-cross-step-bound-city.patch text eol=lf",
    "/.github/patches/s8-cross-step-bound-city.manifest.json text eol=lf",
    "/.github/patches/s8-cross-step-bound-city.audit.json text eol=lf",
    "/native/San9BridgeP1EasyPingM2b/include/s5_current_context.h text eol=lf diff=cpp",
    "/native/San9BridgeP1EasyPingM2b/src/s5_current_context.c text eol=lf diff=cpp",
    "/native/San9BridgeP1EasyPingM2b/src/controller.c text eol=lf diff=cpp",
    "/native/San9BridgeP1EasyPingM2b/src/bridge_dll.c text eol=lf diff=cpp",
    "/native/San9BridgeP1EasyPingM2b/src/offline_selftest.c text eol=lf diff=cpp",
    "/native/San9BridgeP1EasyPingM2b/build.ps1 text eol=lf diff=powershell",
    "/src/San9AutoDomestic.UI/NativeControllerClient.cs text eol=lf diff=csharp",
    "/tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs text eol=lf diff=csharp",
)


def run(
    command: Sequence[str],
    *,
    cwd: Path = ROOT,
    capture: bool = False,
    check: bool = True,
) -> subprocess.CompletedProcess[bytes]:
    print("+", " ".join(str(part) for part in command), flush=True)
    return subprocess.run(
        [str(part) for part in command],
        cwd=cwd,
        check=check,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.PIPE if capture else None,
    )


def canonical_lf(data: bytes) -> bytes:
    return data.replace(b"\r\n", b"\n").replace(b"\r", b"\n")


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def canonical_sha256(data: bytes) -> str:
    return sha256(canonical_lf(data))


def eol_style(data: bytes) -> str:
    body = data[3:] if data.startswith(b"\xef\xbb\xbf") else data
    canonical = canonical_lf(body)
    if b"\r\n" in body and canonical.replace(b"\n", b"\r\n") == body:
        return "crlf"
    if b"\r" in body and b"\n" not in body:
        return "cr"
    return "lf"


def recover_historical_plaintext() -> bytes:
    historical = run(
        ["git", "show", f"{HISTORICAL_COMMIT}:{HISTORICAL_BLOB}"],
        capture=True,
    ).stdout
    encoded = b"".join(historical.split())
    compressed = base64.b64decode(encoded, validate=True)
    plaintext = gzip.decompress(compressed)
    if b"def main(" not in plaintext or b"argparse" not in plaintext:
        raise SystemExit("1549d62 payload is not the expected complete Python patcher")
    LEGACY_PATH.parent.mkdir(parents=True, exist_ok=True)
    LEGACY_PATH.write_bytes(plaintext)
    py_compile.compile(str(LEGACY_PATH), doraise=True)
    print(
        f"Recovered historical patcher: bytes={len(plaintext)} "
        f"sha256={sha256(plaintext)}"
    )
    return plaintext


def ensure_attributes() -> None:
    path = ROOT / ".gitattributes"
    original = path.read_text(encoding="utf-8") if path.is_file() else ""
    lines = original.splitlines()
    known = set(lines)
    changed = False
    if lines and lines[-1] != "":
        lines.append("")
    for line in ATTR_LINES:
        if line not in known:
            lines.append(line)
            known.add(line)
            changed = True
    if changed or not path.is_file():
        path.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8", newline="\n")


def add_worktree(path: Path) -> None:
    if path.exists():
        run(["git", "worktree", "remove", "--force", str(path)], check=False)
        shutil.rmtree(path, ignore_errors=True)
    run(["git", "worktree", "prune"])
    run(["git", "worktree", "add", "--detach", str(path), "HEAD"])
    run(["git", "config", "core.autocrlf", "false"], cwd=path)


def remove_worktree(path: Path) -> None:
    run(["git", "worktree", "remove", "--force", str(path)], check=False)
    shutil.rmtree(path, ignore_errors=True)
    run(["git", "worktree", "prune"], check=False)


def git_show(path: str) -> bytes:
    return run(["git", "show", f"HEAD:{path}"], capture=True).stdout


def changed_files(worktree: Path) -> list[str]:
    payload = run(
        ["git", "diff", "--name-only", "-z", "--no-ext-diff"],
        cwd=worktree,
        capture=True,
    ).stdout
    return sorted(item.decode("utf-8") for item in payload.split(b"\0") if item)


def added_lines(worktree: Path, path: str) -> str:
    diff = run(
        ["git", "diff", "--unified=0", "--no-ext-diff", "--", path],
        cwd=worktree,
        capture=True,
    ).stdout.decode("utf-8", errors="replace")
    return "\n".join(
        line[1:]
        for line in diff.splitlines()
        if line.startswith("+") and not line.startswith("+++")
    )


def validate_semantic_coverage(worktree: Path, changed: list[str]) -> dict:
    changed_set = set(changed)
    missing = sorted(MANDATORY_SOURCE_TARGETS - changed_set)
    if missing:
        raise SystemExit(
            "Historical baseline did not produce the mandatory S8 source/test targets: "
            + ", ".join(missing)
        )

    offline_path = "native/San9BridgeP1EasyPingM2b/src/offline_selftest.c"
    offline_added = added_lines(worktree, offline_path)
    lowered = offline_added.lower()
    token_counts = {
        token: lowered.count(token)
        for token in ("s8", "bound", "reader", "controller", "bridge", "rebind")
    }
    if token_counts["s8"] < 4 or token_counts["bound"] < 2 or token_counts["reader"] < 2:
        raise SystemExit(
            "Recovered baseline does not visibly add the required S8 bound-reader test surface: "
            + json.dumps(token_counts, sort_keys=True)
        )

    path_assertions = {
        "bound_reader_core": "native/San9BridgeP1EasyPingM2b/src/s5_current_context.c" in changed_set,
        "controller_path": "native/San9BridgeP1EasyPingM2b/src/controller.c" in changed_set,
        "bridge_path": "native/San9BridgeP1EasyPingM2b/src/bridge_dll.c" in changed_set,
        "native_offline_tests": offline_path in changed_set,
        "managed_result_mapping": "src/San9AutoDomestic.UI/NativeControllerClient.cs" in changed_set,
        "product_state_tests": "tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs" in changed_set,
    }
    if not all(path_assertions.values()):
        raise SystemExit("S8 semantic path coverage is incomplete")

    return {
        "offline_added_token_counts": token_counts,
        "path_assertions": path_assertions,
    }


def generate_patch_and_manifest(worktree: Path, historical_plaintext: bytes) -> None:
    changed = changed_files(worktree)
    if not changed:
        raise SystemExit("Recovered 1549d62 source command made no changes")

    coverage = validate_semantic_coverage(worktree, changed)
    patch = run(
        [
            "git",
            "diff",
            "--function-context",
            "--binary",
            "--no-ext-diff",
            "--",
            *changed,
        ],
        cwd=worktree,
        capture=True,
    ).stdout
    if not patch.startswith(b"diff --git "):
        raise SystemExit("Function-context patch generation produced no textual patch")

    entries: list[dict] = []
    for relative in changed:
        before = git_show(relative)
        after_path = worktree / relative
        if not after_path.is_file():
            raise SystemExit(f"Function patch unexpectedly deletes or fails to produce: {relative}")
        after = after_path.read_bytes()
        entries.append(
            {
                "path": relative,
                "before_sha256_canonical_lf": canonical_sha256(before),
                "after_sha256_canonical_lf": canonical_sha256(after),
                "before_eol": eol_style(before),
                "after_eol": eol_style(after),
                "before_bytes": len(before),
                "after_bytes": len(after),
            }
        )

    PATCH_DIR.mkdir(parents=True, exist_ok=True)
    PATCH_PATH.write_bytes(canonical_lf(patch))
    manifest = {
        "format": 1,
        "historical_patcher_commit": HISTORICAL_COMMIT,
        "historical_patcher_sha256": sha256(historical_plaintext),
        "source_preimage_commit": run(
            ["git", "rev-parse", "HEAD"], capture=True
        ).stdout.decode("ascii").strip(),
        "patch_mode": "git diff --function-context; canonical-LF pre/post hashes",
        "patch_sha256": sha256(PATCH_PATH.read_bytes()),
        "files": entries,
    }
    MANIFEST_PATH.write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )

    audit = {
        "historical_baseline": HISTORICAL_COMMIT,
        "changed_files": changed,
        "coverage": coverage,
        "patch_bytes": len(PATCH_PATH.read_bytes()),
        "patch_sha256": manifest["patch_sha256"],
    }
    AUDIT_PATH.write_text(
        json.dumps(audit, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def install_final_driver() -> None:
    if not DRIVER_PATH.is_file():
        raise SystemExit(f"S8 driver template is missing: {DRIVER_PATH}")
    FINAL_DRIVER_PATH.write_bytes(canonical_lf(DRIVER_PATH.read_bytes()))
    py_compile.compile(str(FINAL_DRIVER_PATH), doraise=True)


def main() -> int:
    os.chdir(ROOT)
    ensure_attributes()
    historical_plaintext = recover_historical_plaintext()

    runner_temp = Path(os.environ.get("RUNNER_TEMP", tempfile.gettempdir()))
    worktree = runner_temp / "s8-patch-generation"
    add_worktree(worktree)
    try:
        legacy_in_worktree = worktree / LEGACY_PATH.relative_to(ROOT)
        legacy_in_worktree.parent.mkdir(parents=True, exist_ok=True)
        legacy_in_worktree.write_bytes(historical_plaintext)
        run([sys.executable, str(legacy_in_worktree), "source"], cwd=worktree)
        run(["git", "diff", "--check"], cwd=worktree)
        generate_patch_and_manifest(worktree, historical_plaintext)
    finally:
        remove_worktree(worktree)

    install_final_driver()
    print(
        f"Prepared plaintext S8 patch: {PATCH_PATH.relative_to(ROOT)}; "
        f"manifest={MANIFEST_PATH.relative_to(ROOT)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
