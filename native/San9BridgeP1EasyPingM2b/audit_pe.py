#!/usr/bin/env python3
"""Static-only PE/machine-code gate for M2b; never loads either live image."""

from __future__ import annotations

import argparse
import hashlib
import struct
import sys
from pathlib import Path

import pefile


EXPECTED_PEFILE_VERSION = "2024.8.26"
IMAGE_FILE_MACHINE_I386 = 0x14C
IMAGE_FILE_DLL = 0x2000
IMAGE_SCN_MEM_EXECUTE = 0x20000000
IMAGE_SCN_MEM_READ = 0x40000000
IMAGE_SCN_MEM_WRITE = 0x80000000

EXPECTED_EXPORTS = {
    "San9BridgeP1M2b_Contract",
    "San9BridgeP1M2b_GetMsgProc",
    "San9BridgeP1M2b_IdleBridge",
}

COMMON_CRT = {
    "api-ms-win-crt-runtime-l1-1-0.dll",
    "api-ms-win-crt-stdio-l1-1-0.dll",
    "api-ms-win-crt-heap-l1-1-0.dll",
}

OFFLINE_IMPORTS = {
    "api-ms-win-crt-heap-l1-1-0.dll": {
        "_set_new_mode", "free", "malloc"
    },
    "api-ms-win-crt-runtime-l1-1-0.dll": {
        "__p___argc", "__p___argv", "_cexit", "_configure_narrow_argv",
        "_crt_atexit", "_exit", "_initialize_narrow_environment", "_initterm",
        "_initterm_e", "_set_app_type", "_set_invalid_parameter_handler",
        "abort", "exit", "signal",
    },
    "api-ms-win-crt-stdio-l1-1-0.dll": {
        "__acrt_iob_func", "__p__commode", "__p__fmode",
        "__stdio_common_vfprintf", "fwrite",
    },
    "kernel32.dll": {
        "DeleteCriticalSection", "EnterCriticalSection", "GetLastError",
        "InitializeCriticalSection", "LeaveCriticalSection",
        "SetUnhandledExceptionFilter", "Sleep", "TlsGetValue",
        "VirtualProtect", "VirtualQuery",
    },
    "api-ms-win-crt-environment-l1-1-0.dll": {"__p__environ"},
    "api-ms-win-crt-math-l1-1-0.dll": {"__setusermatherr"},
}

CONTROLLER_IMPORTS = {
    "user32.dll": {
        "EnumWindows", "GetClassNameW", "GetWindowThreadProcessId", "IsWindow",
        "PostThreadMessageW", "RegisterWindowMessageW", "SetWindowsHookExW",
        "UnhookWindowsHookEx",
    },
    "advapi32.dll": {
        "ConvertSidToStringSidW",
        "ConvertStringSecurityDescriptorToSecurityDescriptorW",
        "GetTokenInformation", "OpenProcessToken",
    },
    "bcrypt.dll": {"BCryptGenRandom"},
    "api-ms-win-crt-heap-l1-1-0.dll": {"_set_new_mode", "free", "malloc"},
    "api-ms-win-crt-private-l1-1-0.dll": {"wcsrchr"},
    "api-ms-win-crt-runtime-l1-1-0.dll": OFFLINE_IMPORTS[
        "api-ms-win-crt-runtime-l1-1-0.dll"
    ],
    "api-ms-win-crt-stdio-l1-1-0.dll": {
        "__acrt_iob_func", "__p__commode", "__p__fmode",
        "__stdio_common_vfprintf", "fflush", "fgets", "fwrite", "putchar",
        "puts",
    },
    "api-ms-win-crt-string-l1-1-0.dll": {
        "strcmp", "strcspn", "wcscpy", "wcslen", "wcsncpy"
    },
    "kernel32.dll": {
        "CloseHandle", "CompareStringOrdinal", "CreateFileMappingW",
        "CreateFileW", "CreateToolhelp32Snapshot", "DeleteCriticalSection",
        "EnterCriticalSection", "GetCurrentProcess", "GetCurrentProcessId",
        "GetFileAttributesW", "GetFileSizeEx", "GetFullPathNameW",
        "GetLastError", "GetModuleFileNameW", "GetProcAddress",
        "GetProcessHeap", "GetProcessId", "GetProcessTimes", "GetTickCount64",
        "GlobalAddAtomW", "GlobalDeleteAtom", "HeapAlloc", "HeapFree",
        "InitializeCriticalSection", "LeaveCriticalSection", "LoadLibraryExW",
        "LocalFree", "MapViewOfFile", "Module32FirstW", "Module32NextW",
        "OpenProcess", "Process32FirstW", "Process32NextW",
        "QueryFullProcessImageNameW", "ReadFile", "ReadProcessMemory",
        "SetUnhandledExceptionFilter", "Sleep", "TlsGetValue",
        "UnmapViewOfFile", "VirtualProtect", "VirtualQuery", "VirtualQueryEx",
        "lstrcatW", "lstrcpyW",
    },
    "api-ms-win-crt-environment-l1-1-0.dll": {"__p__environ"},
    "api-ms-win-crt-math-l1-1-0.dll": {"__setusermatherr"},
}

