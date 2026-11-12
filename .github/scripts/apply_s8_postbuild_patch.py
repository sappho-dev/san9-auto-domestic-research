from __future__ import annotations

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


def apply_existing(core, root: Path, patch: Path) -> bool:
    if not patch.is_file() or patch.stat().st_size == 0:
        return False
    data = patch.read_bytes()
    if b"\r\n" in data or b"\r" in data:
        raise RuntimeError(f"CRLF/CR found in plaintext postbuild patch: {patch}")
    forward = core.git_apply_check(root, patch, reverse=False)
    reverse = core.git_apply_check(root, patch, reverse=True)
    if forward and reverse:
        raise RuntimeError(f"Ambiguous postbuild patch: {patch}")
    if reverse:
        print(f"{patch.name} status=already-applied")
        return True
    if forward:
        core.run(
            ["git", "apply", "--recount", "--whitespace=nowarn", str(patch)],
            cwd=root,
        )
        core.run(["git", "diff", "--check"], cwd=root)
        if not core.git_apply_check(root, patch, reverse=True):
            raise RuntimeError(f"Reverse verification failed after applying {patch}")
        print(f"{patch.name} status=applied-from-plaintext")
        return True
    raise RuntimeError(
        f"Stored postbuild patch matches neither source nor target state: {patch}"
    )


def main() -> int:
    core = load(CORE_PATH, "s8_postbuild_core")
    generator = load(GENERATOR_PATH, "s8_postbuild_generator")
    if len(sys.argv) < 2 or sys.argv[1] not in {"outputs", "state"}:
        raise SystemExit("usage: apply_s8_postbuild_patch.py {outputs|state} ...")
    mode = sys.argv[1]
    root = core.repo_root()
    relative = generator.OUTPUTS_PATCH if mode == "outputs" else generator.STATE_PATCH
    if apply_existing(core, root, root / relative):
        return 0
    return generator.main()


if __name__ == "__main__":
    raise SystemExit(main())
