#!/usr/bin/env python3
"""Independent stdlib oracle for the frozen 512-byte P1 request/response.

This module deliberately duplicates the reviewed wire specification.  It does
not import, parse, invoke, or generate code from either the C or C# P1Wire
implementation.  Its only dependencies are Python standard-library modules.
"""

from __future__ import annotations

import argparse
import hashlib
import hmac
import os
import struct
import sys


FRAME_SIZE = 512
HMAC_OFFSET = 472
HMAC_SIZE = 32
CRC_OFFSET = 20
REQUEST_SHA256 = "acadf1f1c5cf70672484632e32865871058e3193b84ffc5c233990985fbe80d5"
# Filled from this independent encoder once, then treated as a frozen vector.
RESPONSE_SHA256 = "2dcef30e7c5b5219f255d97c7a260019748551d2279c868e62539ce204a0e87e"


def fill(size: int, seed: int) -> bytes:
    return bytes((seed + index * 17) & 0xFF for index in range(size))


def crc32_ieee(frame: bytes | bytearray) -> int:
    crc = 0xFFFFFFFF
    for index, original in enumerate(frame):
        value = 0 if CRC_OFFSET <= index < CRC_OFFSET + 4 else original
        crc ^= value
        for _ in range(8):
            mask = -(crc & 1) & 0xFFFFFFFF
            crc = ((crc >> 1) ^ (0xEDB88320 & mask)) & 0xFFFFFFFF
    return (~crc) & 0xFFFFFFFF


def key() -> bytes:
    return fill(32, 0xD0)


def encode(response: bool) -> bytes:
    frame = bytearray(FRAME_SIZE)
    struct.pack_into("<IHHIHHIII", frame, 0,
                     0x31503953, 1, 0, FRAME_SIZE,
                     2 if response else 1,
                     2 if response else 1,
                     0, 0, 0)
    struct.pack_into("<QQQIIIII", frame, 32,
                     1, 1_000_000, 1_004_000,
                     0x11223344, 0x12345678, 0x00123456,
                     0x55667788, 0x01020304)
    struct.pack_into("<QQQ", frame, 80,
                     0x0102030405060708,
                     0x1112131415161718,
                     0x2122232425262728)
    frame[104:120] = fill(16, 0x10)
    frame[120:136] = fill(16, 0x20)
    frame[136:152] = fill(16, 0x30)
    for offset, seed in zip(
            range(152, 440, 32),
            (0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xA0, 0xB0, 0xC0),
            strict=True):
        frame[offset:offset + 32] = fill(32, seed)
    if response:
        frame[440:472] = fill(32, 0xE0)

    canonical = bytearray(frame)
    canonical[CRC_OFFSET:CRC_OFFSET + 4] = b"\0" * 4
    canonical[HMAC_OFFSET:HMAC_OFFSET + HMAC_SIZE] = b"\0" * HMAC_SIZE
    frame[HMAC_OFFSET:HMAC_OFFSET + HMAC_SIZE] = hmac.new(
        key(), canonical, hashlib.sha256).digest()
    struct.pack_into("<I", frame, CRC_OFFSET, crc32_ieee(frame))
    return bytes(frame)


