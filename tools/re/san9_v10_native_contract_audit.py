#!/usr/bin/env python3
"""Offline PE/source audit for the non-authorizing V10 native contract."""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import sys

try:
    import pefile
except ImportError as exc:  # pragma: no cover - explicit environment gate
    raise SystemExit(f"pefile is required: {exc}")


EXPECTED_PEFILE_VERSION = "2024.8.26"
EXPECTED_EXPORTS = {
    "San9BridgeV10_GetContract",
    "San9BridgeV10_GetCommandDescriptor",
    "San9BridgeV10_GetStageRoute",
    "San9BridgeV10_GetCleanupRoute",
    "San9BridgeV10_ValidateRequest",
    "San9BridgeV10_ComputeRequestFingerprint",
    "San9BridgeV10_OfflineSelfTest",
}
FORBIDDEN_IMPORTS = {
    "OpenProcess",
    "ReadProcessMemory",
    "WriteProcessMemory",
    "VirtualAllocEx",
    "VirtualProtectEx",
    "CreateRemoteThread",
    "SetWindowsHookExA",
    "SetWindowsHookExW",
    "SendInput",
    "PostMessageA",
    "PostMessageW",
    "SendMessageA",
    "SendMessageW",
    "CreateToolhelp32Snapshot",
    "TerminateProcess",
}

EXPECTED_DLL_IMPORTS = {
    "API-MS-WIN-CRT-RUNTIME-L1-1-0.DLL": {
        "_execute_onexit_table", "_exit", "_initialize_onexit_table",
        "_initterm", "_initterm_e", "_register_onexit_function", "abort",
    },
    "KERNEL32.DLL": {
        "DeleteCriticalSection", "EnterCriticalSection", "GetLastError",
        "InitializeCriticalSection", "LeaveCriticalSection", "Sleep",
        "TlsGetValue", "VirtualProtect", "VirtualQuery",
    },
    "API-MS-WIN-CRT-STDIO-L1-1-0.DLL": {
        "__acrt_iob_func", "__stdio_common_vfprintf", "fwrite",
    },
    "API-MS-WIN-CRT-HEAP-L1-1-0.DLL": {"calloc", "free"},
    "API-MS-WIN-CRT-STRING-L1-1-0.DLL": {"strncmp"},
}

EXPECTED_EXE_IMPORTS = {
    "API-MS-WIN-CRT-HEAP-L1-1-0.DLL": {
        "_set_new_mode", "calloc", "free", "malloc",
    },
    "API-MS-WIN-CRT-RUNTIME-L1-1-0.DLL": {
        "__p___argc", "__p___argv", "_cexit", "_configure_narrow_argv",
        "_crt_atexit", "_exit", "_initialize_narrow_environment", "_initterm",
        "_initterm_e", "_set_app_type", "_set_invalid_parameter_handler",
        "abort", "exit", "signal",
    },
    "API-MS-WIN-CRT-STDIO-L1-1-0.DLL": {
        "__acrt_iob_func", "__p__commode", "__p__fmode",
        "__stdio_common_vfprintf", "__stdio_common_vsprintf", "fwrite",
    },
    "API-MS-WIN-CRT-STRING-L1-1-0.DLL": {"strcmp", "strncmp"},
    "KERNEL32.DLL": {
        "DeleteCriticalSection", "EnterCriticalSection", "GetLastError",
        "InitializeCriticalSection", "LeaveCriticalSection",
        "SetUnhandledExceptionFilter", "Sleep", "TlsGetValue",
        "VirtualProtect", "VirtualQuery",
    },
    "API-MS-WIN-CRT-ENVIRONMENT-L1-1-0.DLL": {"__p__environ"},
    "API-MS-WIN-CRT-MATH-L1-1-0.DLL": {"__setusermatherr"},
}