DLL_IMPORTS = {
    "user32.dll": {"CallNextHookEx", "GetWindowThreadProcessId", "IsWindow"},
    "api-ms-win-crt-runtime-l1-1-0.dll": {
        "_execute_onexit_table", "_exit", "_initialize_onexit_table", "_initterm",
        "_initterm_e", "_register_onexit_function", "abort",
    },
    "api-ms-win-crt-string-l1-1-0.dll": {"wcsncmp"},
    "kernel32.dll": {
        "CloseHandle", "DeleteCriticalSection", "DisableThreadLibraryCalls",
        "EnterCriticalSection", "GetCurrentProcessId", "GetCurrentThreadId",
        "GetLastError", "GetModuleHandleExW", "GetModuleHandleW", "GetTickCount64",
        "GlobalGetAtomNameW", "InitializeCriticalSection", "LeaveCriticalSection",
        "MapViewOfFile", "OpenFileMappingW", "Sleep", "TlsGetValue",
        "UnmapViewOfFile", "VirtualProtect", "VirtualQuery",
    },
    "api-ms-win-crt-stdio-l1-1-0.dll": {
        "__acrt_iob_func", "__stdio_common_vfprintf", "fwrite",
    },
    "api-ms-win-crt-heap-l1-1-0.dll": {"free"},
}

FORBIDDEN_IMPORTS = {
    "WriteProcessMemory", "VirtualProtectEx", "VirtualAllocEx",
    "CreateRemoteThread", "QueueUserAPC", "SendInput",
    "CreateNamedPipeW", "ConnectNamedPipe", "socket", "connect", "FreeLibrary",
}


class AuditFailure(RuntimeError):
    pass


def fail(message: str) -> None:
    raise AuditFailure(message)


def file_sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def section_for_rva(image: pefile.PE, rva: int):
    for section in image.sections:
        start = int(section.VirtualAddress)
        size = max(int(section.Misc_VirtualSize), int(section.SizeOfRawData))
        if start <= rva < start + size:
            flags = int(section.Characteristics)
            return (
                section.Name.rstrip(b"\0").decode("ascii", "replace"),
                bool(flags & IMAGE_SCN_MEM_READ),
                bool(flags & IMAGE_SCN_MEM_WRITE),
                bool(flags & IMAGE_SCN_MEM_EXECUTE),
            )
    return None


def imports(image: pefile.PE) -> dict[str, set[str]]:
    result: dict[str, set[str]] = {}
    for descriptor in getattr(image, "DIRECTORY_ENTRY_IMPORT", []):
        module = descriptor.dll.decode("ascii").lower()
        if module in result:
            fail(f"duplicate import descriptor: {module}")
        names: set[str] = set()
        for imported in descriptor.imports:
            if imported.name is None:
                fail(f"ordinal import rejected: {module}!{imported.ordinal}")
            name = imported.name.decode("ascii")
            if name in names:
                fail(f"duplicate imported symbol: {module}!{name}")
            names.add(name)
        result[module] = names
    return result


def exports(image: pefile.PE) -> dict[str, int]:
    result: dict[str, int] = {}
    directory = getattr(image, "DIRECTORY_ENTRY_EXPORT", None)
    if directory is None:
        return result
    for symbol in directory.symbols:
        if symbol.name is None or symbol.forwarder is not None:
            fail("unnamed or forwarded export rejected")
        name = symbol.name.decode("ascii")
        if name in result:
            fail(f"duplicate export: {name}")
        result[name] = int(symbol.address)
    return result


