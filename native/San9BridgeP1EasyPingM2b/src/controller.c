#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <sddl.h>
#include <bcrypt.h>

#include <stdio.h>
#include <limits.h>
#include <string.h>
#include <wchar.h>

#include "san9_p1_m2b.h"
#include "discover.h"
#include "sha256.h"

typedef LRESULT (CALLBACK *M2bHookProc)(int, WPARAM, LPARAM);

#define SAN9_P1_M2B_S5_MENU_WAIT_MS UINT64_C(60000)
#define SAN9_P1_M2B_S5_MENU_CLOSE_WAIT_MS UINT64_C(120000)
#define SAN9_P1_M2B_S5_MENU_POLL_MS 25u
#define SAN9_P1_M2B_S5_MENU_SIGNAL "OPEN_CURRENT_CITY_MENU"
#define SAN9_P1_M2B_S5_MODAL_POLL_MS 1u

static const volatile char g_s5_terminal_identity[] =
    "S5_V8_TERMINAL_CAS=1;SIGNED_CONFIG_DEADLINE=1;"
    "TIMEOUT_POISON_MONOTONIC=1";

static int random_bytes(void *output, size_t size);
static uint32_t wait_for_s5_request_and_publish(
    San9P1M2bShared *shared,
    const San9P1M2bBootstrapConfig *config);
static uint32_t run_s8_batch_session(
    San9P1M2bShared *shared,
    const San9P1M2bBootstrapConfig *config,
    San9P1S5NoApplyEvidence *output);
static uint32_t run_s5_modal_probe_session(
    San9P1M2bShared *shared,
    const San9P1M2bBootstrapConfig *config,
    San9P1S5ModalProbeEvidence *output);

static LONG interlocked_read(volatile int32_t *value)
{
    return InterlockedCompareExchange((volatile LONG *)value, 0, 0);
}

static int is_apply_once_operation(uint32_t mode)
{
    return mode == SAN9_P1_M2B_OPERATION_S5_APPLY_ONCE
        || mode == SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE
        || mode == SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE
        || mode == SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE
        || mode == SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE
        || mode == SAN9_P1_M2B_OPERATION_S8_BASIC_BATCH
        || mode == SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH;
}

static int is_batch_operation(uint32_t mode)
{
    return mode == SAN9_P1_M2B_OPERATION_S8_BASIC_BATCH
        || mode == SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH;
}

static int is_s5_business_operation(uint32_t mode)
{
    return mode == SAN9_P1_M2B_OPERATION_S5_NO_APPLY
        || is_apply_once_operation(mode);
}

static uint32_t apply_failure_for_mode(uint32_t mode)
{
    if (mode == SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH) {
        return SAN9_P1_M2B_S8_WEALTHY_BATCH_FAILED;
    }
    if (mode == SAN9_P1_M2B_OPERATION_S8_BASIC_BATCH) {
        return SAN9_P1_M2B_S8_BASIC_BATCH_FAILED;
    }
    if (mode == SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE) {
        return SAN9_P1_M2B_S6_REPAIR_APPLY_ONCE_FAILED;
    }
    if (mode == SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE) {
        return SAN9_P1_M2B_S6_TRAIN_APPLY_ONCE_FAILED;
    }
    if (mode == SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE) {
        return SAN9_P1_M2B_S6_PATROL_APPLY_ONCE_FAILED;
    }
    return mode == SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE
        ? SAN9_P1_M2B_S6_CULTIVATE_APPLY_ONCE_FAILED
        : SAN9_P1_M2B_S5_APPLY_ONCE_FAILED;
}

static uint32_t apply_native_id_for_mode(uint32_t mode)
{
    if (mode == SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE) {
        return SAN9_P1_M2B_REPAIR_NATIVE_ID;
    }
    if (mode == SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE) {
        return SAN9_P1_M2B_TRAIN_NATIVE_ID;
    }
    if (mode == SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE) {
        return SAN9_P1_M2B_PATROL_NATIVE_ID;
    }
    return mode == SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE
        ? SAN9_P1_M2B_CULTIVATE_NATIVE_ID : SAN9_P1_M2B_COMMERCE_NATIVE_ID;
}

static uint32_t apply_order_mask_for_mode(uint32_t mode)
{
    if (mode == SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE) {
        return SAN9_P1_M2B_REPAIR_ORDER_FLAG;
    }
    if (mode == SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE) {
        return SAN9_P1_M2B_TRAIN_ORDER_FLAG;
    }
    if (mode == SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE) {
        return SAN9_P1_M2B_PATROL_ORDER_FLAG;
    }
    return mode == SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE
        ? SAN9_P1_M2B_CULTIVATE_ORDER_FLAG : UINT32_C(0x10);
}

static uint32_t apply_cost_for_mode(uint32_t mode)
{
    return mode == SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE
        ? SAN9_P1_M2B_TRAIN_COST : UINT32_C(250);
}

static uint32_t apply_order_mask_for_native(uint32_t native_id)
{
    if (native_id == SAN9_P1_M2B_PATROL_NATIVE_ID) {
        return SAN9_P1_M2B_PATROL_ORDER_FLAG;
    }
    if (native_id == SAN9_P1_M2B_COMMERCE_NATIVE_ID) {
        return UINT32_C(0x10);
    }
    if (native_id == SAN9_P1_M2B_CULTIVATE_NATIVE_ID) {
        return SAN9_P1_M2B_CULTIVATE_ORDER_FLAG;
    }
    if (native_id == SAN9_P1_M2B_TRAIN_NATIVE_ID) {
        return SAN9_P1_M2B_TRAIN_ORDER_FLAG;
    }
    return native_id == SAN9_P1_M2B_REPAIR_NATIVE_ID
        ? SAN9_P1_M2B_REPAIR_ORDER_FLAG : 0u;
}

static uint32_t apply_cost_for_native(uint32_t native_id)
{
    if (native_id == SAN9_P1_M2B_TRAIN_NATIVE_ID) {
        return SAN9_P1_M2B_TRAIN_COST;
    }
    return native_id == SAN9_P1_M2B_PATROL_NATIVE_ID
            || native_id == SAN9_P1_M2B_COMMERCE_NATIVE_ID
            || native_id == SAN9_P1_M2B_CULTIVATE_NATIVE_ID
            || native_id == SAN9_P1_M2B_REPAIR_NATIVE_ID
        ? UINT32_C(250) : UINT32_MAX;
}

typedef struct S5FailureSnapshot {
    uint64_t now_ms;
    uint64_t deadline_ms;
    int32_t bootstrap_state;
    int32_t bootstrap_result;
    int32_t terminal_state;
    uint32_t machine_state;
    uint32_t first_fault;
    uint32_t machine_event;
    uint32_t machine_shadow;
    uint32_t machine_execute;
    uint32_t machine_restore;
    uint32_t machine_apply;
    uint32_t evidence_restart;
    uint32_t evidence_event;
    uint32_t evidence_shadow;
    uint32_t evidence_execute;
    uint32_t evidence_restore;
    uint32_t evidence_apply;
    uint32_t evidence_menu_wake;
    uint32_t evidence_menu_restore;
    uint32_t evidence_ack;
    int32_t menu_state;
    uint32_t menu_wake_post;
    uint32_t menu_hook_entry;
    uint32_t menu_exact_wake;
    uint32_t menu_duplicate;
    uint32_t menu_shadow_arm;
    uint32_t menu_entry_claim;
    uint32_t menu_restore;
    uint32_t menu_original_return;
    uint32_t menu_start;
    uint32_t menu_restart;
} S5FailureSnapshot;

static void print_s5_failure_snapshot(
    const San9P1M2bShared *shared, const char *stage)
{
    S5FailureSnapshot snapshot;
    const San9S5NoApplyMachine *machine;
    const San9P1S5NoApplyEvidence *evidence;
    const San9P1S5MenuHandoff *menu;
    if (shared == NULL || stage == NULL) {
        return;
    }
    memset(&snapshot, 0, sizeof(snapshot));
    machine = &shared->operation.s5.machine;
    evidence = &shared->operation.s5.evidence;
    menu = &shared->operation.s5.menu;
    MemoryBarrier();
    snapshot.now_ms = GetTickCount64();
    snapshot.deadline_ms = shared->operation.s5.request.expires_at_ms;
    snapshot.bootstrap_state = interlocked_read(
        (volatile int32_t *)&shared->bootstrap_state);
    snapshot.bootstrap_result = interlocked_read(
        (volatile int32_t *)&shared->bootstrap_result);
    snapshot.terminal_state = interlocked_read(
        (volatile int32_t *)&shared->operation.s5.terminal_state);
    snapshot.machine_state = (uint32_t)san9_s5_no_apply_machine_state(machine);
    snapshot.first_fault = (uint32_t)san9_s5_no_apply_machine_first_fault(machine);
    snapshot.machine_event = atomic_load_explicit(
        &machine->event_attempt_count, memory_order_acquire);
    snapshot.machine_shadow = atomic_load_explicit(
        &machine->shadow_arm_count, memory_order_acquire);
    snapshot.machine_execute = atomic_load_explicit(
        &machine->execute_enter_count, memory_order_acquire);
    snapshot.machine_restore = atomic_load_explicit(
        &machine->restore_count, memory_order_acquire);
    snapshot.machine_apply = atomic_load_explicit(
        &machine->observed_apply_count, memory_order_acquire);
    snapshot.evidence_restart = evidence->restart_required;
    snapshot.evidence_event = evidence->event_attempt_count;
    snapshot.evidence_shadow = evidence->shadow_arm_count;
    snapshot.evidence_execute = evidence->execute_enter_count;
    snapshot.evidence_restore = evidence->restore_count;
    snapshot.evidence_apply = evidence->observed_apply_count;
    snapshot.evidence_menu_wake = evidence->menu_wake_count;
    snapshot.evidence_menu_restore = evidence->menu_restore_count;
    snapshot.evidence_ack = evidence->controller_ack;
    snapshot.menu_state = interlocked_read((volatile int32_t *)&menu->state);
    snapshot.menu_wake_post = menu->wake_post_count;
    snapshot.menu_hook_entry = menu->hook_entry_count;
    snapshot.menu_exact_wake = menu->exact_wake_count;
    snapshot.menu_duplicate = menu->duplicate_count;
    snapshot.menu_shadow_arm = menu->shadow_arm_count;
    snapshot.menu_entry_claim = menu->entry_claim_count;
    snapshot.menu_restore = menu->menu_restore_count;
    snapshot.menu_original_return = menu->original_return_count;
    snapshot.menu_start = menu->start_count;
    snapshot.menu_restart = menu->restart_required;
    (void)fprintf(stderr,
        "{\"mode\":\"s5-no-apply\",\"phase\":\"FAILURE_SHARED_SNAPSHOT\","
        "\"stage\":\"%s\",\"now_ms\":%llu,\"deadline_ms\":%llu,"
        "\"bootstrap_state\":%ld,\"bootstrap_result\":%ld,"
        "\"terminal_state\":%ld,\"machine_state\":%lu,\"first_fault\":%lu,"
        "\"machine_event\":%lu,\"machine_shadow\":%lu,"
        "\"machine_execute\":%lu,\"machine_restore\":%lu,"
        "\"machine_apply\":%lu,\"evidence_restart\":%lu,"
        "\"evidence_event\":%lu,\"evidence_shadow\":%lu,"
        "\"evidence_execute\":%lu,\"evidence_restore\":%lu,"
        "\"evidence_apply\":%lu,\"evidence_menu_wake\":%lu,"
        "\"evidence_menu_restore\":%lu,\"evidence_ack\":%lu,"
        "\"menu_state\":%ld,\"menu_wake_post\":%lu,"
        "\"menu_hook_entry\":%lu,\"menu_exact_wake\":%lu,"
        "\"menu_duplicate\":%lu,\"menu_shadow_arm\":%lu,"
        "\"menu_entry_claim\":%lu,\"menu_restore\":%lu,"
        "\"menu_original_return\":%lu,\"menu_start\":%lu,"
        "\"menu_restart\":%lu}\n",
        stage, (unsigned long long)snapshot.now_ms,
        (unsigned long long)snapshot.deadline_ms,
        (long)snapshot.bootstrap_state, (long)snapshot.bootstrap_result,
        (long)snapshot.terminal_state, (unsigned long)snapshot.machine_state,
        (unsigned long)snapshot.first_fault,
        (unsigned long)snapshot.machine_event,
        (unsigned long)snapshot.machine_shadow,
        (unsigned long)snapshot.machine_execute,
        (unsigned long)snapshot.machine_restore,
        (unsigned long)snapshot.machine_apply,
        (unsigned long)snapshot.evidence_restart,
        (unsigned long)snapshot.evidence_event,
        (unsigned long)snapshot.evidence_shadow,
        (unsigned long)snapshot.evidence_execute,
        (unsigned long)snapshot.evidence_restore,
        (unsigned long)snapshot.evidence_apply,
        (unsigned long)snapshot.evidence_menu_wake,
        (unsigned long)snapshot.evidence_menu_restore,
        (unsigned long)snapshot.evidence_ack, (long)snapshot.menu_state,
        (unsigned long)snapshot.menu_wake_post,
        (unsigned long)snapshot.menu_hook_entry,
        (unsigned long)snapshot.menu_exact_wake,
        (unsigned long)snapshot.menu_duplicate,
        (unsigned long)snapshot.menu_shadow_arm,
        (unsigned long)snapshot.menu_entry_claim,
        (unsigned long)snapshot.menu_restore,
        (unsigned long)snapshot.menu_original_return,
        (unsigned long)snapshot.menu_start,
        (unsigned long)snapshot.menu_restart);
    (void)fflush(stderr);
}

static void controller_s5_publish_restart(San9P1M2bShared *shared)
{
    if (shared == NULL) {
        return;
    }
    (void)san9_s5_no_apply_machine_fail(&shared->operation.s5.machine,
        SAN9_S5_FAULT_STATE_CORRUPTION);
    atomic_store_explicit(&shared->operation.s5.machine.state,
        SAN9_S5_STATE_RESTART_REQUIRED, memory_order_release);
    shared->operation.s5.evidence.restart_required = 1u;
    InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
    InterlockedExchange((volatile LONG *)&shared->bootstrap_result,
        SAN9_P1_M2B_RESTART_REQUIRED);
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&shared->bootstrap_state,
        SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART);
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&shared->bootstrap_result,
        SAN9_P1_M2B_RESTART_REQUIRED);
}

/* 1: timeout won and restart was published; 0: an in-flight irreversible
   owner must settle; 2: target success already won; 3: clean rejection won. */
static int controller_s5_try_claim_timeout(San9P1M2bShared *shared)
{
    LONG state;
    LONG observed;
    if (shared == NULL) {
        return 1;
    }
    state = interlocked_read(&shared->operation.s5.terminal_state);
    if (state == SAN9_P1_S5_TERMINAL_SUCCESS) {
        return 2;
    }
    if (state == SAN9_P1_S5_TERMINAL_REJECTED) {
        return 3;
    }
    if (state == SAN9_P1_S5_TERMINAL_EVENT_INFLIGHT
        || state == SAN9_P1_S5_TERMINAL_FINISH_COMMIT) {
        return 0;
    }
    if (state == SAN9_P1_S5_TERMINAL_TIMEOUT
        || state == SAN9_P1_S5_TERMINAL_RESTART) {
        return 1;
    }
    if (state != SAN9_P1_S5_TERMINAL_OPEN
        && state != SAN9_P1_S5_TERMINAL_START_PRE_EVENT
        && state != SAN9_P1_S5_TERMINAL_FINISH_VERIFY) {
        observed = InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_RESTART, state);
        if (observed == state) {
            return 1;
        }
        return 0;
    }
    observed = InterlockedCompareExchange(
        (volatile LONG *)&shared->operation.s5.terminal_state,
        SAN9_P1_S5_TERMINAL_TIMEOUT, state);
    if (observed == state) {
        return 1;
    }
    return 0;
}

static int all_zero(const void *value, size_t size)
{
    const uint8_t *bytes = (const uint8_t *)value;
    uint8_t any = 0u;
    size_t index;
    if (value == NULL) {
        return 0;
    }
    for (index = 0u; index < size; ++index) {
        any |= bytes[index];
    }
    return any == 0u;
}

static void sha256_bytes(const void *value, size_t size, uint8_t output[32])
{
    San9P1Sha256Context sha;
    san9_p1_sha256_initialize(&sha);
    san9_p1_sha256_update(&sha, (const uint8_t *)value, size);
    san9_p1_sha256_finish(&sha, output);
}

