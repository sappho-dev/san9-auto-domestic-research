#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "san9_bridge_v51.h"

#include <string.h>

typedef struct OfflineIdleRuntime {
    San9OriginalIdle original_idle;
    San9PingSession *session;
    San9PingMailbox *mailbox;
    const San9IdleGateSnapshot *gate;
    uint64_t now_ms;
    uint32_t initialized;
    uint32_t authenticated_pings;
    uint32_t rejected_pings;
} OfflineIdleRuntime;

typedef struct SyntheticIdleContext {
    OfflineIdleRuntime *runtime;
    uint32_t original_calls;
    int nested_return;
} SyntheticIdleContext;

static HMODULE g_module;
static _Thread_local unsigned g_idle_depth;

static int offline_idle_core(
    OfflineIdleRuntime *runtime,
    void *app,
    int flag)
{
    int original_result;
    int ping_result;
    unsigned depth;

    if (runtime == NULL || runtime->initialized != 1U
        || runtime->original_idle == NULL) {
        return 0;
    }

    depth = ++g_idle_depth;
    original_result = runtime->original_idle(app, flag);
    if (depth == 1U && runtime->session != NULL && runtime->mailbox != NULL
        && runtime->gate != NULL && runtime->now_ms != 0
        && InterlockedCompareExchange(
            (volatile LONG *)&runtime->mailbox->request_state,
            SAN9_MAILBOX_READY,
            SAN9_MAILBOX_READY) == SAN9_MAILBOX_READY) {
        ping_result = san9_ping_process_one(
            runtime->session,
            runtime->mailbox,
            runtime->now_ms,
            runtime->gate);
        if (ping_result == SAN9_PING_OK) {
            ++runtime->authenticated_pings;
        } else {
            ++runtime->rejected_pings;
        }
    }
    --g_idle_depth;
    return original_result;
}

static int SAN9_THISCALL synthetic_original_idle(void *app, int flag)
{
    SyntheticIdleContext *context = (SyntheticIdleContext *)app;
    (void)flag;
    ++context->original_calls;
    if (context->original_calls == 1U) {
        context->nested_return = offline_idle_core(context->runtime, app, 0);
        return 0x4567;
    }
    return 0x2345;
}

static void record_check(San9BridgeSelfTestReport *report, int condition)
{
    if (condition) {
        ++report->passed;
    } else {
        ++report->failed;
    }
}

static void fill_bytes(uint8_t *output, size_t size, uint8_t seed)
{
    size_t index;
    for (index = 0; index < size; ++index) {
        output[index] = (uint8_t)(seed + (uint8_t)(index * 13U));
    }
}

static void run_idle_self_test(San9BridgeSelfTestReport *report)
{
    San9PingSession session;
    San9PingMailbox mailbox;
    San9IdleGateSnapshot gate;
    OfflineIdleRuntime runtime;
    SyntheticIdleContext context;
    uint8_t key[SAN9_PING_KEY_SIZE];
    uint8_t nonce[SAN9_PING_NONCE_SIZE];
    uint8_t target[SAN9_PING_DIGEST_SIZE];
    uint8_t binding_context[SAN9_PING_DIGEST_SIZE];
    uint8_t request_id[SAN9_PING_REQUEST_ID_SIZE];
    uint8_t challenge[SAN9_PING_DIGEST_SIZE];
    uint8_t request[SAN9_PING_FRAME_SIZE];
    uint8_t response[SAN9_PING_FRAME_SIZE];
    const uint32_t synthetic_thread = 0x11112222U;
    const uint32_t synthetic_bridge = 0x33334444U;
    int result;

    fill_bytes(key, sizeof(key), 0x10U);
    fill_bytes(nonce, sizeof(nonce), 0x20U);
    fill_bytes(target, sizeof(target), 0x30U);
    fill_bytes(binding_context, sizeof(binding_context), 0x40U);
    fill_bytes(request_id, sizeof(request_id), 0x50U);
    fill_bytes(challenge, sizeof(challenge), 0x60U);
    san9_ping_mailbox_initialize(&mailbox);

    result = san9_ping_session_initialize(
        &session,
        key,
        nonce,
        target,
        binding_context,
        synthetic_thread,
        synthetic_bridge);
    record_check(report, result == SAN9_PING_OK);

    memset(&gate, 0, sizeof(gate));
    gate.caller = SAN9_EXACT_IDLE_CALLER;
    gate.app_object = SAN9_EXACT_APP_OBJECT;
    gate.thread_id = synthetic_thread;
    gate.slot_address = SAN9_EXACT_IDLE_SLOT;
    gate.slot_value = synthetic_bridge;
    gate.app_vptr = SAN9_EXACT_APP_VTABLE;
    gate.idle_argument = 0;
    gate.exact_target_verified = 1;
    gate.conflict_free = 1;
    gate.process_generation_stable = 1;

    record_check(report, san9_ping_make_request(
        &session, 1U, 1000U, 2000U, request_id, challenge, request) == SAN9_PING_OK);
    record_check(report, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);

    memset(&runtime, 0, sizeof(runtime));
    runtime.original_idle = synthetic_original_idle;
    runtime.session = &session;
    runtime.mailbox = &mailbox;
    runtime.gate = &gate;
    runtime.now_ms = 1500U;
    runtime.initialized = 1U;
    memset(&context, 0, sizeof(context));
    context.runtime = &runtime;

    result = offline_idle_core(&runtime, &context, 0);
    record_check(report, result == 0x4567);
    record_check(report, context.nested_return == 0x2345);
    record_check(report, context.original_calls == 2U);
    record_check(report, runtime.authenticated_pings == 1U);
    record_check(report, runtime.rejected_pings == 0U);
    record_check(report, session.last_accepted_sequence == 1U);
    record_check(report, san9_ping_take_response(&mailbox, response) == SAN9_PING_OK);
    record_check(report, san9_ping_verify_response(&session, request, response) == SAN9_PING_OK);
    record_check(report, g_idle_depth == 0U);

    report->original_idle_calls = context.original_calls;
    report->authenticated_pings = runtime.authenticated_pings;
    report->rejected_pings = runtime.rejected_pings;
    san9_ping_session_clear(&session);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        g_module = instance;
    }
    return TRUE;
}