def validate(frame: bytes, response: bool) -> None:
    if len(frame) != FRAME_SIZE:
        raise ValueError(f"frame size is {len(frame)}, expected {FRAME_SIZE}")
    magic, major, minor, declared = struct.unpack_from("<IHHI", frame, 0)
    kind, state, flags = struct.unpack_from("<HHI", frame, 12)
    persisted_crc, result = struct.unpack_from("<II", frame, 20)
    if (magic, major, minor, declared) != (0x31503953, 1, 0, FRAME_SIZE):
        raise ValueError("header identity mismatch")
    if (kind, state) != ((2, 2) if response else (1, 1)):
        raise ValueError("kind/state mismatch")
    if flags != 0 or any(frame[28:32]) or any(frame[76:80]) or any(frame[504:512]):
        raise ValueError("flags or reserved bytes are nonzero")
    if persisted_crc != crc32_ieee(frame):
        raise ValueError("CRC mismatch")

    canonical = bytearray(frame)
    canonical[CRC_OFFSET:CRC_OFFSET + 4] = b"\0" * 4
    canonical[HMAC_OFFSET:HMAC_OFFSET + HMAC_SIZE] = b"\0" * HMAC_SIZE
    expected_mac = hmac.new(key(), canonical, hashlib.sha256).digest()
    if not hmac.compare_digest(frame[HMAC_OFFSET:HMAC_OFFSET + HMAC_SIZE], expected_mac):
        raise ValueError("HMAC mismatch")

    sequence, issued, expires = struct.unpack_from("<QQQ", frame, 32)
    required_scalars = struct.unpack_from("<IIIII", frame, 56) + struct.unpack_from("<QQQ", frame, 80)
    required_ranges = (frame[104:120], frame[120:136], frame[136:152]) + tuple(
        frame[offset:offset + 32] for offset in range(152, 440, 32))
    if sequence == 0 or any(value == 0 for value in required_scalars):
        raise ValueError("required scalar is zero")
    if any(not any(value) for value in required_ranges):
        raise ValueError("required nonce/digest is all zero")
    if issued == 0 or expires <= issued or expires - issued > 5000:
        raise ValueError("lifetime mismatch")
    result_digest_zero = not any(frame[440:472])
    if response:
        if result != 0 or result_digest_zero:
            raise ValueError("completed response shape mismatch")
    elif result != 0 or not result_digest_zero:
        raise ValueError("request shape mismatch")


def frozen_hash(response: bool) -> str:
    return RESPONSE_SHA256 if response else REQUEST_SHA256


def assert_frozen(frame: bytes, response: bool) -> str:
    digest = hashlib.sha256(frame).hexdigest()
    expected = frozen_hash(response)
    if expected != "PENDING" and not hmac.compare_digest(digest, expected):
        raise ValueError(f"SHA-256 mismatch: actual={digest} expected={expected}")
    return digest


def write_exact(path: str, response: bool) -> None:
    frame = encode(response)
    validate(frame, response)
    digest = assert_frozen(frame, response)
    parent = os.path.dirname(os.path.abspath(path))
    if parent:
        os.makedirs(parent, exist_ok=True)
    with open(path, "wb") as stream:
        stream.write(frame)
    print(f"P1WIRE_PYTHON_{'RESPONSE_' if response else ''}GOLDEN_WRITTEN {path} sha256={digest}")


def verify_exact(path: str, response: bool) -> None:
    with open(path, "rb") as stream:
        candidate = stream.read(FRAME_SIZE + 1)
    validate(candidate, response)
    expected = encode(response)
    if not hmac.compare_digest(candidate, expected):
        raise ValueError("candidate is valid but not the frozen byte-exact golden")
    digest = assert_frozen(candidate, response)
    print(f"P1WIRE_PYTHON_{'RESPONSE_' if response else ''}GOLDEN_VERIFIED {path} sha256={digest}")


def self_test() -> None:
    checks = 0
    for response in (False, True):
        frame = encode(response)
        validate(frame, response)
        assert_frozen(frame, response)
        checks += 3
        for offset in range(FRAME_SIZE):
            changed = bytearray(frame)
            changed[offset] ^= 1
            try:
                validate(bytes(changed), response)
            except ValueError:
                checks += 1
            else:
                raise AssertionError(f"one-byte mutation accepted at offset {offset}")
    print(
        "P1WIRE_PYTHON_ORACLE PASS "
        f"checks={checks} request_sha256={hashlib.sha256(encode(False)).hexdigest()} "
        f"response_sha256={hashlib.sha256(encode(True)).hexdigest()} "
        "stdlib_only=true live_authorization=false"
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    actions = parser.add_mutually_exclusive_group(required=True)
    actions.add_argument("--self-test", action="store_true")
    actions.add_argument("--write-request")
    actions.add_argument("--write-response")
    actions.add_argument("--verify-request")
    actions.add_argument("--verify-response")
    arguments = parser.parse_args()
    try:
        if arguments.self_test:
            self_test()
        elif arguments.write_request:
            write_exact(arguments.write_request, False)
        elif arguments.write_response:
            write_exact(arguments.write_response, True)
        elif arguments.verify_request:
            verify_exact(arguments.verify_request, False)
        else:
            verify_exact(arguments.verify_response, True)
        return 0
    except Exception as error:  # CLI must fail closed.
        print(f"P1WIRE_PYTHON_ORACLE FAIL {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
