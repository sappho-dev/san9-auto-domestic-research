#!/usr/bin/env python3
"""Version-locked, on-disk-only audit of San9PK's possible main-thread bridge.

This utility reads one immutable copy of the exact executable, verifies its
SHA-256 before inspecting any virtual address, and checks only static anchors.
It never opens a process, installs a hook, loads a DLL, writes memory, calls a
game function, or authorizes domestic-command execution.

Examples:
    python san9_v5_static.py
    python san9_v5_static.py --json
    python san9_v5_static.py --disassemble
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path
from typing import Any

try:
    import pefile
except ImportError as error:  # pragma: no cover - dependency check
    raise SystemExit("pefile is required: python -m pip install pefile") from error


TARGET_EXE = Path(r"D:\三国志9\10101749\San9PK.exe")
TARGET_SHA256 = "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028"
IMAGE_BASE = 0x00400000
MACHINE_I386 = 0x014C

APP_OBJECT = 0x01228340
APP_VTABLE = 0x00604DD0
APP_MAIN_LOOP = 0x005C5CE0
APP_IDLE_SLOT = APP_VTABLE + 0x24
APP_IDLE = 0x00434100
MESSAGE_PUMP = 0x005CADA0
PEEK_MESSAGE_IAT = 0x005FF3F4

SCENE_TICK_THUNK = 0x004345C0
TASK_TICK = 0x0047E840
SCENE_SCHEDULER_OFFSET = 0x8C
SCHEDULER_CONSTRUCTOR = 0x0047EAF0
SCHEDULER_VTABLE = 0x00607560

DOMESTIC_CONTROLLER_CONSTRUCTOR = 0x0050F380
DOMESTIC_CONTROLLER_VTABLE = 0x00610BC8

SET_WINDOWS_HOOK_IAT = 0x005FF450
UNHOOK_WINDOWS_HOOK_IAT = 0x005FF444
CALL_NEXT_HOOK_IAT = 0x005FF448
GAME_GETMESSAGE_HOOK = 0x005C9560
FRAMEWORK_WNDPROC = 0x005CC6F0


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
            raise ValueError(f"VA 0x{va:08X}+0x{size:X} is outside the captured file")
        return self.data[offset:end]

    def u32(self, va: int) -> int:
        return struct.unpack("<I", self.read(va, 4))[0]

    def rel32_target(self, va: int, opcode: int = 0xE8) -> int | None:
        instruction = self.read(va, 5)
        if instruction[0] != opcode:
            return None
        relative = struct.unpack("<i", instruction[1:])[0]
        return va + 5 + relative

    def import_at(self, iat_va: int) -> tuple[str, str] | None:
        for descriptor in self.pe.DIRECTORY_ENTRY_IMPORT:
            dll = descriptor.dll.decode("ascii", errors="replace").lower()
            for imported in descriptor.imports:
                if int(imported.address) != iat_va:
                    continue
                if imported.name is None:
                    name = f"#{imported.ordinal}"
                else:
                    name = imported.name.decode("ascii", errors="replace")
                return dll, name
        return None

    def section_at(self, va: int) -> dict[str, Any] | None:
        rva = va - self.image_base
        for section in self.pe.sections:
            start = int(section.VirtualAddress)
            size = max(int(section.Misc_VirtualSize), int(section.SizeOfRawData))
            if start <= rva < start + size:
                return {
                    "name": section.Name.rstrip(b"\0").decode("ascii", errors="replace"),
                    "characteristics": int(section.Characteristics),
                }
        return None


def make_check(name: str, actual: Any, expected: Any) -> dict[str, Any]:
    return {"name": name, "ok": actual == expected, "actual": actual, "expected": expected}


def call_check(view: PEView, name: str, site: int, target: int) -> dict[str, Any]:
    return make_check(name, view.rel32_target(site), target)


def fail_closed_report(
    path: Path,
    digest: str | None,
    name: str,
    actual: Any,
    expected: Any,
) -> dict[str, Any]:
    return {
        "target": {
            "path": str(path),
            "sha256": digest,
            "expected_sha256": TARGET_SHA256,
            "image_base": None,
        },
        "checks": [make_check(name, actual, expected)],
        "all_ok": False,
        "all_ok_scope": "version_locked_on_disk_bridge_anchor_match_only",
        "main_thread_bridge_authorized": False,
        "domestic_execution_authorized": False,
        "process_accessed": False,
        "note": "Fail-closed before any version-specific address inspection.",
    }


def verify(view: PEView, digest: str) -> dict[str, Any]:
    idle_slot_section = view.section_at(APP_IDLE_SLOT)
    checks: list[dict[str, Any]] = [
        make_check("SHA-256", digest.lower(), TARGET_SHA256),
        make_check("PE machine is x86", int(view.pe.FILE_HEADER.Machine), MACHINE_I386),
        make_check("PE ImageBase", view.image_base, IMAGE_BASE),
        make_check(
            "PE has no DYNAMIC_BASE flag",
            int(view.pe.OPTIONAL_HEADER.DllCharacteristics) & 0x40,
            0,
        ),
        make_check("app vtable +0x18 main loop", view.u32(APP_VTABLE + 0x18), APP_MAIN_LOOP),
        make_check("app vtable +0x24 idle", view.u32(APP_IDLE_SLOT), APP_IDLE),
        make_check("app vtable +0x2C shutdown", view.u32(APP_VTABLE + 0x2C), 0x005C5A30),
        make_check(
            "app constructor installs vptr",
            view.read(0x00432233, 6),
            bytes.fromhex("C7 06 D0 4D 60 00"),
        ),
        make_check(
            "fixed app object thunk uses 0x01228340",
            view.read(0x00432950, 5),
            bytes.fromhex("B9 40 83 22 01"),
        ),
        call_check(view, "main loop calls message pump", 0x005C5CF1, MESSAGE_PUMP),
        make_check(
            "main loop calls app vtable +0x24 after pump cleanup",
            view.read(0x005C5D05, 14),
            bytes.fromhex("85 FF 8B CE 75 08 8B 06 57 FF 50 24 EB DD"),
        ),
        make_check(
            "PeekMessageA IAT binding",
            view.import_at(PEEK_MESSAGE_IAT),
            ("user32.dll", "PeekMessageA"),
        ),
        make_check(
            "message pump caches PeekMessageA IAT in ESI",
            view.read(0x005CADA6, 6),
            bytes.fromhex("8B 35 F4 F3 5F 00"),
        ),
        make_check(
            "message pump first PeekMessage call through ESI",
            view.read(0x005CADB2, 16),
            bytes.fromhex("57 6A 00 6A 00 6A 00 8D 44 24 20 50 FF D6 85 C0"),
        ),
        make_check(
            "message pump drain-loop PeekMessage call through ESI",
            view.read(0x005CAE38, 17),
            bytes.fromhex("6A 01 6A 00 6A 00 6A 00 8D 54 24 20 52 FF D6 85 C0"),
        ),
        call_check(view, "app idle calls scene tick thunk", 0x00434142, SCENE_TICK_THUNK),
        make_check(
            "scene tick thunk loads scene+0x8C then calls task tick",
            view.read(SCENE_TICK_THUNK, 14),
            bytes.fromhex("8B 89 8C 00 00 00 E8 75 A2 04 00 C2 04 00"),
        ),
        make_check(
            "scene allocates exactly 0x30-byte scheduler",
            view.read(0x004344BD, 2),
            bytes.fromhex("6A 30"),
        ),
        call_check(view, "scene scheduler allocation uses game allocator", 0x004344C4, 0x005DEC20),
        call_check(view, "scene constructs scheduler", 0x004344DC, SCHEDULER_CONSTRUCTOR),
        make_check(
            "scene stores scheduler at +0x8C",
            view.read(0x004344E9, 6),
            bytes.fromhex("89 86 8C 00 00 00"),
        ),
        call_check(view, "scheduler derives from generic state task", 0x0047EAF8, 0x0047E330),
        make_check(
            "scheduler constructor installs 0x607560",
            view.read(0x0047EAFD, 6),
            bytes.fromhex("C7 06 60 75 60 00"),
        ),
        make_check(
            "scheduler destructor restores 0x607560",
            view.read(0x0047EB10, 6),
            bytes.fromhex("C7 01 60 75 60 00"),
        ),
        make_check("scheduler vtable +0x00 destructor", view.u32(SCHEDULER_VTABLE), 0x0047ECD0),
        make_check("scheduler vtable +0x08 task gate", view.u32(SCHEDULER_VTABLE + 0x08), 0x0047E4C0),
        make_check("scheduler vtable +0x0C state tick", view.u32(SCHEDULER_VTABLE + 0x0C), 0x0047E360),
        make_check("scheduler vtable +0x28 factory", view.u32(SCHEDULER_VTABLE + 0x28), 0x0047EB20),
        make_check(
            "generic task initializes child/pending fields",
            view.read(0x0047E660, 22),
            bytes.fromhex(
                "89 7E 0C 89 7E 1C 89 7E 14 89 7E 28 5F 89 5E 08 "
                "89 76 10 89 46 18"
            ),
        ),
        make_check(
            "task attach swaps parent+0x10 pending slot",
            view.read(0x0047E71C, 6),
            bytes.fromhex("8B 47 10 89 77 10"),
        ),
        call_check(view, "task tick finds active deepest child", 0x0047E86B, 0x0047E3D0),
        make_check(
            "task tick dispatches deepest leaf vtable +0x0C",
            view.read(0x0047E8E8, 14),
            bytes.fromhex("8B CD E8 E1 FA FF FF 8B 10 8B C8 FF 52 0C"),
        ),
        make_check(
            "controller dynamic path allocates 0x40 bytes",
            view.read(0x0050F191, 2),
            bytes.fromhex("6A 40"),
        ),
        call_check(view, "controller allocation uses game allocator", 0x0050F193, 0x005DEC20),
        make_check(
            "controller constructor installs 0x610BC8",
            view.read(0x0050F398, 6),
            bytes.fromhex("C7 06 C8 0B 61 00"),
        ),
        make_check(
            "controller initializes +0x34 state and +0x38 target",
            view.read(0x0050F39E, 14),
            bytes.fromhex("C7 46 34 E8 03 00 00 C7 46 38 00 00 00 00"),
        ),
        make_check(
            "scheduler and domestic controller vptrs differ",
            SCHEDULER_VTABLE == DOMESTIC_CONTROLLER_VTABLE,
            False,
        ),
        make_check("controller vtable +0x0C tick", view.u32(DOMESTIC_CONTROLLER_VTABLE + 0x0C), 0x00516220),
        make_check("controller vtable +0x18 event", view.u32(DOMESTIC_CONTROLLER_VTABLE + 0x18), 0x005179B0),
        make_check("controller vtable +0x1C input", view.u32(DOMESTIC_CONTROLLER_VTABLE + 0x1C), 0x00514370),
        make_check("controller vtable +0x28 factory", view.u32(DOMESTIC_CONTROLLER_VTABLE + 0x28), 0x005125A0),
        make_check(
            "SetWindowsHookExA IAT binding",
            view.import_at(SET_WINDOWS_HOOK_IAT),
            ("user32.dll", "SetWindowsHookExA"),
        ),
        make_check(
            "UnhookWindowsHookEx IAT binding",
            view.import_at(UNHOOK_WINDOWS_HOOK_IAT),
            ("user32.dll", "UnhookWindowsHookEx"),
        ),
        make_check(
            "CallNextHookEx IAT binding",
            view.import_at(CALL_NEXT_HOOK_IAT),
            ("user32.dll", "CallNextHookEx"),
        ),
        make_check(
            "game installs its own WH_GETMESSAGE hook",
            view.read(0x005CA0C0, 13),
            bytes.fromhex("68 60 95 5C 00 6A 03 FF 15 50 F4 5F 00"),
        ),
        make_check(
            "game hook monitors mouse message range",
            view.read(0x005C957C, 16),
            bytes.fromhex("81 FF 00 02 00 00 72 53 81 FF 04 02 00 00 77 4B"),
        ),
        make_check(
            "game WH_GETMESSAGE hook chains with CallNextHookEx",
            view.read(0x005C95D9, 20),
            bytes.fromhex("8B 44 24 18 8B 0D 54 07 B4 01 56 50 55 51 FF 15 48 F4 5F 00"),
        ),
        make_check(
            "game WH_GETMESSAGE callback is stdcall with three arguments",
            view.read(0x005C95ED, 8),
            bytes.fromhex("5E 5D 83 C4 08 C2 0C 00"),
        ),
        make_check(
            "framework WndProc has stdcall four-argument return",
            view.read(0x005CC80C, 10),
            bytes.fromhex("5F 5E 5D 5B 83 C4 38 C2 10 00"),
        ),
        make_check(
            "framework registers 0x5CC6F0 as WndProc",
            view.read(0x005CC83D, 8),
            bytes.fromhex("C7 44 24 0C F0 C6 5C 00"),
        ),
        make_check("idle vtable slot is four-byte aligned", APP_IDLE_SLOT & 3, 0),
        make_check(
            "idle vtable slot resides in writable .rdata",
            (
                None
                if idle_slot_section is None
                else (
                    idle_slot_section["name"],
                    bool(int(idle_slot_section["characteristics"]) & 0x80000000),
                )
            ),
            (".rdata", True),
        ),
    ]

    return {
        "target": {
            "path": str(view.path),
            "sha256": digest,
            "expected_sha256": TARGET_SHA256,
            "machine": int(view.pe.FILE_HEADER.Machine),
            "image_base": view.image_base,
            "dynamic_base": bool(int(view.pe.OPTIONAL_HEADER.DllCharacteristics) & 0x40),
        },
        "main_loop": {
            "app_object": APP_OBJECT,
            "app_vtable": APP_VTABLE,
            "loop": APP_MAIN_LOOP,
            "message_pump": MESSAGE_PUMP,
            "idle_slot": APP_IDLE_SLOT,
            "idle_target": APP_IDLE,
            "idle_call_site": 0x005C5D0E,
            "idle_call_return": 0x005C5D11,
            "scene_tick_thunk": SCENE_TICK_THUNK,
            "scene_task_tick_call": 0x004345C6,
            "task_tick": TASK_TICK,
        },
        "task_hierarchy": {
            "scene_scheduler_offset": SCENE_SCHEDULER_OFFSET,
            "scheduler_allocation_size": 0x30,
            "scheduler_constructor": SCHEDULER_CONSTRUCTOR,
            "scheduler_vtable": SCHEDULER_VTABLE,
            "domestic_controller_allocation_size": 0x40,
            "domestic_controller_constructor": DOMESTIC_CONTROLLER_CONSTRUCTOR,
            "domestic_controller_vtable": DOMESTIC_CONTROLLER_VTABLE,
            "controller_only_fields": {"corps": 0x30, "state": 0x34, "target": 0x38},
            "note": (
                "scene+0x8C is the 0x30-byte generic scheduler object. Offsets +0x30/+0x34/+0x38 "
                "are outside that allocation and may be read only on an active-chain node whose "
                "vptr is exactly 0x610BC8. Controller absence is a valid state; ambiguity must fail closed."
            ),
        },
        "candidate_summary": [
            {
                "candidate": "atomic app-vtable +0x24 pointer hook",
                "address": APP_IDLE_SLOT,
                "original": APP_IDLE,
                "static_disposition": "preferred for a non-executing dynamic heartbeat harness only",
            },
            {
                "candidate": "PeekMessageA IAT hook",
                "address": PEEK_MESSAGE_IAT,
                "original_import": "user32!PeekMessageA",
                "static_disposition": "fallback; broader scope and cached-ESI unload hazard",
            },
            {
                "candidate": "scene task-tick call redirection",
                "address": 0x004345C6,
                "original": TASK_TICK,
                "static_disposition": "not preferred; text patch is non-atomic and needs quiescence",
            },
            {
                "candidate": "WH_GETMESSAGE",
                "hook_id": 3,
                "static_disposition": "bootstrap only; never a proven game-call safe point",
            },
            {
                "candidate": "WH_FOREGROUNDIDLE",
                "hook_id": 11,
                "static_disposition": "ping-only comparator; foreground starvation must be measured",
            },
            {
                "candidate": "WH_CALLWNDPROC or WndProc subclass",
                "static_disposition": "no-go as executor due dispatch reentrancy and lifecycle risk",
            },
        ],
        "checks": checks,
        "all_ok": all(item["ok"] for item in checks),
        "all_ok_scope": "version_locked_on_disk_bridge_anchor_match_only",
        "main_thread_bridge_authorized": False,
        "domestic_execution_authorized": False,
        "process_accessed": False,
        "note": (
            "Matching anchors establish only that this exact disk image contains the described "
            "control-flow and object-layout candidates. Runtime thread identity, reentrancy, hook "
            "coexistence, unload safety, task ownership, and game-command safety remain unverified."
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
    print("San9PK V5 static main-thread bridge audit")
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
    print("scope: static disk evidence only; every execution authorization remains false.")


DISASSEMBLY_WINDOWS: tuple[tuple[str, int, int], ...] = (
    ("main loop", 0x005C5CE0, 0x3E),
    ("message pump", 0x005CADA0, 0xD0),
    ("app idle", 0x00434100, 0x74),
    ("scene scheduler construction", 0x004344BD, 0x4C),
    ("scene tick thunk", SCENE_TICK_THUNK, 0x0E),
    ("task tick", TASK_TICK, 0xC0),
    ("scheduler constructor", SCHEDULER_CONSTRUCTOR, 0x20),
    ("scheduler factory", 0x0047EB20, 0x18C),
    ("controller constructor", DOMESTIC_CONTROLLER_CONSTRUCTOR, 0x34),
    ("existing WH_GETMESSAGE hook", GAME_GETMESSAGE_HOOK, 0x93),
)


def disassemble_windows(view: PEView) -> None:
    try:
        import capstone
    except ImportError as error:  # pragma: no cover - optional mode
        raise SystemExit("capstone is required for --disassemble") from error
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    for name, address, size in DISASSEMBLY_WINDOWS:
        print(f"\n[{name}] 0x{address:08X}..0x{address + size:08X}")
        for instruction in decoder.disasm(view.read(address, size), address):
            print(f"  {instruction.address:08X}  {instruction.mnemonic:<7} {instruction.op_str}")


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, default=TARGET_EXE, help="on-disk San9PK.exe path")
    output = parser.add_mutually_exclusive_group()
    output.add_argument("--json", action="store_true", help="emit JSON")
    output.add_argument("--disassemble", action="store_true", help="show fixed static windows")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    try:
        data = args.exe.read_bytes()
    except OSError as error:
        report = fail_closed_report(args.exe, None, "file read", str(error), "readable file")
        print(json.dumps(report, ensure_ascii=False, indent=2) if args.json else str(error))
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
    except Exception as error:
        report = fail_closed_report(
            args.exe, digest, "PE parse", type(error).__name__, "valid PE32"
        )
        if args.json:
            print(json.dumps(report, ensure_ascii=False, indent=2, default=format_value))
        else:
            print_text(report)
        return 1

    if view.image_base != IMAGE_BASE:
        report = fail_closed_report(
            args.exe, digest, "PE ImageBase", view.image_base, IMAGE_BASE
        )
    else:
        try:
            report = verify(view, digest)
        except Exception as error:
            report = fail_closed_report(
                args.exe,
                digest,
                "static anchor inspection",
                f"{type(error).__name__}: {error}",
                "all reads within the locked image",
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