SAN9_EXPORT intptr_t SAN9_STDCALL San9Bridge_GetMsgProc(
    int code,
    uintptr_t w_param,
    intptr_t l_param)
{
    return (intptr_t)CallNextHookEx(NULL, code, (WPARAM)w_param, (LPARAM)l_param);
}

SAN9_EXPORT intptr_t SAN9_STDCALL San9Bridge_ForegroundIdleProc(
    int code,
    uintptr_t w_param,
    intptr_t l_param)
{
    return (intptr_t)CallNextHookEx(NULL, code, (WPARAM)w_param, (LPARAM)l_param);
}

SAN9_EXPORT int SAN9_FASTCALL San9Bridge_IdleBridge(
    void *app,
    void *unused_edx,
    int flag)
{
    /* There is deliberately no live runtime or setter in V5.1. Until a later,
       explicitly authorized bootstrap proves and initializes every binding,
       this export must never jump to the fixed 0x434100 address. */
    (void)app;
    (void)unused_edx;
    (void)flag;
    return 0;
}

SAN9_EXPORT uint32_t San9Bridge_Bootstrap(const void *request, uint32_t request_size)
{
    (void)request;
    (void)request_size;
    return SAN9_PING_OFFLINE_ONLY;
}

SAN9_EXPORT uint32_t San9Bridge_GetContract(San9BridgeContract *contract, uint32_t size)
{
    if (contract == NULL || size != sizeof(*contract)) {
        return SAN9_PING_INVALID_FRAME;
    }
    memset(contract, 0, sizeof(*contract));
    contract->structure_size = (uint32_t)sizeof(*contract);
    contract->contract_magic = SAN9_PING_PROOF_CONTRACT_MAGIC;
    contract->schema_major = 1;
    contract->schema_minor = 0;
    contract->ping_frame_size = SAN9_PING_FRAME_SIZE;
    contract->mailbox_size = SAN9_PING_MAILBOX_SIZE;
    contract->session_size = SAN9_PING_SESSION_SIZE;
    contract->protocol_role = SAN9_PROTOCOL_ROLE_PING_BOOTSTRAP_PROOF;
    contract->ping_only = 1;
    contract->production_business_protocol_compatible = 0;
    contract->csharp_bridge_288_compatible = 0;
    contract->unauthenticated_recovery_requires_ack = 1;
    contract->trusted_monotonic_time_source_required = 1;
    contract->clock_rollback_requires_new_session = 1;
    contract->execution_authorized = 0;
    contract->hot_unload_allowed = 0;
    contract->live_bootstrap_enabled = 0;
    contract->exact_app_object = SAN9_EXACT_APP_OBJECT;
    contract->exact_app_vtable = SAN9_EXACT_APP_VTABLE;
    contract->exact_idle_slot = SAN9_EXACT_IDLE_SLOT;
    contract->exact_original_idle = SAN9_EXACT_ORIGINAL_IDLE;
    contract->exact_idle_caller = SAN9_EXACT_IDLE_CALLER;
    contract->exact_scheduler_vtable = SAN9_EXACT_SCHEDULER_VTABLE;
    contract->exact_controller_vtable = SAN9_EXACT_CONTROLLER_VTABLE;
    return SAN9_PING_OK;
}

SAN9_EXPORT uint32_t San9Bridge_OfflineSelfTest(
    San9BridgeSelfTestReport *report,
    uint32_t size)
{
    HMODULE pinned_module = NULL;
    uint32_t result;

    if (report == NULL || size != sizeof(*report)) {
        return SAN9_PING_INVALID_FRAME;
    }
    result = (uint32_t)san9_protocol_run_self_tests(report);
    report->ping_only = 1;
    report->execution_authorized = 0;
    report->live_bootstrap_enabled = 0;

    if (GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            (LPCWSTR)(const void *)&San9Bridge_OfflineSelfTest,
            &pinned_module)) {
        report->module_pinned = 1;
        record_check(report, pinned_module == g_module);
    } else {
        report->module_pinned = 0;
        record_check(report, 0);
        result = SAN9_PING_PIN_FAILED;
    }

    run_idle_self_test(report);
    if (report->failed != 0U && result == SAN9_PING_OK) {
        result = SAN9_PING_RESPONSE_INVALID;
    }
    return result;
}
