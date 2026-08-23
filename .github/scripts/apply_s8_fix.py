#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import re
import tempfile
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] if ".github" in str(Path(__file__)) else Path.cwd()


@dataclass
class TextDocument:
    path: Path
    text: str
    newline: str
    bom: bool

    @classmethod
    def load(cls, path: Path) -> "TextDocument":
        raw = path.read_bytes()
        bom = raw.startswith(b"\xef\xbb\xbf")
        payload = raw[3:] if bom else raw
        newline = "\r\n" if b"\r\n" in payload else "\n"
        text = payload.decode("utf-8").replace("\r\n", "\n")
        return cls(path=path, text=text, newline=newline, bom=bom)

    def save(self) -> bool:
        data = self.text.replace("\n", self.newline).encode("utf-8")
        if self.bom:
            data = b"\xef\xbb\xbf" + data
        before = self.path.read_bytes()
        if before == data:
            return False
        self.path.write_bytes(data)
        return True


def binary_sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def normalized_source_bytes(raw: bytes) -> bytes:
    return raw.replace(b"\r\n", b"\n")


def source_sha256(path: Path) -> str:
    return hashlib.sha256(normalized_source_bytes(path.read_bytes())).hexdigest().upper()


def _mask_c_like(text: str) -> str:
    chars = list(text)
    state = "normal"
    index = 0
    while index < len(chars):
        ch = chars[index]
        nxt = chars[index + 1] if index + 1 < len(chars) else ""
        if state == "normal":
            if ch == "/" and nxt == "/":
                chars[index] = chars[index + 1] = " "
                state = "line-comment"
                index += 2
                continue
            if ch == "/" and nxt == "*":
                chars[index] = chars[index + 1] = " "
                state = "block-comment"
                index += 2
                continue
            if ch == '"':
                chars[index] = " "
                state = "string"
                index += 1
                continue
            if ch == "'":
                chars[index] = " "
                state = "char"
                index += 1
                continue
            index += 1
            continue
        if state == "line-comment":
            if ch == "\n":
                state = "normal"
            else:
                chars[index] = " "
            index += 1
            continue
        if state == "block-comment":
            if ch == "*" and nxt == "/":
                chars[index] = chars[index + 1] = " "
                state = "normal"
                index += 2
            else:
                if ch != "\n":
                    chars[index] = " "
                index += 1
            continue
        if state in ("string", "char"):
            if ch == "\\" and index + 1 < len(chars):
                chars[index] = " "
                if chars[index + 1] != "\n":
                    chars[index + 1] = " "
                index += 2
                continue
            terminator = '"' if state == "string" else "'"
            if ch == terminator:
                chars[index] = " "
                state = "normal"
            elif ch != "\n":
                chars[index] = " "
            index += 1
    return "".join(chars)


def _matching_delimiter(masked: str, start: int, opening: str, closing: str) -> int:
    if start >= len(masked) or masked[start] != opening:
        raise RuntimeError(f"delimiter start mismatch at {start}: expected {opening}")
    depth = 0
    for index in range(start, len(masked)):
        if masked[index] == opening:
            depth += 1
        elif masked[index] == closing:
            depth -= 1
            if depth == 0:
                return index
    raise RuntimeError(f"unclosed delimiter {opening} at {start}")


def c_function_span(text: str, name: str) -> tuple[int, int]:
    masked = _mask_c_like(text)
    pattern = re.compile(r"\b" + re.escape(name) + r"\s*\(")
    for match in pattern.finditer(masked):
        open_paren = masked.find("(", match.start())
        close_paren = _matching_delimiter(masked, open_paren, "(", ")")
        cursor = close_paren + 1
        while cursor < len(masked) and masked[cursor].isspace():
            cursor += 1
        if cursor >= len(masked) or masked[cursor] != "{":
            continue
        close_brace = _matching_delimiter(masked, cursor, "{", "}")
        start = text.rfind("\n", 0, match.start()) + 1
        end = close_brace + 1
        while end < len(text) and text[end] in " \t":
            end += 1
        if text.startswith("\n\n", end):
            end += 2
        elif text.startswith("\n", end):
            end += 1
        return start, end
    raise RuntimeError(f"C/C# function not found: {name}")


def typedef_struct_span(text: str, name: str) -> tuple[int, int]:
    masked = _mask_c_like(text)
    match = re.search(
        r"\btypedef\s+struct\s+" + re.escape(name) + r"\s*\{", masked
    )
    if not match:
        raise RuntimeError(f"typedef struct not found: {name}")
    open_brace = masked.find("{", match.start())
    close_brace = _matching_delimiter(masked, open_brace, "{", "}")
    suffix = re.match(r"\s*" + re.escape(name) + r"\s*;", masked[close_brace + 1 :])
    if not suffix:
        raise RuntimeError(f"typedef struct suffix not found: {name}")
    start = text.rfind("\n", 0, match.start()) + 1
    end = close_brace + 1 + suffix.end()
    while end < len(text) and text[end] in " \t":
        end += 1
    if text.startswith("\n\n", end):
        end += 2
    elif text.startswith("\n", end):
        end += 1
    return start, end


def replace_c_function(path: Path, name: str, desired: str) -> None:
    document = TextDocument.load(path)
    start, end = c_function_span(document.text, name)
    replacement = desired.strip("\n") + "\n\n"
    document.text = document.text[:start] + replacement + document.text[end:]
    document.save()


def upsert_c_function(path: Path, name: str, desired: str, before: str) -> None:
    document = TextDocument.load(path)
    try:
        start, end = c_function_span(document.text, name)
        replacement = desired.strip("\n") + "\n\n"
        document.text = document.text[:start] + replacement + document.text[end:]
    except RuntimeError:
        anchor, _ = c_function_span(document.text, before)
        document.text = (
            document.text[:anchor]
            + desired.strip("\n")
            + "\n\n"
            + document.text[anchor:]
        )
    document.save()


def rewrite_c_function(path: Path, name: str, transformer) -> None:
    document = TextDocument.load(path)
    start, end = c_function_span(document.text, name)
    original = document.text[start:end]
    trailing = original[len(original.rstrip("\n")) :] or "\n"
    updated = transformer(original).rstrip("\n") + trailing
    document.text = document.text[:start] + updated + document.text[end:]
    document.save()


def scoped_replace_once(text: str, old: str, new: str, label: str) -> str:
    desired_count = text.count(new)
    if desired_count != 0:
        if desired_count != 1:
            raise RuntimeError(f"{label}: desired form appears {desired_count} times")
        return text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one scoped old form, found {count}")
    return text.replace(old, new, 1)


def ensure_block_before(path: Path, anchor: str, marker: str, block: str) -> None:
    document = TextDocument.load(path)
    desired = block.strip("\n") + "\n\n"
    if desired in document.text:
        return
    if marker in document.text:
        raise RuntimeError(f"{path}: partial or divergent block already contains {marker}")
    count = document.text.count(anchor)
    if count != 1:
        raise RuntimeError(f"{path}: expected one block anchor, found {count}: {anchor}")
    document.text = document.text.replace(anchor, desired + anchor, 1)
    document.save()


def ensure_struct_members(
    path: Path, struct_name: str, old: str, desired: str, marker: str
) -> None:
    document = TextDocument.load(path)
    start, end = typedef_struct_span(document.text, struct_name)
    struct_text = document.text[start:end]
    if desired in struct_text:
        return
    if marker in struct_text:
        raise RuntimeError(f"{path}: partial {struct_name} bound-city members")
    count = struct_text.count(old)
    if count != 1:
        raise RuntimeError(
            f"{path}: expected one {struct_name} member anchor, found {count}"
        )
    struct_text = struct_text.replace(old, desired, 1)
    document.text = document.text[:start] + struct_text + document.text[end:]
    document.save()


def exact_function_text(path: Path, name: str) -> str:
    document = TextDocument.load(path)
    start, end = c_function_span(document.text, name)
    return document.text[start:end]

