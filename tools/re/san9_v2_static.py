#!/usr/bin/env python3
"""Static, read-only San9PK 1.0.1.0 availability audit.

The script reads the on-disk executable only.  It never opens the game process,
changes process memory, injects code, or calls a game function.  All virtual
addresses are version-locked to TARGET_SHA256.

Examples:
    python san9_v2_static.py
    python san9_v2_static.py --json
    python san9_v2_static.py --disassemble
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Iterable

try:
    import pefile
except ImportError as error:  # pragma: no cover - dependency check
    raise SystemExit("pefile is required: python -m pip install pefile") from error


TARGET_EXE = Path(r"D:\三国志9\10101749\San9PK.exe")
TARGET_SHA256 = "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028"
IMAGE_BASE = 0x00400000

COMMAND_DESCRIPTOR_PTR_TABLE = 0x0066AA40
GENERIC_BUILD_CITY_PERSON_LIST = 0x00436D60
READY_PERSON_PREDICATE = 0x00472D30
PERSON_FLAG_SETTER = 0x0044C8A0
MARK_SELECTED_BUSY = 0x00486150
GENERIC_MULTI_SELECT = 0x00570500
GENERIC_LIST_SORT = 0x0046F610
GENERIC_FIELD_SORT = 0x0046F640
COMMIT_SELECTED_LIST = 0x004E6C80
AI_WEIGHTED_SELECT = 0x00473280


@dataclass(frozen=True)
class CommandAnchor:
    command_id: int
    name: str
    handler_vptr: int
    can_execute: int
    execute_ui: int
    availability_call: int
    ui_constructor: int
    ui_vptr: int
    dialog_style: int
    dialog_call: int
    source_sort_call: int
    source_sort_field: int
    selected_sort_call: int
    selected_key_function: int
    selected_key_field: str
    command_object_constructor: int
    mark_busy_call: int


COMMANDS: tuple[CommandAnchor, ...] = (
    CommandAnchor(
        0,
        "巡察",
        0x00609B90,
        0x004C1930,
        0x004C1840,
        0x004C199F,
        0x004D8400,
        0x0060B920,
        0x0066C61C,
        0x004D8C1A,
        0x004D8BFB,
        0x1D,
        0x004D8C2F,
        0x00471D30,
        "intelligence/current +0x58",
        0x00488410,
        0x0048893F,
    ),
    CommandAnchor(
        1,
        "商业",
        0x00609F38,
        0x004C6400,
        0x004C6310,
        0x004C6467,
        0x004E6AF0,
        0x0060CCB0,
        0x0066C628,
        0x004E734A,
        0x004E732B,
        0x1E,
        0x004E735F,
        0x00471D80,
        "politics/current +0x60",
        0x0048B340,
        0x0048B861,
    ),
    CommandAnchor(
        2,
        "开垦",
        0x00609BF0,
        0x004C1F50,
        0x004C1E60,
        0x004C1FB7,
        0x004D9F50,
        0x0060BBA0,
        0x0066C634,
        0x004DA6EA,
        0x004DA6CB,
        0x1E,
        0x004DA6FF,
        0x00471D80,
        "politics/current +0x60",
        0x00488B00,
        0x00489027,
    ),
    CommandAnchor(
        3,
        "修筑",
        0x00609C20,
        0x004C2270,
        0x004C2180,
        0x004C22D7,
        0x004DA9B0,
        0x0060BCE8,
        0x0066C640,
        0x004DB1EA,
        0x004DB1CB,
        0x1B,
        0x004DB1FF,
        0x00471DD0,
        "leadership/current +0x68",
        0x00489080,
        0x0048956E,
    ),
    CommandAnchor(
        5,
        "训练",
        0x00609CE0,
        0x004C3890,
        0x004C37A0,
        0x004C38FF,
        0x004DF350,
        0x0060C370,
        0x0066C658,
        0x004DF8C1,
        0x004DF8A5,
        0x1C,
        0x004DF8D6,
        0x00471CE0,
        "martial/current +0x50",
        0x00489D80,
        0x0048A275,
    ),
)


ANCHOR_WINDOWS: tuple[tuple[str, int, int], ...] = (
    ("city+0xDC list copy thunk", 0x00435940, 0x10),
    ("list copy implementation", 0x00460E30, 0x18),
    ("city-person list + predicate adapter", 0x00436CC0, 0xA0),
    ("three-argument city-person list wrapper", 0x00436D60, 0x28),
    ("ready predicate: !(person+0xE8 bit12)", 0x00472D30, 0x38),
    ("person flag setter", 0x0044C8A0, 0x35),
    ("mark every selected person busy", 0x00486150, 0x50),
    ("commit task+0x6EC to task+0x6AC", 0x004E6C80, 0x30),
    ("generic multi-select wrapper", 0x00570500, 0x2E),
    ("generic key sort wrapper", 0x0046F610, 0x20),
    ("generic field sort wrapper", 0x0046F640, 0x2D),
    ("sort direction 0 is descending", 0x0046F500, 0x60),
    ("person-id key helper", 0x004467E0, 0x14),
    ("AI weighted selection wrapper", 0x00473280, 0x2E),
)


class PEView:
    def __init__(self, path: Path) -> None:
        self.path = path
        self.data = path.read_bytes()
        self.pe = pefile.PE(data=self.data, fast_load=False)
        self.image_base = int(self.pe.OPTIONAL_HEADER.ImageBase)

    def va_to_offset(self, va: int) -> int:
        return int(self.pe.get_offset_from_rva(va - self.image_base))

    def read(self, va: int, size: int) -> bytes:
        offset = self.va_to_offset(va)
        return self.data[offset : offset + size]

    def u32(self, va: int) -> int:
        return struct.unpack("<I", self.read(va, 4))[0]

    def rel32_call_target(self, va: int) -> int | None:
        instruction = self.read(va, 5)
        if len(instruction) != 5 or instruction[0] != 0xE8:
            return None
        relative = struct.unpack("<i", instruction[1:])[0]
        return va + 5 + relative

    def c_string(self, va: int, limit: int = 128) -> bytes:
        raw = self.read(va, limit)
        return raw.split(b"\0", 1)[0]

    def executable_sections(self) -> Iterable[Any]:
        for section in self.pe.sections:
            if int(section.Characteristics) & 0x20000000:
                yield section

    def find_rel32_calls(self, target: int) -> list[int]:
        """Find raw E8 rel32 references in executable sections.

        Results are reference candidates.  They are subsequently disassembled at
        their exact addresses when capstone is available, avoiding the blind spot
        caused by one-pass linear disassembly through embedded switch tables.
        """

        hits: list[int] = []
        for section in self.executable_sections():
            raw_offset = int(section.PointerToRawData)
            raw_size = int(section.SizeOfRawData)
            raw = self.data[raw_offset : raw_offset + raw_size]
            section_va = self.image_base + int(section.VirtualAddress)
            for index in range(0, max(0, len(raw) - 4)):
                if raw[index] != 0xE8:
                    continue
                relative = struct.unpack_from("<i", raw, index + 1)[0]
                instruction_va = section_va + index
                if instruction_va + 5 + relative == target:
                    hits.append(instruction_va)
        return hits


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def decode_cp950(raw: bytes) -> str:
    return raw.decode("cp950", errors="replace")


def decode_descriptor_names(view: PEView, count: int = 6) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for command_id in range(count):
        # This table contains display-name pointers directly.  The separate
        # 12-byte dialog-style records at 0x66C61C... hold the same pointer at +8.
        name_pointer = view.u32(COMMAND_DESCRIPTOR_PTR_TABLE + command_id * 4)
        raw_name = view.c_string(name_pointer, 32)
        rows.append(
            {
                "command_id": command_id,
                "name_pointer": f"0x{name_pointer:08X}",
                "name": decode_cp950(raw_name),
                "name_raw_hex": raw_name.hex(),
            }
        )
    return rows


def disassemble(view: PEView, va: int, size: int) -> list[str]:
    try:
        from capstone import CS_ARCH_X86, CS_MODE_32, Cs
    except ImportError:
        return [f"capstone unavailable; raw={view.read(va, size).hex()}"]
    decoder = Cs(CS_ARCH_X86, CS_MODE_32)
    decoder.skipdata = True
    return [
        f"0x{instruction.address:08X}: {instruction.mnemonic} {instruction.op_str}".rstrip()
        for instruction in decoder.disasm(view.read(va, size), va)
    ]


def collect_report(view: PEView, include_disassembly: bool) -> dict[str, Any]:
    command_rows: list[dict[str, Any]] = []
    for command in COMMANDS:
        row = asdict(command)
        for key, value in tuple(row.items()):
            if isinstance(value, int) and key != "command_id" and key != "source_sort_field":
                row[key] = f"0x{value:08X}"
        row["source_sort_field"] = f"0x{command.source_sort_field:X}"
        row["vtable_checks"] = {
            "can_execute_at_vptr_plus_0x20": (
                view.u32(command.handler_vptr + 0x20) == command.can_execute
            ),
            "execute_ui_at_vptr_plus_0x28": (
                view.u32(command.handler_vptr + 0x28) == command.execute_ui
            ),
            "shared_commit_at_ui_vptr_plus_0x84": (
                view.u32(command.ui_vptr + 0x84) == COMMIT_SELECTED_LIST
            ),
            "availability_calls_city_person_builder": (
                view.rel32_call_target(command.availability_call)
                == GENERIC_BUILD_CITY_PERSON_LIST
            ),
            "dialog_calls_generic_multi_select": (
                view.rel32_call_target(command.dialog_call) == GENERIC_MULTI_SELECT
            ),
            "source_calls_generic_field_sort": (
                view.rel32_call_target(command.source_sort_call) == GENERIC_FIELD_SORT
            ),
            "selected_calls_generic_key_sort": (
                view.rel32_call_target(command.selected_sort_call) == GENERIC_LIST_SORT
            ),
            "execution_marks_selected_busy": (
                view.rel32_call_target(command.mark_busy_call) == MARK_SELECTED_BUSY
            ),
        }
        row["availability_call_bytes"] = view.read(command.availability_call, 5).hex()
        row["dialog_call_bytes"] = view.read(command.dialog_call, 5).hex()
        command_rows.append(row)

    shared = {
        "city_person_list_source": "city+0xDC (0x435940 -> 0x460E30 -> 0x46F200)",
        "ready_predicate": f"0x{READY_PERSON_PREDICATE:08X}",
        "ready_predicate_expression": "(person.flags_E8 & 0x1000) == 0",
        "city_person_builder_calling_convention": (
            "ECX=city; stack=(destination list, predicate, context); ret 0x0C; EAX=count"
        ),
        "handler_candidate_list": "this+0x40; count at this+0x4C",
        "ui_source_list": "task+0x6CC; count at task+0x6D8",
        "ui_working_selection": "task+0x6EC; count at task+0x6F8",
        "ui_committed_selection": "task+0x6AC; count at task+0x6B8",
        "generic_multi_select": f"0x{GENERIC_MULTI_SELECT:08X}",
        "generic_list_sort": f"0x{GENERIC_LIST_SORT:08X}",
        "generic_field_sort": f"0x{GENERIC_FIELD_SORT:08X}",
        "commit_selected": f"0x{COMMIT_SELECTED_LIST:08X}",
        "mark_selected_busy": f"0x{MARK_SELECTED_BUSY:08X}",
        "person_flag_setter": f"0x{PERSON_FLAG_SETTER:08X}",
        "ai_weighted_select": f"0x{AI_WEIGHTED_SELECT:08X}",
        "sort_direction_zero": (
            "descending and stable on equal scores "
            "(0x46F500 swaps only while previous score < current score)"
        ),
        "source_sort_tie": "equal attributes retain city+0xDC source-list order",
        "selected_sort_tie": (
            "after membership is chosen, key functions use current_stat*1000+person_id"
        ),
        "domestic_selection_limit": "min(5, task+0x6D8)",
    }

    xrefs = {
        f"calls_0x{target:08X}": [f"0x{address:08X}" for address in view.find_rel32_calls(target)]
        for target in (
            GENERIC_BUILD_CITY_PERSON_LIST,
            GENERIC_MULTI_SELECT,
            MARK_SELECTED_BUSY,
            AI_WEIGHTED_SELECT,
        )
    }

    report: dict[str, Any] = {
        "target": {
            "path": str(view.path),
            "sha256": sha256_file(view.path),
            "expected_sha256": TARGET_SHA256,
            "image_base": f"0x{view.image_base:08X}",
        },
        "decoded_command_descriptors": decode_descriptor_names(view),
        "commands": command_rows,
        "shared_layout_and_functions": shared,
        "raw_rel32_xrefs": xrefs,
    }
    if include_disassembly:
        report["anchor_disassembly"] = {
            label: disassemble(view, address, size)
            for label, address, size in ANCHOR_WINDOWS
        }
    return report


def print_text(report: dict[str, Any]) -> None:
    target = report["target"]
    print(f"target: {target['path']}")
    print(f"sha256: {target['sha256']}")
    print(f"supported: {target['sha256'].lower() == TARGET_SHA256}")
    print()
    print("command anchors:")
    for command in report["commands"]:
        checks = command["vtable_checks"]
        print(
            f"  {command['name']} id={command['command_id']} "
            f"canExecute={command['can_execute']} ui={command['execute_ui']} "
            f"select={command['dialog_call']} key={command['selected_key_field']} "
            f"checks={all(checks.values())}"
        )
    print()
    shared = report["shared_layout_and_functions"]
    print(f"candidate source: {shared['city_person_list_source']}")
    print(
        f"ready predicate: {shared['ready_predicate']} "
        f"=> {shared['ready_predicate_expression']}"
    )
    print(
        "selection lists: "
        f"{shared['ui_source_list']}; {shared['ui_working_selection']}; "
        f"{shared['ui_committed_selection']}"
    )
    print(f"sort direction 0: {shared['sort_direction_zero']}")
    print(f"source-list tie: {shared['source_sort_tie']}")
    print(f"selected-list tie: {shared['selected_sort_tie']}")
    print(f"selection limit: {shared['domestic_selection_limit']}")
    print()
    for key, addresses in report["raw_rel32_xrefs"].items():
        print(f"{key}: {', '.join(addresses)}")
    if "anchor_disassembly" in report:
        print()
        for label, instructions in report["anchor_disassembly"].items():
            print(f"[{label}]")
            for instruction in instructions:
                print(f"  {instruction}")


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        # PowerShell commonly exposes a legacy Windows code page to child
        # processes.  Emit UTF-8 so Chinese command names remain auditable.
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, default=TARGET_EXE)
    parser.add_argument("--json", action="store_true", help="emit machine-readable JSON")
    parser.add_argument(
        "--disassemble",
        action="store_true",
        help="include short disassembly windows for the shared anchors",
    )
    parser.add_argument(
        "--allow-unsupported-build",
        action="store_true",
        help="report an unknown build without treating hard-coded addresses as authoritative",
    )
    args = parser.parse_args()

    if not args.exe.is_file():
        parser.error(f"executable does not exist: {args.exe}")
    digest = sha256_file(args.exe)
    supported = digest.lower() == TARGET_SHA256
    if not supported and not args.allow_unsupported_build:
        parser.error(
            "unsupported executable hash; refusing to interpret version-locked addresses "
            f"(got {digest})"
        )

    view = PEView(args.exe)
    if view.image_base != IMAGE_BASE and not args.allow_unsupported_build:
        parser.error(
            f"unsupported image base 0x{view.image_base:08X}; expected 0x{IMAGE_BASE:08X}"
        )
    report = collect_report(view, args.disassemble)
    report["target"]["supported"] = supported and view.image_base == IMAGE_BASE

    if args.json:
        json.dump(report, sys.stdout, ensure_ascii=False, indent=2)
        print()
    else:
        print_text(report)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
