#!/usr/bin/env python3
"""Static PE audit for the P1 M1 offline Easy verifier self-test."""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import struct
import sys
import tempfile

import pefile


EXPECTED_PEFILE_VERSION = "2024.8.26"
EXPECTED_WHOLE_IMAGE_SHA256 = (
    "E6BC0FEB88FA280A879072CCD48F39E9A0FF02238268580CFA9631F724BB65AF"
)
FORBIDDEN_IMPORTS = {
    "openprocess",
    "readprocessmemory",
    "writeprocessmemory",
    "virtualprotectex",
    "virtualallocex",
    "createremotethread",
    "queueuserapc",
    "setwindowshookexa",
    "setwindowshookexw",
    "sendinput",
    "mouse_event",
    "keybd_event",
    "createfilemappinga",
    "createfilemappingw",
    "openfilemappinga",
    "openfilemappingw",
    "mapviewoffile",
    "createnamedpipea",
    "createnamedpipew",
    "socket",
    "connect",
}
EXPECTED_IMPORTS = {
    "api-ms-win-crt-environment-l1-1-0.dll": {"__p__environ"},
    "api-ms-win-crt-heap-l1-1-0.dll": {"_set_new_mode", "free", "malloc"},
    "api-ms-win-crt-math-l1-1-0.dll": {"__setusermatherr"},
    "api-ms-win-crt-runtime-l1-1-0.dll": {
        "__p___argc",
        "__p___argv",
        "_cexit",
        "_configure_narrow_argv",
        "_crt_atexit",
        "_exit",
        "_initialize_narrow_environment",
        "_initterm",
        "_initterm_e",
        "_set_app_type",
        "_set_invalid_parameter_handler",
        "abort",
        "exit",
        "signal",
    },
    "api-ms-win-crt-stdio-l1-1-0.dll": {
        "__acrt_iob_func",
        "__p__commode",
        "__p__fmode",
        "__stdio_common_vfprintf",
        "__stdio_common_vsprintf",
        "fwrite",
    },
    "api-ms-win-crt-string-l1-1-0.dll": {"strcmp"},
    "kernel32.dll": {
        "DeleteCriticalSection",
        "EnterCriticalSection",
        "GetLastError",
        "InitializeCriticalSection",
        "LeaveCriticalSection",
        "SetUnhandledExceptionFilter",
        "Sleep",
        "TlsGetValue",
        "VirtualProtect",
        "VirtualQuery",
    },
}
EXPECTED_TLS_CALLBACK_TABLE_RVA = 0x00019B50
EXPECTED_PINNED_CRT_TLS_CALLBACKS = (
    {
        "rva": 0x00005310,
        "section": ".text",
        "code_size": 103,
        "code_sha256": "5930917131C1AD6578E6EBC09432995494DE873FE46E142762D056E8347834DF",
    },
    {
        "rva": 0x00005380,
        "section": ".text",
        "code_size": 34,
        "code_sha256": "FAEC737087E875DAC15FA8D500239B7179C5835BD6B63B5EB04E8F6880979BA2",
    },
)


def fail(message: str) -> None:
    raise RuntimeError(message)


def tls_callbacks(image: pefile.PE) -> list[int]:
    directory = getattr(image, "DIRECTORY_ENTRY_TLS", None)
    if directory is None or directory.struct.AddressOfCallBacks == 0:
        return []
    table_rva = directory.struct.AddressOfCallBacks - image.OPTIONAL_HEADER.ImageBase
    if table_rva < 0:
        fail("TLS callback table precedes ImageBase")
    result: list[int] = []
    for index in range(16):
        raw = image.get_data(table_rva + index * 4, 4)
        if len(raw) != 4:
            fail("TLS callback table is truncated")
        callback = struct.unpack("<I", raw)[0]
        if callback == 0:
            return result
        result.append(callback)
    fail("TLS callback table is not terminated within the hard bound")


def callback_section(image: pefile.PE, callback_rva: int) -> tuple[str, bool, bool] | None:
    for section in image.sections:
        span = max(section.Misc_VirtualSize, section.SizeOfRawData)
        if section.VirtualAddress <= callback_rva < section.VirtualAddress + span:
            name = section.Name.rstrip(b"\0").decode("ascii", errors="replace")
            executable = bool(section.Characteristics & 0x20000000)
            writable = bool(section.Characteristics & 0x80000000)
            return name, executable, writable
    return None