static int exact_config(
    const San9P1M2bBootstrapConfig *config,
    San9P1Frame *binding)
{
    San9P1DecodeStatus decode;
    size_t path_length;
    if (config == NULL || binding == NULL
        || config->magic != SAN9_P1_M2B_CONFIG_MAGIC
        || config->schema_major != SAN9_P1_M2B_SCHEMA_MAJOR
        || config->schema_minor != SAN9_P1_M2B_SCHEMA_MINOR
        || config->structure_size != sizeof(*config)
        || config->target_pid == 0u || config->target_thread_id == 0u
        || config->target_hwnd == 0u || config->owner_token == 0u
        || (config->operation_mode != SAN9_P1_M2B_OPERATION_PROBE0
            && config->operation_mode != SAN9_P1_M2B_OPERATION_PING
            && config->operation_mode != SAN9_P1_M2B_OPERATION_OBSERVE
            && config->operation_mode != SAN9_P1_M2B_OPERATION_S5_NO_APPLY
            && config->operation_mode != SAN9_P1_M2B_OPERATION_S5_APPLY_ONCE
            && config->operation_mode
                != SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE
            && config->operation_mode
                != SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE
            && config->operation_mode
                != SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE
            && config->operation_mode
                != SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE
            && config->operation_mode
                != SAN9_P1_M2B_OPERATION_S8_BASIC_BATCH
            && config->operation_mode
                != SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH
            && config->operation_mode != SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE)
        || config->reserved_config != 0u
        || config->timeout_ms < 100u || config->timeout_ms > 5000u) {
        return 0;
    }
    path_length = wcsnlen(config->dll_path,
        sizeof(config->dll_path) / sizeof(config->dll_path[0]));
    if (path_length == 0u
        || path_length == sizeof(config->dll_path) / sizeof(config->dll_path[0])
        || GetFullPathNameW(config->dll_path, 0u, NULL, NULL) == 0u) {
        return 0;
    }
    memset(binding, 0, sizeof(*binding));
    decode = san9_p1_decode(config->binding_frame, sizeof(config->binding_frame),
        config->hmac_key, sizeof(config->hmac_key), binding);
    if (decode != SAN9_P1_DECODE_ACCEPTED
        || binding->kind != SAN9_P1_PING_REQUEST
        || binding->state != SAN9_P1_PENDING
        || binding->game_pid != config->target_pid
        || binding->main_tid != config->target_thread_id
        || binding->game_hwnd != config->target_hwnd
        || binding->helper_pid != GetCurrentProcessId()
        || binding->easy_loader_pid == 0u
        || binding->game_generation == 0u
        || binding->helper_generation == 0u
        || binding->easy_loader_generation == 0u
        || binding->sequence != 1u
        || !san9_p1_easy_binding_is_exact(&config->easy_binding)
        || config->easy_binding.game_hwnd != config->target_hwnd) {
        return 0;
    }
    return all_zero(&config->s5_request, sizeof(config->s5_request));
}

static int current_user_mapping_security(
    SECURITY_ATTRIBUTES *attributes,
    PSECURITY_DESCRIPTOR *descriptor)
{
    HANDLE token = NULL;
    TOKEN_USER *user = NULL;
    LPWSTR sid = NULL;
    LPWSTR sddl = NULL;
    DWORD needed = 0u;
    size_t chars;
    int ok = 0;
    if (attributes == NULL || descriptor == NULL) {
        return 0;
    }
    memset(attributes, 0, sizeof(*attributes));
    *descriptor = NULL;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) {
        goto cleanup;
    }
    (void)GetTokenInformation(token, TokenUser, NULL, 0u, &needed);
    if (needed == 0u) {
        goto cleanup;
    }
    user = (TOKEN_USER *)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, needed);
    if (user == NULL
        || !GetTokenInformation(token, TokenUser, user, needed, &needed)
        || !ConvertSidToStringSidW(user->User.Sid, &sid)) {
        goto cleanup;
    }
    chars = wcslen(sid) + 20u;
    if (chars > 512u) {
        goto cleanup;
    }
    sddl = (LPWSTR)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY,
        chars * sizeof(wchar_t));
    if (sddl == NULL) {
        goto cleanup;
    }
    (void)lstrcpyW(sddl, L"D:P(A;;GA;;;");
    (void)lstrcatW(sddl, sid);
    (void)lstrcatW(sddl, L")");
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
            sddl, SDDL_REVISION_1, descriptor, NULL)) {
        goto cleanup;
    }
    attributes->nLength = sizeof(*attributes);
    attributes->lpSecurityDescriptor = *descriptor;
    attributes->bInheritHandle = FALSE;
    ok = 1;
cleanup:
    if (sddl != NULL) {
        (void)HeapFree(GetProcessHeap(), 0u, sddl);
    }
    if (sid != NULL) {
        (void)LocalFree(sid);
    }
    if (user != NULL) {
        SecureZeroMemory(user, needed);
        (void)HeapFree(GetProcessHeap(), 0u, user);
    }
    if (token != NULL) {
        (void)CloseHandle(token);
    }
    return ok;
}

static int random_mapping_name(wchar_t output[64], uint32_t *challenge)
{
    static const wchar_t hex[] = L"0123456789ABCDEF";
    uint8_t random[20];
    const wchar_t prefix[] = L"Local\\San9P1M2b-";
    size_t index;
    if (output == NULL || challenge == NULL
        || BCryptGenRandom(NULL, random, sizeof(random),
            BCRYPT_USE_SYSTEM_PREFERRED_RNG) != 0) {
        return 0;
    }
    (void)lstrcpyW(output, prefix);
    for (index = 0u; index < 16u; ++index) {
        output[sizeof(prefix) / sizeof(prefix[0]) - 1u + index * 2u] =
            hex[random[index] >> 4u];
        output[sizeof(prefix) / sizeof(prefix[0]) + index * 2u] =
            hex[random[index] & 15u];
    }
    output[sizeof(prefix) / sizeof(prefix[0]) - 1u + 32u] = L'\0';
    memcpy(challenge, random + 16u, sizeof(*challenge));
    if (*challenge == 0u) {
        *challenge = UINT32_C(0xB2C0A55A);
    }
    SecureZeroMemory(random, sizeof(random));
    return 1;
}

static int wait_for_bootstrap(
    San9P1M2bShared *shared,
    uint32_t timeout_ms)
{
    ULONGLONG deadline = GetTickCount64() + timeout_ms;
    for (;;) {
        LONG state = interlocked_read(&shared->bootstrap_state);
        if (state == SAN9_P1_M2B_BOOTSTRAP_READY) {
            return 1;
        }
        if (state == SAN9_P1_M2B_BOOTSTRAP_REJECTED
            || state == SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART
            || GetTickCount64() >= deadline) {
            return 0;
        }
        Sleep(1u);
    }
}

static void capture_probe0_evidence(
    San9P1M2bShared *shared,
    uint32_t start,
    San9P1M2bProbe0Evidence *evidence)
{
    evidence->start_idle_count = start;
    MemoryBarrier();
    evidence->end_idle_count = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->probe0_idle_count);
    evidence->original_return_count = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->probe0_original_return_count);
    evidence->outer_count = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->probe0_outer_count);
    evidence->nested_count = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->probe0_nested_count);
    evidence->identity_reject_count = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->probe0_identity_reject_count);
    evidence->first_tid = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->probe0_first_tid);
    evidence->last_tid = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->probe0_last_tid);
    evidence->last_caller = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->probe0_last_caller);
    evidence->last_app = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->probe0_last_app);
}

static int wait_for_probe0(
    San9P1M2bShared *shared,
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *evidence)
{
    ULONGLONG deadline;
    uint32_t start;
    uint32_t end;
    if (shared == NULL || config == NULL || evidence == NULL) {
        return 0;
    }
    memset(evidence, 0, sizeof(*evidence));
    start = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->probe0_idle_count);
    evidence->start_idle_count = start;
    deadline = GetTickCount64() + config->timeout_ms;
    for (;;) {
        end = (uint32_t)interlocked_read(
            (volatile int32_t *)&shared->probe0_idle_count);
        if (end >= start + SAN9_P1_M2B_PROBE0_MIN_TICKS) {
            break;
        }
        if (interlocked_read(&shared->bootstrap_state)
                != SAN9_P1_M2B_BOOTSTRAP_READY
            || GetTickCount64() >= deadline) {
            return 0;
        }
        Sleep(1u);
    }
    capture_probe0_evidence(shared, start, evidence);
    return evidence->end_idle_count >= evidence->start_idle_count
            + SAN9_P1_M2B_PROBE0_MIN_TICKS
        && evidence->original_return_count == evidence->end_idle_count
        && evidence->outer_count == evidence->end_idle_count
        && evidence->nested_count == 0u
        && evidence->identity_reject_count == 0u
        && evidence->first_tid == config->target_thread_id
        && evidence->last_tid == config->target_thread_id
        && evidence->last_caller == SAN9_P1_M2B_EXACT_IDLE_CALLER
        && evidence->last_app == SAN9_P1_M2B_EXACT_APP_OBJECT;
}

static int bytes_nonzero(const uint8_t *value, size_t size)
{
    uint8_t combined = 0u;
    size_t index;
    if (value == NULL || size == 0u) {
        return 0;
    }
    for (index = 0u; index < size; ++index) {
        combined = (uint8_t)(combined | value[index]);
    }
    return combined != 0u;
}

static int make_ping_request(
    const San9P1Frame *binding,
    const San9P1M2bBootstrapConfig *config,
    uint32_t ordinal,
    San9P1Frame *request)
{
    ULONGLONG now;
    if (binding == NULL || config == NULL || request == NULL
        || ordinal == 0u || ordinal > SAN9_P1_M2B_PING_COUNT) {
        return 0;
    }
    now = GetTickCount64();
    if (now > UINT64_MAX - config->timeout_ms) {
        return 0;
    }
    *request = *binding;
    request->sequence = ordinal;
    request->issued_at_ms = now;
    request->expires_at_ms = now + config->timeout_ms;
    memset(request->result_digest, 0, sizeof(request->result_digest));
    if (!random_bytes(request->request_id, sizeof(request->request_id))
        || !bytes_nonzero(request->request_id, sizeof(request->request_id))
        || !random_bytes(request->challenge_digest,
            sizeof(request->challenge_digest))
        || !bytes_nonzero(request->challenge_digest,
            sizeof(request->challenge_digest))) {
        san9_p1_secure_zero(request, sizeof(*request));
        return 0;
    }
    return 1;
}

static int wait_for_ping_response(
    San9P1M2bShared *shared,
    uint32_t timeout_ms)
{
    ULONGLONG deadline;
    if (shared == NULL || timeout_ms == 0u) {
        return 0;
    }
    deadline = GetTickCount64() + timeout_ms;
    for (;;) {
        LONG bootstrap = interlocked_read(&shared->bootstrap_state);
        LONG request = interlocked_read(&shared->controller_request_state);
        LONG response = interlocked_read(&shared->controller_response_state);
        if (bootstrap != SAN9_P1_M2B_BOOTSTRAP_READY) {
            return 0;
        }
        if (request == SAN9_P1_M2_SLOT_COMPLETE
            && response == SAN9_P1_M2_SLOT_COMPLETE) {
            return 1;
        }
        if (GetTickCount64() >= deadline) {
            return 0;
        }
        Sleep(1u);
    }
}

static int reset_ping_slots(San9P1M2bShared *shared)
{
    if (shared == NULL
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->controller_response_state,
            SAN9_P1_M2_SLOT_EMPTY, SAN9_P1_M2_SLOT_COMPLETE)
            != SAN9_P1_M2_SLOT_COMPLETE
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->controller_request_state,
            SAN9_P1_M2_SLOT_EMPTY, SAN9_P1_M2_SLOT_COMPLETE)
            != SAN9_P1_M2_SLOT_COMPLETE) {
        return 0;
    }
    return 1;
}

static uint32_t run_ping_session(
    San9P1M2bShared *shared,
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bPingEvidence *evidence)
{
    San9P1Frame binding;
    San9P1Frame request;
    San9P1Frame response;
    San9P1DecodeStatus decode;
    uint8_t raw_request[SAN9_P1_FRAME_SIZE];
    uint8_t raw_response[SAN9_P1_FRAME_SIZE];
    uint8_t expected_digest[SAN9_P1_DIGEST_SIZE];
    uint32_t ordinal;
    uint32_t result = SAN9_P1_M2B_PING_FAILED;
    memset(&binding, 0, sizeof(binding));
    memset(&request, 0, sizeof(request));
    memset(&response, 0, sizeof(response));
    memset(raw_request, 0, sizeof(raw_request));
    memset(raw_response, 0, sizeof(raw_response));
    memset(expected_digest, 0, sizeof(expected_digest));
    if (shared == NULL || config == NULL || evidence == NULL) {
        return SAN9_P1_M2B_INVALID;
    }
    memset(evidence, 0, sizeof(*evidence));
    decode = san9_p1_decode(config->binding_frame,
        sizeof(config->binding_frame), config->hmac_key,
        sizeof(config->hmac_key), &binding);
    if (decode != SAN9_P1_DECODE_ACCEPTED
        || interlocked_read(&shared->controller_request_state)
            != SAN9_P1_M2_SLOT_EMPTY
        || interlocked_read(&shared->controller_response_state)
            != SAN9_P1_M2_SLOT_EMPTY) {
        goto cleanup;
    }
    InterlockedExchange((volatile LONG *)&shared->claim_enabled, 1);
    for (ordinal = 1u; ordinal <= SAN9_P1_M2B_PING_COUNT; ++ordinal) {
        uint32_t evidence_ordinal;
        uint32_t evidence_caller;
        if (!make_ping_request(&binding, config, ordinal, &request)
            || !san9_p1_encode(&request, config->hmac_key,
                sizeof(config->hmac_key), raw_request, sizeof(raw_request))
            || interlocked_read(&shared->controller_response_state)
                != SAN9_P1_M2_SLOT_EMPTY
            || InterlockedCompareExchange(
                (volatile LONG *)&shared->controller_request_state,
                SAN9_P1_M2_SLOT_WRITING, SAN9_P1_M2_SLOT_EMPTY)
                != SAN9_P1_M2_SLOT_EMPTY) {
            goto cleanup;
        }
        memcpy(shared->controller_request_frame, raw_request,
            sizeof(raw_request));
        MemoryBarrier();
        InterlockedExchange(
            (volatile LONG *)&shared->controller_request_state,
            SAN9_P1_M2_SLOT_READY);
        evidence->requested_count = ordinal;
        if (!wait_for_ping_response(shared, config->timeout_ms)) {
            goto cleanup;
        }
        MemoryBarrier();
        memcpy(raw_response, shared->controller_response_frame,
            sizeof(raw_response));
        evidence->completed_count = ordinal;
        if (!san9_p1_decode_and_verify_response(&request, raw_response,
                sizeof(raw_response), config->hmac_key,
                sizeof(config->hmac_key), &decode, &response)) {
            goto cleanup;
        }
        evidence_ordinal = (uint32_t)interlocked_read(
            (volatile int32_t *)&shared->last_evidence_ordinal);
        evidence_caller = (uint32_t)interlocked_read(
            (volatile int32_t *)&shared->last_evidence_caller);
        if (evidence_ordinal != ordinal
            || evidence_caller != SAN9_P1_M2B_EXACT_IDLE_CALLER
            || !san9_p1_m2_result_digest(&request, evidence_caller,
                shared->last_easy_snapshot_digest, ordinal, expected_digest)
            || !san9_p1_constant_time_equal(response.result_digest,
                expected_digest, sizeof(expected_digest))) {
            goto cleanup;
        }
        if (ordinal == 1u) {
            evidence->first_sequence = response.sequence;
            memcpy(evidence->easy_snapshot_digest,
                shared->last_easy_snapshot_digest,
                sizeof(evidence->easy_snapshot_digest));
        } else if (!san9_p1_constant_time_equal(
                evidence->easy_snapshot_digest,
                shared->last_easy_snapshot_digest,
                sizeof(evidence->easy_snapshot_digest))) {
            goto cleanup;
        }
        evidence->last_sequence = response.sequence;
        evidence->last_caller = evidence_caller;
        evidence->verified_count = ordinal;
        evidence->stable_digest_count = ordinal;
        if (!reset_ping_slots(shared)) {
            goto cleanup;
        }
        san9_p1_secure_zero(&request, sizeof(request));
        san9_p1_secure_zero(&response, sizeof(response));
        san9_p1_secure_zero(raw_request, sizeof(raw_request));
        san9_p1_secure_zero(raw_response, sizeof(raw_response));
        san9_p1_secure_zero(expected_digest, sizeof(expected_digest));
    }
    evidence->accepted_count = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->mailbox.accepted_ping_count);
    evidence->controller_ack = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->mailbox.controller_ack);
    if (evidence->requested_count != SAN9_P1_M2B_PING_COUNT
        || evidence->completed_count != SAN9_P1_M2B_PING_COUNT
        || evidence->verified_count != SAN9_P1_M2B_PING_COUNT
        || evidence->stable_digest_count != SAN9_P1_M2B_PING_COUNT
        || evidence->accepted_count != SAN9_P1_M2B_PING_COUNT
        || evidence->controller_ack != SAN9_P1_M2B_PING_COUNT
        || evidence->first_sequence != 1u
        || evidence->last_sequence != SAN9_P1_M2B_PING_COUNT) {
        goto cleanup;
    }
    result = SAN9_P1_M2B_OK;
cleanup:
    InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
    san9_p1_secure_zero(&binding, sizeof(binding));
    san9_p1_secure_zero(&request, sizeof(request));
    san9_p1_secure_zero(&response, sizeof(response));
    san9_p1_secure_zero(raw_request, sizeof(raw_request));
    san9_p1_secure_zero(raw_response, sizeof(raw_response));
    san9_p1_secure_zero(expected_digest, sizeof(expected_digest));
    return result;
}

static int exact_s3_trace(const San9P1S3TraceArea *trace)
{
    return trace != NULL && trace->magic == SAN9_P1_S3_TRACE_MAGIC
        && trace->schema_major == 1u && trace->schema_minor == 0u
        && trace->record_size == sizeof(San9P1S3TraceRecord)
        && trace->capacity == SAN9_P1_S3_TRACE_CAPACITY
        && trace->readonly_dispatch_count == SAN9_P1_S3_READ_OPCODE_COUNT
        && trace->write_dispatch_count == 0u
        && interlocked_read((volatile int32_t *)&trace->ready) == 1;
}

