from __future__ import annotations

import argparse
import json
import re
from dataclasses import asdict, dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CORE = ROOT / "native/San9BridgeP1EasyPingM2b/src/s5_current_context.c"
HEADER = ROOT / "native/San9BridgeP1EasyPingM2b/include/s5_current_context.h"
CONTROLLER = ROOT / "native/San9BridgeP1EasyPingM2b/src/controller.c"
BRIDGE = ROOT / "native/San9BridgeP1EasyPingM2b/src/bridge_dll.c"
OFFLINE = ROOT / "native/San9BridgeP1EasyPingM2b/src/offline_selftest.c"


@dataclass(frozen=True)
class MatrixCell:
    reader: str
    consumer: str
    bound_ab_entry: bool
    zero_target_reuses_first_city: bool
    foreign_nonzero_returns_23: bool
    consumer_uses_bound_ab: bool

    @property
    def passed(self) -> bool:
        return all(asdict(self).values())


def read(path: Path) -> str:
    return path.read_bytes().decode("utf-8-sig").replace("\r\n", "\n").replace("\r", "\n")


def function_body(source: str, name: str) -> str:
    match = re.search(rf"(?m)^[^\n;]*\b{re.escape(name)}\s*\([^;]*?\)\s*\{{", source)
    if match is None:
        raise AssertionError(f"missing function {name}")
    start = source.find("{", match.start())
    depth = 0
    quote: str | None = None
    escaped = False
    line_comment = False
    block_comment = False
    index = start
    while index < len(source):
        char = source[index]
        nxt = source[index + 1] if index + 1 < len(source) else ""
        if line_comment:
            line_comment = char != "\n"
            index += 1
            continue
        if block_comment:
            if char == "*" and nxt == "/":
                block_comment = False
                index += 2
            else:
                index += 1
            continue
        if quote is not None:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = None
            index += 1
            continue
        if char == "/" and nxt == "/":
            line_comment = True
            index += 2
            continue
        if char == "/" and nxt == "*":
            block_comment = True
            index += 2
            continue
        if char in ('"', "'"):
            quote = char
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[match.start() : index + 1]
        index += 1
    raise AssertionError(f"unterminated function {name}")


def contains_all(body: str, *tokens: str) -> bool:
    return all(token in body for token in tokens)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()

    core = read(CORE)
    header = read(HEADER)
    controller = read(CONTROLLER)
    bridge = read(BRIDGE)
    offline = read(OFFLINE)

    normalize = function_body(core, "san9_s5_bound_current_city_normalize")
    bound_read = function_body(core, "s5_bound_reader_read")
    bound_ab = function_body(core, "san9_s5_bound_current_context_capture_reader_ab")
    controller_capture = function_body(controller, "s8_capture_command")
    controller_batch = function_body(controller, "run_s8_batch_session")
    controller_wait = function_body(controller, "run_s5_apply_once_session")
    bridge_capture = function_body(bridge, "s5_capture_ab")
    bridge_start = function_body(bridge, "s5_start_no_apply")

    bound_entry = (
        "san9_s5_bound_current_context_capture_reader_ab" in header
        and contains_all(
            bound_ab,
            "s5_capture_reader_ab_native(",
            "&bound_reader",
            "binding_drift",
            "first->controller_pointer != bound->controller_pointer",
            "second->controller_pointer != bound->controller_pointer",
        )
    )
    zero_reuse = (
        contains_all(
            normalize,
            "observed_controller_target != 0u",
            "observed_controller_target != bound->city_pointer",
            "*normalized_city_pointer = bound->city_pointer",
        )
        and contains_all(
            bound_read,
            "S5_CONTROLLER_CORPS_OFFSET",
            "s5_u32(bytes, 8u)",
            "san9_s5_bound_current_city_normalize",
            "memcpy(bytes + 8u, &normalized_city",
        )
    )
    foreign_rejected = contains_all(
        normalize,
        "SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID",
        "observed_controller_target != 0u",
        "observed_controller_target != bound->city_pointer",
    )

    controller_wiring = (
        contains_all(
            controller_capture,
            "san9_s5_bound_current_context_capture_handle_ab",
            "native_command_id",
            "first, second",
        )
        and contains_all(
            controller_batch,
            "bound_city.controller_pointer == 0u ? NULL : &bound_city",
            "SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID",
            "SAN9_P1_M2B_BATCH_REBIND_REQUIRED",
        )
        and contains_all(
            controller_wait,
            "evidence.terminal_code",
            "SAN9_P1_M2B_BATCH_REBIND_REQUIRED",
            "bootstrap_result",
        )
    )
    bridge_wiring = (
        contains_all(
            bridge_capture,
            "g_runtime.s8_bound_city_valid != 0u",
            "san9_s5_bound_current_context_capture_reader_ab",
            "&g_runtime.s8_bound_city",
            "first, second",
        )
        and contains_all(
            bridge_start,
            "context_status == SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID",
            "s5_reject_before_event_result",
            "SAN9_P1_M2B_BATCH_REBIND_REQUIRED",
            "g_runtime.s8_bound_city.controller_pointer = first.controller_pointer",
            "g_runtime.s8_bound_city.city_pointer = first.city_pointer",
            "g_runtime.s8_bound_city.corps_pointer = first.corps_pointer",
        )
    )

    offline_contract = all(
        token in offline
        for token in (
            "s8-bound-target-zero-normalized-to-frozen-city",
            "s8-bound-same-city-target-accepted",
            "s8-bound-foreign-nonzero-target-rejected",
            "s8-bound-controller-drift-rejected",
            "s8-bound-corps-drift-rejected",
        )
    )

    cells = [
        MatrixCell(reader=reader, consumer=consumer,
            bound_ab_entry=bound_entry,
            zero_target_reuses_first_city=zero_reuse,
            foreign_nonzero_returns_23=foreign_rejected,
            consumer_uses_bound_ab=(controller_wiring if consumer == "Controller" else bridge_wiring))
        for reader in ("A", "B")
        for consumer in ("Controller", "Bridge")
    ]
    report = {
        "contract": {
            "bound_ab_entry": bound_entry,
            "zero_target_reuses_first_city": zero_reuse,
            "foreign_nonzero_rejected": foreign_rejected,
            "offline_117_delta_tests_present": offline_contract,
        },
        "consumer_wiring": {
            "controller": controller_wiring,
            "bridge": bridge_wiring,
        },
        "matrix": [dict(asdict(cell), passed=cell.passed) for cell in cells],
    }
    payload = json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    print(payload, end="")
    if args.report is not None:
        target = args.report if args.report.is_absolute() else ROOT / args.report
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(payload, encoding="utf-8", newline="\n")

    failed = [cell for cell in cells if not cell.passed]
    if not offline_contract or failed:
        labels = ", ".join(f"{cell.reader}/{cell.consumer}" for cell in failed) or "offline-contract"
        raise SystemExit(f"S8 bound path audit failed: {labels}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
