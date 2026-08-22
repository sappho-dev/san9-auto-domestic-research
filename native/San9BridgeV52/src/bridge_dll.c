#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "san9_bridge_v52.h"
#include "san9_v52_lifecycle.h"
#include "live_support.h"
#include "sha256.h"

#include <string.h>
#include <wchar.h>

#if SAN9_V52_LIVE_ENABLED == 1
#include "san9_v52_build_key.h"
static const uint8_t BUILD_ROOT_KEY[SAN9_V52_ROOT_KEY_SIZE] =
    SAN9_V52_BUILD_ROOT_KEY_BYTES;
const char san9_v52_profile_marker[] =
    "SAN9_V52_PROFILE=LIVE_OPTIN;EXECUTION=PING_ONLY;STATE_MACHINE=COMMIT_V3;LIFECYCLE=SHARED";
#else
const char san9_v52_profile_marker[] = "SAN9_V52_PROFILE=OFFLINE;LIVE_ENABLED=0";
#endif

typedef struct San9V52LiveRuntime {
    volatile LONG installed;
    San9V52TargetProbe target_baseline;
    San9PingSession session;
    San9V52SharedBlock *shared;
    HANDLE mapping;
} San9V52LiveRuntime;

static HMODULE g_module;
#if SAN9_V52_LIVE_ENABLED == 1
static San9V52LiveRuntime g_runtime;
#endif
static _Thread_local uint32_t g_idle_depth;

#if SAN9_V52_LIVE_ENABLED == 1
static const uint8_t IDLE_PREFIX[6] = {0x56, 0x57, 0x8b, 0x7c, 0x24, 0x0c};
static const uint8_t MAIN_CALLSITE[14] = {
    0x85, 0xff, 0x8b, 0xce, 0x75, 0x08, 0x8b,
    0x06, 0x57, 0xff, 0x50, 0x24, 0xeb, 0xdd
};
static const uint8_t SCENE_TICK[14] = {
    0x8b, 0x89, 0x8c, 0x00, 0x00, 0x00, 0xe8,
    0x75, 0xa2, 0x04, 0x00, 0xc2, 0x04, 0x00
};

static int prefix_is_exact(const wchar_t *name)
{
    static const wchar_t prefix[] = L"Local\\San9V52-";
    size_t length;
    if (name == NULL || _wcsnicmp(name, prefix, (sizeof(prefix) / sizeof(prefix[0])) - 1U) != 0) {
        return 0;
    }
    length = wcslen(name);
    return length >= 48U && length < SAN9_V52_MAPPING_NAME_MAX;
}

static int live_memory_anchors_match(uint32_t expected_slot)
{
    volatile uint32_t *app_vptr = (volatile uint32_t *)(uintptr_t)SAN9_EXACT_APP_OBJECT;
    volatile uint32_t *slot = (volatile uint32_t *)(uintptr_t)SAN9_EXACT_IDLE_SLOT;
    return *app_vptr == SAN9_EXACT_APP_VTABLE
        && *slot == expected_slot
        && memcmp((const void *)(uintptr_t)SAN9_EXACT_ORIGINAL_IDLE,
            IDLE_PREFIX, sizeof(IDLE_PREFIX)) == 0
        && memcmp((const void *)(uintptr_t)0x005c5d05U,
            MAIN_CALLSITE, sizeof(MAIN_CALLSITE)) == 0
        && memcmp((const void *)(uintptr_t)0x004345c0U,
            SCENE_TICK, sizeof(SCENE_TICK)) == 0;
}

static uint32_t loaded_image_size(HMODULE module)
{
    const uint8_t *base = (const uint8_t *)(uintptr_t)module;
    const IMAGE_DOS_HEADER *dos;
    const IMAGE_NT_HEADERS32 *nt;
    if (base == NULL) {
        return 0U;
    }
    dos = (const IMAGE_DOS_HEADER *)(const void *)base;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE
        || dos->e_lfanew < (LONG)sizeof(*dos)
        || dos->e_lfanew > 0x1000000L) {
        return 0U;
    }
    nt = (const IMAGE_NT_HEADERS32 *)(const void *)(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE
        || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386
        || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC) {
        return 0U;
    }
    return nt->OptionalHeader.SizeOfImage;
}

