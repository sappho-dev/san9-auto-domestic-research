#!/usr/bin/env python3
"""Generate the P1 native Easy verifier table from authenticated evidence.

This tool is deliberately offline.  It authenticates the reviewed JSON before
parsing it, authenticates the exact San9PK image used to obtain idle-anchor
bytes, and writes a deterministic private C header into a build staging area.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import sys
from typing import Any


EXPECTED_MANIFEST_SHA256 = (
    "72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE"
)
EXPECTED_SCHEMA = "san9-easy-ownership-manifest/v1"
REDIRECT_COUNT = 32
AUXILIARY_COUNT = 6
IDLE_PREFIX_LENGTH = 6
IDLE_CALLER_ANCHOR_LENGTH = 12


class EvidenceError(RuntimeError):
    """Raised when frozen evidence is missing, ambiguous, or inconsistent."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise EvidenceError(message)


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def authenticate_manifest_bytes(raw_manifest: bytes) -> dict[str, Any]:
    actual_manifest_sha = sha256_bytes(raw_manifest)
    require(
        actual_manifest_sha == EXPECTED_MANIFEST_SHA256,
        "manifest SHA-256 rejected: "
        f"actual={actual_manifest_sha} required={EXPECTED_MANIFEST_SHA256}",
    )
    try:
        manifest = json.loads(raw_manifest.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise EvidenceError("manifest is not canonical UTF-8 JSON") from error
    require(isinstance(manifest, dict), "manifest root must be an object")
    return manifest


def parse_u32(value: Any, label: str) -> int:
    require(isinstance(value, str) and value.startswith("0x"), f"{label}: hex string required")
    try:
        parsed = int(value, 16)
    except ValueError as error:
        raise EvidenceError(f"{label}: invalid hexadecimal value") from error
    require(0 <= parsed <= 0xFFFFFFFF, f"{label}: outside uint32")
    return parsed


def parse_hex(value: Any, label: str, expected_length: int | None = None) -> bytes:
    require(isinstance(value, str), f"{label}: hex text required")
    require(len(value) % 2 == 0, f"{label}: odd hexadecimal length")
    try:
        result = bytes.fromhex(value)
    except ValueError as error:
        raise EvidenceError(f"{label}: invalid hexadecimal bytes") from error
    if expected_length is not None:
        require(len(result) == expected_length, f"{label}: expected {expected_length} bytes")
    return result


def checked_end(base: int, length: int, label: str) -> int:
    require(0 <= base <= 0xFFFFFFFF, f"{label}: base outside uint32")
    require(0 <= length <= 0x100000000, f"{label}: invalid length")
    end = base + length
    require(end <= 0x100000000, f"{label}: uint32 address-space overflow")
    return end


class Pe32Image:
    """Minimal fail-closed PE32 RVA reader; no third-party parser is needed."""

    def __init__(self, data: bytes) -> None:
        self.data = data
        require(len(data) >= 0x40 and data[:2] == b"MZ", "game image: invalid DOS header")
        pe_offset = self._u32(0x3C)
        require(pe_offset + 24 <= len(data), "game image: truncated PE header")
        require(data[pe_offset : pe_offset + 4] == b"PE\0\0", "game image: invalid PE signature")
        file_header = pe_offset + 4
        self.machine = self._u16(file_header)
        section_count = self._u16(file_header + 2)
        optional_size = self._u16(file_header + 16)
        optional = file_header + 20
        require(optional + optional_size <= len(data), "game image: truncated optional header")
        require(optional_size >= 96 and self._u16(optional) == 0x010B, "game image: not PE32")
        self.image_base = self._u32(optional + 28)
        self.size_of_image = self._u32(optional + 56)
        self.size_of_headers = self._u32(optional + 60)
        section_table = optional + optional_size
        require(section_count > 0, "game image: no sections")
        require(section_table + section_count * 40 <= len(data), "game image: truncated sections")
        self.sections: list[tuple[int, int, int]] = []
        for index in range(section_count):
            entry = section_table + index * 40
            virtual_address = self._u32(entry + 12)
            raw_size = self._u32(entry + 16)
            raw_offset = self._u32(entry + 20)
            require(raw_offset + raw_size <= len(data), f"game image: section {index} raw data truncated")
            self.sections.append((virtual_address, raw_size, raw_offset))

    def _u16(self, offset: int) -> int:
        require(offset + 2 <= len(self.data), "game image: uint16 outside file")
        return struct.unpack_from("<H", self.data, offset)[0]

    def _u32(self, offset: int) -> int:
        require(offset + 4 <= len(self.data), "game image: uint32 outside file")
        return struct.unpack_from("<I", self.data, offset)[0]

    def read_rva(self, rva: int, length: int, label: str) -> bytes:
        require(length > 0, f"{label}: empty read")
        end = checked_end(rva, length, label)
        require(end <= self.size_of_image, f"{label}: outside SizeOfImage")
        if end <= self.size_of_headers:
            require(end <= len(self.data), f"{label}: header bytes outside file")
            return self.data[rva:end]
        matches: list[int] = []
        for virtual_address, raw_size, raw_offset in self.sections:
            if rva >= virtual_address and end <= virtual_address + raw_size:
                matches.append(raw_offset + (rva - virtual_address))
        require(len(matches) == 1, f"{label}: no unique complete raw section mapping")
        offset = matches[0]
        require(offset + length <= len(self.data), f"{label}: mapped bytes outside file")
        return self.data[offset : offset + length]


def c_bytes(value: bytes, capacity: int) -> str:
    require(len(value) <= capacity, "internal: byte initializer exceeds capacity")
    padded = value + b"\0" * (capacity - len(value))
    return "{ " + ", ".join(f"0x{item:02X}u" for item in padded) + " }"


def c_u32(value: int) -> str:
    return f"UINT32_C(0x{value:08X})"


def load_and_validate(manifest_path: Path, game_image_path: Path | None) -> dict[str, Any]:
    raw_manifest = manifest_path.read_bytes()
    actual_manifest_sha = sha256_bytes(raw_manifest)
    manifest = authenticate_manifest_bytes(raw_manifest)
    require(manifest.get("schema") == EXPECTED_SCHEMA, "manifest schema rejected")
    require(manifest.get("generated_offline_only") is True, "manifest is not offline-only evidence")

    binaries = manifest.get("binaries")
    require(isinstance(binaries, dict), "manifest binaries missing")
    game = binaries.get("San9PK.exe")
    easy = binaries.get("Easy.dll")
    require(isinstance(game, dict) and isinstance(easy, dict), "exact game/Easy identities missing")
    for name, binary in (("game", game), ("Easy", easy)):
        require(binary.get("machine") == "IMAGE_FILE_MACHINE_I386", f"{name}: machine rejected")
        require(binary.get("pe_kind") == "PE32", f"{name}: PE kind rejected")
        require(isinstance(binary.get("size"), int) and binary["size"] > 0, f"{name}: size rejected")
        require(
            isinstance(binary.get("sha256"), str) and len(binary["sha256"]) == 64,
            f"{name}: SHA-256 rejected",
        )
    game_base = parse_u32(game.get("preferred_image_base"), "game preferred image base")
    game_size = parse_u32(game.get("size_of_image"), "game SizeOfImage")
    easy_base = parse_u32(easy.get("preferred_image_base"), "Easy preferred image base")
    easy_size = parse_u32(easy.get("size_of_image"), "Easy SizeOfImage")
    checked_end(game_base, game_size, "game image")
    checked_end(easy_base, easy_size, "Easy image")

    summary = manifest.get("summary")
    require(isinstance(summary, dict), "manifest summary missing")
    require(summary.get("redirect_count") == REDIRECT_COUNT, "redirect summary count rejected")
    require(summary.get("auxiliary_physical_write_count") == AUXILIARY_COUNT, "auxiliary summary count rejected")
    require(summary.get("unique_physical_write_count") == REDIRECT_COUNT + AUXILIARY_COUNT, "owned point count rejected")
    require(summary.get("virtual_protect_transition_count") == 1, "page transition count rejected")
    require(
        summary.get("redirect_instruction_counts")
        == {"jmp_rel32": 1, "call_rel32": 29, "call_rel32_nop": 2},
        "redirect instruction summary rejected",
    )

    redirects_raw = manifest.get("redirect_writes")
    require(isinstance(redirects_raw, list) and len(redirects_raw) == REDIRECT_COUNT, "redirect table must be 32 rows")
    redirects: list[dict[str, Any]] = []
    instruction_counts = {"jmp_rel32": 0, "call_rel32": 0, "call_rel32_nop": 0}
    seen_rvas: set[int] = set()
    for index, row in enumerate(redirects_raw):
        label = f"redirect-{index:02d}"
        require(isinstance(row, dict) and row.get("id") == label, f"{label}: ID/order rejected")
        require(row.get("owner") == "Easy.dll" and row.get("target_module") == "San9PK.exe", f"{label}: ownership rejected")
        kind = row.get("instruction_type")
        require(kind in instruction_counts, f"{label}: instruction type rejected")
        expected_length = 6 if kind == "call_rel32_nop" else 5
        require(row.get("length") == expected_length, f"{label}: instruction length rejected")
        game_rva = parse_u32(row.get("target_rva"), f"{label} target RVA")
        target_va = parse_u32(row.get("target_va"), f"{label} target VA")
        require(game_base + game_rva == target_va, f"{label}: VA/RVA mismatch")
        require(game_rva not in seen_rvas, f"{label}: duplicate target RVA")
        seen_rvas.add(game_rva)
        require(game_rva + expected_length <= game_size, f"{label}: target outside game image")
        original = parse_hex(row.get("original_hex"), f"{label} original", expected_length)
        template = row.get("installed_template")
        require(isinstance(template, dict), f"{label}: installed template missing")
        require(template.get("encoding") == "x86_rel32", f"{label}: encoding rejected")
        require(template.get("target_module") == "Easy.dll", f"{label}: installed module rejected")
        opcode = parse_hex(template.get("opcode_hex"), f"{label} opcode", 1)[0]
        require(opcode == (0xE9 if kind == "jmp_rel32" else 0xE8), f"{label}: opcode rejected")
        trailing = parse_hex(template.get("trailing_hex"), f"{label} trailing")
        require(trailing == (b"\x90" if kind == "call_rel32_nop" else b""), f"{label}: trailing bytes rejected")
        easy_target_rva = parse_u32(template.get("target_rva"), f"{label} Easy target RVA")
        require(easy_target_rva < easy_size, f"{label}: Easy target outside image")
        source = (game_base + game_rva) & 0xFFFFFFFF
        target = (easy_base + easy_target_rva) & 0xFFFFFFFF
        displacement = (target - ((source + 5) & 0xFFFFFFFF)) & 0xFFFFFFFF
        installed = bytes([opcode]) + struct.pack("<I", displacement) + trailing
        frozen_installed = parse_hex(
            row.get("installed_preferred_base_hex"), f"{label} preferred installed", expected_length
        )
        require(installed == frozen_installed, f"{label}: preferred rel32 evidence mismatch")
        require(
            row.get("accepted_runtime_states") == ["original", "installed_for_exact_easy_base"],
            f"{label}: accepted states rejected",
        )
        instruction_counts[kind] += 1
        redirects.append(
            {
                "game_rva": game_rva,
                "easy_target_rva": easy_target_rva,
                "length": expected_length,
                "opcode": opcode,
                "original": original,
                "trailing": trailing,
            }
        )
    require(instruction_counts == summary["redirect_instruction_counts"], "redirect counts disagree with rows")

    auxiliary_raw = manifest.get("auxiliary_writes")
    expected_aux_ids = [
        "aux-child-training-a",
        "aux-child-training-b",
        "aux-max-corps-food-a",
        "aux-max-corps-food-b",
        "aux-ai-counter-barbarians",
        "aux-easy-game-hwnd-slot",
    ]
    require(isinstance(auxiliary_raw, list) and len(auxiliary_raw) == AUXILIARY_COUNT, "auxiliary table must be six rows")
    require([row.get("id") for row in auxiliary_raw if isinstance(row, dict)] == expected_aux_ids, "auxiliary ID/order rejected")
    auxiliaries: list[dict[str, Any]] = []
    for index, row in enumerate(auxiliary_raw):
        label = expected_aux_ids[index]
        length = row.get("length")
        require(isinstance(length, int) and 1 <= length <= 4, f"{label}: length rejected")
        target_module = row.get("target_module")
        require(row.get("owner") == "Easy.dll" and target_module in ("San9PK.exe", "Easy.dll"), f"{label}: ownership rejected")
        rva = parse_u32(row.get("target_rva"), f"{label} target RVA")
        module_size = game_size if target_module == "San9PK.exe" else easy_size
        module_base = game_base if target_module == "San9PK.exe" else easy_base
        require(rva + length <= module_size, f"{label}: target outside image")
        if target_module == "San9PK.exe":
            target_va = parse_u32(row.get("target_va"), f"{label} target VA")
            require(module_base + rva == target_va, f"{label}: VA/RVA mismatch")
        original = parse_hex(row.get("original_hex"), f"{label} original", length)
        if label.startswith("aux-child-training-"):
            states = row.get("installed_hex_states")
            require(states == ["32", "00"], f"{label}: training states rejected")
            dynamic = row.get("dynamic_two_state")
            require(
                isinstance(dynamic, dict)
                and dynamic.get("pair_id") == "allow_child_training"
                and dynamic.get("allowed_pairs") == ["32/32", "00/00"]
                and dynamic.get("split_pair_is_invalid") is True,
                f"{label}: training pair invariant rejected",
            )
            installed_a = parse_hex(states[0], f"{label} installed A", length)
            installed_b = parse_hex(states[1], f"{label} installed B", length)
            kind = 1
            variant_count = 2
        elif label == "aux-easy-game-hwnd-slot":
            require(row.get("write_type") == "runtime_hwnd_u32_le", f"{label}: write type rejected")
            require(row.get("installed_template") == "exact_bound_game_hwnd_u32_le", f"{label}: template rejected")
            require(row.get("accepted_runtime_states") == ["zero_before_install", "exact_bound_game_hwnd"], f"{label}: states rejected")
            installed_a = b"\0" * length
            installed_b = b"\0" * length
            kind = 2
            variant_count = 0
        else:
            installed_a = parse_hex(row.get("installed_hex"), f"{label} installed", length)
            installed_b = b"\0" * length
            kind = 0
            variant_count = 1
        auxiliaries.append(
            {
                "module": 0 if target_module == "San9PK.exe" else 1,
                "rva": rva,
                "length": length,
                "kind": kind,
                "variant_count": variant_count,
                "original": original,
                "installed_a": installed_a,
                "installed_b": installed_b,
            }
        )

    page_rows = manifest.get("page_protection_transitions")
    require(isinstance(page_rows, list) and len(page_rows) == 1, "page transition row missing")
    page = page_rows[0]
    require(
        page.get("id") == "page-protection-easy-global-strings"
        and page.get("target_module") == "San9PK.exe"
        and page.get("physical_write_point") is False,
        "page transition identity rejected",
    )
    page_rva = parse_u32(page.get("target_rva"), "page target RVA")
    page_va = parse_u32(page.get("target_va"), "page target VA")
    page_length = page.get("length")
    require(isinstance(page_length, int) and page_length > 0, "page length rejected")
    require(game_base + page_rva == page_va and page_rva + page_length <= game_size, "page address rejected")
    original_protection = parse_u32(page.get("restore_protection", {}).get("value"), "original page protection")
    installed_protection = parse_u32(page.get("install_protection", {}).get("value"), "installed page protection")
    require(original_protection == 0x02 and installed_protection == 0x04, "page protections rejected")

    conflict_graph = manifest.get("conflict_graph")
    require(isinstance(conflict_graph, dict), "conflict graph missing")
    idle_rows = conflict_graph.get("idle_candidate_rows")
    require(isinstance(idle_rows, list) and len(idle_rows) == 3, "three idle candidates required")
    expected_idle_names = ["app_idle_slot", "original_idle_function", "idle_caller_return"]
    require([row.get("name") for row in idle_rows if isinstance(row, dict)] == expected_idle_names, "idle row order rejected")
    idle_vas: list[int] = []
    for index, row in enumerate(idle_rows):
        require(row.get("direct_overlap_with_easy_owned_write") is False and row.get("overlaps") == [], f"idle row {index}: overlap rejected")
        idle_vas.append(parse_u32(row.get("va"), f"idle row {index} VA"))
    require(idle_rows[0].get("length_used_for_overlap_check") == 4, "idle slot length rejected")
    require(idle_rows[1].get("length_used_for_overlap_check") == 1, "idle function row rejected")
    require(idle_rows[2].get("length_used_for_overlap_check") == 1, "idle caller row rejected")
    idle_slot_rva = idle_vas[0] - game_base
    idle_function_rva = idle_vas[1] - game_base
    idle_return_rva = idle_vas[2] - game_base
    require(min(idle_slot_rva, idle_function_rva, idle_return_rva) >= 0, "idle VA precedes game base")
    require(idle_return_rva >= IDLE_CALLER_ANCHOR_LENGTH, "idle caller anchor underflow")
    idle_caller_rva = idle_return_rva - IDLE_CALLER_ANCHOR_LENGTH

    if game_image_path is None:
        evidence_path = game.get("path_evidence")
        require(isinstance(evidence_path, str) and evidence_path, "game path evidence missing")
        game_image_path = Path(evidence_path)
    raw_game = game_image_path.read_bytes()
    actual_game_sha = sha256_bytes(raw_game)
    require(len(raw_game) == game["size"], f"game image size rejected: actual={len(raw_game)} expected={game['size']}")
    require(actual_game_sha == game["sha256"].upper(), f"game image SHA-256 rejected: actual={actual_game_sha}")
    pe = Pe32Image(raw_game)
    require(pe.machine == 0x014C, "game PE machine is not I386")
    require(pe.image_base == game_base and pe.size_of_image == game_size, "game PE image identity rejected")
    idle_slot = pe.read_rva(idle_slot_rva, 4, "idle slot")
    require(struct.unpack("<I", idle_slot)[0] == idle_vas[1], "idle slot does not point to frozen original idle")
    idle_prefix = pe.read_rva(idle_function_rva, IDLE_PREFIX_LENGTH, "original idle prefix")
    idle_caller = pe.read_rva(idle_caller_rva, IDLE_CALLER_ANCHOR_LENGTH, "idle caller anchor")

    return {
        "manifest_sha": actual_manifest_sha,
        "game_file_sha": actual_game_sha,
        "game_base": game_base,
        "game_size": game_size,
        "easy_preferred_base": easy_base,
        "easy_size": easy_size,
        "redirects": redirects,
        "auxiliaries": auxiliaries,
        "page_rva": page_rva,
        "page_length": page_length,
        "page_original_protection": original_protection,
        "page_installed_protection": installed_protection,
        "idle": [
            {"rva": idle_slot_rva, "length": 4, "expected": idle_slot},
            {"rva": idle_function_rva, "length": IDLE_PREFIX_LENGTH, "expected": idle_prefix},
            {"rva": idle_caller_rva, "length": IDLE_CALLER_ANCHOR_LENGTH, "expected": idle_caller},
        ],
    }


def render_header(model: dict[str, Any]) -> str:
    redirects = model["redirects"]
    auxiliaries = model["auxiliaries"]
    idle = model["idle"]
    lines = [
        "/* Generated offline from authenticated evidence. DO NOT EDIT. */",
        "#ifndef SAN9_P1_EASY_MANIFEST_GEN_H",
        "#define SAN9_P1_EASY_MANIFEST_GEN_H",
        "",
        "#include <stdint.h>",
        "",
        f'#define SAN9_P1_EASY_MANIFEST_SHA256 "{model["manifest_sha"]}"',
        f'#define SAN9_P1_EASY_GAME_FILE_SHA256 "{model["game_file_sha"]}"',
        f"#define SAN9_P1_EASY_GENERATED_REDIRECT_COUNT {len(redirects)}u",
        f"#define SAN9_P1_EASY_GENERATED_AUXILIARY_COUNT {len(auxiliaries)}u",
        f"#define SAN9_P1_EASY_GENERATED_IDLE_ANCHOR_COUNT {len(idle)}u",
        f"#define SAN9_P1_EASY_GENERATED_TOTAL_POINT_COUNT {len(redirects) + len(auxiliaries) + 1 + len(idle)}u",
        f"#define SAN9_P1_EASY_GAME_IMAGE_BASE {c_u32(model['game_base'])}",
        f"#define SAN9_P1_EASY_GAME_IMAGE_SIZE {c_u32(model['game_size'])}",
        f"#define SAN9_P1_EASY_PREFERRED_IMAGE_BASE {c_u32(model['easy_preferred_base'])}",
        f"#define SAN9_P1_EASY_IMAGE_SIZE {c_u32(model['easy_size'])}",
        f"#define SAN9_P1_EASY_PAGE_RVA {c_u32(model['page_rva'])}",
        f"#define SAN9_P1_EASY_PAGE_LENGTH {c_u32(model['page_length'])}",
        f"#define SAN9_P1_EASY_PAGE_ORIGINAL_PROTECTION {c_u32(model['page_original_protection'])}",
        f"#define SAN9_P1_EASY_PAGE_INSTALLED_PROTECTION {c_u32(model['page_installed_protection'])}",
        "#define SAN9_P1_EASY_PAGE_COMMITTED_STATE UINT32_C(0x00001000)",
        "",
        "#define SAN9_P1_EASY_AUX_CHILD_TRAINING_A_INDEX 0u",
        "#define SAN9_P1_EASY_AUX_CHILD_TRAINING_B_INDEX 1u",
        "#define SAN9_P1_EASY_AUX_MAX_CORPS_FOOD_A_INDEX 2u",
        "#define SAN9_P1_EASY_AUX_MAX_CORPS_FOOD_B_INDEX 3u",
        "#define SAN9_P1_EASY_AUX_AI_COUNTER_INDEX 4u",
        "#define SAN9_P1_EASY_AUX_HWND_INDEX 5u",
        "#define SAN9_P1_EASY_IDLE_SLOT_INDEX 0u",
        "#define SAN9_P1_EASY_IDLE_FUNCTION_INDEX 1u",
        "#define SAN9_P1_EASY_IDLE_CALLER_INDEX 2u",
        "",
        "typedef struct San9P1EasyGeneratedRedirect {",
        "    uint32_t game_rva;",
        "    uint32_t easy_target_rva;",
        "    uint8_t length;",
        "    uint8_t opcode;",
        "    uint8_t trailing_length;",
        "    uint8_t reserved;",
        "    uint8_t original[6];",
        "    uint8_t trailing[1];",
        "} San9P1EasyGeneratedRedirect;",
        "",
        "typedef struct San9P1EasyGeneratedAuxiliary {",
        "    uint32_t rva;",
        "    uint8_t module_index;",
        "    uint8_t length;",
        "    uint8_t kind;",
        "    uint8_t installed_variant_count;",
        "    uint8_t original[4];",
        "    uint8_t installed_a[4];",
        "    uint8_t installed_b[4];",
        "} San9P1EasyGeneratedAuxiliary;",
        "",
        "typedef struct San9P1EasyGeneratedIdleAnchor {",
        "    uint32_t game_rva;",
        "    uint8_t length;",
        "    uint8_t reserved[3];",
        "    uint8_t expected[12];",
        "} San9P1EasyGeneratedIdleAnchor;",
        "",
        "static const San9P1EasyGeneratedRedirect san9_p1_easy_generated_redirects",
        "    [SAN9_P1_EASY_GENERATED_REDIRECT_COUNT] = {",
    ]
    for item in redirects:
        lines.append(
            "    { "
            f"{c_u32(item['game_rva'])}, {c_u32(item['easy_target_rva'])}, "
            f"{item['length']}u, 0x{item['opcode']:02X}u, {len(item['trailing'])}u, 0u, "
            f"{c_bytes(item['original'], 6)}, {c_bytes(item['trailing'], 1)} "
            "},"
        )
    lines.extend(
        [
            "};",
            "",
            "static const San9P1EasyGeneratedAuxiliary san9_p1_easy_generated_auxiliaries",
            "    [SAN9_P1_EASY_GENERATED_AUXILIARY_COUNT] = {",
        ]
    )
    for item in auxiliaries:
        lines.append(
            "    { "
            f"{c_u32(item['rva'])}, {item['module']}u, {item['length']}u, "
            f"{item['kind']}u, {item['variant_count']}u, "
            f"{c_bytes(item['original'], 4)}, {c_bytes(item['installed_a'], 4)}, "
            f"{c_bytes(item['installed_b'], 4)} "
            "},"
        )
    lines.extend(
        [
            "};",
            "",
            "static const San9P1EasyGeneratedIdleAnchor san9_p1_easy_generated_idle_anchors",
            "    [SAN9_P1_EASY_GENERATED_IDLE_ANCHOR_COUNT] = {",
        ]
    )
    for item in idle:
        lines.append(
            "    { "
            f"{c_u32(item['rva'])}, {item['length']}u, {{ 0u, 0u, 0u }}, "
            f"{c_bytes(item['expected'], 12)} "
            "},"
        )
    lines.extend(["};", "", "#endif", ""])
    return "\n".join(lines)


def write_atomic(output: Path, text: str) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".tmp")
    temporary.write_text(text, encoding="ascii", newline="\n")
    os.replace(temporary, output)


