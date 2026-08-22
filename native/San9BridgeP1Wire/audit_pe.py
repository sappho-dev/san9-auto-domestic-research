#!/usr/bin/env python3
"""Offline PE boundary audit for the P1 wire self-test executable."""

from __future__ import annotations

import argparse
import hashlib
import struct
import sys

import pefile


EXPECTED_PEFILE_VERSION = "2024.8.26"
EXPECTED_PEFILE_SHA256 = "0d0eb68f0f169182613dc64b3ab50b20855508c1c1d2faafe8fccf23edb6a345"
EXPECTED_IMAGE_SHA256 = "354d9aa96d6cf62e3997556fc28c0a2b6d75ca505f2907763e3ac931b5a6530f"
EXPECTED_TLS_CALLBACK_RVAS = (0x00007740, 0x000077B0)
EXPECTED_TLS_CALLBACK_CODE_SIZE = 64
EXPECTED_TLS_CALLBACK_CODE_SHA256 = {
    0x00007740: "35adf8877b41225d87b6f3ef59bd2ff2788c6a9d7cf5898ebb94557fcb016252",
    0x000077B0: "f52cb08e437aaa4b955ff8d392c9e729cd1bb8c6c8296e4387dea22fadaeedac",
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
        "fclose",
        "fgetc",
        "fopen",
        "fread",
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


def fail(message: str) -> None:
    raise RuntimeError(message)


def imported_symbols(image: pefile.PE) -> dict[str, set[str]]:
    result: dict[str, set[str]] = {}
    for descriptor in getattr(image, "DIRECTORY_ENTRY_IMPORT", []):
        module = descriptor.dll.decode("ascii").lower()
        if module in result:
            fail(f"duplicate import descriptor rejected: {module}")
        names: set[str] = set()
        for imported in descriptor.imports:
            if imported.name is None:
                names.add(f"ordinal:{imported.ordinal}")
            else:
                names.add(imported.name.decode("ascii"))
        result[module] = names
    return result


def section_name(section: object) -> str:
    return section.Name.rstrip(b"\0").decode("ascii", errors="replace")


def section_for_range(image: pefile.PE, rva: int, size: int) -> object:
    if rva < 0 or size <= 0 or rva + size > image.OPTIONAL_HEADER.SizeOfImage:
        fail(f"RVA range outside image: rva={rva:#x} size={size:#x}")
    section = image.get_section_by_rva(rva)
    if section is None:
        fail(f"RVA is not owned by a section: {rva:#x}")
    section_start = section.VirtualAddress
    section_size = max(section.Misc_VirtualSize, section.SizeOfRawData)
    if rva < section_start or rva + size > section_start + section_size:
        fail(
            f"RVA range crosses section boundary: rva={rva:#x} size={size:#x} "
            f"section={section_name(section)}"
        )
    return section


def require_section_permissions(
    section: object,
    *,
    executable: bool,
    writable: bool,
    label: str,
) -> None:
    actual_executable = bool(section.Characteristics & 0x20000000)
    actual_writable = bool(section.Characteristics & 0x80000000)
    if actual_executable != executable or actual_writable != writable:
        fail(
            f"{label} section permissions rejected: section={section_name(section)} "
            f"executable={actual_executable} writable={actual_writable}"
        )


def audit_tls_callbacks(image: pefile.PE) -> int:
    """Allow only the frozen Zig/MinGW CRT TLS scaffold.

    The offline executable is expected to carry a four-byte CRT TLS cell and
    exactly two compiler-runtime callbacks.  User-defined TLS is separately
    forbidden by the source gate.  Absence, extra callbacks, pointer drift,
    writable callback code/table, malformed bounds, or changed TLS metadata
    all require an explicit audit update and therefore fail closed.
    """
    directory = image.OPTIONAL_HEADER.DATA_DIRECTORY[9]
    if directory.VirtualAddress == 0 or directory.Size != 24:
        fail(
            f"TLS directory identity rejected: rva={directory.VirtualAddress:#x} "
            f"size={directory.Size} expected_size=24"
        )
    tls_entry = getattr(image, "DIRECTORY_ENTRY_TLS", None)
    if tls_entry is None:
        fail("TLS directory is present but pefile did not parse it")

    tls = tls_entry.struct
    image_base = image.OPTIONAL_HEADER.ImageBase
    if tls.SizeOfZeroFill != 0 or tls.Characteristics != 0x00300000:
        fail(
            f"TLS metadata rejected: zero_fill={tls.SizeOfZeroFill} "
            f"characteristics={tls.Characteristics:#x}"
        )
    values = (
        tls.StartAddressOfRawData,
        tls.EndAddressOfRawData,
        tls.AddressOfIndex,
        tls.AddressOfCallBacks,
    )
    if any(value < image_base for value in values):
        fail("TLS structure contains a VA below ImageBase")
    raw_start = tls.StartAddressOfRawData - image_base
    raw_end = tls.EndAddressOfRawData - image_base
    index_rva = tls.AddressOfIndex - image_base
    callbacks_rva = tls.AddressOfCallBacks - image_base
    if raw_end - raw_start != 4:
        fail(f"TLS raw-data size rejected: {raw_end - raw_start}, expected 4")

    directory_section = section_for_range(image, directory.VirtualAddress, directory.Size)
    require_section_permissions(
        directory_section, executable=False, writable=False, label="TLS directory")
    raw_section = section_for_range(image, raw_start, raw_end - raw_start)
    require_section_permissions(raw_section, executable=False, writable=True, label="TLS raw data")
    index_section = section_for_range(image, index_rva, 4)
    require_section_permissions(index_section, executable=False, writable=True, label="TLS index")

    callback_values: list[int] = []
    terminated = False
    for index in range(16):
        entry_rva = callbacks_rva + index * 4
        table_section = section_for_range(image, entry_rva, 4)
        require_section_permissions(
            table_section, executable=False, writable=False, label="TLS callback table")
        entry_data = image.get_data(entry_rva, 4)
        if len(entry_data) != 4:
            fail(f"TLS callback entry is truncated at index {index}")
        callback_va = struct.unpack("<I", entry_data)[0]
        if callback_va == 0:
            terminated = True
            break
        if callback_va < image_base or callback_va & 3:
            fail(f"TLS callback VA invalid at index {index}: {callback_va:#x}")
        callback_rva = callback_va - image_base
        callback_section = section_for_range(image, callback_rva, 1)
        require_section_permissions(
            callback_section, executable=True, writable=False, label="TLS callback code")
        callback_values.append(callback_rva)
    if not terminated:
        fail("TLS callback table has no null terminator within 16 entries")
    if tuple(callback_values) != EXPECTED_TLS_CALLBACK_RVAS:
        fail(
            f"TLS callback set rejected: actual={[hex(value) for value in callback_values]} "
            f"expected={[hex(value) for value in EXPECTED_TLS_CALLBACK_RVAS]}"
        )
    for callback_rva in callback_values:
        callback_section = section_for_range(
            image, callback_rva, EXPECTED_TLS_CALLBACK_CODE_SIZE)
        require_section_permissions(
            callback_section,
            executable=True,
            writable=False,
            label="TLS callback code fingerprint",
        )
        callback_code = image.get_data(callback_rva, EXPECTED_TLS_CALLBACK_CODE_SIZE)
        if len(callback_code) != EXPECTED_TLS_CALLBACK_CODE_SIZE:
            fail(f"TLS callback code truncated: rva={callback_rva:#x}")
        actual_sha256 = hashlib.sha256(callback_code).hexdigest()
        expected_sha256 = EXPECTED_TLS_CALLBACK_CODE_SHA256.get(callback_rva)
        if actual_sha256 != expected_sha256:
            fail(
                f"TLS callback code identity rejected: rva={callback_rva:#x} "
                f"actual={actual_sha256} expected={expected_sha256}"
            )
    return len(callback_values)


def audit(path: str) -> None:
    if pefile.__version__ != EXPECTED_PEFILE_VERSION:
        fail(
            f"pefile identity rejected: actual={pefile.__version__} "
            f"required={EXPECTED_PEFILE_VERSION}"
        )
    if not isinstance(pefile.__file__, str) or not pefile.__file__.lower().endswith("pefile.py"):
        fail(f"pefile source path rejected: {pefile.__file__!r}")
    with open(pefile.__file__, "rb") as pefile_source:
        pefile_sha256 = hashlib.sha256(pefile_source.read()).hexdigest()
    if pefile_sha256 != EXPECTED_PEFILE_SHA256:
        fail(
            f"pefile source identity rejected: actual={pefile_sha256} "
            f"required={EXPECTED_PEFILE_SHA256}"
        )

    with open(path, "rb") as executable:
        image_sha256 = hashlib.sha256(executable.read()).hexdigest()
    if image_sha256 != EXPECTED_IMAGE_SHA256:
        fail(
            f"frozen offline image identity rejected: actual={image_sha256} "
            f"required={EXPECTED_IMAGE_SHA256}"
        )

    image = pefile.PE(path, fast_load=False)
    try:
        if image.FILE_HEADER.Machine != 0x014C:
            fail(f"not x86 IMAGE_FILE_MACHINE_I386: {image.FILE_HEADER.Machine:#x}")
        if image.OPTIONAL_HEADER.Magic != 0x010B:
            fail(f"not PE32: {image.OPTIONAL_HEADER.Magic:#x}")
        if image.FILE_HEADER.Characteristics & 0x2000:
            fail("self-test unexpectedly has IMAGE_FILE_DLL")
        if image.OPTIONAL_HEADER.Subsystem != 3:
            fail(f"not a console executable: subsystem={image.OPTIONAL_HEADER.Subsystem}")
        if image.OPTIONAL_HEADER.DllCharacteristics & 0x0040 == 0:
            fail("ASLR/DYNAMIC_BASE missing")
        if image.OPTIONAL_HEADER.DllCharacteristics & 0x0100 == 0:
            fail("NX_COMPAT missing")
        entrypoint = image.OPTIONAL_HEADER.AddressOfEntryPoint
        entrypoint_section = section_for_range(image, entrypoint, 1)
        require_section_permissions(
            entrypoint_section,
            executable=True,
            writable=False,
            label="entrypoint",
        )
        if getattr(image, "DIRECTORY_ENTRY_EXPORT", None):
            fail("offline self-test must not export callable entry points")
        if getattr(image, "DIRECTORY_ENTRY_DELAY_IMPORT", None):
            fail("delay imports are not permitted")

        tls_callback_count = audit_tls_callbacks(image)

        actual = imported_symbols(image)
        if actual != EXPECTED_IMPORTS:
            missing_modules = sorted(set(EXPECTED_IMPORTS) - set(actual))
            unexpected_modules = sorted(set(actual) - set(EXPECTED_IMPORTS))
            symbol_differences = {
                module: {
                    "missing": sorted(EXPECTED_IMPORTS[module] - actual.get(module, set())),
                    "unexpected": sorted(actual.get(module, set()) - EXPECTED_IMPORTS[module]),
                }
                for module in sorted(set(EXPECTED_IMPORTS) & set(actual))
                if EXPECTED_IMPORTS[module] != actual[module]
            }
            fail(
                "import allowlist mismatch: "
                f"missing_modules={missing_modules} "
                f"unexpected_modules={unexpected_modules} "
                f"symbol_differences={symbol_differences}"
            )

        for section in image.sections:
            executable = bool(section.Characteristics & 0x20000000)
            writable = bool(section.Characteristics & 0x80000000)
            if executable and writable:
                name = section.Name.rstrip(b"\0").decode("ascii", errors="replace")
                fail(f"RWX section rejected: {name}")
    finally:
        image.close()

    print(
        "P1WIRE_PE_AUDIT PASS "
        f"machine=x86 format=PE32 modules={len(EXPECTED_IMPORTS)} "
        f"exports=0 delay_imports=0 rwx=0 tls_callbacks={tls_callback_count} "
        "tls_policy=frozen-zig-crt-only live_authorization=false"
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    arguments = parser.parse_args()
    try:
        audit(arguments.exe)
        return 0
    except Exception as error:  # noqa: BLE001 - CLI must fail closed.
        print(f"P1WIRE_PE_AUDIT FAIL {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