static void print_s3_trace_record(const San9P1S3TraceRecord *record)
{
    uint32_t index;
    (void)printf("{\"mode\":\"s3-trace\",\"seq\":%u,\"tick\":%llu,"
        "\"flags\":%u,\"failure\":%u,\"depth\":%u,"
        "\"scheduler\":%u,\"scheduler_pending\":%u,"
        "\"controller\":%u,\"controller_child\":%u,"
        "\"controller_pending\":%u,\"controller_corps\":%u,"
        "\"controller_state\":%u,\"controller_target\":%u,"
        "\"leaf\":%u,\"leaf_vptr\":%u,\"leaf_pending\":%u,"
        "\"handler\":%u,\"handler_vptr\":%u,\"handler_state\":%u,"
        "\"handler_corps\":%u,\"handler_target\":%u,"
        "\"list_vptr\":%u,\"list_first\":%u,\"list_last\":%u,"
        "\"list_count\":%u,\"list_tail\":[%u,%u,%u,%u],"
        "\"outer\":%u,\"outer_result\":%u,\"source_count\":%u,"
        "\"working_count\":%u,\"committed_count\":%u,"
        "\"selector\":%u,\"selector_maximum\":%u,"
        "\"command\":%u,\"selected_count\":%u,"
        "\"current_building\":%u,\"chain_vptr\":[",
        record->sequence, (unsigned long long)record->tick_ms,
        record->flags, record->failure_code, record->task_depth,
        record->scheduler_pointer, record->scheduler_pending,
        record->controller_pointer, record->controller_child,
        record->controller_pending, record->controller_corps,
        record->controller_state, record->controller_target,
        record->leaf_pointer, record->leaf_vptr, record->leaf_pending,
        record->handler_pointer, record->handler_vptr,
        record->handler_state, record->handler_corps, record->handler_target,
        record->handler_list_vptr, record->handler_list_first,
        record->handler_list_last, record->handler_list_count,
        record->handler_list_tail[0], record->handler_list_tail[1],
        record->handler_list_tail[2], record->handler_list_tail[3],
        record->outer_pointer, record->outer_result,
        record->outer_source_count, record->outer_working_count,
        record->outer_committed_count, record->selector_pointer,
        record->selector_maximum, record->command_pointer,
        record->global_selected_count, record->current_building);
    for (index = 0u; index < SAN9_P1_S3_CHAIN_VPTR_CAPACITY; ++index) {
        (void)printf(index == 0u ? "%u" : ",%u", record->chain_vptr[index]);
    }
    (void)puts("]}");
    (void)fflush(stdout);
}

static int drain_s3_trace(
    San9P1S3TraceArea *trace,
    San9P1M2bObserveEvidence *evidence)
{
    for (;;) {
        uint32_t read = (uint32_t)interlocked_read(
            (volatile int32_t *)&trace->read_sequence);
        uint32_t expected = read + 1u;
        San9P1S3TraceRecord *slot =
            &trace->records[read % SAN9_P1_S3_TRACE_CAPACITY];
        San9P1S3TraceRecord local;
        if ((uint32_t)interlocked_read(
                (volatile int32_t *)&slot->published_sequence) != expected) {
            break;
        }
        memcpy(&local, slot, sizeof(local));
        MemoryBarrier();
        if (local.sequence != expected
            || local.published_sequence != expected
            || (uint32_t)interlocked_read(
                (volatile int32_t *)&slot->published_sequence) != expected) {
            return 0;
        }
        print_s3_trace_record(&local);
        ++evidence->records;
        InterlockedExchange((volatile LONG *)&trace->read_sequence,
            (LONG)expected);
    }
    return 1;
}

static uint32_t run_observe_session(
    San9P1M2bShared *shared,
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bObserveEvidence *evidence)
{
    const uint32_t required = SAN9_P1_S3_FLAG_CONTROLLER
        | SAN9_P1_S3_FLAG_COMMERCE_HANDLER
        | SAN9_P1_S3_FLAG_SELECTED_FIVE
        | SAN9_P1_S3_FLAG_RETURNED_IDLE;
    uint64_t start;
    uint64_t deadline;
    int complete = 0;
    if (shared == NULL || config == NULL || evidence == NULL
        || !exact_s3_trace(&shared->operation.s3.trace)) {
        return SAN9_P1_M2B_OBSERVE_FAILED;
    }
    memset(evidence, 0, sizeof(*evidence));
    evidence->readonly_dispatch_count =
        shared->operation.s3.trace.readonly_dispatch_count;
    evidence->write_dispatch_count =
        shared->operation.s3.trace.write_dispatch_count;
    start = GetTickCount64();
    deadline = start + SAN9_P1_M2B_OBSERVE_TIMEOUT_MS;
    (void)printf("{\"mode\":\"observe\",\"state\":\"ready\","
        "\"game_pid\":%u,\"main_tid\":%u,\"timeout_ms\":%u,"
        "\"readonly_dispatch\":%u,\"write_dispatch\":0,"
        "\"business_apply\":0}\n",
        config->target_pid, config->target_thread_id,
        SAN9_P1_M2B_OBSERVE_TIMEOUT_MS,
        shared->operation.s3.trace.readonly_dispatch_count);
    (void)fflush(stdout);
    while (GetTickCount64() < deadline) {
        LONG bootstrap = interlocked_read(&shared->bootstrap_state);
        if (bootstrap == SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART) {
            evidence->restart_required = 1u;
            break;
        }
        if (!drain_s3_trace(&shared->operation.s3.trace, evidence)) {
            evidence->restart_required = 1u;
            break;
        }
        evidence->cumulative_flags = (uint32_t)interlocked_read(
            (volatile int32_t *)&shared->operation.s3.trace.cumulative_flags);
        if ((evidence->cumulative_flags & required) == required) {
            complete = 1;
            break;
        }
        Sleep(10u);
    }
    InterlockedExchange(
        (volatile LONG *)&shared->operation.s3.trace.stop_requested, 1);
    Sleep(20u);
    if (!drain_s3_trace(&shared->operation.s3.trace, evidence)) {
        evidence->restart_required = 1u;
    }
    evidence->elapsed_ms = (uint32_t)(GetTickCount64() - start);
    evidence->dropped_records = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->operation.s3.trace.dropped_records);
    evidence->capture_failures = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->operation.s3.trace.capture_failures);
    evidence->cumulative_flags = (uint32_t)interlocked_read(
        (volatile int32_t *)&shared->operation.s3.trace.cumulative_flags);
    return complete && evidence->dropped_records == 0u
        && evidence->capture_failures == 0u && evidence->restart_required == 0u
        ? SAN9_P1_M2B_OK : SAN9_P1_M2B_OBSERVE_FAILED;
}

static uint32_t run_s5_no_apply_session(
    San9P1M2bShared *shared,
    const San9P1M2bBootstrapConfig *config,
    San9P1S5NoApplyEvidence *output)
{
    uint64_t deadline;
    if (shared == NULL || config == NULL || output == NULL) {
        return SAN9_P1_M2B_INVALID;
    }
    if (g_s5_terminal_identity[0] != 'S') {
        return SAN9_P1_M2B_RESTART_REQUIRED;
    }
    memset(output, 0, sizeof(*output));
    deadline = shared->operation.s5.request.expires_at_ms;
    if (deadline == 0u
        || shared->operation.s5.request.issued_at_ms >= deadline
        || deadline - shared->operation.s5.request.issued_at_ms
            != config->timeout_ms) {
        (void)InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_RESTART,
            SAN9_P1_S5_TERMINAL_OPEN);
        return SAN9_P1_M2B_RESTART_REQUIRED;
    }
    for (;;) {
        San9S5MachineState state = san9_s5_no_apply_machine_state(
            &shared->operation.s5.machine);
        LONG menu_state = interlocked_read(&shared->operation.s5.menu.state);
        LONG terminal = interlocked_read(
            &shared->operation.s5.terminal_state);
        if (interlocked_read(&shared->bootstrap_state)
                == SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART
            || state == SAN9_S5_STATE_RESTART_REQUIRED
            || shared->operation.s5.menu.restart_required != 0u
            || terminal == SAN9_P1_S5_TERMINAL_TIMEOUT
            || terminal == SAN9_P1_S5_TERMINAL_RESTART) {
            return SAN9_P1_M2B_RESTART_REQUIRED;
        }
        if (terminal == SAN9_P1_S5_TERMINAL_REJECTED
            && state == SAN9_S5_STATE_REJECTED_PRE_EVENT) {
            return SAN9_P1_M2B_S5_NO_APPLY_FAILED;
        }
        if (terminal == SAN9_P1_S5_TERMINAL_SUCCESS
            && state == SAN9_S5_STATE_NO_APPLY_PROVEN) {
            MemoryBarrier();
            memcpy(output, &shared->operation.s5.evidence, sizeof(*output));
            if (output->magic != SAN9_P1_S5_EVIDENCE_MAGIC
                || output->structure_size != sizeof(*output)
                || output->restart_required != 0u
                || output->event_attempt_count != 1u
                || output->menu_wake_count != 1u
                || output->menu_restore_count != 1u
                || output->shadow_arm_count != 1u
                || output->execute_enter_count != 1u
                || output->restore_count != 1u
                || output->observed_apply_count != 0u
                || output->pre_commerce != output->post_commerce
                || output->pre_money != output->post_money
                || output->pre_order_flags != output->post_order_flags
                || !san9_p1_constant_time_equal(output->pre_business_digest,
                    output->post_business_digest, SAN9_S5_DIGEST_SIZE)
                || !san9_p1_s5_evidence_verify(output,
                    shared->mailbox.hmac_key,
                    sizeof(shared->mailbox.hmac_key))) {
                return SAN9_P1_M2B_RESTART_REQUIRED;
            }
            output->controller_ack = 1u;
            if (!san9_p1_s5_evidence_sign(output, shared->mailbox.hmac_key,
                    sizeof(shared->mailbox.hmac_key))) {
                return SAN9_P1_M2B_RESTART_REQUIRED;
            }
            memcpy(&shared->operation.s5.evidence, output, sizeof(*output));
            MemoryBarrier();
            return SAN9_P1_M2B_OK;
        }
        if (GetTickCount64() >= deadline) {
            int timeout = controller_s5_try_claim_timeout(shared);
            if (timeout == 1) {
                return SAN9_P1_M2B_RESTART_REQUIRED;
            }
            if (timeout == 3) {
                return SAN9_P1_M2B_S5_NO_APPLY_FAILED;
            }
            if (timeout == 2) {
                continue;
            }
            (void)menu_state;
        }
        Sleep(1u);
    }
}

static uint32_t run_s5_apply_once_session(
    San9P1M2bShared *shared,
    const San9P1M2bBootstrapConfig *config,
    San9P1S5NoApplyEvidence *output)
{
    uint64_t deadline;
    uint32_t expected_native_id;
    uint32_t expected_cost;
    uint32_t expected_order_mask;
    uint32_t failure = config == NULL ? SAN9_P1_M2B_INVALID
        : apply_failure_for_mode(config->operation_mode);
    if (shared == NULL || config == NULL || output == NULL
        || shared->operation.s5.request.expires_at_ms
            > UINT64_MAX - SAN9_P1_M2B_S5_APPLY_COMPLETION_TIMEOUT_MS) {
        return SAN9_P1_M2B_INVALID;
    }
    memset(output, 0, sizeof(*output));
    expected_native_id = is_batch_operation(config->operation_mode)
        ? shared->operation.s5.request.native_command_id
        : apply_native_id_for_mode(config->operation_mode);
    expected_cost = is_batch_operation(config->operation_mode)
        ? apply_cost_for_native(expected_native_id)
        : apply_cost_for_mode(config->operation_mode);
    expected_order_mask = is_batch_operation(config->operation_mode)
        ? apply_order_mask_for_native(expected_native_id)
        : apply_order_mask_for_mode(config->operation_mode);
    if (expected_cost == UINT32_MAX || expected_order_mask == 0u) {
        return SAN9_P1_M2B_INVALID;
    }
    deadline = shared->operation.s5.request.expires_at_ms
        + SAN9_P1_M2B_S5_APPLY_COMPLETION_TIMEOUT_MS;
    for (;;) {
        San9S5MachineState state = san9_s5_no_apply_machine_state(
            &shared->operation.s5.machine);
        LONG terminal = interlocked_read(&shared->operation.s5.terminal_state);
        if (interlocked_read(&shared->bootstrap_state)
                == SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART
            || state == SAN9_S5_STATE_RESTART_REQUIRED
            || shared->operation.s5.menu.restart_required != 0u
            || terminal == SAN9_P1_S5_TERMINAL_TIMEOUT
            || terminal == SAN9_P1_S5_TERMINAL_RESTART) {
            return SAN9_P1_M2B_RESTART_REQUIRED;
        }
        if (terminal == SAN9_P1_S5_TERMINAL_REJECTED
            && state == SAN9_S5_STATE_REJECTED_PRE_EVENT) {
            MemoryBarrier();
            if (is_batch_operation(config->operation_mode)
                && shared->operation.s5.evidence.terminal_code
                    == SAN9_P1_M2B_BATCH_REBIND_REQUIRED
                && interlocked_read(&shared->bootstrap_result)
                    == SAN9_P1_M2B_BATCH_REBIND_REQUIRED) {
                return SAN9_P1_M2B_BATCH_REBIND_REQUIRED;
            }
            return failure;
        }
        if (terminal == SAN9_P1_S5_TERMINAL_SUCCESS
            && state == SAN9_S5_STATE_VPTR_RESTORED) {
            MemoryBarrier();
            memcpy(output, &shared->operation.s5.evidence, sizeof(*output));
            if (output->magic != SAN9_P1_S5_EVIDENCE_MAGIC
                || output->structure_size != sizeof(*output)
                || output->restart_required != 0u
                || output->event_attempt_count != 1u
                || (is_batch_operation(config->operation_mode)
                    ? (output->menu_wake_count > 1u
                        || output->menu_restore_count
                            != output->menu_wake_count)
                    : (output->menu_wake_count != 1u
                        || output->menu_restore_count != 1u))
                || output->shadow_arm_count != 1u
                || output->execute_enter_count != 1u
                || output->restore_count != 1u
                || output->observed_apply_count != 1u
                || output->native_command_id != expected_native_id
                || output->post_command_value <= output->pre_command_value
                || output->post_command_value > output->command_maximum
                || output->pre_money
                    < expected_cost
                || output->post_money != output->pre_money
                    - expected_cost
                || output->command_order_mask
                    != expected_order_mask
                || output->post_order_flags != (output->pre_order_flags
                    | output->command_order_mask)
                || !san9_p1_constant_time_equal(output->pre_easy_digest,
                    output->post_easy_digest, SAN9_S5_DIGEST_SIZE)
                || !san9_p1_s5_evidence_verify(output,
                    shared->mailbox.hmac_key,
                    sizeof(shared->mailbox.hmac_key))) {
                return SAN9_P1_M2B_RESTART_REQUIRED;
            }
            output->controller_ack = 1u;
            if (!san9_p1_s5_evidence_sign(output, shared->mailbox.hmac_key,
                    sizeof(shared->mailbox.hmac_key))) {
                return SAN9_P1_M2B_RESTART_REQUIRED;
            }
            memcpy(&shared->operation.s5.evidence, output, sizeof(*output));
            MemoryBarrier();
            return SAN9_P1_M2B_OK;
        }
        if (GetTickCount64() >= deadline) {
            int timeout = controller_s5_try_claim_timeout(shared);
            if (timeout == 2) {
                continue;
            }
            return timeout == 3 ? failure
                : SAN9_P1_M2B_RESTART_REQUIRED;
        }
        Sleep(1u);
    }
}

