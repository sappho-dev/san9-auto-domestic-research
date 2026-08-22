#!/usr/bin/env python3
"""Fail-closed, read-only San9PK selection-lifecycle evidence probe.

The default mode reads only the exact on-disk executable.  Live observation is
impossible unless the analyst supplies ``--pid`` explicitly.  Live mode opens
one existing process with PROCESS_QUERY_INFORMATION | PROCESS_VM_READ and uses
only VirtualQueryEx/ReadProcessMemory for target memory.  It never writes a
file, game memory, input, or code and never calls a game function.
"""

from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import ntpath
import os
import struct
import sys
import time
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence, Tuple


TARGET_EXE = Path(r"D:\三国志9\10101749\San9PK.exe")
TARGET_SHA256 = "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028"
TARGET_FILE_SIZE = 2_636_800
TARGET_VERSION = (1, 0, 1, 0)
IMAGE_BASE = 0x00400000
SIZE_OF_IMAGE = 0x01759000
IMAGE_END = IMAGE_BASE + SIZE_OF_IMAGE
MACHINE_I386 = 0x014C
PE32_MAGIC = 0x010B

PERSON_BASE = 0x01258EE0
PERSON_STRIDE = 0x0128
PERSON_COUNT = 850
PERSON_TABLE_SIZE = PERSON_STRIDE * PERSON_COUNT
PERSON_ID_OFFSET = 0x04
PERSON_MIGHT_OFFSET = 0x50
PERSON_INTELLIGENCE_OFFSET = 0x58
PERSON_POLITICS_OFFSET = 0x60
PERSON_LEADERSHIP_OFFSET = 0x68
PERSON_IDENTITY_OFFSET = 0x84
PERSON_READY_FLAGS_OFFSET = 0xE8
PERSON_RESIDENCE_OFFSET = 0xF4
PERSON_BUSY_BIT = 0x00001000

CITY_BASE = 0x0124DB58
CITY_STRIDE = 0x01F0
CITY_COUNT = 50
CITY_VPTR = 0x00605938
CITY_TYPE_OFFSET = 0x06
CITY_TYPE = 5
CITY_RESIDENT_LIST_OFFSET = 0xDC

GLOBAL_SELECTED_LIST = 0x015455AC
PERSON_LIST_VPTR = 0x00606C8C
LIST_HEADER_SIZE = 0x10
LIST_NODE_SIZE = 0x0C
MAX_SELECTION = 5

TASK_LISTS = {
    "committed": 0x6AC,
    "source": 0x6CC,
    "working": 0x6EC,
}
TASK_READ_SIZE = 0x700
UI_TASKS = {
    0x0060B920: ("patrol", PERSON_INTELLIGENCE_OFFSET),
    0x0060CCB0: ("commerce", PERSON_POLITICS_OFFSET),
    0x0060BBA0: ("cultivate", PERSON_POLITICS_OFFSET),
    0x0060BCE8: ("repair", PERSON_LEADERSHIP_OFFSET),
    0x0060C370: ("train", PERSON_MIGHT_OFFSET),
}

PROCESS_VM_READ = 0x0010
PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_ACCESS = PROCESS_QUERY_INFORMATION | PROCESS_VM_READ
MEM_COMMIT = 0x1000
MEM_PRIVATE = 0x20000
PAGE_NOACCESS = 0x01
PAGE_READONLY = 0x02
PAGE_READWRITE = 0x04
PAGE_WRITECOPY = 0x08
PAGE_EXECUTE_READ = 0x20
PAGE_EXECUTE_READWRITE = 0x40
PAGE_EXECUTE_WRITECOPY = 0x80
PAGE_GUARD = 0x100
READABLE_PROTECTIONS = {
    PAGE_READONLY,
    PAGE_READWRITE,
    PAGE_WRITECOPY,
    PAGE_EXECUTE_READ,
    PAGE_EXECUTE_READWRITE,
    PAGE_EXECUTE_WRITECOPY,
}
WRITABLE_SCAN_PROTECTIONS = {
    PAGE_READWRITE,
    PAGE_WRITECOPY,
    PAGE_EXECUTE_READWRITE,
    PAGE_EXECUTE_WRITECOPY,
}
MIN_USER_ADDRESS = 0x00010000
MAX_USER_ADDRESS_EXCLUSIVE = 0x80000000
SCAN_CHUNK = 1024 * 1024
MAX_SCAN_BYTES = 768 * 1024 * 1024
MAX_QUERY_REGIONS = 200_000
MAX_RAW_CANDIDATES = 32

TH32CS_SNAPPROCESS = 0x00000002
TH32CS_SNAPMODULE = 0x00000008
TH32CS_SNAPMODULE32 = 0x00000010
ERROR_BAD_LENGTH = 24
ERROR_NO_MORE_FILES = 18
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value
MAX_PATH = 260
MAX_MODULE_NAME32 = 255
KNOWN_CONFLICT_PROCESSES = {"san9pkeasy", "san9pkhard", "sanixpkcheat"}
KNOWN_CONFLICT_MODULES = {"easy.dll", "sanixspy.dll", "san9common.dll"}
LOCAL_PROXY_MODULES = {"version.dll", "dinput.dll", "dinput8.dll", "winmm.dll", "dsound.dll"}


class ProbeError(RuntimeError):
    def __init__(self, code: str, message: str, **details: Any) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.details = details

    def report(self) -> Dict[str, Any]:
        return {
            "ok": False,
            "code": self.code,
            "message": self.message,
            "details": self.details,
            "execution_authorized": False,
            "process_write_capability": False,
        }


def _u16(data: bytes, offset: int) -> int:
    return struct.unpack_from("<H", data, offset)[0]


def _u32(data: bytes, offset: int) -> int:
    return struct.unpack_from("<I", data, offset)[0]


def _hex(value: int) -> str:
    return "0x%08X" % value


def _canonical_path(value: str) -> str:
    value = value.replace("/", "\\")
    if value.startswith("\\\\?\\"):
        value = value[4:]
    return ntpath.normcase(ntpath.normpath(value))


def _checked_range(address: int, size: int) -> Tuple[int, int]:
    if size <= 0 or address < MIN_USER_ADDRESS:
        raise ProbeError("READ_RANGE_INVALID", "Read range is empty or below the x86 user floor.", address=_hex(address), size=size)
    end = address + size
    if end <= address or end > MAX_USER_ADDRESS_EXCLUSIVE:
        raise ProbeError("READ_RANGE_INVALID", "Read range exceeds the 32-bit user address space.", address=_hex(address), size=size)
    return address, end


def _exact_table_index(pointer: int, base: int, stride: int, count: int) -> Optional[int]:
    delta = pointer - base
    if delta < 0 or delta >= stride * count or delta % stride:
        return None
    return delta // stride


def _parse_pe(data: bytes) -> Dict[str, int]:
    if len(data) < 0x100 or data[:2] != b"MZ":
        raise ProbeError("PE_DOS_HEADER_INVALID", "Target does not contain a valid DOS header.")
    pe_offset = _u32(data, 0x3C)
    if pe_offset < 0x40 or pe_offset + 0x60 > len(data) or data[pe_offset:pe_offset + 4] != b"PE\0\0":
        raise ProbeError("PE_HEADER_INVALID", "Target does not contain a bounded PE header.", pe_offset=pe_offset)
    optional = pe_offset + 24
    return {
        "machine": _u16(data, pe_offset + 4),
        "optional_magic": _u16(data, optional),
        "image_base": _u32(data, optional + 28),
        "size_of_image": _u32(data, optional + 56),
    }