BOUND_STRUCT_DECL = r'''/* Cross-step batch binding. It is an internal read constraint, not wire
   state and never authorizes writing controller+0x38. */
typedef struct San9S5BoundCurrentCity {
    uint32_t controller_pointer;
    uint32_t city_pointer;
    uint32_t corps_pointer;
} San9S5BoundCurrentCity;'''

BOUND_READER_DECLS = r'''/* A bound capture substitutes the frozen city only in its local read view
   when the observed controller target is zero. A non-zero foreign target,
   controller drift, or corps drift returns CURRENT_CITY_INVALID. */
San9S5CurrentContextStatus san9_s5_bound_current_city_normalize(
    const San9S5BoundCurrentCity *bound,
    uint32_t observed_controller_pointer,
    uint32_t observed_controller_corps,
    uint32_t observed_controller_target,
    uint32_t *normalized_city_pointer);

San9S5CurrentContextStatus san9_s5_bound_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    const San9S5BoundCurrentCity *bound,
    uint32_t native_command_id,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);'''

BOUND_HANDLE_DECL = r'''San9S5CurrentContextStatus san9_s5_bound_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    const San9S5BoundCurrentCity *bound,
    uint32_t native_command_id,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);'''

BOUND_POLICY_FUNCTIONS = r'''static int s5_bound_current_city_shape_valid(
    const San9S5BoundCurrentCity *bound)
{
    uint32_t city_id;
    uint32_t corps_id;
    return bound != NULL
        && s5_valid_pointer(bound->controller_pointer)
        && s5_table_index(bound->city_pointer,
            S5_CITY_BASE, S5_CITY_STRIDE, S5_CITY_COUNT, &city_id)
        && s5_table_index(bound->corps_pointer,
            S5_CORPS_BASE, S5_CORPS_STRIDE, S5_CORPS_COUNT, &corps_id);
}

San9S5CurrentContextStatus san9_s5_bound_current_city_normalize(
    const San9S5BoundCurrentCity *bound,
    uint32_t observed_controller_pointer,
    uint32_t observed_controller_corps,
    uint32_t observed_controller_target,
    uint32_t *normalized_city_pointer)
{
    if (normalized_city_pointer != NULL) {
        *normalized_city_pointer = 0u;
    }
    if (!s5_bound_current_city_shape_valid(bound)
        || normalized_city_pointer == NULL) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
    if (observed_controller_pointer != bound->controller_pointer
        || observed_controller_corps != bound->corps_pointer
        || (observed_controller_target != 0u
            && observed_controller_target != bound->city_pointer)) {
        return SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID;
    }
    *normalized_city_pointer = bound->city_pointer;
    return SAN9_S5_CURRENT_CONTEXT_OK;
}'''

BOUND_READER_FUNCTIONS = r'''typedef struct S5BoundReaderContext {
    const San9S5CurrentContextReader *inner;
    const San9S5BoundCurrentCity *bound;
    int binding_drift;
} S5BoundReaderContext;

static int s5_bound_reader_read(
    void *context,
    uint32_t address,
    void *output,
    size_t output_size)
{
    S5BoundReaderContext *bound_context =
        (S5BoundReaderContext *)context;
    uint32_t controller_fields_address;
    if (bound_context == NULL || bound_context->inner == NULL
        || bound_context->inner->read == NULL
        || bound_context->bound == NULL || output == NULL
        || !bound_context->inner->read(
            bound_context->inner->context, address, output, output_size)) {
        return 0;
    }
    if (s5_add(bound_context->bound->controller_pointer,
            S5_CONTROLLER_CORPS_OFFSET, &controller_fields_address)
        && address == controller_fields_address
        && output_size == sizeof(uint32_t) * 3u) {
        uint8_t *bytes = (uint8_t *)output;
        uint32_t normalized_city = 0u;
        San9S5CurrentContextStatus status =
            san9_s5_bound_current_city_normalize(
                bound_context->bound,
                bound_context->bound->controller_pointer,
                s5_u32(bytes, 0u),
                s5_u32(bytes, 8u),
                &normalized_city);
        if (status != SAN9_S5_CURRENT_CONTEXT_OK) {
            bound_context->binding_drift = 1;
            normalized_city = bound_context->bound->city_pointer;
        }
        memcpy(bytes + 8u, &normalized_city, sizeof(normalized_city));
    }
    return 1;
}

static San9S5CurrentContextStatus s5_capture_reader_ab_native(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    uint32_t native_command_id,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    switch (native_command_id) {
    case S6_NATIVE_COMMAND_PATROL:
        return san9_s6_patrol_current_context_capture_reader_ab(
            reader, identity, first, second);
    case S5_NATIVE_COMMAND_COMMERCE:
        return san9_s5_current_context_capture_reader_ab(
            reader, identity, first, second);
    case S6_NATIVE_COMMAND_CULTIVATE:
        return san9_s6_cultivate_current_context_capture_reader_ab(
            reader, identity, first, second);
    case S6_NATIVE_COMMAND_REPAIR:
        return san9_s6_repair_current_context_capture_reader_ab(
            reader, identity, first, second);
    case S6_NATIVE_COMMAND_TRAIN:
        return san9_s6_train_current_context_capture_reader_ab(
            reader, identity, first, second);
    default:
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
}

San9S5CurrentContextStatus san9_s5_bound_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    const San9S5BoundCurrentCity *bound,
    uint32_t native_command_id,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    S5BoundReaderContext bound_context;
    San9S5CurrentContextReader bound_reader;
    San9S5CurrentContextStatus status;
    if (first != NULL) {
        memset(first, 0, sizeof(*first));
    }
    if (second != NULL) {
        memset(second, 0, sizeof(*second));
    }
    if (reader == NULL || reader->read == NULL
        || !s5_identity_shape_valid(identity)
        || !s5_bound_current_city_shape_valid(bound)
        || first == NULL || second == NULL || first == second) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
    memset(&bound_context, 0, sizeof(bound_context));
    memset(&bound_reader, 0, sizeof(bound_reader));
    bound_context.inner = reader;
    bound_context.bound = bound;
    bound_reader.read = s5_bound_reader_read;
    bound_reader.context = &bound_context;
    status = s5_capture_reader_ab_native(
        &bound_reader, identity, native_command_id, first, second);
    if (bound_context.binding_drift
        || status == SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID
        || (status == SAN9_S5_CURRENT_CONTEXT_OK
            && (first->controller_pointer != bound->controller_pointer
                || first->city_pointer != bound->city_pointer
                || first->corps_pointer != bound->corps_pointer
                || second->controller_pointer != bound->controller_pointer
                || second->city_pointer != bound->city_pointer
                || second->corps_pointer != bound->corps_pointer))) {
        memset(first, 0, sizeof(*first));
        memset(second, 0, sizeof(*second));
        return SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID;
    }
    return status;
}'''

BOUND_HANDLE_FUNCTION = r'''San9S5CurrentContextStatus san9_s5_bound_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    const San9S5BoundCurrentCity *bound,
    uint32_t native_command_id,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    San9S5ExpectedIdentity identity;
    San9S5HandleReadContext read_context;
    San9S5CurrentContextReader reader;
    San9S5CurrentContextStatus status;
    if (first != NULL) {
        memset(first, 0, sizeof(*first));
    }
    if (second != NULL) {
        memset(second, 0, sizeof(*second));
    }
    if (process == NULL || process == INVALID_HANDLE_VALUE
        || expected_process_id == 0u || expected_process_generation == 0u
        || expected_main_thread_id == 0u || expected_window == NULL
        || first == NULL || second == NULL || first == second) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
    memset(&identity, 0, sizeof(identity));
    memset(&read_context, 0, sizeof(read_context));
    memset(&reader, 0, sizeof(reader));
    identity.process_id = expected_process_id;
    identity.process_generation = expected_process_generation;
    identity.main_thread_id = expected_main_thread_id;
    identity.window_handle = (uint64_t)(uintptr_t)expected_window;
    if (!s5_handle_binding_matches(process, &identity, expected_window)) {
        return SAN9_S5_CURRENT_CONTEXT_BINDING_MISMATCH;
    }
    read_context.process = process;
    reader.read = s5_handle_read;
    reader.context = &read_context;
    status = san9_s5_bound_current_context_capture_reader_ab(
        &reader, &identity, bound, native_command_id, first, second);
    if (!s5_handle_binding_matches(process, &identity, expected_window)) {
        memset(first, 0, sizeof(*first));
        memset(second, 0, sizeof(*second));
        return SAN9_S5_CURRENT_CONTEXT_BINDING_MISMATCH;
    }
    return status;
}'''