static int target_probe_matches_runtime(
    const San9V52TargetProbe *probe,
    const San9V52TargetProbe *baseline)
{
    return probe->pid == baseline->pid
        && probe->thread_id == baseline->thread_id
        && probe->hwnd_value == baseline->hwnd_value
        && probe->creation_time == baseline->creation_time
        && _wcsicmp(probe->image_path, baseline->image_path) == 0
        && probe->module_count == baseline->module_count
        && probe->module_without_bridge_count
            == baseline->module_without_bridge_count
        && probe->bridge_module_count == baseline->bridge_module_count
        && probe->bridge_module_base == baseline->bridge_module_base
        && probe->bridge_module_size == baseline->bridge_module_size
        && _wcsicmp(
            probe->bridge_module_path,
            baseline->bridge_module_path) == 0
        && san9_constant_time_equal(
            probe->exe_digest,
            baseline->exe_digest,
            SAN9_PING_DIGEST_SIZE)
        && san9_constant_time_equal(
            probe->context_digest,
            baseline->context_digest,
            SAN9_PING_DIGEST_SIZE)
        && san9_constant_time_equal(
            probe->module_inventory_digest,
            baseline->module_inventory_digest,
            SAN9_PING_DIGEST_SIZE)
        && san9_constant_time_equal(
            probe->module_without_bridge_digest,
            baseline->module_without_bridge_digest,
            SAN9_PING_DIGEST_SIZE);
}

static int fill_expectations_from_envelope(
    San9V52BootstrapExpectations *expected,
    const San9V52BootstrapEnvelope *envelope,
    uint64_t now_ms,
    const uint8_t mapping_digest[SAN9_PING_DIGEST_SIZE])
{
    if (expected == NULL || envelope == NULL || mapping_digest == NULL) {
        return 0;
    }
    memset(expected, 0, sizeof(*expected));
    expected->now_ms = now_ms;
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
    memcpy(expected->mapping_name_digest, mapping_digest, SAN9_PING_DIGEST_SIZE);
    return 1;
}

static int publish_rejection_if_owner(
    San9V52SharedBlock *shared,
    int result,
    LONG expected_state)
{
    LONG observed;
    if (shared == NULL) {
        return 0;
    }
    observed = InterlockedCompareExchange(
        (volatile LONG *)&shared->bootstrap_state, 0, 0);
    if (!san9_v52_lifecycle_reject_allowed(observed, expected_state)
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->bootstrap_state,
            SAN9_V52_BOOTSTRAP_REJECTING,
            expected_state) != expected_state) {
        return 0;
    }
    InterlockedExchange(
        (volatile LONG *)&shared->bootstrap_result,
        (LONG)result);
    InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
    MemoryBarrier();
    return InterlockedCompareExchange(
        (volatile LONG *)&shared->bootstrap_state,
        SAN9_V52_BOOTSTRAP_REJECTED,
        SAN9_V52_BOOTSTRAP_REJECTING) == SAN9_V52_BOOTSTRAP_REJECTING;
}

static void reject_bootstrap(
    San9V52SharedBlock *shared,
    int result,
    LONG expected_state,
    HANDLE mapping)
{
    (void)publish_rejection_if_owner(shared, result, expected_state);
    if (shared != NULL) {
        UnmapViewOfFile(shared);
    }
    if (mapping != NULL) {
        CloseHandle(mapping);
    }
}

