from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
CORE_PATH = HERE / "materialize_s8_function_patch_v3.py"
HARDENER_PATH = HERE / "ensure_s8_materializer_windows_safe.py"


def load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot import {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def main() -> int:
    hardener = load_module(HARDENER_PATH, "s8_materializer_hardener")
    hardener.main()
    core = load_module(CORE_PATH, "s8_materializer_core")
    root = core.repo_root()
    patch = root / core.PATCH_PATH

    # Recovery is always revalidated from immutable Git object 1549d62, but the
    # legacy patch program is never re-executed once a plaintext patch exists.
    core.recover_baseline(root)
    if patch.is_file() and patch.stat().st_size > 0:
        data = patch.read_bytes()
        if b"\r\n" in data or b"\r" in data:
            raise RuntimeError(f"CRLF/CR found in plaintext patch: {patch}")
        forward = core.git_apply_check(root, patch, reverse=False)
        reverse = core.git_apply_check(root, patch, reverse=True)
        if forward and reverse:
            raise RuntimeError("Stored S8 patch is ambiguous in both directions")
        if reverse:
            print("S8 function-context patch status=already-applied")
            return 0
        if forward:
            core.run(
                ["git", "apply", "--recount", "--whitespace=nowarn", str(patch)],
                cwd=root,
            )
            core.run(["git", "diff", "--check"], cwd=root)
            if not core.git_apply_check(root, patch, reverse=True):
                raise RuntimeError("Stored S8 patch failed reverse verification after apply")
            print("S8 function-context patch status=applied-from-plaintext")
            return 0
        raise RuntimeError(
            "Stored S8 function-context patch matches neither source nor target state; "
            "refusing partial or heuristic repair"
        )

    return core.main()


if __name__ == "__main__":
    raise SystemExit(main())