S8_CAPTURE_COMMAND_FUNCTION = r'''static San9S5CurrentContextStatus s8_capture_command(
    HANDLE process,
    const San9P1M2bBootstrapConfig *config,
    uint64_t game_generation,
    uint32_t native_command_id,
    const San9S5BoundCurrentCity *bound,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    if (bound != NULL) {
        return san9_s5_bound_current_context_capture_handle_ab(process,
            config->target_pid, game_generation, config->target_thread_id,
            (HWND)(uintptr_t)config->target_hwnd, bound,
            native_command_id, first, second);
    }
    if (native_command_id == SAN9_P1_M2B_PATROL_NATIVE_ID) {
        return san9_s6_patrol_current_context_capture_handle_ab(process,
            config->target_pid, game_generation, config->target_thread_id,
            (HWND)(uintptr_t)config->target_hwnd, first, second);
    }
    if (native_command_id == SAN9_P1_M2B_CULTIVATE_NATIVE_ID) {
        return san9_s6_cultivate_current_context_capture_handle_ab(process,
            config->target_pid, game_generation, config->target_thread_id,
            (HWND)(uintptr_t)config->target_hwnd, first, second);
    }
    if (native_command_id == SAN9_P1_M2B_TRAIN_NATIVE_ID) {
        return san9_s6_train_current_context_capture_handle_ab(process,
            config->target_pid, game_generation, config->target_thread_id,
            (HWND)(uintptr_t)config->target_hwnd, first, second);
    }
    if (native_command_id == SAN9_P1_M2B_REPAIR_NATIVE_ID) {
        return san9_s6_repair_current_context_capture_handle_ab(process,
            config->target_pid, game_generation, config->target_thread_id,
            (HWND)(uintptr_t)config->target_hwnd, first, second);
    }
    if (native_command_id != SAN9_P1_M2B_COMMERCE_NATIVE_ID) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
    return san9_s5_current_context_capture_handle_ab(process,
        config->target_pid, game_generation, config->target_thread_id,
        (HWND)(uintptr_t)config->target_hwnd, first, second);
}'''

BRIDGE_CAPTURE_AB_FUNCTION = r'''static San9S5CurrentContextStatus s5_capture_ab(
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    San9S5CurrentContextReader reader;
    San9S5ExpectedIdentity identity;
    memset(&reader, 0, sizeof(reader));
    memset(&identity, 0, sizeof(identity));
    reader.read = s5_direct_read;
    identity.process_id = g_runtime.binding_frame.game_pid;
    identity.main_thread_id = g_runtime.binding_frame.main_tid;
    identity.process_generation = g_runtime.binding_frame.game_generation;
    identity.window_handle = g_runtime.binding_frame.game_hwnd;
#if SAN9_COMBINED_BATCH_BUILD
    if (g_runtime.shared != NULL && g_runtime.shared->operation_mode
            == SAN9_ACTIVE_APPLY_MODE) {
        if (g_runtime.s8_bound_city_valid != 0u) {
            return san9_s5_bound_current_context_capture_reader_ab(
                &reader, &identity, &g_runtime.s8_bound_city,
                s8_active_native_id(), first, second);
        }
        switch (s8_active_native_id()) {
        case SAN9_P1_M2B_PATROL_NATIVE_ID:
            return san9_s6_patrol_current_context_capture_reader_ab(
                &reader, &identity, first, second);
        case SAN9_P1_M2B_COMMERCE_NATIVE_ID:
            return san9_s5_current_context_capture_reader_ab(
                &reader, &identity, first, second);
        case SAN9_P1_M2B_CULTIVATE_NATIVE_ID:
            return san9_s6_cultivate_current_context_capture_reader_ab(
                &reader, &identity, first, second);
        case SAN9_P1_M2B_TRAIN_NATIVE_ID:
            return san9_s6_train_current_context_capture_reader_ab(
                &reader, &identity, first, second);
        case SAN9_P1_M2B_REPAIR_NATIVE_ID:
            return san9_s6_repair_current_context_capture_reader_ab(
                &reader, &identity, first, second);
        default:
            return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
        }
    }
#endif
    if (g_runtime.shared != NULL && g_runtime.shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE) {
        return san9_s6_repair_current_context_capture_reader_ab(
            &reader, &identity, first, second);
    }
    if (g_runtime.shared != NULL && g_runtime.shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE) {
        return san9_s6_train_current_context_capture_reader_ab(
            &reader, &identity, first, second);
    }
    if (g_runtime.shared != NULL && g_runtime.shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE) {
        return san9_s6_patrol_current_context_capture_reader_ab(
            &reader, &identity, first, second);
    }
    return g_runtime.shared != NULL
            && g_runtime.shared->operation_mode
                == SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE
        ? san9_s6_cultivate_current_context_capture_reader_ab(
            &reader, &identity, first, second)
        : san9_s5_current_context_capture_reader_ab(
            &reader, &identity, first, second);
}'''