static void try_live_bootstrap(const MSG *message)
{
    wchar_t mapping_name[SAN9_V52_MAPPING_NAME_MAX];
    wchar_t dll_path[1024];
    UINT name_length;
    HANDLE mapping = NULL;
    San9V52SharedBlock *shared = NULL;
    San9V52BootstrapExpectations expected;
    San9V52TargetProbe target;
    San9V52TargetProbe final_target;
    San9PingSession prepared_session;
    uint8_t mapping_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t dll_digest[SAN9_PING_DIGEST_SIZE];
    uint64_t controller_creation;
    HMODULE pinned = NULL;
    LONG slot_previous;
    DWORD dll_path_length;
    uint32_t own_image_size;
    int result;

    if (message == NULL || message->message < 0xc000U || message->message > 0xffffU
        || message->wParam == 0U || message->lParam == 0
        || InterlockedCompareExchange(&g_runtime.installed, 0, 0) != 0) {
        return;
    }
    memset(&prepared_session, 0, sizeof(prepared_session));
    memset(mapping_name, 0, sizeof(mapping_name));
    name_length = GlobalGetAtomNameW(
        (ATOM)(message->wParam & 0xffffU),
        mapping_name,
        (int)(sizeof(mapping_name) / sizeof(mapping_name[0])));
    if (name_length == 0U || name_length >= sizeof(mapping_name) / sizeof(mapping_name[0])
        || !prefix_is_exact(mapping_name)
        || !san9_v52_hash_wide_string(mapping_name, mapping_digest)) {
        return;
    }
    mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, mapping_name);
    if (mapping == NULL) {
        return;
    }
    shared = (San9V52SharedBlock *)MapViewOfFile(
        mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, SAN9_V52_SHARED_SIZE);
    if (shared == NULL) {
        CloseHandle(mapping);
        return;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->bootstrap_state,
            SAN9_V52_BOOTSTRAP_CLAIMED,
            SAN9_V52_BOOTSTRAP_SEALED) != SAN9_V52_BOOTSTRAP_SEALED) {
        UnmapViewOfFile(shared);
        CloseHandle(mapping);
        return;
    }
    if (shared->claim_enabled != 1
        || !fill_expectations_from_envelope(
            &expected, &shared->envelope, GetTickCount64(), mapping_digest)) {
        reject_bootstrap(
            shared,
            SAN9_V52_INVALID,
            SAN9_V52_BOOTSTRAP_CLAIMED,
            mapping);
        return;
    }
    expected.registered_message = message->message;
    expected.mapping_atom = (uint32_t)(message->wParam & 0xffffU);
    expected.message_tag = (uint32_t)message->lParam;
    result = san9_v52_bootstrap_validate(&shared->envelope, BUILD_ROOT_KEY, &expected);
    if (result != SAN9_V52_OK) {
        reject_bootstrap(
            shared,
            result,
            SAN9_V52_BOOTSTRAP_CLAIMED,
            mapping);
        return;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->bootstrap_state,
            SAN9_V52_BOOTSTRAP_INSTALLING,
            SAN9_V52_BOOTSTRAP_CLAIMED) != SAN9_V52_BOOTSTRAP_CLAIMED) {
        UnmapViewOfFile(shared);
        CloseHandle(mapping);
        return;
    }

    result = san9_v52_probe_target(
        GetCurrentProcessId(),
        GetCurrentThreadId(),
        shared->envelope.target_hwnd,
        shared->envelope.target_creation_time,
        &target);
    if (result != SAN9_V52_OK) {
        reject_bootstrap(
            shared,
            result,
            SAN9_V52_BOOTSTRAP_INSTALLING,
            mapping);
        return;
    }
    if (!san9_v52_get_process_creation_time(
            shared->envelope.controller_pid, &controller_creation)) {
        reject_bootstrap(
            shared,
            SAN9_V52_GENERATION_FAILED,
            SAN9_V52_BOOTSTRAP_INSTALLING,
            mapping);
        return;
    }
    dll_path_length = GetModuleFileNameW(
        g_module, dll_path, (DWORD)(sizeof(dll_path) / sizeof(dll_path[0])));
    if (dll_path_length == 0U
        || dll_path_length >= sizeof(dll_path) / sizeof(dll_path[0])
        || !san9_v52_hash_file(dll_path, dll_digest)) {
        reject_bootstrap(
            shared,
            SAN9_V52_TARGET_FAILED,
            SAN9_V52_BOOTSTRAP_INSTALLING,
            mapping);
        return;
    }
    own_image_size = loaded_image_size(g_module);
    if (target.bridge_module_count != 1U
        || target.bridge_module_base != (uint32_t)(uintptr_t)g_module
        || target.bridge_module_size == 0U
        || target.bridge_module_size != own_image_size
        || _wcsicmp(target.bridge_module_path, dll_path) != 0) {
        reject_bootstrap(
            shared,
            SAN9_V52_CONFLICT_FAILED,
            SAN9_V52_BOOTSTRAP_INSTALLING,
            mapping);
        return;
    }

    expected.controller_creation_time = controller_creation;
    expected.target_pid = target.pid;
    expected.target_thread_id = target.thread_id;
    expected.target_hwnd = target.hwnd_value;
    expected.target_creation_time = target.creation_time;
    memcpy(expected.target_digest, target.exe_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(expected.exe_digest, target.exe_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(expected.context_digest, target.context_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(expected.dll_digest, dll_digest, SAN9_PING_DIGEST_SIZE);
    result = san9_v52_bootstrap_validate(&shared->envelope, BUILD_ROOT_KEY, &expected);
    if (result != SAN9_V52_OK) {
        reject_bootstrap(shared,
            result,
            SAN9_V52_BOOTSTRAP_INSTALLING,
            mapping);
        return;
    }
    result = san9_ping_session_initialize(
        &prepared_session,
        shared->envelope.session_key,
        shared->envelope.session_nonce,
        shared->envelope.target_digest,
        shared->envelope.context_digest,
        shared->envelope.target_thread_id,
        (uint32_t)(uintptr_t)&San9BridgeV52_IdleBridge);
    if (result != SAN9_PING_OK) {
        san9_ping_session_clear(&prepared_session);
        reject_bootstrap(
            shared,
            SAN9_V52_INTERNAL_FAILED,
            SAN9_V52_BOOTSTRAP_INSTALLING,
            mapping);
        return;
    }
    result = san9_v52_probe_target(
        GetCurrentProcessId(),
        GetCurrentThreadId(),
        shared->envelope.target_hwnd,
        shared->envelope.target_creation_time,
        &final_target);
    if (result != SAN9_V52_OK
        || !target_probe_matches_runtime(&final_target, &target)
        || shared->claim_enabled != 1
        || !live_memory_anchors_match(SAN9_EXACT_ORIGINAL_IDLE)) {
        san9_ping_session_clear(&prepared_session);
        reject_bootstrap(
            shared,
            result != SAN9_V52_OK ? result : SAN9_V52_TARGET_FAILED,
            SAN9_V52_BOOTSTRAP_INSTALLING,
            mapping);
        return;
    }

    /* This is the last fallible validation before commit.  The timestamp is
       freshly sampled after the final target/code/slot anchors, and the full
       authenticated envelope is revalidated.  No pin or slot operation is
       reachable unless the immediately following INSTALLING -> COMMITTING CAS
       wins ownership. */
    expected.now_ms = GetTickCount64();
    result = san9_v52_bootstrap_validate(
        &shared->envelope, BUILD_ROOT_KEY, &expected);
    if (result != SAN9_V52_OK) {
        san9_ping_session_clear(&prepared_session);
        reject_bootstrap(
            shared,
            result,
            SAN9_V52_BOOTSTRAP_INSTALLING,
            mapping);
        return;
    }
    slot_previous = InterlockedCompareExchange(
        (volatile LONG *)&shared->bootstrap_state, 0, 0);
    if (!san9_v52_lifecycle_commit_allowed(slot_previous, result)
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->bootstrap_state,
            SAN9_V52_BOOTSTRAP_COMMITTING,
            SAN9_V52_BOOTSTRAP_INSTALLING) != SAN9_V52_BOOTSTRAP_INSTALLING) {
        san9_ping_session_clear(&prepared_session);
        UnmapViewOfFile(shared);
        CloseHandle(mapping);
        return;
    }

    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            (LPCWSTR)(const void *)&San9BridgeV52_GetMsgProc,
            &pinned)
        || pinned != g_module) {
        san9_ping_session_clear(&prepared_session);
        reject_bootstrap(
            shared,
            SAN9_V52_PIN_FAILED,
            SAN9_V52_BOOTSTRAP_COMMITTING,
            mapping);
        return;
    }
    memset(&g_runtime, 0, sizeof(g_runtime));
    g_runtime.session = prepared_session;
    san9_secure_zero(&prepared_session, sizeof(prepared_session));
    g_runtime.target_baseline = final_target;
    g_runtime.shared = shared;
    g_runtime.mapping = mapping;
    MemoryBarrier();
    slot_previous = InterlockedCompareExchange(
        (volatile LONG *)(uintptr_t)SAN9_EXACT_IDLE_SLOT,
        (LONG)(uintptr_t)&San9BridgeV52_IdleBridge,
        SAN9_EXACT_ORIGINAL_IDLE);
    if ((uint32_t)slot_previous != SAN9_EXACT_ORIGINAL_IDLE) {
        san9_ping_session_clear(&g_runtime.session);
        memset(&g_runtime, 0, sizeof(g_runtime));
        reject_bootstrap(
            shared,
            SAN9_V52_SLOT_FAILED,
            SAN9_V52_BOOTSTRAP_COMMITTING,
            mapping);
        return;
    }
    InterlockedExchange(&g_runtime.installed, 1);
    InterlockedExchange(
        (volatile LONG *)&shared->bootstrap_result,
        SAN9_V52_OK);
    MemoryBarrier();
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->bootstrap_state,
            SAN9_V52_BOOTSTRAP_READY,
            SAN9_V52_BOOTSTRAP_COMMITTING)
        != SAN9_V52_BOOTSTRAP_COMMITTING) {
        /* The slot is already committed and is never restored.  An impossible
           state-owner loss therefore disables claiming and requires restart. */
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
    }
    /* Mapping/view and pinned module intentionally persist until process exit. */
}
#endif

