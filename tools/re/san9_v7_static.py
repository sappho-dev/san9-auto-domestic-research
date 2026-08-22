#!/usr/bin/env python3
"""Offline-only San9PK 1.0.1.0 native-UI static audit.

The tool reads one on-disk executable and verifies version-locked instruction
and vtable anchors for the V7 native-UI research note.  It has no live mode:
it never enumerates or opens a process, calls a game function, sends input, or
writes a file/process.  Matching anchors are evidence, not execution authority.

Examples:
    python tools/re/san9_v7_static.py
    python tools/re/san9_v7_static.py --json
    python tools/re/san9_v7_static.py --disassemble
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

try:
    import pefile
except ImportError as error:  # pragma: no cover - dependency gate
    raise SystemExit("pefile is required: python -m pip install pefile") from error


TARGET_EXE = Path(r"D:\三国志9\10101749\San9PK.exe")
TARGET_SHA256 = "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028"
TARGET_SIZE = 2_636_800
MACHINE_I386 = 0x014C
PE32_MAGIC = 0x010B
IMAGE_BASE = 0x00400000
SIZE_OF_IMAGE = 0x01759000

ROOT_VPTR = 0x00610BC8
ROOT_INPUT_EVENT = 0x00514370
ROOT_TARGET_MENU = 0x00513890
ROOT_HIT_RESOLVER = 0x005135F0
ROOT_EVENT_DISPATCH = 0x005179B0
ROOT_FACTORY = 0x005125A0
ROOT_TEMP_CONSTRUCTOR = 0x0050F3C0
ROOT_TEMP_DESTRUCTOR = 0x0050F400
ROOT_FACTORY_FROM_EVENT = 0x0050FC90
ROOT_MENU_QUERY = 0x00514300
CURRENT_CITY_GLOBAL = 0x01232474

FORCE_SELECTION_MANAGER = 0x01232688
FORCE_RUNTIME_CLASS = 0x00605C2C
FORCE_RUNTIME_NAME = 0x00605C44
CITY_RUNTIME_CLASS = 0x00605460
CITY_RUNTIME_NAME = 0x00605928
CITY_BY_ID = 0x00452F70
CITY_VPTR = 0x00605938
CITY_RUNTIME_CLASS_VIRTUAL = 0x0043A2A0
CITY_DYNAMIC_CAST_FROM_ROOT_TARGET = 0x0050FCD0
FORCE_SELECTION_PEEK = 0x0046AAD0
FORCE_SELECTION_REPLACE = 0x0046AB10
FORCE_SELECTION_ADVANCE = 0x0046AC10
FORCE_TYPED_LIST_CONSTRUCTOR = 0x0046DF70
GAME_DATA_LIST_COPY = 0x0046F200
FORCE_TABLE_BY_ID = 0x00453100
FORCE_DIALOG_VPTR = 0x006079D0
FORCE_DIALOG_APPLY = 0x00487D20
FORCE_SELECTION_CONTROLLER_VPTR = 0x00610B80
FORCE_SELECTION_TICK = 0x0050EC90
FORCE_SELECTION_GET_CURRENT = 0x0050EC10
ROOT_FROM_FORCE_CONSTRUCTOR = 0x0050F380
ROOT_FROM_TARGET_CONSTRUCTOR = 0x0050F3C0
COMMERCE_HANDLER_CONSTRUCTOR = 0x004C61F0
COMMERCE_HANDLER_VPTR = 0x00609F38

COMMAND_MENU_CONSTRUCTOR = 0x004C9150
COMMAND_MENU_VPTR = 0x0060A238
COMMAND_MENU_QUERY = 0x004C9320
MODAL_ENTRY = 0x0041FB00
TASK_VALIDATE = 0x0047E510
TASK_ATTACH = 0x0047E6F0
TASK_IDLE_TEST = 0x0047E420

LIST_CLEAR = 0x005180E0
LIST_COPY_BOUNDED = 0x0046EF80
PERSON_LIST_CONSTRUCTOR = 0x00470DF0
SELECTOR_WRAPPER = 0x00570500
SELECTOR_CORE = 0x00570150
SELECTOR_FACTORY = 0x0056EA20
SELECTOR_TYPE_A_CASE = 0x0056ED4D
SELECTOR_TYPE_A_CONSTRUCTOR = 0x0056A230
SELECTOR_DIALOG_VPTR = 0x0061F0B8
SELECTOR_DIALOG_EVENT = 0x00578090
SELECTOR_DIALOG_ACCEPT = 0x00577170
SELECTOR_ROW_TOGGLE = 0x005726C0
SELECTOR_PREFIX_FILL = 0x00575A30
SELECTOR_CLEAR = 0x00573170
SELECTOR_SOURCE_TO_ROWS = 0x005727C0
SELECTOR_ROW_EVENT_DRIVER = 0x00572250

FRAMEWORK_MODAL_DRIVER = 0x005B8480
FRAMEWORK_CREATE_DIALOG = 0x005B8330
FRAMEWORK_CREATE_WINDOW = 0x005CD650
FRAMEWORK_INSTALL_CBT_OBJECT = 0x005CD5E0
FRAMEWORK_CBT_HOOK = 0x005CD540
FRAMEWORK_BIND_HWND = 0x005CD480
FRAMEWORK_MODAL_TRACK = 0x005B7B80
FRAMEWORK_TRACK_TOP_WRAPPER = 0x005CF050
FRAMEWORK_TOP_HWND = 0x005CA7D0
FRAMEWORK_PERMANENT_CWND_FROM_HWND = 0x005CD460
FRAMEWORK_TRACKED_HWND_PUSH = 0x005CCB10
SEND_MESSAGE_WRAPPER = 0x005CF150
POST_MESSAGE_WRAPPER = 0x005CF180
POST_MESSAGE_FALLBACK = 0x005CA9F0
SEND_MESSAGE_IAT = 0x005FF3CC
POST_MESSAGE_IAT = 0x005FF460
WM_COMMAND = 0x0111

TASK_ACCEPT_BASE_EVENT = 0x004CB4C0
TASK_ACCEPT_COPY = 0x004E6C80
TASK_MODAL_COMPLETE = 0x004CB480


@dataclass(frozen=True)
class SelectionTask:
    command_id: int
    name: str
    vptr: int
    event_handler: int
    selector_call: int
    base_event_call: int
    validator_callback: int


TASKS: tuple[SelectionTask, ...] = (
    SelectionTask(0, "巡察", 0x0060B920, 0x004D8BA0, 0x004D8C1A, 0x004D8C66, 0x004D87F0),
    SelectionTask(1, "商业", 0x0060CCB0, 0x004E72D0, 0x004E734A, 0x004E7396, 0x004DADC0),
    SelectionTask(2, "开垦", 0x0060BBA0, 0x004DA670, 0x004DA6EA, 0x004DA736, 0x004DADC0),
    SelectionTask(3, "修筑", 0x0060BCE8, 0x004DB170, 0x004DB1EA, 0x004DB236, 0x004DADC0),
    SelectionTask(5, "训练", 0x0060C370, 0x004DF8E0, 0x004DF8C1, 0x004DF94C, 0),
)


class PEView:
    def __init__(self, path: Path, data: bytes) -> None:
        self.path = path
        self.data = data
        self.pe = pefile.PE(data=data, fast_load=False)
        self.image_base = int(self.pe.OPTIONAL_HEADER.ImageBase)

    def va_to_offset(self, va: int) -> int:
        if va < self.image_base:
            raise ValueError(f"VA 0x{va:08X} is below ImageBase")
        return int(self.pe.get_offset_from_rva(va - self.image_base))

    def read(self, va: int, size: int) -> bytes:
        if size < 0:
            raise ValueError("negative read size")
        offset = self.va_to_offset(va)
        end = offset + size
        if offset < 0 or end > len(self.data):
            raise ValueError(f"VA window 0x{va:08X}+0x{size:X} is outside the file")
        return self.data[offset:end]

    def u32(self, va: int) -> int:
        return struct.unpack("<I", self.read(va, 4))[0]

    def rel32_target(self, va: int, opcode: int = 0xE8) -> int | None:
        raw = self.read(va, 5)
        if raw[0] != opcode:
            return None
        return va + 5 + struct.unpack("<i", raw[1:])[0]

    def raw_dword_refs(self, value: int) -> list[int]:
        return self.raw_byte_refs(struct.pack("<I", value))

    def raw_byte_refs(self, pattern: bytes) -> list[int]:
        if not pattern:
            raise ValueError("empty byte pattern")
        result: list[int] = []
        start = 0
        while True:
            offset = self.data.find(pattern, start)
            if offset < 0:
                break
            try:
                rva = int(self.pe.get_rva_from_offset(offset))
            except Exception:
                start = offset + 1
                continue
            result.append(self.image_base + rva)
            start = offset + 1
        return result

    def import_name_at_iat(self, va: int) -> tuple[str, str] | None:
        for descriptor in self.pe.DIRECTORY_ENTRY_IMPORT:
            dll = descriptor.dll.decode("ascii", errors="replace").lower()
            for imported in descriptor.imports:
                if int(imported.address) != va:
                    continue
                name = "" if imported.name is None else imported.name.decode("ascii", errors="replace")
                return dll, name
        return None


def check(name: str, actual: Any, expected: Any) -> dict[str, Any]:
    return {"name": name, "ok": actual == expected, "actual": actual, "expected": expected}


def call_check(view: PEView, name: str, site: int, target: int) -> dict[str, Any]:
    return check(name, view.rel32_target(site), target)


def rel32_calls(view: PEView, start: int, end: int, target: int) -> list[int]:
    raw = view.read(start, end - start)
    hits: list[int] = []
    for offset in range(max(0, len(raw) - 4)):
        if raw[offset] != 0xE8:
            continue
        relative = struct.unpack_from("<i", raw, offset + 1)[0]
        site = start + offset
        if site + 5 + relative == target:
            hits.append(site)
    return hits


def fail_report(path: Path, digest: str | None, reason: str, actual: Any, expected: Any) -> dict[str, Any]:
    return {
        "scope": "exact-version on-disk static native-UI evidence only",
        "target": {"path": str(path), "sha256": digest, "expected_sha256": TARGET_SHA256},
        "checks": [check(reason, actual, expected)],
        "all_static_anchors_ok": False,
        "native_target_binding_route_confirmed": False,
        "native_city_target_setter_found": False,
        "transient_root_target_write_authorized": False,
        "attached_heap_root_authorized": False,
        "force_selection_manager_route_static_confirmed": False,
        "force_selection_manager_is_city_target_route": False,
        "can_execute_and_dispatch_atomic_entry_found": False,
        "active_modal_selector_accessor_static_confirmed": False,
        "postmessage_staging_candidate_static_confirmed": False,
        "persistent_selector_object_accessor_found": False,
        "stable_selector_object_accessor_found": False,
        "business_bridge_go": False,
        "business_execution_authorized": False,
        "process_accessed": False,
        "live_mode_present": False,
    }


def verify(view: PEView, digest: str) -> dict[str, Any]:
    checks: list[dict[str, Any]] = [
        check("file size", len(view.data), TARGET_SIZE),
        check("SHA-256", digest.lower(), TARGET_SHA256),
        check("machine I386", int(view.pe.FILE_HEADER.Machine), MACHINE_I386),
        check("PE32 optional header", int(view.pe.OPTIONAL_HEADER.Magic), PE32_MAGIC),
        check("ImageBase", view.image_base, IMAGE_BASE),
        check("SizeOfImage", int(view.pe.OPTIONAL_HEADER.SizeOfImage), SIZE_OF_IMAGE),
        check("root vtable +0x18 event dispatcher", view.u32(ROOT_VPTR + 0x18), ROOT_EVENT_DISPATCH),
        check("root vtable +0x1C native input event", view.u32(ROOT_VPTR + 0x1C), ROOT_INPUT_EVENT),
        check("root vtable +0x28 handler factory", view.u32(ROOT_VPTR + 0x28), ROOT_FACTORY),
        check(
            "root native input event recognizes event 0x7D1",
            view.read(0x00514370, 0x0B),
            bytes.fromhex("8B 44 24 04 2D D1 07 00 00 74 12"),
        ),
        call_check(view, "event 0x7D1 enters target/menu path", 0x00514392, ROOT_TARGET_MENU),
        call_check(view, "target/menu path first enforces root idle", 0x005138B5, TASK_IDLE_TEST),
        call_check(view, "target/menu path resolves hit object", 0x0051390F, ROOT_HIT_RESOLVER),
        check(
            "resolved object is assigned to live root+0x38",
            view.read(0x00513914, 7),
            bytes.fromhex("50 89 47 38 E8 23 1F"),
        ),
        check(
            "menu retry re-resolves and rebinds root+0x38",
            view.read(0x00513A76, 0x10),
            bytes.fromhex("8D 44 24 10 50 E8 70 FB FF FF 50 89 47 38 E8 B7"),
        ),
        check("current-city global is written after target binding", view.u32(0x00513970), CURRENT_CITY_GLOBAL),
        check(
            "all direct absolute current-city references remain the reviewed set",
            view.raw_dword_refs(CURRENT_CITY_GLOBAL),
            [0x0043FF27, 0x00451646, 0x00460936, 0x00513970],
        ),
        check("CForceData runtime class names itself", view.u32(FORCE_RUNTIME_CLASS), FORCE_RUNTIME_NAME),
        check("CForceData runtime name", view.read(FORCE_RUNTIME_NAME, 11), b"CForceData\x00"),
        check("CCityData runtime class is distinct", view.u32(CITY_RUNTIME_CLASS), CITY_RUNTIME_NAME),
        check("CCityData runtime name", view.read(CITY_RUNTIME_NAME, 10), b"CCityData\x00"),
        check(
            "city-id lookup bounds 50 CCityData entries of size 0x1F0",
            view.read(CITY_BY_ID, 0x1C),
            bytes.fromhex("8B 44 24 04 85 C0 7C 11 83 F8 32 7D 0C 69 C0 F0 01 00 00 05 58 DB 24 01 C3 33 C0 C3"),
        ),
        check("CCityData exact vptr exposes its runtime class", view.u32(CITY_VPTR), CITY_RUNTIME_CLASS_VIRTUAL),
        check(
            "CCityData runtime-class virtual returns exact class",
            view.read(CITY_RUNTIME_CLASS_VIRTUAL, 6),
            bytes.fromhex("B8 60 54 60 00 C3"),
        ),
        check("CCityData constructor installs exact vptr", view.u32(0x0043A2E2), CITY_VPTR),
        check("force typed-list constructor pins CForceData", view.u32(0x0046DF72), FORCE_RUNTIME_CLASS),
        check(
            "force-selection peek gates on non-empty manager+0x38 list",
            view.read(FORCE_SELECTION_PEEK, 0x13),
            bytes.fromhex("8B 41 44 85 C0 75 03 33 C0 C3 8B 41 3C 56 8B 70 08 85 F6"),
        ),
        check("force-selection peek requires CForceData runtime class", view.u32(0x0046AAF1), FORCE_RUNTIME_CLASS),
        call_check(view, "force-selection peek performs runtime-class check", 0x0046AAF7, 0x005DED60),
        check(
            "force-selection replacement targets manager+0x38 typed list",
            view.read(FORCE_SELECTION_REPLACE, 3),
            bytes.fromhex("83 C1 38"),
        ),
        check(
            "force-selection replacement tail-calls generic typed-list copy",
            view.rel32_target(0x0046AB13, opcode=0xE9),
            GAME_DATA_LIST_COPY,
        ),
        check(
            "all force-selection replacement call sites remain reviewed",
            rel32_calls(view, 0x00401000, 0x005FEE00, FORCE_SELECTION_REPLACE),
            [0x00457BE8, 0x00487E4E, 0x00488299, 0x0050F041],
        ),
        check(
            "all force-selection advance call sites remain reviewed",
            rel32_calls(view, 0x00401000, 0x005FEE00, FORCE_SELECTION_ADVANCE),
            [0x0050ECDC],
        ),
        check(
            "all force-selection peek call sites remain reviewed",
            rel32_calls(view, 0x00401000, 0x005FEE00, FORCE_SELECTION_PEEK),
            [
                0x00413E42, 0x00413EE4, 0x00437BCD, 0x00439BB2, 0x0044488A,
                0x004454EC, 0x00450C40, 0x00451990, 0x0045AF68, 0x0045C538,
                0x0045C63B, 0x00463496, 0x0046973C, 0x004FC9F7, 0x0050EDB7,
                0x00511869, 0x00511BA3, 0x0051D514, 0x005219F9, 0x0052444C,
            ],
        ),
        check("force-selection advance valid-object path tail-returns next force", view.rel32_target(0x0046AC53, opcode=0xE9), FORCE_SELECTION_PEEK),
        check("force-selection advance invalid-object path tail-returns next force", view.rel32_target(0x0046AC67, opcode=0xE9), FORCE_SELECTION_PEEK),
        call_check(view, "load path constructs a CForceData typed list", 0x00457BA2, FORCE_TYPED_LIST_CONSTRUCTOR),
        call_check(view, "load path resolves force-table entry by index", 0x00457BBA, FORCE_TABLE_BY_ID),
        check(
            "force-table lookup bounds 50 entries of size 0xD4",
            view.read(FORCE_TABLE_BY_ID, 0x19),
            bytes.fromhex("8B 44 24 04 85 C0 7C 11 83 F8 32 7D 0C 69 C0 D4 00 00 00 05 38 3C 25 01 C3"),
        ),
        call_check(view, "force-dialog first path constructs a CForceData typed list", 0x00487E1C, FORCE_TYPED_LIST_CONSTRUCTOR),
        call_check(view, "force-dialog second path constructs a CForceData typed list", 0x00488253, FORCE_TYPED_LIST_CONSTRUCTOR),
        check("force-dialog virtual apply entry", view.u32(FORCE_DIALOG_VPTR + 0x30), FORCE_DIALOG_APPLY),
        call_check(view, "force-controller path constructs a CForceData typed list", 0x0050EE0A, FORCE_TYPED_LIST_CONSTRUCTOR),
        check("force-controller list callback requires CForceData", view.u32(0x0050EC38), FORCE_RUNTIME_CLASS),
        check("force-selection controller vtable tick", view.u32(FORCE_SELECTION_CONTROLLER_VPTR + 0x0C), FORCE_SELECTION_TICK),
        call_check(view, "force-selection state 0x3E9 advances its CForceData list", 0x0050ECDC, FORCE_SELECTION_ADVANCE),
        check(
            "force-selection getter fixes ECX to the force manager",
            view.read(FORCE_SELECTION_GET_CURRENT, 5),
            bytes.fromhex("B9 88 26 23 01"),
        ),
        check(
            "force-selection getter tail-calls CForceData peek",
            view.rel32_target(0x0050EC15, opcode=0xE9),
            FORCE_SELECTION_PEEK,
        ),
        call_check(view, "force-selection tick reads the selected CForceData", 0x0050F16E, FORCE_SELECTION_GET_CURRENT),
        call_check(view, "force-selection tick constructs a normal root from selected force", 0x0050F1B6, ROOT_FROM_FORCE_CONSTRUCTOR),
        call_check(view, "root-from-force constructor delegates selected force to base", 0x0050F38F, 0x004BDF10),
        check(
            "root-from-force base stores CForceData at root+0x30",
            view.read(0x004BDF21, 0x0B),
            bytes.fromhex("C7 06 F8 98 60 00 89 4E 30 8B C6"),
        ),
        check(
            "root-from-force leaves root+0x38 target null",
            view.read(0x0050F39E, 0x0E),
            bytes.fromhex("C7 46 34 E8 03 00 00 C7 46 38 00 00 00 00"),
        ),
        check(
            "root-from-force stores its separate third context at root+0x3C",
            view.read(0x0050F3AC, 3),
            bytes.fromhex("89 56 3C"),
        ),
        call_check(view, "force-root is attached only after construction", 0x0050F1F3, TASK_ATTACH),
        check(
            "root target accessor dynamically requires CCityData",
            view.read(CITY_DYNAMIC_CAST_FROM_ROOT_TARGET, 0x12),
            bytes.fromhex("8B 41 38 50 68 60 54 60 00 E8 92 F0 0C 00 83 C4 08 C3"),
        ),
        call_check(view, "commerce factory reads CCityData from root+0x38", 0x00512925, CITY_DYNAMIC_CAST_FROM_ROOT_TARGET),
        check("commerce factory constructs expected handler", view.rel32_target(0x0051292F), COMMERCE_HANDLER_CONSTRUCTOR),
        check("commerce handler installs expected vptr", view.u32(0x004C622E), COMMERCE_HANDLER_VPTR),
        check(
            "commerce handler snapshots its validated CCityData at handler+0x60",
            view.read(0x004C6237, 0x20),
            bytes.fromhex("8B 7C 24 24 57 C6 44 24 18 01 E8 FA F5 F3 FF 8B 4C 24 10 83 C4 04 F7 D8 1B C0 23 C7 89 46 60 5F"),
        ),
        call_check(view, "heap root-with-target constructor delegates force to base", 0x0050F3CF, 0x004BDF10),
        check(
            "heap root-with-target constructor writes target only at root+0x38",
            view.read(0x0050F3D4, 0x1B),
            bytes.fromhex("8B 54 24 10 C7 06 C8 0B 61 00 C7 46 34 E8 03 00 00 89 56 38 C7 46 3C 00 00 00 00"),
        ),
        call_check(view, "target path constructs native command menu", 0x00513A08, COMMAND_MENU_CONSTRUCTOR),
        call_check(view, "target path runs native command menu modal", 0x00513A1C, MODAL_ENTRY),
        check(
            "menu return dispatches through live root vtable+0x18",
            view.read(0x00513A96, 8),
            bytes.fromhex("8B 17 53 FF 52 18 C7 44"),
        ),
        check("command menu installs expected vptr", view.u32(0x004C91B4), COMMAND_MENU_VPTR),
        check("command menu vtable +0xF8 is availability query", view.u32(COMMAND_MENU_VPTR + 0xF8), COMMAND_MENU_QUERY),
        call_check(view, "menu availability query constructs temporary target-bound root", 0x004C9355, ROOT_TEMP_CONSTRUCTOR),
        call_check(view, "menu availability query invokes root query driver", 0x004C9371, ROOT_MENU_QUERY),
        call_check(view, "menu availability query destroys temporary root", 0x004C9387, ROOT_TEMP_DESTRUCTOR),
        call_check(view, "root query maps event through vtable+0x28 factory", 0x00514318, ROOT_FACTORY_FROM_EVENT),
        check(
            "factory-from-event subtracts 0x2710 and calls root vtable+0x28",
            view.read(ROOT_FACTORY_FROM_EVENT, 0x10),
            bytes.fromhex("8B 54 24 08 8B 01 81 C2 F0 D8 FF FF 52 FF 50 28"),
        ),
        call_check(view, "menu query runs handler vtable+0x20 via 0x47E510", 0x00514327, TASK_VALIDATE),
        check(
            "menu query destroys the temporary handler",
            view.read(0x0051433E, 8),
            bytes.fromhex("8B 07 6A 01 8B CF FF 10"),
        ),
        check(
            "real command dispatch has no rel32 call to CanExecute driver",
            rel32_calls(view, 0x00517BD9, 0x00517C05, TASK_VALIDATE),
            [],
        ),
        call_check(view, "real command dispatch attaches its separately-created handler", 0x00517BF9, TASK_ATTACH),
        check(
            "real command dispatch sets live root state after attach",
            view.read(0x00517BFE, 7),
            bytes.fromhex("C7 46 34 EA 03 00 00"),
        ),
        check("commerce task vptr constructor write", view.u32(0x004E6B35), 0x0060CCB0),
        call_check(view, "commerce committed list constructor", 0x004E6B39, PERSON_LIST_CONSTRUCTOR),
        call_check(view, "commerce source list constructor", 0x004E6B4B, PERSON_LIST_CONSTRUCTOR),
        call_check(view, "commerce working list constructor", 0x004E6B5D, PERSON_LIST_CONSTRUCTOR),
        check("selector type A jump-table case", view.u32(0x0056EF28 + 0x0A * 4), SELECTOR_TYPE_A_CASE),
        check("selector type A allocation is 0x5F44 bytes", view.read(SELECTOR_TYPE_A_CASE, 5), bytes.fromhex("68 44 5F 00 00")),
        call_check(view, "selector type A constructor", 0x0056ED88, SELECTOR_TYPE_A_CONSTRUCTOR),
        check("selector dialog installs expected vptr", view.u32(0x0056A287), SELECTOR_DIALOG_VPTR),
        check("selector dialog vtable +0x28 event entry", view.u32(SELECTOR_DIALOG_VPTR + 0x28), SELECTOR_DIALOG_EVENT),
        check("selector dialog vtable +0x80 enters framework modal", view.u32(SELECTOR_DIALOG_VPTR + 0x80), MODAL_ENTRY),
        check("selector dialog vtable +0x84 accept", view.u32(SELECTOR_DIALOG_VPTR + 0x84), SELECTOR_DIALOG_ACCEPT),
        check("selector dialog vtable +0xF0 row toggle", view.u32(SELECTOR_DIALOG_VPTR + 0xF0), SELECTOR_ROW_TOGGLE),
        check("selector dialog vtable +0x15C prefix fill", view.u32(SELECTOR_DIALOG_VPTR + 0x15C), SELECTOR_PREFIX_FILL),
        check("selector dialog vtable +0x160 clear", view.u32(SELECTOR_DIALOG_VPTR + 0x160), SELECTOR_CLEAR),
        check("dialog event 0x1D4D maps to accept block", view.u32(0x0057826C), 0x005780AE),
        check("dialog event 0x1D51 maps to prefix-fill block", view.u32(0x0057827C), 0x00578134),
        check("dialog event 0x1D52 maps to clear block", view.u32(0x00578280), 0x00578148),
        check(
            "dialog event 0x1D4D calls vtable+0x84",
            view.read(0x005780AE, 0x0A),
            bytes.fromhex("8B 06 8B CE FF 90 84 00 00 00"),
        ),
        check(
            "dialog event 0x1D51 calls vtable+0x15C",
            view.read(0x00578134, 0x0A),
            bytes.fromhex("8B 16 8B CE FF 92 5C 01 00 00"),
        ),
        check(
            "dialog event 0x1D52 calls vtable+0x160",
            view.read(0x00578148, 0x0A),
            bytes.fromhex("8B 06 8B CE FF 90 60 01 00 00"),
        ),
        call_check(view, "clear iterates rows through native selection helper", 0x00573197, 0x00572550),
        check(
            "clear passes row index with zero state and zero operation",
            view.read(0x00573190, 0x0C),
            bytes.fromhex("6A 00 6A 00 56 8B CF E8 B4 F3 FF FF"),
        ),
        check(
            "clear row helper tail-calls whole-row state helper",
            view.rel32_target(0x00572556, opcode=0xE9),
            0x0056AA30,
        ),
        call_check(view, "visible row event range enters row-event driver", 0x005781E8, SELECTOR_ROW_EVENT_DRIVER),
        check(
            "row-event driver calls vtable+0xF0 toggle",
            view.read(0x00572297, 0x0F),
            bytes.fromhex("8B 16 8D 44 24 10 50 8B CE FF 92 F0 00 00 00"),
        ),
        call_check(view, "source chain row population appends in traversal order", 0x005727EB, 0x0056A6A0),
        check(
            "prefix-fill derives remaining capacity from dialog+0x180",
            view.read(0x00575A9E, 0x0A),
            bytes.fromhex("8B 86 80 01 00 00 2B C3 8B E8"),
        ),
        check(
            "prefix-fill tests optional combination-validator pointer",
            view.read(0x00575ABC, 0x0A),
            bytes.fromhex("8B 86 84 01 00 00 85 C0 74 24"),
        ),
        call_check(view, "prefix-fill obtains the candidate row object", 0x00575AC9, 0x0056A6C0),
        check(
            "prefix-fill indirectly calls optional combination validator",
            view.read(0x00575AD8, 0x10),
            bytes.fromhex("8D 4C 24 14 51 FF 96 84 01 00 00 83 C4 04 85 C0"),
        ),
        call_check(view, "prefix-fill marks selected row through native row helper", 0x00575AF1, 0x0056AA30),
        check(
            "task event 0xBB9 calls task vtable+0x84",
            view.read(TASK_ACCEPT_BASE_EVENT, 0x13),
            bytes.fromhex("8B 44 24 04 3D B9 0B 00 00 75 10 8B 01 FF 90 84 00 00 00"),
        ),
        call_check(view, "task accept clears committed list", 0x004E6C8C, LIST_CLEAR),
        call_check(view, "task accept copies working list to committed list", 0x004E6C9F, LIST_COPY_BOUNDED),
        check("task accept tail-calls modal completion", view.rel32_target(0x004E6CA8, opcode=0xE9), TASK_MODAL_COMPLETE),
        check(
            "modal completion sets task+0x680 result to 1",
            view.read(0x004CB483, 0x10),
            bytes.fromhex("E8 D8 4D F5 FF C7 86 80 06 00 00 01 00 00 00 5E"),
        ),
        check(
            "prefix-fill virtual slot has one exact call site",
            view.raw_byte_refs(bytes.fromhex("FF 92 5C 01 00 00")),
            [0x00578138],
        ),
        check(
            "clear virtual slot has one exact call site",
            view.raw_byte_refs(bytes.fromhex("FF 90 60 01 00 00")),
            [0x0057814C],
        ),
        check(
            "prefix-fill implementation has no direct rel32 caller in text",
            rel32_calls(view, 0x00401000, 0x005FEE00, SELECTOR_PREFIX_FILL),
            [],
        ),
        check(
            "clear implementation has no direct rel32 caller in text",
            rel32_calls(view, 0x00401000, 0x005FEE00, SELECTOR_CLEAR),
            [],
        ),
        check(
            "inner accept implementation has no direct rel32 caller in text",
            rel32_calls(view, 0x00401000, 0x005FEE00, SELECTOR_DIALOG_ACCEPT),
            [],
        ),
        check(
            "selector core retains factory result and synchronously enters its modal slot",
            view.read(0x005701D0, 0x31),
            bytes.fromhex(
                "E8 4B E8 FF FF 8B F0 83 C4 14 33 FF 85 F6 74 47 8B 44 24 2C 53 50 "
                "8B CE E8 A3 60 00 00 8B 4C 24 30 8B 16 89 8E 84 01 00 00 8B CE FF 92 80 00 00 00"
            ),
        ),
        check("framework modal entry tail-calls modal driver", view.rel32_target(0x0041FB1A, opcode=0xE9), FRAMEWORK_MODAL_DRIVER),
        call_check(view, "modal driver constructs dialog window", 0x005B8551, FRAMEWORK_CREATE_DIALOG),
        call_check(view, "dialog creation reaches framework CreateWindow path", 0x005B840F, FRAMEWORK_CREATE_WINDOW),
        call_check(view, "CreateWindow path registers exact CWnd for CBT binding", 0x005CD6DF, FRAMEWORK_INSTALL_CBT_OBJECT),
        call_check(view, "CBT hook binds the pending exact CWnd to HWND", 0x005CD590, FRAMEWORK_BIND_HWND),
        check(
            "HWND binding stores CWnd+4 then enters permanent map insertion",
            view.read(0x005CD49D, 0x0B),
            bytes.fromhex("57 8B C8 89 7E 04 E8 E8 57 00 00"),
        ),
        call_check(view, "dialog creation enters modal HWND tracking", 0x005B846E, FRAMEWORK_MODAL_TRACK),
        call_check(view, "modal tracker normalizes and tracks dialog HWND", 0x005B7B9E, FRAMEWORK_TRACK_TOP_WRAPPER),
        call_check(view, "tracked-HWND transition reads previous stack top", 0x005CB0E1, FRAMEWORK_TOP_HWND),
        call_check(view, "tracked-HWND transition updates the framework stack", 0x005CB0F2, 0x005CCBA0),
        call_check(view, "tracked-HWND update pushes the new top", 0x005CCBBE, FRAMEWORK_TRACKED_HWND_PUSH),
        check(
            "top-HWND accessor returns tracked stack[count-1] or null",
            view.read(FRAMEWORK_TOP_HWND, 0x14),
            bytes.fromhex("A1 90 18 B4 01 85 C0 7E 08 8B 04 85 DC 23 B4 01 C3 33 C0 C3"),
        ),
        call_check(view, "permanent CWnd getter requests the no-create handle map", 0x005CD462, 0x005CD1A0),
        check(
            "permanent CWnd getter tail-calls map lookup and consumes HWND",
            view.read(0x005CD46C, 0x0E),
            bytes.fromhex("33 C0 85 C9 74 05 E9 E9 55 00 00 C2 04 00"),
        ),
        check(
            "selector keyboard path sends native WM_COMMAND 0x1D51 to its own HWND",
            view.read(0x00577AEB, 0x15),
            bytes.fromhex("8B 46 04 6A 00 68 51 1D 00 00 68 11 01 00 00 50 E8 50 76 05 00"),
        ),
        check(
            "selector keyboard path sends native WM_COMMAND 0x1D52 to its own HWND",
            view.read(0x00577B64, 0x15),
            bytes.fromhex("8B 46 04 6A 00 68 52 1D 00 00 68 11 01 00 00 50 E8 D7 75 05 00"),
        ),
        check("native message wrapper imports SendMessageA", view.import_name_at_iat(SEND_MESSAGE_IAT), ("user32.dll", "SendMessageA")),
        check("staging candidate wrapper imports PostMessageA", view.import_name_at_iat(POST_MESSAGE_IAT), ("user32.dll", "PostMessageA")),
        check(
            "SendMessage wrapper has exact USER32 fast-path thunk",
            view.read(0x005CF169, 6),
            bytes.fromhex("FF 25 CC F3 5F 00"),
        ),
        check(
            "PostMessage wrapper has exact USER32 fast-path thunk",
            view.read(0x005CF199, 6),
            bytes.fromhex("FF 25 60 F4 5F 00"),
        ),
        check(
            "ordinary-dialog PostMessage wrapper enters framework packet queue",
            view.rel32_target(0x005CF1A3, opcode=0xE9),
            POST_MESSAGE_FALLBACK,
        ),
        check(
            "framework async envelope allocates a 0x1C-byte packet",
            view.read(0x005CAA62, 7),
            bytes.fromhex("6A 1C E8 CD A6 00 00"),
        ),
        check(
            "framework async envelope stores target/message/wParam/lParam",
            view.read(0x005CAA76, 0x22),
            bytes.fromhex(
                "56 57 89 3E 89 5E 04 89 56 08 89 46 0C C7 46 10 00 00 00 00 "
                "C7 46 14 00 00 00 00 C7 46 18 00 00 00 00"
            ),
        ),
        check(
            "framework async envelope posts message 0x601 to main HWND",
            view.read(0x005CAA98, 0x12),
            bytes.fromhex("8B 0D 00 08 B4 01 68 01 06 00 00 51 FF 15 60 F4 5F 00"),
        ),
    ]

    task_rows: list[dict[str, Any]] = []
    for task in TASKS:
        row_checks = [
            check(f"{task.name}: vtable +0x28 event handler", view.u32(task.vptr + 0x28), task.event_handler),
            check(f"{task.name}: vtable +0x80 modal entry", view.u32(task.vptr + 0x80), MODAL_ENTRY),
            check(f"{task.name}: vtable +0x84 common accept", view.u32(task.vptr + 0x84), TASK_ACCEPT_COPY),
            check(f"{task.name}: event handler recognizes 0x3E8", view.u32(task.event_handler + 0x13), 0x000003E8),
            call_check(view, f"{task.name}: opens type-A native selector", task.selector_call, SELECTOR_WRAPPER),
            call_check(view, f"{task.name}: other events delegate to common task event", task.base_event_call, TASK_ACCEPT_BASE_EVENT),
        ]
        checks.extend(row_checks)
        task_rows.append(
            {
                "command_id": task.command_id,
                "name": task.name,
                "task_vptr": task.vptr,
                "event_handler": task.event_handler,
                "validator_callback": task.validator_callback,
                "checks_ok": all(item["ok"] for item in row_checks),
            }
        )

    all_ok = all(item["ok"] for item in checks)
    return {
        "scope": "exact-version on-disk static native-UI evidence only",
        "target": {
            "path": str(view.path),
            "sha256": digest,
            "image_base": view.image_base,
            "size_of_image": int(view.pe.OPTIONAL_HEADER.SizeOfImage),
        },
        "checks": checks,
        "selection_tasks": task_rows,
        "all_static_anchors_ok": all_ok,
        "native_target_binding_route_confirmed": all_ok,
        "native_target_binding_scope": "visible UI hit-coordinate to live root+0x38 only",
        "current_city_to_root_binding_entry_found": False,
        "native_city_target_setter_found": False,
        "city_by_id_and_exact_vptr_static_confirmed": all_ok,
        "transient_root_target_write_authorized": False,
        "attached_heap_root_authorized": False,
        "force_selection_manager_route_static_confirmed": all_ok,
        "force_selection_manager_payload_type": "CForceData",
        "force_selection_manager_is_city_target_route": False,
        "can_execute_and_dispatch_atomic_entry_found": False,
        "selector_upper_events_confirmed": all_ok,
        "combined_clear_select_accept_event_found": False,
        "active_modal_selector_accessor_static_confirmed": all_ok,
        "postmessage_staging_candidate_static_confirmed": all_ok,
        "persistent_selector_object_accessor_found": False,
        "stable_selector_object_accessor_found": False,
        "business_bridge_go": False,
        "business_execution_authorized": False,
        "process_accessed": False,
        "live_mode_present": False,
        "unknowns": [
            "stable live root/controller getter with generation and scene token",
            "city-id to valid native hit-coordinate for every controlled city",
            "atomic CanExecute plus dispatch on the same handler/context snapshot",
            "runtime modal-generation/lifetime token for the active selector accessor",
            "safe staged message or hook scheduling in the nested selector pump without stale delivery/reentrancy",
        ],
    }


def format_value(value: Any) -> str:
    if isinstance(value, bytes):
        return value.hex(" ")
    if isinstance(value, int) and value >= 0x1000:
        return f"0x{value:08X}"
    return repr(value)


def print_text(report: dict[str, Any]) -> None:
    print("San9PK V7 native-UI static audit")
    print(f"file: {report['target']['path']}")
    print(f"sha256: {report['target'].get('sha256')}")
    print()
    for item in report["checks"]:
        marker = "PASS" if item["ok"] else "FAIL"
        print(f"[{marker}] {item['name']}")
        if not item["ok"]:
            print(f"       actual={format_value(item['actual'])}")
            print(f"       expected={format_value(item['expected'])}")
    print()
    print(f"all_static_anchors_ok={str(report['all_static_anchors_ok']).lower()}")
    print("business_bridge_go=false business_execution_authorized=false process_accessed=false")


DISASSEMBLY_WINDOWS: tuple[tuple[str, int, int], ...] = (
    ("root input wrapper", ROOT_INPUT_EVENT, 0x2A),
    ("target binding and menu", ROOT_TARGET_MENU, 0x27B),
    ("CCityData by city id", CITY_BY_ID, 0x30),
    ("force-selection CForceData peek", FORCE_SELECTION_PEEK, 0x40),
    ("force-selection replace/advance", FORCE_SELECTION_REPLACE, 0x70),
    ("force-selection controller tick", FORCE_SELECTION_TICK, 0x5A0),
    ("root-from-force constructor", ROOT_FROM_FORCE_CONSTRUCTOR, 0x40),
    ("heap root-with-target constructor", ROOT_FROM_TARGET_CONSTRUCTOR, 0x40),
    ("menu availability query", COMMAND_MENU_QUERY, 0x80),
    ("root query driver", ROOT_MENU_QUERY, 0x6E),
    ("real event command block", 0x00517BD9, 0x2C),
    ("commerce task selector event", 0x004E72D0, 0xDF),
    ("selector dialog event", SELECTOR_DIALOG_EVENT, 0x1D8),
    ("selector row toggle", SELECTOR_ROW_TOGGLE, 0x70),
    ("selector prefix fill", SELECTOR_PREFIX_FILL, 0x170),
    ("selector modal object creation", 0x005701C0, 0x70),
    ("active modal top-HWND accessor", FRAMEWORK_TOP_HWND, 0x20),
    ("permanent CWnd map accessor", FRAMEWORK_PERMANENT_CWND_FROM_HWND, 0x20),
    ("selector native WM_COMMAND envelope", 0x00577AE8, 0xA0),
    ("SendMessage/PostMessage wrappers", SEND_MESSAGE_WRAPPER, 0x60),
    ("outer task accept", TASK_ACCEPT_COPY, 0x2D),
)


def disassemble(view: PEView) -> None:
    try:
        import capstone
    except ImportError as error:  # pragma: no cover - optional presentation mode
        raise SystemExit("capstone is required for --disassemble") from error
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    for name, address, size in DISASSEMBLY_WINDOWS:
        print(f"\n[{name}] 0x{address:08X}..0x{address + size:08X}")
        for instruction in decoder.disasm(view.read(address, size), address):
            print(f"  {instruction.address:08X}  {instruction.mnemonic:<7} {instruction.op_str}")


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, default=TARGET_EXE, help="on-disk executable; exact SHA is mandatory")
    output = parser.add_mutually_exclusive_group()
    output.add_argument("--json", action="store_true", help="emit JSON")
    output.add_argument("--disassemble", action="store_true", help="print fixed static windows after verification")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    try:
        data = args.exe.read_bytes()
    except OSError as error:
        report = fail_report(args.exe, None, "file read", str(error), "readable exact target")
        print(json.dumps(report, ensure_ascii=False, indent=2) if args.json else str(error))
        return 2

    digest = hashlib.sha256(data).hexdigest()
    if digest.lower() != TARGET_SHA256:
        report = fail_report(args.exe, digest, "SHA-256", digest.lower(), TARGET_SHA256)
    else:
        try:
            view = PEView(args.exe, data)
            report = verify(view, digest)
        except Exception as error:
            report = fail_report(
                args.exe,
                digest,
                "static inspection",
                f"{type(error).__name__}: {error}",
                "all version-locked reads valid",
            )

    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2, default=format_value))
    else:
        print_text(report)

    if args.disassemble and report["all_static_anchors_ok"]:
        disassemble(view)
    return 0 if report["all_static_anchors_ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