BOUND_TEST_BLOCK = r'''typedef struct BoundFixture {
    uint32_t window_address;
    uint32_t owner_address;
    uint32_t scene_address;
    uint32_t scheduler_address;
    uint32_t controller_address;
    uint32_t city_address;
    uint32_t corps_address;
    uint32_t node_address[SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT];
    uint32_t person_address[SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT];
    uint32_t target_a;
    uint32_t target_b;
    uint32_t controller_field_reads;
    uint8_t app[8u];
    uint8_t scheduler[0x14u];
    uint8_t controller[0x14u];
    uint8_t controller_fields[0x0Cu];
    uint8_t city[0x1F0u];
    uint8_t corps[0xD4u];
    uint8_t node[SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT][0x0Cu];
    uint8_t person[SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT][0xF8u];
} BoundFixture;

static int bound_fixture_read(
    void *context, uint32_t address, void *output, size_t output_size)
{
    BoundFixture *fixture = (BoundFixture *)context;
    uint32_t value;
    uint32_t index;
    if (fixture == NULL || output == NULL) {
        return 0;
    }
    if (address == UINT32_C(0x01228340)
        && output_size == sizeof(fixture->app)) {
        memcpy(output, fixture->app, output_size);
        return 1;
    }
    if (address == fixture->window_address + UINT32_C(0x1C)
        && output_size == sizeof(value)) {
        value = fixture->owner_address;
        memcpy(output, &value, sizeof(value));
        return 1;
    }
    if (address == fixture->owner_address + UINT32_C(0x18)
        && output_size == sizeof(value)) {
        value = fixture->scene_address;
        memcpy(output, &value, sizeof(value));
        return 1;
    }
    if (address == fixture->scene_address + UINT32_C(0x8C)
        && output_size == sizeof(value)) {
        value = fixture->scheduler_address;
        memcpy(output, &value, sizeof(value));
        return 1;
    }
    if (address == fixture->scheduler_address
        && output_size == sizeof(fixture->scheduler)) {
        memcpy(output, fixture->scheduler, output_size);
        return 1;
    }
    if (address == fixture->controller_address
        && output_size == sizeof(fixture->controller)) {
        memcpy(output, fixture->controller, output_size);
        return 1;
    }
    if (address == fixture->controller_address + UINT32_C(0x30)
        && output_size == sizeof(fixture->controller_fields)) {
        memcpy(output, fixture->controller_fields, output_size);
        value = fixture->controller_field_reads++ == 0u
            ? fixture->target_a : fixture->target_b;
        memcpy((uint8_t *)output + 8u, &value, sizeof(value));
        return 1;
    }
    if (address == fixture->city_address
        && output_size == sizeof(fixture->city)) {
        memcpy(output, fixture->city, output_size);
        return 1;
    }
    if (address == fixture->corps_address
        && output_size == sizeof(fixture->corps)) {
        memcpy(output, fixture->corps, output_size);
        return 1;
    }
    for (index = 0u; index < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT; ++index) {
        if (address == fixture->node_address[index]
            && output_size == sizeof(fixture->node[index])) {
            memcpy(output, fixture->node[index], output_size);
            return 1;
        }
        if (address == fixture->person_address[index]
            && output_size == sizeof(fixture->person[index])) {
            memcpy(output, fixture->person[index], output_size);
            return 1;
        }
    }
    return 0;
}

static void bound_fixture_initialize(
    BoundFixture *fixture,
    San9S5CurrentContextReader *reader,
    San9S5ExpectedIdentity *identity,
    San9S5BoundCurrentCity *bound)
{
    uint32_t index;
    memset(fixture, 0, sizeof(*fixture));
    memset(reader, 0, sizeof(*reader));
    memset(identity, 0, sizeof(*identity));
    memset(bound, 0, sizeof(*bound));
    fixture->window_address = UINT32_C(0x00110000);
    fixture->owner_address = UINT32_C(0x00111000);
    fixture->scene_address = UINT32_C(0x00112000);
    fixture->scheduler_address = UINT32_C(0x00113000);
    fixture->controller_address = UINT32_C(0x00114000);
    fixture->city_address = UINT32_C(0x0124DB58) + UINT32_C(0x1F0);
    fixture->corps_address = UINT32_C(0x01253C38) + UINT32_C(0xD4);
    fixture_u32(fixture->app, 0u, UINT32_C(0x00604DD0));
    fixture_u32(fixture->app, 4u, fixture->window_address);
    fixture_u32(fixture->scheduler, 0u, UINT32_C(0x00607560));
    fixture_u32(fixture->scheduler, 0x0Cu, fixture->controller_address);
    fixture_u32(fixture->scheduler, 0x10u, 0u);
    fixture_u32(fixture->controller, 0u, UINT32_C(0x00610BC8));
    fixture_u32(fixture->controller, 0x0Cu, 0u);
    fixture_u32(fixture->controller, 0x10u, fixture->controller_address);
    fixture_u32(fixture->controller_fields, 0u, fixture->corps_address);
    fixture_u32(fixture->controller_fields, 4u, UINT32_C(0x3E9));
    fixture_u32(fixture->controller_fields, 8u, 0u);
    fixture_u32(fixture->city, 0u, UINT32_C(0x00605938));
    fixture->city[0x06u] = 5u;
    fixture_u32(fixture->city, 0x58u, UINT32_C(0x00606490));
    fixture_u32(fixture->city, 0xBCu, fixture->city_address);
    fixture_u32(fixture->city, 0xCCu, fixture->corps_address);
    fixture_u32(fixture->city, 0xE0u, UINT32_C(0x00200000));
    fixture_u32(fixture->city, 0xE4u,
        UINT32_C(0x00200000)
            + (SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT - 1u) * UINT32_C(0x20));
    fixture_u32(fixture->city, 0xE8u,
        SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT);
    fixture_u32(fixture->city, 0x1C4u, 100u);
    fixture_u32(fixture->city, 0x1C8u, 1000u);
    fixture_u32(fixture->city, 0x1CCu, 100u);
    fixture_u32(fixture->city, 0x1D0u, 100u);
    fixture_u32(fixture->city, 0x1D4u, 500u);
    fixture_u32(fixture->city, 0x1D8u, 500u);
    fixture_u32(fixture->city, 0x1E0u, 0u);
    fixture_u32(fixture->city, 0x88u, 10000u);
    fixture_u32(fixture->city, 0x90u, 80u);
    fixture_u16(fixture->city, 0x3Cu, 500u);
    fixture_u16(fixture->city, 0x3Eu, 0u);
    fixture_u32(fixture->corps, 0x14u, 1000u);
    fixture_u32(fixture->corps, 0x34u, 1u);
    fixture_u32(fixture->corps, 0xB8u, fixture->corps_address);
    fixture_u32(fixture->corps, 0xBCu, UINT32_C(0x01258EE0));
    for (index = 0u; index < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT; ++index) {
        uint32_t previous = index == 0u ? 0u
            : UINT32_C(0x00200000) + (index - 1u) * UINT32_C(0x20);
        uint32_t next = index + 1u == SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT
            ? 0u : UINT32_C(0x00200000) + (index + 1u) * UINT32_C(0x20);
        fixture->node_address[index] =
            UINT32_C(0x00200000) + index * UINT32_C(0x20);
        fixture->person_address[index] =
            UINT32_C(0x01258EE0) + index * UINT32_C(0x128);
        fixture_u32(fixture->node[index], 0u, next);
        fixture_u32(fixture->node[index], 4u, previous);
        fixture_u32(fixture->node[index], 8u,
            fixture->person_address[index]);
        fixture_u16(fixture->person[index], 0x04u, (uint16_t)index);
        fixture_u32(fixture->person[index], 0x60u, 100u - index);
        fixture_u32(fixture->person[index], 0x84u, 0u);
        fixture_u32(fixture->person[index], 0xE8u, 0u);
        fixture_u32(fixture->person[index], 0xF4u,
            fixture->city_address + UINT32_C(0x58));
    }
    fixture->target_a = 0u;
    fixture->target_b = fixture->city_address;
    reader->read = bound_fixture_read;
    reader->context = fixture;
    identity->process_id = 10u;
    identity->main_thread_id = 11u;
    identity->process_generation = UINT64_C(12);
    identity->window_handle = UINT64_C(13);
    bound->controller_pointer = fixture->controller_address;
    bound->city_pointer = fixture->city_address;
    bound->corps_pointer = fixture->corps_address;
}

static void test_s8_bound_current_city_policy(void)
{
    BoundFixture fixture;
    San9S5CurrentContextReader reader;
    San9S5ExpectedIdentity identity;
    San9S5BoundCurrentCity bound;
    San9S5CurrentContextSnapshot first;
    San9S5CurrentContextSnapshot second;
    San9S5CurrentContextSnapshot zero_snapshot;
    San9S5CurrentContextStatus status;
    uint32_t normalized = UINT32_C(0xFFFFFFFF);
    bound_fixture_initialize(&fixture, &reader, &identity, &bound);
    memset(&first, 0, sizeof(first));
    memset(&second, 0, sizeof(second));
    memset(&zero_snapshot, 0, sizeof(zero_snapshot));
    check(san9_s5_bound_current_city_normalize(&bound,
            bound.controller_pointer, bound.corps_pointer, 0u, &normalized)
            == SAN9_S5_CURRENT_CONTEXT_OK
        && normalized == bound.city_pointer,
        "s8-bound-target-zero-normalized-to-frozen-city");
    normalized = 0u;
    check(san9_s5_bound_current_city_normalize(&bound,
            bound.controller_pointer, bound.corps_pointer,
            bound.city_pointer, &normalized) == SAN9_S5_CURRENT_CONTEXT_OK
        && normalized == bound.city_pointer,
        "s8-bound-same-city-target-accepted");
    normalized = UINT32_C(0xFFFFFFFF);
    check(san9_s5_bound_current_city_normalize(&bound,
            bound.controller_pointer, bound.corps_pointer,
            bound.city_pointer + UINT32_C(0x1F0), &normalized)
            == SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID
        && normalized == 0u,
        "s8-bound-foreign-nonzero-target-rejected");
    normalized = UINT32_C(0xFFFFFFFF);
    check(san9_s5_bound_current_city_normalize(&bound,
            bound.controller_pointer + 4u, bound.corps_pointer,
            0u, &normalized)
            == SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID
        && normalized == 0u,
        "s8-bound-controller-drift-rejected");
    normalized = UINT32_C(0xFFFFFFFF);
    check(san9_s5_bound_current_city_normalize(&bound,
            bound.controller_pointer, bound.corps_pointer + UINT32_C(0xD4),
            0u, &normalized)
            == SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID
        && normalized == 0u,
        "s8-bound-corps-drift-rejected");
    fixture.controller_field_reads = 0u;
    status = san9_s5_bound_current_context_capture_reader_ab(
        &reader, &identity, &bound, SAN9_S5_COMMERCE_NATIVE_ID,
        &first, &second);
    check(status == SAN9_S5_CURRENT_CONTEXT_OK
        && first.controller_target == bound.city_pointer
        && first.city_pointer == bound.city_pointer
        && first.corps_pointer == bound.corps_pointer,
        "s8-controller-path-reader-a-zero-uses-frozen-city");
    check(status == SAN9_S5_CURRENT_CONTEXT_OK
        && second.controller_target == bound.city_pointer
        && san9_s5_current_context_equal(&first, &second),
        "s8-bridge-path-reader-b-same-city-remains-frozen");
    fixture.controller_field_reads = 0u;
    fixture.target_b = bound.city_pointer + UINT32_C(0x1F0);
    memset(&first, 0xA5, sizeof(first));
    memset(&second, 0xA5, sizeof(second));
    status = san9_s5_bound_current_context_capture_reader_ab(
        &reader, &identity, &bound, SAN9_S5_COMMERCE_NATIVE_ID,
        &first, &second);
    check(status == SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID
        && memcmp(&first, &zero_snapshot, sizeof(first)) == 0
        && memcmp(&second, &zero_snapshot, sizeof(second)) == 0,
        "s8-bound-reader-b-foreign-city-fails-closed-and-clears");
    fixture.controller_field_reads = 0u;
    fixture.target_a = bound.city_pointer + UINT32_C(0x1F0);
    fixture.target_b = bound.city_pointer;
    memset(&first, 0xA5, sizeof(first));
    memset(&second, 0xA5, sizeof(second));
    status = san9_s5_bound_current_context_capture_reader_ab(
        &reader, &identity, &bound, SAN9_S5_COMMERCE_NATIVE_ID,
        &first, &second);
    check(status == SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID
        && memcmp(&first, &zero_snapshot, sizeof(first)) == 0
        && memcmp(&second, &zero_snapshot, sizeof(second)) == 0,
        "s8-bound-reader-a-foreign-city-fails-closed-and-clears");
}'''

