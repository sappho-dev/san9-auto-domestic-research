#!/usr/bin/env python3
"""Read-only San9PK 1.0.1.0 structure probe.

This utility is intentionally limited to opening an existing process for query/read
access and printing a JSON snapshot.  It does not inject code, call game functions,
or change process memory.

The offsets below are version locked to the SHA-256 recorded in TARGET_SHA256.
Unsupported builds are rejected unless the analyst explicitly asks for a raw,
non-authoritative probe with --allow-unsupported-build.
"""

from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import os
import struct
import sys
from pathlib import Path
from typing import Any, Optional


TARGET_EXE = Path(r"D:\三国志9\10101749\San9PK.exe")
TARGET_SHA256 = "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028"

FORCE_ADDR = 0x01253C38
FORCE_SIZE = 0xD4
FORCE_NUM = 50

PERSON_ADDR = 0x01258EE0
PERSON_SIZE = 0x128
PERSON_NUM = 850

CITY_ADDR = 0x0124DB58
CITY_SIZE = 0x1F0
CITY_NUM = 50

PROCESS_VM_READ = 0x0010
PROCESS_QUERY_INFORMATION = 0x0400
MAX_USER_ADDRESS_32 = 0x7FFFFFFF


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while True:
            block = stream.read(1024 * 1024)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def u16(data: bytes, offset: int) -> int:
    return struct.unpack_from("<H", data, offset)[0]


def u32(data: bytes, offset: int) -> int:
    return struct.unpack_from("<I", data, offset)[0]


def i32(data: bytes, offset: int) -> int:
    return struct.unpack_from("<i", data, offset)[0]


def exact_record_index(pointer: int, base: int, stride: int, count: int) -> Optional[int]:
    delta = pointer - base
    if delta < 0 or delta >= stride * count or delta % stride:
        return None
    return delta // stride


def decode_big5_field(raw: bytes) -> tuple[str, str]:
    field = raw.split(b"\0", 1)[0]
    return field.decode("cp950", errors="replace"), field.hex()


def decode_person_name(record: bytes) -> dict[str, Any]:
    surname, surname_hex = decode_big5_field(record[0x10:0x15])
    given, given_hex = decode_big5_field(record[0x15:0x1A])
    surname_bytes = bytes.fromhex(surname_hex)
    hex_digits = b"0123456789abcdefABCDEF"
    possible_easy_prefix = (
        len(surname_bytes) >= 4
        and surname_bytes[0] in hex_digits
        and surname_bytes[1] in hex_digits
        and surname_bytes[2] >= 0x80
    )
    return {
        "decoded": surname + given,
        "surname_raw_hex": surname_hex,
        "given_raw_hex": given_hex,
        "possible_easy_prefix": possible_easy_prefix,
    }


class ReadError(RuntimeError):
    pass


class ProcessReader:
    def __init__(self, pid: int) -> None:
        if os.name != "nt":
            raise RuntimeError("This probe requires Windows.")

        from ctypes import wintypes

        self._wintypes = wintypes
        self._kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self._kernel32.OpenProcess.argtypes = [
            wintypes.DWORD,
            wintypes.BOOL,
            wintypes.DWORD,
        ]
        self._kernel32.OpenProcess.restype = wintypes.HANDLE
        self._kernel32.ReadProcessMemory.argtypes = [
            wintypes.HANDLE,
            wintypes.LPCVOID,
            wintypes.LPVOID,
            ctypes.c_size_t,
            ctypes.POINTER(ctypes.c_size_t),
        ]
        self._kernel32.ReadProcessMemory.restype = wintypes.BOOL
        self._kernel32.QueryFullProcessImageNameW.argtypes = [
            wintypes.HANDLE,
            wintypes.DWORD,
            wintypes.LPWSTR,
            ctypes.POINTER(wintypes.DWORD),
        ]
        self._kernel32.QueryFullProcessImageNameW.restype = wintypes.BOOL
        self._kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        self._kernel32.CloseHandle.restype = wintypes.BOOL

        self.pid = pid
        self.handle = self._kernel32.OpenProcess(
            PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, False, pid
        )
        if not self.handle:
            error = ctypes.get_last_error()
            raise OSError(error, f"OpenProcess({pid}) failed")

    def close(self) -> None:
        if self.handle:
            self._kernel32.CloseHandle(self.handle)
            self.handle = None

    def __enter__(self) -> "ProcessReader":
        return self

    def __exit__(self, exc_type: Any, exc: Any, traceback: Any) -> None:
        self.close()

    def image_path(self) -> Optional[Path]:
        capacity = self._wintypes.DWORD(32768)
        buffer = ctypes.create_unicode_buffer(capacity.value)
        ok = self._kernel32.QueryFullProcessImageNameW(
            self.handle, 0, buffer, ctypes.byref(capacity)
        )
        return Path(buffer.value) if ok else None

    def read(self, address: int, size: int) -> bytes:
        if (
            address < 0x10000
            or size < 0
            or size > 2 * 1024 * 1024
            or address + size > MAX_USER_ADDRESS_32 + 1
        ):
            raise ReadError(f"refusing implausible read: address={address:#x}, size={size}")
        buffer = ctypes.create_string_buffer(size)
        transferred = ctypes.c_size_t()
        ok = self._kernel32.ReadProcessMemory(
            self.handle,
            ctypes.c_void_p(address),
            buffer,
            size,
            ctypes.byref(transferred),
        )
        if not ok or transferred.value != size:
            error = ctypes.get_last_error()
            raise ReadError(
                f"RPM failed at {address:#x}: requested={size}, "
                f"received={transferred.value}, winerror={error}"
            )
        return buffer.raw

    def read_u32(self, address: int) -> int:
        return struct.unpack("<I", self.read(address, 4))[0]


