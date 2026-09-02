from __future__ import annotations

import argparse
import hashlib
import importlib.util
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
CORE_PATH = HERE / "materialize_s8_function_patch_v3.py"
SPEC = importlib.util.spec_from_file_location("s8_materializer_core", CORE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Cannot import materializer core: {CORE_PATH}")
core = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = core
SPEC.loader.exec_module(core)

OUTPUTS_PATCH = Path(".github/patches/s8-deterministic-output-hashes.patch")
STATE_PATCH = Path(".github/patches/s8-state-evidence.patch")


def normalize_lf(path: Path) -> None:
    data = path.read_bytes()
    path.write_bytes(data.replace(b"\r\n", b"\n").replace(b"\r", b"\n"))


def generate_mode_patch(
    root: Path,
    *,
    mode: str,
    mode_args: list[str],
    allowed_paths: set[str],
) -> bytes:
    baseline = core.recover_baseline(root)
    with tempfile.TemporaryDirectory(prefix=f"san9-s8-{mode}-") as temp_dir:
        worktree = Path(temp_dir) / "worktree"
        core.run(
            ["git", "worktree", "add", "--detach", str(worktree), "HEAD"],
            cwd=root,
        )
        try:
            script = worktree / core.BASELINE_COPY
            script.parent.mkdir(parents=True, exist_ok=True)
            script.write_bytes(baseline.plaintext)
            core.run(
                [sys.executable, str(script), mode, *mode_args],
                cwd=worktree,
            )
            for relative in allowed_paths:
                candidate = worktree / relative
                if candidate.is_file():
                    normalize_lf(candidate)
            core.run(["git", "diff", "--check"], cwd=worktree)
            actual = core.changed_paths(worktree)
            unexpected = actual - allowed_paths
            if unexpected:
                raise RuntimeError(
                    f"Baseline {mode} mode changed non-allowlisted paths: "
                    + ", ".join(sorted(unexpected))
                )
            if not actual:
                return b""
            return core.run(
                [
                    "git",
                    "diff",
                    "--binary",
                    "--full-index",
                    "--function-context",
                    "--no-ext-diff",
                    "--",
                    *sorted(allowed_paths),
                ],
                cwd=worktree,
                capture=True,
            ).stdout
        finally:
            core.run(
                ["git", "worktree", "remove", "--force", str(worktree)],
                cwd=root,
                check=False,
            )
            core.run(["git", "worktree", "prune"], cwd=root, check=False)


def persist_and_apply(root: Path, relative: Path, patch: bytes) -> str:
    target = root / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    if not patch:
        if target.exists():
            existing = target.read_bytes()
            if existing and core.git_apply_check(root, target, reverse=True):
                return "already-applied"
        return "no-change"
    if b"\r\n" in patch or b"\r" in patch:
        raise RuntimeError(f"Generated {relative} contains CRLF/CR")
    target.write_bytes(patch)
    return core.apply_patch_idempotently(root, patch)


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="mode", required=True)

    outputs = subparsers.add_parser("outputs")
    outputs.add_argument("--artifact-root", required=True)

    state = subparsers.add_parser("state")
    state.add_argument("--artifact-root", required=True)
    state.add_argument("--native-log", required=True)
    state.add_argument("--root-log", required=True)

    args = parser.parse_args()
    root = core.repo_root()
    if args.mode == "outputs":
        patch = generate_mode_patch(
            root,
            mode="outputs",
            mode_args=["--artifact-root", str(Path(args.artifact_root).resolve())],
            allowed_paths={
                "build.ps1",
                "native/San9BridgeP1EasyPingM2b/build.ps1",
            },
        )
        status = persist_and_apply(root, OUTPUTS_PATCH, patch)
        target = OUTPUTS_PATCH
    else:
        patch = generate_mode_patch(
            root,
            mode="state",
            mode_args=[
                "--artifact-root",
                str(Path(args.artifact_root).resolve()),
                "--native-log",
                str(Path(args.native_log).resolve()),
                "--root-log",
                str(Path(args.root_log).resolve()),
            ],
            allowed_paths={"STATE.md"},
        )
        status = persist_and_apply(root, STATE_PATCH, patch)
        target = STATE_PATCH

    digest = hashlib.sha256(patch).hexdigest() if patch else "none"
    print(
        f"S8 postbuild mode={args.mode} status={status} "
        f"patch={target.as_posix()} sha256={digest}"
    )
    core.run(["git", "diff", "--check"], cwd=root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