def run_self_test(manifest_path: Path, game_image_path: Path | None) -> int:
    checks = 0

    def test(condition: bool, message: str) -> None:
        nonlocal checks
        checks += 1
        require(condition, f"self-test: {message}")

    raw = manifest_path.read_bytes()
    authenticated = authenticate_manifest_bytes(raw)
    test(authenticated.get("schema") == EXPECTED_SCHEMA, "exact manifest authenticates")
    changed = bytearray(raw)
    changed[len(changed) // 2] ^= 1
    try:
        authenticate_manifest_bytes(bytes(changed))
    except EvidenceError:
        checks += 1
    else:
        raise EvidenceError("self-test: one-byte manifest mutation authenticated")
    test(
        checked_end(0, 0x100000000, "self-test full space") == 0x100000000,
        "exact uint32 address space accepted",
    )
    for base, length, label in (
        (0xFFFFFFFF, 2, "one-byte overflow"),
        (0xFFFFA000, 0x9000, "Easy image overflow"),
    ):
        try:
            checked_end(base, length, label)
        except EvidenceError:
            checks += 1
        else:
            raise EvidenceError(f"self-test: {label} accepted")
    try:
        parse_u32("0x100000000", "self-test RVA overflow")
    except EvidenceError:
        checks += 1
    else:
        raise EvidenceError("self-test: uint32 RVA overflow accepted")
    model = load_and_validate(manifest_path, game_image_path)
    test(len(model["redirects"]) == REDIRECT_COUNT, "32 redirect rows")
    test(len(model["auxiliaries"]) == AUXILIARY_COUNT, "six auxiliary rows")
    test(len(model["idle"]) == 3, "three idle anchors")
    test(
        all(
            item["game_rva"] + item["length"] <= model["game_size"]
            and item["easy_target_rva"] < model["easy_size"]
            for item in model["redirects"]
        ),
        "all redirect game/handler RVAs bounded",
    )
    test(
        all(
            item["rva"] + item["length"]
            <= (model["game_size"] if item["module"] == 0 else model["easy_size"])
            for item in model["auxiliaries"]
        ),
        "all auxiliary RVAs bounded",
    )
    first = render_header(model)
    second = render_header(model)
    test(first == second, "deterministic header rendering")
    test(
        sha256_bytes(first.encode("ascii"))
        == "CCA306579C23581A660CD6972CC37104DBE5339094DDD841FED8C91A40E98F7A",
        "reviewed generated header digest",
    )
    print(
        "P1_NATIVE_EASY_GENERATOR_SELFTEST PASS "
        f"checks={checks} manifest_sha256={model['manifest_sha']} "
        f"header_sha256={sha256_bytes(first.encode('ascii'))}"
    )
    return checks


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--game-image", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--self-test", action="store_true")
    arguments = parser.parse_args()
    try:
        if arguments.self_test:
            run_self_test(arguments.manifest, arguments.game_image)
            return 0
        require(arguments.output is not None, "--output is required unless --self-test is used")
        model = load_and_validate(arguments.manifest, arguments.game_image)
        rendered = render_header(model)
        write_atomic(arguments.output, rendered)
        output_sha = sha256_bytes(rendered.encode("ascii"))
        print(
            "P1_NATIVE_EASY_MANIFEST PASS "
            f"redirects={len(model['redirects'])} auxiliaries={len(model['auxiliaries'])} "
            f"idle_anchors={len(model['idle'])} manifest_sha256={model['manifest_sha']} "
            f"header_sha256={output_sha} output={arguments.output}"
        )
        return 0
    except Exception as error:  # noqa: BLE001 - CLI must fail closed.
        print(f"P1_NATIVE_EASY_MANIFEST FAIL {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
