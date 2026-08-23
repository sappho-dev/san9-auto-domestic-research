from __future__ import annotations

import argparse
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCES = (
    ROOT / "native/San9BridgeP1EasyPingM2b/src/s5_current_context.c",
    ROOT / "native/San9BridgeP1EasyPingM2b/src/controller.c",
    ROOT / "native/San9BridgeP1EasyPingM2b/src/bridge_dll.c",
    ROOT / "native/San9BridgeP1EasyPingM2b/src/offline_selftest.c",
    ROOT / "tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs",
)
TOKENS = (
    "s8",
    "batch_rebind_required",
    "bound",
    "frozen",
    "reader_kind",
    "controller_target",
    "current_city_pointer",
)


def read(path: Path) -> str:
    return path.read_bytes().decode("utf-8-sig").replace("\r\n", "\n").replace("\r", "\n")


def brace_blocks(source: str) -> list[tuple[str, str]]:
    pattern = re.compile(
        r"(?m)^[ \t]*(?:static[ \t]+|private[ \t]+|public[ \t]+|internal[ \t]+)*"
        r"[A-Za-z_][\w<>,?\[\] \t\*]*?[ \t]+([A-Za-z_]\w*)"
        r"[ \t]*\([^;{}]*\)[ \t]*(?:\n[ \t]*)?\{"
    )
    result: list[tuple[str, str]] = []
    for match in pattern.finditer(source):
        opening = source.find("{", match.start())
        depth = 0
        quote: str | None = None
        escaped = False
        line_comment = False
        block_comment = False
        index = opening
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
                    result.append((match.group(1), source[match.start() : index + 1]))
                    break
            index += 1
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    sections: list[str] = []
    for path in SOURCES:
        source = read(path)
        for name, body in brace_blocks(source):
            lowered = (name + " " + body).lower()
            score = sum(token in lowered for token in TOKENS)
            if score < 2:
                continue
            relative = path.relative_to(ROOT).as_posix()
            sections.append(
                f"===== {relative} :: {name} :: token_score={score} =====\n{body}\n"
            )
    target = args.output if args.output.is_absolute() else ROOT / args.output
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text("\n".join(sections), encoding="utf-8", newline="\n")
    print(f"wrote {len(sections)} function-boundary sections to {target}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
