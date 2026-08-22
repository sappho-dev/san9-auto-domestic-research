#!/usr/bin/env python3
"""Offline PE audit for the V5.1 ping-bootstrap proof artifacts.

This script reads two ordinary files. It never enumerates or opens a process,
loads the DLL, installs a hook, or authorizes any game/business operation.
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
except ImportError as error:  # pragma: no cover
    raise SystemExit(
        "pefile 2024.8.26 is required: "
        "python -m pip install pefile==2024.8.26"
    ) from error


MINIMUM_PYTHON = (3, 11, 0)
REQUIRED_PEFILE_VERSION = "2024.8.26"
MACHINE_I386 = 0x014C
PE32_MAGIC = 0x010B
IMAGE_FILE_DLL = 0x2000

EXPECTED_EXPORTS = {
    "San9Bridge_Bootstrap",
    "San9Bridge_ForegroundIdleProc",
    "San9Bridge_GetContract",
    "San9Bridge_GetMsgProc",
    "San9Bridge_IdleBridge",
    "San9Bridge_OfflineSelfTest",
}

FORBIDDEN_MODULES = {
    "advapi32.dll",
    "dbghelp.dll",
    "iphlpapi.dll",
    "ntdll.dll",
    "psapi.dll",
    "urlmon.dll",
    "winhttp.dll",
    "wininet.dll",
    "winmm.dll",
    "ws2_32.dll",
}

FORBIDDEN_IMPORTS = {
    "createremotethread",
    "createremotethreadex",
    "createtoolhelp32snapshot",
    "debugactiveprocess",
    "enumprocesses",
    "enumprocessmodules",
    "freelibrary",
    "freelibraryandexitthread",
    "getwindowthreadprocessid",
    "mouse_event",
    "ntcreatethreadex",
    "openprocess",
    "openthread",
    "postmessagea",
    "postmessagew",
    "process32first",
    "process32firstw",
    "process32next",
    "process32nextw",
    "queueuserapc",
    "readprocessmemory",
    "sendinput",
    "sendmessagea",
    "sendmessagew",
    "setwindowlonga",
    "setwindowlongw",
    "setwindowlongptra",
    "setwindowlongptrw",
    "setwindowshookexa",
    "setwindowshookexw",
    "thread32first",
    "thread32next",
    "unhookwindowshookex",
    "virtualallocex",
    "virtualfreeex",
    "virtualprotectex",
    "writeprocessmemory",
}

FORBIDDEN_BINARY_TOKENS = {
    b"SetWindowsHookEx",
    b"OpenProcess",
    b"ReadProcessMemory",
    b"WriteProcessMemory",
    b"VirtualAllocEx",
    b"CreateRemoteThread",
    b"San9PK.exe",
}


def version_gate(
    python_version: tuple[int, int, int],
    pefile_version: str,
) -> bool:
    return python_version >= MINIMUM_PYTHON and pefile_version == REQUIRED_PEFILE_VERSION


def version_gate_report() -> dict[str, Any]:
    actual_python = tuple(sys.version_info[:3])
    actual_pefile = str(getattr(pefile, "__version__", ""))
    checks = {
        "accepts_minimum_and_exact_pefile": version_gate(
            MINIMUM_PYTHON,
            REQUIRED_PEFILE_VERSION,
        ),
        "rejects_python_below_minimum": not version_gate(
            (MINIMUM_PYTHON[0], MINIMUM_PYTHON[1] - 1, 99),
            REQUIRED_PEFILE_VERSION,
        ),
        "rejects_wrong_pefile_patch": not version_gate(
            MINIMUM_PYTHON,
            "2024.8.25",
        ),
        "rejects_pefile_version_suffix": not version_gate(
            MINIMUM_PYTHON,
            REQUIRED_PEFILE_VERSION + "+local",
        ),
        "actual_toolchain_allowed": version_gate(actual_python, actual_pefile),
    }
    return {
        "python_executable": str(Path(sys.executable).resolve()),
        "python_actual": ".".join(str(part) for part in actual_python),
        "python_minimum": ".".join(str(part) for part in MINIMUM_PYTHON),
        "pefile_actual": actual_pefile,
        "pefile_required_exact": REQUIRED_PEFILE_VERSION,
        "checks": checks,
        "all_ok": all(checks.values()),
    }


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_pe(path: Path) -> tuple[bytes, pefile.PE]:
    data = path.read_bytes()
    return data, pefile.PE(data=data, fast_load=False)


def imports(pe: pefile.PE) -> dict[str, list[str]]:
    result: dict[str, list[str]] = {}
    for descriptor in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []):
        module = descriptor.dll.decode("ascii", errors="replace").lower()
        names: list[str] = []
        for imported in descriptor.imports:
            if imported.name is None:
                names.append(f"#{imported.ordinal}")
            else:
                names.append(imported.name.decode("ascii", errors="replace"))
        result[module] = sorted(names)
    return dict(sorted(result.items()))


def exports(pe: pefile.PE) -> list[str]:
    directory = getattr(pe, "DIRECTORY_ENTRY_EXPORT", None)
    if directory is None:
        return []
    result: list[str] = []
    for symbol in directory.symbols:
        if symbol.name is None:
            result.append(f"#{symbol.ordinal}")
        else:
            result.append(symbol.name.decode("ascii", errors="replace"))
        if symbol.forwarder is not None:
            result[-1] += "->" + symbol.forwarder.decode("ascii", errors="replace")
    return sorted(result)


def tls_callback_count(pe: pefile.PE) -> int:
    directory = getattr(pe, "DIRECTORY_ENTRY_TLS", None)
    if directory is None or directory.struct.AddressOfCallBacks == 0:
        return 0
    pointer_size = 4 if pe.OPTIONAL_HEADER.Magic == PE32_MAGIC else 8
    callback_rva = int(directory.struct.AddressOfCallBacks - pe.OPTIONAL_HEADER.ImageBase)
    unpack = "<I" if pointer_size == 4 else "<Q"
    count = 0
    for index in range(64):
        raw = pe.get_data(callback_rva + (index * pointer_size), pointer_size)
        if len(raw) != pointer_size or struct.unpack(unpack, raw)[0] == 0:
            break
        count += 1
    return count


def inspect(path: Path, expect_dll: bool) -> dict[str, Any]:
    data, pe = read_pe(path)
    imported = imports(pe)
    exported = exports(pe)
    imported_modules = set(imported)
    imported_names = {
        name.lower()
        for names in imported.values()
        for name in names
        if not name.startswith("#")
    }
    ordinal_imports = sorted(
        f"{module}!{name}"
        for module, names in imported.items()
        for name in names
        if name.startswith("#")
    )
    bad_modules = sorted(imported_modules & FORBIDDEN_MODULES)
    bad_imports = sorted(imported_names & FORBIDDEN_IMPORTS)
    bad_tokens = sorted(
        token.decode("ascii")
        for token in FORBIDDEN_BINARY_TOKENS
        if token.lower() in data.lower()
    )
    is_dll = bool(pe.FILE_HEADER.Characteristics & IMAGE_FILE_DLL)
    user32 = imported.get("user32.dll", [])
    tls_callbacks = tls_callback_count(pe)
    checks = {
        "machine_i386": pe.FILE_HEADER.Machine == MACHINE_I386,
        "optional_header_pe32": pe.OPTIONAL_HEADER.Magic == PE32_MAGIC,
        "dll_characteristic_exact": is_dll == expect_dll,
        "forbidden_modules_absent": not bad_modules,
        "forbidden_imports_absent": not bad_imports,
        "forbidden_binary_tokens_absent": not bad_tokens,
        "ordinal_imports_absent": not ordinal_imports,
        "no_delay_imports": not bool(getattr(pe, "DIRECTORY_ENTRY_DELAY_IMPORT", [])),
    }
    if expect_dll:
        checks.update(
            {
                "exports_exact_and_clean": set(exported) == EXPECTED_EXPORTS
                and len(exported) == len(EXPECTED_EXPORTS),
                "only_call_next_hook_from_user32": user32 == ["CallNextHookEx"],
                "dll_has_no_dynamic_loader_import": not {
                    "getprocaddress",
                    "loadlibrarya",
                    "loadlibraryw",
                }
                & imported_names,
                "dll_has_pin_and_tls_runtime": {
                    "getmodulehandleexw",
                    "tlsgetvalue",
                }
                <= imported_names,
            }
        )
    else:
        checks.update(
            {
                "controller_exports_none": exported == [],
                "controller_has_no_user32": user32 == [],
                "controller_loader_is_local_harness_only": {
                    "getmodulefilenamew",
                    "getprocaddress",
                    "loadlibraryw",
                }
                <= imported_names,
            }
        )
    return {
        "path": str(path.resolve()),
        "size": len(data),
        "sha256": sha256(data),
        "machine": f"0x{pe.FILE_HEADER.Machine:04X}",
        "optional_header_magic": f"0x{pe.OPTIONAL_HEADER.Magic:04X}",
        "is_dll": is_dll,
        "exports": exported,
        "imports": imported,
        "forbidden_modules_found": bad_modules,
        "forbidden_imports_found": bad_imports,
        "forbidden_binary_tokens_found": bad_tokens,
        "ordinal_imports_found": ordinal_imports,
        "loader_runtime_observations": {
            "tls_callback_count": tls_callbacks,
            "crt_or_tls_loader_work_present": tls_callbacks != 0,
            "local_virtualprotect_import_present": "virtualprotect" in imported_names,
            "local_virtualquery_import_present": "virtualquery" in imported_names,
        },
        "checks": checks,
        "all_ok": all(checks.values()),
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    default_artifacts = (
        Path(__file__).resolve().parents[2]
        / "tools"
        / "artifacts"
        / "San9BridgeV51"
    )
    parser.add_argument(
        "--dll",
        type=Path,
        default=default_artifacts / "San9BridgeV51.dll",
    )
    parser.add_argument(
        "--controller",
        type=Path,
        default=default_artifacts / "San9BridgeV51SelfTest.exe",
    )
    parser.add_argument("--json", action="store_true")
    parser.add_argument(
        "--self-test-version-gates",
        action="store_true",
        help="run actual and synthetic Python/pefile version gates without reading PE files",
    )
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
                "San9Bridge V5.1 audit toolchain gate: "
                f"{passed}/{total}, python={toolchain['python_actual']} "
                f"(minimum {toolchain['python_minimum']}), "
                f"pefile={toolchain['pefile_actual']} "
                f"(required {toolchain['pefile_required_exact']}), "
                f"all_ok={str(toolchain['all_ok']).lower()}"
            )
            for check, ok in toolchain["checks"].items():
                if not ok:
                    print(f"  FAIL {check}")
        return 0 if toolchain["all_ok"] else 3
    if not toolchain["all_ok"]:
        print(
            "native audit toolchain gate failed: "
            f"python={toolchain['python_actual']} (minimum {toolchain['python_minimum']}), "
            f"pefile={toolchain['pefile_actual']} "
            f"(required exactly {toolchain['pefile_required_exact']})",
            file=sys.stderr,
        )
        return 3
    try:
        dll = inspect(args.dll, expect_dll=True)
        controller = inspect(args.controller, expect_dll=False)
    except (OSError, pefile.PEFormatError) as error:
        print(f"native audit input error: {error}", file=sys.stderr)
        return 2

    result = {
        "scope": "offline PE32 ping-bootstrap proof artifacts only",
        "process_accessed": False,
        "hook_installed": False,
        "live_mode_present": False,
        "live_loader_lifecycle_proven": False,
        "business_execution_authorized": False,
        "production_protocol_compatible": False,
        "csharp_bridge_288_compatible": False,
        "audit_toolchain": toolchain,
        "dll": dll,
        "controller": controller,
        "all_ok": dll["all_ok"] and controller["all_ok"],
    }
    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        print("San9Bridge V5.1 offline native PE audit")
        print(
            f"toolchain: python={toolchain['python_actual']} "
            f"(minimum {toolchain['python_minimum']}), "
            f"pefile={toolchain['pefile_actual']} "
            f"(required exactly {toolchain['pefile_required_exact']})"
        )
        print(
            "scope=ping-bootstrap-proof process_accessed=false hook_installed=false "
            "live_mode_present=false business_execution_authorized=false"
        )
        for name in ("dll", "controller"):
            artifact = result[name]
            passed = sum(bool(value) for value in artifact["checks"].values())
            total = len(artifact["checks"])
            print(
                f"{name}: {passed}/{total} checks, machine={artifact['machine']}, "
                f"sha256={artifact['sha256']}, all_ok={str(artifact['all_ok']).lower()}"
            )
            if name == "dll":
                loader = artifact["loader_runtime_observations"]
                print(
                    "  loader observation: "
                    f"tls_callbacks={loader['tls_callback_count']} "
                    "live_loader_lifecycle_proven=false"
                )
            for check, ok in artifact["checks"].items():
                if not ok:
                    print(f"  FAIL {check}")
        print(f"all_ok={str(result['all_ok']).lower()}")
    return 0 if result["all_ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