uint32_t san9_v52_enter_idle_depth(void)
{
    if (g_idle_depth == UINT32_MAX) {
        return 0U;
    }
    return ++g_idle_depth;
}

void san9_v52_after_original(
    void *app,
    int flag,
    uint32_t depth,
    uint32_t caller)
{
#if SAN9_V52_LIVE_ENABLED == 1
    San9IdleGateSnapshot gate;
    San9V52TargetProbe fresh_target;
    int ping_result;
    if (depth == 1U && g_idle_depth == 1U
        && InterlockedCompareExchange(&g_runtime.installed, 1, 1) == 1
        && g_runtime.shared != NULL
        && g_runtime.shared->claim_enabled == 1
        && g_runtime.shared->bootstrap_state == SAN9_V52_BOOTSTRAP_READY
        && g_runtime.shared->mailbox.request_state == SAN9_MAILBOX_READY
        && app == (void *)(uintptr_t)SAN9_EXACT_APP_OBJECT
        && flag == 0
        && caller == SAN9_EXACT_IDLE_CALLER
        && GetCurrentThreadId() == g_runtime.target_baseline.thread_id
        && *(volatile uint32_t *)(uintptr_t)SAN9_EXACT_APP_OBJECT == SAN9_EXACT_APP_VTABLE
        && *(volatile uint32_t *)(uintptr_t)SAN9_EXACT_IDLE_SLOT
            == (uint32_t)(uintptr_t)&San9BridgeV52_IdleBridge
        && live_memory_anchors_match(
            (uint32_t)(uintptr_t)&San9BridgeV52_IdleBridge)
        && san9_v52_probe_target(
            g_runtime.target_baseline.pid,
            g_runtime.target_baseline.thread_id,
            g_runtime.target_baseline.hwnd_value,
            g_runtime.target_baseline.creation_time,
            &fresh_target) == SAN9_V52_OK
        && target_probe_matches_runtime(
            &fresh_target,
            &g_runtime.target_baseline)) {
        memset(&gate, 0, sizeof(gate));
        gate.caller = caller;
        gate.app_object = (uint32_t)(uintptr_t)app;
        gate.thread_id = GetCurrentThreadId();
        gate.slot_address = SAN9_EXACT_IDLE_SLOT;
        gate.slot_value = (uint32_t)(uintptr_t)&San9BridgeV52_IdleBridge;
        gate.app_vptr = *(volatile uint32_t *)(uintptr_t)SAN9_EXACT_APP_OBJECT;
        gate.idle_argument = flag;
        gate.exact_target_verified = 1U;
        gate.conflict_free = 1U;
        gate.process_generation_stable = 1U;
        ping_result = san9_ping_process_one(
            &g_runtime.session,
            &g_runtime.shared->mailbox,
            GetTickCount64(),
            &gate);
        if (ping_result == SAN9_PING_OK) {
            InterlockedIncrement(
                (volatile LONG *)&g_runtime.shared->ping_count);
        }
    }
#else
    (void)app;
    (void)flag;
    (void)caller;
#endif
    if (depth != 0U) {
        if (g_idle_depth == depth) {
            --g_idle_depth;
        } else {
            g_idle_depth = 0U;
        }
    }
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        g_module = instance;
    }
    return TRUE;
}