SOURCE_TARGETS = (
    "native/San9BridgeP1EasyPingM2b/include/s5_current_context.h",
    "native/San9BridgeP1EasyPingM2b/src/s5_current_context.c",
    "native/San9BridgeP1EasyPingM2b/src/controller.c",
    "native/San9BridgeP1EasyPingM2b/src/bridge_dll.c",
    "native/San9BridgeP1EasyPingM2b/src/offline_selftest.c",
    "native/San9BridgeP1EasyPingM2b/build.ps1",
    "src/San9AutoDomestic.UI/NativeControllerClient.cs",
    "tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs",
)


def patch_header() -> None:
    path = ROOT / "native/San9BridgeP1EasyPingM2b/include/s5_current_context.h"
    ensure_block_before(
        path,
        "typedef int (*San9S5CurrentContextReadCallback)(\n",
        "typedef struct San9S5BoundCurrentCity",
        BOUND_STRUCT_DECL,
    )
    ensure_block_before(
        path,
        "int san9_s5_current_context_business_digest(\n",
        "san9_s5_bound_current_context_capture_reader_ab",
        BOUND_READER_DECLS,
    )
    document = TextDocument.load(path)
    desired = BOUND_HANDLE_DECL.strip("\n") + "\n\n"
    if desired not in document.text:
        if "san9_s5_bound_current_context_capture_handle_ab" in document.text:
            raise RuntimeError(f"{path}: divergent bound handle declaration")
        anchor = "#endif\n\nint san9_s5_current_context_digest(\n"
        if document.text.count(anchor) != 1:
            raise RuntimeError(f"{path}: Win32 handle declaration anchor not unique")
        document.text = document.text.replace(anchor, desired + anchor, 1)
        document.save()


def patch_context_source() -> None:
    path = ROOT / "native/San9BridgeP1EasyPingM2b/src/s5_current_context.c"
    ensure_block_before(
        path,
        "static void s5_hash_u16(San9P1Sha256Context *sha, uint16_t value)\n",
        "san9_s5_bound_current_city_normalize",
        BOUND_POLICY_FUNCTIONS,
    )
    ensure_block_before(
        path,
        "#if defined(_WIN32) && !defined(SAN9_S5_CONTEXT_NO_HANDLE)\n",
        "typedef struct S5BoundReaderContext",
        BOUND_READER_FUNCTIONS,
    )
    document = TextDocument.load(path)
    desired = BOUND_HANDLE_FUNCTION.strip("\n") + "\n\n"
    if desired not in document.text:
        if "san9_s5_bound_current_context_capture_handle_ab" in document.text:
            raise RuntimeError(f"{path}: divergent bound handle function")
        anchor = "#endif\n\nconst char *san9_s5_current_context_status_name(\n"
        if document.text.count(anchor) != 1:
            raise RuntimeError(f"{path}: handle implementation anchor not unique")
        document.text = document.text.replace(anchor, desired + anchor, 1)
        document.save()


def _patch_s8_batch_body(function_text: str) -> str:
    updated = function_text
    updated = scoped_replace_once(
        updated,
        "    uint32_t bound_controller = 0u;\n"
        "    uint32_t bound_city = 0u;\n"
        "    uint32_t bound_corps = 0u;\n",
        "    San9S5BoundCurrentCity bound_city;\n",
        "run_s8_batch_session bound declarations",
    )
    updated = scoped_replace_once(
        updated,
        "    memset(&step_evidence, 0, sizeof(step_evidence));\n"
        "    memset(binding_digest, 0, sizeof(binding_digest));\n",
        "    memset(&step_evidence, 0, sizeof(step_evidence));\n"
        "    memset(&bound_city, 0, sizeof(bound_city));\n"
        "    memset(binding_digest, 0, sizeof(binding_digest));\n",
        "run_s8_batch_session bound initialization",
    )
    updated = scoped_replace_once(
        updated,
        "            status = s8_capture_command(process, config,\n"
        "                binding.game_generation, commands[step], &first, &second);\n",
        "            status = s8_capture_command(process, config,\n"
        "                binding.game_generation, commands[step],\n"
        "                bound_city.controller_pointer == 0u ? NULL : &bound_city,\n"
        "                &first, &second);\n",
        "run_s8_batch_session bound capture call",
    )
    updated = scoped_replace_once(
        updated,
        "        if (bound_city == 0u) {\n"
        "            bound_controller = first.controller_pointer;\n"
        "            bound_city = first.city_pointer;\n"
        "            bound_corps = first.corps_pointer;\n"
        "        } else if (first.controller_pointer != bound_controller\n"
        "            || first.city_pointer != bound_city\n"
        "            || first.corps_pointer != bound_corps) {\n",
        "        if (bound_city.controller_pointer == 0u) {\n"
        "            bound_city.controller_pointer = first.controller_pointer;\n"
        "            bound_city.city_pointer = first.city_pointer;\n"
        "            bound_city.corps_pointer = first.corps_pointer;\n"
        "        } else if (first.controller_pointer != bound_city.controller_pointer\n"
        "            || first.city_pointer != bound_city.city_pointer\n"
        "            || first.corps_pointer != bound_city.corps_pointer) {\n",
        "run_s8_batch_session frozen binding",
    )
    updated = scoped_replace_once(
        updated,
        "    san9_p1_secure_zero(&step_evidence, sizeof(step_evidence));\n"
        "    san9_p1_secure_zero(binding_digest, sizeof(binding_digest));\n",
        "    san9_p1_secure_zero(&step_evidence, sizeof(step_evidence));\n"
        "    san9_p1_secure_zero(&bound_city, sizeof(bound_city));\n"
        "    san9_p1_secure_zero(binding_digest, sizeof(binding_digest));\n",
        "run_s8_batch_session bound cleanup",
    )
    return updated


