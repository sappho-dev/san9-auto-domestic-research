#!/usr/bin/env python3
"""On-disk PE and x86 wrapper audit for San9Bridge V5.2 artifacts.

The script reads ordinary artifact files only.  It never loads a DLL, starts
an executable, enumerates a process, installs a hook, or accesses the game.
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
    import capstone
    import pefile
except ImportError as error:  # pragma: no cover
    raise SystemExit(
        "pefile 2024.8.26 and capstone 5.0.7 are required: "
        "python -m pip install pefile==2024.8.26 capstone==5.0.7"
    ) from error


MINIMUM_PYTHON = (3, 11, 0)
REQUIRED_PEFILE_VERSION = "2024.8.26"
REQUIRED_CAPSTONE_VERSION = "5.0.7"
MACHINE_I386 = 0x014C
PE32_MAGIC = 0x010B
IMAGE_FILE_DLL = 0x2000

EXPECTED_EXPORTS = {
    "San9BridgeV52_Bootstrap",
    "San9BridgeV52_ForegroundIdleProc",
    "San9BridgeV52_GetContract",
    "San9BridgeV52_GetMsgProc",
    "San9BridgeV52_IdleBridge",
    "San9BridgeV52_OfflineSelfTest",
}

NETWORK_OR_DEBUG_MODULES = {
    "dbghelp.dll",
    "iphlpapi.dll",
    "urlmon.dll",
    "winhttp.dll",
    "wininet.dll",
    "ws2_32.dll",
}

ALWAYS_FORBIDDEN_IMPORTS = {
    "createremotethread",
    "createremotethreadex",
    "debugactiveprocess",
    "freelibrary",
    "freelibraryandexitthread",
    "keybd_event",
    "mouse_event",
    "ntcreatethreadex",
    "openthread",
    "queueuserapc",
    "readprocessmemory",
    "sendinput",
    "sendmessagea",
    "sendmessagew",
    "setcursorpos",
    "setwindowlonga",
    "setwindowlongw",
    "setwindowlongptra",
    "setwindowlongptrw",
    "virtualallocex",
    "virtualfreeex",
    "virtualprotectex",
    "writeprocessmemory",
}

OFFLINE_FORBIDDEN_IMPORTS = {
    "createfilemappingw",
    "createtoolhelp32snapshot",
    "enumwindows",
    "getclassnamew",
    "getwindowthreadprocessid",
    "globaladdatomw",
    "globaldeleteatom",
    "globalgetatomnamew",
    "module32firstw",
    "module32nextw",
    "openfilemappingw",
    "openprocess",
    "postthreadmessagew",
    "process32firstw",
    "process32nextw",
    "queryfullprocessimagenamew",
    "registerwindowmessagew",
    "setwindowshookexw",
    "unhookwindowshookex",
}

BUSINESS_VAS = {
    0x004AAF00,
    0x004AB7D0,
    0x004ABD10,
    0x004AE200,
    0x004B3540,
    0x00473280,
    0x01258EE0,
}

EXACT_TARGET_PATH = r"D:\三国志9\10101749\San9PK.exe"
FROZEN_CONFLICT_NAMES = (
    "San9PKEasy.exe",
    "San9PKHard.exe",
    "SanIXPKCheat.exe",
    "Easy.dll",
    "SanIXSpy.dll",
    "San9Common.dll",
    "version.dll",
    "dinput.dll",
    "dinput8.dll",
    "winmm.dll",
    "dsound.dll",
)


def version_gate(
    python_version: tuple[int, int, int],
    pefile_version: str,
    capstone_version: str,
) -> bool:
    return (
        python_version >= MINIMUM_PYTHON
        and pefile_version == REQUIRED_PEFILE_VERSION
        and capstone_version == REQUIRED_CAPSTONE_VERSION
    )


def version_gate_report() -> dict[str, Any]:
    actual_python = tuple(sys.version_info[:3])
    actual_pefile = str(getattr(pefile, "__version__", ""))
    actual_capstone = str(getattr(capstone, "__version__", ""))
    checks = {
        "accepts_locked_dependencies": version_gate(
            MINIMUM_PYTHON,
            REQUIRED_PEFILE_VERSION,
            REQUIRED_CAPSTONE_VERSION,
        ),
        "rejects_old_python": not version_gate(
            (3, 10, 99), REQUIRED_PEFILE_VERSION, REQUIRED_CAPSTONE_VERSION
        ),
        "rejects_wrong_pefile": not version_gate(
            MINIMUM_PYTHON, "2024.8.25", REQUIRED_CAPSTONE_VERSION
        ),
        "rejects_pefile_suffix": not version_gate(
            MINIMUM_PYTHON,
            REQUIRED_PEFILE_VERSION + "+local",
            REQUIRED_CAPSTONE_VERSION,
        ),
        "rejects_wrong_capstone": not version_gate(
            MINIMUM_PYTHON, REQUIRED_PEFILE_VERSION, "5.0.6"
        ),
        "rejects_capstone_suffix": not version_gate(
            MINIMUM_PYTHON,
            REQUIRED_PEFILE_VERSION,
            REQUIRED_CAPSTONE_VERSION + "+local",
        ),
        "actual_toolchain_allowed": version_gate(
            actual_python, actual_pefile, actual_capstone
        ),
    }
    return {
        "python_executable": str(Path(sys.executable).resolve()),
        "python_actual": ".".join(str(part) for part in actual_python),
        "python_minimum": ".".join(str(part) for part in MINIMUM_PYTHON),
        "pefile_actual": actual_pefile,
        "pefile_required_exact": REQUIRED_PEFILE_VERSION,
        "capstone_actual": actual_capstone,
        "capstone_required_exact": REQUIRED_CAPSTONE_VERSION,
        "checks": checks,
        "all_ok": all(checks.values()),
    }


def imports(pe: pefile.PE) -> dict[str, list[str]]:
    result: dict[str, list[str]] = {}
    for descriptor in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []):
        module = descriptor.dll.decode("ascii", errors="replace").lower()
        names = []
        for imported in descriptor.imports:
            names.append(
                f"#{imported.ordinal}"
                if imported.name is None
                else imported.name.decode("ascii", errors="replace")
            )
        result[module] = sorted(names)
    return dict(sorted(result.items()))


def export_records(pe: pefile.PE) -> list[dict[str, Any]]:
    directory = getattr(pe, "DIRECTORY_ENTRY_EXPORT", None)
    if directory is None:
        return []
    return [
        {
            "name": None
            if symbol.name is None
            else symbol.name.decode("ascii", errors="replace"),
            "ordinal": int(symbol.ordinal),
            "rva": int(symbol.address),
            "forwarder": None
            if symbol.forwarder is None
            else symbol.forwarder.decode("ascii", errors="replace"),
        }
        for symbol in directory.symbols
    ]


def tls_callback_count(pe: pefile.PE) -> int:
    directory = getattr(pe, "DIRECTORY_ENTRY_TLS", None)
    if directory is None or directory.struct.AddressOfCallBacks == 0:
        return 0
    callback_rva = int(
        directory.struct.AddressOfCallBacks - pe.OPTIONAL_HEADER.ImageBase
    )
    count = 0
    for index in range(64):
        raw = pe.get_data(callback_rva + index * 4, 4)
        if len(raw) != 4 or struct.unpack("<I", raw)[0] == 0:
            break
        count += 1
    return count


def has_text(data: bytes, value: str) -> bool:
    candidates = [value.encode("utf-16le")]
    try:
        candidates.append(value.encode("ascii"))
    except UnicodeEncodeError:
        pass
    return any(candidate in data for candidate in candidates)


def all_text(data: bytes, values: tuple[str, ...]) -> bool:
    return all(has_text(data, value) for value in values)


def common_pe_details(path: Path, expect_dll: bool) -> dict[str, Any]:
    data = path.read_bytes()
    pe = pefile.PE(data=data, fast_load=False)
    imported = imports(pe)
    records = export_records(pe)
    names = {
        name.lower()
        for module_names in imported.values()
        for name in module_names
        if not name.startswith("#")
    }
    modules = set(imported)
    ordinal_imports = sorted(
        f"{module}!{name}"
        for module, module_names in imported.items()
        for name in module_names
        if name.startswith("#")
    )
    is_dll = bool(pe.FILE_HEADER.Characteristics & IMAGE_FILE_DLL)
    checks = {
        "machine_i386": pe.FILE_HEADER.Machine == MACHINE_I386,
        "optional_header_pe32": pe.OPTIONAL_HEADER.Magic == PE32_MAGIC,
        "dll_characteristic_exact": is_dll == expect_dll,
        "no_delay_imports": not bool(
            getattr(pe, "DIRECTORY_ENTRY_DELAY_IMPORT", [])
        ),
        "no_ordinal_imports": not ordinal_imports,
        "network_debug_modules_absent": not (
            modules & NETWORK_OR_DEBUG_MODULES
        ),
        "always_forbidden_imports_absent": not (
            names & ALWAYS_FORBIDDEN_IMPORTS
        ),
        "business_addresses_absent": not any(
            struct.pack("<I", address) in data for address in BUSINESS_VAS
        ),
    }
    return {
        "path": str(path.resolve()),
        "data": data,
        "pe": pe,
        "imports": imported,
        "import_names": names,
        "exports": records,
        "is_dll": is_dll,
        "size": len(data),
        "sha256": hashlib.sha256(data).hexdigest(),
        "machine": f"0x{pe.FILE_HEADER.Machine:04X}",
        "optional_header_magic": f"0x{pe.OPTIONAL_HEADER.Magic:04X}",
        "tls_callback_count": tls_callback_count(pe),
        "ordinal_imports": ordinal_imports,
        "checks": checks,
    }


def idle_instructions(pe: pefile.PE, records: list[dict[str, Any]]) -> list[Any]:
    record = next(
        item for item in records if item["name"] == "San9BridgeV52_IdleBridge"
    )
    code = pe.get_data(record["rva"], 192)
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    instructions = []
    for instruction in decoder.disasm(
        code, pe.OPTIONAL_HEADER.ImageBase + record["rva"]
    ):
        instructions.append(instruction)
        if instruction.mnemonic in {"ret", "retf"}:
            break
    return instructions


def audit_offline_idle(details: dict[str, Any]) -> dict[str, bool]:
    instructions = idle_instructions(details["pe"], details["exports"])
    text = [(item.mnemonic, item.op_str) for item in instructions]
    return {
        "offline_idle_is_xor_eax_ret4": len(text) == 2
        and text[0] == ("xor", "eax, eax")
        and text[1] == ("ret", "4"),
        "offline_idle_has_no_original_call_motif": bytes.fromhex(
            "b800414300ffd0"
        )
        not in details["data"],
    }


def audit_live_idle(details: dict[str, Any]) -> dict[str, bool]:
    instructions = idle_instructions(details["pe"], details["exports"])
    pairs = [(item.mnemonic, item.op_str) for item in instructions]
    original_indexes = [
        index
        for index, pair in enumerate(pairs)
        if pair == ("mov", "eax, 0x434100")
        and index + 1 < len(pairs)
        and pairs[index + 1] == ("call", "eax")
    ]
    original_index = original_indexes[0] if len(original_indexes) == 1 else -1
    before = pairs[:original_index] if original_index >= 0 else []
    after = pairs[original_index + 2 :] if original_index >= 0 else []
    saved_location = ""
    if after and after[0][0] == "mov" and after[0][1].endswith(", eax"):
        saved_location = after[0][1].split(",", 1)[0]
    return {
        "idle_disassembly_reaches_ret": bool(instructions)
        and instructions[-1].mnemonic == "ret",
        "original_call_sequence_exactly_once": len(original_indexes) == 1,
        "original_raw_motif_exactly_once": details["data"].count(
            bytes.fromhex("b800414300ffd0")
        )
        == 1,
        "no_branch_or_ret_can_skip_original": original_index >= 0
        and not any(
            mnemonic.startswith("j")
            or mnemonic.startswith("loop")
            or mnemonic in {"ret", "retf"}
            for mnemonic, _ in before
        ),
        "enter_helper_precedes_unconditional_original": original_index >= 0
        and any(mnemonic == "call" for mnemonic, _ in before),
        "original_eax_saved_and_restored": bool(saved_location)
        and ("mov", f"eax, {saved_location}") in after,
        "post_helper_follows_original": sum(
            1 for mnemonic, _ in after if mnemonic == "call"
        )
        >= 1,
        "nonvolatile_registers_preserved": all(
            ("push", register) in pairs and ("pop", register) in pairs
            for register in ("ebx", "esi", "edi", "ebp")
        ),
        "df_is_cleared_for_helpers_and_restored": ("pushfd", "") in after
        and ("std", "") in after
        and ("cld", "") in pairs,
        "single_ret4_epilogue": sum(
            1 for mnemonic, operand in pairs if mnemonic == "ret" and operand == "4"
        )
        == 1,
    }


def finalize(details: dict[str, Any]) -> dict[str, Any]:
    output = {key: value for key, value in details.items() if key not in {"data", "pe", "import_names"}}
    output["all_ok"] = all(output["checks"].values())
    return output


def audit_artifacts(
    profile: str, dll_path: Path, controller_path: Path
) -> dict[str, Any]:
    dll = common_pe_details(dll_path, expect_dll=True)
    controller = common_pe_details(controller_path, expect_dll=False)
    dll_export_names = {item["name"] for item in dll["exports"]}
    clean_exports = (
        dll_export_names == EXPECTED_EXPORTS
        and len(dll["exports"]) == len(EXPECTED_EXPORTS)
        and all(
            item["name"] is not None and item["forwarder"] is None
            for item in dll["exports"]
        )
    )
    dll["checks"].update(
        {
            "exports_exact_named_not_forwarded": clean_exports,
            "call_next_hook_chain_present": "callnexthookex"
            in dll["import_names"],
            "no_free_library_path": "freelibrary" not in dll["import_names"],
        }
    )
    controller["checks"].update(
        {
            "controller_has_no_exports": controller["exports"] == [],
            "controller_no_free_library_path": "freelibrary"
            not in controller["import_names"],
        }
    )

    if profile == "offline":
        offline_bad_dll = dll["import_names"] & OFFLINE_FORBIDDEN_IMPORTS
        offline_bad_controller = (
            controller["import_names"] & OFFLINE_FORBIDDEN_IMPORTS
        )
        dll["checks"].update(audit_offline_idle(dll))
        dll["checks"].update(
            {
                "offline_live_imports_absent": not offline_bad_dll,
                "offline_profile_marker_present": has_text(
                    dll["data"], "SAN9_V52_PROFILE=OFFLINE;LIVE_ENABLED=0"
                ),
                "offline_exact_target_token_absent": not has_text(
                    dll["data"], "San9PK.exe"
                ),
                "offline_user32_only_call_next": dll["imports"].get(
                    "user32.dll", []
                )
                == ["CallNextHookEx"],
                "offline_pin_runtime_present": "getmodulehandleexw"
                in dll["import_names"],
            }
        )
        controller["checks"].update(
            {
                "offline_live_imports_absent": not offline_bad_controller,
                "offline_target_token_absent": not has_text(
                    controller["data"], "San9PK.exe"
                ),
                "offline_controller_has_no_user32": "user32.dll"
                not in controller["imports"],
                "offline_controller_selftest_only_marker": has_text(
                    controller["data"],
                    "OFFLINE_ONLY: this artifact was compiled with SAN9_V52_LIVE_ENABLED=0",
                ),
            }
        )
    else:
        required_dll = {
            "callnexthookex",
            "createtoolhelp32snapshot",
            "enumwindows",
            "getclassnamew",
            "getmodulehandleexw",
            "getwindowthreadprocessid",
            "globalgetatomnamew",
            "mapviewoffile",
            "module32firstw",
            "module32nextw",
            "openfilemappingw",
            "openprocess",
            "process32firstw",
            "process32nextw",
            "queryfullprocessimagenamew",
        }
        forbidden_dll = {
            "createfilemappingw",
            "globaladdatomw",
            "globaldeleteatom",
            "getprocaddress",
            "loadlibraryw",
            "postthreadmessagew",
            "registerwindowmessagew",
            "setwindowshookexw",
            "unhookwindowshookex",
        }
        required_controller = {
            "bcryptgenrandom",
            "createfilemappingw",
            "createtoolhelp32snapshot",
            "enumwindows",
            "globaladdatomw",
            "globaldeleteatom",
            "loadlibraryw",
            "openprocess",
            "postthreadmessagew",
            "registerwindowmessagew",
            "setwindowshookexw",
            "unhookwindowshookex",
        }
        dll["checks"].update(audit_live_idle(dll))
        dll["checks"].update(
            {
                "live_dll_required_gates_imported": required_dll
                <= dll["import_names"],
                "live_dll_cannot_install_or_unhook": not (
                    forbidden_dll & dll["import_names"]
                ),
                "live_profile_marker_present": has_text(
                    dll["data"],
                    "SAN9_V52_PROFILE=LIVE_OPTIN;EXECUTION=PING_ONLY",
                ),
                "commit_v3_shared_lifecycle_marker_present": all_text(
                    dll["data"],
                    ("STATE_MACHINE=COMMIT_V3", "LIFECYCLE=SHARED"),
                ),
                "exact_target_path_present": has_text(
                    dll["data"], EXACT_TARGET_PATH
                ),
                "frozen_conflict_table_present": all_text(
                    dll["data"], FROZEN_CONFLICT_NAMES
                ),
            }
        )
        controller["checks"].update(
            {
                "live_controller_required_bootstrap_imports": required_controller
                <= controller["import_names"],
                "live_controller_no_hook_callback_import": "callnexthookex"
                not in controller["import_names"],
                "exact_target_path_present": has_text(
                    controller["data"], EXACT_TARGET_PATH
                ),
                "frozen_conflict_table_present": all_text(
                    controller["data"], FROZEN_CONFLICT_NAMES
                ),
                "high_friction_cli_tokens_present": all_text(
                    controller["data"],
                    (
                        "--live-bootstrap",
                        "I_ACCEPT_SETWINDOWSHOOKEX",
                        "I_ACCEPT_NO_HOT_UNLOAD",
                        "I_ACCEPT_PING_ONLY_NO_BUSINESS",
                    ),
                ),
                "indeterminate_restart_diagnostic_present": has_text(
                    controller["data"],
                    "INDETERMINATE/RESTART_REQUIRED",
                ),
                "cleanup_incomplete_diagnostic_present": all_text(
                    controller["data"],
                    ("CLEANUP_INCOMPLETE", "GlobalDeleteAtom failed twice"),
                ),
                "authenticated_expiry_diagnostic_present": has_text(
                    controller["data"],
                    "authenticated envelope expired",
                ),
                "console_not_main_ui_warning_present": all_text(
                    controller["data"],
                    ("not the main UI", "double-clicked"),
                ),
            }
        )

    dll_output = finalize(dll)
    controller_output = finalize(controller)
    return {
        "scope": "on-disk V5.2 artifacts only",
        "profile": profile,
        "artifact_executed": False,
        "dll_loaded": False,
        "process_accessed": False,
        "hook_installed": False,
        "business_execution_authorized": False,
        "live_dynamic_safety_proven": False,
        "live_loader_lifecycle_proven": False,
        "dll": dll_output,
        "controller": controller_output,
        "all_ok": dll_output["all_ok"] and controller_output["all_ok"],
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--profile", choices=("offline", "live-optin"), default="offline"
    )
    parser.add_argument("--dll", type=Path)
    parser.add_argument("--controller", type=Path)
    parser.add_argument("--json", action="store_true")
    parser.add_argument("--self-test-version-gates", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    toolchain = version_gate_report()
    if args.self_test_version_gates:
        if args.json:
            print(json.dumps(toolchain, ensure_ascii=False, indent=2))
        else:
            passed = sum(bool(value) for value in toolchain["checks"].values())
            total = len(toolchain["checks"])
            print(
                f"San9Bridge V5.2 audit toolchain gate: {passed}/{total}, "
                f"python={toolchain['python_actual']} "
                f"(minimum {toolchain['python_minimum']}), "
                f"pefile={toolchain['pefile_actual']} "
                f"(exact {toolchain['pefile_required_exact']}), "
                f"capstone={toolchain['capstone_actual']} "
                f"(exact {toolchain['capstone_required_exact']}), "
                f"all_ok={str(toolchain['all_ok']).lower()}"
            )
        return 0 if toolchain["all_ok"] else 3
    if not toolchain["all_ok"]:
        print("V5.2 audit toolchain gate failed", file=sys.stderr)
        return 3

    artifact_root = (
        Path(__file__).resolve().parents[2]
        / "tools"
        / "artifacts"
        / "San9BridgeV52"
    )
    if args.profile == "offline":
        dll_path = args.dll or artifact_root / "offline" / "San9BridgeV52.dll"
        controller_path = (
            args.controller
            or artifact_root / "offline" / "San9BridgeV52SelfTest.exe"
        )
    else:
        dll_path = (
            args.dll
            or artifact_root / "live-optin" / "San9BridgeV52Live.dll"
        )
        controller_path = (
            args.controller
            or artifact_root
            / "live-optin"
            / "San9BridgeV52LiveController.exe"
        )
    try:
        result = audit_artifacts(args.profile, dll_path, controller_path)
    except (OSError, pefile.PEFormatError, StopIteration) as error:
        print(f"V5.2 audit input error: {error}", file=sys.stderr)
        return 2
    result["audit_toolchain"] = toolchain
    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        print(f"San9Bridge V5.2 {args.profile} on-disk native audit")
        print(
            "artifact_executed=false dll_loaded=false process_accessed=false "
            "hook_installed=false business_execution_authorized=false "
            "live_dynamic_safety_proven=false"
        )
        for name in ("dll", "controller"):
            artifact = result[name]
            passed = sum(bool(value) for value in artifact["checks"].values())
            total = len(artifact["checks"])
            print(
                f"{name}: {passed}/{total}, machine={artifact['machine']}, "
                f"tls_callbacks={artifact['tls_callback_count']}, "
                f"sha256={artifact['sha256']}, "
                f"all_ok={str(artifact['all_ok']).lower()}"
            )
            for check, ok in artifact["checks"].items():
                if not ok:
                    print(f"  FAIL {check}")
        print(f"all_ok={str(result['all_ok']).lower()}")
    return 0 if result["all_ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