SAN9_EXPORT intptr_t SAN9_STDCALL San9BridgeV52_GetMsgProc(
    int code,
    uintptr_t w_param,
    intptr_t l_param)
{
#if SAN9_V52_LIVE_ENABLED == 1
    if (code >= 0 && w_param == PM_REMOVE && l_param != 0) {
        try_live_bootstrap((const MSG *)(uintptr_t)l_param);
    }
#endif
    return (intptr_t)CallNextHookEx(NULL, code, (WPARAM)w_param, (LPARAM)l_param);
}

SAN9_EXPORT intptr_t SAN9_STDCALL San9BridgeV52_ForegroundIdleProc(
    int code,
    uintptr_t w_param,
    intptr_t l_param)
{
    return (intptr_t)CallNextHookEx(NULL, code, (WPARAM)w_param, (LPARAM)l_param);
}

SAN9_EXPORT uint32_t San9BridgeV52_Bootstrap(const void *request, uint32_t size)
{
    (void)request;
    (void)size;
    return SAN9_V52_OFFLINE_ONLY;
}

SAN9_EXPORT uint32_t San9BridgeV52_GetContract(San9V52Contract *contract, uint32_t size)
{
    if (contract == NULL || size != sizeof(*contract)) {
        return SAN9_V52_INVALID;
    }
    memset(contract, 0, sizeof(*contract));
    contract->structure_size = (uint32_t)sizeof(*contract);
    contract->contract_magic = SAN9_V52_CONTRACT_MAGIC;
    contract->compiled_profile = SAN9_V52_LIVE_ENABLED
        ? SAN9_V52_PROFILE_LIVE_OPTIN : SAN9_V52_PROFILE_OFFLINE;
    contract->live_bootstrap_compiled = SAN9_V52_LIVE_ENABLED;
    contract->default_execution_authorized = 0U;
    contract->ping_only = 1U;
    contract->business_fields_present = 0U;
    contract->hot_unload_allowed = 0U;
    contract->slot_restore_allowed = 0U;
    contract->idle_calls_original_exactly_once = SAN9_V52_LIVE_ENABLED;
    contract->outer_depth_max_ping_count = 1U;
    contract->bootstrap_envelope_size = SAN9_V52_ENVELOPE_SIZE;
    contract->shared_block_size = SAN9_V52_SHARED_SIZE;
    contract->ping_frame_size = SAN9_PING_FRAME_SIZE;
    contract->exact_app_object = SAN9_EXACT_APP_OBJECT;
    contract->exact_app_vtable = SAN9_EXACT_APP_VTABLE;
    contract->exact_idle_slot = SAN9_EXACT_IDLE_SLOT;
    contract->exact_original_idle = SAN9_EXACT_ORIGINAL_IDLE;
    contract->exact_idle_caller = SAN9_EXACT_IDLE_CALLER;
    contract->exact_scheduler_vtable = SAN9_EXACT_SCHEDULER_VTABLE;
    contract->exact_controller_vtable = SAN9_EXACT_CONTROLLER_VTABLE;
    contract->csharp_bridge_288_compatible = 0U;
    contract->live_dynamic_safety_proven = 0U;
    return san9_v52_profile_marker[0] == '\0' ? SAN9_V52_INTERNAL_FAILED : SAN9_V52_OK;
}

SAN9_EXPORT uint32_t San9BridgeV52_OfflineSelfTest(
    San9V52SelfTestReport *report,
    uint32_t size)
{
    HMODULE pinned = NULL;
    uint32_t result;
    if (report == NULL || size != sizeof(*report)) {
        return SAN9_V52_INVALID;
    }
    result = (uint32_t)san9_v52_run_synthetic_tests(report);
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            (LPCWSTR)(const void *)&San9BridgeV52_OfflineSelfTest,
            &pinned)
        || pinned != g_module) {
        ++report->failed;
        result = SAN9_V52_PIN_FAILED;
    } else {
        ++report->passed;
    }
    report->live_code_executed = 0U;
    return result;
}
