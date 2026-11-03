from __future__ import annotations

import argparse
import json
import re
from dataclasses import asdict, dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATHS = {
    "core": ROOT / "native/San9BridgeP1EasyPingM2b/src/s5_current_context.c",
    "header": ROOT / "native/San9BridgeP1EasyPingM2b/include/s5_current_context.h",
    "controller": ROOT / "native/San9BridgeP1EasyPingM2b/src/controller.c",
    "bridge": ROOT / "native/San9BridgeP1EasyPingM2b/src/bridge_dll.c",
    "offline": ROOT / "native/San9BridgeP1EasyPingM2b/src/offline_selftest.c",
    "ui_tests": ROOT / "tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs",
}


@dataclass(frozen=True)
class MatrixCell:
    reader: str
    consumer: str
    reader_entry_present: bool
    consumer_calls_capture: bool
    zero_live_target_reuses_bound_city: bool
    nonzero_foreign_target_returns_rebind: bool
    offline_or_product_test_present: bool

    @property
    def passed(self) -> bool:
        return all(asdict(self).values())


def read(path: Path) -> str:
    return path.read_bytes().decode("utf-8-sig").replace("\r\n", "\n").replace("\r", "\n")


def squash(value: str) -> str:
    return re.sub(r"\s+", " ", value).lower()


def function_blocks(source: str) -> list[tuple[str, str]]:
    start_re = re.compile(
        r"(?m)^[ \t]*(?:static[ \t]+)?[A-Za-z_][\w \t\*]*?[ \t]+"
        r"([A-Za-z_]\w*)[ \t]*\([^;{}]*\)[ \t]*(?:\n[ \t]*)?\{"
    )
    blocks: list[tuple[str, str]] = []
    for match in start_re.finditer(source):
        brace = source.find("{", match.start())
        depth = 0
        quote: str | None = None
        escaped = False
        line_comment = False
        block_comment = False
        index = brace
        while 0 <= index < len(source):
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
                    blocks.append((match.group(1), source[match.start() : index + 1]))
                    break
            index += 1
    return blocks


def core_contract(core: str, header: str) -> tuple[bool, bool, list[str]]:
    combined = squash(core + "\n" + header)
    bound = r"(?:bound|frozen|latched|captured)[a-z0-9_ ]{0,80}city"
    live = r"(?:controller_target|current_city_pointer|live[a-z0-9_ ]{0,40}city|target)"
    zero = bool(
        re.search(rf"{live}[^;{{}}]{{0,180}}==\s*(?:0|null)", combined)
        or re.search(rf"(?:0|null)\s*==[^;{{}}]{{0,180}}{live}", combined)
        or re.search(rf"!\s*{live}", combined)
    )
    reuse = bool(re.search(rf"{live}[^;{{}}]{{0,260}}{bound}|{bound}[^;{{}}]{{0,260}}{live}", combined))
    zero_reuse = zero and reuse

    mismatch = bool(
        re.search(rf"{live}[^;{{}}]{{0,260}}!=[^;{{}}]{{0,260}}{bound}", combined)
        or re.search(rf"{bound}[^;{{}}]{{0,260}}!=[^;{{}}]{{0,260}}{live}", combined)
        or (
            "current_city_pointer" in combined
            and "controller_target" in combined
            and "!=" in combined
        )
    )
    rebind = any(token in combined for token in ("batch_rebind_required", "rebind_required", "return 23", "= 23"))

    reader_names = sorted(
        set(
            re.findall(
                r"\b[a-z_]\w*(?:reader_kind|bound\w*reader|reader\w*bound|capture_reader)\w*\b",
                combined,
            )
        )
    )
    return zero_reuse, mismatch and rebind, reader_names


def reader_entries(core: str, header: str) -> tuple[bool, bool, list[str]]:
    combined = squash(core + "\n" + header)
    names = sorted(
        set(
            re.findall(
                r"\b[a-z_]\w*(?:capture_reader_kind|capture_bound|bound_reader|reader_kind)\w*\b",
                combined,
            )
        )
    )
    explicit_a = any(re.search(r"(?:^|_)a(?:_|$)|primary|kind_?a", name) for name in names)
    explicit_b = any(re.search(r"(?:^|_)b(?:_|$)|secondary|kind_?b", name) for name in names)
    occurrences = combined.count("reader_kind") + combined.count("capture_bound")
    if not (explicit_a and explicit_b):
        explicit_a = occurrences >= 2
        explicit_b = occurrences >= 2
    return explicit_a, explicit_b, names


def consumer_wiring(source: str) -> tuple[bool, bool]:
    lowered = squash(source)
    capture = (
        "current_context_capture" in lowered
        or "capture_reader_kind" in lowered
        or ("current_context" in lowered and "capture" in lowered)
    )
    propagation = any(
        token in lowered
        for token in (
            "batch_rebind_required",
            "rebind_required",
            "san9_s5_current_context_status",
            "current_context_status",
        )
    )
    return capture, propagation


def runtime_evidence(offline: str, ui_tests: str) -> tuple[list[str], bool]:
    blocks = function_blocks(offline)
    names: list[str] = []
    bodies: list[str] = []
    for name, body in blocks:
        lowered = squash(name + " " + body)
        if ("s8" in lowered or "batch" in lowered) and any(
            token in lowered for token in ("bound", "frozen", "rebind", "current_city", "controller_target")
        ):
            names.append(name)
            bodies.append(body)
    combined = squash("\n".join(bodies) + "\n" + ui_tests)
    evidence = bool(names) and all(
        any(token in combined for token in group)
        for group in (
            ("bound", "frozen", "latched", "captured"),
            ("rebind", "batch_rebind_required", "23"),
            ("zero", "null", "== 0", "= 0", "0u"),
        )
    )
    return names, evidence


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()

    source = {name: read(path) for name, path in PATHS.items()}
    zero_reuse, mismatch_rebind, core_reader_names = core_contract(source["core"], source["header"])
    reader_a, reader_b, reader_entry_names = reader_entries(source["core"], source["header"])
    controller_capture, controller_status = consumer_wiring(source["controller"])
    bridge_capture, bridge_status = consumer_wiring(source["bridge"])
    test_names, runtime_test = runtime_evidence(source["offline"], source["ui_tests"])

    cells: list[MatrixCell] = []
    for reader, reader_present in (("A", reader_a), ("B", reader_b)):
        for consumer, capture, status in (
            ("Controller", controller_capture, controller_status),
            ("Bridge", bridge_capture, bridge_status),
        ):
            cells.append(
                MatrixCell(
                    reader=reader,
                    consumer=consumer,
                    reader_entry_present=reader_present,
                    consumer_calls_capture=capture and status,
                    zero_live_target_reuses_bound_city=zero_reuse,
                    nonzero_foreign_target_returns_rebind=mismatch_rebind,
                    offline_or_product_test_present=runtime_test,
                )
            )

    report = {
        "core_reader_identifiers": core_reader_names,
        "reader_entry_identifiers": reader_entry_names,
        "runtime_test_functions": test_names,
        "core_contract": {
            "zero_live_target_reuses_bound_city": zero_reuse,
            "nonzero_foreign_target_returns_rebind": mismatch_rebind,
        },
        "consumer_wiring": {
            "controller": {
                "capture": controller_capture,
                "status_propagation": controller_status,
            },
            "bridge": {
                "capture": bridge_capture,
                "status_propagation": bridge_status,
            },
        },
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
        matrix = ", ".join(f"reader-{cell.reader}/{cell.consumer}" for cell in failed)
        raise SystemExit(f"S8 bound reader/consumer path matrix incomplete: {matrix}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