def verify_disk_target() -> Dict[str, Any]:
    expected = _canonical_path(str(TARGET_EXE))
    resolved = _canonical_path(os.path.realpath(str(TARGET_EXE)))
    if resolved != expected:
        raise ProbeError("TARGET_PATH_REDIRECTED", "The fixed executable path resolves elsewhere.", expected=str(TARGET_EXE), resolved=os.path.realpath(str(TARGET_EXE)))
    try:
        data = TARGET_EXE.read_bytes()
    except OSError as error:
        raise ProbeError("TARGET_READ_FAILED", "Cannot read the fixed executable.", path=str(TARGET_EXE), error=str(error)) from error
    if len(data) != TARGET_FILE_SIZE:
        raise ProbeError("TARGET_SIZE_MISMATCH", "Executable size does not match the exact target.", actual=len(data), expected=TARGET_FILE_SIZE)
    digest = hashlib.sha256(data).hexdigest()
    if digest != TARGET_SHA256:
        raise ProbeError("TARGET_SHA256_MISMATCH", "Executable SHA-256 does not match; no version-specific address was inspected.", actual=digest.upper(), expected=TARGET_SHA256.upper())
    pe = _parse_pe(data)
    expected_pe = {
        "machine": MACHINE_I386,
        "optional_magic": PE32_MAGIC,
        "image_base": IMAGE_BASE,
        "size_of_image": SIZE_OF_IMAGE,
    }
    if pe != expected_pe:
        raise ProbeError("TARGET_PE_IDENTITY_MISMATCH", "PE identity conflicts with the hash-locked target.", actual=pe, expected=expected_pe)
    return {
        "path": str(TARGET_EXE),
        "resolved_path": os.path.realpath(str(TARGET_EXE)),
        "size": len(data),
        "sha256": digest.upper(),
        "version_locked_by_exact_hash": ".".join(str(part) for part in TARGET_VERSION),
        "machine": "i386",
        "image_base": _hex(pe["image_base"]),
        "size_of_image": _hex(pe["size_of_image"]),
    }


class _FileTime(ctypes.Structure):
    _fields_ = [("low", ctypes.c_uint32), ("high", ctypes.c_uint32)]


class _MemoryBasicInformation(ctypes.Structure):
    _fields_ = [
        ("BaseAddress", ctypes.c_void_p),
        ("AllocationBase", ctypes.c_void_p),
        ("AllocationProtect", ctypes.c_uint32),
        ("PartitionId", ctypes.c_uint16),
        ("RegionSize", ctypes.c_size_t),
        ("State", ctypes.c_uint32),
        ("Protect", ctypes.c_uint32),
        ("Type", ctypes.c_uint32),
    ]


class _ProcessEntry32W(ctypes.Structure):
    _fields_ = [
        ("dwSize", ctypes.c_uint32),
        ("cntUsage", ctypes.c_uint32),
        ("th32ProcessID", ctypes.c_uint32),
        ("th32DefaultHeapID", ctypes.c_size_t),
        ("th32ModuleID", ctypes.c_uint32),
        ("cntThreads", ctypes.c_uint32),
        ("th32ParentProcessID", ctypes.c_uint32),
        ("pcPriClassBase", ctypes.c_int32),
        ("dwFlags", ctypes.c_uint32),
        ("szExeFile", ctypes.c_wchar * MAX_PATH),
    ]


class _ModuleEntry32W(ctypes.Structure):
    _fields_ = [
        ("dwSize", ctypes.c_uint32),
        ("th32ModuleID", ctypes.c_uint32),
        ("th32ProcessID", ctypes.c_uint32),
        ("GlblcntUsage", ctypes.c_uint32),
        ("ProccntUsage", ctypes.c_uint32),
        ("modBaseAddr", ctypes.c_void_p),
        ("modBaseSize", ctypes.c_uint32),
        ("hModule", ctypes.c_void_p),
        ("szModule", ctypes.c_wchar * (MAX_MODULE_NAME32 + 1)),
        ("szExePath", ctypes.c_wchar * MAX_PATH),
    ]