static uint32_t run_s5_modal_probe_session(
    San9P1M2bShared *shared,
    const San9P1M2bBootstrapConfig *config,
    San9P1S5ModalProbeEvidence *output)
{
    San9P1S5ModalProbeEvidence *evidence;
    uint64_t deadline;
    char signal[64];
    if (shared == NULL || config == NULL || output == NULL) {
        return SAN9_P1_M2B_INVALID;
    }
    memset(output, 0, sizeof(*output));
    memset(signal, 0, sizeof(signal));
    evidence = &shared->operation.modal.evidence;
    fputs("WAITING_FOR_S5_MODAL_PROBE_SIGNAL type "
        SAN9_P1_M2B_S5_MODAL_PROBE_SIGNAL "\n", stdout);
    (void)fflush(stdout);
    if (fgets(signal, sizeof(signal), stdin) == NULL) {
        return SAN9_P1_M2B_S5_MODAL_PROBE_FAILED;
    }
    signal[strcspn(signal, "\r\n")] = '\0';
    if (strcmp(signal, SAN9_P1_M2B_S5_MODAL_PROBE_SIGNAL) != 0
        || interlocked_read(&shared->bootstrap_state)
            != SAN9_P1_M2B_BOOTSTRAP_READY
        || interlocked_read(&shared->operation.modal.state)
            != SAN9_P1_S5_MODAL_EMPTY) {
        return SAN9_P1_M2B_S5_MODAL_PROBE_FAILED;
    }
    puts("S5_MODAL_PROBE_SIGNAL_ACCEPTED");
    evidence->wake_post_count = 1u;
    MemoryBarrier();
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.modal.state,
            SAN9_P1_S5_MODAL_ARMED, SAN9_P1_S5_MODAL_EMPTY)
            != SAN9_P1_S5_MODAL_EMPTY
        || !PostThreadMessageW(config->target_thread_id,
            shared->registered_message, (WPARAM)shared->mapping_atom,
            (LPARAM)shared->challenge)) {
        return SAN9_P1_M2B_S5_MODAL_PROBE_FAILED;
    }
    puts("S5_MODAL_PROBE_WAKE_POSTED_VTABLE_V7");
    deadline = GetTickCount64() + config->timeout_ms;
    for (;;) {
        LONG state = interlocked_read(&shared->operation.modal.state);
        if (state == SAN9_P1_S5_MODAL_COMPLETE
            || state == SAN9_P1_S5_MODAL_REJECTED) {
            MemoryBarrier();
            memcpy(output, evidence, sizeof(*output));
            break;
        }
        if (GetTickCount64() >= deadline) {
            LONG state = interlocked_read(&shared->operation.modal.state);
            return san9_p1_s5_modal_timeout_requires_restart(state)
                ? SAN9_P1_M2B_RESTART_REQUIRED
                : SAN9_P1_M2B_S5_MODAL_PROBE_FAILED;
        }
        Sleep(SAN9_P1_M2B_S5_MODAL_POLL_MS);
    }
    if (output->state == SAN9_P1_S5_MODAL_REJECTED) {
        return output->restart_required != 0u
            && san9_p1_s5_modal_evidence_verify(output,
                shared->mailbox.hmac_key, sizeof(shared->mailbox.hmac_key))
            ? SAN9_P1_M2B_RESTART_REQUIRED
            : SAN9_P1_M2B_S5_MODAL_PROBE_FAILED;
    }
    if (!san9_p1_s5_modal_evidence_is_vtable_exact(output,
            config->target_thread_id, shared->registered_message,
            shared->mapping_atom, shared->challenge)
        || !san9_p1_s5_modal_evidence_verify(output,
            shared->mailbox.hmac_key, sizeof(shared->mailbox.hmac_key))) {
        return SAN9_P1_M2B_S5_MODAL_PROBE_FAILED;
    }
    output->controller_ack = 1u;
    if (!san9_p1_s5_modal_evidence_sign(output, shared->mailbox.hmac_key,
            sizeof(shared->mailbox.hmac_key))) {
        return SAN9_P1_M2B_S5_MODAL_PROBE_FAILED;
    }
    memcpy(evidence, output, sizeof(*output));
    MemoryBarrier();
    return SAN9_P1_M2B_OK;
}

static uint32_t controller_run(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1M2bPingEvidence *ping_evidence,
    San9P1M2bObserveEvidence *observe_evidence,
    San9P1S5NoApplyEvidence *s5_evidence,
    San9P1S5ModalProbeEvidence *modal_evidence)
{
    SECURITY_ATTRIBUTES attributes;
    PSECURITY_DESCRIPTOR descriptor = NULL;
    wchar_t mapping_name[64];
    uint32_t challenge = 0u;
    UINT message = 0u;
    ATOM atom = 0;
    HANDLE mapping = NULL;
    San9P1M2bShared *shared = NULL;
    HMODULE module = NULL;
    M2bHookProc hook_proc = NULL;
    HHOOK hook = NULL;
    San9P1Frame binding;
    DWORD window_pid = 0u;
    DWORD window_tid;
    uint32_t result = SAN9_P1_M2B_INVALID;
    int s5_failure_snapshot_printed = 0;

    memset(&binding, 0, sizeof(binding));
    if (probe_evidence == NULL
        || (config != NULL
            && config->operation_mode == SAN9_P1_M2B_OPERATION_PING
            && ping_evidence == NULL)
        || (config != NULL
            && config->operation_mode == SAN9_P1_M2B_OPERATION_OBSERVE
            && observe_evidence == NULL)
        || (config != NULL
            && is_s5_business_operation(config->operation_mode)
            && s5_evidence == NULL)
        || (config != NULL
            && config->operation_mode == SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE
            && modal_evidence == NULL)) {
        return SAN9_P1_M2B_INVALID;
    }
    memset(probe_evidence, 0, sizeof(*probe_evidence));
    if (ping_evidence != NULL) {
        memset(ping_evidence, 0, sizeof(*ping_evidence));
    }
    if (observe_evidence != NULL) {
        memset(observe_evidence, 0, sizeof(*observe_evidence));
    }
    if (s5_evidence != NULL) {
        memset(s5_evidence, 0, sizeof(*s5_evidence));
    }
    if (modal_evidence != NULL) {
        memset(modal_evidence, 0, sizeof(*modal_evidence));
    }
    if (!exact_config(config, &binding)) {
        return SAN9_P1_M2B_AUTH_REJECTED;
    }
    window_tid = GetWindowThreadProcessId((HWND)(uintptr_t)config->target_hwnd,
        &window_pid);
    if (window_tid != config->target_thread_id || window_pid != config->target_pid) {
        return SAN9_P1_M2B_AUTH_REJECTED;
    }
    if (!random_mapping_name(mapping_name, &challenge)
        || !current_user_mapping_security(&attributes, &descriptor)) {
        return SAN9_P1_M2B_ACL_FAILED;
    }
    mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, &attributes,
        PAGE_READWRITE, 0u, SAN9_P1_M2B_SHARED_SIZE, mapping_name);
    (void)LocalFree(descriptor);
    descriptor = NULL;
    if (mapping == NULL || GetLastError() == ERROR_ALREADY_EXISTS) {
        result = SAN9_P1_M2B_MAPPING_FAILED;
        goto cleanup;
    }
    shared = (San9P1M2bShared *)MapViewOfFile(mapping,
        FILE_MAP_READ | FILE_MAP_WRITE, 0u, 0u, SAN9_P1_M2B_SHARED_SIZE);
    if (shared == NULL) {
        result = SAN9_P1_M2B_MAPPING_FAILED;
        goto cleanup;
    }
    message = RegisterWindowMessageW(L"San9P1M2b.Authenticated.Bootstrap.v1");
    atom = GlobalAddAtomW(mapping_name);
    if (message < 0xC000u || atom == 0) {
        result = SAN9_P1_M2B_MAPPING_FAILED;
        goto cleanup;
    }
    memset(shared, 0, sizeof(*shared));
    shared->magic = SAN9_P1_M2B_SHARED_MAGIC;
    shared->schema_major = SAN9_P1_M2B_SCHEMA_MAJOR;
    shared->schema_minor = SAN9_P1_M2B_SCHEMA_MINOR;
    shared->declared_size = sizeof(*shared);
    shared->bootstrap_state = SAN9_P1_M2B_BOOTSTRAP_WRITING;
    shared->target_pid = config->target_pid;
    shared->target_thread_id = config->target_thread_id;
    shared->target_hwnd = config->target_hwnd;
    shared->registered_message = message;
    shared->mapping_atom = atom;
    shared->challenge = challenge;
    shared->operation_mode = config->operation_mode;
    shared->owner_token = config->owner_token;
    shared->easy_binding = config->easy_binding;
    memcpy(shared->binding_frame, config->binding_frame,
        sizeof(shared->binding_frame));
    if (!san9_p1_m2_mailbox_initialize(&shared->mailbox,
            config->hmac_key, sizeof(config->hmac_key))) {
        result = SAN9_P1_M2B_MAPPING_FAILED;
        goto cleanup;
    }
    if (is_s5_business_operation(config->operation_mode)) {
        if (!san9_s5_no_apply_machine_initialize(
                &shared->operation.s5.machine)) {
            result = SAN9_P1_M2B_AUTH_REJECTED;
            goto cleanup;
        }
    }
    if (config->operation_mode == SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE) {
        San9P1S5ModalProbeEvidence *modal =
            &shared->operation.modal.evidence;
        shared->operation.modal.state = SAN9_P1_S5_MODAL_EMPTY;
        modal->magic = SAN9_P1_S5_MODAL_EVIDENCE_MAGIC;
        modal->schema_major = SAN9_P1_M2B_SCHEMA_MAJOR;
        modal->schema_minor = SAN9_P1_M2B_SCHEMA_MINOR;
        modal->structure_size = sizeof(*modal);
        modal->state = SAN9_P1_S5_MODAL_EMPTY;
    }
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&shared->bootstrap_state,
        SAN9_P1_M2B_BOOTSTRAP_SEALED);

    module = LoadLibraryExW(config->dll_path, NULL,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (module == NULL) {
        result = SAN9_P1_M2B_HOOK_FAILED;
        goto cleanup;
    }
    hook_proc = (M2bHookProc)(void *)GetProcAddress(module,
        "San9BridgeP1M2b_GetMsgProc");
    if (hook_proc == NULL) {
        result = SAN9_P1_M2B_HOOK_FAILED;
        goto cleanup;
    }
    hook = SetWindowsHookExW(WH_GETMESSAGE, hook_proc, module,
        config->target_thread_id);
    if (hook == NULL
        || !PostThreadMessageW(config->target_thread_id, message,
            (WPARAM)atom, (LPARAM)challenge)
        || !wait_for_bootstrap(shared, config->timeout_ms)) {
        result = interlocked_read(&shared->bootstrap_result) != 0
            ? (uint32_t)interlocked_read(&shared->bootstrap_result)
            : SAN9_P1_M2B_TIMEOUT;
        goto cleanup;
    }
    if (config->operation_mode != SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE
        && !is_s5_business_operation(config->operation_mode)) {
        if (!UnhookWindowsHookEx(hook)) {
            result = SAN9_P1_M2B_HOOK_FAILED;
            hook = NULL;
            goto cleanup;
        }
        hook = NULL;
    }
    if (!wait_for_probe0(shared, config, probe_evidence)) {
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
        InterlockedExchange((volatile LONG *)&shared->bootstrap_result,
            SAN9_P1_M2B_RESTART_REQUIRED);
        InterlockedExchange((volatile LONG *)&shared->bootstrap_state,
            SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART);
        result = SAN9_P1_M2B_PROBE0_FAILED;
        goto cleanup;
    }
    if (config->operation_mode == SAN9_P1_M2B_OPERATION_PING
        && run_ping_session(shared, config, ping_evidence)
            != SAN9_P1_M2B_OK) {
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
        InterlockedExchange((volatile LONG *)&shared->bootstrap_result,
            SAN9_P1_M2B_RESTART_REQUIRED);
        InterlockedExchange((volatile LONG *)&shared->bootstrap_state,
            SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART);
        result = SAN9_P1_M2B_PING_FAILED;
        goto cleanup;
    }
    if (config->operation_mode == SAN9_P1_M2B_OPERATION_OBSERVE) {
        uint32_t observe_start = probe_evidence->start_idle_count;
        uint32_t observe_result = run_observe_session(shared, config,
            observe_evidence);
        capture_probe0_evidence(shared, observe_start, probe_evidence);
        if (observe_result != SAN9_P1_M2B_OK) {
            result = observe_result;
            goto quiesce;
        }
    }
    if (config->operation_mode == SAN9_P1_M2B_OPERATION_S5_NO_APPLY) {
        result = wait_for_s5_request_and_publish(shared, config);
        if (result != SAN9_P1_M2B_OK) {
            print_s5_failure_snapshot(shared, "request_publish_failure");
            s5_failure_snapshot_printed = 1;
            goto quiesce;
        }
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 1);
        result = run_s5_no_apply_session(shared, config, s5_evidence);
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
        if (result != SAN9_P1_M2B_OK) {
            print_s5_failure_snapshot(shared,
                "session_failure_pre_controller_poison");
            s5_failure_snapshot_printed = 1;
            if (result == SAN9_P1_M2B_RESTART_REQUIRED) {
                controller_s5_publish_restart(shared);
            }
            goto quiesce;
        }
    }
    if (is_batch_operation(config->operation_mode)) {
        result = run_s8_batch_session(shared, config, s5_evidence);
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
        if (result != SAN9_P1_M2B_OK) {
            if (result != SAN9_P1_M2B_BATCH_REBIND_REQUIRED) {
                print_s5_failure_snapshot(shared,
                    "s8_batch_failure_pre_controller_poison");
                s5_failure_snapshot_printed = 1;
            }
            if (result == SAN9_P1_M2B_RESTART_REQUIRED) {
                controller_s5_publish_restart(shared);
            }
            goto quiesce;
        }
    }
    if (is_apply_once_operation(config->operation_mode)
        && !is_batch_operation(config->operation_mode)) {
        result = wait_for_s5_request_and_publish(shared, config);
        if (result != SAN9_P1_M2B_OK) {
            print_s5_failure_snapshot(shared, "apply_request_publish_failure");
            s5_failure_snapshot_printed = 1;
            goto quiesce;
        }
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 1);
        result = run_s5_apply_once_session(shared, config, s5_evidence);
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
        if (result != SAN9_P1_M2B_OK) {
            print_s5_failure_snapshot(shared,
                "apply_session_failure_pre_controller_poison");
            s5_failure_snapshot_printed = 1;
            if (result == SAN9_P1_M2B_RESTART_REQUIRED) {
                controller_s5_publish_restart(shared);
            }
            goto quiesce;
        }
    }
    if (config->operation_mode == SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE) {
        result = run_s5_modal_probe_session(shared, config, modal_evidence);
        if (result != SAN9_P1_M2B_OK) {
            if (result == SAN9_P1_M2B_RESTART_REQUIRED) {
                InterlockedExchange((volatile LONG *)&shared->bootstrap_result,
                    SAN9_P1_M2B_RESTART_REQUIRED);
                InterlockedExchange((volatile LONG *)&shared->bootstrap_state,
                    SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART);
            }
            goto quiesce;
        }
    }
    if (config->operation_mode == SAN9_P1_M2B_OPERATION_PING
        && !wait_for_probe0(shared, config, probe_evidence)) {
        InterlockedExchange((volatile LONG *)&shared->bootstrap_result,
            SAN9_P1_M2B_RESTART_REQUIRED);
        InterlockedExchange((volatile LONG *)&shared->bootstrap_state,
            SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART);
        result = SAN9_P1_M2B_PING_FAILED;
        goto cleanup;
    }
quiesce:
    InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
    if (interlocked_read(&shared->bootstrap_state)
            == SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART) {
        result = SAN9_P1_M2B_RESTART_REQUIRED;
        goto cleanup;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->mailbox.lifecycle_state,
            SAN9_P1_M2_LIFECYCLE_QUIESCENT_PINNED,
            SAN9_P1_M2_LIFECYCLE_READY) != SAN9_P1_M2_LIFECYCLE_READY) {
        result = SAN9_P1_M2B_RESTART_REQUIRED;
        goto cleanup;
    }
    InterlockedExchange((volatile LONG *)&shared->bootstrap_state,
        SAN9_P1_M2B_BOOTSTRAP_QUIESCENT_PINNED);
    if (result == SAN9_P1_M2B_INVALID) {
        result = SAN9_P1_M2B_OK;
    }

cleanup:
    if (config != NULL
        && is_s5_business_operation(config->operation_mode)
        && result != SAN9_P1_M2B_OK && shared != NULL
        && !s5_failure_snapshot_printed) {
        print_s5_failure_snapshot(shared, "cleanup_pre_unhook");
        s5_failure_snapshot_printed = 1;
    }
    if (hook != NULL) {
        if (!UnhookWindowsHookEx(hook)) {
            if (config != NULL
                && (config->operation_mode
                        == SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE
                    || is_s5_business_operation(config->operation_mode))) {
                if (config->operation_mode
                        != SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE
                    && shared != NULL && !s5_failure_snapshot_printed) {
                    print_s5_failure_snapshot(shared,
                        "unhook_failure_pre_poison");
                    s5_failure_snapshot_printed = 1;
                }
                fputs("S5_PERSISTENT_HOOK_UNHOOK_FAILED_RESTART_REQUIRED\n",
                    stderr);
                result = SAN9_P1_M2B_RESTART_REQUIRED;
                if (shared != NULL) {
                    InterlockedExchange(
                        (volatile LONG *)&shared->bootstrap_result,
                        SAN9_P1_M2B_RESTART_REQUIRED);
                    InterlockedExchange(
                        (volatile LONG *)&shared->bootstrap_state,
                        SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART);
                }
            } else if (result == SAN9_P1_M2B_OK) {
                result = SAN9_P1_M2B_HOOK_FAILED;
            }
        }
        hook = NULL;
    }
    if (atom != 0) {
        (void)GlobalDeleteAtom(atom);
    }
    if (shared != NULL) {
        (void)UnmapViewOfFile(shared);
    }
    if (mapping != NULL) {
        (void)CloseHandle(mapping);
    }
    /* The target copy is pinned and there is deliberately no unload path.
       The controller-side loader reference dies only with controller exit. */
    san9_p1_secure_zero(&binding, sizeof(binding));
    return result;
}

uint32_t san9_p1_m2b_controller_probe0(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *evidence)
{
    if (config == NULL
        || config->operation_mode != SAN9_P1_M2B_OPERATION_PROBE0) {
        if (evidence != NULL) {
            memset(evidence, 0, sizeof(*evidence));
        }
        return SAN9_P1_M2B_INVALID;
    }
    return controller_run(config, evidence, NULL, NULL, NULL, NULL);
}