def verify_pinned_crt_tls_callbacks(image: pefile.PE) -> None:
    directory = getattr(image, "DIRECTORY_ENTRY_TLS", None)
    if directory is None or directory.struct.AddressOfCallBacks == 0:
        fail("pinned CRT TLS callback table is missing")
    table_rva = directory.struct.AddressOfCallBacks - image.OPTIONAL_HEADER.ImageBase
    if table_rva != EXPECTED_TLS_CALLBACK_TABLE_RVA:
        fail(
            "pinned CRT TLS callback table RVA changed: "
            f"actual={table_rva:#x} expected={EXPECTED_TLS_CALLBACK_TABLE_RVA:#x}"
        )
    callbacks = tls_callbacks(image)
    actual_rvas = tuple(
        callback - image.OPTIONAL_HEADER.ImageBase for callback in callbacks
    )
    expected_rvas = tuple(item["rva"] for item in EXPECTED_PINNED_CRT_TLS_CALLBACKS)
    if actual_rvas != expected_rvas:
        fail(
            "pinned CRT TLS callback RVA/order changed: "
            f"actual={[hex(item) for item in actual_rvas]} "
            f"expected={[hex(item) for item in expected_rvas]}"
        )
    if image.OPTIONAL_HEADER.AddressOfEntryPoint in actual_rvas:
        fail("entrypoint substitution is not valid pinned CRT TLS ownership evidence")
    for callback_rva, evidence in zip(actual_rvas, EXPECTED_PINNED_CRT_TLS_CALLBACKS):
        section = callback_section(image, callback_rva)
        if section is None:
            fail(f"pinned CRT TLS callback is outside all sections: {callback_rva:#x}")
        section_name, executable, writable = section
        if section_name != evidence["section"] or not executable or writable:
            fail(
                "pinned CRT TLS callback section ownership changed: "
                f"rva={callback_rva:#x} section={section_name!r} "
                f"executable={executable} writable={writable}"
            )
        code = image.get_data(callback_rva, evidence["code_size"])
        if len(code) != evidence["code_size"]:
            fail(f"pinned CRT TLS callback code is truncated: {callback_rva:#x}")
        if code[-3:] != b"\xC2\x0C\x00":
            fail(
                "pinned CRT TLS callback terminal ret 0x0c changed: "
                f"rva={callback_rva:#x} tail={code[-3:].hex().upper()}"
            )
        actual_sha256 = hashlib.sha256(code).hexdigest().upper()
        if actual_sha256 != evidence["code_sha256"]:
            fail(
                "pinned CRT TLS callback code identity changed: "
                f"rva={callback_rva:#x} actual={actual_sha256} "
                f"expected={evidence['code_sha256']}"
            )