def verify_common(path: Path, expected_dll: bool, expected_sha: str | None):
    if expected_sha and file_sha(path) != expected_sha.upper():
        fail(f"whole image hash mismatch: {path.name}")
    image = pefile.PE(str(path), fast_load=False)
    if image.FILE_HEADER.Machine != IMAGE_FILE_MACHINE_I386:
        fail(f"not x86: {path.name}")
    is_dll = bool(image.FILE_HEADER.Characteristics & IMAGE_FILE_DLL)
    if is_dll != expected_dll:
        fail(f"PE role mismatch: {path.name}")
    if getattr(image, "DIRECTORY_ENTRY_DELAY_IMPORT", None):
        fail(f"delay imports rejected: {path.name}")
    entry = section_for_rva(image, int(image.OPTIONAL_HEADER.AddressOfEntryPoint))
    if entry is None or not entry[1] or entry[2] or not entry[3]:
        fail(f"entrypoint must be RX/non-W: {path.name}")
    for section in image.sections:
        flags = int(section.Characteristics)
        if (flags & IMAGE_SCN_MEM_EXECUTE) and (flags & IMAGE_SCN_MEM_WRITE):
            fail(f"RWX section rejected: {path.name}")
    tls = getattr(image, "DIRECTORY_ENTRY_TLS", None)
    if tls is None:
        fail(f"TLS directory missing: {path.name}")
    table_rva = int(tls.struct.AddressOfCallBacks - image.OPTIONAL_HEADER.ImageBase)
    callbacks = []
    for index in range(16):
        raw = image.get_data(table_rva + index * 4, 4)
        if len(raw) != 4:
            fail(f"truncated TLS table: {path.name}")
        value = struct.unpack("<I", raw)[0]
        if value == 0:
            break
        callback_rva = value - int(image.OPTIONAL_HEADER.ImageBase)
        owner = section_for_rva(image, callback_rva)
        if owner is None or not owner[1] or owner[2] or not owner[3]:
            fail(f"TLS callback must be RX/non-W: {path.name}")
        callbacks.append(callback_rva)
    if len(callbacks) != 2:
        fail(f"unexpected TLS callback count: {path.name} count={len(callbacks)}")
    return image


def verify_imports(image: pefile.PE, expected: dict[str, set[str]], role: str) -> None:
    actual = imports(image)
    if actual != expected:
        fail(f"exact import set changed for {role}: actual={actual!r}")
    flat = set().union(*actual.values()) if actual else set()
    forbidden = sorted(flat & FORBIDDEN_IMPORTS)
    if forbidden:
        fail(f"forbidden imports for {role}: {forbidden!r}")


def audit_offline(path: Path, expected_sha: str | None) -> None:
    image = verify_common(path, False, expected_sha)
    if exports(image):
        fail("offline selftest must not export")
    verify_imports(image, OFFLINE_IMPORTS, "offline")
    if b"S3_READONLY_BUILD=1;READ_DISPATCH=17;WRITE_DISPATCH=0;MAX_READ=256\0" \
            not in path.read_bytes():
        fail("offline S3 readonly dispatch marker missing")


def audit_controller(path: Path, expected_sha: str | None) -> None:
    image = verify_common(path, False, expected_sha)
    if exports(image):
        fail("controller must not export")
    verify_imports(image, CONTROLLER_IMPORTS, "controller")
    raw = path.read_bytes()
    for marker in (
        "D:P(A;;GA;;;".encode("utf-16le"),
        "Local\\San9P1M2b-".encode("utf-16le"),
        "San9P1M2b.Authenticated.Bootstrap.v1".encode("utf-16le"),
        b"--inspect\0",
        b"--probe0\0",
        b"--observe\0",
        b"--s5-no-apply\0",
        b"--s5-modal-probe\0",
        b"--s5-apply-once\0",
        b"--s6-cultivate-apply-once\0",
        b"--s6-patrol-apply-once\0",
        b"--s6-train-apply-once\0",
        b"--s6-repair-apply-once\0",
        b"WAITING_FOR_USER_MENU_SIGNAL",
        b"OPEN_CURRENT_CITY_MENU\0",
        b"USER_MENU_SIGNAL_ACCEPTED",
        b"REQUEST_SEALED_WAKE_POSTED_V8",
        b"S5_V8_TERMINAL_CAS=1;SIGNED_CONFIG_DEADLINE=1;"
        b"TIMEOUT_POISON_MONOTONIC=1\0",
        b"MENU_WAIT_FAILED",
        b"I_ACCEPT_PROBE0_PINNED_UNTIL_GAME_RESTART\0",
        b"I_ACCEPT_S3_READONLY_OBSERVATION_PINNED_UNTIL_GAME_RESTART\0",
        b"I_ACCEPT_S5_NO_APPLY_TIMING_PINNED_UNTIL_GAME_RESTART\0",
        b"I_ACCEPT_S5_MODAL_PROBE_PINNED_UNTIL_GAME_RESTART\0",
        b"I_ACCEPT_ONE_CURRENT_CITY_COMMERCE_APPLY_PINNED_UNTIL_GAME_RESTART\0",
        b"I_ACCEPT_ONE_CURRENT_CITY_CULTIVATE_APPLY_PINNED_UNTIL_GAME_RESTART\0",
        b"I_ACCEPT_ONE_CURRENT_CITY_PATROL_APPLY_PINNED_UNTIL_GAME_RESTART\0",
        b"I_ACCEPT_ONE_CURRENT_CITY_TRAIN_APPLY_PINNED_UNTIL_GAME_RESTART\0",
        b"I_ACCEPT_ONE_CURRENT_CITY_REPAIR_APPLY_PINNED_UNTIL_GAME_RESTART\0",
        "bridge_s6_cultivate_apply_once.dll".encode("utf-16le"),
        "bridge_s6_patrol_apply_once.dll".encode("utf-16le"),
        "bridge_s6_train_apply_once.dll".encode("utf-16le"),
        "bridge_s6_repair_apply_once.dll".encode("utf-16le"),
        b"WAITING_FOR_S5_MODAL_PROBE_SIGNAL",
        b"CURRENT_CITY_MENU_READY\0",
        b"S5_MODAL_PROBE_SIGNAL_ACCEPTED\0",
        b"S5_MODAL_PROBE_WAKE_POSTED_VTABLE_V7\0",
        b"S5_PERSISTENT_HOOK_UNHOOK_FAILED_RESTART_REQUIRED\n\0",
        b"S3_READONLY_BUILD=1;READ_DISPATCH=17;WRITE_DISPATCH=0;MAX_READ=256\0",
    ):
        if marker not in raw:
            fail("controller ACL/bootstrap marker missing")