def parse_position_list(reader: ProcessReader, city_address: int) -> dict[str, Any]:
    """Parse the source-named city 'in-position officer' list at city+0xDC.

    The meaning is deliberately not promoted to 'unacted/free'.  A live sample
    showed that it held every active in-city officer, including officers that are
    not proven command-available.
    """

    header_address = city_address + 0xDC
    header = reader.read(header_address, 0x10)
    first = u32(header, 0x04)
    last = u32(header, 0x08)
    reported_count = u32(header, 0x0C)
    result: dict[str, Any] = {
        "header": f"0x{header_address:08X}",
        "vtable_or_type": f"0x{u32(header, 0):08X}",
        "first_node": f"0x{first:08X}",
        "last_node": f"0x{last:08X}",
        "reported_count": reported_count,
        "person_indices": [],
        "complete": False,
    }

    if reported_count > PERSON_NUM:
        result["error"] = "reported count is implausible"
        return result
    if reported_count == 0:
        result["complete"] = first == 0
        return result

    current = first
    seen: set[int] = set()
    nodes: list[str] = []
    people: list[int] = []
    try:
        while current and len(nodes) <= reported_count:
            if current in seen:
                result["error"] = "cycle detected"
                break
            seen.add(current)
            node = reader.read(current, 0x0C)
            next_node = u32(node, 0x00)
            person_pointer = u32(node, 0x08)
            person_index = exact_record_index(
                person_pointer, PERSON_ADDR, PERSON_SIZE, PERSON_NUM
            )
            nodes.append(f"0x{current:08X}")
            if person_index is None:
                result["error"] = f"non-person payload 0x{person_pointer:08X}"
                break
            people.append(person_index)
            current = next_node
    except ReadError as error:
        result["error"] = str(error)

    result["nodes"] = nodes
    result["person_indices"] = people
    result["complete"] = (
        "error" not in result
        and current == 0
        and len(people) == reported_count
        and (not nodes or nodes[-1].upper() == f"0x{last:08X}".upper())
    )
    return result


def force_snapshot(force_blob: bytes) -> tuple[list[dict[str, Any]], dict[int, dict[str, Any]]]:
    rows: list[dict[str, Any]] = []
    by_index: dict[int, dict[str, Any]] = {}
    for index in range(FORCE_NUM):
        address = FORCE_ADDR + index * FORCE_SIZE
        record = force_blob[index * FORCE_SIZE : (index + 1) * FORCE_SIZE]
        flags = record[0x34]
        main_pointer = u32(record, 0xB8)
        leader_pointer = u32(record, 0xBC)
        row = {
            "index": index,
            "address": f"0x{address:08X}",
            "valid_by_leader_nonzero": leader_pointer != 0,
            "flags_0x34": f"0x{flags:02X}",
            "player_control_bit0": bool(flags & 0x01),
            "barbarian_bit2": bool(flags & 0x04),
            "main_force_pointer": f"0x{main_pointer:08X}",
            "main_force_index": exact_record_index(
                main_pointer, FORCE_ADDR, FORCE_SIZE, FORCE_NUM
            ),
            "is_main_record": main_pointer == address,
            "leader_pointer_0xBC": f"0x{leader_pointer:08X}",
            "leader_person_index": exact_record_index(
                leader_pointer, PERSON_ADDR, PERSON_SIZE, PERSON_NUM
            ),
        }
        by_index[index] = row
        if row["valid_by_leader_nonzero"]:
            rows.append(row)
    return rows, by_index