uint32_t san9_p1_m2b_controller_ping(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1M2bPingEvidence *ping_evidence)
{
    if (config == NULL
        || config->operation_mode != SAN9_P1_M2B_OPERATION_PING) {
        if (probe_evidence != NULL) {
            memset(probe_evidence, 0, sizeof(*probe_evidence));
        }
        if (ping_evidence != NULL) {
            memset(ping_evidence, 0, sizeof(*ping_evidence));
        }
        return SAN9_P1_M2B_INVALID;
    }
    return controller_run(config, probe_evidence, ping_evidence, NULL, NULL,
        NULL);
}

uint32_t san9_p1_m2b_controller_observe(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1M2bObserveEvidence *observe_evidence)
{
    if (config == NULL
        || config->operation_mode != SAN9_P1_M2B_OPERATION_OBSERVE) {
        if (probe_evidence != NULL) {
            memset(probe_evidence, 0, sizeof(*probe_evidence));
        }
        if (observe_evidence != NULL) {
            memset(observe_evidence, 0, sizeof(*observe_evidence));
        }
        return SAN9_P1_M2B_INVALID;
    }
    return controller_run(config, probe_evidence, NULL, observe_evidence, NULL,
        NULL);
}

uint32_t san9_p1_m2b_controller_s5_no_apply(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence)
{
    if (config == NULL
        || config->operation_mode != SAN9_P1_M2B_OPERATION_S5_NO_APPLY) {
        if (probe_evidence != NULL) {
            memset(probe_evidence, 0, sizeof(*probe_evidence));
        }
        if (evidence != NULL) {
            memset(evidence, 0, sizeof(*evidence));
        }
        return SAN9_P1_M2B_INVALID;
    }
    return controller_run(config, probe_evidence, NULL, NULL, evidence, NULL);
}

uint32_t san9_p1_m2b_controller_s5_apply_once(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence)
{
    if (config == NULL
        || config->operation_mode != SAN9_P1_M2B_OPERATION_S5_APPLY_ONCE) {
        if (probe_evidence != NULL) {
            memset(probe_evidence, 0, sizeof(*probe_evidence));
        }
        if (evidence != NULL) {
            memset(evidence, 0, sizeof(*evidence));
        }
        return SAN9_P1_M2B_INVALID;
    }
    return controller_run(config, probe_evidence, NULL, NULL, evidence, NULL);
}

uint32_t san9_p1_m2b_controller_s6_cultivate_apply_once(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence)
{
    if (config == NULL || config->operation_mode
            != SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE) {
        if (probe_evidence != NULL) {
            memset(probe_evidence, 0, sizeof(*probe_evidence));
        }
        if (evidence != NULL) {
            memset(evidence, 0, sizeof(*evidence));
        }
        return SAN9_P1_M2B_INVALID;
    }
    return controller_run(config, probe_evidence, NULL, NULL, evidence, NULL);
}

uint32_t san9_p1_m2b_controller_s6_patrol_apply_once(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence)
{
    if (config == NULL || config->operation_mode
            != SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE) {
        if (probe_evidence != NULL) {
            memset(probe_evidence, 0, sizeof(*probe_evidence));
        }
        if (evidence != NULL) {
            memset(evidence, 0, sizeof(*evidence));
        }
        return SAN9_P1_M2B_INVALID;
    }
    return controller_run(config, probe_evidence, NULL, NULL, evidence, NULL);
}

uint32_t san9_p1_m2b_controller_s6_train_apply_once(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence)
{
    if (config == NULL || config->operation_mode
            != SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE) {
        if (probe_evidence != NULL) {
            memset(probe_evidence, 0, sizeof(*probe_evidence));
        }
        if (evidence != NULL) {
            memset(evidence, 0, sizeof(*evidence));
        }
        return SAN9_P1_M2B_INVALID;
    }
    return controller_run(config, probe_evidence, NULL, NULL, evidence, NULL);
}

uint32_t san9_p1_m2b_controller_s6_repair_apply_once(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence)
{
    if (config == NULL || config->operation_mode
            != SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE) {
        if (probe_evidence != NULL) memset(probe_evidence, 0, sizeof(*probe_evidence));
        if (evidence != NULL) memset(evidence, 0, sizeof(*evidence));
        return SAN9_P1_M2B_INVALID;
    }
    return controller_run(config, probe_evidence, NULL, NULL, evidence, NULL);
}

uint32_t san9_p1_m2b_controller_s8_basic_batch(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence)
{
    if (config == NULL || config->operation_mode
            != SAN9_P1_M2B_OPERATION_S8_BASIC_BATCH) {
        if (probe_evidence != NULL) memset(probe_evidence, 0, sizeof(*probe_evidence));
        if (evidence != NULL) memset(evidence, 0, sizeof(*evidence));
        return SAN9_P1_M2B_INVALID;
    }
    return controller_run(config, probe_evidence, NULL, NULL, evidence, NULL);
}

uint32_t san9_p1_m2b_controller_s8_wealthy_batch(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence)
{
    if (config == NULL || config->operation_mode
            != SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH) {
        if (probe_evidence != NULL) memset(probe_evidence, 0, sizeof(*probe_evidence));
        if (evidence != NULL) memset(evidence, 0, sizeof(*evidence));
        return SAN9_P1_M2B_INVALID;
    }
    return controller_run(config, probe_evidence, NULL, NULL, evidence, NULL);
}

uint32_t san9_p1_m2b_controller_s5_modal_probe(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5ModalProbeEvidence *evidence)
{
    if (config == NULL
        || config->operation_mode != SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE) {
        if (probe_evidence != NULL) {
            memset(probe_evidence, 0, sizeof(*probe_evidence));
        }
        if (evidence != NULL) {
            memset(evidence, 0, sizeof(*evidence));
        }
        return SAN9_P1_M2B_INVALID;
    }
    return controller_run(config, probe_evidence, NULL, NULL, NULL, evidence);
}

static int random_bytes(void *output, size_t size)
{
    return output != NULL && size != 0u && size <= ULONG_MAX
        && BCryptGenRandom(NULL, (PUCHAR)output, (ULONG)size,
            BCRYPT_USE_SYSTEM_PREFERRED_RNG) == 0;
}

static int hex_nibble(char value)
{
    if (value >= '0' && value <= '9') {
        return value - '0';
    }
    if (value >= 'A' && value <= 'F') {
        return value - 'A' + 10;
    }
    return -1;
}

static int digest_from_hex(
    const char text[65],
    uint8_t output[SAN9_P1_DIGEST_SIZE])
{
    size_t index;
    if (text == NULL || output == NULL || text[64] != '\0') {
        return 0;
    }
    for (index = 0u; index < SAN9_P1_DIGEST_SIZE; ++index) {
        int high = hex_nibble(text[index * 2u]);
        int low = hex_nibble(text[index * 2u + 1u]);
        if (high < 0 || low < 0) {
            return 0;
        }
        output[index] = (uint8_t)((high << 4) | low);
    }
    return 1;
}

static void digest_two(
    const void *first,
    size_t first_size,
    const void *second,
    size_t second_size,
    uint8_t output[SAN9_P1_DIGEST_SIZE])
{
    San9P1Sha256Context sha;
    san9_p1_sha256_initialize(&sha);
    san9_p1_sha256_update(&sha, (const uint8_t *)first, first_size);
    san9_p1_sha256_update(&sha, (const uint8_t *)second, second_size);
    san9_p1_sha256_finish(&sha, output);
}

static int controller_and_bridge_paths(
    wchar_t controller_path[260],
    wchar_t bridge_path[260],
    uint32_t operation_mode)
{
    DWORD length;
    wchar_t *separator;
    static const wchar_t no_apply_name[] = L"bridge.dll";
    static const wchar_t apply_name[] = L"bridge_apply_once.dll";
    static const wchar_t cultivate_name[] =
        L"bridge_s6_cultivate_apply_once.dll";
    static const wchar_t patrol_name[] =
        L"bridge_s6_patrol_apply_once.dll";
    static const wchar_t train_name[] =
        L"bridge_s6_train_apply_once.dll";
    static const wchar_t repair_name[] =
        L"bridge_s6_repair_apply_once.dll";
    static const wchar_t s8_basic_name[] = L"bridge_s8_basic_batch.dll";
    static const wchar_t s8_wealthy_name[] = L"bridge_s8_wealthy_batch.dll";
    const wchar_t *bridge_name = operation_mode
            == SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH
        ? s8_wealthy_name : operation_mode
            == SAN9_P1_M2B_OPERATION_S8_BASIC_BATCH
        ? s8_basic_name : operation_mode
            == SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE
        ? repair_name : operation_mode
            == SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE
        ? train_name : operation_mode
            == SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE
        ? patrol_name : operation_mode
                == SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE
            ? cultivate_name
            : operation_mode == SAN9_P1_M2B_OPERATION_S5_APPLY_ONCE
                ? apply_name : no_apply_name;
    size_t bridge_name_count = wcslen(bridge_name) + 1u;
    length = GetModuleFileNameW(NULL, controller_path, 260u);
    if (length == 0u || length >= 260u) {
        return 0;
    }
    separator = wcsrchr(controller_path, L'\\');
    if (separator == NULL) {
        return 0;
    }
    if ((size_t)(separator - controller_path) + 1u
            + bridge_name_count > 260u) {
        return 0;
    }
    memcpy(bridge_path, controller_path,
        ((size_t)(separator - controller_path) + 1u) * sizeof(wchar_t));
    (void)wcscpy(bridge_path + (separator - controller_path) + 1, bridge_name);
    return GetFileAttributesW(bridge_path) != INVALID_FILE_ATTRIBUTES;
}

static uint32_t wait_for_s5_request_and_publish(
    San9P1M2bShared *shared,
    const San9P1M2bBootstrapConfig *config)
{
    HANDLE process = NULL;
    San9P1Frame binding;
    San9S5CurrentContextSnapshot first;
    San9S5CurrentContextSnapshot second;
    San9S5NoApplyRequest request;
    uint32_t person_ids[SAN9_S5_TOP5_COUNT];
    uint8_t binding_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t nonce[SAN9_S5_NONCE_SIZE];
    uint64_t deadline;
    char menu_signal[64];
    San9S5CurrentContextStatus context_status =
        SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    uint32_t index;
    int apply_once = config != NULL
        && is_apply_once_operation(config->operation_mode);
    int cultivate = config != NULL && config->operation_mode
        == SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE;
    int patrol = config != NULL && config->operation_mode
        == SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE;
    int train = config != NULL && config->operation_mode
        == SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE;
    int repair = config != NULL && config->operation_mode
        == SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE;
    const char *mode_name = repair ? "s6-repair-apply-once"
        : train ? "s6-train-apply-once"
        : patrol ? "s6-patrol-apply-once"
        : cultivate ? "s6-cultivate-apply-once"
            : apply_once ? "s5-apply-once" : "s5-no-apply";
    uint32_t result = apply_once ? apply_failure_for_mode(config->operation_mode)
        : SAN9_P1_M2B_S5_NO_APPLY_FAILED;
    memset(&binding, 0, sizeof(binding));
    memset(&first, 0, sizeof(first));
    memset(&second, 0, sizeof(second));
    memset(&request, 0, sizeof(request));
    memset(person_ids, 0, sizeof(person_ids));
    memset(binding_digest, 0, sizeof(binding_digest));
    memset(nonce, 0, sizeof(nonce));
    memset(menu_signal, 0, sizeof(menu_signal));
    if (shared == NULL || config == NULL
        || san9_p1_decode(config->binding_frame, sizeof(config->binding_frame),
            config->hmac_key, sizeof(config->hmac_key), &binding)
            != SAN9_P1_DECODE_ACCEPTED
        || binding.game_pid != config->target_pid
        || binding.main_tid != config->target_thread_id
        || binding.game_hwnd != config->target_hwnd
        || binding.game_generation == 0u) {
        goto cleanup;
    }
    process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ,
        FALSE, config->target_pid);
    if (process == NULL) {
        goto cleanup;
    }
    sha256_bytes(config->binding_frame, sizeof(config->binding_frame),
        binding_digest);
    (void)printf("{\"mode\":\"%s\"," 
        "\"phase\":\"WAITING_FOR_USER_MENU_SIGNAL\"," 
        "\"game_pid\":%lu}\n", mode_name,
        (unsigned long)config->target_pid);
    (void)fflush(stdout);
    if (fgets(menu_signal, sizeof(menu_signal), stdin) == NULL) {
        goto cleanup;
    }
    menu_signal[strcspn(menu_signal, "\r\n")] = '\0';
    if (strcmp(menu_signal, SAN9_P1_M2B_S5_MENU_SIGNAL) != 0
        || interlocked_read(&shared->bootstrap_state)
            != SAN9_P1_M2B_BOOTSTRAP_READY
        || interlocked_read(&shared->claim_enabled) != 0
        || san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
            != SAN9_S5_STATE_EMPTY) {
        goto cleanup;
    }
    (void)printf("{\"mode\":\"%s\"," 
        "\"phase\":\"USER_MENU_SIGNAL_ACCEPTED\"," 
        "\"capture_wait_ms\":%llu}\n",
        mode_name,
        (unsigned long long)SAN9_P1_M2B_S5_MENU_WAIT_MS);
    (void)fflush(stdout);
    deadline = GetTickCount64() + SAN9_P1_M2B_S5_MENU_WAIT_MS;
    while (GetTickCount64() < deadline) {
        if (interlocked_read(&shared->bootstrap_state)
                != SAN9_P1_M2B_BOOTSTRAP_READY
            || interlocked_read(&shared->claim_enabled) != 0
            || san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
                != SAN9_S5_STATE_EMPTY) {
            goto cleanup;
        }
        context_status = repair
            ? san9_s6_repair_current_context_capture_handle_ab(process,
                config->target_pid, binding.game_generation,
                config->target_thread_id,
                (HWND)(uintptr_t)config->target_hwnd, &first, &second)
            : train
            ? san9_s6_train_current_context_capture_handle_ab(process,
                config->target_pid, binding.game_generation,
                config->target_thread_id,
                (HWND)(uintptr_t)config->target_hwnd, &first, &second)
            : patrol
            ? san9_s6_patrol_current_context_capture_handle_ab(process,
                config->target_pid, binding.game_generation,
                config->target_thread_id,
                (HWND)(uintptr_t)config->target_hwnd, &first, &second)
            : cultivate
                ? san9_s6_cultivate_current_context_capture_handle_ab(process,
                    config->target_pid, binding.game_generation,
                    config->target_thread_id,
                    (HWND)(uintptr_t)config->target_hwnd, &first, &second)
                : san9_s5_current_context_capture_handle_ab(process,
                    config->target_pid, binding.game_generation,
                    config->target_thread_id,
                    (HWND)(uintptr_t)config->target_hwnd, &first, &second);
        if (context_status == SAN9_S5_CURRENT_CONTEXT_OK) {
            uint64_t now_ms;
            for (index = 0u; index < SAN9_S5_TOP5_COUNT; ++index) {
                person_ids[index] = first.top5[index].person_id;
            }
            if (!random_bytes(nonce, sizeof(nonce))) {
                goto cleanup;
            }
            now_ms = GetTickCount64();
            if (now_ms > UINT64_MAX - config->timeout_ms) {
                goto cleanup;
            }
            if ((repair
                    ? san9_s6_repair_apply_once_request_initialize(&request,
                        first.city_id, first.corps_id, person_ids,
                        now_ms, now_ms + config->timeout_ms,
                        nonce, binding_digest, first.canonical_digest)
                    : train
                    ? san9_s6_train_apply_once_request_initialize(&request,
                        first.city_id, first.corps_id, person_ids,
                        now_ms, now_ms + config->timeout_ms,
                        nonce, binding_digest, first.canonical_digest)
                    : patrol
                    ? san9_s6_patrol_apply_once_request_initialize(&request,
                        first.city_id, first.corps_id, person_ids,
                        now_ms, now_ms + config->timeout_ms,
                        nonce, binding_digest, first.canonical_digest)
                    : cultivate
                        ? san9_s6_cultivate_apply_once_request_initialize(
                            &request, first.city_id, first.corps_id, person_ids,
                            now_ms, now_ms + config->timeout_ms,
                            nonce, binding_digest, first.canonical_digest)
                        : apply_once
                            ? san9_s5_apply_once_request_initialize(&request,
                                first.city_id, first.corps_id, person_ids,
                                now_ms, now_ms + config->timeout_ms,
                                nonce, binding_digest, first.canonical_digest)
                            : san9_s5_no_apply_request_initialize(&request,
                                first.city_id, first.corps_id, person_ids,
                                now_ms, now_ms + config->timeout_ms,
                                nonce, binding_digest, first.canonical_digest))
                    != SAN9_S5_REQUEST_VALID
                || (repair
                    ? san9_s6_repair_apply_once_request_sign(&request,
                        config->hmac_key, sizeof(config->hmac_key))
                    : train
                    ? san9_s6_train_apply_once_request_sign(&request,
                        config->hmac_key, sizeof(config->hmac_key))
                    : patrol
                    ? san9_s6_patrol_apply_once_request_sign(&request,
                        config->hmac_key, sizeof(config->hmac_key))
                    : cultivate
                        ? san9_s6_cultivate_apply_once_request_sign(&request,
                            config->hmac_key, sizeof(config->hmac_key))
                        : apply_once
                            ? san9_s5_apply_once_request_sign(&request,
                                config->hmac_key, sizeof(config->hmac_key))
                            : san9_s5_no_apply_request_sign(&request,
                                config->hmac_key, sizeof(config->hmac_key)))
                    != SAN9_S5_REQUEST_VALID) {
                goto cleanup;
            }
            shared->operation.s5.request = request;
            MemoryBarrier();
            if (InterlockedCompareExchange(
                    (volatile LONG *)&shared->operation.s5.terminal_state,
                    SAN9_P1_S5_TERMINAL_OPEN,
                    SAN9_P1_S5_TERMINAL_EMPTY)
                        != SAN9_P1_S5_TERMINAL_EMPTY
                || san9_s5_no_apply_machine_publish(
                    &shared->operation.s5.machine,
                    &shared->operation.s5.request) != SAN9_S5_MACHINE_OK
                || InterlockedCompareExchange(
                    (volatile LONG *)&shared->operation.s5.menu.state,
                    SAN9_P1_S5_MENU_ARMED, SAN9_P1_S5_MENU_EMPTY)
                        != SAN9_P1_S5_MENU_EMPTY) {
                goto cleanup;
            }
            shared->operation.s5.menu.wake_post_count = 1u;
            InterlockedExchange((volatile LONG *)&shared->claim_enabled, 1);
            MemoryBarrier();
            if (!PostThreadMessageW(config->target_thread_id,
                    shared->registered_message,
                    (WPARAM)shared->mapping_atom,
                    (LPARAM)shared->challenge)) {
                print_s5_failure_snapshot(shared,
                    "wake_post_failure_pre_controller_poison");
                InterlockedExchange(
                    (volatile LONG *)&shared->operation.s5.terminal_state,
                    SAN9_P1_S5_TERMINAL_RESTART);
                controller_s5_publish_restart(shared);
                result = SAN9_P1_M2B_RESTART_REQUIRED;
                goto cleanup;
            }
            (void)printf("{\"mode\":\"%s\"," 
                "\"phase\":\"REQUEST_SEALED_WAKE_POSTED_V8\",\"city\":%lu," 
                "\"corps\":%lu,\"top5\":[%lu,%lu,%lu,%lu,%lu]}\n",
                mode_name,
                (unsigned long)first.city_id,
                (unsigned long)first.corps_id,
                (unsigned long)person_ids[0],
                (unsigned long)person_ids[1],
                (unsigned long)person_ids[2],
                (unsigned long)person_ids[3],
                (unsigned long)person_ids[4]);
            (void)fflush(stdout);
            result = SAN9_P1_M2B_OK;
            goto cleanup;
        }
        if (context_status == SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT
            || context_status == SAN9_S5_CURRENT_CONTEXT_BINDING_MISMATCH
            || context_status == SAN9_S5_CURRENT_CONTEXT_DIGEST_FAILED) {
            goto cleanup;
        }
        Sleep(SAN9_P1_M2B_S5_MENU_POLL_MS);
    }
    (void)printf("{\"mode\":\"%s\"," 
        "\"phase\":\"MENU_WAIT_FAILED\"," 
        "\"context_status\":%u}\n", mode_name,
        (unsigned)context_status);
    (void)fflush(stdout);
