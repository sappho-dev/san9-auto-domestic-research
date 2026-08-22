#!/usr/bin/env python3
"""Offline-only San9PK 1.0.1.0 city-binding static audit.

This V9 tool verifies the exact executable anchors behind the conclusion that
native events 0x7D1/0x7D3 are coordinate gestures, while the domestic root's
0x511560 city-list path does not bind the live controller target.  Other
CCityListDlg callers are separate consumers and are not generalized into that
root-path claim.  The tool has no live mode and does not authorize a transient
write to the controller target field.

Examples:
    python tools/re/san9_v9_city_binding_static.py
    python tools/re/san9_v9_city_binding_static.py --json
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from typing import Any

import san9_v7_static as v7


EVENT_FORWARDER = 0x004345A0
GENERIC_VIEW_7D1_EMITTER = 0x00563900
MAINMAP_7D1_EMITTER = 0x005207E0
BOX_7D3_EMITTER = 0x0051D0B0
ROOT_BOX_PATH = 0x00513B10

CITY_LIST_DRIVER = 0x00511560
ROOT_CITY_LIST_ENTRY = 0x00513E20
CITY_LIST_CONSTRUCTOR = 0x005684D0
CITY_LIST_RUNTIME_CLASS = 0x0061DE50
CITY_LIST_RUNTIME_NAME = 0x0061DF58
CITY_LIST_VPTR = 0x0061DF68
CITY_MAP_HIGHLIGHT = 0x00533580
SELECTOR_SELECTED_LIST = 0x00572810
CHIiki_BUILDING_DESCRIPTOR = 0x00605268
CHIiki_BUILDING_NAME = 0x00605280

MAINMAP_DIALOG_ACCESSOR = 0x0050F430
MAINMAP_VPTR = 0x00611308
MAINMAP_DIALOG_VIRTUAL = 0x00521F10


def check(name: str, actual: Any, expected: Any) -> dict[str, Any]:
    return {"name": name, "ok": actual == expected, "actual": actual, "expected": expected}


def call_check(view: v7.PEView, name: str, site: int, target: int) -> dict[str, Any]:
    return check(name, view.rel32_target(site), target)


def verify(view: v7.PEView, digest: str) -> dict[str, Any]:
    v7_report = v7.verify(view, digest)
    checks: list[dict[str, Any]] = [
        check("V7 exact-version prerequisite", v7_report["all_static_anchors_ok"], True),
        check(
            "scene event forwarder dereferences scene+0x8C and tail-calls task delivery",
            view.read(EVENT_FORWARDER, 0x0B),
            bytes.fromhex("8B 89 8C 00 00 00 E9 35 9F 04 00"),
        ),
        check(
            "all direct calls to the scene event forwarder remain reviewed",
            v7.rel32_calls(view, 0x00401000, 0x005FEE00, EVENT_FORWARDER),
            [0x0051D0CD, 0x005208AF, 0x0056391D],
        ),
        check(
            "CMainmapView 0x7D1 emitter packs transformed X/Y and one scalar argument",
            view.read(0x0052089B, 0x19),
            bytes.fromhex(
                "0F B7 44 24 28 8B 4E 18 C1 E2 10 53 0B D0 52 68 D1 07 00 00 "
                "E8 EC 3C F1 FF"
            ),
        ),
        check(
            "0x7D3 emitter packs two WORD coordinates and one scalar argument",
            view.read(BOX_7D3_EMITTER, 0x22),
            bytes.fromhex(
                "8B 44 24 04 0F B7 54 24 0C 8B 49 18 50 0F B7 44 24 0C C1 E2 10 "
                "0B D0 52 68 D3 07 00 00 E8 CE 74 F1 FF"
            ),
        ),
        check(
            "generic view 0x7D1 emitter is also a packed-coordinate forwarder",
            view.read(GENERIC_VIEW_7D1_EMITTER, 0x22),
            bytes.fromhex(
                "8B 44 24 04 0F B7 54 24 0C 8B 49 18 50 0F B7 44 24 0C C1 E2 10 "
                "0B D0 52 68 D1 07 00 00 E8 7E 0C ED FF"
            ),
        ),
        check(
            "root input method routes 0x7D1 and 0x7D3 to separate coordinate paths",
            view.read(v7.ROOT_INPUT_EVENT, 0x28),
            bytes.fromhex(
                "8B 44 24 04 2D D1 07 00 00 74 12 83 E8 02 75 17 8B 44 24 08 50 "
                "E8 86 F7 FF FF C2 0C 00 8B 54 24 08 52 E8 F9 F4 FF FF C2"
            ),
        ),
        call_check(view, "root 0x7D3 path is the coordinate-box resolver", 0x00514385, ROOT_BOX_PATH),
        check(
            "0x7D3 path selects a hit-test result into root+0x38",
            view.read(0x00513BCA, 0x15),
            bytes.fromhex("51 8D 54 24 2C 52 E8 2B 4A F0 FF 50 89 46 38 E8 62 1C EF FF 83"),
        ),
        check(
            "root city-list entry builds a filtered list then invokes the one driver",
            view.read(0x00513E38, 0x31),
            bytes.fromhex(
                "56 8B F1 8D 4C 24 04 E8 2C 99 F5 FF 6A 00 68 D0 07 51 00 8D 4C "
                "24 0C C7 44 24 34 00 00 00 00 E8 A4 B4 F5 FF 8D 44 24 04 50 8B "
                "CE E8 F8 D6 FF FF 8D"
            ),
        ),
        check(
            "city-list driver has exactly one direct caller",
            v7.rel32_calls(view, 0x00401000, 0x005FEE00, CITY_LIST_DRIVER),
            [0x00513E63],
        ),
        check("CCityListDlg runtime descriptor names itself", view.u32(CITY_LIST_RUNTIME_CLASS), CITY_LIST_RUNTIME_NAME),
        check("CCityListDlg runtime name", view.read(CITY_LIST_RUNTIME_NAME, 13), b"CCityListDlg\x00"),
        check("CCityListDlg constructor installs the exact vptr", view.u32(0x0056850A), CITY_LIST_VPTR),
        check(
            "all direct CCityListDlg constructor callers remain reviewed",
            v7.rel32_calls(view, 0x00401000, 0x005FEE00, CITY_LIST_CONSTRUCTOR),
            [0x00511654, 0x00550851, 0x005561B7, 0x0056EB1E],
        ),
        call_check(view, "0x550732 consumer reads the selected-list result", 0x00550898, SELECTOR_SELECTED_LIST),
        check("0x550732 consumer takes the selected object", view.read(0x005508AD, 3), bytes.fromhex("8B 5A 08")),
        call_check(view, "0x550732 consumer continues through its own object path", 0x005508CB, 0x00435840),
        call_check(view, "0x556120 consumer reads the selected-list result", 0x005561F6, SELECTOR_SELECTED_LIST),
        check("0x556120 consumer takes the selected object", view.read(0x00556207, 3), bytes.fromhex("8B 72 08")),
        check(
            "0x556120 consumer stores into its explicit result array",
            view.read(0x00556221, 7),
            bytes.fromhex("89 34 C5 C4 E6 A5 01"),
        ),
        call_check(view, "0x556120 consumer continues through 0x555C70", 0x00556228, 0x00555C70),
        call_check(view, "0x56EA20 factory case constructs and returns CCityListDlg", 0x0056EB1E, CITY_LIST_CONSTRUCTOR),
        check("CCityListDlg vtable event slot is generic selector event", view.u32(CITY_LIST_VPTR + 0x28), v7.SELECTOR_DIALOG_EVENT),
        check("CCityListDlg vtable modal slot is framework modal", view.u32(CITY_LIST_VPTR + 0x80), v7.MODAL_ENTRY),
        check("CCityListDlg vtable accept slot is generic selector accept", view.u32(CITY_LIST_VPTR + 0x84), v7.SELECTOR_DIALOG_ACCEPT),
        call_check(view, "city-list default branch opens information dialog", 0x00511732, 0x0050FA10),
        call_check(view, "city-list selected branch highlights each city", 0x005117D6, CITY_MAP_HIGHLIGHT),
        call_check(view, "city-list all-items branch highlights each city", 0x0051185B, CITY_MAP_HIGHLIGHT),
        check(
            "map highlight path operates on map visual/highlight state",
            view.read(CITY_MAP_HIGHLIGHT, 0x25),
            bytes.fromhex(
                "53 55 56 57 8B 7C 24 14 8B 07 8B D9 8B CF FF 50 0C 50 B9 E0 5E "
                "54 01 E8 C4 BD FF FF 8B CB 8B F0 E8 2B 51 08 00"
            ),
        ),
        check(
            "main-map helper runtime-casts then invokes vtable+0xA8",
            view.read(MAINMAP_DIALOG_ACCESSOR, 0x22),
            bytes.fromhex(
                "E8 DB 8B FF FF 50 68 C8 11 61 00 E8 30 F9 0C 00 83 C4 08 85 C0 "
                "75 01 C3 8B 10 8B C8 FF A2 A8 00 00 00"
            ),
        ),
        check("CMainmapView vtable+0xA8 target", view.u32(MAINMAP_VPTR + 0xA8), MAINMAP_DIALOG_VIRTUAL),
        check(
            "CMainmapView vtable+0xA8 returns mainmap+0x298",
            view.read(MAINMAP_DIALOG_VIRTUAL, 7),
            bytes.fromhex("8B 81 98 02 00 00 C3"),
        ),
        check(
            "current-operation building observation has only the reviewed direct references",
            view.raw_dword_refs(v7.CURRENT_CITY_GLOBAL),
            [0x0043FF27, 0x00451646, 0x00460936, 0x00513970],
        ),
        check(
            "current-operation observation uses CChiikiBuildingData runtime type",
            view.read(0x0051395C, 12),
            bytes.fromhex("68 68 52 60 00 8B CB E8 F8 B3 0C 00"),
        ),
        check(
            "CChiikiBuildingData descriptor names itself",
            view.u32(CHIiki_BUILDING_DESCRIPTOR),
            CHIiki_BUILDING_NAME,
        ),
        check(
            "CChiikiBuildingData runtime name",
            view.read(CHIiki_BUILDING_NAME, 20),
            b"CChiikiBuildingData\x00",
        ),
        check(
            "root vptr immediate references remain constructors/destructor only",
            view.raw_dword_refs(v7.ROOT_VPTR),
            [0x0050F39A, 0x0050F3DA, 0x0050F402],
        ),
        check(
            "normal root constructor leaves target null",
            view.read(0x0050F39E, 0x0E),
            bytes.fromhex("C7 46 34 E8 03 00 00 C7 46 38 00 00 00 00"),
        ),
        check(
            "target-bound temporary constructor writes only its supplied target",
            view.read(0x0050F3D4, 0x1B),
            bytes.fromhex(
                "8B 54 24 10 C7 06 C8 0B 61 00 C7 46 34 E8 03 00 00 89 56 38 "
                "C7 46 3C 00 00 00 00"
            ),
        ),
    ]

    all_ok = all(item["ok"] for item in checks)
    return {
        "scope": "exact-version on-disk city-binding evidence only",
        "target": {"path": str(view.path), "sha256": digest},
        "checks": checks,
        "all_static_anchors_ok": all_ok,
        "native_0x7d1_payload": "packed hit coordinate plus scalar argument",
        "native_0x7d3_payload": "packed hit coordinate plus scalar argument",
        "native_event_accepts_city_id_or_object": False,
        "domestic_root_city_list_binds_controller_target": False,
        "authoritative_multi_city_ui_accessor_found": False,
        "current_operation_building_global_is_observation_only": True,
        "native_city_target_setter_found": False,
        "transient_root_target_fallback_static_candidate": all_ok,
        "transient_root_target_fallback_authorized": False,
        "business_bridge_go": False,
        "business_execution_authorized": False,
        "process_accessed": False,
        "live_mode_present": False,
    }


def fail_report(path: Path, digest: str | None, error: str) -> dict[str, Any]:
    return {
        "scope": "exact-version on-disk city-binding evidence only",
        "target": {"path": str(path), "sha256": digest},
        "checks": [check("static inspection", error, "all version-locked reads valid")],
        "all_static_anchors_ok": False,
        "native_city_target_setter_found": False,
        "transient_root_target_fallback_authorized": False,
        "business_bridge_go": False,
        "business_execution_authorized": False,
        "process_accessed": False,
        "live_mode_present": False,
    }


def format_value(value: Any) -> str:
    if isinstance(value, bytes):
        return value.hex(" ")
    if isinstance(value, int) and value >= 0x1000:
        return f"0x{value:08X}"
    return repr(value)


def print_text(report: dict[str, Any]) -> None:
    print("San9PK V9 city-binding static audit")
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
    print("native_city_target_setter_found=false")
    print("transient_root_target_fallback_authorized=false")
    print("business_bridge_go=false process_accessed=false live_mode_present=false")


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, default=v7.TARGET_EXE, help="on-disk executable; exact SHA is mandatory")
    parser.add_argument("--json", action="store_true", help="emit JSON")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    try:
        data = args.exe.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        if digest.lower() != v7.TARGET_SHA256:
            report = fail_report(args.exe, digest, "SHA-256 mismatch")
        else:
            report = verify(v7.PEView(args.exe, data), digest)
    except Exception as error:
        report = fail_report(args.exe, None, f"{type(error).__name__}: {error}")

    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2, default=format_value))
    else:
        print_text(report)
    return 0 if report["all_static_anchors_ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
