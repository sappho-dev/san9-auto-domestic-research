from __future__ import annotations

import argparse
import importlib.util
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
CORE_PATH = HERE / "materialize_s8_function_patch_v3.py"
GENERATOR_PATH = HERE / "materialize_s8_postbuild_patch.py"


def load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot import {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def apply_named_patch(core, root: Path, relative: Path, patch: bytes) -> str:
    target = root / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    if patch:
        if b"\r\n" in patch or b"\r" in patch:
            raise RuntimeError(f"Generated postbuild patch contains CRLF/CR: {relative}")
        target.write_bytes(patch)
    if not target.is_file() or target.stat().st_size == 0:
        return "no-change"

    forward = core.git_apply_check(root, target, reverse=False)
    reverse = core.git_apply_check(root, target, reverse=True)
    if forward and reverse:
        raise RuntimeError(f"Ambiguous postbuild patch: {relative}")
    if reverse:
        return "already-applied"
    if not forward:
        diagnostic = core.run(
            ["git", "apply", "--check", "--recount", "--verbose", str(target)],
            cwd=root,
            check=False,
            capture=True,
        ).stdout.decode("utf-8", errors="replace")
        raise RuntimeError(
            f"Postbuild patch matches neither source nor target state: {relative}\n{diagnostic}"
        )
    core.run(
        ["git", "apply", "--recount", "--whitespace=nowarn", str(target)],
        cwd=root,
    )
    core.run(["git", "diff", "--check"], cwd=root)
    if not core.git_apply_check(root, target, reverse=True):
        raise RuntimeError(f"Reverse check failed after applying {relative}")
    return "applied"


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

    core = load(CORE_PATH, "s8_postbuild_v2_core")
    generator = load(GENERATOR_PATH, "s8_postbuild_v2_generator")
    root = core.repo_root()

    if args.mode == "outputs":
        relative = generator.OUTPUTS_PATCH
        existing = root / relative
        if existing.is_file() and existing.stat().st_size > 0:
            status = apply_named_patch(core, root, relative, b"")
        else:
            patch = generator.generate_mode_patch(
                root,
                mode="outputs",
                mode_args=["--artifact-root", str(Path(args.artifact_root).resolve())],
                allowed_paths={
                    "build.ps1",
                    "native/San9BridgeP1EasyPingM2b/build.ps1",
                },
            )
            status = apply_named_patch(core, root, relative, patch)
    else:
        relative = generator.STATE_PATCH
        existing = root / relative
        if existing.is_file() and existing.stat().st_size > 0:
            status = apply_named_patch(core, root, relative, b"")
        else:
            patch = generator.generate_mode_patch(
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
            status = apply_named_patch(core, root, relative, patch)

    print(f"S8 postbuild mode={args.mode} status={status} patch={relative.as_posix()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
