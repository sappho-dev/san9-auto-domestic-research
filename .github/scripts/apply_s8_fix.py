#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] if '.github' in str(Path(__file__)) else Path.cwd()


def read_normalized(path: Path) -> tuple[str, str, bool]:
    raw = path.read_bytes()
    bom = raw.startswith(b'\xef\xbb\xbf')
    if bom:
        raw = raw[3:]
    newline = '\r\n' if b'\r\n' in raw else '\n'
    text = raw.decode('utf-8').replace('\r\n', '\n')
    return text, newline, bom


def write_normalized(path: Path, text: str, newline: str, bom: bool) -> None:
    data = text.replace('\n', newline).encode('utf-8')
    if bom:
        data = b'\xef\xbb\xbf' + data
    path.write_bytes(data)


def replace_once(path: Path, old: str, new: str) -> None:
    text, newline, bom = read_normalized(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected one exact replacement, found {count}')
    write_normalized(path, text.replace(old, new, 1), newline, bom)


def regex_replace_once(path: Path, pattern: str, replacement: str, flags: int = 0) -> None:
    text, newline, bom = read_normalized(path)
    updated, count = re.subn(pattern, replacement, text, count=1, flags=flags)
    if count != 1:
        raise RuntimeError(f'{path}: expected one regex replacement, found {count}: {pattern}')
    write_normalized(path, updated, newline, bom)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def patch_header() -> None:
    path = ROOT / 'native/San9BridgeP1EasyPingM2b/include/s5_current_context.h'
    replace_once(
        path,
        '''typedef struct San9S5ExpectedIdentity {
    uint32_t process_id;
    uint32_t main_thread_id;
    uint64_t process_generation;
    uint64_t window_handle;
} San9S5ExpectedIdentity;

typedef int (*San9S5CurrentContextReadCallback)(
''',
        '''typedef struct San9S5ExpectedIdentity {
    uint32_t process_id;
    uint32_t main_thread_id;
    uint64_t process_generation;
    uint64_t window_handle;
} San9S5ExpectedIdentity;

/* Cross-step batch binding.  It is an internal read constraint, not wire
   state and never authorizes writing controller+0x38. */
typedef struct San9S5BoundCurrentCity {
    uint32_t controller_pointer;
    uint32_t city_pointer;
    uint32_t corps_pointer;
} San9S5BoundCurrentCity;

typedef int (*San9S5CurrentContextReadCallback)(
''')
    replace_once(
        path,
        '''San9S5CurrentContextStatus san9_s6_repair_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

int san9_s5_current_context_business_digest(
''',
        '''San9S5CurrentContextStatus san9_s6_repair_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

/* A bound capture substitutes the frozen city only in its local read view
   when the observed controller target is zero.  A non-zero foreign target,
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
    San9S5CurrentContextSnapshot *second);

int san9_s5_current_context_business_digest(
''')
    replace_once(
        path,
        '''San9S5CurrentContextStatus san9_s6_repair_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);
#endif
''',
        '''San9S5CurrentContextStatus san9_s6_repair_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

San9S5CurrentContextStatus san9_s5_bound_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    const San9S5BoundCurrentCity *bound,
    uint32_t native_command_id,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);
#endif
''')


def patch_context_source() -> None:
    path = ROOT / 'native/San9BridgeP1EasyPingM2b/src/s5_current_context.c'
    replace_once(
        path,
        '''static void s5_hash_u16(San9P1Sha256Context *sha, uint16_t value)
''',
        '''static int s5_bound_current_city_shape_valid(
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
}

static void s5_hash_u16(San9P1Sha256Context *sha, uint16_t value)
''')

    replace_once(
        path,
        '''#if defined(_WIN32) && !defined(SAN9_S5_CONTEXT_NO_HANDLE)
typedef struct San9S5HandleReadContext {
''',
        '''typedef struct S5BoundReaderContext {
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
}

#if defined(_WIN32) && !defined(SAN9_S5_CONTEXT_NO_HANDLE)
typedef struct San9S5HandleReadContext {
''')

    replace_once(
        path,
        '''#endif

const char *san9_s5_current_context_status_name(
''',
        '''San9S5CurrentContextStatus san9_s5_bound_current_context_capture_handle_ab(
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
}
#endif

const char *san9_s5_current_context_status_name(
''')


def patch_controller() -> None:
    path = ROOT / 'native/San9BridgeP1EasyPingM2b/src/controller.c'
    replace_once(
        path,
        '''static San9S5CurrentContextStatus s8_capture_command(
    HANDLE process,
    const San9P1M2bBootstrapConfig *config,
    uint64_t game_generation,
    uint32_t native_command_id,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    if (native_command_id == SAN9_P1_M2B_PATROL_NATIVE_ID) {
''',
        '''static San9S5CurrentContextStatus s8_capture_command(
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
''')
    replace_once(
        path,
        '''    uint32_t bound_controller = 0u;
    uint32_t bound_city = 0u;
    uint32_t bound_corps = 0u;
''',
        '''    San9S5BoundCurrentCity bound_city;
''')
    replace_once(
        path,
        '''    memset(&step_evidence, 0, sizeof(step_evidence));
    memset(binding_digest, 0, sizeof(binding_digest));
''',
        '''    memset(&step_evidence, 0, sizeof(step_evidence));
    memset(&bound_city, 0, sizeof(bound_city));
    memset(binding_digest, 0, sizeof(binding_digest));
''')
    replace_once(
        path,
        '''            status = s8_capture_command(process, config,
                binding.game_generation, commands[step], &first, &second);
''',
        '''            status = s8_capture_command(process, config,
                binding.game_generation, commands[step],
                bound_city.controller_pointer == 0u ? NULL : &bound_city,
                &first, &second);
''')
    replace_once(
        path,
        '''        if (bound_city == 0u) {
            bound_controller = first.controller_pointer;
            bound_city = first.city_pointer;
            bound_corps = first.corps_pointer;
        } else if (first.controller_pointer != bound_controller
            || first.city_pointer != bound_city
            || first.corps_pointer != bound_corps) {
''',
        '''        if (bound_city.controller_pointer == 0u) {
            bound_city.controller_pointer = first.controller_pointer;
            bound_city.city_pointer = first.city_pointer;
            bound_city.corps_pointer = first.corps_pointer;
        } else if (first.controller_pointer != bound_city.controller_pointer
            || first.city_pointer != bound_city.city_pointer
            || first.corps_pointer != bound_city.corps_pointer) {
''')
    replace_once(
        path,
        '''    san9_p1_secure_zero(&step_evidence, sizeof(step_evidence));
    san9_p1_secure_zero(binding_digest, sizeof(binding_digest));
''',
        '''    san9_p1_secure_zero(&step_evidence, sizeof(step_evidence));
    san9_p1_secure_zero(&bound_city, sizeof(bound_city));
    san9_p1_secure_zero(binding_digest, sizeof(binding_digest));
''')


def patch_bridge() -> None:
    path = ROOT / 'native/San9BridgeP1EasyPingM2b/src/bridge_dll.c'
    replace_once(
        path,
        '''    uint32_t s8_native_id_latch;
    uint32_t s8_request_latched;
    San9S5NoApplyGate s8_step_gates[5];
''',
        '''    uint32_t s8_native_id_latch;
    uint32_t s8_request_latched;
    San9S5BoundCurrentCity s8_bound_city;
    uint32_t s8_bound_city_valid;
    San9S5NoApplyGate s8_step_gates[5];
''')
    replace_once(
        path,
        '''    g_runtime.owner_token = shared->owner_token;
    g_runtime.main_thread_id = shared->target_thread_id;
    if (shared->operation_mode == SAN9_P1_M2B_OPERATION_OBSERVE) {
''',
        '''    g_runtime.owner_token = shared->owner_token;
    g_runtime.main_thread_id = shared->target_thread_id;
#if SAN9_COMBINED_BATCH_BUILD
    memset(&g_runtime.s8_bound_city, 0, sizeof(g_runtime.s8_bound_city));
    g_runtime.s8_bound_city_valid = 0u;
#endif
    if (shared->operation_mode == SAN9_P1_M2B_OPERATION_OBSERVE) {
''')
    replace_once(
        path,
        '''    if (g_runtime.shared != NULL && g_runtime.shared->operation_mode
            == SAN9_ACTIVE_APPLY_MODE) {
        switch (s8_active_native_id()) {
''',
        '''    if (g_runtime.shared != NULL && g_runtime.shared->operation_mode
            == SAN9_ACTIVE_APPLY_MODE) {
        if (g_runtime.s8_bound_city_valid != 0u) {
            return san9_s5_bound_current_context_capture_reader_ab(
                &reader, &identity, &g_runtime.s8_bound_city,
                s8_active_native_id(), first, second);
        }
        switch (s8_active_native_id()) {
''')
    replace_once(
        path,
        '''    g_runtime.s5_pre = first;
#if SAN9_COMBINED_BATCH_BUILD
    s8_select_exact_vtables(first.native_command_id);
''',
        '''#if SAN9_COMBINED_BATCH_BUILD
    if (shared->operation_mode == SAN9_ACTIVE_APPLY_MODE
        && g_runtime.s8_bound_city_valid == 0u) {
        g_runtime.s8_bound_city.controller_pointer = first.controller_pointer;
        g_runtime.s8_bound_city.city_pointer = first.city_pointer;
        g_runtime.s8_bound_city.corps_pointer = first.corps_pointer;
        MemoryBarrier();
        g_runtime.s8_bound_city_valid = 1u;
    }
#endif
    g_runtime.s5_pre = first;
#if SAN9_COMBINED_BATCH_BUILD
    s8_select_exact_vtables(first.native_command_id);
''')


def patch_offline_tests() -> None:
    path = ROOT / 'native/San9BridgeP1EasyPingM2b/src/offline_selftest.c'
    replace_once(
        path,
        '''typedef struct FakeRead {
''',
        '''static void test_s8_bound_current_city_policy(void)
{
    San9S5BoundCurrentCity bound;
    uint32_t normalized = UINT32_C(0xFFFFFFFF);
    memset(&bound, 0, sizeof(bound));
    bound.controller_pointer = UINT32_C(0x00100000);
    bound.city_pointer = UINT32_C(0x0124DB58) + UINT32_C(0x1F0);
    bound.corps_pointer = UINT32_C(0x01253C38) + UINT32_C(0xD4);
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
}

typedef struct FakeRead {
''')
    replace_once(
        path,
        '''    test_s5_v8_frozen_post();
    test_s5_no_apply_contract();
''',
        '''    test_s5_v8_frozen_post();
    test_s8_bound_current_city_policy();
    test_s5_no_apply_contract();
''')


def patch_ui() -> None:
    path = ROOT / 'src/San9AutoDomestic.UI/NativeControllerClient.cs'
    replace_once(
        path,
        '''            if (string.Equals(phase, "BATCH_REBIND_REQUIRED", StringComparison.Ordinal))
            {
                return "当前城市绑定已变化；本次停止，不会重试。";
            }
''',
        '''            if (string.Equals(phase, "BATCH_REBIND_REQUIRED", StringComparison.Ordinal))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "当前城市绑定已变化；停止前已执行 {0}，跳过 {1}，不会重试。",
                    NativeJson.IntegerValue(json, "executed", 0),
                    NativeJson.IntegerValue(json, "skipped", 0));
            }
''')
    replace_once(
        path,
        '''            if (string.Equals(phase, "BATCH_REBIND_REQUIRED", StringComparison.Ordinal))
            {
                rebindRequired = true;
                state = NativeControllerState.RebindRequired;
            }
''',
        '''            if (string.Equals(phase, "BATCH_REBIND_REQUIRED", StringComparison.Ordinal))
            {
                rebindRequired = true;
                executed = NativeJson.IntegerValue(json, "executed", executed);
                skipped = NativeJson.IntegerValue(json, "skipped", skipped);
                state = NativeControllerState.RebindRequired;
            }
''')

    tests = ROOT / 'tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs'
    replace_once(
        tests,
        '''            AssertEx.Equal(
                NativeControllerState.RebindRequired,
                rebind.Complete(23, string.Empty).State,
                "Rebind must be a distinct terminal state.");
''',
        '''            NativeBatchResult rebindResult = rebind.Complete(23, string.Empty);
            AssertEx.Equal(
                NativeControllerState.RebindRequired,
                rebindResult.State,
                "Rebind must be a distinct terminal state.");
            AssertEx.Equal(1, rebindResult.Executed,
                "Rebind must retain the already executed count.");
            AssertEx.Equal(0, rebindResult.Skipped,
                "Rebind must retain the skipped count.");
            AssertEx.Contains(
                "停止前已执行 1，跳过 0",
                NativeControllerClient.DescribeJsonLine(
                    "{\"phase\":\"BATCH_REBIND_REQUIRED\",\"executed\":1,\"skipped\":0}"),
                "The visible rebind line must preserve completed work.");
''')


def update_source_pins() -> None:
    build = ROOT / 'native/San9BridgeP1EasyPingM2b/build.ps1'
    text, newline, bom = read_normalized(build)
    rel_paths = [
        r'include\s5_current_context.h',
        r'src\s5_current_context.c',
        r'src\controller.c',
        r'src\bridge_dll.c',
        r'src\offline_selftest.c',
    ]
    for rel in rel_paths:
        digest = sha256(ROOT / 'native/San9BridgeP1EasyPingM2b' / Path(rel.replace('\\', '/')))
        pattern = re.compile(
            r"(@\(\(Join-Path \$PSScriptRoot '" + re.escape(rel)
            + r"'\),')([A-F0-9]{64})('\),)")
        text, count = pattern.subn(r'\g<1>' + digest + r'\g<3>', text, count=1)
        if count != 1:
            raise RuntimeError(f'could not update source pin for {rel}')
    write_normalized(build, text, newline, bom)


def patch_source() -> None:
    patch_header()
    patch_context_source()
    patch_controller()
    patch_bridge()
    patch_offline_tests()
    patch_ui()
    update_source_pins()


def parse_native_expected(text: str, role: str) -> str:
    match = re.search(r'^\s*' + re.escape(role) + r"\s*=\s*'([A-F0-9]{64})'", text, re.MULTILINE)
    if not match:
        raise RuntimeError(f'native expected role not found: {role}')
    return match.group(1)


def patch_outputs(artifact_root: Path) -> None:
    roles = {
        'Offline': 'offline.exe',
        'Controller': 'controller.exe',
        'Dll': 'bridge.dll',
        'ApplyDll': 'bridge_apply_once.dll',
        'CultivateDll': 'bridge_s6_cultivate_apply_once.dll',
        'PatrolDll': 'bridge_s6_patrol_apply_once.dll',
        'TrainDll': 'bridge_s6_train_apply_once.dll',
        'RepairDll': 'bridge_s6_repair_apply_once.dll',
        'S8BasicDll': 'bridge_s8_basic_batch.dll',
        'S8WealthyDll': 'bridge_s8_wealthy_batch.dll',
    }
    hashes = {role: sha256(artifact_root / name) for role, name in roles.items()}
    native_build = ROOT / 'native/San9BridgeP1EasyPingM2b/build.ps1'
    text, newline, bom = read_normalized(native_build)
    old_runtime = {
        'Controller': parse_native_expected(text, 'Controller'),
        'S8BasicDll': parse_native_expected(text, 'S8BasicDll'),
        'S8WealthyDll': parse_native_expected(text, 'S8WealthyDll'),
    }
    for role, digest in hashes.items():
        pattern = r"(^\s*" + re.escape(role) + r"\s*=\s*')[A-F0-9]{64}(')"
        text, count = re.subn(pattern, r'\g<1>' + digest + r'\g<2>', text,
                              count=1, flags=re.MULTILINE)
        if count != 1:
            raise RuntimeError(f'could not update native expected role {role}')
    write_normalized(native_build, text, newline, bom)

    root_build = ROOT / 'build.ps1'
    root_text, root_newline, root_bom = read_normalized(root_build)
    for role in ('Controller', 'S8BasicDll', 'S8WealthyDll'):
        old = old_runtime[role]
        new = hashes[role]
        count = root_text.count(old)
        if count < 2:
            raise RuntimeError(f'root build expected at least two {role} hash occurrences, found {count}')
        root_text = root_text.replace(old, new)
    write_normalized(root_build, root_text, root_newline, root_bom)

    ui = ROOT / 'src/San9AutoDomestic.UI/NativeControllerClient.cs'
    ui_text, ui_newline, ui_bom = read_normalized(ui)
    for role in ('Controller', 'S8BasicDll', 'S8WealthyDll'):
        old = old_runtime[role]
        new = hashes[role]
        count = ui_text.count(old)
        if count != 1:
            raise RuntimeError(f'UI expected one {role} hash occurrence, found {count}')
        ui_text = ui_text.replace(old, new)
    write_normalized(ui, ui_text, ui_newline, ui_bom)

    print(json.dumps(hashes, sort_keys=True))


def patch_state(artifact_root: Path, native_log: Path, root_log: Path) -> None:
    hashes = {
        'Controller': sha256(artifact_root / 'controller.exe'),
        'S8BasicDll': sha256(artifact_root / 'bridge_s8_basic_batch.dll'),
        'S8WealthyDll': sha256(artifact_root / 'bridge_s8_wealthy_batch.dll'),
    }
    native_text = native_log.read_text(encoding='utf-8', errors='replace')
    root_text = root_log.read_text(encoding='utf-8', errors='replace')
    native_match = re.search(r'P1_M2B_OFFLINE_SELFTEST passed=(\d+) failed=0 live_runs=0', native_text)
    if not native_match:
        raise RuntimeError('native offline pass count not found')
    native_passed = int(native_match.group(1))
    ui_matches = re.findall(r'Total:\s*(\d+),\s*Passed:\s*(\d+),\s*Failed:\s*0', root_text)
    if not ui_matches:
        raise RuntimeError('UI test pass count not found')
    ui_total, ui_passed = map(int, ui_matches[-1])
    if ui_total != ui_passed:
        raise RuntimeError('UI tests were not all green')
    ui_exe = ROOT / 'tools/artifacts/bin/San9AutoDomestic.exe'
    ui_hash = sha256(ui_exe)

    state = ROOT / 'STATE.md'
    text, newline, bom = read_normalized(state)
    text = re.sub(r'> 当前状态唯一真源；覆盖式更新。快照：\d{4}-\d{2}-\d{2}。',
                  '> 当前状态唯一真源；覆盖式更新。快照：2026-08-23。', text, count=1)
    current = f'''## 当前唯一任务

S8 跨项误判已完成源码级修复与离线闭合：首个真正执行项冻结 controller/city/corps；后续捕获只在本地只读视图中把 `controller+0x38 == 0` 规范化为该冻结城市，同城非零目标同样接受，任何非零异城目标、controller 漂移或 corps 漂移仍返回 `BATCH_REBIND_REQUIRED`。controller 与驻留 Bridge 在真正发布/执行下一项前使用同一绑定规则；没有写回 `root+0x38`，没有放宽进程代、窗口、主线程、Easy 摘要、城市结构、军团、命令位、资金和 top5 复核。UI 同时保留重绑终止前的 `executed/skipped`，不再把已完成的首项显示为 0。

本次仅做离线构建与测试：native 双根确定性构建、PE 前后审计和 offline `{native_passed}/{native_passed}` 通过；产品根构建与 UI 状态/协议测试 `{ui_passed}/{ui_total}` 通过；`live_loaded=0`。尚未进行新的游戏实机验证，旧等级 3 授权仍已消费；下一次 live 必须先完全重启游戏、使用新的测试旬次/复制存档并取得新的明确等级 3 授权。

### 产品 UI 正式构建'''
    text, count = re.subn(
        r'## 当前唯一任务\n\n.*?\n\n### 产品 UI 正式构建',
        current, text, count=1, flags=re.DOTALL)
    if count != 1:
        raise RuntimeError('could not replace current task section')

    product_start = text.index('### 产品 UI 正式构建')
    product_end = text.index('### S8 Basic combined 离线里程碑', product_start)
    product = text[product_start:product_end]
    product = re.sub(r'- 唯一可见用户入口：`tools/artifacts/bin/San9AutoDomestic\.exe`，SHA-256 `[A-F0-9]+`。',
                     f'- 唯一可见用户入口：`tools/artifacts/bin/San9AutoDomestic.exe`，SHA-256 `{ui_hash}`。',
                     product, count=1)
    product = re.sub(r'  - `controller\.exe`：`[A-F0-9]+`',
                     f'  - `controller.exe`：`{hashes["Controller"]}`', product, count=1)
    product = re.sub(r'  - `bridge_s8_basic_batch\.dll`：`[A-F0-9]+`',
                     f'  - `bridge_s8_basic_batch.dll`：`{hashes["S8BasicDll"]}`', product, count=1)
    product = re.sub(r'  - `bridge_s8_wealthy_batch\.dll`：`[A-F0-9]+`',
                     f'  - `bridge_s8_wealthy_batch.dll`：`{hashes["S8WealthyDll"]}`', product, count=1)
    product = re.sub(
        r'- 正式根构建 .*?本次构建没有启动 controller 或访问游戏。',
        f'- 2026-08-23 修复构建已事务发布；UI 状态/协议测试 `{ui_passed}/{ui_total}`，native M2b 离线测试 `{native_passed}/{native_passed}`，双根确定性、PE 前后审计与既有回归全部通过；`live_loaded=0`，本次构建没有启动 controller 或访问游戏。',
        product, count=1)
    text = text[:product_start] + product + text[product_end:]

    next_steps = '''## 下一步

1. S8 Basic 与 Wealthy 引擎、实机历史证据、存档出口和产品 UI 接线不重复开发；本次只修复了已审计的跨项绑定误判。
2. 下一步只允许做一次新的修复后 UI live smoke：必须先完全重启游戏，使用新的测试旬次/复制存档，并取得新的等级 3 明确授权；未获授权前不得启动 controller、不得加载 Bridge、不得自动重试。
3. live 验收只看首项成功后第二项能否在同一冻结城市继续，以及真正异城目标是否仍 fail-closed；若出现任何不确定状态，立即要求重启，不扩写焦点、注入或安全门设计。

## Git'''
    text, count = re.subn(r'## 下一步\n\n.*?\n\n## Git', next_steps, text,
                          count=1, flags=re.DOTALL)
    if count != 1:
        raise RuntimeError('could not replace next-step section')
    write_normalized(state, text, newline, bom)


def main() -> None:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest='command', required=True)
    sub.add_parser('source')
    outputs = sub.add_parser('outputs')
    outputs.add_argument('--artifact-root', required=True, type=Path)
    state = sub.add_parser('state')
    state.add_argument('--artifact-root', required=True, type=Path)
    state.add_argument('--native-log', required=True, type=Path)
    state.add_argument('--root-log', required=True, type=Path)
    args = parser.parse_args()
    if args.command == 'source':
        patch_source()
    elif args.command == 'outputs':
        patch_outputs(args.artifact_root)
    elif args.command == 'state':
        patch_state(args.artifact_root, args.native_log, args.root_log)


if __name__ == '__main__':
    main()