def patch_controller() -> None:
    path = ROOT / "native/San9BridgeP1EasyPingM2b/src/controller.c"
    replace_c_function(path, "s8_capture_command", S8_CAPTURE_COMMAND_FUNCTION)
    rewrite_c_function(path, "run_s8_batch_session", _patch_s8_batch_body)


def _patch_bootstrap_bound_reset(function_text: str) -> str:
    return scoped_replace_once(
        function_text,
        "    g_runtime.owner_token = shared->owner_token;\n"
        "    g_runtime.main_thread_id = shared->target_thread_id;\n",
        "    g_runtime.owner_token = shared->owner_token;\n"
        "    g_runtime.main_thread_id = shared->target_thread_id;\n"
        "#if SAN9_COMBINED_BATCH_BUILD\n"
        "    memset(&g_runtime.s8_bound_city, 0, sizeof(g_runtime.s8_bound_city));\n"
        "    g_runtime.s8_bound_city_valid = 0u;\n"
        "#endif\n",
        "handle_bootstrap_message bound reset",
    )


def _patch_bridge_bound_lock(function_text: str) -> str:
    return scoped_replace_once(
        function_text,
        "    g_runtime.s5_pre = first;\n"
        "#if SAN9_COMBINED_BATCH_BUILD\n"
        "    s8_select_exact_vtables(first.native_command_id);\n",
        "#if SAN9_COMBINED_BATCH_BUILD\n"
        "    if (shared->operation_mode == SAN9_ACTIVE_APPLY_MODE\n"
        "        && g_runtime.s8_bound_city_valid == 0u) {\n"
        "        g_runtime.s8_bound_city.controller_pointer = first.controller_pointer;\n"
        "        g_runtime.s8_bound_city.city_pointer = first.city_pointer;\n"
        "        g_runtime.s8_bound_city.corps_pointer = first.corps_pointer;\n"
        "        MemoryBarrier();\n"
        "        g_runtime.s8_bound_city_valid = 1u;\n"
        "    }\n"
        "#endif\n"
        "    g_runtime.s5_pre = first;\n"
        "#if SAN9_COMBINED_BATCH_BUILD\n"
        "    s8_select_exact_vtables(first.native_command_id);\n",
        "s5_start_no_apply bound lock",
    )


def patch_bridge() -> None:
    path = ROOT / "native/San9BridgeP1EasyPingM2b/src/bridge_dll.c"
    ensure_struct_members(
        path,
        "M2bRuntime",
        "    uint32_t s8_native_id_latch;\n"
        "    uint32_t s8_request_latched;\n"
        "    San9S5NoApplyGate s8_step_gates[5];\n",
        "    uint32_t s8_native_id_latch;\n"
        "    uint32_t s8_request_latched;\n"
        "    San9S5BoundCurrentCity s8_bound_city;\n"
        "    uint32_t s8_bound_city_valid;\n"
        "    San9S5NoApplyGate s8_step_gates[5];\n",
        "s8_bound_city",
    )
    rewrite_c_function(path, "handle_bootstrap_message", _patch_bootstrap_bound_reset)
    replace_c_function(path, "s5_capture_ab", BRIDGE_CAPTURE_AB_FUNCTION)
    rewrite_c_function(path, "s5_start_no_apply", _patch_bridge_bound_lock)


def patch_offline_tests() -> None:
    path = ROOT / "native/San9BridgeP1EasyPingM2b/src/offline_selftest.c"
    ensure_block_before(path, "typedef struct FakeRead {\n", "typedef struct BoundFixture", BOUND_TEST_BLOCK)
    rewrite_c_function(
        path,
        "main",
        lambda body: scoped_replace_once(
            body,
            "    test_s5_v8_frozen_post();\n"
            "    test_s5_no_apply_contract();\n",
            "    test_s5_v8_frozen_post();\n"
            "    test_s8_bound_current_city_policy();\n"
            "    test_s5_no_apply_contract();\n",
            "offline main bound test registration",
        ),
    )


def _patch_describe_json_line(function_text: str) -> str:
    return scoped_replace_once(
        function_text,
        "            if (string.Equals(phase, \"BATCH_REBIND_REQUIRED\", StringComparison.Ordinal))\n"
        "            {\n"
        "                return \"当前城市绑定已变化；本次停止，不会重试。\";\n"
        "            }\n",
        "            if (string.Equals(phase, \"BATCH_REBIND_REQUIRED\", StringComparison.Ordinal))\n"
        "            {\n"
        "                return string.Format(\n"
        "                    CultureInfo.InvariantCulture,\n"
        "                    \"当前城市绑定已变化；停止前已执行 {0}，跳过 {1}，不会重试。\",\n"
        "                    NativeJson.IntegerValue(json, \"executed\", 0),\n"
        "                    NativeJson.IntegerValue(json, \"skipped\", 0));\n"
        "            }\n",
        "DescribeJsonLine rebind summary",
    )


def _patch_protocol_accept_line(function_text: str) -> str:
    return scoped_replace_once(
        function_text,
        "            if (string.Equals(phase, \"BATCH_REBIND_REQUIRED\", StringComparison.Ordinal))\n"
        "            {\n"
        "                rebindRequired = true;\n"
        "                state = NativeControllerState.RebindRequired;\n"
        "            }\n",
        "            if (string.Equals(phase, \"BATCH_REBIND_REQUIRED\", StringComparison.Ordinal))\n"
        "            {\n"
        "                rebindRequired = true;\n"
        "                executed = NativeJson.IntegerValue(json, \"executed\", executed);\n"
        "                skipped = NativeJson.IntegerValue(json, \"skipped\", skipped);\n"
        "                state = NativeControllerState.RebindRequired;\n"
        "            }\n",
        "AcceptLine rebind counts",
    )


def patch_ui() -> None:
    path = ROOT / "src/San9AutoDomestic.UI/NativeControllerClient.cs"
    rewrite_c_function(path, "DescribeJsonLine", _patch_describe_json_line)
    rewrite_c_function(path, "AcceptLine", _patch_protocol_accept_line)

    tests = ROOT / "tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs"
    rewrite_c_function(
        tests,
        "NativeBatchSuccessRequiresCompleteAndFinalResult",
        lambda body: scoped_replace_once(
            body,
            "            AssertEx.Equal(\n"
            "                NativeControllerState.RebindRequired,\n"
            "                rebind.Complete(23, string.Empty).State,\n"
            "                \"Rebind must be a distinct terminal state.\");\n",
            "            NativeBatchResult rebindResult = rebind.Complete(23, string.Empty);\n"
            "            AssertEx.Equal(\n"
            "                NativeControllerState.RebindRequired,\n"
            "                rebindResult.State,\n"
            "                \"Rebind must be a distinct terminal state.\");\n"
            "            AssertEx.Equal(1, rebindResult.Executed,\n"
            "                \"Rebind must retain the already executed count.\");\n"
            "            AssertEx.Equal(0, rebindResult.Skipped,\n"
            "                \"Rebind must retain the skipped count.\");\n"
            "            AssertEx.Contains(\n"
            "                \"停止前已执行 1，跳过 0\",\n"
            "                NativeControllerClient.DescribeJsonLine(\n"
            "                    \"{\\\"phase\\\":\\\"BATCH_REBIND_REQUIRED\\\",\\\"executed\\\":1,\\\"skipped\\\":0}\"),\n"
            "                \"The visible rebind line must preserve completed work.\");\n",
            "UI rebind assertions",
        ),
    )


NORMALIZED_HASH_FUNCTION = r'''function Get-NormalizedSourceSha256([string]$Path) {
    [byte[]]$raw = [IO.File]::ReadAllBytes($Path)
    [byte[]]$normalized = [byte[]]::new($raw.Length)
    $write = 0
    for ($read = 0; $read -lt $raw.Length; ++$read) {
        if ($raw[$read] -eq 13 -and $read + 1 -lt $raw.Length
            -and $raw[$read + 1] -eq 10) {
            continue
        }
        $normalized[$write] = $raw[$read]
        ++$write
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        [byte[]]$hash = $sha.ComputeHash($normalized, 0, $write)
        return ([BitConverter]::ToString($hash)).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}'''