def audit(
    path: str,
    emit_pass: bool = True,
    enforce_whole_image_identity: bool = True,
) -> None:
    if pefile.__version__ != EXPECTED_PEFILE_VERSION:
        fail(
            f"pefile identity rejected: actual={pefile.__version__} "
            f"required={EXPECTED_PEFILE_VERSION}"
        )
    raw_image = Path(path).read_bytes()
    actual_whole_image_sha256 = hashlib.sha256(raw_image).hexdigest().upper()
    if (
        enforce_whole_image_identity
        and actual_whole_image_sha256 != EXPECTED_WHOLE_IMAGE_SHA256
    ):
        fail(
            "whole offline image SHA-256 rejected: "
            f"actual={actual_whole_image_sha256} "
            f"expected={EXPECTED_WHOLE_IMAGE_SHA256}"
        )
    image = pefile.PE(data=raw_image, fast_load=False)
    try:
        if image.FILE_HEADER.Machine != 0x014C:
            fail(f"not x86 IMAGE_FILE_MACHINE_I386: {image.FILE_HEADER.Machine:#x}")
        if image.OPTIONAL_HEADER.Magic != 0x010B:
            fail(f"not PE32: {image.OPTIONAL_HEADER.Magic:#x}")
        if image.FILE_HEADER.Characteristics & 0x2000:
            fail("offline self-test unexpectedly has IMAGE_FILE_DLL")
        if image.OPTIONAL_HEADER.Subsystem != 3:
            fail(f"not a console executable: subsystem={image.OPTIONAL_HEADER.Subsystem}")
        if image.OPTIONAL_HEADER.DllCharacteristics & 0x0040 == 0:
            fail("ASLR/DYNAMIC_BASE missing")
        if image.OPTIONAL_HEADER.DllCharacteristics & 0x0100 == 0:
            fail("NX_COMPAT missing")
        if image.OPTIONAL_HEADER.DATA_DIRECTORY[5].Size == 0:
            fail("base relocations missing")
        if getattr(image, "DIRECTORY_ENTRY_EXPORT", None):
            fail("offline self-test must not export callable entry points")
        if getattr(image, "DIRECTORY_ENTRY_DELAY_IMPORT", None):
            fail("delay imports are not permitted")
        entrypoint = callback_section(
            image, image.OPTIONAL_HEADER.AddressOfEntryPoint
        )
        if entrypoint is None:
            fail("entrypoint is outside all PE sections")
        entrypoint_name, entrypoint_executable, entrypoint_writable = entrypoint
        if not entrypoint_executable or entrypoint_writable:
            fail(
                "entrypoint is not inside a non-writable executable section: "
                f"rva={image.OPTIONAL_HEADER.AddressOfEntryPoint:#x} "
                f"section={entrypoint_name!r} executable={entrypoint_executable} "
                f"writable={entrypoint_writable}"
            )
        verify_pinned_crt_tls_callbacks(image)

        actual_imports: dict[str, set[str]] = {}
        ordinal_imports: list[str] = []
        for descriptor in getattr(image, "DIRECTORY_ENTRY_IMPORT", []):
            module = descriptor.dll.decode("ascii").lower()
            if module in actual_imports:
                fail(f"duplicate import descriptor rejected: {module}")
            names: set[str] = set()
            for imported in descriptor.imports:
                if imported.name is None:
                    ordinal_imports.append(f"{module}!{imported.ordinal}")
                else:
                    names.add(imported.name.decode("ascii"))
            actual_imports[module] = names
        if ordinal_imports:
            fail(f"ordinal imports rejected: {ordinal_imports}")
        if actual_imports != EXPECTED_IMPORTS:
            missing_modules = sorted(set(EXPECTED_IMPORTS) - set(actual_imports))
            unexpected_modules = sorted(set(actual_imports) - set(EXPECTED_IMPORTS))
            symbol_differences = {
                module: {
                    "missing": sorted(EXPECTED_IMPORTS[module] - actual_imports.get(module, set())),
                    "unexpected": sorted(actual_imports.get(module, set()) - EXPECTED_IMPORTS[module]),
                }
                for module in sorted(set(EXPECTED_IMPORTS) & set(actual_imports))
                if EXPECTED_IMPORTS[module] != actual_imports[module]
            }
            fail(
                "exact import allowlist mismatch: "
                f"missing_modules={missing_modules} unexpected_modules={unexpected_modules} "
                f"symbol_differences={symbol_differences}"
            )
        imported_names = {
            name.lower() for names in actual_imports.values() for name in names
        }
        forbidden = sorted(imported_names & FORBIDDEN_IMPORTS)
        if forbidden:
            fail(f"live/process/IPC imports rejected: {forbidden}")

        for section in image.sections:
            executable = bool(section.Characteristics & 0x20000000)
            writable = bool(section.Characteristics & 0x80000000)
            if executable and writable:
                name = section.Name.rstrip(b"\0").decode("ascii", errors="replace")
                fail(f"RWX section rejected: {name}")
    finally:
        image.close()

    if emit_pass:
        print(
            "P1_NATIVE_EASY_PE_AUDIT PASS machine=x86 format=PE32 exports=0 "
            "delay_imports=0 pinned_crt_tls_callbacks=2 tls_rva_code_identity=exact "
            "duplicate_import_descriptors=0 rwx=0 exact_imports=1 process_access=0 "
            f"target_writes=0 ipc=0 live_target_code=0 whole_sha256={actual_whole_image_sha256}"
        )


def expect_fixture_rejected(path: Path, expected_text: str, name: str) -> None:
    try:
        audit(
            str(path),
            emit_pass=False,
            enforce_whole_image_identity=False,
        )
    except RuntimeError as error:
        if expected_text not in str(error):
            fail(
                f"malicious fixture {name} failed for the wrong reason: {error}"
            )
        return
    fail(f"malicious fixture {name} was accepted")


