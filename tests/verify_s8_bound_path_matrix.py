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
UI_TESTS = ROOT / "tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs"


@dataclass(frozen=True)
class MatrixCell:
    reader: str
    consumer: str
    bound_capture_wired: bool
    zero_target_reuses_bound: bool
    nonzero_mismatch_rejected: bool
    runtime_test_evidence: bool

    @property
    def passed(self) -> bool:
        return all(
            (
                self.bound_capture_wired,
                self.zero_target_reuses_bound,
                self.nonzero_mismatch_rejected,
                self.runtime_test_evidence,
            )
        )


def text(path: Path) -> str:
    return path.read_bytes().decode("utf-8-sig").replace("\r\n", "\n").replace("\r", "\n")


def compact(value: str) -> str:
    return re.sub(r"\s+", " ", value).strip().lower()


def contains_all(value: str, alternatives: tuple[tuple[str, ...], ...]) -> bool:
    lowered = compact(value)
    return all(any(token.lower() in lowered for token in group) for group in alternatives)


def extract_function_blocks(source: str) -> list[tuple[str, str]]:
    blocks: list[tuple[str, str]] = []
    signature = re.compile(
        r"(?m)^[ \t]*(?:static[ \t]+)?(?:[A-Za-z_][\w \t\*]+?)[ \t]+"
        r"([A-Za-z_]\w*)[ \t]*\([^;{}]*\)[ \t]*(?:\r?\n[ \t]*)?\{"
    )
    for match in signature.finditer(source):
        name = match.group(1)
        brace = source.find("{", match.start())
        if brace < 0:
            continue
        depth = 0
        index = brace
        quote: str | None = None
        escaped = False
        line_comment = False
        block_comment = False
        while index < len(source):
            char = source[index]
            nxt = source[index + 1] if index + 1 < len(source) else ""
            if line_comment:
                if char == "\n":
                    line_comment = False
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
                index += 1
                continue
            if char == "{":
                depth += 1
            elif char == "}":
                depth -= 1
                if depth == 0:
                    blocks.append((name, source[match.start() : index + 1]))
                    break
            index += 1
    return blocks


def relevant_runtime_tests(source: str) -> list[tuple[str, str]]:
    result: list[tuple[str, str]] = []
    for name, body in extract_function_blocks(source):
        lowered = compact(name + " " + body)
        if "s8" not in lowered and "batch" not in lowered:
            continue
        if any(token in lowered for token in ("bound", "rebind", "frozen", "current city", "current_city")):
            result.append((name, body))
    return result


def reader_evidence(core_source: str, header_source: str) -> tuple[list[str], bool]:
    combined = core_source + "\n" + header_source
    identifiers = sorted(
        set(
            re.findall(
                r"\b[A-Za-z_]\w*(?:bound\w*reader|reader\w*bound|reader_kind\w*)\b",
                combined,
                flags=re.IGNORECASE,
            )
        )
    )
    explicit = [name for name in identifiers if re.search(r"(?:_a|_b|kind|primary|secondary)$", name, re.I)]
    capture_calls = re.findall(r"\b[A-Za-z_]\w*current_context\w*capture\w*\s*\(", combined, re.I)
    dual = len(explicit) >= 2 or len(set(capture_calls)) >= 2 or compact(combined).count("reader_kind") >= 2
    return identifiers, dual