def evaluate_conflict_inventory(processes: Sequence[Dict[str, Any]], modules: Sequence[Dict[str, Any]]) -> Dict[str, Any]:
    """Pure policy gate shared by live Toolhelp scans and synthetic tests."""
    process_conflicts = []
    for process in processes:
        base_name = ntpath.splitext(ntpath.basename(str(process.get("name", ""))))[0].casefold()
        if base_name in KNOWN_CONFLICT_PROCESSES:
            process_conflicts.append({"name": process.get("name"), "pid": process.get("pid")})
    module_conflicts = []
    target_directory = _canonical_path(ntpath.dirname(str(TARGET_EXE)))
    main_modules = []
    canonical_modules = []
    for module in modules:
        name = ntpath.basename(str(module.get("name") or module.get("path") or ""))
        path = str(module.get("path") or "")
        base = int(module.get("base", 0))
        size = int(module.get("size", 0))
        if not name or not path or base <= 0 or size <= 0:
            raise ProbeError("CONFLICT_MODULE_ENTRY_INCOMPLETE", "Toolhelp returned an incomplete module entry.", module=module)
        folded = name.casefold()
        if folded in KNOWN_CONFLICT_MODULES:
            module_conflicts.append({"kind": "known_module", "name": name, "path": path})
        if folded in LOCAL_PROXY_MODULES and _canonical_path(ntpath.dirname(path)) == target_directory:
            module_conflicts.append({"kind": "local_proxy", "name": name, "path": path})
        if folded == "san9pk.exe":
            main_modules.append({"name": name, "path": path, "base": base, "size": size})
        canonical_modules.append((_canonical_path(path), folded, base, size))
    if process_conflicts or module_conflicts:
        raise ProbeError("KNOWN_CONFLICT_PRESENT", "A known modifier process/module or game-directory proxy is present.", processes=process_conflicts, modules=module_conflicts)
    if len(main_modules) != 1:
        raise ProbeError("MAIN_MODULE_ENUMERATION_MISMATCH", "Module scan did not find exactly one San9PK.exe main module.", matches=main_modules)
    main = main_modules[0]
    if (_canonical_path(main["path"]) != _canonical_path(str(TARGET_EXE)) or main["base"] != IMAGE_BASE or main["size"] != SIZE_OF_IMAGE):
        raise ProbeError("MAIN_MODULE_IDENTITY_MISMATCH", "Toolhelp main-module identity does not match the exact target.", actual=main, expected={"path": str(TARGET_EXE), "base": IMAGE_BASE, "size": SIZE_OF_IMAGE})
    module_bytes = json.dumps(sorted(canonical_modules), ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    return {
        "clear": True,
        "known_process_scan_succeeded": True,
        "target_module_scan_succeeded": True,
        "known_process_matches": 0,
        "blocking_module_matches": 0,
        "inspected_module_count": len(modules),
        "module_inventory_sha256": hashlib.sha256(module_bytes).hexdigest().upper(),
        "main_module": {"path": main["path"], "base": _hex(main["base"]), "size": _hex(main["size"])},
    }


class ProcessReader:
    """One generation-bound read-only process handle."""

    def __init__(self, pid: int) -> None:
        if os.name != "nt":
            raise ProbeError("WINDOWS_REQUIRED", "Live mode is supported only on Windows.")
        from ctypes import wintypes

        self.pid = pid
        self._wintypes = wintypes
        self._kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        k32 = self._kernel32
        k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        k32.OpenProcess.restype = wintypes.HANDLE
        k32.CloseHandle.argtypes = [wintypes.HANDLE]
        k32.CloseHandle.restype = wintypes.BOOL
        k32.ReadProcessMemory.argtypes = [wintypes.HANDLE, wintypes.LPCVOID, wintypes.LPVOID, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
        k32.ReadProcessMemory.restype = wintypes.BOOL
        k32.VirtualQueryEx.argtypes = [wintypes.HANDLE, wintypes.LPCVOID, ctypes.POINTER(_MemoryBasicInformation), ctypes.c_size_t]
        k32.VirtualQueryEx.restype = ctypes.c_size_t
        k32.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
        k32.QueryFullProcessImageNameW.restype = wintypes.BOOL
        k32.GetProcessTimes.argtypes = [wintypes.HANDLE, ctypes.POINTER(_FileTime), ctypes.POINTER(_FileTime), ctypes.POINTER(_FileTime), ctypes.POINTER(_FileTime)]
        k32.GetProcessTimes.restype = wintypes.BOOL
        k32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
        k32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
        k32.Process32FirstW.argtypes = [wintypes.HANDLE, ctypes.POINTER(_ProcessEntry32W)]
        k32.Process32FirstW.restype = wintypes.BOOL
        k32.Process32NextW.argtypes = [wintypes.HANDLE, ctypes.POINTER(_ProcessEntry32W)]
        k32.Process32NextW.restype = wintypes.BOOL
        k32.Module32FirstW.argtypes = [wintypes.HANDLE, ctypes.POINTER(_ModuleEntry32W)]
        k32.Module32FirstW.restype = wintypes.BOOL
        k32.Module32NextW.argtypes = [wintypes.HANDLE, ctypes.POINTER(_ModuleEntry32W)]
        k32.Module32NextW.restype = wintypes.BOOL
        self.handle = k32.OpenProcess(PROCESS_ACCESS, False, pid)
        if not self.handle:
            raise ProbeError("OPEN_PROCESS_FAILED", "OpenProcess QUERY|VM_READ failed.", pid=pid, winerror=ctypes.get_last_error(), requested_access=_hex(PROCESS_ACCESS))

    def close(self) -> None:
        if self.handle:
            self._kernel32.CloseHandle(self.handle)
            self.handle = None

    def __enter__(self) -> "ProcessReader":
        return self

    def __exit__(self, exc_type: Any, exc: Any, traceback: Any) -> None:
        self.close()

    def identity(self) -> Dict[str, Any]:
        capacity = self._wintypes.DWORD(32768)
        buffer = ctypes.create_unicode_buffer(capacity.value)
        if not self._kernel32.QueryFullProcessImageNameW(self.handle, 0, buffer, ctypes.byref(capacity)):
            raise ProbeError("PROCESS_PATH_QUERY_FAILED", "QueryFullProcessImageNameW failed.", winerror=ctypes.get_last_error())
        creation, exit_time, kernel, user = _FileTime(), _FileTime(), _FileTime(), _FileTime()
        if not self._kernel32.GetProcessTimes(self.handle, ctypes.byref(creation), ctypes.byref(exit_time), ctypes.byref(kernel), ctypes.byref(user)):
            raise ProbeError("PROCESS_TIME_QUERY_FAILED", "GetProcessTimes failed.", winerror=ctypes.get_last_error())
        path = buffer.value
        if _canonical_path(path) != _canonical_path(str(TARGET_EXE)):
            raise ProbeError("PROCESS_PATH_MISMATCH", "PID image path is not the exact target path.", actual=path, expected=str(TARGET_EXE))
        return {
            "pid": self.pid,
            "creation_time_100ns": (creation.high << 32) | creation.low,
            "image_path": path,
        }

    def query(self, address: int) -> Dict[str, int]:
        mbi = _MemoryBasicInformation()
        returned = self._kernel32.VirtualQueryEx(self.handle, ctypes.c_void_p(address), ctypes.byref(mbi), ctypes.sizeof(mbi))
        if returned == 0:
            raise ProbeError("VIRTUAL_QUERY_FAILED", "VirtualQueryEx failed.", address=_hex(address), winerror=ctypes.get_last_error())
        base = int(mbi.BaseAddress or 0)
        size = int(mbi.RegionSize)
        if size <= 0 or base > address or base + size <= address:
            raise ProbeError("VIRTUAL_QUERY_INVALID", "VirtualQueryEx returned a non-progressing region.", address=_hex(address), base=_hex(base), size=size)
        return {
            "base": base,
            "size": size,
            "state": int(mbi.State),
            "protect": int(mbi.Protect),
            "type": int(mbi.Type),
        }

    @staticmethod
    def _is_readable(region: Dict[str, int]) -> bool:
        protect = region["protect"]
        return region["state"] == MEM_COMMIT and not (protect & PAGE_GUARD) and (protect & 0xFF) in READABLE_PROTECTIONS

    def _validate_read_pages(self, address: int, size: int) -> None:
        _, end = _checked_range(address, size)
        cursor = address
        while cursor < end:
            region = self.query(cursor)
            if not self._is_readable(region):
                raise ProbeError("READ_PAGE_NOT_READABLE", "Range crosses a non-readable page.", address=_hex(cursor), state=_hex(region["state"]), protect=_hex(region["protect"]))
            region_end = min(region["base"] + region["size"], MAX_USER_ADDRESS_EXCLUSIVE)
            if region_end <= cursor:
                raise ProbeError("VIRTUAL_QUERY_INVALID", "VirtualQueryEx did not advance during read validation.", address=_hex(cursor))
            cursor = min(region_end, end)

    def read(self, address: int, size: int) -> bytes:
        self._validate_read_pages(address, size)
        buffer = ctypes.create_string_buffer(size)
        transferred = ctypes.c_size_t()
        ok = self._kernel32.ReadProcessMemory(self.handle, ctypes.c_void_p(address), buffer, size, ctypes.byref(transferred))
        if not ok or transferred.value != size:
            raise ProbeError("READ_PROCESS_MEMORY_FAILED", "ReadProcessMemory did not return the exact requested bytes.", address=_hex(address), requested=size, received=int(transferred.value), winerror=ctypes.get_last_error())
        return buffer.raw

    def validate_live_pe(self) -> Dict[str, int]:
        pe = _parse_pe(self.read(IMAGE_BASE, 0x1000))
        expected = {"machine": MACHINE_I386, "optional_magic": PE32_MAGIC, "image_base": IMAGE_BASE, "size_of_image": SIZE_OF_IMAGE}
        if pe != expected:
            raise ProbeError("LIVE_PE_IDENTITY_MISMATCH", "Loaded image PE identity does not match the exact target.", actual=pe, expected=expected)
        return pe

    def _toolhelp_snapshot(self, flags: int, pid: int, name: str) -> Any:
        last_error = 0
        for _ in range(3):
            ctypes.set_last_error(0)
            handle = self._kernel32.CreateToolhelp32Snapshot(flags, pid)
            if handle not in (None, 0, INVALID_HANDLE_VALUE):
                return handle
            last_error = ctypes.get_last_error()
            if last_error != ERROR_BAD_LENGTH:
                break
        raise ProbeError("CONFLICT_SCAN_SNAPSHOT_FAILED", "Toolhelp conflict-scan snapshot failed.", scan=name, winerror=last_error)

    def _process_inventory(self) -> List[Dict[str, Any]]:
        snapshot = self._toolhelp_snapshot(TH32CS_SNAPPROCESS, 0, "known_processes")
        try:
            entry = _ProcessEntry32W()
            entry.dwSize = ctypes.sizeof(entry)
            ctypes.set_last_error(0)
            if not self._kernel32.Process32FirstW(snapshot, ctypes.byref(entry)):
                raise ProbeError("CONFLICT_PROCESS_SCAN_FAILED", "Process32FirstW failed.", winerror=ctypes.get_last_error())
            rows: List[Dict[str, Any]] = []
            while True:
                if not entry.szExeFile:
                    raise ProbeError("CONFLICT_PROCESS_ENTRY_INCOMPLETE", "Toolhelp returned a process without an executable name.", pid=int(entry.th32ProcessID))
                rows.append({"pid": int(entry.th32ProcessID), "name": entry.szExeFile})
                entry.dwSize = ctypes.sizeof(entry)
                ctypes.set_last_error(0)
                if not self._kernel32.Process32NextW(snapshot, ctypes.byref(entry)):
                    error = ctypes.get_last_error()
                    if error != ERROR_NO_MORE_FILES:
                        raise ProbeError("CONFLICT_PROCESS_SCAN_FAILED", "Process32NextW ended unexpectedly.", winerror=error)
                    return rows
        finally:
            self._kernel32.CloseHandle(snapshot)

    def _module_inventory(self) -> List[Dict[str, Any]]:
        snapshot = self._toolhelp_snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, self.pid, "target_modules")
        try:
            entry = _ModuleEntry32W()
            entry.dwSize = ctypes.sizeof(entry)
            ctypes.set_last_error(0)
            if not self._kernel32.Module32FirstW(snapshot, ctypes.byref(entry)):
                raise ProbeError("CONFLICT_MODULE_SCAN_FAILED", "Module32FirstW failed.", winerror=ctypes.get_last_error())
            rows: List[Dict[str, Any]] = []
            while True:
                rows.append({
                    "name": entry.szModule,
                    "path": entry.szExePath,
                    "base": int(entry.modBaseAddr or 0),
                    "size": int(entry.modBaseSize),
                })
                entry.dwSize = ctypes.sizeof(entry)
                ctypes.set_last_error(0)
                if not self._kernel32.Module32NextW(snapshot, ctypes.byref(entry)):
                    error = ctypes.get_last_error()
                    if error != ERROR_NO_MORE_FILES:
                        raise ProbeError("CONFLICT_MODULE_SCAN_FAILED", "Module32NextW ended unexpectedly.", winerror=error)
                    return rows
        finally:
            self._kernel32.CloseHandle(snapshot)

    def scan_conflicts(self) -> Dict[str, Any]:
        return evaluate_conflict_inventory(self._process_inventory(), self._module_inventory())


class PersonTable:
    def __init__(self, raw: bytes) -> None:
        if len(raw) != PERSON_TABLE_SIZE:
            raise ProbeError("PERSON_TABLE_SIZE", "PERSON table capture has the wrong size.", actual=len(raw), expected=PERSON_TABLE_SIZE)
        self.raw = raw
        canonical = bytearray()
        for person_id in range(PERSON_COUNT):
            record = person_id * PERSON_STRIDE
            embedded = _u16(raw, record + PERSON_ID_OFFSET)
            if embedded != person_id:
                raise ProbeError("PERSON_RECORD_ID_MISMATCH", "PERSON record ID does not match its exact table slot.", slot=person_id, embedded=embedded)
            canonical.extend(struct.pack(
                "<H7I",
                embedded,
                _u32(raw, record + PERSON_MIGHT_OFFSET),
                _u32(raw, record + PERSON_INTELLIGENCE_OFFSET),
                _u32(raw, record + PERSON_POLITICS_OFFSET),
                _u32(raw, record + PERSON_LEADERSHIP_OFFSET),
                _u32(raw, record + PERSON_IDENTITY_OFFSET),
                _u32(raw, record + PERSON_READY_FLAGS_OFFSET),
                _u32(raw, record + PERSON_RESIDENCE_OFFSET),
            ))
        self.canonical_sha256 = hashlib.sha256(canonical).hexdigest().upper()

    def record(self, person_id: int) -> Dict[str, int]:
        if person_id < 0 or person_id >= PERSON_COUNT:
            raise ProbeError("PERSON_ID_RANGE", "Person ID is outside the fixed table.", person_id=person_id)
        offset = person_id * PERSON_STRIDE
        return {
            "id": person_id,
            "pointer": PERSON_BASE + offset,
            "might": _u32(self.raw, offset + PERSON_MIGHT_OFFSET),
            "intelligence": _u32(self.raw, offset + PERSON_INTELLIGENCE_OFFSET),
            "politics": _u32(self.raw, offset + PERSON_POLITICS_OFFSET),
            "leadership": _u32(self.raw, offset + PERSON_LEADERSHIP_OFFSET),
            "identity": _u32(self.raw, offset + PERSON_IDENTITY_OFFSET),
            "ready_flags": _u32(self.raw, offset + PERSON_READY_FLAGS_OFFSET),
            "residence": _u32(self.raw, offset + PERSON_RESIDENCE_OFFSET),
        }


def _validate_list_type(type_tag: int, expected: Optional[int], name: str) -> None:
    if type_tag != PERSON_LIST_VPTR:
        raise ProbeError("LIST_TYPE_INVALID", "List vptr is not the exact version-locked person-list type.", list=name, actual=_hex(type_tag), expected=_hex(PERSON_LIST_VPTR))
    if expected is not None and type_tag != expected:
        raise ProbeError("LIST_TYPE_MISMATCH", "Person-list objects do not share the exact type/vptr.", list=name, actual=_hex(type_tag), expected=_hex(expected))


def parse_person_list(memory: Any, header_address: int, name: str, persons: PersonTable, maximum: int, expected_type: Optional[int] = None, header_bytes: Optional[bytes] = None) -> Dict[str, Any]:
    header = header_bytes if header_bytes is not None else memory.read(header_address, LIST_HEADER_SIZE)
    if len(header) != LIST_HEADER_SIZE:
        raise ProbeError("LIST_HEADER_SHORT", "List header capture is not exact.", list=name)
    type_tag, first, last, count = struct.unpack("<IIII", header)
    _validate_list_type(type_tag, expected_type, name)
    if count > maximum:
        raise ProbeError("LIST_COUNT_LIMIT", "List count exceeds its proven bound.", list=name, count=count, maximum=maximum)
    if count == 0:
        if first or last:
            raise ProbeError("EMPTY_LIST_POINTERS", "An empty list has non-null endpoints.", list=name, first=_hex(first), last=_hex(last))
        return {"header": _hex(header_address), "type": _hex(type_tag), "first": _hex(first), "last": _hex(last), "count": 0, "nodes": [], "person_ids": []}
    if first == 0 or last == 0:
        raise ProbeError("NONEMPTY_LIST_NULL_ENDPOINT", "A non-empty list has a null endpoint.", list=name)
    nodes: List[str] = []
    ids: List[int] = []
    seen_nodes: set[int] = set()
    seen_people: set[int] = set()
    current = first
    previous = 0
    for index in range(count):
        _checked_range(current, LIST_NODE_SIZE)
        if current % 4 or current in seen_nodes:
            raise ProbeError("LIST_NODE_CYCLE", "List node is unaligned, repeated, or cyclic.", list=name, node=_hex(current), index=index)
        seen_nodes.add(current)
        raw = memory.read(current, LIST_NODE_SIZE)
        next_node, previous_node, person_pointer = struct.unpack("<III", raw)
        if previous_node != previous:
            raise ProbeError("LIST_PREVIOUS_MISMATCH", "List backward link does not match traversal.", list=name, node=_hex(current), actual=_hex(previous_node), expected=_hex(previous))
        person_id = _exact_table_index(person_pointer, PERSON_BASE, PERSON_STRIDE, PERSON_COUNT)
        if person_id is None:
            raise ProbeError("LIST_PERSON_POINTER_INVALID", "List payload is not an exact PERSON record pointer.", list=name, node=_hex(current), pointer=_hex(person_pointer))
        if person_id in seen_people:
            raise ProbeError("LIST_PERSON_DUPLICATE", "A person appears twice in one list.", list=name, person_id=person_id)
        if _u16(persons.raw, person_id * PERSON_STRIDE + PERSON_ID_OFFSET) != person_id:
            raise ProbeError("LIST_PERSON_ID_CONFLICT", "List pointer and embedded PERSON ID conflict.", list=name, person_id=person_id)
        seen_people.add(person_id)
        nodes.append(_hex(current))
        ids.append(person_id)
        previous = current
        current = next_node
    if current != 0:
        raise ProbeError("LIST_NOT_TERMINATED", "List did not terminate exactly at its declared count.", list=name, next_after_count=_hex(current))
    if previous != last:
        raise ProbeError("LIST_LAST_MISMATCH", "List tail does not match its declared last node.", list=name, actual=_hex(previous), expected=_hex(last))
    return {"header": _hex(header_address), "type": _hex(type_tag), "first": _hex(first), "last": _hex(last), "count": count, "nodes": nodes, "person_ids": ids}


def _source_origin(memory: Any, source: Dict[str, Any], persons: PersonTable, list_type: int, ability_name: str) -> Dict[str, Any]:
    source_ids = source["person_ids"]
    if not source_ids:
        raise ProbeError("SOURCE_EMPTY", "A surviving UI selection task has no source candidates; treating it as stale/transient is safer.")
    owner_city: Optional[int] = None
    containers: Dict[int, Tuple[int, int]] = {}
    for person_id in source_ids:
        person = persons.record(person_id)
        if person["identity"] > 3:
            raise ProbeError("SOURCE_PERSON_IDENTITY", "Source candidate is not an active identity 0..3.", person_id=person_id, identity=person["identity"])
        if person["ready_flags"] & PERSON_BUSY_BIT:
            raise ProbeError("SOURCE_PERSON_BUSY", "Source candidate already has the command-busy bit.", person_id=person_id, flags=_hex(person["ready_flags"]))
        if person[ability_name] > 255:
            raise ProbeError("SOURCE_ABILITY_RANGE", "Source candidate ability exceeds the validated range.", person_id=person_id, ability=ability_name, value=person[ability_name])
        residence = person["residence"]
        _checked_range(residence + 0x20, 0x48)
        if residence not in containers:
            raw = memory.read(residence + 0x20, 0x48)
            containers[residence] = (_u32(raw, 0), _u32(raw, 0x44))
        flags, owner = containers[residence]
        city_id = _exact_table_index(owner, CITY_BASE, CITY_STRIDE, CITY_COUNT)
        if not (flags & 1) or city_id is None:
            raise ProbeError("SOURCE_RESIDENCE_INVALID", "Candidate residence does not resolve through embedded bit0 to an exact CITY record.", person_id=person_id, residence=_hex(residence), flags=_hex(flags), owner=_hex(owner))
        if owner_city is None:
            owner_city = city_id
        elif owner_city != city_id:
            raise ProbeError("SOURCE_MULTIPLE_CITIES", "One UI source list resolves to more than one city.", first_city=owner_city, other_city=city_id)
    assert owner_city is not None
    city_address = CITY_BASE + owner_city * CITY_STRIDE
    city = memory.read(city_address, CITY_STRIDE)
    if _u32(city, 0) != CITY_VPTR or city[CITY_TYPE_OFFSET] != CITY_TYPE:
        raise ProbeError("SOURCE_CITY_IDENTITY", "Source owner is not the exact validated CITY object.", city_id=owner_city, vptr=_hex(_u32(city, 0)), type_value=city[CITY_TYPE_OFFSET])
    resident_header = city[CITY_RESIDENT_LIST_OFFSET:CITY_RESIDENT_LIST_OFFSET + LIST_HEADER_SIZE]
    resident = parse_person_list(memory, city_address + CITY_RESIDENT_LIST_OFFSET, "city_resident", persons, PERSON_COUNT, list_type, resident_header)
    for person_id in resident["person_ids"]:
        person = persons.record(person_id)
        if person["identity"] > 3:
            raise ProbeError("RESIDENT_IDENTITY_INVALID", "City resident chain contains a non-active identity.", person_id=person_id, identity=person["identity"])
        residence = person["residence"]
        _checked_range(residence + 0x20, 0x48)
        if residence not in containers:
            raw = memory.read(residence + 0x20, 0x48)
            containers[residence] = (_u32(raw, 0), _u32(raw, 0x44))
        flags, owner = containers[residence]
        if not (flags & 1) or owner != city_address:
            raise ProbeError("RESIDENT_OWNER_MISMATCH", "City resident node and PERSON residence chain disagree.", person_id=person_id, owner=_hex(owner), expected=_hex(city_address))
    ready_resident = [person_id for person_id in resident["person_ids"] if not (persons.record(person_id)["ready_flags"] & PERSON_BUSY_BIT)]
    expected_source = sorted(ready_resident, key=lambda person_id: persons.record(person_id)[ability_name], reverse=True)
    if source_ids != expected_source:
        raise ProbeError("NATIVE_SOURCE_ORDER_MISMATCH", "Source list is not the exact ready resident set in native stable ability order.", actual=source_ids, expected=expected_source, ability=ability_name)
    return {
        "city_id": owner_city,
        "city_address": _hex(city_address),
        "resident_count": resident["count"],
        "resident_person_ids": resident["person_ids"],
        "ready_count": len(ready_resident),
        "native_source_order_verified": True,
    }


def validate_task_candidate(memory: Any, address: int, persons: PersonTable) -> Dict[str, Any]:
    _checked_range(address, TASK_READ_SIZE)
    if address % 4:
        raise ProbeError("TASK_ADDRESS_ALIGNMENT", "UI task address is not 4-byte aligned.", address=_hex(address))
    raw = memory.read(address, TASK_READ_SIZE)
    vptr = _u32(raw, 0)
    if vptr not in UI_TASKS:
        raise ProbeError("TASK_VPTR_CHANGED", "Candidate vptr changed before validation; its old bytes are not interpreted.", address=_hex(address), actual=_hex(vptr))
    command, ability_offset = UI_TASKS[vptr]
    ability_name = {
        PERSON_MIGHT_OFFSET: "might",
        PERSON_INTELLIGENCE_OFFSET: "intelligence",
        PERSON_POLITICS_OFFSET: "politics",
        PERSON_LEADERSHIP_OFFSET: "leadership",
    }[ability_offset]
    parsed: Dict[str, Dict[str, Any]] = {}
    list_type: Optional[int] = None
    for name in ("committed", "source", "working"):
        offset = TASK_LISTS[name]
        header = raw[offset:offset + LIST_HEADER_SIZE]
        item = parse_person_list(memory, address + offset, "task_" + name, persons, PERSON_COUNT if name == "source" else MAX_SELECTION, list_type, header)
        current_type = int(item["type"], 16)
        if list_type is None:
            list_type = current_type
        parsed[name] = item
    assert list_type is not None
    source_ids = parsed["source"]["person_ids"]
    limit = min(MAX_SELECTION, len(source_ids))
    source_set = set(source_ids)
    for name in ("working", "committed"):
        ids = parsed[name]["person_ids"]
        if len(ids) > limit:
            raise ProbeError("SELECTION_EXCEEDS_NATIVE_LIMIT", "Selection exceeds min(5, source count).", list=name, count=len(ids), limit=limit)
        if not set(ids).issubset(source_set):
            raise ProbeError("SELECTION_NOT_IN_SOURCE", "Selection contains a person outside this task's source list.", list=name, selected=ids, source=source_ids)
    origin = _source_origin(memory, parsed["source"], persons, list_type, ability_name)
    source_details = [{"person_id": person_id, "ability": persons.record(person_id)[ability_name]} for person_id in source_ids]
    return {
        "address": _hex(address),
        "vptr": _hex(vptr),
        "command": command,
        "ability": ability_name,
        "list_type": _hex(list_type),
        "lists": parsed,
        "source_origin": origin,
        "source_details": source_details,
        "native_source_top5": source_ids[:limit],
        "native_selection_limit": limit,
    }


def scan_ui_task_candidates(reader: ProcessReader) -> Tuple[List[int], Dict[str, int]]:
    patterns = {struct.pack("<I", vptr) for vptr in UI_TASKS}
    candidates: List[int] = []
    cursor = MIN_USER_ADDRESS
    queried = 0
    scanned_regions = 0
    scanned_bytes = 0
    while cursor < MAX_USER_ADDRESS_EXCLUSIVE:
        queried += 1
        if queried > MAX_QUERY_REGIONS:
            raise ProbeError("SCAN_REGION_LIMIT", "VirtualQueryEx region limit exceeded; scan would be incomplete.", limit=MAX_QUERY_REGIONS)
        region = reader.query(cursor)
        region_end = min(region["base"] + region["size"], MAX_USER_ADDRESS_EXCLUSIVE)
        if region_end <= cursor:
            raise ProbeError("SCAN_NO_PROGRESS", "Memory-map scan did not advance.", address=_hex(cursor))
        base_protect = region["protect"] & 0xFF
        scan_region = (
            region["state"] == MEM_COMMIT
            and region["type"] == MEM_PRIVATE
            and not (region["protect"] & PAGE_GUARD)
            and base_protect in WRITABLE_SCAN_PROTECTIONS
        )
        if scan_region:
            scanned_regions += 1
            length = region_end - max(cursor, region["base"])
            if scanned_bytes + length > MAX_SCAN_BYTES:
                raise ProbeError("SCAN_BYTE_LIMIT", "Readable private heap exceeds the bounded scan budget; refusing a partial uniqueness claim.", scanned=scanned_bytes, next_region=length, limit=MAX_SCAN_BYTES)
            address = max(cursor, region["base"])
            while address < region_end:
                size = min(SCAN_CHUNK, region_end - address)
                blob = reader.read(address, size)
                first = (-address) & 3
                for offset in range(first, len(blob) - 3, 4):
                    if blob[offset:offset + 4] in patterns:
                        candidates.append(address + offset)
                        if len(candidates) > MAX_RAW_CANDIDATES:
                            raise ProbeError("TASK_VPTR_OCCURRENCE_LIMIT", "Too many UI vptr occurrences for a safe uniqueness decision.", limit=MAX_RAW_CANDIDATES)
                address += size
            scanned_bytes += length
        cursor = region_end
    return candidates, {"queried_regions": queried, "scanned_private_regions": scanned_regions, "scanned_bytes": scanned_bytes}


def _same_identity(actual: Dict[str, Any], expected: Dict[str, Any]) -> bool:
    return actual["pid"] == expected["pid"] and actual["creation_time_100ns"] == expected["creation_time_100ns"] and _canonical_path(actual["image_path"]) == _canonical_path(expected["image_path"])


def _unique_candidate(candidates: Sequence[int]) -> Optional[int]:
    if len(candidates) > 1:
        raise ProbeError("UI_TASK_NOT_UNIQUE", "More than one five-class UI task vptr exists in writable private memory.", candidates=[_hex(item) for item in candidates])
    return candidates[0] if candidates else None


def _capture_pass(reader: ProcessReader, session: Dict[str, Any]) -> Dict[str, Any]:
    before = reader.identity()
    if not _same_identity(before, session):
        raise ProbeError("PROCESS_GENERATION_CHANGED", "PID creation generation or image path changed before capture.", expected=session, actual=before)
    reader.validate_live_pe()
    conflicts_before = reader.scan_conflicts()
    persons = PersonTable(reader.read(PERSON_BASE, PERSON_TABLE_SIZE))
    candidates, scan = scan_ui_task_candidates(reader)
    candidate = _unique_candidate(candidates)
    task = validate_task_candidate(reader, candidate, persons) if candidate is not None else None
    expected_type = int(task["list_type"], 16) if task is not None else None
    global_list = parse_person_list(reader, GLOBAL_SELECTED_LIST, "global_selected", persons, MAX_SELECTION, expected_type)
    conflicts_after = reader.scan_conflicts()
    if conflicts_before != conflicts_after:
        raise ProbeError("CONFLICT_INVENTORY_CHANGED", "Conflict/module inventory changed within one capture pass.", before=conflicts_before, after=conflicts_after)
    reader.validate_live_pe()
    after = reader.identity()
    if not _same_identity(after, session) or not _same_identity(after, before):
        raise ProbeError("PROCESS_GENERATION_CHANGED", "PID creation generation or image path changed during capture.", expected=session, before=before, after=after)
    canonical = {
        "conflict_scan": conflicts_after,
        "person_table_canonical_sha256": persons.canonical_sha256,
        "task": task,
        "global_selected": global_list,
    }
    canonical_bytes = json.dumps(canonical, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    return {
        "canonical": canonical,
        "canonical_sha256": hashlib.sha256(canonical_bytes).hexdigest().upper(),
        "scan": scan,
    }


def _ab_equal(first: Dict[str, Any], second: Dict[str, Any]) -> bool:
    return first.get("canonical_sha256") == second.get("canonical_sha256") and first.get("canonical") == second.get("canonical")


def stable_capture(reader: ProcessReader, session: Dict[str, Any], attempts: int = 3) -> Dict[str, Any]:
    mismatches: List[Dict[str, Any]] = []
    for attempt in range(1, attempts + 1):
        first = _capture_pass(reader, session)
        second = _capture_pass(reader, session)
        if _ab_equal(first, second):
            return {
                "ab_stable": True,
                "attempt": attempt,
                "canonical_sha256": second["canonical_sha256"],
                "conflict_scan": second["canonical"]["conflict_scan"],
                "person_table_canonical_sha256": second["canonical"]["person_table_canonical_sha256"],
                "task": second["canonical"]["task"],
                "global_selected": second["canonical"]["global_selected"],
                "scan_a": first["scan"],
                "scan_b": second["scan"],
                "execution_authorized": False,
            }
        mismatches.append({"attempt": attempt, "a": first["canonical_sha256"], "b": second["canonical_sha256"]})
    raise ProbeError("AB_STABILITY_FAILED", "Selection evidence changed across every bounded A/B attempt.", attempts=mismatches)


class LifetimeTracker:
    def __init__(self) -> None:
        self._key: Optional[Tuple[str, str]] = None
        self._lifetime = 0

    def observe(self, task: Optional[Dict[str, Any]]) -> Dict[str, Any]:
        new_key = None if task is None else (task["address"], task["vptr"])
        events: List[Dict[str, Any]] = []
        if self._key is not None and self._key != new_key:
            events.append({
                "event": "task_lifetime_ended",
                "lifetime_id": self._lifetime,
                "former_address": self._key[0],
                "former_vptr": self._key[1],
                "rule": "former address bytes were not dereferenced or interpreted after identity loss",
            })
            self._key = None
        if new_key is not None and self._key is None:
            self._lifetime += 1
            self._key = new_key
            events.append({"event": "task_lifetime_started", "lifetime_id": self._lifetime, "address": new_key[0], "vptr": new_key[1]})
        return {"active_lifetime_id": self._lifetime if self._key is not None else None, "events": events}


class SparseMemory:
    """Pure in-memory transport used only by --self-test."""

    def __init__(self) -> None:
        self.segments: List[Tuple[int, bytearray]] = []

    def add(self, address: int, data: bytes) -> None:
        end = address + len(data)
        for base, segment in self.segments:
            if address < base + len(segment) and base < end:
                raise AssertionError("synthetic segments overlap")
        self.segments.append((address, bytearray(data)))

    def read(self, address: int, size: int) -> bytes:
        for base, segment in self.segments:
            offset = address - base
            if 0 <= offset and offset + size <= len(segment):
                return bytes(segment[offset:offset + size])
        raise ProbeError("SYNTHETIC_READ_MISS", "Synthetic memory has no exact range.", address=_hex(address), size=size)

    def write(self, address: int, data: bytes) -> None:
        for base, segment in self.segments:
            offset = address - base
            if 0 <= offset and offset + len(data) <= len(segment):
                segment[offset:offset + len(data)] = data
                return
        raise AssertionError("synthetic write miss")


def _synthetic_list(memory: SparseMemory, header: int, ids: Sequence[int], node_base: int, list_type: int) -> List[int]:
    nodes = [node_base + index * 0x20 for index in range(len(ids))]
    memory.write(header, struct.pack("<IIII", list_type, nodes[0] if nodes else 0, nodes[-1] if nodes else 0, len(nodes)))
    for index, (node, person_id) in enumerate(zip(nodes, ids)):
        next_node = nodes[index + 1] if index + 1 < len(nodes) else 0
        previous = nodes[index - 1] if index else 0
        memory.add(node, struct.pack("<III", next_node, previous, PERSON_BASE + person_id * PERSON_STRIDE))
    return nodes


def _synthetic_state(source: Sequence[int] = (10, 11, 12), working: Sequence[int] = (10, 11), committed: Sequence[int] = (), resident: Sequence[int] = (10, 11, 12, 13)) -> Tuple[SparseMemory, int, Dict[str, List[int]]]:
    memory = SparseMemory()
    task_address = 0x00200000
    list_type = PERSON_LIST_VPTR
    task = bytearray(TASK_READ_SIZE)
    struct.pack_into("<I", task, 0, 0x0060CCB0)
    memory.add(task_address, task)
    people = bytearray(PERSON_TABLE_SIZE)
    for person_id in range(PERSON_COUNT):
        offset = person_id * PERSON_STRIDE
        struct.pack_into("<H", people, offset + PERSON_ID_OFFSET, person_id)
        struct.pack_into("<I", people, offset + PERSON_IDENTITY_OFFSET, 0xFFFFFFFF)
    politics = {10: 90, 11: 80, 12: 80, 13: 70}
    city_id = 4
    city_address = CITY_BASE + city_id * CITY_STRIDE
    for person_id in resident:
        offset = person_id * PERSON_STRIDE
        residence = 0x03000000 + person_id * 0x100
        struct.pack_into("<I", people, offset + PERSON_POLITICS_OFFSET, politics.get(person_id, 1))
        struct.pack_into("<I", people, offset + PERSON_IDENTITY_OFFSET, 2)
        struct.pack_into("<I", people, offset + PERSON_READY_FLAGS_OFFSET, PERSON_BUSY_BIT if person_id == 13 else 0)
        struct.pack_into("<I", people, offset + PERSON_RESIDENCE_OFFSET, residence)
        container = bytearray(0x68)
        struct.pack_into("<I", container, 0x20, 1)
        struct.pack_into("<I", container, 0x64, city_address)
        memory.add(residence, container)
    memory.add(PERSON_BASE, people)
    city_table = bytearray(CITY_STRIDE * CITY_COUNT)
    city_offset = city_id * CITY_STRIDE
    struct.pack_into("<I", city_table, city_offset, CITY_VPTR)
    city_table[city_offset + CITY_TYPE_OFFSET] = CITY_TYPE
    memory.add(CITY_BASE, city_table)
    memory.add(GLOBAL_SELECTED_LIST, bytes(LIST_HEADER_SIZE))
    nodes = {
        "committed": _synthetic_list(memory, task_address + TASK_LISTS["committed"], committed, 0x02000000, list_type),
        "source": _synthetic_list(memory, task_address + TASK_LISTS["source"], source, 0x02010000, list_type),
        "working": _synthetic_list(memory, task_address + TASK_LISTS["working"], working, 0x02020000, list_type),
        "resident": _synthetic_list(memory, city_address + CITY_RESIDENT_LIST_OFFSET, resident, 0x02030000, list_type),
        "global": _synthetic_list(memory, GLOBAL_SELECTED_LIST, working, 0x02040000, list_type),
    }
    return memory, task_address, nodes


def _expect_error(code: str, action: Any) -> None:
    try:
        action()
    except ProbeError as error:
        if error.code != code:
            raise AssertionError("expected %s, got %s" % (code, error.code)) from error
        return
    raise AssertionError("expected %s" % code)


def run_self_test() -> Dict[str, Any]:
    tests: List[Tuple[str, Any]] = []

    def valid() -> None:
        memory, task, _ = _synthetic_state()
        persons = PersonTable(memory.read(PERSON_BASE, PERSON_TABLE_SIZE))
        result = validate_task_candidate(memory, task, persons)
        assert result["native_source_top5"] == [10, 11, 12]
        assert result["source_origin"]["ready_count"] == 3
        global_list = parse_person_list(memory, GLOBAL_SELECTED_LIST, "global_selected", persons, 5, int(result["list_type"], 16))
        assert global_list["person_ids"] == [10, 11]

    tests.append(("valid three-chain/native-order sample", valid))

    def cycle() -> None:
        memory, task, nodes = _synthetic_state()
        memory.write(nodes["source"][-1], struct.pack("<I", nodes["source"][0]))
        persons = PersonTable(memory.read(PERSON_BASE, PERSON_TABLE_SIZE))
        _expect_error("LIST_NOT_TERMINATED", lambda: validate_task_candidate(memory, task, persons))

    tests.append(("node cycle rejected", cycle))

    def wrong_previous() -> None:
        memory, task, nodes = _synthetic_state()
        memory.write(nodes["source"][1] + 4, struct.pack("<I", 0))
        persons = PersonTable(memory.read(PERSON_BASE, PERSON_TABLE_SIZE))
        _expect_error("LIST_PREVIOUS_MISMATCH", lambda: validate_task_candidate(memory, task, persons))

    tests.append(("back-link mismatch rejected", wrong_previous))

    def wrong_person() -> None:
        memory, task, nodes = _synthetic_state()
        memory.write(nodes["source"][0] + 8, struct.pack("<I", PERSON_BASE + 1))
        persons = PersonTable(memory.read(PERSON_BASE, PERSON_TABLE_SIZE))
        _expect_error("LIST_PERSON_POINTER_INVALID", lambda: validate_task_candidate(memory, task, persons))

    tests.append(("misaligned PERSON pointer rejected", wrong_person))

    def wrong_record_id() -> None:
        memory, task, _ = _synthetic_state()
        memory.write(PERSON_BASE + 10 * PERSON_STRIDE + PERSON_ID_OFFSET, struct.pack("<H", 99))
        _expect_error("PERSON_RECORD_ID_MISMATCH", lambda: PersonTable(memory.read(PERSON_BASE, PERSON_TABLE_SIZE)))

    tests.append(("PERSON slot ID mismatch rejected", wrong_record_id))

    def order_violation() -> None:
        memory, task, _ = _synthetic_state(source=(11, 10, 12))
        persons = PersonTable(memory.read(PERSON_BASE, PERSON_TABLE_SIZE))
        _expect_error("NATIVE_SOURCE_ORDER_MISMATCH", lambda: validate_task_candidate(memory, task, persons))

    tests.append(("ability order violation rejected", order_violation))

    def stable_tie_violation() -> None:
        memory, task, _ = _synthetic_state(source=(10, 12, 11))
        persons = PersonTable(memory.read(PERSON_BASE, PERSON_TABLE_SIZE))
        _expect_error("NATIVE_SOURCE_ORDER_MISMATCH", lambda: validate_task_candidate(memory, task, persons))

    tests.append(("resident-order tie violation rejected", stable_tie_violation))

    def selection_outside_source() -> None:
        memory, task, _ = _synthetic_state(working=(10, 13))
        persons = PersonTable(memory.read(PERSON_BASE, PERSON_TABLE_SIZE))
        _expect_error("SELECTION_NOT_IN_SOURCE", lambda: validate_task_candidate(memory, task, persons))

    tests.append(("selection outside source rejected", selection_outside_source))

    def selection_over_limit() -> None:
        memory, task, _ = _synthetic_state(source=(10, 11), working=(10, 11, 12), resident=(10, 11, 13))
        persons = PersonTable(memory.read(PERSON_BASE, PERSON_TABLE_SIZE))
        _expect_error("SELECTION_EXCEEDS_NATIVE_LIMIT", lambda: validate_task_candidate(memory, task, persons))

    tests.append(("min(5, source) bound rejected", selection_over_limit))

    def duplicate_person() -> None:
        memory, task, nodes = _synthetic_state()
        memory.write(nodes["working"][1] + 8, struct.pack("<I", PERSON_BASE + 10 * PERSON_STRIDE))
        persons = PersonTable(memory.read(PERSON_BASE, PERSON_TABLE_SIZE))
        _expect_error("LIST_PERSON_DUPLICATE", lambda: validate_task_candidate(memory, task, persons))

    tests.append(("duplicate person rejected", duplicate_person))

    def lifetime_release() -> None:
        tracker = LifetimeTracker()
        task = {"address": "0x001AEEA4", "vptr": "0x0060CCB0"}
        first = tracker.observe(task)
        ended = tracker.observe(None)
        restarted = tracker.observe(task)
        assert first["active_lifetime_id"] == 1
        assert ended["events"][0]["event"] == "task_lifetime_ended"
        assert "lists" not in ended["events"][0]
        assert restarted["active_lifetime_id"] == 2

    tests.append(("released address never carries stale payload", lifetime_release))

    def ab_and_uniqueness_guards() -> None:
        first = {"canonical_sha256": "A", "canonical": {"task": {"address": 1}}}
        second = {"canonical_sha256": "A", "canonical": {"task": {"address": 1}}}
        changed = {"canonical_sha256": "B", "canonical": {"task": None}}
        assert _ab_equal(first, second)
        assert not _ab_equal(first, changed)
        assert _unique_candidate([]) is None
        assert _unique_candidate([0x001AEEA4]) == 0x001AEEA4
        _expect_error("UI_TASK_NOT_UNIQUE", lambda: _unique_candidate([0x001AEEA4, 0x00200000]))

    tests.append(("A/B equality and unique-vptr guards", ab_and_uniqueness_guards))

    def clean_inventory() -> None:
        modules = [
            {"name": "San9PK.exe", "path": str(TARGET_EXE), "base": IMAGE_BASE, "size": SIZE_OF_IMAGE},
            {"name": "kernel32.dll", "path": r"C:\Windows\System32\kernel32.dll", "base": 0x76000000, "size": 0x100000},
        ]
        result = evaluate_conflict_inventory([{"pid": 1, "name": "explorer.exe"}], modules)
        assert result["clear"] and result["inspected_module_count"] == 2

    tests.append(("clean conflict inventory accepted", clean_inventory))

    def conflict_process() -> None:
        modules = [{"name": "San9PK.exe", "path": str(TARGET_EXE), "base": IMAGE_BASE, "size": SIZE_OF_IMAGE}]
        _expect_error("KNOWN_CONFLICT_PRESENT", lambda: evaluate_conflict_inventory([{"pid": 77, "name": "San9PKHard.exe"}], modules))

    tests.append(("known modifier process rejected", conflict_process))

    def conflict_module() -> None:
        modules = [
            {"name": "San9PK.exe", "path": str(TARGET_EXE), "base": IMAGE_BASE, "size": SIZE_OF_IMAGE},
            {"name": "Easy.dll", "path": r"D:\三国志9\10101749\Easy.dll", "base": 0x10000000, "size": 0x20000},
        ]
        _expect_error("KNOWN_CONFLICT_PRESENT", lambda: evaluate_conflict_inventory([], modules))

    tests.append(("known injected module rejected", conflict_module))

    def local_proxy() -> None:
        modules = [
            {"name": "San9PK.exe", "path": str(TARGET_EXE), "base": IMAGE_BASE, "size": SIZE_OF_IMAGE},
            {"name": "dinput8.dll", "path": r"D:\三国志9\10101749\dinput8.dll", "base": 0x10000000, "size": 0x20000},
        ]
        _expect_error("KNOWN_CONFLICT_PRESENT", lambda: evaluate_conflict_inventory([], modules))

    tests.append(("game-directory proxy rejected", local_proxy))

    def incomplete_module_scan() -> None:
        _expect_error("MAIN_MODULE_ENUMERATION_MISMATCH", lambda: evaluate_conflict_inventory([], []))

    tests.append(("incomplete target module scan rejected", incomplete_module_scan))

    def explicit_live_cli() -> None:
        default = build_parser().parse_args([])
        synthetic = build_parser().parse_args(["--self-test"])
        live = build_parser().parse_args(["--pid", "19876", "--samples", "2"])
        assert default.pid is None and not default.self_test
        assert synthetic.self_test and synthetic.pid is None
        assert live.pid == 19876 and live.samples == 2 and not live.self_test

    tests.append(("live CLI requires explicit PID", explicit_live_cli))

    passed: List[str] = []
    for name, test in tests:
        test()
        passed.append(name)
    return {
        "ok": True,
        "mode": "pure_in_memory_self_test",
        "passed": len(passed),
        "total": len(tests),
        "tests": passed,
        "disk_accessed": False,
        "process_accessed": False,
        "execution_authorized": False,
    }


def _bounded_int(name: str, minimum: int, maximum: int):
    def parse(value: str) -> int:
        try:
            result = int(value, 10)
        except ValueError as error:
            raise argparse.ArgumentTypeError("%s must be a decimal integer" % name) from error
        if result < minimum or result > maximum:
            raise argparse.ArgumentTypeError("%s must be in [%d, %d]" % (name, minimum, maximum))
        return result
    return parse


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Read-only, version-locked San9PK V6 selection lifecycle probe. No arguments means disk-only verification.")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--self-test", action="store_true", help="run synthetic in-memory tests; do not access disk or a process")
    mode.add_argument("--pid", type=_bounded_int("pid", 1, 0xFFFFFFFF), help="explicitly enable read-only live observation of this PID")
    parser.add_argument("--samples", type=_bounded_int("samples", 1, 120), default=1, help="bounded live A/B samples (requires --pid; default: 1)")
    parser.add_argument("--interval-ms", type=_bounded_int("interval-ms", 0, 10000), default=100, help="delay between live samples (requires --pid; default: 100)")
    return parser


def _print(value: Dict[str, Any]) -> None:
    print(json.dumps(value, ensure_ascii=False, sort_keys=True))


def run_live(pid: int, samples: int, interval_ms: int) -> int:
    disk = verify_disk_target()
    tracker = LifetimeTracker()
    with ProcessReader(pid) as reader:
        session = reader.identity()
        reader.validate_live_pe()
        session_conflicts = reader.scan_conflicts()
        _print({
            "ok": True,
            "event": "session_started",
            "target": disk,
            "process": session,
            "conflict_scan": session_conflicts,
            "requested_access": _hex(PROCESS_ACCESS),
            "allowed_target_memory_apis": ["VirtualQueryEx", "ReadProcessMemory"],
            "read_only_inventory_apis": ["CreateToolhelp32Snapshot", "Process32FirstW/NextW", "Module32FirstW/NextW"],
            "execution_authorized": False,
        })
        for index in range(samples):
            snapshot = stable_capture(reader, session)
            # Re-hash the exact disk target after each accepted live A/B sample.
            verify_disk_target()
            lifecycle = tracker.observe(snapshot["task"])
            _print({"ok": True, "event": "selection_sample", "sample_index": index, "process_generation": session, "lifecycle": lifecycle, "snapshot": snapshot, "execution_authorized": False})
            if index + 1 < samples and interval_ms:
                time.sleep(interval_ms / 1000.0)
        if not _same_identity(reader.identity(), session):
            raise ProbeError("PROCESS_GENERATION_CHANGED", "PID creation generation changed before session close.")
        reader.scan_conflicts()
        verify_disk_target()
    _print({"ok": True, "event": "session_completed", "samples": samples, "execution_authorized": False})
    return 0


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = build_parser().parse_args(argv)
    if args.pid is None and (args.samples != 1 or args.interval_ms != 100):
        raise ProbeError("LIVE_OPTION_WITHOUT_PID", "--samples and --interval-ms are valid only with explicit --pid.")
    if args.self_test:
        _print(run_self_test())
        return 0
    if args.pid is not None:
        return run_live(args.pid, args.samples, args.interval_ms)
    _print({
        "ok": True,
        "mode": "disk_only_default",
        "target": verify_disk_target(),
        "process_accessed": False,
        "live_requires_explicit_pid": True,
        "execution_authorized": False,
    })
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        _print({"ok": False, "code": "INTERRUPTED", "message": "Stopped by Ctrl+C; the read-only handle is closing.", "execution_authorized": False})
        raise SystemExit(130)
    except ProbeError as error:
        _print(error.report())
        raise SystemExit(2)
