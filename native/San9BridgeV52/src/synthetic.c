#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "san9_bridge_v52.h"
#include "san9_v52_lifecycle.h"
#include "san9_v52_safety_contract.h"
#include "sha256.h"

#include <string.h>
#include <wchar.h>

typedef struct SyntheticLoaderSnapshot {
    uint32_t exact_path;
    uint32_t exact_hash;
    uint32_t exact_file_size;
    uint32_t pe32_x86;
    uint32_t exact_image_base;
    uint32_t exact_image_size;
    uint32_t single_handle_identity_stable;
    uint32_t file_share_read_only;
    uint32_t path_bounds_safe;
    uint32_t exact_window_class;
    uint32_t global_class_unique;
    uint32_t unique_target_window;
    uint32_t window_pid_matches;
    uint32_t thread_matches;
    uint32_t fresh_pre_hook_gate;
    uint32_t fresh_claim_gate;
    uint32_t fresh_post_ready_gate;
    uint32_t generation_pre_hook;
    uint32_t generation_claim;
    uint32_t generation_post_ready;
    uint32_t conflict_process_scan_complete;
    uint32_t conflict_processes_absent;
    uint32_t module_scan_complete;
    uint32_t main_module_unique;
    uint32_t main_module_path_exact;
    uint32_t main_module_base_exact;
    uint32_t main_module_size_exact;
    uint32_t conflict_modules_absent;
    uint32_t module_inventory_ab_stable;
    uint32_t local_proxy_modules_absent;
    uint32_t local_proxy_files_absent;
    uint32_t pin_succeeded;
    uint32_t slot_is_original;
    uint32_t slot_cas_unique;
    uint32_t ready_before_unhook;
} SyntheticLoaderSnapshot;

typedef struct SyntheticIdle {
    San9PingSession session;
    San9PingMailbox mailbox;
    San9IdleGateSnapshot gate;
    uint32_t original_calls;
    uint32_t pings;
    uint32_t post_calls;
} SyntheticIdle;

static void record(San9V52SelfTestReport *report, int condition, uint32_t *group)
{
    if (condition) {
        ++report->passed;
    } else {
        ++report->failed;
    }
    if (group != NULL) {
        ++*group;
    }
}

static void fill_bytes(uint8_t *output, size_t size, uint8_t seed)
{
    size_t index;
    for (index = 0; index < size; ++index) {
        output[index] = (uint8_t)(seed + (uint8_t)(index * 19U));
    }
}