def sha256(path: pathlib.Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def audit_pe(path: pathlib.Path, expect_dll: bool) -> tuple[list[dict], dict]:
    checks: list[dict] = []

    def check(name: str, passed: bool, detail: str = "") -> None:
        checks.append({"name": name, "passed": bool(passed), "detail": detail})

    pe = pefile.PE(str(path), fast_load=False)
    check(f"{path.name}:machine_i386", pe.FILE_HEADER.Machine == 0x14C)
    check(f"{path.name}:pe32", pe.OPTIONAL_HEADER.Magic == 0x10B)
    is_dll = bool(pe.FILE_HEADER.Characteristics & 0x2000)
    check(f"{path.name}:kind", is_dll == expect_dll, f"is_dll={is_dll}")
    check(
        f"{path.name}:dynamic_base",
        bool(pe.OPTIONAL_HEADER.DllCharacteristics & 0x40),
    )
    check(
        f"{path.name}:nx_compat",
        bool(pe.OPTIONAL_HEADER.DllCharacteristics & 0x100),
    )

    wx_sections = []
    for section in pe.sections:
        writable = bool(section.Characteristics & 0x80000000)
        executable = bool(section.Characteristics & 0x20000000)
        if writable and executable:
            wx_sections.append(section.Name.rstrip(b"\0").decode("ascii", "replace"))
    check(f"{path.name}:no_wx_sections", not wx_sections, repr(wx_sections))

    imports: set[str] = set()
    imports_by_library: dict[str, set[str]] = {}
    ordinal_imports: list[str] = []
    for descriptor in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []):
        library = descriptor.dll.decode("ascii", "replace").upper()
        symbols = imports_by_library.setdefault(library, set())
        for item in descriptor.imports:
            if item.name:
                symbol = item.name.decode("ascii", "replace")
                imports.add(symbol)
                symbols.add(symbol)
            else:
                ordinal_imports.append(f"{library}:{item.ordinal}")
    forbidden = sorted(imports.intersection(FORBIDDEN_IMPORTS))
    check(f"{path.name}:no_forbidden_imports", not forbidden, repr(forbidden))
    check(f"{path.name}:no_ordinal_imports", not ordinal_imports, repr(ordinal_imports))
    delayed = getattr(pe, "DIRECTORY_ENTRY_DELAY_IMPORT", [])
    check(f"{path.name}:no_delay_imports", not delayed, f"count={len(delayed)}")
    expected_imports = EXPECTED_DLL_IMPORTS if expect_dll else EXPECTED_EXE_IMPORTS
    check(
        f"{path.name}:exact_import_allowlist",
        imports_by_library == expected_imports,
        "actual=" + repr({key: sorted(value) for key, value in imports_by_library.items()}),
    )

    exports: set[str] = set()
    unnamed_exports: list[int] = []
    forwarded_exports: list[str] = []
    export_directory = getattr(pe, "DIRECTORY_ENTRY_EXPORT", None)
    if export_directory is not None:
        for item in export_directory.symbols:
            if item.name:
                export_name = item.name.decode("ascii", "replace")
                exports.add(export_name)
                if item.forwarder:
                    forwarded_exports.append(
                        export_name + "->" + item.forwarder.decode("ascii", "replace")
                    )
            else:
                unnamed_exports.append(item.ordinal)
                if item.forwarder:
                    forwarded_exports.append(
                        str(item.ordinal) + "->" + item.forwarder.decode("ascii", "replace")
                    )
    check(f"{path.name}:no_unnamed_exports", not unnamed_exports, repr(unnamed_exports))
    check(f"{path.name}:no_forwarded_exports", not forwarded_exports, repr(forwarded_exports))
    if expect_dll:
        check(
            f"{path.name}:exact_exports",
            exports == EXPECTED_EXPORTS,
            f"exports={sorted(exports)}",
        )
    else:
        check(f"{path.name}:no_contract_exports", not exports, repr(sorted(exports)))

    return checks, {
        "path": str(path),
        "sha256": sha256(path),
        "imports": sorted(imports),
        "imports_by_library": {
            key: sorted(value) for key, value in imports_by_library.items()
        },
        "exports": sorted(exports),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dll", type=pathlib.Path, required=True)
    parser.add_argument("--selftest", type=pathlib.Path, required=True)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args()

    checks: list[dict] = []
    checks.append(
        {
            "name": "pefile_exact_version",
            "passed": pefile.__version__ == EXPECTED_PEFILE_VERSION,
            "detail": pefile.__version__,
        }
    )
    dll_checks, dll_info = audit_pe(args.dll.resolve(), True)
    exe_checks, exe_info = audit_pe(args.selftest.resolve(), False)
    checks.extend(dll_checks)
    checks.extend(exe_checks)
    all_ok = all(item["passed"] for item in checks)
    result = {
        "all_ok": all_ok,
        "all_ok_scope": "offline_non_authorizing_native_contract_only",
        "execution_authorized": False,
        "process_accessed": False,
        "checks_passed": sum(1 for item in checks if item["passed"]),
        "checks_total": len(checks),
        "checks": checks,
        "dll": dll_info,
        "selftest": exe_info,
    }
    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        print(
            f"V10 PE audit: {result['checks_passed']}/{result['checks_total']} "
            f"passed; execution_authorized=false; process_accessed=false"
        )
        for item in checks:
            if not item["passed"]:
                print(f"FAIL {item['name']}: {item['detail']}")
        print(f"DLL SHA256={dll_info['sha256']}")
        print(f"SelfTest SHA256={exe_info['sha256']}")
    return 0 if all_ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