def audit_dll(path: Path, expected_sha: str | None,
              apply_once: bool = False, cultivate: bool = False,
              patrol: bool = False, train: bool = False,
              repair: bool = False) -> None:
    image = verify_common(path, True, expected_sha)
    verify_imports(image, DLL_IMPORTS, "dll")
    exported = exports(image)
    if set(exported) != EXPECTED_EXPORTS:
        fail(f"DLL exports changed: {set(exported)!r}")
    for name, rva in exported.items():
        owner = section_for_rva(image, rva)
        if owner is None or not owner[1] or owner[2] or not owner[3]:
            fail(f"export not RX/non-W: {name}")
    wrapper = image.get_data(exported["San9BridgeP1M2b_IdleBridge"], 144)
    original_call = bytes.fromhex("b800414300ffd0")
    fxsave = bytes.fromhex("0fae00")
    fxrstor = bytes.fromhex("0fae08")
    if wrapper.count(original_call) != 1 or wrapper.count(fxsave) != 1 \
            or wrapper.count(fxrstor) != 1 or bytes.fromhex("c20400") not in wrapper:
        fail("idle wrapper does not contain exactly one original-idle call/ret4")
    required_wrapper_fragments = (
        bytes.fromhex("5589e5535657"),          # frame + EBX/ESI/EDI
        bytes.fromhex("81ec30020000"),          # bounded 560-byte save area
        bytes.fromhex("83e0f0"),                # 16-byte FXSAVE pointer
        bytes.fromhex("83e4f0"),                # 16-byte helper call stack
        bytes.fromhex("ff75e8e8"),              # leave(depth-token), not blind decrement
        bytes.fromhex("8945e4"),                # save original EAX
        bytes.fromhex("8b45e4"),                # restore original EAX
        bytes.fromhex("2500040000"),            # capture original DF
        bytes.fromhex("837de0007403fdeb01fc"),  # restore DF after all calls
        bytes.fromhex("81c4300200005f5e5b5d"),  # exact ESP/nonvolatile unwind
    )
    if any(fragment not in wrapper for fragment in required_wrapper_fragments):
        fail("idle wrapper ABI/FX state preservation fragment missing")
    restore_at = wrapper.find(fxrstor)
    df_restore_at = wrapper.find(bytes.fromhex("837de0007403fdeb01fc"))
    if restore_at < 0 or df_restore_at <= restore_at \
            or b"\xe8" in wrapper[df_restore_at:wrapper.find(bytes.fromhex("c20400"))]:
        fail("call occurs after FX/DF restoration")
    raw = path.read_bytes()
    if b"S3_READONLY_BUILD=1;READ_DISPATCH=17;WRITE_DISPATCH=0;MAX_READ=256\0" \
            not in raw:
        fail("DLL S3 readonly dispatch marker missing")
    if repair:
        apply_core = (b"S6_REPAIR_APPLY_ONCE_REQUEST_CORE=1;DISTINCT_KIND=1;"
            b"DISTINCT_HMAC_DOMAIN=1\0")
        apply_v1 = (b"S6_REPAIR_APPLY_ONCE_V1=1;"
            b"HANDLER_EXECUTE_RETURN_COMMAND=1;"
            b"COMMAND_APPLY_SHADOW=1;APPLY_EXACT_ONCE=1;DIRECT_DRIVER=0;"
            b"DIRECT_VALIDATOR=0;DIRECT_ATTACH=0\0")
        apply_identity = (b"S6_REPAIR_APPLY_ONCE=1;CURRENT_CITY=1;TOP5=5;"
            b"APPEND=0046EF70;COMMAND_CTOR=00489080;"
            b"RETURN_TO_NATIVE_DRIVER=1;DIRECT_APPLY=0\0")
        if any(marker not in raw for marker in
                (apply_core, apply_v1, apply_identity)):
            fail("Repair APPLY_ONCE DLL identity missing")
    elif train:
        apply_core = (b"S6_TRAIN_APPLY_ONCE_REQUEST_CORE=1;DISTINCT_KIND=1;"
            b"DISTINCT_HMAC_DOMAIN=1\0")
        apply_v1 = (b"S6_TRAIN_APPLY_ONCE_V1=1;"
            b"HANDLER_EXECUTE_RETURN_COMMAND=1;"
            b"COMMAND_APPLY_SHADOW=1;APPLY_EXACT_ONCE=1;DIRECT_DRIVER=0;"
            b"DIRECT_VALIDATOR=0;DIRECT_ATTACH=0\0")
        apply_identity = (b"S6_TRAIN_APPLY_ONCE=1;CURRENT_CITY=1;TOP5=5;"
            b"APPEND=0046EF70;COMMAND_CTOR=00489D80;"
            b"RETURN_TO_NATIVE_DRIVER=1;DIRECT_APPLY=0\0")
        if any(marker not in raw for marker in
                (apply_core, apply_v1, apply_identity)):
            fail("Train APPLY_ONCE DLL identity missing")
    elif patrol:
        apply_core = (b"S6_PATROL_APPLY_ONCE_REQUEST_CORE=1;DISTINCT_KIND=1;"
            b"DISTINCT_HMAC_DOMAIN=1\0")
        apply_v1 = (b"S6_PATROL_APPLY_ONCE_V1=1;"
            b"HANDLER_EXECUTE_RETURN_COMMAND=1;"
            b"COMMAND_APPLY_SHADOW=1;APPLY_EXACT_ONCE=1;DIRECT_DRIVER=0;"
            b"DIRECT_VALIDATOR=0;DIRECT_ATTACH=0\0")
        apply_identity = (b"S6_PATROL_APPLY_ONCE=1;CURRENT_CITY=1;TOP5=5;"
            b"APPEND=0046EF70;COMMAND_CTOR=00488410;"
            b"RETURN_TO_NATIVE_DRIVER=1;DIRECT_APPLY=0\0")
        if any(marker not in raw for marker in
                (apply_core, apply_v1, apply_identity)):
            fail("Patrol APPLY_ONCE DLL identity missing")
    elif cultivate:
        apply_core = (b"S6_CULTIVATE_APPLY_ONCE_REQUEST_CORE=1;DISTINCT_KIND=1;"
            b"DISTINCT_HMAC_DOMAIN=1\0")
        apply_v1 = (b"S6_CULTIVATE_APPLY_ONCE_V1=1;"
            b"HANDLER_EXECUTE_RETURN_COMMAND=1;"
            b"COMMAND_APPLY_SHADOW=1;APPLY_EXACT_ONCE=1;DIRECT_DRIVER=0;"
            b"DIRECT_VALIDATOR=0;DIRECT_ATTACH=0\0")
        apply_identity = (b"S6_CULTIVATE_APPLY_ONCE=1;CURRENT_CITY=1;TOP5=5;"
            b"APPEND=0046EF70;COMMAND_CTOR=00488B00;"
            b"RETURN_TO_NATIVE_DRIVER=1;DIRECT_APPLY=0\0")
        if any(marker not in raw for marker in
                (apply_core, apply_v1, apply_identity)):
            fail("Cultivate APPLY_ONCE DLL identity missing")
    elif apply_once:
        apply_core = (b"S5_APPLY_ONCE_REQUEST_CORE=1;DISTINCT_KIND=1;"
            b"DISTINCT_HMAC_DOMAIN=1\0")
        apply_v1 = (b"S5_APPLY_ONCE_V1=1;HANDLER_EXECUTE_RETURN_COMMAND=1;"
            b"COMMAND_APPLY_SHADOW=1;APPLY_EXACT_ONCE=1;DIRECT_DRIVER=0;"
            b"DIRECT_VALIDATOR=0;DIRECT_ATTACH=0\0")
        apply_identity = (b"S5_APPLY_ONCE=1;CURRENT_CITY=1;TOP5=5;"
            b"APPEND=0046EF70;COMMAND_CTOR=0048B340;"
            b"RETURN_TO_NATIVE_DRIVER=1;DIRECT_APPLY=0\0")
        if any(marker not in raw for marker in
                (apply_core, apply_v1, apply_identity)):
            fail("APPLY_ONCE DLL identity missing")
    else:
        s5_marker = (b"S5_NO_APPLY_BUILD=1;EVENT_CALLS=1;HANDLER_SHADOW=1;"
            b"NATIVE_APPLY_CALLS=0;COMMAND_CTORS=0;TARGET_BUSINESS_WRITES=0\0")
        if s5_marker not in raw:
            fail("DLL S5 NO_APPLY identity missing")
        s5_v8_marker = (b"S5_NO_APPLY_V8=1;MENU_WAKE=1;MENU_SHADOW=1;ROOT_EVENT=1;"
            b"HANDLER_SHADOW=1;EXECUTE_ONCE=1;APPLY=0;COMMAND_CTORS=0;"
            b"TARGET_BUSINESS_WRITES=0;TERMINAL_CAS=1;SIGNED_DEADLINE=1;"
            b"POISON_MONOTONIC=1\0")
        if s5_v8_marker not in raw:
            fail("DLL S5 v8 identity missing")
    modal_marker = (b"S5_MODAL_PROBE=VTABLE_V7;HOOK_WAKE=1;NULL_HWND=1;"
        b"VTABLE_BYTES=260;TICK_OFFSET=164;TICKS=3;ROOT_EVENT=0;"
        b"S5_PUBLISH=0;APPLY=0;AUTHORIZATION=0\0")
    if modal_marker not in raw:
        fail("DLL S5 modal vtable-v7 identity missing")
    executable = b"".join(section.get_data() for section in image.sections
        if int(section.Characteristics) & IMAGE_SCN_MEM_EXECUTE)
    root_event_call = bytes.fromhex("b8b0795100ffd0")
    if executable.count(root_event_call) != 1:
        fail("S5 must contain exactly one root-event call shim")
    modal_original_call = bytes.fromhex("b8c0754100ffd0")
    if executable.count(modal_original_call) != 2:
        fail("v7 probe plus v8 S5 wrappers must each call menu +0xA4 original once")
    modal_original_at = executable.find(modal_original_call)
    modal_prologue = bytes.fromhex("5589e5535657")
    modal_start = executable.rfind(modal_prologue, 0, modal_original_at)
    modal_tail = bytes.fromhex("81c4300200005f5e5b5dc3")
    modal_end_at = executable.find(modal_tail, modal_original_at)
    if modal_start < 0 or modal_original_at - modal_start > 48 or modal_end_at < 0:
        fail("modal vtable wrapper boundary missing")
    modal_wrapper = executable[modal_start:modal_end_at + len(modal_tail)]
    modal_original_relative = modal_wrapper.find(modal_original_call)
    modal_fxsave = modal_wrapper.find(bytes.fromhex("0fae00"))
    modal_fxrstor = modal_wrapper.find(bytes.fromhex("0fae08"))
    modal_df_restore = modal_wrapper.find(bytes.fromhex("837de4007403fdeb01fc"))
    modal_required_fragments = (
        bytes.fromhex("81ec30020000"),          # bounded 560-byte save area
        bytes.fromhex("894df08b45048945ec8b4df0"),  # self/caller and ECX ABI
        bytes.fromhex("8945e8"),                # preserve original return EAX
        bytes.fromhex("9c5825000400008945e4fc"),  # capture DF, then CLD
        bytes.fromhex("83e0f0"),                # 16-byte FXSAVE pointer
        bytes.fromhex("83e4f0ff75ecff75f0e8"),  # aligned helper(self, caller)
        bytes.fromhex("837de4007403fdeb01fc"),  # restore DF after all calls
        bytes.fromhex("8b45e8"),                # restore original return EAX
    )
    if any(fragment not in modal_wrapper for fragment in modal_required_fragments):
        fail("modal vtable wrapper ABI/FX state preservation fragment missing")
    if modal_wrapper.count(modal_original_call) != 1 \
            or modal_wrapper.count(bytes.fromhex("0fae00")) != 1 \
            or modal_wrapper.count(bytes.fromhex("0fae08")) != 1 \
            or bytes.fromhex("c20400") in modal_wrapper:
        fail("modal vtable wrapper original/FX/plain-ret contract changed")
    modal_exact_prefix = bytes.fromhex(
        "5589e553565781ec30020000894df08b45048945ec8b4df0") + modal_original_call
    modal_exact_suffix = (bytes.fromhex("837de4007403fdeb01fc8b45e8") + modal_tail)
    if not modal_wrapper.startswith(modal_exact_prefix):
        fail("modal vtable wrapper calls code before the native original")
    if modal_fxsave <= modal_original_relative or modal_fxrstor <= modal_fxsave \
            or modal_df_restore <= modal_fxrstor \
            or not modal_wrapper.endswith(modal_exact_suffix):
        fail("modal vtable wrapper original/helper/FX/DF ordering changed")
    v8_original_at = executable.find(
        modal_original_call, modal_original_at + len(modal_original_call))
    v8_start = executable.rfind(modal_prologue, 0, v8_original_at)
    v8_tail = bytes.fromhex("81c4400200005f5e5b5dc3")
    v8_end_at = executable.find(v8_tail, v8_original_at)
    if v8_start < 0 or v8_original_at - v8_start > 128 or v8_end_at < 0:
        fail("S5 v8 menu wrapper boundary missing")
    v8_wrapper = executable[v8_start:v8_end_at + len(v8_tail)]
    v8_original_relative = v8_wrapper.find(modal_original_call)
    v8_required_fragments = (
        bytes.fromhex("5589e553565781ec40020000"),
        bytes.fromhex("894df08b45048945ec"),       # save self/caller pre-original
        bytes.fromhex("9c5825000400008945e8fc"), # entry DF then CLD
        bytes.fromhex("83e4f0ff75ecff75f0e8"),   # entry(self, caller)
        bytes.fromhex("8945dc"),                   # opaque entry token
        bytes.fromhex("837de8007403fdeb01fc8b4df0"),
        bytes.fromhex("8945d8"),                   # native original EAX
        bytes.fromhex("9c5825000400008945d4fc"), # post-original DF
        bytes.fromhex("83e4f0ff75ecff75dce8"),   # post(token, caller), no self
        bytes.fromhex("837dd4007403fdeb01fc8b45d8"),
    )
    if any(fragment not in v8_wrapper for fragment in v8_required_fragments):
        fail("S5 v8 menu wrapper entry/original/no-self-post ABI missing")
    if v8_wrapper.count(modal_original_call) != 1 \
            or v8_wrapper.count(bytes.fromhex("0fae00")) != 2 \
            or v8_wrapper.count(bytes.fromhex("0fae08")) != 2 \
            or bytes.fromhex("c20400") in v8_wrapper \
            or not v8_wrapper.endswith(
                bytes.fromhex("837dd4007403fdeb01fc8b45d8") + v8_tail):
        fail("S5 v8 menu wrapper original/FX/plain-ret contract changed")
    v8_after_original = v8_wrapper[
        v8_original_relative + len(modal_original_call):]
    if bytes.fromhex("8b4df0") in v8_after_original \
            or bytes.fromhex("ff75f0") in v8_after_original:
        fail("S5 v8 wrapper reads/forwards menu self after native original")
    forbidden_calls = [
        bytes.fromhex("b8d0b54800ffd0"),  # direct Commerce Apply
        bytes.fromhex("b8908d4800ffd0"),  # direct Cultivate Apply
        bytes.fromhex("b890864800ffd0"),  # direct Patrol Apply
        bytes.fromhex("b8f09f4800ffd0"),  # direct Train Apply
        bytes.fromhex("b810934800ffd0"),  # direct Repair Apply
        bytes.fromhex("b8b0ea5000ffd0"),  # handler driver
        bytes.fromhex("b810e54700ffd0"),  # validator
        bytes.fromhex("b8f0e64700ffd0"),  # command attach
    ]
    if not apply_once:
        forbidden_calls.append(bytes.fromhex("b840b34800ffd0"))
    for forbidden_call in forbidden_calls:
        if forbidden_call in executable:
            fail("DLL contains a forbidden native call")
    if apply_once:
        required_apply_calls = {
            "list-ctor": bytes.fromhex("b8f00d4700ffd0"),
            "list-append": bytes.fromhex("b870ef4600ffd0"),
            "list-dtor": bytes.fromhex("b8100e4700ffd0"),
            "allocator": bytes.fromhex("b820ec5d00ffd0"),
            "raw-free": bytes.fromhex("b870ec5d00ffd0"),
            "command-ctor": bytes.fromhex(
                "b880904800ffd0" if repair else
                "b8809d4800ffd0" if train else
                "b810844800ffd0" if patrol else
                "b8008b4800ffd0" if cultivate else "b840b34800ffd0"),
        }
        for name, sequence in required_apply_calls.items():
            if executable.count(sequence) != 1:
                fail(f"APPLY_ONCE {name} shim count is not exactly one")
    required = {
        "slot": struct.pack("<I", 0x00604DF4),
        "original": struct.pack("<I", 0x00434100),
        "caller": struct.pack("<I", 0x005C5D11),
        "app": struct.pack("<I", 0x01228340),
        "app-vtable": struct.pack("<I", 0x00604DD0),
        "manifest": b"72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE",
        "easy-redirect26": struct.pack("<I", 0x001CAE0E),
        "easy-redirect26-target": struct.pack("<I", 0x00001580),
        "modal-top-hwnd": struct.pack("<I", 0x005CA7D0),
        "modal-permanent-cwnd": struct.pack("<I", 0x005CD460),
        "modal-menu-vptr": struct.pack("<I", 0x0060A238),
        "modal-menu-tick-slot": struct.pack("<I", 0x0060A2DC),
        "modal-menu-original": struct.pack("<I", 0x004175C0),
        "modal-loop-caller": struct.pack("<I", 0x005B67C3),
        "lock-cmpxchg": bytes.fromhex("f00fb1"),
    }
    for name, marker in required.items():
        if marker not in raw:
            fail(f"DLL machine/data marker missing: {name}")
    for commerce_address in (
        0x004C61F0, 0x004C6400, 0x00470DF0, 0x00470E10, 0x0046EF80,
        0x005DEC20, 0x0048B340, 0x0047E510, 0x0047E6F0,
    ):
        # Commerce candidates are compile-time placeholders only and must not
        # become executable immediates in the ping-only DLL.
        if struct.pack("<I", commerce_address) in image.get_data(
                exported["San9BridgeP1M2b_IdleBridge"], 112):
            fail("Commerce candidate leaked into idle wrapper")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--offline", type=Path, required=True)
    parser.add_argument("--controller", type=Path, required=True)
    parser.add_argument("--dll", type=Path, required=True)
    parser.add_argument("--apply-dll", type=Path)
    parser.add_argument("--cultivate-dll", type=Path)
    parser.add_argument("--patrol-dll", type=Path)
    parser.add_argument("--train-dll", type=Path)
    parser.add_argument("--repair-dll", type=Path)
    parser.add_argument("--offline-sha")
    parser.add_argument("--controller-sha")
    parser.add_argument("--dll-sha")
    parser.add_argument("--apply-dll-sha")
    parser.add_argument("--cultivate-dll-sha")
    parser.add_argument("--patrol-dll-sha")
    parser.add_argument("--train-dll-sha")
    parser.add_argument("--repair-dll-sha")
    args = parser.parse_args()
    if pefile.__version__ != EXPECTED_PEFILE_VERSION:
        fail(f"pefile version mismatch: {pefile.__version__}")
    audit_offline(args.offline, args.offline_sha)
    audit_controller(args.controller, args.controller_sha)
    audit_dll(args.dll, args.dll_sha)
    if (args.apply_dll is None) != (args.apply_dll_sha is None):
        fail("apply DLL path/hash must be supplied together")
    if args.apply_dll is not None:
        audit_dll(args.apply_dll, args.apply_dll_sha, True)
    if (args.cultivate_dll is None) != (args.cultivate_dll_sha is None):
        fail("Cultivate DLL path/hash must be supplied together")
    if args.cultivate_dll is not None:
        audit_dll(args.cultivate_dll, args.cultivate_dll_sha, True, True)
    if (args.patrol_dll is None) != (args.patrol_dll_sha is None):
        fail("Patrol DLL path/hash must be supplied together")
    if args.patrol_dll is not None:
        audit_dll(args.patrol_dll, args.patrol_dll_sha, True, False, True)
    if (args.train_dll is None) != (args.train_dll_sha is None):
        fail("Train DLL path/hash must be supplied together")
    if args.train_dll is not None:
        audit_dll(args.train_dll, args.train_dll_sha,
                  True, False, False, True)
    if (args.repair_dll is None) != (args.repair_dll_sha is None):
        fail("Repair DLL path/hash must be supplied together")
    if args.repair_dll is not None:
        audit_dll(args.repair_dll, args.repair_dll_sha,
                  True, False, False, False, True)
    print(
        "P1_M2B_PE_AUDIT PASS x86=1 live_loaded=0 current_user_acl=1 "
        "wh_getmessage=1 easy_manifest=1 slot_cas=1 idle_original_once=1 "
        "p1wire_m2a=1 inspect_process_access=read_only probe0_counter=1 "
        "s3_readonly_build=1 readonly_dispatch=17 write_dispatch=0 max_read=256 "
        "commerce_abi_ready=1 commerce_live_authorization=0"
        " s5_no_apply=v8 menu_original_once=1 menu_post_self_reads=0"
        " root_event_once=1 handler_execute_once=1 apply=0"
        " terminal_cas=1 signed_deadline=1 poison_monotonic=1"
        " s5_apply_once=isolated command_ctor=1 direct_apply_call=0"
        " s6_cultivate_apply_once=isolated command_ctor=1 direct_apply_call=0"
        " s6_patrol_apply_once=isolated command_ctor=1 direct_apply_call=0"
        " s6_train_apply_once=isolated command_ctor=1 direct_apply_call=0"
        " s6_repair_apply_once=isolated command_ctor=1 direct_apply_call=0"
        " s5_modal_probe=vtable_v7 modal_original_once=1 modal_restore_cas=1"
        " modal_wake_authorization=0"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except AuditFailure as error:
        print(f"P1_M2B_PE_AUDIT FAIL: {error}", file=sys.stderr)
        raise SystemExit(1)
