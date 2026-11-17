from __future__ import annotations

import argparse
import hashlib
import os
import py_compile
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

BASELINE = "1549d62"
BASELINE_COPY = Path(".github/scripts/apply_s8_fix.baseline_1549d62.py")
PATCH_PATH = Path(".github/patches/s8-cross-step-bound-city.function-context.patch")
RECOVERY_NOTE = Path(".github/scripts/apply_s8_fix.recovery.txt")
REQUIRED_MARKERS = (
    b"BATCH_REBIND_REQUIRED",
    b"s5_current_context",
    b"controller.c",
    b"bridge_dll.c",
    b"offline_selftest.c",
)
ALLOWED_SOURCE_PATHS = {
    "build.ps1",
    "native/San9BridgeP1EasyPingM2b/include/s5_current_context.h",
    "native/San9BridgeP1EasyPingM2b/src/s5_current_context.c",
    "native/San9BridgeP1EasyPingM2b/src/controller.c",
    "native/San9BridgeP1EasyPingM2b/src/bridge_dll.c",
    "native/San9BridgeP1EasyPingM2b/src/offline_selftest.c",
    "native/San9BridgeP1EasyPingM2b/build.ps1",
    "src/San9AutoDomestic.UI/NativeControllerClient.cs",
    "tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs",
}


def run(
    args: list[str],
    *,
    cwd: Path,
    check: bool = True,
    capture: bool = False,
) -> subprocess.CompletedProcess[bytes]:
    env = os.environ.copy()
    env.update(
        {
            "GIT_CONFIG_COUNT": "2",
            "GIT_CONFIG_KEY_0": "core.autocrlf",
            "GIT_CONFIG_VALUE_0": "false",
            "GIT_CONFIG_KEY_1": "core.safecrlf",
            "GIT_CONFIG_VALUE_1": "false",
        }
    )
    return subprocess.run(
        args,
        cwd=cwd,
        env=env,
        check=check,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.STDOUT if capture else None,
    )


def repo_root() -> Path:
    result = run(
        ["git", "rev-parse", "--show-toplevel"],
        cwd=Path.cwd(),
        capture=True,
    )
    return Path(result.stdout.decode("utf-8").strip()).resolve()


def recover_baseline(root: Path) -> tuple[str, bytes]:
    tree = run(
        ["git", "ls-tree", "-r", "--name-only", "-z", BASELINE],
        cwd=root,
        capture=True,
    ).stdout
    candidates: list[tuple[str, bytes]] = []
    for raw_path in tree.split(b"\0"):
        if not raw_path:
            continue
        path = raw_path.decode("utf-8")
        if not path.lower().endswith(".py"):
            continue
        blob = run(
            ["git", "show", f"{BASELINE}:{path}"],
            cwd=root,
            capture=True,
        ).stdout
        if all(marker in blob for marker in REQUIRED_MARKERS):
            compile(blob, f"{BASELINE}:{path}", "exec")
            candidates.append((path, blob))

    if len(candidates) != 1:
        names = ", ".join(path for path, _ in candidates) or "<none>"
        raise RuntimeError(
            f"Expected one complete S8 plaintext patcher in {BASELINE}; "
            f"found {len(candidates)}: {names}"
        )

    source_path, blob = candidates[0]
    target = root / BASELINE_COPY
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(blob)
    py_compile.compile(str(target), doraise=True)
    digest = hashlib.sha256(blob).hexdigest()
    (root / RECOVERY_NOTE).write_text(
        "\n".join(
            (
                f"baseline={BASELINE}",
                f"source_path={source_path}",
                f"sha256={digest}",
                f"size={len(blob)}",
                "transport=exact git blob bytes; damaged Base64/GZip payload is unused",
                "",
            )
        ),
        encoding="utf-8",
        newline="\n",
    )
    return source_path, blob


def changed_paths(worktree: Path) -> set[str]:
    output = run(
        ["git", "status", "--porcelain=v1", "-z"],
        cwd=worktree,
        capture=True,
    ).stdout
    paths: set[str] = set()
    fields = [field for field in output.split(b"\0") if field]
    index = 0
    while index < len(fields):
        field = fields[index]
        if len(field) < 4:
            raise RuntimeError(f"Malformed git status field: {field!r}")
        status = field[:2].decode("ascii", errors="replace")
        path = field[3:].decode("utf-8")
        paths.add(path.replace("\\", "/"))
        if "R" in status or "C" in status:
            index += 1
            if index >= len(fields):
                raise RuntimeError("Malformed rename/copy entry in git status")
            paths.add(fields[index].decode("utf-8").replace("\\", "/"))
        index += 1
    return paths