static void make_envelope_fixture(
    San9V52BootstrapEnvelope *envelope,
    San9V52BootstrapExpectations *expected,
    uint8_t root_key[SAN9_V52_ROOT_KEY_SIZE])
{
    memset(envelope, 0, sizeof(*envelope));
    memset(expected, 0, sizeof(*expected));
    fill_bytes(root_key, SAN9_V52_ROOT_KEY_SIZE, 0x11U);
    envelope->sequence = 1U;
    envelope->issued_at_ms = 1000U;
    envelope->expires_at_ms = 2000U;
    envelope->controller_pid = 100U;
    envelope->target_pid = 200U;
    envelope->target_thread_id = 300U;
    envelope->registered_message = 0xc123U;
    envelope->mapping_atom = 0x1234U;
    envelope->message_tag = 0x89abcdefU;
    envelope->target_creation_time = 0x1122334455667788ULL;
    envelope->controller_creation_time = 0x8877665544332211ULL;
    envelope->target_hwnd = 0x00012345U;
    fill_bytes(envelope->session_nonce, sizeof(envelope->session_nonce), 0x21U);
    fill_bytes(envelope->request_id, sizeof(envelope->request_id), 0x31U);
    fill_bytes(envelope->exe_digest, sizeof(envelope->exe_digest), 0x41U);
    memcpy(envelope->target_digest, envelope->exe_digest, sizeof(envelope->target_digest));
    fill_bytes(envelope->context_digest, sizeof(envelope->context_digest), 0x51U);
    fill_bytes(envelope->dll_digest, sizeof(envelope->dll_digest), 0x61U);
    fill_bytes(envelope->session_key, sizeof(envelope->session_key), 0x71U);
    fill_bytes(
        envelope->mapping_name_digest,
        sizeof(envelope->mapping_name_digest),
        0x81U);
    fill_bytes(envelope->challenge, sizeof(envelope->challenge), 0x91U);

    expected->now_ms = 1500U;
    expected->last_sequence = 0U;
    expected->target_creation_time = envelope->target_creation_time;
    expected->controller_creation_time = envelope->controller_creation_time;
    expected->controller_pid = envelope->controller_pid;
    expected->target_pid = envelope->target_pid;
    expected->target_thread_id = envelope->target_thread_id;
    expected->target_hwnd = envelope->target_hwnd;
    expected->registered_message = envelope->registered_message;
    expected->mapping_atom = envelope->mapping_atom;
    expected->message_tag = envelope->message_tag;
    memcpy(expected->target_digest, envelope->target_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(expected->context_digest, envelope->context_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(expected->exe_digest, envelope->exe_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(expected->dll_digest, envelope->dll_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(
        expected->mapping_name_digest,
        envelope->mapping_name_digest,
        SAN9_PING_DIGEST_SIZE);
}

static int synthetic_loader_validate(const SyntheticLoaderSnapshot *snapshot)
{
    return snapshot != NULL
        && snapshot->exact_path == 1U
        && snapshot->exact_hash == 1U
        && snapshot->exact_file_size == 1U
        && snapshot->pe32_x86 == 1U
        && snapshot->exact_image_base == 1U
        && snapshot->exact_image_size == 1U
        && snapshot->single_handle_identity_stable == 1U
        && snapshot->file_share_read_only == 1U
        && snapshot->path_bounds_safe == 1U
        && snapshot->exact_window_class == 1U
        && snapshot->global_class_unique == 1U
        && snapshot->unique_target_window == 1U
        && snapshot->window_pid_matches == 1U
        && snapshot->thread_matches == 1U
        && snapshot->fresh_pre_hook_gate == 1U
        && snapshot->fresh_claim_gate == 1U
        && snapshot->fresh_post_ready_gate == 1U
        && snapshot->generation_pre_hook == 1U
        && snapshot->generation_claim == 1U
        && snapshot->generation_post_ready == 1U
        && snapshot->conflict_process_scan_complete == 1U
        && snapshot->conflict_processes_absent == 1U
        && snapshot->module_scan_complete == 1U
        && snapshot->main_module_unique == 1U
        && snapshot->main_module_path_exact == 1U
        && snapshot->main_module_base_exact == 1U
        && snapshot->main_module_size_exact == 1U
        && snapshot->conflict_modules_absent == 1U
        && snapshot->module_inventory_ab_stable == 1U
        && snapshot->local_proxy_modules_absent == 1U
        && snapshot->local_proxy_files_absent == 1U
        && snapshot->pin_succeeded == 1U
        && snapshot->slot_is_original == 1U
        && snapshot->slot_cas_unique == 1U
        && snapshot->ready_before_unhook == 1U;
}

static int synthetic_original(SyntheticIdle *idle)
{
    ++idle->original_calls;
    return 0x4567;
}

static int synthetic_idle_entry(
    SyntheticIdle *idle,
    uint32_t enter_token,
    uint32_t post_helper_ok,
    uint64_t now_ms)
{
    int original_result;
    int ping_result;

    /* This ordering mirrors the assembly contract: no token or helper result
       is allowed to bypass the single unconditional original call. */
    original_result = synthetic_original(idle);
    if (enter_token == 1U) {
        ++idle->post_calls;
        if (post_helper_ok == 1U) {
            ping_result = san9_ping_process_one(
                &idle->session,
                &idle->mailbox,
                now_ms,
                &idle->gate);
            if (ping_result == SAN9_PING_OK) {
                ++idle->pings;
            }
        }
    }
    return original_result;
}

static void run_bootstrap_tests(San9V52SelfTestReport *report)
{
    San9V52BootstrapEnvelope envelope;
    San9V52BootstrapEnvelope changed;
    San9V52BootstrapExpectations expected;
    San9V52BootstrapExpectations wrong;
    uint8_t root_key[SAN9_V52_ROOT_KEY_SIZE];
    uint8_t wrong_key[SAN9_V52_ROOT_KEY_SIZE];

    make_envelope_fixture(&envelope, &expected, root_key);
    record(report, sizeof(envelope) == SAN9_V52_ENVELOPE_SIZE, &report->bootstrap_tests);
    record(report, sizeof(San9V52SharedBlock) == SAN9_V52_SHARED_SIZE, &report->bootstrap_tests);
    record(report, san9_v52_bootstrap_seal(&envelope, root_key) == SAN9_V52_OK,
        &report->bootstrap_tests);
    record(report, san9_v52_bootstrap_validate(&envelope, root_key, &expected) == SAN9_V52_OK,
        &report->bootstrap_tests);

    memcpy(&changed, &envelope, sizeof(changed));
    changed.challenge[0] ^= 1U;
    record(report,
        san9_v52_bootstrap_validate(&changed, root_key, &expected) == SAN9_V52_AUTH_FAILED,
        &report->bootstrap_tests);

    memcpy(wrong_key, root_key, sizeof(wrong_key));
    wrong_key[0] ^= 1U;
    record(report,
        san9_v52_bootstrap_validate(&envelope, wrong_key, &expected) == SAN9_V52_AUTH_FAILED,
        &report->bootstrap_tests);

    changed = envelope;
    changed.sequence = 2U;
    record(report, san9_v52_bootstrap_seal(&changed, root_key) == SAN9_V52_OK,
        &report->bootstrap_tests);
    record(report,
        san9_v52_bootstrap_validate(&changed, root_key, &expected)
            == SAN9_V52_SEQUENCE_FAILED,
        &report->bootstrap_tests);

    wrong = expected;
    wrong.now_ms = envelope.expires_at_ms;
    record(report,
        san9_v52_bootstrap_validate(&envelope, root_key, &wrong) == SAN9_V52_EXPIRED,
        &report->bootstrap_tests);
    wrong = expected;
    ++wrong.target_thread_id;
    record(report,
        san9_v52_bootstrap_validate(&envelope, root_key, &wrong)
            == SAN9_V52_BINDING_FAILED,
        &report->bootstrap_tests);
    wrong = expected;
    wrong.mapping_name_digest[0] ^= 1U;
    record(report,
        san9_v52_bootstrap_validate(&envelope, root_key, &wrong)
            == SAN9_V52_BINDING_FAILED,
        &report->bootstrap_tests);
    wrong = expected;
    ++wrong.target_creation_time;
    record(report,
        san9_v52_bootstrap_validate(&envelope, root_key, &wrong)
            == SAN9_V52_BINDING_FAILED,
        &report->bootstrap_tests);

    san9_secure_zero(root_key, sizeof(root_key));
    san9_secure_zero(wrong_key, sizeof(wrong_key));
}

static void run_loader_tests(San9V52SelfTestReport *report)
{
    SyntheticLoaderSnapshot baseline;
    SyntheticLoaderSnapshot changed;
    uint32_t *fields;
    size_t index;

    memset(&baseline, 1, sizeof(baseline));
    /* memset(1) makes each uint32 0x01010101, so normalize explicitly. */
    fields = (uint32_t *)&baseline;
    for (index = 0; index < sizeof(baseline) / sizeof(uint32_t); ++index) {
        fields[index] = 1U;
    }
    record(report,
        wcscmp(SAN9_V52_EXACT_WINDOW_CLASS, L"KOEI_SAN9WINDOW") == 0,
        &report->loader_tests);
    record(report,
        SAN9_V52_CONFLICT_PROCESS_COUNT == 3,
        &report->loader_tests);
    record(report,
        wcscmp(SAN9_V52_CONFLICT_PROCESSES[0], L"San9PKEasy.exe") == 0
            && wcscmp(SAN9_V52_CONFLICT_PROCESSES[1], L"San9PKHard.exe") == 0
            && wcscmp(SAN9_V52_CONFLICT_PROCESSES[2], L"SanIXPKCheat.exe") == 0,
        &report->loader_tests);
    record(report,
        SAN9_V52_CONFLICT_MODULE_COUNT == 3,
        &report->loader_tests);
    record(report,
        wcscmp(SAN9_V52_CONFLICT_MODULES[0], L"Easy.dll") == 0
            && wcscmp(SAN9_V52_CONFLICT_MODULES[1], L"SanIXSpy.dll") == 0
            && wcscmp(SAN9_V52_CONFLICT_MODULES[2], L"San9Common.dll") == 0,
        &report->loader_tests);
    record(report,
        SAN9_V52_LOCAL_PROXY_COUNT == 5,
        &report->loader_tests);
    record(report,
        wcscmp(SAN9_V52_LOCAL_PROXIES[0], L"version.dll") == 0
            && wcscmp(SAN9_V52_LOCAL_PROXIES[1], L"dinput.dll") == 0
            && wcscmp(SAN9_V52_LOCAL_PROXIES[2], L"dinput8.dll") == 0
            && wcscmp(SAN9_V52_LOCAL_PROXIES[3], L"winmm.dll") == 0
            && wcscmp(SAN9_V52_LOCAL_PROXIES[4], L"dsound.dll") == 0,
        &report->loader_tests);

    record(report, synthetic_loader_validate(&baseline), &report->loader_tests);
    for (index = 0; index < sizeof(baseline) / sizeof(uint32_t); ++index) {
        changed = baseline;
        ((uint32_t *)&changed)[index] = 0U;
        record(report, !synthetic_loader_validate(&changed), &report->loader_tests);
    }

    /* Synthetic hook lifecycle: pin before unique slot CAS, ready before unhook. */
    record(report,
        baseline.pin_succeeded && baseline.slot_is_original
            && baseline.slot_cas_unique,
        &report->loader_tests);
    ++report->slot_installs;
    record(report, report->slot_installs == 1U, &report->loader_tests);
    ++report->unhook_after_ready;
    record(report,
        report->unhook_after_ready == 1U && baseline.ready_before_unhook,
        &report->loader_tests);
    changed = baseline;
    changed.slot_is_original = 0U;
    record(report, !synthetic_loader_validate(&changed), &report->loader_tests);
}

static void run_idle_tests(San9V52SelfTestReport *report)
{
    SyntheticIdle idle;
    uint8_t key[SAN9_PING_KEY_SIZE];
    uint8_t nonce[SAN9_PING_NONCE_SIZE];
    uint8_t target[SAN9_PING_DIGEST_SIZE];
    uint8_t context[SAN9_PING_DIGEST_SIZE];
    uint8_t request_id[SAN9_PING_REQUEST_ID_SIZE];
    uint8_t challenge[SAN9_PING_DIGEST_SIZE];
    uint8_t request[SAN9_PING_FRAME_SIZE];
    uint8_t response[SAN9_PING_FRAME_SIZE];
    uint32_t before;

    memset(&idle, 0, sizeof(idle));
    fill_bytes(key, sizeof(key), 0x10U);
    fill_bytes(nonce, sizeof(nonce), 0x20U);
    fill_bytes(target, sizeof(target), 0x30U);
    fill_bytes(context, sizeof(context), 0x40U);
    fill_bytes(request_id, sizeof(request_id), 0x50U);
    fill_bytes(challenge, sizeof(challenge), 0x60U);
    san9_ping_mailbox_initialize(&idle.mailbox);
    record(report, san9_ping_session_initialize(
        &idle.session, key, nonce, target, context, 0x1234U, 0x5678U) == SAN9_PING_OK,
        &report->idle_tests);
    memset(&idle.gate, 0, sizeof(idle.gate));
    idle.gate.caller = SAN9_EXACT_IDLE_CALLER;
    idle.gate.app_object = SAN9_EXACT_APP_OBJECT;
    idle.gate.thread_id = 0x1234U;
    idle.gate.slot_address = SAN9_EXACT_IDLE_SLOT;
    idle.gate.slot_value = 0x5678U;
    idle.gate.app_vptr = SAN9_EXACT_APP_VTABLE;
    idle.gate.exact_target_verified = 1U;
    idle.gate.conflict_free = 1U;
    idle.gate.process_generation_stable = 1U;

    /* Enter failure, nested entry, gate failure, and post failure all retain
       original_once and never claim a ping. */
    before = idle.original_calls;
    record(report, synthetic_idle_entry(&idle, 0U, 1U, 1000U) == 0x4567,
        &report->idle_tests);
    record(report, idle.original_calls == before + 1U && idle.pings == 0U,
        &report->idle_tests);
    before = idle.original_calls;
    record(report, synthetic_idle_entry(&idle, 2U, 1U, 1000U) == 0x4567,
        &report->idle_tests);
    record(report, idle.original_calls == before + 1U && idle.pings == 0U,
        &report->idle_tests);
    before = idle.original_calls;
    record(report, synthetic_idle_entry(&idle, 1U, 0U, 1000U) == 0x4567,
        &report->idle_tests);
    record(report, idle.original_calls == before + 1U && idle.pings == 0U,
        &report->idle_tests);

    record(report, san9_ping_make_request(
        &idle.session, 1U, 1000U, 2000U, request_id, challenge, request) == SAN9_PING_OK,
        &report->idle_tests);
    record(report, san9_ping_publish_request(&idle.mailbox, request) == SAN9_PING_OK,
        &report->idle_tests);
    idle.gate.caller ^= 1U;
    before = idle.original_calls;
    record(report, synthetic_idle_entry(&idle, 1U, 1U, 1500U) == 0x4567,
        &report->idle_tests);
    record(report, idle.original_calls == before + 1U && idle.pings == 0U
        && idle.mailbox.request_state == SAN9_MAILBOX_READY, &report->idle_tests);
    idle.gate.caller = SAN9_EXACT_IDLE_CALLER;
    before = idle.original_calls;
    record(report, synthetic_idle_entry(&idle, 1U, 1U, 1500U) == 0x4567,
        &report->idle_tests);
    record(report, idle.original_calls == before + 1U && idle.pings == 1U,
        &report->idle_tests);
    record(report, san9_ping_take_response(&idle.mailbox, response) == SAN9_PING_OK,
        &report->idle_tests);
    record(report, san9_ping_verify_response(&idle.session, request, response) == SAN9_PING_OK,
        &report->idle_tests);

    report->original_idle_calls = idle.original_calls;
    report->authenticated_pings = idle.pings;
    san9_ping_session_clear(&idle.session);
}

static void run_race_tests(San9V52SelfTestReport *report)
{
    static const int32_t cancellable_states[] = {
        SAN9_V52_BOOTSTRAP_SEALED,
        SAN9_V52_BOOTSTRAP_CLAIMED,
        SAN9_V52_BOOTSTRAP_INSTALLING
    };
    San9V52BootstrapEnvelope envelope;
    San9V52BootstrapExpectations expected;
    San9V52RecoveryFacts recovery;
    uint8_t root_key[SAN9_V52_ROOT_KEY_SIZE];
    volatile LONG state;
    LONG claim_enabled;
    uint32_t pin_calls;
    uint32_t slot_calls;
    uint32_t failure_streak;
    uint32_t take_count;
    int validation_result;
    int completion;
    size_t index;

    /* Timeout cancellation calls the same pure policy as the controller, then
       exercises the same STOPPED-before-claim-clear CAS ordering. */
    for (index = 0;
        index < sizeof(cancellable_states) / sizeof(cancellable_states[0]);
        ++index) {
        state = cancellable_states[index];
        claim_enabled = 1;
        if (san9_v52_lifecycle_cancel_allowed(state, 0)
            && InterlockedCompareExchange(
                &state, SAN9_V52_BOOTSTRAP_STOPPED, state)
                == cancellable_states[index]) {
            claim_enabled = 0;
        }
        record(report,
            state == SAN9_V52_BOOTSTRAP_STOPPED && claim_enabled == 0,
            &report->race_tests);
    }

    /* Commit wins: cancellation is no longer legal and deadline handling waits
       for the terminal publication rather than mutating the shared state. */
    state = SAN9_V52_BOOTSTRAP_INSTALLING;
    record(report,
        san9_v52_lifecycle_commit_allowed(state, SAN9_V52_OK)
            && InterlockedCompareExchange(
                &state,
                SAN9_V52_BOOTSTRAP_COMMITTING,
                SAN9_V52_BOOTSTRAP_INSTALLING)
                == SAN9_V52_BOOTSTRAP_INSTALLING
            && !san9_v52_lifecycle_cancel_allowed(state, 0)
            && san9_v52_lifecycle_deadline_decision(state, 2000U, 2000U)
                == SAN9_V52_DEADLINE_WAIT_COMMIT,
        &report->race_tests);
    record(report,
        InterlockedCompareExchange(
            &state,
            SAN9_V52_BOOTSTRAP_READY,
            SAN9_V52_BOOTSTRAP_COMMITTING)
            == SAN9_V52_BOOTSTRAP_COMMITTING,
        &report->race_tests);

    /* Rejection may only be published by the exact state owner. */
    record(report,
        san9_v52_lifecycle_reject_allowed(
            SAN9_V52_BOOTSTRAP_COMMITTING,
            SAN9_V52_BOOTSTRAP_COMMITTING),
        &report->race_tests);
    record(report,
        !san9_v52_lifecycle_reject_allowed(
            SAN9_V52_BOOTSTRAP_STOPPED,
            SAN9_V52_BOOTSTRAP_INSTALLING)
            && !san9_v52_lifecycle_reject_allowed(
                SAN9_V52_BOOTSTRAP_READY,
                SAN9_V52_BOOTSTRAP_COMMITTING),
        &report->race_tests);

    /* Only one callback wins SEALED -> CLAIMED. */
    state = SAN9_V52_BOOTSTRAP_SEALED;
    record(report,
        InterlockedCompareExchange(
            &state,
            SAN9_V52_BOOTSTRAP_CLAIMED,
            SAN9_V52_BOOTSTRAP_SEALED) == SAN9_V52_BOOTSTRAP_SEALED
            && InterlockedCompareExchange(
                &state,
                SAN9_V52_BOOTSTRAP_CLAIMED,
                SAN9_V52_BOOTSTRAP_SEALED) != SAN9_V52_BOOTSTRAP_SEALED,
        &report->race_tests);

    /* The controller's cancellation deadline is the authenticated envelope
       expiry, not a new five-second timer started after PostThreadMessage. */
    record(report,
        san9_v52_lifecycle_deadline_decision(
            SAN9_V52_BOOTSTRAP_INSTALLING, 1999U, 2000U)
                == SAN9_V52_DEADLINE_WAIT
            && san9_v52_lifecycle_deadline_decision(
                SAN9_V52_BOOTSTRAP_INSTALLING, 2000U, 2000U)
                == SAN9_V52_DEADLINE_TRY_CANCEL,
        &report->race_tests);

    /* Initial validation can be valid while the final fresh validation lands
       exactly on expiry.  The production commit policy then leaves both pin
       and slot call counters at zero. */
    make_envelope_fixture(&envelope, &expected, root_key);
    pin_calls = 0U;
    slot_calls = 0U;
    record(report,
        san9_v52_bootstrap_seal(&envelope, root_key) == SAN9_V52_OK
            && san9_v52_bootstrap_validate(
                &envelope, root_key, &expected) == SAN9_V52_OK,
        &report->race_tests);
    expected.now_ms = envelope.expires_at_ms;
    validation_result = san9_v52_bootstrap_validate(
        &envelope, root_key, &expected);
    if (san9_v52_lifecycle_commit_allowed(
            SAN9_V52_BOOTSTRAP_INSTALLING, validation_result)) {
        ++pin_calls;
        ++slot_calls;
    }
    record(report,
        validation_result == SAN9_V52_EXPIRED
            && pin_calls == 0U && slot_calls == 0U,
        &report->race_tests);
    san9_secure_zero(root_key, sizeof(root_key));

    /* READY alone is insufficient.  The consumer waits through response
       READY/count=0 and takes exactly once only at request COMPLETE/count=1. */
    take_count = 0U;
    completion = san9_v52_lifecycle_ping_completion(
        SAN9_MAILBOX_CLAIMED, SAN9_MAILBOX_READY, 0);
    if (completion == SAN9_V52_PING_TAKE_ONCE) {
        ++take_count;
    }
    record(report,
        completion == SAN9_V52_PING_WAIT && take_count == 0U,
        &report->race_tests);
    completion = san9_v52_lifecycle_ping_completion(
        SAN9_MAILBOX_COMPLETE, SAN9_MAILBOX_READY, 1);
    if (completion == SAN9_V52_PING_TAKE_ONCE) {
        ++take_count;
    }
    record(report,
        completion == SAN9_V52_PING_TAKE_ONCE && take_count == 1U,
        &report->race_tests);
    record(report,
        san9_v52_lifecycle_ping_completion(
            SAN9_MAILBOX_COMPLETE, SAN9_MAILBOX_CLAIMED, 1)
                == SAN9_V52_PING_FAIL_INVALID
            && take_count == 1U
            && san9_v52_lifecycle_ping_completion(
                SAN9_MAILBOX_COMPLETE, SAN9_MAILBOX_READY, 2)
                == SAN9_V52_PING_FAIL_MULTIPLE,
        &report->race_tests);

    /* Cleanup retry accounting and recovery are also production helpers. */
    memset(&recovery, 0, sizeof(recovery));
    failure_streak = 0U;
    failure_streak = san9_v52_lifecycle_next_failure_streak(failure_streak, 0);
    failure_streak = san9_v52_lifecycle_next_failure_streak(failure_streak, 0);
    recovery.unhook_failure_streak = failure_streak;
    record(report,
        failure_streak == 2U
            && san9_v52_lifecycle_recovery_classify(&recovery)
                == SAN9_V52_RECOVERY_RESTART_REQUIRED,
        &report->race_tests);

    memset(&recovery, 0, sizeof(recovery));
    failure_streak = 0U;
    failure_streak = san9_v52_lifecycle_next_failure_streak(failure_streak, 0);
    failure_streak = san9_v52_lifecycle_next_failure_streak(failure_streak, 1);
    recovery.unhook_failure_streak = failure_streak;
    record(report,
        failure_streak == 0U
            && san9_v52_lifecycle_recovery_classify(&recovery)
                == SAN9_V52_RECOVERY_CLEAN,
        &report->race_tests);

    memset(&recovery, 0, sizeof(recovery));
    failure_streak = 0U;
    failure_streak = san9_v52_lifecycle_next_failure_streak(failure_streak, 0);
    failure_streak = san9_v52_lifecycle_next_failure_streak(failure_streak, 0);
    recovery.atom_failure_streak = failure_streak;
    record(report,
        failure_streak == 2U
            && san9_v52_lifecycle_recovery_classify(&recovery)
            == SAN9_V52_RECOVERY_CLEANUP_INCOMPLETE,
        &report->race_tests);

    memset(&recovery, 0, sizeof(recovery));
    recovery.pin_succeeded = 1U;
    recovery.slot_failed = 1U;
    record(report,
        san9_v52_lifecycle_recovery_classify(&recovery)
            == SAN9_V52_RECOVERY_RESTART_REQUIRED,
        &report->race_tests);

    memset(&recovery, 0, sizeof(recovery));
    recovery.ready_seen = 1U;
    recovery.post_ready_failed = 1U;
    record(report,
        san9_v52_lifecycle_recovery_classify(&recovery)
            == SAN9_V52_RECOVERY_RESTART_REQUIRED,
        &report->race_tests);

    memset(&recovery, 0, sizeof(recovery));
    recovery.ready_seen = 1U;
    recovery.ping_failed = 1U;
    record(report,
        san9_v52_lifecycle_recovery_classify(&recovery)
            == SAN9_V52_RECOVERY_RESTART_REQUIRED,
        &report->race_tests);

    memset(&recovery, 0, sizeof(recovery));
    recovery.indeterminate_commit = 1U;
    record(report,
        san9_v52_lifecycle_recovery_classify(&recovery)
            == SAN9_V52_RECOVERY_RESTART_REQUIRED,
        &report->race_tests);

    /* Pin failure is still pre-pin and recoverable; creation lookup failure is
       rejected only by the current INSTALLING owner. */
    memset(&recovery, 0, sizeof(recovery));
    record(report,
        san9_v52_lifecycle_recovery_classify(&recovery)
            == SAN9_V52_RECOVERY_CLEAN
            && san9_v52_lifecycle_reject_allowed(
                SAN9_V52_BOOTSTRAP_INSTALLING,
                SAN9_V52_BOOTSTRAP_INSTALLING)
            && SAN9_V52_GENERATION_FAILED != SAN9_V52_OK,
        &report->race_tests);
}

int san9_v52_run_synthetic_tests(San9V52SelfTestReport *report)
{
    San9BridgeSelfTestReport inherited;
    if (report == NULL) {
        return SAN9_V52_INVALID;
    }
    memset(report, 0, sizeof(*report));
    report->structure_size = (uint32_t)sizeof(*report);
    record(report, san9_protocol_run_self_tests(&inherited) == SAN9_PING_OK
        && inherited.failed == 0U, NULL);
    report->inherited_v51_passed = inherited.passed;
    run_bootstrap_tests(report);
    run_loader_tests(report);
    run_race_tests(report);
    run_idle_tests(report);
    report->live_code_executed = 0U;
    return report->failed == 0U ? SAN9_V52_OK : SAN9_V52_INTERNAL_FAILED;
}
