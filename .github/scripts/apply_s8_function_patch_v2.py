from __future__ import annotations

import hashlib
import importlib.util
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
CORE_PATH = HERE / "materialize_s8_function_patch_v3.py"
HARDENER_PATH = HERE / "ensure_s8_materializer_windows_safe.py"


def load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot import {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def apply_or_verify(core, root: Path, patch: Path) -> str | None:
    if not patch.is_file() or patch.stat().st_size == 0:
        return None
    data = patch.read_bytes()
    if b"\r\n" in data or b"\r" in data:
        return None
    forward = core.git_apply_check(root, patch, reverse=False)
    reverse = core.git_apply_check(root, patch, reverse=True)
    if forward and reverse:
        raise RuntimeError("S8 source patch is ambiguous in both directions")
    if reverse:
        return "already-applied"
    if forward:
        core.run(
            ["git", "apply", "--recount", "--whitespace=nowarn", str(patch)],
            cwd=root,
        )
        core.run(["git", "diff", "--check"], cwd=root)
        if not core.git_apply_check(root, patch, reverse=True):
            raise RuntimeError("Reverse check failed after applying stored S8 source patch")
        return "applied-from-plaintext"
    return None


def main() -> int:
    hardener = load(HARDENER_PATH, "s8_materializer_hardener_v2")
    hardener.main()
    core = load(CORE_PATH, "s8_materializer_core_v2")
    root = core.repo_root()
    patch = root / core.PATCH_PATH
    baseline = core.recover_baseline(root)

    status = apply_or_verify(core, root, patch)
    if status is not None:
        print(f"S8 function-context patch status={status}")
        return 0

    previous_sha = "none"
    if patch.is_file():
        previous_sha = hashlib.sha256(patch.read_bytes()).hexdigest()
    canonical = core.generate_patch(root, baseline)
    patch.parent.mkdir(parents=True, exist_ok=True)
    patch.write_bytes(canonical)
    status = apply_or_verify(core, root, patch)
    if status is None:
        raise RuntimeError(
            "Canonical S8 patch regenerated from immutable 1549d62 baseline matches "
            "neither the current source nor target state; refusing partial repair"
        )
    print(
        f"S8 function-context patch status={status}; canonical-restored=true; "
        f"replaced_sha256={previous_sha}; canonical_sha256={hashlib.sha256(canonical).hexdigest()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