cleanup:
    if (process != NULL) {
        (void)CloseHandle(process);
    }
    san9_p1_secure_zero(&binding, sizeof(binding));
    san9_p1_secure_zero(&first, sizeof(first));
    san9_p1_secure_zero(&second, sizeof(second));
    san9_p1_secure_zero(&request, sizeof(request));
    san9_p1_secure_zero(person_ids, sizeof(person_ids));
    san9_p1_secure_zero(binding_digest, sizeof(binding_digest));
    san9_p1_secure_zero(nonce, sizeof(nonce));
    san9_p1_secure_zero(menu_signal, sizeof(menu_signal));
    return result;
}

static San9S5CurrentContextStatus s8_capture_command(
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
}

static int s8_is_skip_status(San9S5CurrentContextStatus status)
{
    return status == SAN9_S5_CURRENT_CONTEXT_MONEY_INSUFFICIENT
        || status == SAN9_S5_CURRENT_CONTEXT_COMMERCE_COMPLETE
        || status == SAN9_S6_CURRENT_CONTEXT_CULTIVATE_COMPLETE
        || status == SAN9_S6_CURRENT_CONTEXT_PATROL_COMPLETE
        || status == SAN9_S6_CURRENT_CONTEXT_TRAIN_COMPLETE
        || status == SAN9_S6_CURRENT_CONTEXT_TRAIN_NO_TROOPS
        || status == SAN9_S6_CURRENT_CONTEXT_REPAIR_COMPLETE
        || status == SAN9_S5_CURRENT_CONTEXT_ORDER_CONFLICT
        || status == SAN9_S5_CURRENT_CONTEXT_READY_TOP5_UNAVAILABLE;
}

static uint32_t s8_publish_request(
    San9P1M2bShared *shared,
    const San9P1M2bBootstrapConfig *config,
    const uint8_t binding_digest[SAN9_S5_DIGEST_SIZE],
    const San9S5CurrentContextSnapshot *snapshot,
    uint32_t native_command_id,
    int first_wake)
{
    San9S5NoApplyRequest request;
    uint32_t person_ids[SAN9_S5_TOP5_COUNT];
    uint8_t nonce[SAN9_S5_NONCE_SIZE];
    uint64_t now_ms;
    uint32_t index;
    uint32_t menu_state = first_wake ? SAN9_P1_S5_MENU_ARMED
        : SAN9_P1_S5_MENU_BATCH_IDLE_ARMED;
    San9S5RequestStatus request_status;
    const char *mode_name = config != NULL && config->operation_mode
            == SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH
        ? "s8-wealthy-batch" : "s8-basic-batch";
    memset(&request, 0, sizeof(request));
    memset(person_ids, 0, sizeof(person_ids));
    memset(nonce, 0, sizeof(nonce));
    if (shared == NULL || config == NULL || snapshot == NULL
        || apply_order_mask_for_native(native_command_id) == 0u
        || apply_cost_for_native(native_command_id) == UINT32_MAX
        || interlocked_read(&shared->bootstrap_state)
            != SAN9_P1_M2B_BOOTSTRAP_READY
        || interlocked_read(&shared->claim_enabled) != 0
        || san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
            != SAN9_S5_STATE_EMPTY
        || interlocked_read(&shared->operation.s5.terminal_state)
            != SAN9_P1_S5_TERMINAL_EMPTY
        || interlocked_read(&shared->operation.s5.menu.state)
            != SAN9_P1_S5_MENU_EMPTY
        || !random_bytes(nonce, sizeof(nonce))) {
        goto fail;
    }
    for (index = 0u; index < SAN9_S5_TOP5_COUNT; ++index) {
        person_ids[index] = snapshot->top5[index].person_id;
    }
    now_ms = GetTickCount64();
    if (now_ms > UINT64_MAX - config->timeout_ms) {
        goto fail;
    }
    switch (native_command_id) {
    case SAN9_P1_M2B_PATROL_NATIVE_ID:
        request_status = san9_s6_patrol_apply_once_request_initialize(&request,
            snapshot->city_id, snapshot->corps_id, person_ids,
            now_ms, now_ms + config->timeout_ms, nonce, binding_digest,
            snapshot->canonical_digest);
        break;
    case SAN9_P1_M2B_COMMERCE_NATIVE_ID:
        request_status = san9_s5_apply_once_request_initialize(&request,
            snapshot->city_id, snapshot->corps_id, person_ids,
            now_ms, now_ms + config->timeout_ms, nonce, binding_digest,
            snapshot->canonical_digest);
        break;
    case SAN9_P1_M2B_CULTIVATE_NATIVE_ID:
        request_status = san9_s6_cultivate_apply_once_request_initialize(&request,
            snapshot->city_id, snapshot->corps_id, person_ids,
            now_ms, now_ms + config->timeout_ms, nonce, binding_digest,
            snapshot->canonical_digest);
        break;
    case SAN9_P1_M2B_TRAIN_NATIVE_ID:
        request_status = san9_s6_train_apply_once_request_initialize(&request,
            snapshot->city_id, snapshot->corps_id, person_ids,
            now_ms, now_ms + config->timeout_ms, nonce, binding_digest,
            snapshot->canonical_digest);
        break;
    case SAN9_P1_M2B_REPAIR_NATIVE_ID:
        request_status = san9_s6_repair_apply_once_request_initialize(&request,
            snapshot->city_id, snapshot->corps_id, person_ids,
            now_ms, now_ms + config->timeout_ms, nonce, binding_digest,
            snapshot->canonical_digest);
        break;
    default:
        request_status = SAN9_S5_REQUEST_INVALID_ARGUMENT;
        break;
    }
    if (request_status != SAN9_S5_REQUEST_VALID) {
        goto fail;
    }
    switch (native_command_id) {
    case SAN9_P1_M2B_PATROL_NATIVE_ID:
        request_status = san9_s6_patrol_apply_once_request_sign(&request,
            config->hmac_key, sizeof(config->hmac_key));
        break;
    case SAN9_P1_M2B_COMMERCE_NATIVE_ID:
        request_status = san9_s5_apply_once_request_sign(&request,
            config->hmac_key, sizeof(config->hmac_key));
        break;
    case SAN9_P1_M2B_CULTIVATE_NATIVE_ID:
        request_status = san9_s6_cultivate_apply_once_request_sign(&request,
            config->hmac_key, sizeof(config->hmac_key));
        break;
    case SAN9_P1_M2B_TRAIN_NATIVE_ID:
        request_status = san9_s6_train_apply_once_request_sign(&request,
            config->hmac_key, sizeof(config->hmac_key));
        break;
    case SAN9_P1_M2B_REPAIR_NATIVE_ID:
        request_status = san9_s6_repair_apply_once_request_sign(&request,
            config->hmac_key, sizeof(config->hmac_key));
        break;
    default:
        request_status = SAN9_S5_REQUEST_INVALID_ARGUMENT;
        break;
    }
    if (request_status != SAN9_S5_REQUEST_VALID) {
        goto fail;
    }
    shared->operation.s5.request = request;
    MemoryBarrier();
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_OPEN, SAN9_P1_S5_TERMINAL_EMPTY)
                != SAN9_P1_S5_TERMINAL_EMPTY
        || san9_s5_no_apply_machine_publish(&shared->operation.s5.machine,
            &shared->operation.s5.request) != SAN9_S5_MACHINE_OK) {
        controller_s5_publish_restart(shared);
        san9_p1_secure_zero(&request, sizeof(request));
        san9_p1_secure_zero(nonce, sizeof(nonce));
        return SAN9_P1_M2B_RESTART_REQUIRED;
    }
    InterlockedExchange((volatile LONG *)&shared->claim_enabled, 1);
    MemoryBarrier();
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.menu.state,
            (LONG)menu_state, SAN9_P1_S5_MENU_EMPTY)
                != SAN9_P1_S5_MENU_EMPTY) {
        controller_s5_publish_restart(shared);
        san9_p1_secure_zero(&request, sizeof(request));
        san9_p1_secure_zero(nonce, sizeof(nonce));
        return SAN9_P1_M2B_RESTART_REQUIRED;
    }
    if (first_wake) {
        shared->operation.s5.menu.wake_post_count = 1u;
    }
    if (first_wake && !PostThreadMessageW(config->target_thread_id,
            shared->registered_message, (WPARAM)shared->mapping_atom,
            (LPARAM)shared->challenge)) {
        InterlockedExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_RESTART);
        controller_s5_publish_restart(shared);
        san9_p1_secure_zero(&request, sizeof(request));
        san9_p1_secure_zero(nonce, sizeof(nonce));
        return SAN9_P1_M2B_RESTART_REQUIRED;
    }
    (void)printf("{\"mode\":\"%s\"," 
        "\"phase\":\"STEP_REQUEST_PUBLISHED\",\"native_id\":%lu,"
        "\"trigger\":\"%s\",\"city\":%lu,\"corps\":%lu,"
        "\"top5\":[%lu,%lu,%lu,%lu,%lu]}\n",
        mode_name, (unsigned long)native_command_id,
        first_wake ? "menu" : "idle",
        (unsigned long)snapshot->city_id,
        (unsigned long)snapshot->corps_id,
        (unsigned long)person_ids[0], (unsigned long)person_ids[1],
        (unsigned long)person_ids[2], (unsigned long)person_ids[3],
        (unsigned long)person_ids[4]);
    (void)fflush(stdout);
    san9_p1_secure_zero(&request, sizeof(request));
    san9_p1_secure_zero(nonce, sizeof(nonce));
    return SAN9_P1_M2B_OK;
fail:
    san9_p1_secure_zero(&request, sizeof(request));
    san9_p1_secure_zero(nonce, sizeof(nonce));
    return config == NULL ? SAN9_P1_M2B_INVALID
        : apply_failure_for_mode(config->operation_mode);
}

static uint32_t s8_reset_transaction_after_ack(San9P1M2bShared *shared)
{
    uint64_t deadline;
    if (shared == NULL
        || interlocked_read(&shared->operation.s5.terminal_state)
            != SAN9_P1_S5_TERMINAL_SUCCESS
        || san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
            != SAN9_S5_STATE_VPTR_RESTORED
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.menu.state,
            SAN9_P1_S5_MENU_BATCH_RESET_REQUESTED,
            SAN9_P1_S5_MENU_STARTED) != SAN9_P1_S5_MENU_STARTED) {
        return SAN9_P1_M2B_RESTART_REQUIRED;
    }
    deadline = GetTickCount64() + UINT64_C(5000);
    while (GetTickCount64() < deadline
        && interlocked_read(&shared->operation.s5.menu.state)
            != SAN9_P1_S5_MENU_BATCH_RESET_ACK) {
        if (interlocked_read(&shared->bootstrap_state)
                != SAN9_P1_M2B_BOOTSTRAP_READY) {
            return SAN9_P1_M2B_RESTART_REQUIRED;
        }
        Sleep(1u);
    }
    if (interlocked_read(&shared->operation.s5.menu.state)
            != SAN9_P1_S5_MENU_BATCH_RESET_ACK) {
        return SAN9_P1_M2B_RESTART_REQUIRED;
    }
    memset(&shared->operation.s5, 0, sizeof(shared->operation.s5));
    if (!san9_s5_no_apply_machine_initialize(
            &shared->operation.s5.machine)) {
        return SAN9_P1_M2B_RESTART_REQUIRED;
    }
    MemoryBarrier();
    return SAN9_P1_M2B_OK;
}

