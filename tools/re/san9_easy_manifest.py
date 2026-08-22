#!/usr/bin/env python3
"""Build and fail-closed validate the San9PKEasy 1.1.0.5 ownership manifest.

This tool is deliberately offline-only.  It reads three PE files from disk and
does not enumerate, open, inspect, start, or modify a process.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import struct
import sys
from pathlib import Path
from typing import Any, Iterable

import capstone
import pefile


SCHEMA = "san9-easy-ownership-manifest/v1"
TOOL_VERSION = "1.0.0"
EXPECTED_PEFILE = "2024.8.26"
EXPECTED_CAPSTONE = "5.0.7"

DEFAULT_GAME = Path(r"D:\三国志9\10101749\San9PK.exe")
DEFAULT_LOADER = Path(r"D:\三国志9\10101749\San9PKEasy.exe")
DEFAULT_EASY = Path(r"D:\三国志9\10101749\Easy.dll")
DEFAULT_MANIFEST = Path(__file__).resolve().parents[2] / "docs" / "easy-compatibility-manifest.json"

EXPECTED_BINARIES = {
    "San9PK.exe": {
        "size": 2_636_800,
        "sha256": "D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028",
        "image_base": 0x00400000,
        "machine": 0x014C,
    },
    "San9PKEasy.exe": {
        "size": 24_576,
        "sha256": "CDACA1477EDB5A3BD79BDA8540E19837FC3F9170965D972E8E48FB24CAE21C07",
        "image_base": 0x00400000,
        "machine": 0x014C,
    },
    "Easy.dll": {
        "size": 32_768,
        "sha256": "E8BA3A603F6B0E7AF8A246DA0FE5CBDBA86AD9B77C5A507DD86159EF6C74A3F0",
        "image_base": 0x10000000,
        "machine": 0x014C,
    },
}

# The first 30 loader descriptors are regular 12-byte records at loader RVA
# 0x4054.  The final two are the six-byte indirect-call replacements.
REDIRECT_DESCRIPTOR_RVAS = [0x4054 + index * 0x0C for index in range(30)] + [0x41CC, 0x41D8]
REDIRECT_LENGTHS = [5] * 30 + [6, 6]
REDIRECT_TYPES = ["jmp_rel32"] + ["call_rel32"] * 29 + ["call_rel32_nop", "call_rel32_nop"]

# Offsets into Easy's table at RVA 0x6390.  They are recovered by following
# each installer block's [ESP+N] source; duplicates are intentional.
HOOK_TABLE_OFFSETS = [
    0x04, 0x08, 0x08, 0x0C, 0x10, 0x14, 0x18, 0x1C,
    0x20, 0x24, 0x28, 0x2C, 0x30, 0x34, 0x34, 0x38,
    0x38, 0x3C, 0x3C, 0x40, 0x40, 0x44, 0x44, 0x48,
    0x48, 0x4C, 0x50, 0x54, 0x58, 0x5C, 0x60, 0x64,
]

INSTALL_REDIRECT_CALLS = [
    0x401D02, 0x401D2E, 0x401D5A, 0x401D86, 0x401DB2, 0x401DDE,
    0x401E0A, 0x401E36, 0x401E62, 0x401E8E, 0x401EBA, 0x401EE6,
    0x401F12, 0x401F3E, 0x401F6A, 0x401F96, 0x401FC2, 0x401FEE,
    0x40201A, 0x402046, 0x402072, 0x40209E, 0x4020CA, 0x4020F6,
    0x402122, 0x40214E, 0x40217A, 0x4021A6, 0x4021D2, 0x4021FA,
    0x402222, 0x40224A,
]

RESTORE_REDIRECT_CALLS = [
    0x40244E, 0x402464, 0x40247A, 0x40248F, 0x4024A5, 0x4024BB,
    0x4024D0, 0x4024E6, 0x4024FC, 0x402511, 0x402527, 0x40253D,
    0x402552, 0x402568, 0x40257E, 0x402593, 0x4025A9, 0x4025BF,
    0x4025D4, 0x4025EA, 0x402600, 0x402615, 0x40262B, 0x402641,
    0x402656, 0x40266C, 0x402682, 0x402697, 0x4026AD, 0x4026C3,
    0x4026D8, 0x4026EE,
]

REDIRECT_PURPOSES = [
    "multiplayer_exploration.reset_selection",
    "multiplayer_exploration.apply_each_selected_person",
    "multiplayer_exploration.apply_each_selected_person",
    "advisor_recommendation.selector_wrapper",
    "advisor_recommendation.selector_wrapper",
    "advisor_recommendation.selector_wrapper",
    "advisor_recommendation.selector_wrapper",
    "advisor_recommendation.selector_wrapper",
    "advisor_recommendation.selector_wrapper",
    "advisor_recommendation.selector_wrapper",
    "advisor_recommendation.selector_wrapper",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "advisor_recommendation.candidate_collector",
    "keyboard_shortcuts.window_message_filter",
    "keyboard_shortcuts.modal_result_override",
    "keyboard_shortcuts.modal_result_override",
    "unit_formation_appearance.index_remap",
    "max_corps_food.call_replacement",
    "max_facility_wounded.call_replacement",
]

WPM_IMPORT_XREFS = [0x401787, 0x401B47, 0x401CE3, 0x402351, 0x402438]
WPM_GROUPS = {
    "bootstrap_remote_dll_path": [0x401787],
    "install_auxiliary": [0x401B5D, 0x401B76, 0x401B90, 0x401BAA],
    "install_redirects": INSTALL_REDIRECT_CALLS,
    "runtime_training_pair": [0x402362, 0x4023A7],
    "restore_redirects": RESTORE_REDIRECT_CALLS,
    "restore_auxiliary": [0x402704, 0x402719, 0x40272F, 0x402745, 0x40275A],
}
VIRTUAL_PROTECT_XREFS = [0x401B34, 0x402772]

IDLE_CANDIDATES = [
    ("app_idle_slot", 0x00604DF4, 4),
    ("original_idle_function", 0x00434100, 1),
    ("idle_caller_return", 0x005C5D11, 1),
]


class ManifestError(RuntimeError):
    pass


def _hx(value: int, width: int = 8) -> str:
    return f"0x{value:0{width}X}"


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def _read_u32(pe: pefile.PE, rva: int) -> int:
    raw = pe.get_data(rva, 4)
    if len(raw) != 4:
        raise ManifestError(f"short PE read at RVA {_hx(rva)}")
    return struct.unpack("<I", raw)[0]


def _validate_blob_identity(name: str, data: bytes) -> tuple[int, str]:
    expected = EXPECTED_BINARIES[name]
    size = len(data)
    digest = hashlib.sha256(data).hexdigest().upper()
    if size != expected["size"]:
        raise ManifestError(f"{name}: size {size} != {expected['size']}")
    if digest != expected["sha256"]:
        raise ManifestError(f"{name}: SHA-256 {digest} != {expected['sha256']}")
    return size, digest


def _file_identity(name: str, path: Path, data: bytes, pe: pefile.PE) -> dict[str, Any]:
    expected = EXPECTED_BINARIES[name]
    size, digest = _validate_blob_identity(name, data)
    if pe.FILE_HEADER.Machine != expected["machine"]:
        raise ManifestError(f"{name}: machine {_hx(pe.FILE_HEADER.Machine, 4)} is not I386")
    if pe.OPTIONAL_HEADER.Magic != 0x10B:
        raise ManifestError(f"{name}: not PE32")
    if pe.OPTIONAL_HEADER.ImageBase != expected["image_base"]:
        raise ManifestError(f"{name}: unexpected image base {_hx(pe.OPTIONAL_HEADER.ImageBase)}")
    return {
        "path_evidence": str(path.resolve()),
        "size": size,
        "sha256": digest,
        "machine": "IMAGE_FILE_MACHINE_I386",
        "pe_kind": "PE32",
        "preferred_image_base": _hx(pe.OPTIONAL_HEADER.ImageBase),
        "size_of_image": _hx(pe.OPTIONAL_HEADER.SizeOfImage),
    }


def _imports(pe: pefile.PE) -> dict[str, tuple[int, str]]:
    result: dict[str, tuple[int, str]] = {}
    for descriptor in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []):
        module = descriptor.dll.decode("ascii")
        for imported in descriptor.imports:
            if imported.name:
                result[imported.name.decode("ascii")] = (imported.address, module)
    return result


def _text_instructions(pe: pefile.PE) -> list[capstone.CsInsn]:
    section = next((item for item in pe.sections if item.Name.rstrip(b"\0") == b".text"), None)
    if section is None:
        raise ManifestError("loader has no .text section")
    engine = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    engine.detail = True
    return list(engine.disasm(section.get_data(), pe.OPTIONAL_HEADER.ImageBase + section.VirtualAddress))


def _iat_xrefs(instructions: Iterable[capstone.CsInsn], iat_va: int) -> list[int]:
    found: list[int] = []
    for instruction in instructions:
        for operand in instruction.operands:
            if operand.type == capstone.x86.X86_OP_MEM and operand.mem.disp == iat_va:
                found.append(instruction.address)
                break
    return found


def _call_reg_sites(
    instructions: Iterable[capstone.CsInsn], start: int, end: int, register_name: str
) -> list[int]:
    return [
        item.address
        for item in instructions
        if start <= item.address < end and item.mnemonic == "call" and item.op_str == register_name
    ]


def _memory_displacements(instructions: Iterable[capstone.CsInsn]) -> set[int]:
    result: set[int] = set()
    for instruction in instructions:
        for operand in instruction.operands:
            if operand.type == capstone.x86.X86_OP_MEM:
                result.add(operand.mem.disp & 0xFFFFFFFF)
    return result


def _push_immediates(instructions: Iterable[capstone.CsInsn]) -> set[int]:
    result: set[int] = set()
    for instruction in instructions:
        if instruction.mnemonic != "push" or len(instruction.operands) != 1:
            continue
        operand = instruction.operands[0]
        if operand.type == capstone.x86.X86_OP_IMM:
            result.add(operand.imm & 0xFFFFFFFF)
    return result


def _verify_redirect_source_blocks(
    loader: pefile.PE, instructions: list[capstone.CsInsn]
) -> dict[str, int]:
    base = loader.OPTIONAL_HEADER.ImageBase
    for index, callsite in enumerate(INSTALL_REDIRECT_CALLS):
        start = 0x401CDA if index == 0 else INSTALL_REDIRECT_CALLS[index - 1] + 2
        block = [item for item in instructions if start <= item.address <= callsite]
        memory = _memory_displacements(block)
        pushes = _push_immediates(block)
        descriptor_va = base + REDIRECT_DESCRIPTOR_RVAS[index]
        expected_stack_displacement = HOOK_TABLE_OFFSETS[index] + 0x0C
        stack_reads = {
            operand.mem.disp
            for item in block
            if item.mnemonic == "mov"
            for operand in item.operands
            if operand.type == capstone.x86.X86_OP_MEM
            and operand.mem.base == capstone.x86.X86_REG_ESP
        }
        scratch_rva = 0x404C if index == 0 else (0x403C if index >= 30 else 0x4034)
        if descriptor_va not in memory:
            raise ManifestError(f"redirect {index}: installer descriptor source not found")
        if expected_stack_displacement not in stack_reads:
            raise ManifestError(f"redirect {index}: Easy table stack source changed")
        if REDIRECT_LENGTHS[index] not in pushes or base + scratch_rva not in pushes:
            raise ManifestError(f"redirect {index}: installer length/template source changed")
        terminal = next((item for item in block if item.address == callsite), None)
        if terminal is None or terminal.mnemonic != "call" or terminal.op_str != "edi":
            raise ManifestError(f"redirect {index}: installer WPM call changed")

    for index, callsite in enumerate(RESTORE_REDIRECT_CALLS):
        start = 0x402430 if index == 0 else RESTORE_REDIRECT_CALLS[index - 1] + 2
        block = [item for item in instructions if start <= item.address <= callsite]
        memory = _memory_displacements(block)
        pushes = _push_immediates(block)
        descriptor_rva = REDIRECT_DESCRIPTOR_RVAS[index]
        if base + descriptor_rva not in memory:
            raise ManifestError(f"redirect {index}: restore target source not found")
        if base + descriptor_rva + 4 not in pushes or REDIRECT_LENGTHS[index] not in pushes:
            raise ManifestError(f"redirect {index}: restore byte source/length changed")
        terminal = next((item for item in block if item.address == callsite), None)
        if terminal is None or terminal.mnemonic != "call" or terminal.op_str != "edi":
            raise ManifestError(f"redirect {index}: restore WPM call changed")
    return {"install_source_blocks_verified": 32, "restore_source_blocks_verified": 32}


def _verify_loader_write_closure(loader: pefile.PE) -> dict[str, Any]:
    imports = _imports(loader)
    if "WriteProcessMemory" not in imports or "VirtualProtectEx" not in imports:
        raise ManifestError("loader is missing required memory-write imports")
    wpm_va, wpm_module = imports["WriteProcessMemory"]
    protect_va, protect_module = imports["VirtualProtectEx"]
    if wpm_module.upper() != "KERNEL32.DLL" or protect_module.upper() != "KERNEL32.DLL":
        raise ManifestError("memory-write APIs are not imported from KERNEL32.dll")
    instructions = _text_instructions(loader)
    actual_wpm_xrefs = _iat_xrefs(instructions, wpm_va)
    if actual_wpm_xrefs != WPM_IMPORT_XREFS:
        raise ManifestError(
            f"WriteProcessMemory IAT xrefs changed: {[ _hx(x) for x in actual_wpm_xrefs ]}"
        )
    actual_protect_xrefs = _iat_xrefs(instructions, protect_va)
    if actual_protect_xrefs != VIRTUAL_PROTECT_XREFS:
        raise ManifestError(
            f"VirtualProtectEx IAT xrefs changed: {[ _hx(x) for x in actual_protect_xrefs ]}"
        )

    register_groups = {
        "install_auxiliary": _call_reg_sites(instructions, 0x401B47, 0x401BB0, "edi"),
        "install_redirects": _call_reg_sites(instructions, 0x401CE3, 0x402250, "edi"),
        "runtime_training_pair": _call_reg_sites(instructions, 0x402351, 0x4023D3, "edi"),
        "restore_all": _call_reg_sites(instructions, 0x402438, 0x40275C, "edi"),
    }
    expected_register_groups = {
        "install_auxiliary": WPM_GROUPS["install_auxiliary"],
        "install_redirects": WPM_GROUPS["install_redirects"],
        "runtime_training_pair": WPM_GROUPS["runtime_training_pair"],
        "restore_all": WPM_GROUPS["restore_redirects"] + WPM_GROUPS["restore_auxiliary"],
    }
    if register_groups != expected_register_groups:
        raise ManifestError("WriteProcessMemory register-call closure changed")
    redirect_source_checks = _verify_redirect_source_blocks(loader, instructions)

    all_static_calls = [site for sites in WPM_GROUPS.values() for site in sites]
    if len(all_static_calls) != 76 or len(set(all_static_calls)) != 76:
        raise ManifestError("internal WPM accounting invariant failed")
    return {
        "write_process_memory": {
            "import_module": wpm_module,
            "iat_va": _hx(wpm_va),
            "iat_rva": _hx(wpm_va - loader.OPTIONAL_HEADER.ImageBase),
            "iat_reference_sites": [_hx(item) for item in actual_wpm_xrefs],
            "groups": {
                name: {"count": len(sites), "call_sites": [_hx(item) for item in sites]}
                for name, sites in WPM_GROUPS.items()
            },
            "static_call_site_count": len(all_static_calls),
            "ephemeral_non_ownership_calls": 1,
            "unique_owned_physical_points": 38,
            "redirect_source_checks": redirect_source_checks,
        },
        "virtual_protect_ex": {
            "import_module": protect_module,
            "iat_va": _hx(protect_va),
            "iat_rva": _hx(protect_va - loader.OPTIONAL_HEADER.ImageBase),
            "iat_reference_sites": [_hx(item) for item in actual_protect_xrefs],
        },
    }


def _easy_table(easy: pefile.PE) -> tuple[int, dict[int, int], list[dict[str, str]]]:
    code = easy.get_data(0x1000, 0x110)
    if len(code) != 0x110:
        raise ManifestError("Easy initializer is truncated")
    records: list[tuple[int, int, int]] = []
    cursor = 0
    while cursor + 10 <= len(code) and code[cursor : cursor + 2] == b"\xC7\x05":
        destination, value = struct.unpack_from("<II", code, cursor + 2)
        records.append((0x1000 + cursor, destination, value))
        cursor += 10
    if len(records) != 26 or code[cursor] != 0xC3:
        raise ManifestError("Easy table initializer shape changed")
    table_va = records[0][1]
    if records[0][2] != 0x12345678:
        raise ManifestError("Easy table sentinel changed")
    expected_destinations = [table_va + index * 4 for index in range(26)]
    if [record[1] for record in records] != expected_destinations:
        raise ManifestError("Easy table destinations are not contiguous")
    table_rva = table_va - easy.OPTIONAL_HEADER.ImageBase
    if table_rva != 0x6390:
        raise ManifestError(f"Easy table RVA changed to {_hx(table_rva)}")
    targets: dict[int, int] = {}
    evidence: list[dict[str, str]] = []
    for initializer_rva, destination, value in records[1:]:
        table_offset = destination - table_va
        target_rva = value - easy.OPTIONAL_HEADER.ImageBase
        if not (0x1000 <= target_rva < easy.OPTIONAL_HEADER.SizeOfImage):
            raise ManifestError("Easy hook target is outside Easy.dll")
        targets[table_offset] = target_rva
        evidence.append(
            {
                "initializer_rva": _hx(initializer_rva),
                "table_offset": _hx(table_offset, 2),
                "target_rva": _hx(target_rva),
            }
        )
    if sorted(targets) != list(range(4, 0x68, 4)):
        raise ManifestError("Easy hook table offsets changed")
    return table_rva, targets, evidence


def _installed_bytes(instruction_type: str, target_va: int, easy_base: int, easy_rva: int) -> bytes:
    opcode = 0xE9 if instruction_type == "jmp_rel32" else 0xE8
    if not all(0 <= value <= 0xFFFFFFFF for value in (target_va, easy_base, easy_rva)):
        raise ManifestError("rel32 operands must be UInt32")
    destination = easy_base + easy_rva
    if destination > 0xFFFFFFFF:
        raise ManifestError("Easy base plus target RVA exceeds x86 address space")
    # x86-32 EIP and rel32 addition are modulo 2^32.  The loader's 32-bit SUB
    # produces exactly this bit pattern even when the mathematical difference
    # is outside signed-int32 (for example an Easy base of 0x90000000).
    displacement_bits = (destination - ((target_va + 5) & 0xFFFFFFFF)) & 0xFFFFFFFF
    result = bytes([opcode]) + struct.pack("<I", displacement_bits)
    if instruction_type == "call_rel32_nop":
        result += b"\x90"
    return result


def _redirects(
    game: pefile.PE, loader: pefile.PE, easy: pefile.PE, table_targets: dict[int, int]
) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    game_base = game.OPTIONAL_HEADER.ImageBase
    preferred_easy_base = easy.OPTIONAL_HEADER.ImageBase
    for index, (descriptor_rva, length, kind, table_offset) in enumerate(
        zip(REDIRECT_DESCRIPTOR_RVAS, REDIRECT_LENGTHS, REDIRECT_TYPES, HOOK_TABLE_OFFSETS)
    ):
        target_va = _read_u32(loader, descriptor_rva)
        target_rva = target_va - game_base
        original = loader.get_data(descriptor_rva + 4, length)
        disk = game.get_data(target_rva, length)
        if len(original) != length or original != disk:
            raise ManifestError(f"redirect {index}: loader original bytes do not match San9PK.exe")
        easy_target_rva = table_targets.get(table_offset)
        if easy_target_rva is None:
            raise ManifestError(f"redirect {index}: missing Easy table offset {_hx(table_offset, 2)}")
        preferred = _installed_bytes(kind, target_va, preferred_easy_base, easy_target_rva)
        if len(preferred) != length:
            raise ManifestError(f"redirect {index}: installed length mismatch")
        result.append(
            {
                "id": f"redirect-{index:02d}",
                "owner": "Easy.dll",
                "target_module": "San9PK.exe",
                "target_va": _hx(target_va),
                "target_rva": _hx(target_rva),
                "instruction_type": kind,
                "length": length,
                "original_hex": original.hex().upper(),
                "installed_preferred_base_hex": preferred.hex().upper(),
                "installed_template": {
                    "encoding": "x86_rel32",
                    "opcode_hex": f"{preferred[0]:02X}",
                    "target_module": "Easy.dll",
                    "target_rva": _hx(easy_target_rva),
                    "trailing_hex": "90" if kind == "call_rel32_nop" else "",
                },
                "accepted_runtime_states": ["original", "installed_for_exact_easy_base"],
                "known_purpose": REDIRECT_PURPOSES[index],
                "source": {
                    "loader_descriptor_rva": _hx(descriptor_rva),
                    "easy_table_rva": _hx(0x6390),
                    "easy_table_offset": _hx(table_offset, 2),
                    "install_wpm_call_va": _hx(INSTALL_REDIRECT_CALLS[index]),
                    "restore_wpm_call_va": _hx(RESTORE_REDIRECT_CALLS[index]),
                },
            }
        )
    return result


def _game_bytes(game: pefile.PE, va: int, length: int) -> bytes:
    raw = game.get_data(va - game.OPTIONAL_HEADER.ImageBase, length)
    if len(raw) != length:
        raise ManifestError(f"short game read at VA {_hx(va)}")
    return raw


def _auxiliaries(game: pefile.PE, loader: pefile.PE, easy: pefile.PE) -> list[dict[str, Any]]:
    # Values and addresses are read from the loader's own data records, then
    # cross-checked against the exact game file.
    train_a = _read_u32(loader, 0x41E8)
    train_b = _read_u32(loader, 0x41EC)
    train_original = loader.get_data(0x41F0, 1)
    cap_a = _read_u32(loader, 0x41BC)
    cap_b = _read_u32(loader, 0x41C0)
    cap_original = loader.get_data(0x41C4, 4)
    cap_installed = loader.get_data(0x41C8, 4)
    ai_target = _read_u32(loader, 0x41F4)
    ai_original = loader.get_data(0x41F8, 1)
    if train_original != b"\x32" or cap_original != struct.pack("<I", 1_000_000):
        raise ManifestError("loader auxiliary original constants changed")
    if cap_installed != struct.pack("<I", 10_000_000) or ai_original != b"\xE4":
        raise ManifestError("loader auxiliary install constants changed")
    if loader.get_data(0x4480, 1) != b"\x00" or loader.get_data(0x447C, 1) != b"\x00":
        raise ManifestError("loader zero-state auxiliary storage changed")
    for target, expected in [
        (train_a, train_original), (train_b, train_original),
        (cap_a, cap_original), (cap_b, cap_original), (ai_target, ai_original),
    ]:
        if _game_bytes(game, target, len(expected)) != expected:
            raise ManifestError(f"auxiliary original mismatch at {_hx(target)}")

    game_base = game.OPTIONAL_HEADER.ImageBase
    entries: list[dict[str, Any]] = []
    for suffix, target, install_call, restore_call in [
        ("a", train_a, 0x402362, 0x402704),
        ("b", train_b, 0x4023A7, 0x402719),
    ]:
        entries.append(
            {
                "id": f"aux-child-training-{suffix}",
                "owner": "Easy.dll",
                "target_module": "San9PK.exe",
                "target_va": _hx(target),
                "target_rva": _hx(target - game_base),
                "write_type": "immediate_u8",
                "length": 1,
                "original_hex": "32",
                "installed_hex_states": ["32", "00"],
                "dynamic_two_state": {
                    "kind": "paired_checkbox_state",
                    "pair_id": "allow_child_training",
                    "allowed_pairs": ["32/32", "00/00"],
                    "split_pair_is_invalid": True,
                },
                "accepted_runtime_states": ["32", "00"],
                "known_purpose": "allow_child_training.threshold",
                "source": {
                    "loader_address_rva": _hx(0x41E8 if suffix == "a" else 0x41EC),
                    "loader_original_rva": _hx(0x41F0),
                    "runtime_wpm_call_va": _hx(install_call),
                    "restore_wpm_call_va": _hx(restore_call),
                },
            }
        )
    for suffix, target, install_call, restore_call in [
        ("a", cap_a, 0x401B90, 0x40272F),
        ("b", cap_b, 0x401BAA, 0x402745),
    ]:
        entries.append(
            {
                "id": f"aux-max-corps-food-{suffix}",
                "owner": "Easy.dll",
                "target_module": "San9PK.exe",
                "target_va": _hx(target),
                "target_rva": _hx(target - game_base),
                "write_type": "immediate_u32_le",
                "length": 4,
                "original_hex": cap_original.hex().upper(),
                "installed_hex": cap_installed.hex().upper(),
                "accepted_runtime_states": [cap_original.hex().upper(), cap_installed.hex().upper()],
                "known_purpose": "max_corps_food.literal_1000000_to_10000000",
                "source": {
                    "loader_address_rva": _hx(0x41BC if suffix == "a" else 0x41C0),
                    "loader_original_rva": _hx(0x41C4),
                    "loader_installed_rva": _hx(0x41C8),
                    "install_wpm_call_va": _hx(install_call),
                    "restore_wpm_call_va": _hx(restore_call),
                },
            }
        )
    entries.append(
        {
            "id": "aux-ai-counter-barbarians",
            "owner": "Easy.dll",
            "target_module": "San9PK.exe",
            "target_va": _hx(ai_target),
            "target_rva": _hx(ai_target - game_base),
            "write_type": "short_branch_displacement_u8",
            "length": 1,
            "original_hex": ai_original.hex().upper(),
            "installed_hex": "00",
            "accepted_runtime_states": [ai_original.hex().upper(), "00"],
            "known_purpose": "computer_can_counter_barbarian_forces",
            "source": {
                "loader_address_rva": _hx(0x41F4),
                "loader_original_rva": _hx(0x41F8),
                "loader_installed_zero_rva": _hx(0x4480),
                "install_wpm_call_va": _hx(0x401B76),
                "restore_wpm_call_va": _hx(0x40275A),
            },
        }
    )
    entries.append(
        {
            "id": "aux-easy-game-hwnd-slot",
            "owner": "Easy.dll",
            "target_module": "Easy.dll",
            "target_rva": _hx(0x6450),
            "write_type": "runtime_hwnd_u32_le",
            "length": 4,
            "original_hex": easy.get_data(0x6450, 4).hex().upper(),
            "installed_template": "exact_bound_game_hwnd_u32_le",
            "accepted_runtime_states": ["zero_before_install", "exact_bound_game_hwnd"],
            "known_purpose": "keyboard_shortcuts.game_window_handle",
            "source": {
                "easy_table_rva": _hx(0x6390),
                "loader_addend": _hx(0xC0),
                "install_wpm_call_va": _hx(0x401B5D),
                "restore_wpm_call_va": None,
            },
        }
    )
    if len(entries) != 6:
        raise ManifestError("auxiliary count is not six")
    return entries


def _interval(record: dict[str, Any]) -> tuple[int, int] | None:
    if record.get("target_module") != "San9PK.exe":
        return None
    start = int(record["target_va"], 16)
    return start, start + int(record["length"])


def _conflicts(redirects: list[dict[str, Any]], auxiliaries: list[dict[str, Any]]) -> dict[str, Any]:
    owned = [interval for item in redirects + auxiliaries if (interval := _interval(item)) is not None]
    candidate_rows: list[dict[str, Any]] = []
    for name, start, length in IDLE_CANDIDATES:
        end = start + length
        overlaps = [(_hx(a), _hx(b)) for a, b in owned if max(start, a) < min(end, b)]
        candidate_rows.append(
            {
                "name": name,
                "va": _hx(start),
                "length_used_for_overlap_check": length,
                "direct_overlap_with_easy_owned_write": bool(overlaps),
                "overlaps": [{"start": a, "end_exclusive": b} for a, b in overlaps],
            }
        )
    page_start = 0x0061C000
    page_end = page_start + 0x1000
    page_overlaps = [
        name for name, start, length in IDLE_CANDIDATES if max(start, page_start) < min(start + length, page_end)
    ]
    if any(row["direct_overlap_with_easy_owned_write"] for row in candidate_rows) or page_overlaps:
        raise ManifestError("current idle candidates directly conflict with Easy ownership")
    return {
        "scope_warning": "No direct address overlap is not a live coexistence proof.",
        "idle_candidate_rows": candidate_rows,
        "easy_writable_page_overlap_with_idle_candidates": page_overlaps,
    }


def build_manifest(game_path: Path, loader_path: Path, easy_path: Path) -> dict[str, Any]:
    if pefile.__version__ != EXPECTED_PEFILE or capstone.__version__ != EXPECTED_CAPSTONE:
        raise ManifestError(
            f"dependency drift: pefile={pefile.__version__}, capstone={capstone.__version__}"
        )
    paths = {"San9PK.exe": game_path, "San9PKEasy.exe": loader_path, "Easy.dll": easy_path}
    for name, path in paths.items():
        if not path.is_file():
            raise ManifestError(f"{name}: file not found: {path}")
    # Parse immutable byte snapshots instead of retaining Windows file handles.
    # Besides making the analysis race-free at file granularity, this lets the
    # mutation self-test restore its temporary copies immediately.
    game_data = game_path.read_bytes()
    loader_data = loader_path.read_bytes()
    easy_data = easy_path.read_bytes()
    # Identity is rejected before a mutated blob reaches PE/disassembly logic.
    _validate_blob_identity("San9PK.exe", game_data)
    _validate_blob_identity("San9PKEasy.exe", loader_data)
    _validate_blob_identity("Easy.dll", easy_data)
    game = pefile.PE(data=game_data, fast_load=False)
    loader = pefile.PE(data=loader_data, fast_load=False)
    easy = pefile.PE(data=easy_data, fast_load=False)
    identities = {
        "San9PK.exe": _file_identity("San9PK.exe", game_path, game_data, game),
        "San9PKEasy.exe": _file_identity("San9PKEasy.exe", loader_path, loader_data, loader),
        "Easy.dll": _file_identity("Easy.dll", easy_path, easy_data, easy),
    }
    loader_audit = _verify_loader_write_closure(loader)
    table_rva, table_targets, table_evidence = _easy_table(easy)
    redirects = _redirects(game, loader, easy, table_targets)
    auxiliaries = _auxiliaries(game, loader, easy)
    page = {
        "id": "page-protection-easy-global-strings",
        "target_module": "San9PK.exe",
        "target_va": _hx(_read_u32(loader, 0x41E4)),
        "target_rva": _hx(_read_u32(loader, 0x41E4) - game.OPTIONAL_HEADER.ImageBase),
        "length": 0x1000,
        "install_protection": {"name": "PAGE_READWRITE", "value": _hx(0x04, 2)},
        "restore_protection": {"name": "PAGE_READONLY", "value": _hx(0x02, 2)},
        "physical_write_point": False,
        "install_call_va": _hx(0x401B34),
        "restore_call_va": _hx(0x402772),
        "invariant": "A new bridge must not depend on the page remaining writable.",
    }
    if page["target_va"] != "0x0061C000":
        raise ManifestError("protected page changed")

    type_counts = {
        "jmp_rel32": sum(item["instruction_type"] == "jmp_rel32" for item in redirects),
        "call_rel32": sum(item["instruction_type"] == "call_rel32" for item in redirects),
        "call_rel32_nop": sum(item["instruction_type"] == "call_rel32_nop" for item in redirects),
    }
    if type_counts != {"jmp_rel32": 1, "call_rel32": 29, "call_rel32_nop": 2}:
        raise ManifestError(f"redirect type counts changed: {type_counts}")
    if len({item["target_va"] for item in redirects}) != 32:
        raise ManifestError("redirect target addresses are not unique")
    all_intervals = [interval for item in redirects + auxiliaries if (interval := _interval(item)) is not None]
    for index, left in enumerate(all_intervals):
        for right in all_intervals[index + 1 :]:
            if max(left[0], right[0]) < min(left[1], right[1]):
                raise ManifestError(f"owned write intervals overlap: {left} vs {right}")

    return {
        "schema": SCHEMA,
        "tool_version": TOOL_VERSION,
        "generated_offline_only": True,
        "dependencies": {"pefile": pefile.__version__, "capstone": capstone.__version__},
        "binaries": identities,
        "summary": {
            "redirect_count": len(redirects),
            "redirect_instruction_counts": type_counts,
            "auxiliary_physical_write_count": len(auxiliaries),
            "unique_physical_write_count": len(redirects) + len(auxiliaries),
            "virtual_protect_transition_count": 1,
        },
        "easy_hook_table": {
            "table_rva": _hx(table_rva),
            "sentinel": _hx(0x12345678),
            "pointer_entries": table_evidence,
        },
        "loader_write_closure": loader_audit,
        "redirect_writes": redirects,
        "auxiliary_writes": auxiliaries,
        "page_protection_transitions": [page],
        "cross_point_invariants": [
            {
                "id": "child-training-pair",
                "members": ["aux-child-training-a", "aux-child-training-b"],
                "allowed_hex_tuples": [["32", "32"], ["00", "00"]],
                "other_combinations": "reject",
            },
            {
                "id": "redirect-base-binding",
                "rule": "Every installed rel32 must resolve into the one exact loaded Easy.dll at its declared target RVA.",
            },
            {
                "id": "whole-manifest-atomicity",
                "rule": "A partial, mixed, unknown, or changed state rejects execution before any bridge write.",
            },
        ],
        "conflict_graph": _conflicts(redirects, auxiliaries),
    }


def _canonical(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def _validate_manifest_shape(manifest: dict[str, Any]) -> None:
    if manifest.get("schema") != SCHEMA or manifest.get("tool_version") != TOOL_VERSION:
        raise ManifestError("manifest schema/tool version mismatch")
    summary = manifest.get("summary", {})
    expected_summary = {
        "redirect_count": 32,
        "redirect_instruction_counts": {"jmp_rel32": 1, "call_rel32": 29, "call_rel32_nop": 2},
        "auxiliary_physical_write_count": 6,
        "unique_physical_write_count": 38,
        "virtual_protect_transition_count": 1,
    }
    if summary != expected_summary:
        raise ManifestError("manifest summary is not the frozen 32+6 profile")
    redirects = manifest.get("redirect_writes")
    auxiliaries = manifest.get("auxiliary_writes")
    if not isinstance(redirects, list) or len(redirects) != 32:
        raise ManifestError("manifest redirect list count mismatch")
    if not isinstance(auxiliaries, list) or len(auxiliaries) != 6:
        raise ManifestError("manifest auxiliary list count mismatch")
    if any(not isinstance(item, dict) for item in redirects + auxiliaries):
        raise ManifestError("manifest write-point records must be objects")
    ids = [item.get("id") for item in redirects + auxiliaries]
    if len(ids) != len(set(ids)) or any(not isinstance(item, str) for item in ids):
        raise ManifestError("manifest IDs are missing or duplicated")


def _parse_u32_hex(value: Any, label: str, *, nonzero: bool = False) -> int:
    if not isinstance(value, str) or not value.startswith("0x"):
        raise ManifestError(f"{label} must be a canonical 0x-prefixed string")
    try:
        parsed = int(value, 16)
    except ValueError as exc:
        raise ManifestError(f"{label} is not hexadecimal") from exc
    if parsed < (1 if nonzero else 0) or parsed > 0xFFFFFFFF:
        raise ManifestError(f"{label} is outside UInt32")
    return parsed


def _normalize_hex_bytes(value: Any, length: int, label: str) -> str:
    if not isinstance(value, str) or len(value) != length * 2:
        raise ManifestError(f"{label} must contain exactly {length} bytes")
    try:
        raw = bytes.fromhex(value)
    except ValueError as exc:
        raise ManifestError(f"{label} is not hexadecimal") from exc
    if len(raw) != length:
        raise ManifestError(f"{label} decoded length mismatch")
    return raw.hex().upper()


def _validate_easy_image_bounds(manifest: dict[str, Any], easy_base: int) -> None:
    image = manifest.get("binaries", {}).get("Easy.dll", {})
    image_size = _parse_u32_hex(image.get("size_of_image"), "Easy SizeOfImage", nonzero=True)
    if easy_base + image_size > 0x1_0000_0000:
        raise ManifestError("Easy module image range wraps beyond UInt32")

    def require_range(value: Any, length: int, label: str) -> None:
        rva = _parse_u32_hex(value, label)
        if not isinstance(length, int) or isinstance(length, bool) or length <= 0:
            raise ManifestError(f"{label} length must be a positive integer")
        if rva + length > image_size:
            raise ManifestError(f"{label} is outside the authenticated Easy image")

    table = manifest.get("easy_hook_table", {})
    table_rva = _parse_u32_hex(table.get("table_rva"), "Easy hook table RVA")
    require_range(table.get("table_rva"), 4, "Easy hook table sentinel RVA")
    entries = table.get("pointer_entries")
    if not isinstance(entries, list):
        raise ManifestError("Easy hook table entries are missing")
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            raise ManifestError("Easy hook table entry is not an object")
        require_range(entry.get("initializer_rva"), 1, f"Easy initializer RVA {index}")
        table_offset = _parse_u32_hex(entry.get("table_offset"), f"Easy table offset {index}")
        require_range(_hx(table_rva + table_offset), 4, f"Easy table slot RVA {index}")
        require_range(entry.get("target_rva"), 1, f"Easy hook target RVA {index}")

    for item in manifest["redirect_writes"]:
        require_range(
            item.get("installed_template", {}).get("target_rva"),
            1,
            f"{item.get('id')} Easy target RVA",
        )
    for item in manifest["auxiliary_writes"]:
        if item.get("target_module") == "Easy.dll":
            require_range(item.get("target_rva"), item.get("length"), f"{item.get('id')} Easy slot RVA")


def validate_manifest(
    manifest: dict[str, Any], game_path: Path, loader_path: Path, easy_path: Path
) -> dict[str, Any]:
    _validate_manifest_shape(manifest)
    reconstructed = build_manifest(game_path, loader_path, easy_path)
    if _canonical(manifest) != _canonical(reconstructed):
        raise ManifestError("manifest content differs from deterministic binary reconstruction")
    return reconstructed


def synthesize_runtime_snapshot(
    manifest: dict[str, Any], easy_base: int, mode: str = "installed", hwnd: int = 0x00123456
) -> dict[str, Any]:
    if mode not in {"original", "installed"}:
        raise ManifestError("snapshot mode must be original or installed")
    points: dict[str, str] = {}
    for item in manifest["redirect_writes"]:
        if mode == "original":
            value = item["original_hex"]
        else:
            target_va = int(item["target_va"], 16)
            easy_rva = int(item["installed_template"]["target_rva"], 16)
            value = _installed_bytes(item["instruction_type"], target_va, easy_base, easy_rva).hex().upper()
        points[item["id"]] = value
    for item in manifest["auxiliary_writes"]:
        if item["id"] == "aux-easy-game-hwnd-slot":
            value = item["original_hex"] if mode == "original" else struct.pack("<I", hwnd).hex().upper()
        elif mode == "original":
            value = item["original_hex"]
        elif "installed_hex" in item:
            value = item["installed_hex"]
        elif item["id"].startswith("aux-child-training-"):
            value = "32"
        else:
            raise ManifestError(f"cannot synthesize {item['id']}")
        points[item["id"]] = value
    return {
        "schema": "san9-easy-offline-runtime-snapshot/v1",
        "easy_module_base": _hx(easy_base),
        "exact_game_hwnd": _hx(hwnd),
        "points": points,
        "page_protections": {
            "page-protection-easy-global-strings": "0x02" if mode == "original" else "0x04"
        },
    }


def validate_runtime_snapshot(
    manifest: dict[str, Any],
    snapshot: dict[str, Any],
    game_path: Path = DEFAULT_GAME,
    loader_path: Path = DEFAULT_LOADER,
    easy_path: Path = DEFAULT_EASY,
) -> str:
    """Validate already-captured bytes without doing any process access.

    This helper authenticates the supplied manifest by deterministic binary
    reconstruction before interpreting any snapshot field.  It is intended for
    later adapters and for mutation tests; the caller remains responsible for
    obtaining bytes under its own authorization.
    """
    manifest = validate_manifest(manifest, game_path, loader_path, easy_path)
    if snapshot.get("schema") != "san9-easy-offline-runtime-snapshot/v1":
        raise ManifestError("runtime snapshot schema mismatch")
    easy_base = _parse_u32_hex(snapshot.get("easy_module_base"), "Easy module base", nonzero=True)
    _validate_easy_image_bounds(manifest, easy_base)
    hwnd = _parse_u32_hex(snapshot.get("exact_game_hwnd"), "game HWND", nonzero=True)
    points = snapshot.get("points")
    expected_ids = [item["id"] for item in manifest["redirect_writes"] + manifest["auxiliary_writes"]]
    if not isinstance(points, dict) or set(points) != set(expected_ids):
        raise ManifestError("runtime snapshot point set is incomplete or contains unknown points")
    page_protections = snapshot.get("page_protections")
    if not isinstance(page_protections, dict) or set(page_protections) != {
        "page-protection-easy-global-strings"
    }:
        raise ManifestError("runtime snapshot page-protection set is incomplete or unknown")

    modes: set[str] = set()
    for item in manifest["redirect_writes"]:
        actual = _normalize_hex_bytes(points[item["id"]], item["length"], item["id"])
        installed = _installed_bytes(
            item["instruction_type"],
            int(item["target_va"], 16),
            easy_base,
            int(item["installed_template"]["target_rva"], 16),
        ).hex().upper()
        if actual == item["original_hex"]:
            modes.add("original")
        elif actual == installed:
            modes.add("installed")
        else:
            raise ManifestError(f"runtime redirect mismatch: {item['id']}")
    if len(modes) != 1:
        raise ManifestError("runtime redirects are a mixed/partial state")
    mode = next(iter(modes))
    expected_protection = "0x02" if mode == "original" else "0x04"
    protection_value = page_protections["page-protection-easy-global-strings"]
    if not isinstance(protection_value, str) or protection_value.lower() != expected_protection:
        raise ManifestError("Easy-owned game page protection does not match the whole-hook state")

    train = [
        _normalize_hex_bytes(points["aux-child-training-a"], 1, "aux-child-training-a"),
        _normalize_hex_bytes(points["aux-child-training-b"], 1, "aux-child-training-b"),
    ]
    if mode == "original" and train != ["32", "32"]:
        raise ManifestError("original whole-hook state requires restored child-training 32/32")
    if mode == "installed" and train not in (["32", "32"], ["00", "00"]):
        raise ManifestError("child-training pair is split or unknown")
    for item in manifest["auxiliary_writes"]:
        actual = _normalize_hex_bytes(points[item["id"]], item["length"], item["id"])
        if item["id"].startswith("aux-child-training-"):
            continue
        if item["id"] == "aux-easy-game-hwnd-slot":
            allowed = item["original_hex"] if mode == "original" else struct.pack("<I", hwnd).hex().upper()
            if actual != allowed:
                raise ManifestError("Easy HWND slot does not bind the exact game HWND")
            continue
        allowed = item["original_hex"] if mode == "original" else item["installed_hex"]
        if actual != allowed:
            raise ManifestError(f"runtime auxiliary mismatch: {item['id']}")
    return mode


def _load_json(path: Path) -> dict[str, Any]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ManifestError(f"cannot read JSON {path}: {exc}") from exc
    if not isinstance(data, dict):
        raise ManifestError(f"JSON root is not an object: {path}")
    return data


def _write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(value, ensure_ascii=False, indent=2) + "\n"
    path.write_text(text, encoding="utf-8", newline="\n")


def _paths_from_args(args: argparse.Namespace) -> tuple[Path, Path, Path]:
    return Path(args.game), Path(args.loader), Path(args.easy)


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game", default=str(DEFAULT_GAME), help="exact San9PK.exe disk path")
    parser.add_argument("--loader", default=str(DEFAULT_LOADER), help="exact San9PKEasy.exe disk path")
    parser.add_argument("--easy", default=str(DEFAULT_EASY), help="exact Easy.dll disk path")
    subparsers = parser.add_subparsers(dest="command", required=True)
    generate = subparsers.add_parser("generate", help="deterministically generate the manifest")
    generate.add_argument("--output", default=str(DEFAULT_MANIFEST))
    validate = subparsers.add_parser("validate", help="reconstruct and compare the frozen manifest")
    validate.add_argument("--manifest", default=str(DEFAULT_MANIFEST))
    snapshot = subparsers.add_parser(
        "validate-runtime-snapshot", help="validate an offline, caller-supplied byte snapshot"
    )
    snapshot.add_argument("--manifest", default=str(DEFAULT_MANIFEST))
    snapshot.add_argument("--snapshot", required=True)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    try:
        if args.command == "generate":
            manifest = build_manifest(*_paths_from_args(args))
            output = Path(args.output)
            _write_json(output, manifest)
            print(f"PASS generated {output} redirects=32 auxiliaries=6 WPM-calls=76")
            return 0
        if args.command == "validate":
            manifest = _load_json(Path(args.manifest))
            validate_manifest(manifest, *_paths_from_args(args))
            print("PASS exact 32 redirects + 6 auxiliaries; WPM/VirtualProtect closure complete")
            return 0
        manifest = _load_json(Path(args.manifest))
        mode = validate_runtime_snapshot(
            manifest,
            _load_json(Path(args.snapshot)),
            *_paths_from_args(args),
        )
        print(f"PASS offline runtime snapshot state={mode}")
        return 0
    except (
        ManifestError,
        pefile.PEFormatError,
        OSError,
        ValueError,
        TypeError,
        KeyError,
        AttributeError,
        struct.error,
    ) as exc:
        print(f"REJECT {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
