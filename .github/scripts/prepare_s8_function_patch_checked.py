from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
BASE_PATH = HERE / "prepare_s8_function_patch.py"

spec = importlib.util.spec_from_file_location("s8_patch_generator", BASE_PATH)
if spec is None or spec.loader is None:
    raise SystemExit(f"Cannot load S8 patch generator: {BASE_PATH}")
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)


def validate_semantic_coverage(worktree: Path, changed: list[str]) -> dict:
    changed_set = set(changed)
    missing = sorted(module.MANDATORY_SOURCE_TARGETS - changed_set)
    if missing:
        raise SystemExit(
            "Historical baseline did not produce mandatory S8 reader/controller/Bridge/test targets: "
            + ", ".join(missing)
        )

    offline_path = "native/San9BridgeP1EasyPingM2b/src/offline_selftest.c"
    offline_added = module.added_lines(worktree, offline_path)
    if not offline_added.strip():
        raise SystemExit("Historical baseline changed offline_selftest.c without adding test code")

    lowered = offline_added.lower()
    token_counts = {
        token: lowered.count(token)
        for token in ("s8", "bound", "reader", "controller", "bridge", "rebind")
    }
    path_assertions = {
        "bound_reader_a_b_core": "native/San9BridgeP1EasyPingM2b/src/s5_current_context.c" in changed_set,
        "controller_path": "native/San9BridgeP1EasyPingM2b/src/controller.c" in changed_set,
        "bridge_path": "native/San9BridgeP1EasyPingM2b/src/bridge_dll.c" in changed_set,
        "native_offline_tests": offline_path in changed_set,
        "managed_result_mapping": "src/San9AutoDomestic.UI/NativeControllerClient.cs" in changed_set,
        "product_state_tests": "tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs" in changed_set,
    }
    if not all(path_assertions.values()):
        raise SystemExit(
            "S8 semantic path coverage is incomplete: "
            + json.dumps(path_assertions, sort_keys=True)
        )

    return {
        "offline_added_lines": len(offline_added.splitlines()),
        "offline_added_token_counts": token_counts,
        "path_assertions": path_assertions,
        "runtime_gate": (
            "The Windows pipeline must pass the complete native build, explicit offline.exe, "
            "and complete product regression build before committing."
        ),
    }


module.validate_semantic_coverage = validate_semantic_coverage
raise SystemExit(module.main())