def patch_native_build_hashing() -> None:
    path = ROOT / "native/San9BridgeP1EasyPingM2b/build.ps1"
    ensure_block_before(path, "$expected = @{\n", "function Get-NormalizedSourceSha256", NORMALIZED_HASH_FUNCTION)
    document = TextDocument.load(path)
    document.text = scoped_replace_once(
        document.text,
        "    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $pin[0]).Hash\n",
        "    $actual = Get-NormalizedSourceSha256 -Path $pin[0]\n",
        "native source pin CRLF normalization",
    )
    document.save()


def update_source_pins() -> None:
    build = ROOT / "native/San9BridgeP1EasyPingM2b/build.ps1"
    document = TextDocument.load(build)
    rel_paths = [
        r"include\s5_current_context.h",
        r"src\s5_current_context.c",
        r"src\controller.c",
        r"src\bridge_dll.c",
        r"src\offline_selftest.c",
    ]
    for rel in rel_paths:
        source = ROOT / "native/San9BridgeP1EasyPingM2b" / Path(rel.replace("\\", "/"))
        digest = source_sha256(source)
        pattern = re.compile(
            r"(@\(\(Join-Path \$PSScriptRoot '" + re.escape(rel)
            + r"'\),')([A-F0-9]{64})('\),)"
        )
        document.text, count = pattern.subn(
            lambda match, value=digest: match.group(1) + value + match.group(3),
            document.text,
            count=1,
        )
        if count != 1:
            raise RuntimeError(f"could not update normalized source pin for {rel}")
    document.save()


def verify_source_paths() -> None:
    context = ROOT / "native/San9BridgeP1EasyPingM2b/src/s5_current_context.c"
    controller = ROOT / "native/San9BridgeP1EasyPingM2b/src/controller.c"
    bridge = ROOT / "native/San9BridgeP1EasyPingM2b/src/bridge_dll.c"
    offline = ROOT / "native/San9BridgeP1EasyPingM2b/src/offline_selftest.c"

    bound_reader = exact_function_text(
        context, "san9_s5_bound_current_context_capture_reader_ab"
    )
    bound_read = exact_function_text(context, "s5_bound_reader_read")
    controller_capture = exact_function_text(controller, "s8_capture_command")
    controller_batch = exact_function_text(controller, "run_s8_batch_session")
    bridge_capture = exact_function_text(bridge, "s5_capture_ab")
    bridge_start = exact_function_text(bridge, "s5_start_no_apply")
    offline_test = exact_function_text(offline, "test_s8_bound_current_city_policy")

    checks = {
        "bound-reader-local-substitution": (
            "memcpy(bytes + 8u, &normalized_city" in bound_read
            and "binding_drift = 1" in bound_read
            and "WriteProcessMemory" not in bound_read
        ),
        "bound-reader-ab-clear-on-drift": (
            "bound_context.binding_drift" in bound_reader
            and "memset(first, 0" in bound_reader
            and "memset(second, 0" in bound_reader
        ),
        "controller-bound-handle-path": (
            "san9_s5_bound_current_context_capture_handle_ab" in controller_capture
            and "bound_city.controller_pointer == 0u ? NULL : &bound_city"
            in controller_batch
            and "first.city_pointer != bound_city.city_pointer" in controller_batch
        ),
        "bridge-bound-reader-path": (
            "g_runtime.s8_bound_city_valid != 0u" in bridge_capture
            and "san9_s5_bound_current_context_capture_reader_ab" in bridge_capture
            and "g_runtime.s8_bound_city_valid = 1u" in bridge_start
        ),
        "reader-a-b-offline-tests": (
            "s8-controller-path-reader-a-zero-uses-frozen-city" in offline_test
            and "s8-bridge-path-reader-b-same-city-remains-frozen" in offline_test
            and "s8-bound-reader-b-foreign-city-fails-closed-and-clears"
            in offline_test
            and "s8-bound-reader-a-foreign-city-fails-closed-and-clears"
            in offline_test
        ),
        "no-controller-target-write": (
            "WriteProcessMemory" not in bound_reader
            and "controller+0x38" not in controller_capture
            and "controller+0x38" not in bridge_capture
        ),
    }
    failed = [name for name, passed in checks.items() if not passed]
    if failed:
        raise RuntimeError("source path verification failed: " + ", ".join(failed))


def patch_source() -> None:
    patch_header()
    patch_context_source()
    patch_controller()
    patch_bridge()
    patch_offline_tests()
    patch_ui()
    patch_native_build_hashing()
    update_source_pins()
    verify_source_paths()


def parse_native_expected(text: str, role: str) -> str:
    match = re.search(
        r"^\s*" + re.escape(role) + r"\s*=\s*'([A-F0-9]{64})'",
        text,
        re.MULTILINE,
    )
    if not match:
        raise RuntimeError(f"native expected role not found: {role}")
    return match.group(1)


def patch_outputs(artifact_root: Path) -> None:
    roles = {
        "Offline": "offline.exe",
        "Controller": "controller.exe",
        "Dll": "bridge.dll",
        "ApplyDll": "bridge_apply_once.dll",
        "CultivateDll": "bridge_s6_cultivate_apply_once.dll",
        "PatrolDll": "bridge_s6_patrol_apply_once.dll",
        "TrainDll": "bridge_s6_train_apply_once.dll",
        "RepairDll": "bridge_s6_repair_apply_once.dll",
        "S8BasicDll": "bridge_s8_basic_batch.dll",
        "S8WealthyDll": "bridge_s8_wealthy_batch.dll",
    }
    hashes = {role: binary_sha256(artifact_root / name) for role, name in roles.items()}
    native_build = ROOT / "native/San9BridgeP1EasyPingM2b/build.ps1"
    document = TextDocument.load(native_build)
    old_runtime = {
        "Controller": parse_native_expected(document.text, "Controller"),
        "S8BasicDll": parse_native_expected(document.text, "S8BasicDll"),
        "S8WealthyDll": parse_native_expected(document.text, "S8WealthyDll"),
    }
    for role, digest in hashes.items():
        pattern = re.compile(
            r"(^\s*" + re.escape(role) + r"\s*=\s*')[A-F0-9]{64}(')",
            re.MULTILINE,
        )
        document.text, count = pattern.subn(
            lambda match, value=digest: match.group(1) + value + match.group(2),
            document.text,
            count=1,
        )
        if count != 1:
            raise RuntimeError(f"could not update native expected role {role}")
    document.save()

    root_build = ROOT / "build.ps1"
    root_document = TextDocument.load(root_build)
    for role in ("Controller", "S8BasicDll", "S8WealthyDll"):
        old = old_runtime[role]
        new = hashes[role]
        count = root_document.text.count(old)
        if count < 2:
            raise RuntimeError(
                f"root build expected at least two {role} hash occurrences, found {count}"
            )
        root_document.text = root_document.text.replace(old, new)
    root_document.save()

    ui = ROOT / "src/San9AutoDomestic.UI/NativeControllerClient.cs"
    ui_document = TextDocument.load(ui)
    for role in ("Controller", "S8BasicDll", "S8WealthyDll"):
        old = old_runtime[role]
        new = hashes[role]
        count = ui_document.text.count(old)
        if count != 1:
            raise RuntimeError(f"UI expected one {role} hash occurrence, found {count}")
        ui_document.text = ui_document.text.replace(old, new)
    ui_document.save()
    print(json.dumps(hashes, sort_keys=True))