static uint32_t run_s8_batch_session(
    San9P1M2bShared *shared,
    const San9P1M2bBootstrapConfig *config,
    San9P1S5NoApplyEvidence *output)
{
    static const uint32_t basic_commands[2] = {
        SAN9_P1_M2B_COMMERCE_NATIVE_ID,
        SAN9_P1_M2B_CULTIVATE_NATIVE_ID
    };
    static const uint32_t wealthy_commands[5] = {
        SAN9_P1_M2B_PATROL_NATIVE_ID,
        SAN9_P1_M2B_COMMERCE_NATIVE_ID,
        SAN9_P1_M2B_CULTIVATE_NATIVE_ID,
        SAN9_P1_M2B_TRAIN_NATIVE_ID,
        SAN9_P1_M2B_REPAIR_NATIVE_ID
    };
    const uint32_t *commands;
    uint32_t command_count;
    const char *mode_name = "s8-batch";
    HANDLE process = NULL;
    San9P1Frame binding;
    San9S5CurrentContextSnapshot first;
    San9S5CurrentContextSnapshot second;
    San9P1S5NoApplyEvidence step_evidence;
    uint8_t binding_digest[SAN9_S5_DIGEST_SIZE];
    char menu_signal[64];
    San9S5BoundCurrentCity bound_city;
    uint32_t executed = 0u;
    uint32_t skipped = 0u;
    uint32_t step;
    uint32_t result = SAN9_P1_M2B_INVALID;
    memset(&binding, 0, sizeof(binding));
    memset(&first, 0, sizeof(first));
    memset(&second, 0, sizeof(second));
    memset(&step_evidence, 0, sizeof(step_evidence));
    memset(&bound_city, 0, sizeof(bound_city));
    memset(binding_digest, 0, sizeof(binding_digest));
    memset(menu_signal, 0, sizeof(menu_signal));
    if (shared == NULL || config == NULL || output == NULL
        || !is_batch_operation(config->operation_mode)
        || san9_p1_decode(config->binding_frame, sizeof(config->binding_frame),
            config->hmac_key, sizeof(config->hmac_key), &binding)
                != SAN9_P1_DECODE_ACCEPTED
        || binding.game_pid != config->target_pid
        || binding.main_tid != config->target_thread_id
        || binding.game_hwnd != config->target_hwnd
        || binding.game_generation == 0u) {
        goto cleanup;
    }
    if (config->operation_mode == SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH) {
        commands = wealthy_commands;
        command_count = 5u;
        mode_name = "s8-wealthy-batch";
    } else {
        commands = basic_commands;
        command_count = 2u;
        mode_name = "s8-basic-batch";
    }
    result = apply_failure_for_mode(config->operation_mode);
    memset(output, 0, sizeof(*output));
    process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ,
        FALSE, config->target_pid);
    if (process == NULL) {
        goto cleanup;
    }
    sha256_bytes(config->binding_frame, sizeof(config->binding_frame),
        binding_digest);
    (void)printf("{\"mode\":\"%s\"," 
        "\"phase\":\"WAITING_FOR_USER_MENU_SIGNAL\",\"game_pid\":%lu}\n",
        mode_name, (unsigned long)config->target_pid);
    (void)fflush(stdout);
    if (fgets(menu_signal, sizeof(menu_signal), stdin) == NULL) {
        goto cleanup;
    }
    menu_signal[strcspn(menu_signal, "\r\n")] = '\0';
    if (strcmp(menu_signal, SAN9_P1_M2B_S5_MENU_SIGNAL) != 0
        || interlocked_read(&shared->bootstrap_state)
            != SAN9_P1_M2B_BOOTSTRAP_READY
        || interlocked_read(&shared->claim_enabled) != 0
        || san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
            != SAN9_S5_STATE_EMPTY) {
        goto cleanup;
    }
    for (step = 0u; step < command_count; ++step) {
        San9S5CurrentContextStatus status;
        uint64_t capture_deadline = GetTickCount64()
            + SAN9_P1_M2B_S5_MENU_WAIT_MS;
        do {
            memset(&first, 0, sizeof(first));
            memset(&second, 0, sizeof(second));
            status = s8_capture_command(process, config,
                binding.game_generation, commands[step],
                bound_city.controller_pointer == 0u ? NULL : &bound_city,
                &first, &second);
            if (status != SAN9_S5_CURRENT_CONTEXT_CONTROLLER_NOT_IDLE
                && status != SAN9_S5_CURRENT_CONTEXT_AB_MISMATCH
                && status != SAN9_S5_CURRENT_CONTEXT_READ_FAILED) {
                break;
            }
            Sleep(SAN9_P1_M2B_S5_MENU_POLL_MS);
        } while (GetTickCount64() < capture_deadline);
        if (status == SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID) {
            result = SAN9_P1_M2B_BATCH_REBIND_REQUIRED;
            goto cleanup;
        }
        if (s8_is_skip_status(status)) {
            ++skipped;
            (void)printf("{\"mode\":\"%s\"," 
                "\"phase\":\"STEP_SKIPPED\",\"native_id\":%lu,"
                "\"context_status\":%u}\n",
                mode_name, (unsigned long)commands[step], (unsigned)status);
            (void)fflush(stdout);
            continue;
        }
        if (status != SAN9_S5_CURRENT_CONTEXT_OK) {
            goto cleanup;
        }
        if (bound_city.controller_pointer == 0u) {
            bound_city.controller_pointer = first.controller_pointer;
            bound_city.city_pointer = first.city_pointer;
            bound_city.corps_pointer = first.corps_pointer;
        } else if (first.controller_pointer != bound_city.controller_pointer
            || first.city_pointer != bound_city.city_pointer
            || first.corps_pointer != bound_city.corps_pointer) {
            result = SAN9_P1_M2B_BATCH_REBIND_REQUIRED;
            goto cleanup;
        }
        if (executed != 0u) {
            result = s8_reset_transaction_after_ack(shared);
            if (result != SAN9_P1_M2B_OK) {
                goto cleanup;
            }
        }
        result = s8_publish_request(shared, config, binding_digest,
            &first, commands[step], executed == 0u);
        if (result != SAN9_P1_M2B_OK) {
            goto cleanup;
        }
        memset(&step_evidence, 0, sizeof(step_evidence));
        result = run_s5_apply_once_session(shared, config, &step_evidence);
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
        if (result != SAN9_P1_M2B_OK) {
            goto cleanup;
        }
        memcpy(output, &step_evidence, sizeof(*output));
        ++executed;
    }
    (void)printf("{\"mode\":\"%s\"," 
        "\"phase\":\"BATCH_COMPLETE\",\"executed\":%lu,"
        "\"skipped\":%lu,\"rebind_required\":false}\n",
        mode_name, (unsigned long)executed, (unsigned long)skipped);
    (void)fflush(stdout);
    result = SAN9_P1_M2B_OK;
cleanup:
    if (result == SAN9_P1_M2B_BATCH_REBIND_REQUIRED) {
        (void)printf("{\"mode\":\"%s\"," 
            "\"phase\":\"BATCH_REBIND_REQUIRED\",\"executed\":%lu,"
            "\"skipped\":%lu}\n",
            mode_name, (unsigned long)executed, (unsigned long)skipped);
        (void)fflush(stdout);
    }
    if (process != NULL) {
        (void)CloseHandle(process);
    }
    san9_p1_secure_zero(&binding, sizeof(binding));
    san9_p1_secure_zero(&first, sizeof(first));
    san9_p1_secure_zero(&second, sizeof(second));
    san9_p1_secure_zero(&step_evidence, sizeof(step_evidence));
    san9_p1_secure_zero(&bound_city, sizeof(bound_city));
    san9_p1_secure_zero(binding_digest, sizeof(binding_digest));
    san9_p1_secure_zero(menu_signal, sizeof(menu_signal));
    return result;
}

static int prepare_config(
    const San9P1M2bDiscovery *discovery,
    uint32_t operation_mode,
    San9P1M2bBootstrapConfig *config)
{
    static const char manifest_sha[] =
        "72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE";
    wchar_t controller_path[260];
    wchar_t bridge_path[260];
    uint8_t controller_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t bridge_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t profile_material[SAN9_P1_DIGEST_SIZE * 3u];
    struct ContextMaterial {
        uint32_t game_pid;
        uint32_t main_tid;
        uint32_t hwnd;
        uint32_t helper_pid;
        uint32_t loader_pid;
        uint32_t reserved;
        uint64_t game_generation;
        uint64_t helper_generation;
        uint64_t loader_generation;
        San9P1EasyBinding easy_binding;
    } context;
    struct TicketMaterial {
        San9P1EasyBinding easy_binding;
        uint64_t game_generation;
        uint64_t loader_generation;
        uint8_t snapshot_digest[SAN9_P1_DIGEST_SIZE];
    } ticket;
    San9P1Frame frame;
    ULONGLONG now;
    if (discovery == NULL || config == NULL
        || (operation_mode != SAN9_P1_M2B_OPERATION_PROBE0
            && operation_mode != SAN9_P1_M2B_OPERATION_PING
            && operation_mode != SAN9_P1_M2B_OPERATION_OBSERVE
            && operation_mode != SAN9_P1_M2B_OPERATION_S5_NO_APPLY
            && operation_mode != SAN9_P1_M2B_OPERATION_S5_APPLY_ONCE
            && operation_mode
                != SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE
            && operation_mode
                != SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE
            && operation_mode
                != SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE
            && operation_mode
                != SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE
            && operation_mode != SAN9_P1_M2B_OPERATION_S8_BASIC_BATCH
            && operation_mode != SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH
            && operation_mode != SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE)
        || discovery->status != SAN9_P1_M2B_DISCOVERY_OK
        || !controller_and_bridge_paths(
            controller_path, bridge_path, operation_mode)
        || !san9_p1_m2b_hash_file(controller_path, controller_digest)
        || !san9_p1_m2b_hash_file(bridge_path, bridge_digest)) {
        return 0;
    }
    memset(config, 0, sizeof(*config));
    memset(&frame, 0, sizeof(frame));
    memset(&context, 0, sizeof(context));
    memset(&ticket, 0, sizeof(ticket));
    config->magic = SAN9_P1_M2B_CONFIG_MAGIC;
    config->schema_major = SAN9_P1_M2B_SCHEMA_MAJOR;
    config->schema_minor = SAN9_P1_M2B_SCHEMA_MINOR;
    config->structure_size = sizeof(*config);
    config->target_pid = discovery->game_pid;
    config->target_thread_id = discovery->main_thread_id;
    config->target_hwnd = discovery->game_hwnd;
    config->timeout_ms = 5000u;
    config->operation_mode = operation_mode;
    config->easy_binding = discovery->easy_binding;
    (void)wcsncpy(config->dll_path, bridge_path,
        sizeof(config->dll_path) / sizeof(config->dll_path[0]) - 1u);
    if (!random_bytes(&config->owner_token, sizeof(config->owner_token))
        || config->owner_token == 0u
        || !random_bytes(config->hmac_key, sizeof(config->hmac_key))) {
        goto fail;
    }

    now = GetTickCount64();
    frame.kind = SAN9_P1_PING_REQUEST;
    frame.state = SAN9_P1_PENDING;
    frame.sequence = 1u;
    frame.issued_at_ms = now;
    frame.expires_at_ms = now + config->timeout_ms;
    frame.game_pid = discovery->game_pid;
    frame.main_tid = discovery->main_thread_id;
    frame.game_hwnd = discovery->game_hwnd;
    frame.helper_pid = discovery->helper_pid;
    frame.easy_loader_pid = discovery->easy_loader_pid;
    frame.game_generation = discovery->game_generation;
    frame.helper_generation = discovery->helper_generation;
    frame.easy_loader_generation = discovery->easy_loader_generation;
    if (!random_bytes(frame.session_nonce, sizeof(frame.session_nonce))
        || !random_bytes(frame.request_id, sizeof(frame.request_id))
        || !random_bytes(frame.easy_epoch_nonce, sizeof(frame.easy_epoch_nonce))
        || !random_bytes(frame.mapping_digest, sizeof(frame.mapping_digest))
        || !random_bytes(frame.challenge_digest, sizeof(frame.challenge_digest))) {
        goto fail;
    }
    memcpy(profile_material, discovery->game_file_digest,
        SAN9_P1_DIGEST_SIZE);
    memcpy(profile_material + SAN9_P1_DIGEST_SIZE,
        discovery->easy_loader_file_digest, SAN9_P1_DIGEST_SIZE);
    memcpy(profile_material + SAN9_P1_DIGEST_SIZE * 2u,
        discovery->easy_module_file_digest, SAN9_P1_DIGEST_SIZE);
    digest_two(profile_material, sizeof(profile_material),
        "San9PK101+Easy1105", sizeof("San9PK101+Easy1105") - 1u,
        frame.profile_digest);
    digest_two(controller_digest, sizeof(controller_digest),
        bridge_digest, sizeof(bridge_digest), frame.build_digest);
    memcpy(frame.bridge_digest, bridge_digest, sizeof(frame.bridge_digest));
    if (!digest_from_hex(manifest_sha, frame.manifest_digest)) {
        goto fail;
    }
    memcpy(frame.easy_epoch_digest, discovery->easy_snapshot_digest,
        sizeof(frame.easy_epoch_digest));
    context.game_pid = discovery->game_pid;
    context.main_tid = discovery->main_thread_id;
    context.hwnd = discovery->game_hwnd;
    context.helper_pid = discovery->helper_pid;
    context.loader_pid = discovery->easy_loader_pid;
    context.game_generation = discovery->game_generation;
    context.helper_generation = discovery->helper_generation;
    context.loader_generation = discovery->easy_loader_generation;
    context.easy_binding = discovery->easy_binding;
    digest_two(&context, sizeof(context), frame.profile_digest,
        sizeof(frame.profile_digest), frame.context_digest);
    ticket.easy_binding = discovery->easy_binding;
    ticket.game_generation = discovery->game_generation;
    ticket.loader_generation = discovery->easy_loader_generation;
    memcpy(ticket.snapshot_digest, discovery->easy_snapshot_digest,
        sizeof(ticket.snapshot_digest));
    digest_two(&ticket, sizeof(ticket), frame.manifest_digest,
        sizeof(frame.manifest_digest), frame.easy_ticket_digest);
    if (!san9_p1_encode(&frame, config->hmac_key, sizeof(config->hmac_key),
            config->binding_frame, sizeof(config->binding_frame))) {
        goto fail;
    }
    san9_p1_secure_zero(&frame, sizeof(frame));
    san9_p1_secure_zero(&context, sizeof(context));
    san9_p1_secure_zero(&ticket, sizeof(ticket));
    san9_p1_secure_zero(profile_material, sizeof(profile_material));
    san9_p1_secure_zero(controller_digest, sizeof(controller_digest));
    san9_p1_secure_zero(bridge_digest, sizeof(bridge_digest));
    return 1;
fail:
    san9_p1_secure_zero(&frame, sizeof(frame));
    san9_p1_secure_zero(&context, sizeof(context));
    san9_p1_secure_zero(&ticket, sizeof(ticket));
    san9_p1_secure_zero(profile_material, sizeof(profile_material));
    san9_p1_secure_zero(controller_digest, sizeof(controller_digest));
    san9_p1_secure_zero(bridge_digest, sizeof(bridge_digest));
    san9_p1_secure_zero(config, sizeof(*config));
    return 0;
}

static void print_digest(const uint8_t digest[SAN9_P1_DIGEST_SIZE])
{
    static const char hex[] = "0123456789ABCDEF";
    size_t index;
    for (index = 0u; index < SAN9_P1_DIGEST_SIZE; ++index) {
        (void)putchar(hex[digest[index] >> 4u]);
        (void)putchar(hex[digest[index] & 15u]);
    }
}

static void print_inspect_json(const San9P1M2bDiscovery *discovery)
{
    int ready = discovery != NULL
        && discovery->status == SAN9_P1_M2B_DISCOVERY_OK;
    const char *status = discovery == NULL ? "INVALID_ARGUMENT"
        : san9_p1_m2b_discovery_status_name(discovery->status);
    (void)printf("{\"mode\":\"inspect\",\"ready\":%s,\"status\":\"%s\"",
        ready ? "true" : "false", status);
    if (discovery != NULL) {
        (void)printf(",\"game_pid\":%u,\"main_tid\":%u,\"game_hwnd\":%u,"
            "\"loader_pid\":%u,\"game_generation\":%llu,"
            "\"loader_generation\":%llu,\"easy_base\":%u,"
            "\"easy_state\":%u,\"stable_snapshot\":%u,"
            "\"installed_redirects\":%u,\"original_redirects\":%u,"
            "\"unknown_redirects\":%u,\"detail_flags\":%u,"
            "\"first_failure_point\":%u,\"page_base\":%u,"
            "\"page_size\":%u,\"page_state\":%u,\"page_protection\":%u,"
            "\"page_allocation_base\":%u,"
            "\"page_allocation_protection\":%u,\"page_type\":%u,"
            "\"live_authorized\":false,\"snapshot_sha256\":\"",
            discovery->game_pid, discovery->main_thread_id,
            discovery->game_hwnd, discovery->easy_loader_pid,
            (unsigned long long)discovery->game_generation,
            (unsigned long long)discovery->easy_loader_generation,
            discovery->easy_binding.easy_image_base,
            (unsigned int)discovery->easy_report.state,
            discovery->easy_report.stable_snapshot,
            discovery->easy_report.installed_redirect_count,
            discovery->easy_report.original_redirect_count,
            discovery->easy_report.unknown_redirect_count,
            discovery->easy_report.detail_flags,
            discovery->easy_report.first_failure_point,
            discovery->easy_page.base_address,
            discovery->easy_page.region_size,
            discovery->easy_page.state,
            discovery->easy_page.protection,
            discovery->easy_page_allocation_base,
            discovery->easy_page_allocation_protection,
            discovery->easy_page_type);
        print_digest(discovery->easy_snapshot_digest);
        (void)putchar('"');
    }
    (void)puts("}");
}

static void print_probe0_json(
    uint32_t result,
    const San9P1M2bDiscovery *discovery,
    const San9P1M2bProbe0Evidence *evidence)
{
    (void)printf("{\"mode\":\"probe0\",\"ok\":%s,\"result\":%u,"
        "\"game_pid\":%u,\"main_tid\":%u,\"start\":%u,\"end\":%u,"
        "\"original_returns\":%u,\"outer\":%u,\"nested\":%u,"
        "\"identity_rejects\":%u,\"caller\":%u,\"app\":%u,"
        "\"business_apply\":0,\"restart_required\":%s}\n",
        result == SAN9_P1_M2B_OK ? "true" : "false", result,
        discovery != NULL ? discovery->game_pid : 0u,
        discovery != NULL ? discovery->main_thread_id : 0u,
        evidence != NULL ? evidence->start_idle_count : 0u,
        evidence != NULL ? evidence->end_idle_count : 0u,
        evidence != NULL ? evidence->original_return_count : 0u,
        evidence != NULL ? evidence->outer_count : 0u,
        evidence != NULL ? evidence->nested_count : 0u,
        evidence != NULL ? evidence->identity_reject_count : 0u,
        evidence != NULL ? evidence->last_caller : 0u,
        evidence != NULL ? evidence->last_app : 0u,
        result == SAN9_P1_M2B_OK ? "false" : "true");
}

static void print_ping_json(
    uint32_t result,
    const San9P1M2bDiscovery *discovery,
    const San9P1M2bProbe0Evidence *probe,
    const San9P1M2bPingEvidence *ping)
{
    (void)printf("{\"mode\":\"ping\",\"ok\":%s,\"result\":%u,"
        "\"game_pid\":%u,\"main_tid\":%u,\"requested\":%u,"
        "\"completed\":%u,\"verified\":%u,\"accepted\":%u,"
        "\"ack\":%u,\"first_sequence\":%llu,\"last_sequence\":%llu,"
        "\"stable_digest_count\":%u,\"caller\":%u,"
        "\"original_returns\":%u,\"outer\":%u,\"nested\":%u,"
        "\"identity_rejects\":%u,\"business_apply\":0,"
        "\"restart_required\":%s,\"easy_snapshot_sha256\":\"",
        result == SAN9_P1_M2B_OK ? "true" : "false", result,
        discovery != NULL ? discovery->game_pid : 0u,
        discovery != NULL ? discovery->main_thread_id : 0u,
        ping != NULL ? ping->requested_count : 0u,
        ping != NULL ? ping->completed_count : 0u,
        ping != NULL ? ping->verified_count : 0u,
        ping != NULL ? ping->accepted_count : 0u,
        ping != NULL ? ping->controller_ack : 0u,
        (unsigned long long)(ping != NULL ? ping->first_sequence : 0u),
        (unsigned long long)(ping != NULL ? ping->last_sequence : 0u),
        ping != NULL ? ping->stable_digest_count : 0u,
        ping != NULL ? ping->last_caller : 0u,
        probe != NULL ? probe->original_return_count : 0u,
        probe != NULL ? probe->outer_count : 0u,
        probe != NULL ? probe->nested_count : 0u,
        probe != NULL ? probe->identity_reject_count : 0u,
        result == SAN9_P1_M2B_OK ? "false" : "true");
    if (ping != NULL) {
        print_digest(ping->easy_snapshot_digest);
    }
    (void)puts("\"}");
}