def consumer_semantics(source: str) -> tuple[bool, bool, bool]:
    lowered = compact(source)
    bound_tokens = ("bound", "frozen", "latched", "captured")
    city_tokens = ("city", "current_city", "city_pointer")
    wired = any(token in lowered for token in bound_tokens) and any(token in lowered for token in city_tokens)
    zero_patterns = (
        r"(?:target|current_city|city_pointer|controller_target)[^;{}]{0,160}==\s*(?:0|null)",
        r"(?:0|null)\s*==[^;{}]{0,160}(?:target|current_city|city_pointer|controller_target)",
        r"!\s*(?:target|current_city|city_pointer|controller_target)",
    )
    zero = any(re.search(pattern, lowered, re.I) for pattern in zero_patterns) and wired
    mismatch_patterns = (
        r"(?:target|current_city|city_pointer|controller_target)[^;{}]{0,220}!=[^;{}]{0,220}(?:bound|frozen|latched|captured)",
        r"(?:bound|frozen|latched|captured)[^;{}]{0,220}!=[^;{}]{0,220}(?:target|current_city|city_pointer|controller_target)",
    )
    mismatch = any(re.search(pattern, lowered, re.I) for pattern in mismatch_patterns)
    rejected = mismatch and any(
        token in lowered
        for token in ("batch_rebind_required", "rebind_required", "return 23", "= 23")
    )
    return wired, zero, rejected


def test_mentions(body: str, reader: str, consumer: str) -> bool:
    lowered = compact(body)
    reader_tokens = (
        ("reader a", "reader_a", "bound reader", "bound_reader", "reader kind")
        if reader == "A"
        else ("reader b", "reader_b", "bound reader", "bound_reader", "reader kind")
    )
    consumer_tokens = (
        ("controller", "controller.exe", "native controller")
        if consumer == "Controller"
        else ("bridge", "bridge.dll", "bridge_dll")
    )
    semantic_tokens = ("zero", "null", "0", "rebind", "bound", "frozen")
    return (
        any(token in lowered for token in reader_tokens)
        and any(token in lowered for token in consumer_tokens)
        and any(token in lowered for token in semantic_tokens)
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()

    core_source = text(CORE)
    header_source = text(HEADER)
    controller_source = text(CONTROLLER)
    bridge_source = text(BRIDGE)
    offline_source = text(OFFLINE)
    ui_source = text(UI_TESTS)

    reader_ids, dual_reader = reader_evidence(core_source, header_source)
    controller_wired, controller_zero, controller_mismatch = consumer_semantics(controller_source)
    bridge_wired, bridge_zero, bridge_mismatch = consumer_semantics(bridge_source)

    runtime_tests = relevant_runtime_tests(offline_source)
    runtime_blob = "\n".join(body for _, body in runtime_tests) + "\n" + ui_source
    cells: list[MatrixCell] = []
    for reader in ("A", "B"):
        for consumer, semantics in (
            (
                "Controller",
                (controller_wired, controller_zero, controller_mismatch),
            ),
            ("Bridge", (bridge_wired, bridge_zero, bridge_mismatch)),
        ):
            direct_evidence = test_mentions(runtime_blob, reader, consumer)
            broad_runtime_evidence = bool(runtime_tests) and contains_all(
                runtime_blob,
                (
                    ("bound", "frozen", "latched"),
                    ("rebind", "batch_rebind_required", "23"),
                ),
            )
            cells.append(
                MatrixCell(
                    reader=reader,
                    consumer=consumer,
                    bound_capture_wired=dual_reader and semantics[0],
                    zero_target_reuses_bound=semantics[1],
                    nonzero_mismatch_rejected=semantics[2],
                    runtime_test_evidence=direct_evidence or broad_runtime_evidence,
                )
            )

    report = {
        "reader_identifiers": reader_ids,
        "dual_reader_path_detected": dual_reader,
        "runtime_test_functions": [name for name, _ in runtime_tests],
        "matrix": [dict(asdict(cell), passed=cell.passed) for cell in cells],
    }
    payload = json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    print(payload)
    if args.report is not None:
        target = args.report if args.report.is_absolute() else ROOT / args.report
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(payload, encoding="utf-8", newline="\n")

    failed = [cell for cell in cells if not cell.passed]
    if failed:
        names = ", ".join(f"reader-{cell.reader}/{cell.consumer}" for cell in failed)
        raise SystemExit(f"S8 bound-path matrix incomplete: {names}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