def patch_state(artifact_root: Path, native_log: Path, root_log: Path) -> None:
    hashes = {
        "Controller": binary_sha256(artifact_root / "controller.exe"),
        "S8BasicDll": binary_sha256(artifact_root / "bridge_s8_basic_batch.dll"),
        "S8WealthyDll": binary_sha256(artifact_root / "bridge_s8_wealthy_batch.dll"),
    }
    native_text = native_log.read_text(encoding="utf-8", errors="replace")
    root_text = root_log.read_text(encoding="utf-8", errors="replace")
    native_match = re.search(
        r"P1_M2B_OFFLINE_SELFTEST passed=(\d+) failed=0 live_runs=0", native_text
    )
    if not native_match:
        raise RuntimeError("native offline pass count not found")
    native_passed = int(native_match.group(1))
    ui_matches = re.findall(
        r"Total:\s*(\d+),\s*Passed:\s*(\d+),\s*Failed:\s*0", root_text
    )
    if not ui_matches:
        raise RuntimeError("UI test pass count not found")
    ui_total, ui_passed = map(int, ui_matches[-1])
    if ui_total != ui_passed:
        raise RuntimeError("UI tests were not all green")
    ui_exe = ROOT / "tools/artifacts/bin/San9AutoDomestic.exe"
    ui_hash = binary_sha256(ui_exe)

    state = ROOT / "STATE.md"
    document = TextDocument.load(state)
    document.text = re.sub(
        r"> 当前状态唯一真源；覆盖式更新。快照：\d{4}-\d{2}-\d{2}。",
        "> 当前状态唯一真源；覆盖式更新。快照：2026-08-23。",
        document.text,
        count=1,
    )
    current = f'''## 当前唯一任务

S8 跨项误判已完成源码级修复与离线闭合：首个真正执行项冻结 controller/city/corps；后续捕获只在本地只读视图中把 `controller+0x38 == 0` 规范化为该冻结城市，同城非零目标同样接受，任何非零异城目标、controller 漂移或 corps 漂移仍返回 `BATCH_REBIND_REQUIRED`。controller 与驻留 Bridge 在真正发布/执行下一项前使用同一绑定规则；没有写回 `root+0x38`，没有放宽进程代、窗口、主线程、Easy 摘要、城市结构、军团、命令位、资金和 top5 复核。UI 同时保留重绑终止前的 `executed/skipped`，不再把已完成的首项显示为 0。

本次仅做离线构建与测试：native 双根确定性构建、PE 前后审计和 offline `{native_passed}/{native_passed}` 通过；产品根构建与 UI 状态/协议测试 `{ui_passed}/{ui_total}` 通过；`live_loaded=0`。尚未进行新的游戏实机验证，旧等级 3 授权仍已消费；下一次 live 必须先完全重启游戏、使用新的测试旬次/复制存档并取得新的明确等级 3 授权。

### 产品 UI 正式构建'''
    document.text, count = re.subn(
        r"## 当前唯一任务\n\n.*?\n\n### 产品 UI 正式构建",
        current,
        document.text,
        count=1,
        flags=re.DOTALL,
    )
    if count != 1:
        raise RuntimeError("could not replace current task section")

    product_start = document.text.index("### 产品 UI 正式构建")
    product_end = document.text.index("### S8 Basic combined 离线里程碑", product_start)
    product = document.text[product_start:product_end]
    product = re.sub(
        r"- 唯一可见用户入口：`tools/artifacts/bin/San9AutoDomestic\.exe`，SHA-256 `[A-F0-9]+`。",
        f"- 唯一可见用户入口：`tools/artifacts/bin/San9AutoDomestic.exe`，SHA-256 `{ui_hash}`。",
        product,
        count=1,
    )
    product = re.sub(
        r"  - `controller\.exe`：`[A-F0-9]+`",
        f"  - `controller.exe`：`{hashes['Controller']}`",
        product,
        count=1,
    )
    product = re.sub(
        r"  - `bridge_s8_basic_batch\.dll`：`[A-F0-9]+`",
        f"  - `bridge_s8_basic_batch.dll`：`{hashes['S8BasicDll']}`",
        product,
        count=1,
    )
    product = re.sub(
        r"  - `bridge_s8_wealthy_batch\.dll`：`[A-F0-9]+`",
        f"  - `bridge_s8_wealthy_batch.dll`：`{hashes['S8WealthyDll']}`",
        product,
        count=1,
    )
    product = re.sub(
        r"- 正式根构建 .*?本次构建没有启动 controller 或访问游戏。",
        f"- 2026-08-23 修复构建已事务发布；UI 状态/协议测试 `{ui_passed}/{ui_total}`，native M2b 离线测试 `{native_passed}/{native_passed}`，双根确定性、PE 前后审计与既有回归全部通过；`live_loaded=0`，本次构建没有启动 controller 或访问游戏。",
        product,
        count=1,
    )
    document.text = document.text[:product_start] + product + document.text[product_end:]

    next_steps = '''## 下一步

1. S8 Basic 与 Wealthy 引擎、实机历史证据、存档出口和产品 UI 接线不重复开发；本次只修复了已审计的跨项绑定误判。
2. 下一步只允许做一次新的修复后 UI live smoke：必须先完全重启游戏，使用新的测试旬次/复制存档，并取得新的等级 3 明确授权；未获授权前不得启动 controller、不得加载 Bridge、不得自动重试。
3. live 验收只看首项“巡察”成功后第二项“商业”能否在同一冻结城市继续，以及真正异城目标是否仍 fail-closed；若出现任何不确定状态，立即要求重启，不扩写焦点、注入或安全门设计。

## Git'''
    document.text, count = re.subn(
        r"## 下一步\n\n.*?\n\n## Git",
        next_steps,
        document.text,
        count=1,
        flags=re.DOTALL,
    )
    if count != 1:
        raise RuntimeError("could not replace next-step section")
    document.save()


def run_selftest() -> None:
    passed = 0
    checks = []

    checks.append(
        (
            normalized_source_bytes(b"a\r\nb\r\n") == b"a\nb\n"
            and hashlib.sha256(normalized_source_bytes(b"a\r\nb\r\n")).digest()
            == hashlib.sha256(b"a\nb\n").digest(),
            "crlf-normalized-source-hash",
        )
    )

    with tempfile.TemporaryDirectory(prefix="s8-patcher-selftest-") as temporary:
        temporary_root = Path(temporary)
        source = ROOT
        for relative in SOURCE_TARGETS:
            target = temporary_root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes((source / relative).read_bytes())
        old_root = globals()["ROOT"]
        globals()["ROOT"] = temporary_root
        try:
            patch_source()
            first = {
                relative: (temporary_root / relative).read_bytes()
                for relative in SOURCE_TARGETS
            }
            patch_source()
            second = {
                relative: (temporary_root / relative).read_bytes()
                for relative in SOURCE_TARGETS
            }
            checks.append((first == second, "source-patch-idempotent"))
            checks.append(
                (
                    (b"\r\n" in first[SOURCE_TARGETS[0]])
                    == (b"\r\n" in (source / SOURCE_TARGETS[0]).read_bytes()),
                    "source-newline-style-preserved",
                )
            )
            verify_source_paths()
            checks.append((True, "function-boundary-path-verification"))
        finally:
            globals()["ROOT"] = old_root

    for ok, name in checks:
        if not ok:
            raise RuntimeError(f"S8 patcher selftest failed: {name}")
        passed += 1
        print(f"PASS {name}")
    print(f"S8_PATCH_SELFTEST passed={passed} failed=0")


def main() -> None:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("source")
    sub.add_parser("verify")
    sub.add_parser("selftest")
    outputs = sub.add_parser("outputs")
    outputs.add_argument("--artifact-root", required=True, type=Path)
    state = sub.add_parser("state")
    state.add_argument("--artifact-root", required=True, type=Path)
    state.add_argument("--native-log", required=True, type=Path)
    state.add_argument("--root-log", required=True, type=Path)
    args = parser.parse_args()
    if args.command == "source":
        patch_source()
    elif args.command == "verify":
        verify_source_paths()
    elif args.command == "selftest":
        run_selftest()
    elif args.command == "outputs":
        patch_outputs(args.artifact_root)
    elif args.command == "state":
        patch_state(args.artifact_root, args.native_log, args.root_log)


if __name__ == "__main__":
    main()