static void print_observe_json(
    uint32_t result,
    const San9P1M2bDiscovery *discovery,
    const San9P1M2bProbe0Evidence *probe,
    const San9P1M2bObserveEvidence *observe)
{
    (void)printf("{\"mode\":\"observe\",\"state\":\"complete\","
        "\"ok\":%s,\"result\":%u,\"game_pid\":%u,\"main_tid\":%u,"
        "\"records\":%u,\"dropped\":%u,\"capture_failures\":%u,"
        "\"flags\":%u,\"readonly_dispatch\":%u,\"write_dispatch\":%u,"
        "\"elapsed_ms\":%u,\"original_returns\":%u,\"outer_ticks\":%u,"
        "\"nested\":%u,\"identity_rejects\":%u,\"business_apply\":0,"
        "\"restart_required\":%s}\n",
        result == SAN9_P1_M2B_OK ? "true" : "false", result,
        discovery != NULL ? discovery->game_pid : 0u,
        discovery != NULL ? discovery->main_thread_id : 0u,
        observe != NULL ? observe->records : 0u,
        observe != NULL ? observe->dropped_records : 0u,
        observe != NULL ? observe->capture_failures : 0u,
        observe != NULL ? observe->cumulative_flags : 0u,
        observe != NULL ? observe->readonly_dispatch_count : 0u,
        observe != NULL ? observe->write_dispatch_count : 0u,
        observe != NULL ? observe->elapsed_ms : 0u,
        probe != NULL ? probe->original_return_count : 0u,
        probe != NULL ? probe->outer_count : 0u,
        probe != NULL ? probe->nested_count : 0u,
        probe != NULL ? probe->identity_reject_count : 0u,
        observe != NULL && observe->restart_required != 0u ? "true" : "false");
}

static void print_s5_json(
    const char *mode_name,
    uint32_t result,
    const San9P1M2bDiscovery *discovery,
    const San9P1M2bProbe0Evidence *probe,
    const San9P1S5NoApplyEvidence *evidence)
{
    (void)printf("{\"mode\":\"%s\",\"result\":%u," 
        "\"game_pid\":%u,\"state\":%u,\"restart_required\":%u,"
        "\"event\":%u,\"shadow\":%u,\"execute\":%u,"
        "\"restore\":%u,\"menu_wake\":%u,\"menu_restore\":%u,"
        "\"apply\":%u,\"controller_ack\":%u,"
        "\"probe_start\":%u,\"probe_end\":%u,"
        "\"probe_original\":%u,\"probe_outer\":%u,"
        "\"probe_nested\":%u,\"probe_rejects\":%u}\n",
        mode_name, result, discovery != NULL ? discovery->game_pid : 0u,
        evidence != NULL ? evidence->machine_state : 0u,
        evidence != NULL ? evidence->restart_required : 0u,
        evidence != NULL ? evidence->event_attempt_count : 0u,
        evidence != NULL ? evidence->shadow_arm_count : 0u,
        evidence != NULL ? evidence->execute_enter_count : 0u,
        evidence != NULL ? evidence->restore_count : 0u,
        evidence != NULL ? evidence->menu_wake_count : 0u,
        evidence != NULL ? evidence->menu_restore_count : 0u,
        evidence != NULL ? evidence->observed_apply_count : 0u,
        evidence != NULL ? evidence->controller_ack : 0u,
        probe != NULL ? probe->start_idle_count : 0u,
        probe != NULL ? probe->end_idle_count : 0u,
        probe != NULL ? probe->original_return_count : 0u,
        probe != NULL ? probe->outer_count : 0u,
        probe != NULL ? probe->nested_count : 0u,
        probe != NULL ? probe->identity_reject_count : 0u);
    (void)fflush(stdout);
}

static void print_s5_modal_json(
    uint32_t result,
    const San9P1M2bDiscovery *discovery,
    const San9P1M2bProbe0Evidence *probe,
    const San9P1S5ModalProbeEvidence *evidence)
{
    (void)printf("{\"mode\":\"s5-modal-probe\",\"result\":%u,"
        "\"game_pid\":%u,\"state\":%u,\"wake_post\":%u,"
        "\"hook_entry\":%u,\"exact_wake\":%u,\"claim\":%u,"
        "\"duplicate\":%u,\"hook_depth\":%u,\"callnext_stable\":%u,"
        "\"easy_stable\":%u,\"shadow_arm\":%u,\"root_event\":0,"
        "\"s5_publish\":0,\"apply\":0,\"controller_ack\":%u,"
        "\"probe_original\":%u,\"probe_outer\":%u}\n",
        result, discovery != NULL ? discovery->game_pid : 0u,
        evidence != NULL ? evidence->state : 0u,
        evidence != NULL ? evidence->wake_post_count : 0u,
        evidence != NULL ? evidence->hook_entry_count : 0u,
        evidence != NULL ? evidence->exact_wake_count : 0u,
        evidence != NULL ? evidence->wake_claim_count : 0u,
        evidence != NULL ? evidence->duplicate_count : 0u,
        evidence != NULL ? evidence->hook_depth : 0u,
        evidence != NULL ? evidence->callnext_stable : 0u,
        evidence != NULL ? evidence->easy_stable_count : 0u,
        evidence != NULL ? evidence->shadow_arm_count : 0u,
        evidence != NULL ? evidence->controller_ack : 0u,
        probe != NULL ? probe->original_return_count : 0u,
        probe != NULL ? probe->outer_count : 0u);
    (void)fflush(stdout);
}

int main(int argc, char **argv)
{
    San9P1M2bDiscovery discovery;
    San9P1M2bBootstrapConfig config;
    San9P1M2bProbe0Evidence evidence;
    San9P1M2bPingEvidence ping_evidence;
    San9P1M2bObserveEvidence observe_evidence;
    San9P1S5NoApplyEvidence s5_evidence;
    San9P1S5ModalProbeEvidence modal_evidence;
    San9P1M2bDiscoveryStatus discovery_status;
    uint32_t operation_mode = SAN9_P1_M2B_OPERATION_NONE;
    uint32_t result;
    memset(&discovery, 0, sizeof(discovery));
    memset(&config, 0, sizeof(config));
    memset(&evidence, 0, sizeof(evidence));
    memset(&ping_evidence, 0, sizeof(ping_evidence));
    memset(&observe_evidence, 0, sizeof(observe_evidence));
    memset(&s5_evidence, 0, sizeof(s5_evidence));
    memset(&modal_evidence, 0, sizeof(modal_evidence));
    if (argc == 2 && strcmp(argv[1], "--inspect") == 0) {
        discovery_status = san9_p1_m2b_discover(&discovery);
        print_inspect_json(&discovery);
        return discovery_status == SAN9_P1_M2B_DISCOVERY_OK ? 0 : 2;
    }
    if (argc == 4 && strcmp(argv[1], "--probe0") == 0
        && strcmp(argv[2], "--confirm") == 0
        && strcmp(argv[3], SAN9_P1_M2B_CONFIRM_WORD) == 0) {
        operation_mode = SAN9_P1_M2B_OPERATION_PROBE0;
    } else if (argc == 4 && strcmp(argv[1], "--ping") == 0
        && strcmp(argv[2], "--confirm") == 0
        && strcmp(argv[3], SAN9_P1_M2B_PING_CONFIRM_WORD) == 0) {
        operation_mode = SAN9_P1_M2B_OPERATION_PING;
    } else if (argc == 4 && strcmp(argv[1], "--observe") == 0
        && strcmp(argv[2], "--confirm") == 0
        && strcmp(argv[3], SAN9_P1_M2B_OBSERVE_CONFIRM_WORD) == 0) {
        operation_mode = SAN9_P1_M2B_OPERATION_OBSERVE;
    } else if (argc == 4 && strcmp(argv[1], "--s5-no-apply") == 0
        && strcmp(argv[2], "--confirm") == 0
        && strcmp(argv[3], SAN9_P1_M2B_S5_NO_APPLY_CONFIRM_WORD) == 0) {
        operation_mode = SAN9_P1_M2B_OPERATION_S5_NO_APPLY;
    } else if (argc == 4 && strcmp(argv[1], "--s5-modal-probe") == 0
        && strcmp(argv[2], "--confirm") == 0
        && strcmp(argv[3], SAN9_P1_M2B_S5_MODAL_PROBE_CONFIRM_WORD) == 0) {
        operation_mode = SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE;
    } else if (argc == 4 && strcmp(argv[1], "--s5-apply-once") == 0
        && strcmp(argv[2], "--confirm") == 0
        && strcmp(argv[3], SAN9_P1_M2B_S5_APPLY_ONCE_CONFIRM_WORD) == 0) {
        operation_mode = SAN9_P1_M2B_OPERATION_S5_APPLY_ONCE;
    } else if (argc == 4
        && strcmp(argv[1], "--s6-cultivate-apply-once") == 0
        && strcmp(argv[2], "--confirm") == 0
        && strcmp(argv[3],
            SAN9_P1_M2B_S6_CULTIVATE_APPLY_ONCE_CONFIRM_WORD) == 0) {
        operation_mode = SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE;
    } else if (argc == 4
        && strcmp(argv[1], "--s6-patrol-apply-once") == 0
        && strcmp(argv[2], "--confirm") == 0
        && strcmp(argv[3],
            SAN9_P1_M2B_S6_PATROL_APPLY_ONCE_CONFIRM_WORD) == 0) {
        operation_mode = SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE;
    } else if (argc == 4
        && strcmp(argv[1], "--s6-train-apply-once") == 0
        && strcmp(argv[2], "--confirm") == 0
        && strcmp(argv[3],
            SAN9_P1_M2B_S6_TRAIN_APPLY_ONCE_CONFIRM_WORD) == 0) {
        operation_mode = SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE;
    } else if (argc == 4
        && strcmp(argv[1], "--s6-repair-apply-once") == 0
        && strcmp(argv[2], "--confirm") == 0
        && strcmp(argv[3],
            SAN9_P1_M2B_S6_REPAIR_APPLY_ONCE_CONFIRM_WORD) == 0) {
        operation_mode = SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE;
    } else if (argc == 4 && strcmp(argv[1], "--s8-basic-batch") == 0
        && strcmp(argv[2], "--confirm") == 0
        && strcmp(argv[3], SAN9_P1_M2B_S8_BASIC_BATCH_CONFIRM_WORD) == 0) {
        operation_mode = SAN9_P1_M2B_OPERATION_S8_BASIC_BATCH;
    } else if (argc == 4 && strcmp(argv[1], "--s8-wealthy-batch") == 0
        && strcmp(argv[2], "--confirm") == 0
        && strcmp(argv[3], SAN9_P1_M2B_S8_WEALTHY_BATCH_CONFIRM_WORD) == 0) {
        operation_mode = SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH;
    } else {
        fputs("usage: controller.exe --inspect | --probe0 --confirm "
            SAN9_P1_M2B_CONFIRM_WORD " | --ping --confirm "
            SAN9_P1_M2B_PING_CONFIRM_WORD " | --observe --confirm "
            SAN9_P1_M2B_OBSERVE_CONFIRM_WORD " | --s5-no-apply --confirm "
            SAN9_P1_M2B_S5_NO_APPLY_CONFIRM_WORD
            " | --s5-modal-probe --confirm "
            SAN9_P1_M2B_S5_MODAL_PROBE_CONFIRM_WORD
            " | --s5-apply-once --confirm "
            SAN9_P1_M2B_S5_APPLY_ONCE_CONFIRM_WORD
            " | --s6-cultivate-apply-once --confirm "
            SAN9_P1_M2B_S6_CULTIVATE_APPLY_ONCE_CONFIRM_WORD
            " | --s6-patrol-apply-once --confirm "
            SAN9_P1_M2B_S6_PATROL_APPLY_ONCE_CONFIRM_WORD
            " | --s6-train-apply-once --confirm "
            SAN9_P1_M2B_S6_TRAIN_APPLY_ONCE_CONFIRM_WORD
            " | --s6-repair-apply-once --confirm "
            SAN9_P1_M2B_S6_REPAIR_APPLY_ONCE_CONFIRM_WORD
            " | --s8-basic-batch --confirm "
            SAN9_P1_M2B_S8_BASIC_BATCH_CONFIRM_WORD
            " | --s8-wealthy-batch --confirm "
            SAN9_P1_M2B_S8_WEALTHY_BATCH_CONFIRM_WORD "\n", stderr);
        return 64;
    }
    discovery_status = san9_p1_m2b_discover(&discovery);
    if (discovery_status != SAN9_P1_M2B_DISCOVERY_OK) {
        print_inspect_json(&discovery);
        return 2;
    }
    if (!prepare_config(&discovery, operation_mode, &config)) {
        print_probe0_json(SAN9_P1_M2B_AUTH_REJECTED, &discovery, &evidence);
        return 3;
    }
    if (operation_mode == SAN9_P1_M2B_OPERATION_PING) {
        result = san9_p1_m2b_controller_ping(&config, &evidence,
            &ping_evidence);
        print_ping_json(result, &discovery, &evidence, &ping_evidence);
    } else if (operation_mode == SAN9_P1_M2B_OPERATION_OBSERVE) {
        result = san9_p1_m2b_controller_observe(&config, &evidence,
            &observe_evidence);
        print_observe_json(result, &discovery, &evidence, &observe_evidence);
    } else if (operation_mode == SAN9_P1_M2B_OPERATION_S5_NO_APPLY) {
        result = san9_p1_m2b_controller_s5_no_apply(
            &config, &evidence, &s5_evidence);
        print_s5_json("s5-no-apply", result, &discovery,
            &evidence, &s5_evidence);
    } else if (operation_mode == SAN9_P1_M2B_OPERATION_S5_APPLY_ONCE) {
        result = san9_p1_m2b_controller_s5_apply_once(
            &config, &evidence, &s5_evidence);
        print_s5_json("s5-apply-once", result, &discovery,
            &evidence, &s5_evidence);
    } else if (operation_mode
            == SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE) {
        result = san9_p1_m2b_controller_s6_cultivate_apply_once(
            &config, &evidence, &s5_evidence);
        print_s5_json("s6-cultivate-apply-once", result, &discovery,
            &evidence, &s5_evidence);
    } else if (operation_mode
            == SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE) {
        result = san9_p1_m2b_controller_s6_patrol_apply_once(
            &config, &evidence, &s5_evidence);
        print_s5_json("s6-patrol-apply-once", result, &discovery,
            &evidence, &s5_evidence);
    } else if (operation_mode
            == SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE) {
        result = san9_p1_m2b_controller_s6_train_apply_once(
            &config, &evidence, &s5_evidence);
        print_s5_json("s6-train-apply-once", result, &discovery,
            &evidence, &s5_evidence);
    } else if (operation_mode
            == SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE) {
        result = san9_p1_m2b_controller_s6_repair_apply_once(
            &config, &evidence, &s5_evidence);
        print_s5_json("s6-repair-apply-once", result, &discovery,
            &evidence, &s5_evidence);
    } else if (operation_mode == SAN9_P1_M2B_OPERATION_S8_BASIC_BATCH) {
        result = san9_p1_m2b_controller_s8_basic_batch(
            &config, &evidence, &s5_evidence);
        print_s5_json("s8-basic-batch", result, &discovery,
            &evidence, &s5_evidence);
    } else if (operation_mode == SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH) {
        result = san9_p1_m2b_controller_s8_wealthy_batch(
            &config, &evidence, &s5_evidence);
        print_s5_json("s8-wealthy-batch", result, &discovery,
            &evidence, &s5_evidence);
    } else if (operation_mode == SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE) {
        result = san9_p1_m2b_controller_s5_modal_probe(
            &config, &evidence, &modal_evidence);
        print_s5_modal_json(result, &discovery, &evidence, &modal_evidence);
    } else {
        result = san9_p1_m2b_controller_probe0(&config, &evidence);
        print_probe0_json(result, &discovery, &evidence);
    }
    san9_p1_secure_zero(&config, sizeof(config));
    san9_p1_secure_zero(&evidence, sizeof(evidence));
    san9_p1_secure_zero(&ping_evidence, sizeof(ping_evidence));
    san9_p1_secure_zero(&observe_evidence, sizeof(observe_evidence));
    san9_p1_secure_zero(&s5_evidence, sizeof(s5_evidence));
    san9_p1_secure_zero(&modal_evidence, sizeof(modal_evidence));
    return result == SAN9_P1_M2B_OK ? 0 : (int)result;
}