def run_malicious_fixture_selftests(exe_path: Path) -> None:
    original = exe_path.read_bytes()
    image = pefile.PE(data=original, fast_load=False)
    try:
        descriptors = list(getattr(image, "DIRECTORY_ENTRY_IMPORT", []))
        if len(descriptors) < 2:
            fail("fixture source does not have two import descriptors")
        duplicate_name_offset = descriptors[1].struct.get_file_offset() + 12
        first_name_rva = descriptors[0].struct.Name
        tls_table_rva = (
            image.DIRECTORY_ENTRY_TLS.struct.AddressOfCallBacks
            - image.OPTIONAL_HEADER.ImageBase
        )
        tls_table_offset = image.get_offset_from_rva(tls_table_rva)
        callback_rva = EXPECTED_PINNED_CRT_TLS_CALLBACKS[0]["rva"]
        callback_offset = image.get_offset_from_rva(callback_rva)
        callback_tail_offset = (
            callback_offset
            + EXPECTED_PINNED_CRT_TLS_CALLBACKS[0]["code_size"]
            - 1
        )
        entrypoint_va = (
            image.OPTIONAL_HEADER.ImageBase
            + image.OPTIONAL_HEADER.AddressOfEntryPoint
        )
        entrypoint_field_offset = image.OPTIONAL_HEADER.get_field_absolute_offset(
            "AddressOfEntryPoint"
        )
        data_sections = [
            section
            for section in image.sections
            if section.Name.rstrip(b"\0") == b".data"
        ]
        if len(data_sections) != 1:
            fail("fixture source does not have one .data section")
        data_rva = data_sections[0].VirtualAddress
    finally:
        image.close()

    with tempfile.TemporaryDirectory(
        prefix="p1-m1-malicious-pe-", dir=str(exe_path.parent)
    ) as temporary_root:
        root = Path(temporary_root)

        duplicate = bytearray(original)
        struct.pack_into("<I", duplicate, duplicate_name_offset, first_name_rva)
        duplicate_path = root / "duplicate-import-descriptor.exe"
        duplicate_path.write_bytes(duplicate)
        expect_fixture_rejected(
            duplicate_path, "duplicate import descriptor rejected", "duplicate-import"
        )

        entrypoint = bytearray(original)
        struct.pack_into("<I", entrypoint, tls_table_offset, entrypoint_va)
        entrypoint_path = root / "tls-entrypoint-substitution.exe"
        entrypoint_path.write_bytes(entrypoint)
        expect_fixture_rejected(
            entrypoint_path, "TLS callback RVA/order changed", "tls-entrypoint"
        )

        callback_code = bytearray(original)
        callback_code[callback_offset] ^= 1
        callback_code_path = root / "tls-callback-code-mutation.exe"
        callback_code_path.write_bytes(callback_code)
        expect_fixture_rejected(
            callback_code_path,
            "TLS callback code identity changed",
            "tls-code-mutation",
        )

        callback_tail = bytearray(original)
        callback_tail[callback_tail_offset] ^= 1
        callback_tail_path = root / "tls-callback-tail-mutation.exe"
        callback_tail_path.write_bytes(callback_tail)
        expect_fixture_rejected(
            callback_tail_path,
            "TLS callback terminal ret 0x0c changed",
            "tls-tail-mutation",
        )

        entrypoint_data = bytearray(original)
        struct.pack_into("<I", entrypoint_data, entrypoint_field_offset, data_rva)
        entrypoint_data_path = root / "entrypoint-data-substitution.exe"
        entrypoint_data_path.write_bytes(entrypoint_data)
        expect_fixture_rejected(
            entrypoint_data_path,
            "entrypoint is not inside a non-writable executable section",
            "entrypoint-data",
        )

    print(
        "P1_NATIVE_EASY_PE_AUDIT_SELFTEST PASS fixtures=5 "
        "duplicate_import=reject tls_entrypoint=reject tls_code_mutation=reject "
        "tls_tail_mutation=reject entrypoint_data=reject"
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    parser.add_argument("--self-test", action="store_true")
    arguments = parser.parse_args()
    try:
        audit(arguments.exe)
        if arguments.self_test:
            run_malicious_fixture_selftests(Path(arguments.exe))
        return 0
    except Exception as error:  # noqa: BLE001 - audit CLI must fail closed.
        print(f"P1_NATIVE_EASY_PE_AUDIT FAIL {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
