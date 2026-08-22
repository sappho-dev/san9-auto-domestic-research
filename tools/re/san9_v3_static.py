#!/usr/bin/env python3
"""Static, read-only San9PK 1.0.1.0 command-lifecycle audit.

The script reads the on-disk executable only.  It does not open the running
game, write process memory, inject code, create a remote thread, or call any
game function.  Every virtual address is version-locked to TARGET_SHA256.

Examples:
    python san9_v3_static.py
    python san9_v3_static.py --json
    python san9_v3_static.py --disassemble
    python san9_v3_static.py --exe D:\\games\\San9PK.exe
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

try:
    import pefile
except ImportError as error:  # pragma: no cover - dependency check
    raise SystemExit("pefile is required: python -m pip install pefile") from error


TARGET_EXE = Path(r"D:\三国志9\10101749\San9PK.exe")
TARGET_SHA256 = "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028"
IMAGE_BASE = 0x00400000

ROOT_VPTR = 0x00610BC8
ROOT_CONSTRUCTOR = 0x0050F380
ROOT_EVENT_DISPATCH = 0x005179B0
ROOT_FACTORY = 0x005125A0
ROOT_FACTORY_TABLE = 0x00513128
ROOT_CITY_CAST = 0x0050FCD0
ROOT_FACILITY_CAST = 0x0050FCB0

HANDLER_CONTEXT_CONSTRUCTOR = 0x0050E940
HANDLER_TICK = 0x004C5280
HANDLER_EXECUTE_DRIVER = 0x0050EAB0
HANDLER_COMMON_GATE = 0x004C52C0
HANDLER_CORPS_VALID = 0x0050E990
CORPS_VPTR = 0x00605C50
CORPS_IS_PLAYER_CONTROLLED = 0x0043F9F0
GAME_ALLOCATOR = 0x005DEC20

TASK_VALIDATE_AND_INIT = 0x0047E510
TASK_HAS_ACTIVE_CHILD = 0x0047E420
TASK_ATTACH_CHILD = 0x0047E6F0
COMMAND_BASE_CONSTRUCTOR = 0x00485B20
COMMAND_TICK = 0x00485D20
COMMAND_APPLY_THUNK = 0x00485A10
COMMAND_COMMON_VALIDATOR = 0x0048AB30
COMMAND_MIDDLE_CONSTRUCTOR = 0x0048A320
COMMAND_PARENT_CONSTRUCTOR = 0x00487100
LIST_CLEAR = 0x005180E0
LIST_COPY = 0x0046F200
MARK_SELECTED_BUSY = 0x00486150
PERSON_FLAG_SETTER = 0x0044C8A0
FEE_HELPER = 0x00488450
SUBTRACT_CORPS_FUNDS = 0x0043D8D0

GLOBAL_SELECTED_LIST = 0x015455AC
GLOBAL_SELECTED_HEAD = 0x015455B0
GLOBAL_SELECTED_COUNT = 0x015455B8

CURRENT_CITY_GLOBAL = 0x01232474
SCENARIO_INDEX_GLOBAL = 0x01232480
GAME_MODE_GLOBAL = 0x01232484
STRATEGIC_DATE_GLOBAL = 0x0123269C


@dataclass(frozen=True)
class CommandExecAnchor:
    command_id: int
    name: str
    root_factory_case: int
    root_factory_ctor_call: int
    handler_constructor: int
    handler_vptr: int
    can_execute: int
    execute_ui: int
    execute_dialog_call: int
    execute_command_ctor_call: int
    command_constructor: int
    command_vptr: int
    command_validator: int
    command_apply: int
    stat_mutation_call: int
    stat_mutation_target: int
    mark_busy_call: int
    fee_call: int | None
    subtract_funds_call: int | None
    order_flag_call: int
    order_flag_target: int
    order_mask: int


COMMANDS: tuple[CommandExecAnchor, ...] = (
    CommandExecAnchor(
        0,
        "巡察",
        0x005128B8,
        0x005128E7,
        0x004C1720,
        0x00609B90,
        0x004C1930,
        0x004C1840,
        0x004C187A,
        0x004C18D5,
        0x00488410,
        0x00607A80,
        0x00488580,
        0x00488690,
        0x00488933,
        0x0043A480,
        0x0048893F,
        0x00488949,
        0x00488958,
        0x0048896C,
        0x0043AC00,
        0x08,
    ),
    CommandExecAnchor(
        1,
        "商业",
        0x00512900,
        0x0051292F,
        0x004C61F0,
        0x00609F38,
        0x004C6400,
        0x004C6310,
        0x004C634A,
        0x004C63A5,
        0x0048B340,
        0x00607D98,
        0x0048B4B0,
        0x0048B5D0,
        0x0048B6F7,
        0x0043A500,
        0x0048B861,
        0x0048B86B,
        0x0048B878,
        0x0048B883,
        0x0043AC00,
        0x10,
    ),
    CommandExecAnchor(
        2,
        "开垦",
        0x00512948,
        0x00512977,
        0x004C1D40,
        0x00609BF0,
        0x004C1F50,
        0x004C1E60,
        0x004C1E9A,
        0x004C1EF5,
        0x00488B00,
        0x00607B30,
        0x00488C70,
        0x00488D90,
        0x00488EBA,
        0x0043A570,
        0x00489027,
        0x00489031,
        0x0048903E,
        0x00489049,
        0x0043AC00,
        0x20,
    ),
    CommandExecAnchor(
        3,
        "修筑",
        0x00512990,
        0x005129BF,
        0x004C2060,
        0x00609C20,
        0x004C2270,
        0x004C2180,
        0x004C21BA,
        0x004C2215,
        0x00489080,
        0x00607B88,
        0x004890E0,
        0x00489310,
        0x00489562,
        0x00435670,
        0x0048956E,
        0x00489578,
        0x00489583,
        0x0048958E,
        0x00435CD0,
        0x01,
    ),
    CommandExecAnchor(
        5,
        "训练",
        0x00512A20,
        0x00512A4F,
        0x004C35F0,
        0x00609CE0,
        0x004C3890,
        0x004C37A0,
        0x004C37DA,
        0x004C3835,
        0x00489D80,
        0x00607C38,
        0x00489F10,
        0x00489FF0,
        0x0048A269,
        0x0045DDF0,
        0x0048A275,
        None,
        None,
        0x0048A280,
        0x00435CD0,
        0x02,
    ),
)


HANDLER_TO_BASE_CALL: dict[int, int] = {
    0: 0x004C174C,
    1: 0x004C621C,
    2: 0x004C1D6C,
    3: 0x004C208C,
    5: 0x004C361C,
}

COMMAND_TO_MIDDLE_CALL: dict[int, int] = {
    0: 0x00488421,
    1: 0x0048B351,
    2: 0x00488B11,
    3: 0x00489091,
    5: 0x00489D91,
}

TRAIN_APPLY_END = 0x0048A317


BASE_ANCHOR_WINDOWS: tuple[tuple[str, int, int], ...] = (
    ("root constructor A", 0x0050F380, 0x33),
    ("root factory dispatch prefix", 0x005125A0, 0x40),
    ("root event command basic block", 0x00517BD9, 0x30),
    ("handler common gate -> corps player bit", 0x004C52C0, 0x2F),
    ("corps vtable +0x40 reads corps+0x34 bit0", 0x0043F9F0, 0x07),
    ("handler execute driver", 0x0050EAB0, 0x97),
    ("task validator dispatches vptr+0x20", 0x0047E510, 0x14),
    ("generic child relation helper", 0x0047E6F0, 0x37),
    ("command global-list base constructor", 0x00485B20, 0x7E),
    ("command state machine", 0x00485D20, 0x119),
    ("first selected object resolver", 0x00485E50, 0x30),
    ("selected person -> corps resolver", 0x00485F10, 0x21),
    ("selected person -> city resolver", 0x00485F40, 0x21),
    ("selected person -> facility resolver", 0x00485F70, 0x21),
    ("common command validator", 0x0048AB30, 0xF4),
    ("mark selected people busy", 0x00486150, 0x4F),
    ("person E8 flag setter", 0x0044C8A0, 0x34),
    ("count times 50 fee helper", 0x00488450, 0x08),
    ("scenario/mode low-byte encoding path", 0x0045B00D, 0x1B),
    ("strategic date calendar conversion", 0x0044B654, 0x3D),
)


COMMAND_ANCHOR_WINDOWS: tuple[tuple[str, int, int], ...] = tuple(
    window
    for command in COMMANDS
    for window in (
        (f"{command.name} factory case basic block", command.root_factory_case, 0x48),
        (f"{command.name} handler constructor prefix", command.handler_constructor, 0x42),
        (
            f"{command.name} Execute command-allocation basic block",
            command.execute_command_ctor_call - 0x25,
            0x30,
        ),
        (f"{command.name} command constructor", command.command_constructor, 0x20),
        (f"{command.name} command validator prefix", command.command_validator, 0x20),
        (f"{command.name} command apply prefix", command.command_apply, 0x20),
    )
)


ANCHOR_WINDOWS = BASE_ANCHOR_WINDOWS + COMMAND_ANCHOR_WINDOWS


class PEView:
    def __init__(self, path: Path, data: bytes) -> None:
        self.path = path
        self.data = data
        self.pe = pefile.PE(data=self.data, fast_load=False)
        self.image_base = int(self.pe.OPTIONAL_HEADER.ImageBase)

    def va_to_offset(self, va: int) -> int:
        if va < self.image_base:
            raise ValueError(f"VA 0x{va:08X} is below ImageBase 0x{self.image_base:08X}")
        return int(self.pe.get_offset_from_rva(va - self.image_base))

    def read(self, va: int, size: int) -> bytes:
        if size < 0:
            raise ValueError("read size cannot be negative")
        offset = self.va_to_offset(va)
        end = offset + size
        if offset < 0 or end > len(self.data):
            raise ValueError(
                f"VA window 0x{va:08X}+0x{size:X} is outside the single captured file image"
            )
        return self.data[offset:end]

    def u32(self, va: int) -> int:
        return struct.unpack("<I", self.read(va, 4))[0]

    def rel32_target(self, va: int, opcode: int = 0xE8) -> int | None:
        instruction = self.read(va, 5)
        if len(instruction) != 5 or instruction[0] != opcode:
            return None
        relative = struct.unpack("<i", instruction[1:])[0]
        return va + 5 + relative


def make_check(name: str, actual: Any, expected: Any) -> dict[str, Any]:
    return {
        "name": name,
        "ok": actual == expected,
        "actual": actual,
        "expected": expected,
    }


def call_check(view: PEView, name: str, call_site: int, target: int) -> dict[str, Any]:
    return make_check(name, view.rel32_target(call_site), target)


def find_rel32_call_sites(view: PEView, start: int, end: int, target: int) -> list[int]:
    """Find raw E8 rel32 encodings in one version-locked function range."""
    if end < start:
        raise ValueError("rel32 scan end precedes start")
    raw = view.read(start, end - start)
    sites: list[int] = []
    for offset in range(0, max(0, len(raw) - 4)):
        if raw[offset] != 0xE8:
            continue
        relative = struct.unpack_from("<i", raw, offset + 1)[0]
        site = start + offset
        if site + 5 + relative == target:
            sites.append(site)
    return sites


def fail_closed_report(
    path: Path,
    digest: str | None,
    name: str,
    actual: Any,
    expected: Any,
) -> dict[str, Any]:
    """Build a minimal report without touching any version-specific VA."""
    return {
        "target": {
            "path": str(path),
            "sha256": digest,
            "expected_sha256": TARGET_SHA256,
            "image_base": None,
        },
        "checks": [make_check(name, actual, expected)],
        "all_ok": False,
        "all_ok_scope": "version_locked_static_anchor_match_only",
        "execution_authorized": False,
        "note": (
            "Fail-closed before version-specific address inspection. This script never "
            "authorizes process calls, writes, injection, or execution."
        ),
    }


def verify(view: PEView, digest: str) -> dict[str, Any]:
    checks: list[dict[str, Any]] = [
        make_check("SHA-256", digest.lower(), TARGET_SHA256),
        make_check("PE ImageBase", view.image_base, IMAGE_BASE),
        make_check("root vtable +0x18 event dispatcher", view.u32(ROOT_VPTR + 0x18), ROOT_EVENT_DISPATCH),
        make_check("root vtable +0x28 handler factory", view.u32(ROOT_VPTR + 0x28), ROOT_FACTORY),
        make_check("factory id 0 jump target", view.u32(ROOT_FACTORY_TABLE + 0 * 4), COMMANDS[0].root_factory_case),
        make_check("factory id 1 jump target", view.u32(ROOT_FACTORY_TABLE + 1 * 4), COMMANDS[1].root_factory_case),
        make_check("factory id 2 jump target", view.u32(ROOT_FACTORY_TABLE + 2 * 4), COMMANDS[2].root_factory_case),
        make_check("factory id 3 jump target", view.u32(ROOT_FACTORY_TABLE + 3 * 4), COMMANDS[3].root_factory_case),
        make_check("factory id 5 jump target", view.u32(ROOT_FACTORY_TABLE + 5 * 4), COMMANDS[4].root_factory_case),
        make_check(
            "root constructor vptr write",
            view.read(0x0050F398, 6),
            bytes.fromhex("C7 06 C8 0B 61 00"),
        ),
        make_check(
            "47E510 clears task+0x24/+0x28 then dispatches vptr+0x20",
            view.read(TASK_VALIDATE_AND_INIT, 0x13),
            bytes.fromhex("8B 01 C7 41 24 FF FF FF FF C7 41 28 00 00 00 00 FF 60 20"),
        ),
        make_check(
            "47E6F0 stores child at parent+0x10",
            view.read(0x0047E71C, 6),
            bytes.fromhex("8B 47 10 89 77 10"),
        ),
        make_check(
            "47E420 reports active child when +0x10 != self",
            view.read(TASK_HAS_ACTIVE_CHILD, 0x0B),
            bytes.fromhex("8B 51 10 33 C0 3B D1 0F 95 C0 C3"),
        ),
        make_check("command base global list address", view.u32(0x00485B48), GLOBAL_SELECTED_LIST),
        make_check("command initial state is 0x3E8", view.read(0x00485B70, 7), bytes.fromhex("C7 46 30 E8 03 00 00")),
        make_check("command apply thunk uses vptr+0x30", view.read(COMMAND_APPLY_THUNK, 5), bytes.fromhex("8B 01 FF 60 30")),
        call_check(view, "handler base constructor calls context constructor", 0x004C5194, HANDLER_CONTEXT_CONSTRUCTOR),
        call_check(view, "command middle constructor calls parent constructor", 0x0048A334, COMMAND_PARENT_CONSTRUCTOR),
        call_check(view, "command parent constructor calls global-list base constructor", 0x00487116, COMMAND_BASE_CONSTRUCTOR),
        call_check(view, "command base clears global selected list", 0x00485B5A, LIST_CLEAR),
        make_check("command base copies from the same global selected list", view.u32(0x00485B6C), GLOBAL_SELECTED_LIST),
        call_check(view, "command base copies caller selection into global list", 0x00485B77, LIST_COPY),
        make_check("common validator references global selected list", view.u32(0x0048AB85), GLOBAL_SELECTED_LIST),
        make_check(
            "common validator tests person+0xE8 bit12",
            view.read(0x0048ABCA, 9),
            bytes.fromhex("8B 86 E8 00 00 00 F6 C4 10"),
        ),
        make_check(
            "busy helper passes set=1 and mask=0x1000",
            view.read(0x0048618C, 7),
            bytes.fromhex("6A 01 68 00 10 00 00"),
        ),
        call_check(view, "busy helper calls person flag setter", 0x00486193, PERSON_FLAG_SETTER),
        make_check(
            "person flag setter writes the mask to person+0xE8",
            view.read(0x0044C8A8, 18),
            bytes.fromhex("8B 91 E8 00 00 00 8B 44 24 04 0B D0 89 91 E8 00 00 00"),
        ),
        make_check(
            "fee helper is exactly count*50",
            view.read(FEE_HELPER, 8),
            bytes.fromhex("8B 44 24 04 6B C0 32 C3"),
        ),
        make_check(
            "training apply has no rel32 call to count*50 fee helper",
            find_rel32_call_sites(view, COMMANDS[4].command_apply, TRAIN_APPLY_END, FEE_HELPER),
            [],
        ),
        make_check(
            "training apply has no rel32 call to corps funds subtractor",
            find_rel32_call_sites(
                view,
                COMMANDS[4].command_apply,
                TRAIN_APPLY_END,
                SUBTRACT_CORPS_FUNDS,
            ),
            [],
        ),
        call_check(view, "handler common gate validates handler+0x38 corps", 0x004C52C3, HANDLER_CORPS_VALID),
        make_check("corps vtable +0x40 player-control predicate", view.u32(CORPS_VPTR + 0x40), CORPS_IS_PLAYER_CONTROLLED),
        make_check(
            "corps player-control predicate is (corps+0x34)&1",
            view.read(CORPS_IS_PLAYER_CONTROLLED, 7),
            bytes.fromhex("8B 41 34 83 E0 01 C3"),
        ),
        call_check(view, "handler driver validates returned command", 0x0050EADD, TASK_VALIDATE_AND_INIT),
        call_check(view, "handler driver attaches validated command", 0x0050EAEB, TASK_ATTACH_CHILD),
        call_check(view, "root event attaches factory result", 0x00517BF9, TASK_ATTACH_CHILD),
        make_check(
            "root event block computes command id from ECX eventCode",
            view.read(0x00517BD9, 6),
            bytes.fromhex("8D 81 F0 D8 FF FF"),
        ),
        make_check(
            "root event block dispatches factory with root preserved in ESI",
            view.read(0x00517BE8, 8),
            bytes.fromhex("8B 16 50 8B CE FF 52 28"),
        ),
        call_check(view, "command state reaches apply thunk", 0x00485E17, COMMAND_APPLY_THUNK),
        make_check("encoding path reads scenario index as byte", view.read(0x0045B00D, 6), bytes.fromhex("8A 15 80 24 23 01")),
        make_check("encoding path reads game mode as byte", view.read(0x0045B01C, 5), bytes.fromhex("A0 84 24 23 01")),
    ]

    command_rows: list[dict[str, Any]] = []
    for command in COMMANDS:
        command_alloc_start = command.execute_command_ctor_call - 0x25
        row_checks = [
            make_check(
                f"{command.name}: factory requests 0x64-byte handler allocation",
                view.read(command.root_factory_case, 2),
                bytes.fromhex("6A 64"),
            ),
            call_check(
                view,
                f"{command.name}: handler allocation uses game allocator",
                command.root_factory_case + 2,
                GAME_ALLOCATOR,
            ),
            make_check(
                f"{command.name}: handler vtable +0x0C tick",
                view.u32(command.handler_vptr + 0x0C),
                HANDLER_TICK,
            ),
            make_check(
                f"{command.name}: handler vtable +0x20 CanExecute",
                view.u32(command.handler_vptr + 0x20),
                command.can_execute,
            ),
            make_check(
                f"{command.name}: handler vtable +0x28 Execute UI",
                view.u32(command.handler_vptr + 0x28),
                command.execute_ui,
            ),
            call_check(
                view,
                f"{command.name}: factory calls handler constructor",
                command.root_factory_ctor_call,
                command.handler_constructor,
            ),
            call_check(
                view,
                f"{command.name}: derived handler constructor calls shared base",
                HANDLER_TO_BASE_CALL[command.command_id],
                0x004C5180,
            ),
            call_check(
                view,
                f"{command.name}: Execute opens modal dialog",
                command.execute_dialog_call,
                0x0041FB00,
            ),
            make_check(
                f"{command.name}: Execute requests 0x40-byte command allocation",
                view.read(command_alloc_start, 2),
                bytes.fromhex("6A 40"),
            ),
            call_check(
                view,
                f"{command.name}: command allocation uses game allocator",
                command_alloc_start + 2,
                GAME_ALLOCATOR,
            ),
            call_check(
                view,
                f"{command.name}: Execute calls 0x40-byte command constructor",
                command.execute_command_ctor_call,
                command.command_constructor,
            ),
            call_check(
                view,
                f"{command.name}: derived command constructor calls shared middle constructor",
                COMMAND_TO_MIDDLE_CALL[command.command_id],
                COMMAND_MIDDLE_CONSTRUCTOR,
            ),
            make_check(
                f"{command.name}: command constructor installs expected vptr",
                view.read(command.command_constructor + 0x16, 6),
                b"\xC7\x06" + struct.pack("<I", command.command_vptr),
            ),
            make_check(
                f"{command.name}: command vtable +0x0C state tick",
                view.u32(command.command_vptr + 0x0C),
                COMMAND_TICK,
            ),
            make_check(
                f"{command.name}: command vtable +0x20 validator",
                view.u32(command.command_vptr + 0x20),
                command.command_validator,
            ),
            make_check(
                f"{command.name}: command vtable +0x30 apply",
                view.u32(command.command_vptr + 0x30),
                command.command_apply,
            ),
            call_check(
                view,
                f"{command.name}: apply mutates city stat",
                command.stat_mutation_call,
                command.stat_mutation_target,
            ),
            call_check(
                view,
                f"{command.name}: apply marks selected people busy",
                command.mark_busy_call,
                MARK_SELECTED_BUSY,
            ),
            make_check(
                f"{command.name}: order setter receives set=1 and mask=0x{command.order_mask:X}",
                view.read(command.order_flag_call - 6, 6),
                bytes((0x6A, 0x01, 0x6A, command.order_mask, 0x8B, 0xCE)),
            ),
            call_check(
                view,
                f"{command.name}: apply sets city order flag",
                command.order_flag_call,
                command.order_flag_target,
            ),
        ]
        if command.fee_call is not None and command.subtract_funds_call is not None:
            row_checks.extend(
                (
                    call_check(
                        view,
                        f"{command.name}: apply computes count*50 fee",
                        command.fee_call,
                        FEE_HELPER,
                    ),
                    call_check(
                        view,
                        f"{command.name}: apply subtracts corps funds",
                        command.subtract_funds_call,
                        SUBTRACT_CORPS_FUNDS,
                    ),
                )
            )
        checks.extend(row_checks)
        command_rows.append(
            {
                **asdict(command),
                "checks_ok": all(item["ok"] for item in row_checks),
            }
        )

    return {
        "target": {
            "path": str(view.path),
            "sha256": digest,
            "expected_sha256": TARGET_SHA256,
            "image_base": view.image_base,
        },
        "root_controller": {
            "vptr": ROOT_VPTR,
            "event_dispatch_slot": 0x18,
            "event_dispatch": ROOT_EVENT_DISPATCH,
            "factory_slot": 0x28,
            "factory": ROOT_FACTORY,
            "factory_table": ROOT_FACTORY_TABLE,
            "corps_field": 0x30,
            "state_field": 0x34,
            "target_context_field": 0x38,
            "city_cast": ROOT_CITY_CAST,
            "facility_cast": ROOT_FACILITY_CAST,
            "handler_common_gate": HANDLER_COMMON_GATE,
            "corps_is_player_controlled": CORPS_IS_PLAYER_CONTROLLED,
        },
        "task_lifecycle": {
            "validate_and_init": TASK_VALIDATE_AND_INIT,
            "has_active_child": TASK_HAS_ACTIVE_CHILD,
            "attach_child": TASK_ATTACH_CHILD,
            "handler_execute_driver": HANDLER_EXECUTE_DRIVER,
            "command_tick": COMMAND_TICK,
            "command_apply_thunk": COMMAND_APPLY_THUNK,
            "selected_global": GLOBAL_SELECTED_LIST,
            "selected_head": GLOBAL_SELECTED_HEAD,
            "selected_count": GLOBAL_SELECTED_COUNT,
            "note": (
                "The inspected 0x47E6F0 body handles a task-child relationship and writes "
                "parent+0x10. No persistent domestic-order enqueue was identified in the "
                "inspected path; this is not an exhaustive whole-program side-effect claim."
            ),
        },
        "context_globals": {
            "current_city_ui_pointer": CURRENT_CITY_GLOBAL,
            "scenario_index_low_byte": SCENARIO_INDEX_GLOBAL,
            "game_mode_low_byte": GAME_MODE_GLOBAL,
            "strategic_date_u32": STRATEGIC_DATE_GLOBAL,
        },
        "commands": command_rows,
        "checks": checks,
        "all_ok": all(item["ok"] for item in checks),
        "all_ok_scope": "version_locked_static_anchor_match_only",
        "execution_authorized": False,
        "note": (
            "all_ok means only that the listed on-disk address/version anchors matched "
            "the single captured file image. It does not validate runtime ownership, "
            "thread affinity, UI invariants, or authorize any execution."
        ),
    }


def format_value(value: Any) -> str:
    if isinstance(value, bytes):
        return value.hex(" ")
    if isinstance(value, int) and value >= 0x1000:
        return f"0x{value:08X}"
    return repr(value)


def print_text(report: dict[str, Any]) -> None:
    target = report["target"]
    print("San9PK V3 static command-lifecycle audit")
    print(f"file: {target['path']}")
    print(f"sha256: {target['sha256']}")
    print()
    for check in report["checks"]:
        marker = "PASS" if check["ok"] else "FAIL"
        print(f"[{marker}] {check['name']}")
        if not check["ok"]:
            print(f"       actual={format_value(check['actual'])}")
            print(f"       expected={format_value(check['expected'])}")
    print()
    print(
        "result: "
        + (
            "all listed version-locked static anchors matched"
            if report["all_ok"]
            else "one or more static/version preconditions did not match"
        )
    )
    print(
        "scope: all_ok is address/version evidence only; execution_authorized is always false."
    )
    print(
        "observed boundary: the inspected 0x47E6F0 path writes a task-child relation; "
        "no persistent domestic-order enqueue was identified in the inspected path."
    )


def disassemble_windows(view: PEView) -> None:
    try:
        import capstone
    except ImportError as error:  # pragma: no cover - optional display mode
        raise SystemExit("capstone is required for --disassemble") from error

    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    for name, address, size in ANCHOR_WINDOWS:
        print()
        print(f"[{name}] 0x{address:08X}..0x{address + size:08X}")
        for instruction in decoder.disasm(view.read(address, size), address):
            print(f"  {instruction.address:08X}  {instruction.mnemonic:<7} {instruction.op_str}")


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, default=TARGET_EXE, help="path to the on-disk San9PK.exe")
    output = parser.add_mutually_exclusive_group()
    output.add_argument("--json", action="store_true", help="emit JSON")
    output.add_argument("--disassemble", action="store_true", help="print short, fixed anchor windows")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    try:
        # One immutable capture is used for hashing, PE parsing, anchor checks and
        # optional disassembly.  The path is never reopened, eliminating a
        # hash-versus-analysis TOCTOU window.
        data = args.exe.read_bytes()
    except OSError as error:
        report = fail_closed_report(args.exe, None, "file read", str(error), "readable file")
        if args.json:
            print(json.dumps(report, ensure_ascii=False, indent=2, default=format_value))
        else:
            print_text(report)
        return 2

    digest = hashlib.sha256(data).hexdigest()
    if digest.lower() != TARGET_SHA256:
        report = fail_closed_report(args.exe, digest, "SHA-256", digest.lower(), TARGET_SHA256)
        if args.json:
            print(json.dumps(report, ensure_ascii=False, indent=2, default=format_value))
        else:
            print_text(report)
        return 1

    try:
        view = PEView(args.exe, data)
    except Exception as error:  # pefile exposes several format-specific exceptions
        report = fail_closed_report(args.exe, digest, "PE parse", type(error).__name__, "valid PE32")
        if args.json:
            print(json.dumps(report, ensure_ascii=False, indent=2, default=format_value))
        else:
            print_text(report)
        return 1

    if view.image_base != IMAGE_BASE:
        report = fail_closed_report(
            args.exe,
            digest,
            "PE ImageBase",
            view.image_base,
            IMAGE_BASE,
        )
        if args.json:
            print(json.dumps(report, ensure_ascii=False, indent=2, default=format_value))
        else:
            print_text(report)
        return 1

    try:
        report = verify(view, digest)
    except Exception as error:
        report = fail_closed_report(
            args.exe,
            digest,
            "static anchor inspection",
            f"{type(error).__name__}: {error}",
            "all reads within the version-locked image",
        )
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2, default=format_value))
    else:
        print_text(report)
    if args.disassemble and report["all_ok"]:
        disassemble_windows(view)
    return 0 if report["all_ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