def generate_patch(root: Path, baseline_blob: bytes) -> bytes:
    with tempfile.TemporaryDirectory(prefix="san9-s8-target-") as temp_dir:
        worktree = Path(temp_dir) / "worktree"
        run(
            ["git", "worktree", "add", "--detach", str(worktree), "HEAD"],
            cwd=root,
        )
        try:
            script = worktree / BASELINE_COPY
            script.parent.mkdir(parents=True, exist_ok=True)
            script.write_bytes(baseline_blob)
            py_compile.compile(str(script), doraise=True)
            run([sys.executable, str(script), "source"], cwd=worktree)
            run(["git", "diff", "--check"], cwd=worktree)

            actual = changed_paths(worktree)
            unexpected = actual - ALLOWED_SOURCE_PATHS
            missing = ALLOWED_SOURCE_PATHS - actual
            if unexpected:
                raise RuntimeError(
                    "Recovered patcher changed non-allowlisted paths: "
                    + ", ".join(sorted(unexpected))
                )
            if missing:
                raise RuntimeError(
                    "Recovered patcher did not change expected paths: "
                    + ", ".join(sorted(missing))
                )

            patch = run(
                [
                    "git",
                    "diff",
                    "--binary",
                    "--full-index",
                    "--function-context",
                    "--no-ext-diff",
                    "--",
                    *sorted(ALLOWED_SOURCE_PATHS),
                ],
                cwd=worktree,
                capture=True,
            ).stdout
            if not patch.startswith(b"diff --git "):
                raise RuntimeError("Generated S8 patch is empty or malformed")
            if b"@@" not in patch:
                raise RuntimeError("Generated S8 patch has no function-context hunks")
            return patch
        finally:
            run(
                ["git", "worktree", "remove", "--force", str(worktree)],
                cwd=root,
                check=False,
            )
            run(["git", "worktree", "prune"], cwd=root, check=False)


def git_apply_check(root: Path, patch: Path, *, reverse: bool) -> bool:
    args = ["git", "apply", "--check", "--recount", "--whitespace=nowarn"]
    if reverse:
        args.append("--reverse")
    args.append(str(patch))
    return run(args, cwd=root, check=False, capture=True).returncode == 0


def apply_patch_idempotently(root: Path, patch_bytes: bytes) -> str:
    patch = root / PATCH_PATH
    patch.parent.mkdir(parents=True, exist_ok=True)
    patch.write_bytes(patch_bytes)

    forward = git_apply_check(root, patch, reverse=False)
    reverse = git_apply_check(root, patch, reverse=True)
    if forward and reverse:
        raise RuntimeError("S8 patch is ambiguous: both forward and reverse checks pass")
    if reverse:
        return "already-applied"
    if not forward:
        diagnostic = run(
            ["git", "apply", "--check", "--recount", "--verbose", str(patch)],
            cwd=root,
            check=False,
            capture=True,
        ).stdout.decode("utf-8", errors="replace")
        raise RuntimeError("S8 function-context patch cannot be applied:\n" + diagnostic)

    run(
        [
            "git",
            "apply",
            "--recount",
            "--whitespace=nowarn",
            str(patch),
        ],
        cwd=root,
    )
    run(["git", "diff", "--check"], cwd=root)
    if not git_apply_check(root, patch, reverse=True):
        raise RuntimeError("Reverse check failed after applying S8 patch")
    return "applied"


def verify_lf_and_hashes(root: Path) -> None:
    paths = [BASELINE_COPY, PATCH_PATH, RECOVERY_NOTE]
    for relative in paths:
        data = (root / relative).read_bytes()
        if b"\r\n" in data:
            raise RuntimeError(f"Generated plaintext artifact contains CRLF: {relative}")
        print(
            f"{relative.as_posix()} size={len(data)} "
            f"sha256={hashlib.sha256(data).hexdigest()}"
        )


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Recover the intact S8 patcher and materialize an idempotent function-context patch."
    )
    parser.add_argument(
        "--generate-only",
        action="store_true",
        help="Generate plaintext recovery and patch artifacts without applying source changes.",
    )
    args = parser.parse_args()

    root = repo_root()
    source_path, baseline_blob = recover_baseline(root)
    patch_bytes = generate_patch(root, baseline_blob)
    status = "generated-only" if args.generate_only else apply_patch_idempotently(root, patch_bytes)
    if args.generate_only:
        target = root / PATCH_PATH
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(patch_bytes)
    verify_lf_and_hashes(root)
    print(
        f"S8 source materialization status={status} baseline={BASELINE}:{source_path} "
        f"patch_sha256={hashlib.sha256(patch_bytes).hexdigest()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