def city_snapshot(
    reader: ProcessReader,
    city_blob: bytes,
    forces_by_index: dict[int, dict[str, Any]],
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for index in range(CITY_NUM):
        address = CITY_ADDR + index * CITY_SIZE
        record = city_blob[index * CITY_SIZE : (index + 1) * CITY_SIZE]
        name, name_hex = decode_big5_field(record[0x20:0x30])
        legion_pointer = u32(record, 0xCC)
        legion_index = exact_record_index(
            legion_pointer, FORCE_ADDR, FORCE_SIZE, FORCE_NUM
        )
        legion = forces_by_index.get(legion_index) if legion_index is not None else None
        row: dict[str, Any] = {
            "index": index,
            "address": f"0x{address:08X}",
            "type_0x06": record[0x06],
            "is_city_type_5": record[0x06] == 5,
            "name": name,
            "name_raw_hex": name_hex,
            "legion_pointer_0xCC": f"0x{legion_pointer:08X}",
            "legion_index": legion_index,
            "owner_main_force_index": legion["main_force_index"] if legion else None,
            "current_player_control": legion["player_control_bit0"] if legion else False,
            "structural_main_corps": legion["is_main_record"] if legion else False,
        }
        try:
            row["position_list_0xDC"] = parse_position_list(reader, address)
        except ReadError as error:
            row["position_list_0xDC"] = {"complete": False, "error": str(error)}
        rows.append(row)
    return rows


def person_snapshot(
    reader: ProcessReader,
    person_blob: bytes,
    selected_city_indices: set[int],
) -> tuple[list[dict[str, Any]], dict[int, set[int]]]:
    rows: list[dict[str, Any]] = []
    active_in_city: dict[int, set[int]] = {}

    for index in range(PERSON_NUM):
        address = PERSON_ADDR + index * PERSON_SIZE
        record = person_blob[index * PERSON_SIZE : (index + 1) * PERSON_SIZE]
        status = i32(record, 0x84)
        location_pointer = u32(record, 0xF4)
        location_flags: Optional[int] = None
        owner_pointer: Optional[int] = None
        city_index: Optional[int] = None

        if 0x10000 <= location_pointer <= MAX_USER_ADDRESS_32 - 0x68:
            try:
                location = reader.read(location_pointer + 0x20, 0x48)
                location_flags = u32(location, 0x00)
                owner_pointer = u32(location, 0x44)
                city_index = exact_record_index(
                    owner_pointer, CITY_ADDR, CITY_SIZE, CITY_NUM
                )
            except ReadError:
                pass

        physically_in_city = bool(
            city_index is not None
            and location_flags is not None
            and location_flags & 0x01
        )
        # Public-source comparisons identify 0=君主, 1=都督, 4=俘虏 and
        # negative values as inactive; the ordinary active range is 0..3.
        active_identity = 0 <= status <= 3
        if physically_in_city and active_identity and city_index is not None:
            active_in_city.setdefault(city_index, set()).add(index)

        if not (
            physically_in_city
            and active_identity
            and city_index in selected_city_indices
        ):
            continue

        rows.append(
            {
                "index": index,
                "record_id_0x04": u16(record, 0x04),
                "address": f"0x{address:08X}",
                "name": decode_person_name(record),
                "identity_status_i32_0x84": status,
                "city_index_by_location_chain": city_index,
                "location_pointer_0xF4": f"0x{location_pointer:08X}",
                "location_flags_0x20": (
                    f"0x{location_flags:08X}" if location_flags is not None else None
                ),
                "location_owner_0x64": (
                    f"0x{owner_pointer:08X}" if owner_pointer is not None else None
                ),
                "abilities": {
                    "leadership": {"base_0x64": u32(record, 0x64), "current_0x68": u32(record, 0x68)},
                    "war": {"base_0x4C": u32(record, 0x4C), "current_0x50": u32(record, 0x50)},
                    "intelligence": {"base_0x54": u32(record, 0x54), "current_0x58": u32(record, 0x58)},
                    "politics": {"base_0x5C": u32(record, 0x5C), "current_0x60": u32(record, 0x60)},
                },
                "acted_or_command_available": None,
            }
        )

    return rows, active_in_city


def add_position_list_consistency(
    cities: list[dict[str, Any]], active_in_city: dict[int, set[int]]
) -> None:
    for city in cities:
        position = city.get("position_list_0xDC", {})
        listed = set(position.get("person_indices", []))
        active = active_in_city.get(city["index"], set())
        city["position_list_consistency"] = {
            "active_status_0_to_3_in_city_count": len(active),
            "position_list_count_parsed": len(listed),
            "sets_equal": bool(position.get("complete")) and listed == active,
            "active_missing_from_list": sorted(active - listed),
            "list_not_active_in_city": sorted(listed - active),
        }


def build_snapshot(reader: ProcessReader, all_cities: bool) -> dict[str, Any]:
    force_blob = reader.read(FORCE_ADDR, FORCE_NUM * FORCE_SIZE)
    city_blob = reader.read(CITY_ADDR, CITY_NUM * CITY_SIZE)
    person_blob = reader.read(PERSON_ADDR, PERSON_NUM * PERSON_SIZE)

    forces, forces_by_index = force_snapshot(force_blob)
    cities = city_snapshot(reader, city_blob, forces_by_index)
    controlled = {city["index"] for city in cities if city["current_player_control"]}
    selected = set(range(CITY_NUM)) if all_cities else controlled
    people, active_in_city = person_snapshot(reader, person_blob, selected)
    add_position_list_consistency(cities, active_in_city)

    return {
        "tables": {
            "force": {"base": f"0x{FORCE_ADDR:08X}", "stride": FORCE_SIZE, "count": FORCE_NUM},
            "city": {"base": f"0x{CITY_ADDR:08X}", "stride": CITY_SIZE, "count": CITY_NUM},
            "person": {"base": f"0x{PERSON_ADDR:08X}", "stride": PERSON_SIZE, "count": PERSON_NUM},
        },
        "valid_forces": forces,
        "cities": cities,
        "controlled_city_indices": sorted(controlled),
        "selected_active_in_city_people": people,
        "limitations": [
            "acted/command-available state is not decoded; null is intentional",
            "city+0xDC is an in-position list, not proven to be an unacted/free list",
            "San9 exposes four ability fields here; no fifth/charisma field is claimed",
            "loaded modifiers can change code or data; this snapshot is not pristine evidence",
        ],
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", required=True, type=int, help="existing San9PK process id")
    parser.add_argument(
        "--exe", type=Path, default=TARGET_EXE, help="on-disk executable to hash"
    )
    parser.add_argument(
        "--all-cities",
        action="store_true",
        help="include active in-city people for every city, not just player-controlled cities",
    )
    parser.add_argument(
        "--allow-unsupported-build",
        action="store_true",
        help="read despite a hash mismatch; all decoded meanings then remain non-authoritative",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        disk_hash = sha256_file(args.exe)
    except OSError as error:
        print(f"cannot hash {args.exe}: {error}", file=sys.stderr)
        return 2

    supported = disk_hash.lower() == TARGET_SHA256
    if not supported and not args.allow_unsupported_build:
        print(
            "unsupported executable hash; refusing to interpret version-locked offsets\n"
            f"expected: {TARGET_SHA256}\nactual:   {disk_hash}",
            file=sys.stderr,
        )
        return 3

    try:
        with ProcessReader(args.pid) as reader:
            process_path = reader.image_path()
            if process_path is not None and process_path.resolve() != args.exe.resolve():
                print(
                    f"PID image mismatch: process={process_path}, expected={args.exe}",
                    file=sys.stderr,
                )
                return 4
            snapshot = build_snapshot(reader, args.all_cities)
    except (OSError, ReadError, RuntimeError) as error:
        print(str(error), file=sys.stderr)
        return 5

    output = {
        "schema": "san9pk-v1-read/1",
        "pid": args.pid,
        "process_image": str(process_path) if process_path else None,
        "disk_sha256": disk_hash,
        "supported_exact_build": supported,
        "snapshot": snapshot,
    }
    print(json.dumps(output, ensure_ascii=True, indent=2, sort_keys=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
