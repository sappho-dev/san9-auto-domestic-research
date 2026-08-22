#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "san9_bridge_v52.h"
#include "san9_v52_lifecycle.h"
#include "live_support.h"
#include "sha256.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#if SAN9_V52_LIVE_ENABLED == 1
#include <bcrypt.h>
#include "san9_v52_build_key.h"
static const uint8_t BUILD_ROOT_KEY[SAN9_V52_ROOT_KEY_SIZE] =
    SAN9_V52_BUILD_ROOT_KEY_BYTES;
#endif

typedef uint32_t (*GetContractFunction)(San9V52Contract *, uint32_t);
typedef uint32_t (*OfflineSelfTestFunction)(San9V52SelfTestReport *, uint32_t);
typedef uint32_t (*BootstrapFunction)(const void *, uint32_t);
typedef int (SAN9_FASTCALL *IdleBridgeFunction)(void *, void *, int);

static int sibling_dll_path(wchar_t *output, size_t capacity)
{
    DWORD length;
    wchar_t *separator;
#if SAN9_V52_LIVE_ENABLED == 1
    static const wchar_t dll_name[] = L"San9BridgeV52Live.dll";
#else
    static const wchar_t dll_name[] = L"San9BridgeV52.dll";
#endif
    size_t prefix_size;
    size_t dll_size = sizeof(dll_name) / sizeof(dll_name[0]);
    if (output == NULL || capacity == 0U || capacity > 0xffffffffU) {
        return 0;
    }
    length = GetModuleFileNameW(NULL, output, (DWORD)capacity);
    if (length == 0U || length >= capacity) {
        return 0;
    }
    separator = wcsrchr(output, L'\\');
    if (separator == NULL) {
        return 0;
    }
    prefix_size = (size_t)(separator - output) + 1U;
    if (prefix_size + dll_size > capacity) {
        return 0;
    }
    memcpy(output + prefix_size, dll_name, sizeof(dll_name));
    return 1;
}

static int contract_is_exact(const San9V52Contract *contract)
{
    return contract->structure_size == sizeof(*contract)
        && contract->contract_magic == SAN9_V52_CONTRACT_MAGIC
        && contract->compiled_profile == (SAN9_V52_LIVE_ENABLED
            ? SAN9_V52_PROFILE_LIVE_OPTIN : SAN9_V52_PROFILE_OFFLINE)
        && contract->live_bootstrap_compiled == SAN9_V52_LIVE_ENABLED
        && contract->default_execution_authorized == 0U
        && contract->ping_only == 1U
        && contract->business_fields_present == 0U
        && contract->hot_unload_allowed == 0U
        && contract->slot_restore_allowed == 0U
        && contract->idle_calls_original_exactly_once == SAN9_V52_LIVE_ENABLED
        && contract->outer_depth_max_ping_count == 1U
        && contract->bootstrap_envelope_size == SAN9_V52_ENVELOPE_SIZE
        && contract->shared_block_size == SAN9_V52_SHARED_SIZE
        && contract->ping_frame_size == SAN9_PING_FRAME_SIZE
        && contract->exact_app_object == SAN9_EXACT_APP_OBJECT
        && contract->exact_app_vtable == SAN9_EXACT_APP_VTABLE
        && contract->exact_idle_slot == SAN9_EXACT_IDLE_SLOT
        && contract->exact_original_idle == SAN9_EXACT_ORIGINAL_IDLE
        && contract->exact_idle_caller == SAN9_EXACT_IDLE_CALLER
        && contract->exact_scheduler_vtable == SAN9_EXACT_SCHEDULER_VTABLE
        && contract->exact_controller_vtable == SAN9_EXACT_CONTROLLER_VTABLE
        && contract->csharp_bridge_288_compatible == 0U
        && contract->live_dynamic_safety_proven == 0U;
}

static int run_offline_self_test(const wchar_t *dll_path)
{
    HMODULE module;
    GetContractFunction get_contract;
    OfflineSelfTestFunction dll_self_test;
    BootstrapFunction bootstrap;
    IdleBridgeFunction idle_bridge;
    San9V52Contract contract;
    San9V52SelfTestReport local_report;
    San9V52SelfTestReport dll_report;
    uint32_t result;
    int failed = 0;

    result = (uint32_t)san9_v52_run_synthetic_tests(&local_report);
    if (result != SAN9_V52_OK || local_report.failed != 0U
        || local_report.live_code_executed != 0U) {
        ++failed;
    }
    module = LoadLibraryW(dll_path);
    if (module == NULL) {
        fprintf(stderr, "offline DLL load failed: win32=%lu\n", GetLastError());
        return 2;
    }
    get_contract = (GetContractFunction)(void *)GetProcAddress(module, "San9BridgeV52_GetContract");
    dll_self_test = (OfflineSelfTestFunction)(void *)GetProcAddress(
        module, "San9BridgeV52_OfflineSelfTest");
    bootstrap = (BootstrapFunction)(void *)GetProcAddress(module, "San9BridgeV52_Bootstrap");
    idle_bridge = (IdleBridgeFunction)(void *)GetProcAddress(module, "San9BridgeV52_IdleBridge");
    if (get_contract == NULL || dll_self_test == NULL || bootstrap == NULL
        || idle_bridge == NULL) {
        fputs("required V5.2 clean export missing\n", stderr);
        return 2;
    }
    memset(&contract, 0, sizeof(contract));
    if (get_contract(&contract, sizeof(contract)) != SAN9_V52_OK
        || !contract_is_exact(&contract)
        || bootstrap(NULL, 0) != SAN9_V52_OFFLINE_ONLY) {
        ++failed;
    }
#if SAN9_V52_LIVE_ENABLED == 0
    if (idle_bridge(NULL, NULL, 0) != 0) {
        ++failed;
    }
#else
    (void)idle_bridge; /* Live wrapper is machine-audited and never executed here. */
#endif
    memset(&dll_report, 0, sizeof(dll_report));
    result = dll_self_test(&dll_report, sizeof(dll_report));
    if (result != SAN9_V52_OK || dll_report.failed != 0U
        || dll_report.live_code_executed != 0U) {
        ++failed;
    }
    printf(
        "San9BridgeV52 offline self-test: %s\n"
        "local: passed=%lu failed=%lu inherited_v51=%lu bootstrap=%lu loader=%lu race=%lu idle=%lu\n"
        "dll: passed=%lu failed=%lu original_idle_calls=%lu authenticated_pings=%lu live_code_executed=%lu\n"
        "contract: profile=%s ping_only=1 business_fields=0 hot_unload=0 slot_restore=0 dynamic_safety_proven=0\n",
        failed == 0 ? "PASS" : "FAIL",
        (unsigned long)local_report.passed,
        (unsigned long)local_report.failed,
        (unsigned long)local_report.inherited_v51_passed,
        (unsigned long)local_report.bootstrap_tests,
        (unsigned long)local_report.loader_tests,
        (unsigned long)local_report.race_tests,
        (unsigned long)local_report.idle_tests,
        (unsigned long)dll_report.passed,
        (unsigned long)dll_report.failed,
        (unsigned long)dll_report.original_idle_calls,
        (unsigned long)dll_report.authenticated_pings,
        (unsigned long)dll_report.live_code_executed,
        SAN9_V52_LIVE_ENABLED ? "live-optin-compile" : "offline");
    /* DLL self-test pins the local copy; there is no FreeLibrary path. */
    return failed == 0 ? 0 : 1;
}

#if SAN9_V52_LIVE_ENABLED == 1
typedef struct LiveArguments {
    uint32_t pid;
    uint32_t thread_id;
    uint32_t hwnd_value;
    uint64_t creation_time;
    uint32_t present_mask;
} LiveArguments;

enum {
    ARG_PID = 1U << 0,
    ARG_THREAD = 1U << 1,
    ARG_HWND = 1U << 2,
    ARG_CREATION = 1U << 3,
    ARG_SHA = 1U << 4,
    ARG_HOOK = 1U << 5,
    ARG_PERSIST = 1U << 6,
    ARG_PING = 1U << 7,
    ARG_ALL = 0xffU
};

static int parse_u32(const char *text, int base, uint32_t *output)
{
    char *end = NULL;
    unsigned long value;
    if (text == NULL || *text == '\0' || output == NULL) {
        return 0;
    }
    value = strtoul(text, &end, base);
    if (*end != '\0' || value == 0U || value > 0xffffffffUL) {
        return 0;
    }
    *output = (uint32_t)value;
    return 1;
}

static int parse_u64_hex(const char *text, uint64_t *output)
{
    char *end = NULL;
    unsigned long long value;
    if (text == NULL || *text == '\0' || output == NULL) {
        return 0;
    }
    value = strtoull(text, &end, 16);
    if (*end != '\0' || value == 0U) {
        return 0;
    }
    *output = (uint64_t)value;
    return 1;
}

static int accept_once(uint32_t *mask, uint32_t bit)
{
    if ((*mask & bit) != 0U) {
        return 0;
    }
    *mask |= bit;
    return 1;
}

static int parse_live_arguments(int argc, char **argv, LiveArguments *arguments)
{
    int index;
    memset(arguments, 0, sizeof(*arguments));
    if (argc != 18 || strcmp(argv[1], "--live-bootstrap") != 0) {
        return 0;
    }
    for (index = 2; index + 1 < argc; index += 2) {
        const char *name = argv[index];
        const char *value = argv[index + 1];
        if (strcmp(name, "--pid") == 0 && accept_once(&arguments->present_mask, ARG_PID)) {
            if (!parse_u32(value, 10, &arguments->pid)) return 0;
        } else if (strcmp(name, "--thread-id") == 0
            && accept_once(&arguments->present_mask, ARG_THREAD)) {
            if (!parse_u32(value, 10, &arguments->thread_id)) return 0;
        } else if (strcmp(name, "--hwnd-hex") == 0
            && accept_once(&arguments->present_mask, ARG_HWND)) {
            if (!parse_u32(value, 16, &arguments->hwnd_value)) return 0;
        } else if (strcmp(name, "--creation-filetime-hex") == 0
            && accept_once(&arguments->present_mask, ARG_CREATION)) {
            if (!parse_u64_hex(value, &arguments->creation_time)) return 0;
        } else if (strcmp(name, "--confirm-target-sha") == 0
            && accept_once(&arguments->present_mask, ARG_SHA)) {
            if (strcmp(value,
                "D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028") != 0) return 0;
        } else if (strcmp(name, "--confirm-hook-injection") == 0
            && accept_once(&arguments->present_mask, ARG_HOOK)) {
            if (strcmp(value, "I_ACCEPT_SETWINDOWSHOOKEX") != 0) return 0;
        } else if (strcmp(name, "--confirm-persistent-slot") == 0
            && accept_once(&arguments->present_mask, ARG_PERSIST)) {
            if (strcmp(value, "I_ACCEPT_NO_HOT_UNLOAD") != 0) return 0;
        } else if (strcmp(name, "--confirm-ping-only") == 0
            && accept_once(&arguments->present_mask, ARG_PING)) {
            if (strcmp(value, "I_ACCEPT_PING_ONLY_NO_BUSINESS") != 0) return 0;
        } else {
            return 0;
        }
    }
    return arguments->present_mask == ARG_ALL;
}

static int random_bytes(uint8_t *output, size_t size)
{
    return output != NULL && size <= 0xffffffffU
        && BCryptGenRandom(NULL, output, (ULONG)size,
            BCRYPT_USE_SYSTEM_PREFERRED_RNG) == 0;
}

static int make_random_names(
    wchar_t mapping_name[SAN9_V52_MAPPING_NAME_MAX],
    wchar_t message_name[SAN9_V52_MAPPING_NAME_MAX])
{
    uint8_t mapping_random[24];
    uint8_t message_random[24];
    wchar_t mapping_hex[49];
    wchar_t message_hex[49];
    size_t index;
    if (!random_bytes(mapping_random, sizeof(mapping_random))
        || !random_bytes(message_random, sizeof(message_random))) {
        return 0;
    }
    for (index = 0; index < sizeof(mapping_random); ++index) {
        swprintf(mapping_hex + index * 2U, 3, L"%02X", mapping_random[index]);
        swprintf(message_hex + index * 2U, 3, L"%02X", message_random[index]);
    }
    mapping_hex[48] = L'\0';
    message_hex[48] = L'\0';
    if (swprintf(mapping_name, SAN9_V52_MAPPING_NAME_MAX,
            L"Local\\San9V52-%ls", mapping_hex) < 0
        || swprintf(message_name, SAN9_V52_MAPPING_NAME_MAX,
            L"San9V52Msg-%ls", message_hex) < 0) {
        return 0;
    }
    san9_secure_zero(mapping_random, sizeof(mapping_random));
    san9_secure_zero(message_random, sizeof(message_random));
    return 1;
}

static int probes_same_identity(
    const San9V52TargetProbe *left,
    const San9V52TargetProbe *right,
    int require_same_modules)
{
    return left->pid == right->pid
        && left->thread_id == right->thread_id
        && left->hwnd_value == right->hwnd_value
        && left->creation_time == right->creation_time
        && _wcsicmp(left->image_path, right->image_path) == 0
        && san9_constant_time_equal(
            left->exe_digest, right->exe_digest, SAN9_PING_DIGEST_SIZE)
        && san9_constant_time_equal(
            left->context_digest, right->context_digest, SAN9_PING_DIGEST_SIZE)
        && (!require_same_modules
            || (left->module_count == right->module_count
                && left->module_without_bridge_count
                    == right->module_without_bridge_count
                && left->bridge_module_count == right->bridge_module_count
                && left->bridge_module_base == right->bridge_module_base
                && left->bridge_module_size == right->bridge_module_size
                && _wcsicmp(
                    left->bridge_module_path,
                    right->bridge_module_path) == 0
                && san9_constant_time_equal(
                    left->module_inventory_digest,
                    right->module_inventory_digest,
                    SAN9_PING_DIGEST_SIZE)
                && san9_constant_time_equal(
                    left->module_without_bridge_digest,
                    right->module_without_bridge_digest,
                    SAN9_PING_DIGEST_SIZE)));
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

static int probe_has_no_bridge(const San9V52TargetProbe *probe)
{
    return probe->bridge_module_count == 0U
        && probe->bridge_module_base == 0U
        && probe->bridge_module_size == 0U
        && probe->bridge_module_path[0] == L'\0'
        && probe->module_without_bridge_count == probe->module_count
        && san9_constant_time_equal(
            probe->module_without_bridge_digest,
            probe->module_inventory_digest,
            SAN9_PING_DIGEST_SIZE);
}

static int post_ready_bridge_delta_is_exact(
    const San9V52TargetProbe *pre_hook,
    const San9V52TargetProbe *post_ready,
    const wchar_t *dll_path,
    HMODULE local_module,
    const uint8_t dll_digest[SAN9_PING_DIGEST_SIZE])
{
    uint8_t observed_digest[SAN9_PING_DIGEST_SIZE];
    uint32_t image_size = loaded_image_size(local_module);
    return probe_has_no_bridge(pre_hook)
        && post_ready->bridge_module_count == 1U
        && post_ready->bridge_module_base != 0U
        && post_ready->bridge_module_size != 0U
        && post_ready->bridge_module_size == image_size
        && _wcsicmp(post_ready->bridge_module_path, dll_path) == 0
        && post_ready->module_count == pre_hook->module_count + 1U
        && post_ready->module_without_bridge_count == pre_hook->module_count
        && san9_constant_time_equal(
            post_ready->module_without_bridge_digest,
            pre_hook->module_inventory_digest,
            SAN9_PING_DIGEST_SIZE)
        && san9_v52_hash_file(
            post_ready->bridge_module_path,
            observed_digest)
        && san9_constant_time_equal(
            observed_digest,
            dll_digest,
            SAN9_PING_DIGEST_SIZE);
}

static LONG read_bootstrap_state(const San9V52SharedBlock *shared)
{
    return InterlockedCompareExchange(
        (volatile LONG *)&shared->bootstrap_state,
        0,
        0);
}

static int try_stop_precommit(
    San9V52SharedBlock *shared,
    int allow_writing,
    LONG *observed_state)
{
    LONG state = read_bootstrap_state(shared);
    for (;;) {
        LONG previous;
        if (!san9_v52_lifecycle_cancel_allowed(state, allow_writing)) {
            *observed_state = state;
            return 0;
        }
        previous = InterlockedCompareExchange(
            (volatile LONG *)&shared->bootstrap_state,
            SAN9_V52_BOOTSTRAP_STOPPED,
            state);
        if (previous == state) {
            /* Cancellation owns STOPPED before it disables claim. */
            InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
            *observed_state = SAN9_V52_BOOTSTRAP_STOPPED;
            return 1;
        }
        state = previous;
    }
}

static int unhook_with_diagnostic(
    HHOOK *hook,
    const char *phase,
    uint32_t *attempts,
    uint32_t *failure_streak)
{
    DWORD error;
    if (*hook == NULL) {
        return 1;
    }
    ++*attempts;
    SetLastError(ERROR_SUCCESS);
    if (UnhookWindowsHookEx(*hook)) {
        *hook = NULL;
        *failure_streak = san9_v52_lifecycle_next_failure_streak(
            *failure_streak, 1);
        return 1;
    }
    *failure_streak = san9_v52_lifecycle_next_failure_streak(
        *failure_streak, 0);
    error = GetLastError();
    fprintf(stderr,
        "V5.2 %s: UnhookWindowsHookEx failed, win32=%lu streak=%lu; retry required\n",
        phase, (unsigned long)error, (unsigned long)*failure_streak);
    return 0;
}

static int delete_atom_with_diagnostic(
    ATOM *atom,
    const char *phase,
    uint32_t *attempts,
    uint32_t *failure_streak)
{
    DWORD error;
    ATOM result;
    if (*atom == 0U) {
        return 1;
    }
    ++*attempts;
    SetLastError(ERROR_SUCCESS);
    result = GlobalDeleteAtom(*atom);
    if (result == 0U) {
        *atom = 0U;
        *failure_streak = san9_v52_lifecycle_next_failure_streak(
            *failure_streak, 1);
        return 1;
    }
    *failure_streak = san9_v52_lifecycle_next_failure_streak(
        *failure_streak, 0);
    error = GetLastError();
    fprintf(stderr,
        "V5.2 %s: GlobalDeleteAtom failed, atom=%u win32=%lu streak=%lu; retry required\n",
        phase, (unsigned)*atom, (unsigned long)error,
        (unsigned long)*failure_streak);
    return 0;
}

static int run_live_bootstrap(
    const LiveArguments *arguments,
    const wchar_t *dll_path)
{
    San9V52TargetProbe initial;
    San9V52TargetProbe pre_hook;
    San9V52TargetProbe post_ready;
    San9V52SharedBlock *shared = NULL;
    San9PingSession controller_session;
    uint8_t dll_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t random_material[100];
    uint8_t request[SAN9_PING_FRAME_SIZE];
    uint8_t response[SAN9_PING_FRAME_SIZE];
    wchar_t mapping_name[SAN9_V52_MAPPING_NAME_MAX];
    wchar_t message_name[SAN9_V52_MAPPING_NAME_MAX];
    HANDLE mapping = NULL;
    HMODULE module = NULL;
    HOOKPROC hook_proc = NULL;
    HHOOK hook = NULL;
    ATOM atom = 0U;
    UINT registered_message = 0U;
    uint64_t controller_creation = 0U;
    uint64_t now;
    uint64_t commit_wait_start = 0U;
    uint64_t ping_deadline_ms = 0U;
    LONG state = SAN9_V52_BOOTSTRAP_EMPTY;
    LONG observed_ping_count = 0;
    uint32_t unhook_attempts = 0U;
    uint32_t atom_attempts = 0U;
    uint32_t unhook_failure_streak = 0U;
    uint32_t atom_failure_streak = 0U;
    San9V52RecoveryFacts recovery;
    int result = SAN9_V52_INTERNAL_FAILED;
    int session_initialized = 0;
    int installed_ready = 0;
    int commit_watchdog_active = 0;
    int restart_required = 0;
    int cleanup_class = SAN9_V52_RECOVERY_CLEAN;
    int exit_code = 1;

    memset(&controller_session, 0, sizeof(controller_session));
    memset(dll_digest, 0, sizeof(dll_digest));
    memset(random_material, 0, sizeof(random_material));
    memset(request, 0, sizeof(request));
    memset(response, 0, sizeof(response));
    memset(mapping_name, 0, sizeof(mapping_name));
    memset(message_name, 0, sizeof(message_name));
    memset(&recovery, 0, sizeof(recovery));

    result = san9_v52_probe_target(
        arguments->pid, arguments->thread_id, arguments->hwnd_value,
        arguments->creation_time, &initial);
    if (result != SAN9_V52_OK
        || !probe_has_no_bridge(&initial)
        || !san9_v52_hash_file(dll_path, dll_digest)) {
        goto cleanup;
    }
    if (!san9_v52_get_process_creation_time(
            GetCurrentProcessId(), &controller_creation)) {
        result = SAN9_V52_GENERATION_FAILED;
        goto cleanup;
    }
    if (!make_random_names(mapping_name, message_name)
        || !random_bytes(random_material, sizeof(random_material))) {
        goto cleanup;
    }
    registered_message = RegisterWindowMessageW(message_name);
    if (registered_message < 0xc000U || registered_message > 0xffffU) {
        goto cleanup;
    }
    atom = GlobalAddAtomW(mapping_name);
    if (atom == 0U) {
        goto cleanup;
    }
    /* NULL SECURITY_ATTRIBUTES uses the token's default DACL. This is only an
       accidental-collision boundary, not protection from a malicious peer
       running as the same user. */
    mapping = CreateFileMappingW(
        INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0,
        SAN9_V52_SHARED_SIZE, mapping_name);
    if (mapping == NULL || GetLastError() == ERROR_ALREADY_EXISTS) {
        goto cleanup;
    }
    shared = (San9V52SharedBlock *)MapViewOfFile(
        mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, SAN9_V52_SHARED_SIZE);
    if (shared == NULL) {
        goto cleanup;
    }
    memset(shared, 0, sizeof(*shared));
    shared->bootstrap_state = SAN9_V52_BOOTSTRAP_WRITING;
    shared->claim_enabled = 1;
    shared->envelope.sequence = 1U;
    now = GetTickCount64();
    shared->envelope.issued_at_ms = now;
    shared->envelope.expires_at_ms = now + SAN9_V52_MAX_BOOTSTRAP_LIFETIME_MS;
    shared->envelope.controller_pid = GetCurrentProcessId();
    shared->envelope.target_pid = arguments->pid;
    shared->envelope.target_thread_id = arguments->thread_id;
    shared->envelope.target_hwnd = arguments->hwnd_value;
    shared->envelope.target_creation_time = arguments->creation_time;
    shared->envelope.controller_creation_time = controller_creation;
    shared->envelope.registered_message = registered_message;
    shared->envelope.mapping_atom = atom;
    memcpy(&shared->envelope.message_tag, random_material, sizeof(uint32_t));
    if (shared->envelope.message_tag == 0U) shared->envelope.message_tag = 1U;
    memcpy(shared->envelope.session_key, random_material + 4, SAN9_PING_KEY_SIZE);
    memcpy(shared->envelope.session_nonce, random_material + 36, SAN9_PING_NONCE_SIZE);
    memcpy(shared->envelope.request_id, random_material + 52, SAN9_PING_REQUEST_ID_SIZE);
    memcpy(shared->envelope.challenge, random_material + 68, SAN9_PING_DIGEST_SIZE);
    memcpy(shared->envelope.target_digest, initial.exe_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(shared->envelope.exe_digest, initial.exe_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(shared->envelope.context_digest, initial.context_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(shared->envelope.dll_digest, dll_digest, SAN9_PING_DIGEST_SIZE);
    if (!san9_v52_hash_wide_string(
            mapping_name, shared->envelope.mapping_name_digest)
        || san9_v52_bootstrap_seal(&shared->envelope, BUILD_ROOT_KEY) != SAN9_V52_OK) {
        goto cleanup;
    }
    MemoryBarrier();
    InterlockedExchange(
        (volatile LONG *)&shared->bootstrap_state,
        SAN9_V52_BOOTSTRAP_SEALED);

    module = LoadLibraryW(dll_path);
    if (module == NULL) {
        goto cleanup;
    }
    hook_proc = (HOOKPROC)(void *)GetProcAddress(module, "San9BridgeV52_GetMsgProc");
    if (hook_proc == NULL) {
        goto cleanup;
    }
    /* Fresh pre-hook gate; no result from the initial probe is blindly reused. */
    result = san9_v52_probe_target(
        arguments->pid, arguments->thread_id, arguments->hwnd_value,
        arguments->creation_time, &pre_hook);
    if (result != SAN9_V52_OK
        || !probe_has_no_bridge(&pre_hook)
        || !probes_same_identity(&initial, &pre_hook, 1)) {
        goto cleanup;
    }
    hook = SetWindowsHookExW(WH_GETMESSAGE, hook_proc, module, arguments->thread_id);
    if (hook == NULL) {
        goto cleanup;
    }
    if (!PostThreadMessageW(
            arguments->thread_id,
            registered_message,
            atom,
            (LPARAM)shared->envelope.message_tag)) {
        goto cleanup;
    }
    for (;;) {
        uint64_t current_now;
        int deadline_decision;
        LONG observed;
        state = read_bootstrap_state(shared);
        if (state == SAN9_V52_BOOTSTRAP_READY
            || state == SAN9_V52_BOOTSTRAP_REJECTED) {
            break;
        }
        if (state == SAN9_V52_BOOTSTRAP_STOPPED) {
            result = SAN9_V52_TIMEOUT;
            goto cleanup;
        }
        current_now = GetTickCount64();
        deadline_decision = san9_v52_lifecycle_deadline_decision(
            state,
            current_now,
            shared->envelope.expires_at_ms);
        if (deadline_decision == SAN9_V52_DEADLINE_WAIT_COMMIT) {
            if (!commit_watchdog_active) {
                commit_watchdog_active = 1;
                commit_wait_start = current_now;
            } else if (current_now < commit_wait_start
                || current_now - commit_wait_start
                >= SAN9_V52_COMMIT_WATCHDOG_MS) {
                result = SAN9_V52_RESTART_REQUIRED;
                restart_required = 1;
                recovery.indeterminate_commit = 1U;
                exit_code = 3;
                fputs(
                    "V5.2 bootstrap is INDETERMINATE in COMMITTING/REJECTING; "
                    "the target must be restarted before retry\n",
                    stderr);
                goto cleanup;
            }
        } else if (deadline_decision == SAN9_V52_DEADLINE_TRY_CANCEL) {
            if (try_stop_precommit(shared, 0, &observed)) {
                result = SAN9_V52_TIMEOUT;
                fputs("V5.2 authenticated envelope expired and STOPPED won the pre-commit CAS\n",
                    stderr);
                goto cleanup;
            }
            if (observed == SAN9_V52_BOOTSTRAP_COMMITTING
                || observed == SAN9_V52_BOOTSTRAP_REJECTING) {
                commit_watchdog_active = 1;
                commit_wait_start = current_now;
            } else if (observed != SAN9_V52_BOOTSTRAP_READY
                && observed != SAN9_V52_BOOTSTRAP_REJECTED) {
                result = SAN9_V52_INDETERMINATE;
                restart_required = 1;
                recovery.indeterminate_commit = 1U;
                exit_code = 3;
                fputs(
                    "V5.2 bootstrap reached an unexpected non-cancellable state; "
                    "restart required\n",
                    stderr);
                goto cleanup;
            }
        } else if (deadline_decision == SAN9_V52_DEADLINE_RESTART_REQUIRED) {
            result = SAN9_V52_RESTART_REQUIRED;
            restart_required = 1;
            recovery.indeterminate_commit = 1U;
            exit_code = 3;
            fputs("V5.2 bootstrap deadline reached an invalid lifecycle state; restart required\n",
                stderr);
            goto cleanup;
        }
        Sleep(1);
    }
    result = InterlockedCompareExchange(
        (volatile LONG *)&shared->bootstrap_result, 0, 0);
    if (state == SAN9_V52_BOOTSTRAP_REJECTED
        && result == SAN9_V52_SLOT_FAILED) {
        /* SLOT_FAILED is published only after the commit owner has pinned. */
        recovery.pin_succeeded = 1U;
        recovery.slot_failed = 1U;
    }
    if (state == SAN9_V52_BOOTSTRAP_READY && result == SAN9_V52_OK) {
        installed_ready = 1;
        recovery.ready_seen = 1U;
    }
    /* A terminal state is followed immediately by a bounded unhook.  A single
       transient failure is retried; two consecutive failures are classified
       by the shared lifecycle policy as RESTART_REQUIRED. */
    while (hook != NULL && unhook_attempts < 2U) {
        if (unhook_with_diagnostic(
                &hook,
                "terminal unhook",
                &unhook_attempts,
                &unhook_failure_streak)) {
            break;
        }
    }
    if (hook != NULL) {
        goto cleanup;
    }
    /* The target has already resolved and opened the mapping at any terminal
       state, so the bootstrap atom is no longer needed. */
    (void)delete_atom_with_diagnostic(
        &atom,
        "terminal atom release",
        &atom_attempts,
        &atom_failure_streak);
    if (state != SAN9_V52_BOOTSTRAP_READY || result != SAN9_V52_OK) {
        goto cleanup;
    }
    /* Fresh post-ready gate. Module A/B must be stable at this new phase; the
       inventory may legitimately include the newly pinned V5.2 DLL. */
    result = san9_v52_probe_target(
        arguments->pid, arguments->thread_id, arguments->hwnd_value,
        arguments->creation_time, &post_ready);
    if (result != SAN9_V52_OK
        || !probes_same_identity(&pre_hook, &post_ready, 0)
        || !post_ready_bridge_delta_is_exact(
            &pre_hook,
            &post_ready,
            dll_path,
            module,
            dll_digest)) {
        if (result == SAN9_V52_OK) {
            result = SAN9_V52_TARGET_FAILED;
        }
        recovery.post_ready_failed = 1U;
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
        goto cleanup;
    }

    result = san9_ping_session_initialize(
        &controller_session,
        shared->envelope.session_key,
        shared->envelope.session_nonce,
        shared->envelope.target_digest,
        shared->envelope.context_digest,
        arguments->thread_id,
        1U);
    if (result == SAN9_PING_OK) {
        session_initialized = 1;
    } else {
        recovery.ping_failed = 1U;
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
        goto cleanup;
    }
    now = GetTickCount64();
    ping_deadline_ms = now + 2000U;
    result = san9_ping_make_request(
        &controller_session, 1U, now, ping_deadline_ms,
        shared->envelope.request_id,
        shared->envelope.challenge,
        request);
    if (result == SAN9_PING_OK) {
        result = san9_ping_publish_request(&shared->mailbox, request);
    }
    if (result != SAN9_PING_OK) {
        recovery.ping_failed = 1U;
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
        goto cleanup;
    }

    for (;;) {
        LONG request_state;
        LONG response_state;
        int completion;
        response_state = InterlockedCompareExchange(
            (volatile LONG *)&shared->mailbox.response_state, 0, 0);
        request_state = InterlockedCompareExchange(
            (volatile LONG *)&shared->mailbox.request_state, 0, 0);
        observed_ping_count = InterlockedCompareExchange(
            (volatile LONG *)&shared->ping_count, 0, 0);
        completion = san9_v52_lifecycle_ping_completion(
            request_state, response_state, observed_ping_count);
        if (completion == SAN9_V52_PING_TAKE_ONCE) {
            break;
        }
        if (completion == SAN9_V52_PING_FAIL_MULTIPLE
            || completion == SAN9_V52_PING_FAIL_INVALID) {
            result = SAN9_V52_SEQUENCE_FAILED;
            recovery.ping_failed = 1U;
            InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
            goto cleanup;
        }
        if (GetTickCount64() >= ping_deadline_ms) {
            result = SAN9_V52_TIMEOUT;
            recovery.ping_failed = 1U;
            InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
            goto cleanup;
        }
        Sleep(1);
    }

    result = san9_ping_take_response(&shared->mailbox, response);
    if (result == SAN9_PING_OK) {
        result = san9_ping_verify_response(&controller_session, request, response);
    }
    observed_ping_count = InterlockedCompareExchange(
        (volatile LONG *)&shared->ping_count, 0, 0);
    if (result != SAN9_PING_OK || observed_ping_count != 1) {
        recovery.ping_failed = 1U;
        InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
        goto cleanup;
    }
    InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
    InterlockedExchange(
        (volatile LONG *)&shared->controller_acknowledged,
        1);
    (void)InterlockedCompareExchange(
        (volatile LONG *)&shared->bootstrap_state,
        SAN9_V52_BOOTSTRAP_STOPPED,
        SAN9_V52_BOOTSTRAP_READY);
    exit_code = 0;
    printf("V5.2 live-optin ping result=%d ping_count=%ld; bridge remains pinned/no-op\n",
        result, (long)observed_ping_count);

cleanup:
    if (shared != NULL) {
        LONG cleanup_state = read_bootstrap_state(shared);
        LONG cleanup_result = InterlockedCompareExchange(
            (volatile LONG *)&shared->bootstrap_result, 0, 0);
        if (cleanup_state == SAN9_V52_BOOTSTRAP_REJECTED
            && cleanup_result == SAN9_V52_SLOT_FAILED) {
            recovery.pin_succeeded = 1U;
            recovery.slot_failed = 1U;
        }
        if (!installed_ready
            && cleanup_state == SAN9_V52_BOOTSTRAP_READY) {
            /* READY won but the controller did not reach its post-ready gate. */
            installed_ready = 1;
            recovery.ready_seen = 1U;
            recovery.post_ready_failed = 1U;
        }
        if (!restart_required
            && (installed_ready
                || cleanup_state == SAN9_V52_BOOTSTRAP_READY)) {
            InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
            InterlockedExchange(
                (volatile LONG *)&shared->controller_acknowledged,
                1);
            (void)InterlockedCompareExchange(
                (volatile LONG *)&shared->bootstrap_state,
                SAN9_V52_BOOTSTRAP_STOPPED,
                SAN9_V52_BOOTSTRAP_READY);
        } else if (!restart_required
            && cleanup_state != SAN9_V52_BOOTSTRAP_REJECTED
            && cleanup_state != SAN9_V52_BOOTSTRAP_STOPPED) {
            LONG observed;
            if (!try_stop_precommit(shared, 1, &observed)
                && (observed == SAN9_V52_BOOTSTRAP_COMMITTING
                    || observed == SAN9_V52_BOOTSTRAP_REJECTING)) {
                restart_required = 1;
                recovery.indeterminate_commit = 1U;
                result = SAN9_V52_RESTART_REQUIRED;
                exit_code = 3;
                fputs(
                    "V5.2 cleanup observed COMMITTING/REJECTING; state and claim "
                    "were not cancelled, target restart required\n",
                    stderr);
            }
        }
    }
    while (hook != NULL && unhook_attempts < 2U) {
        if (unhook_with_diagnostic(
                &hook,
                "cleanup unhook retry",
                &unhook_attempts,
                &unhook_failure_streak)) {
            break;
        }
    }
    while (atom != 0U && atom_attempts < 2U) {
        if (delete_atom_with_diagnostic(
                &atom,
                "cleanup atom retry",
                &atom_attempts,
                &atom_failure_streak)) {
            break;
        }
    }
    recovery.unhook_failure_streak = unhook_failure_streak;
    recovery.atom_failure_streak = atom_failure_streak;
    cleanup_class = san9_v52_lifecycle_recovery_classify(&recovery);
    if (atom_failure_streak >= 2U) {
        fputs(
            "V5.2 CLEANUP_INCOMPLETE: GlobalDeleteAtom failed twice; atom remains registered\n",
            stderr);
    }
    if (cleanup_class == SAN9_V52_RECOVERY_RESTART_REQUIRED) {
        restart_required = 1;
        result = SAN9_V52_RESTART_REQUIRED;
        exit_code = 3;
    } else if (cleanup_class == SAN9_V52_RECOVERY_CLEANUP_INCOMPLETE) {
        result = SAN9_V52_CLEANUP_INCOMPLETE;
        exit_code = 4;
    }
    if (session_initialized) {
        san9_ping_session_clear(&controller_session);
    }
    san9_secure_zero(random_material, sizeof(random_material));
    san9_secure_zero(request, sizeof(request));
    san9_secure_zero(response, sizeof(response));
    if (shared != NULL) {
        UnmapViewOfFile(shared);
    }
    if (mapping != NULL) {
        CloseHandle(mapping);
    }
    /* No slot restore, no target FreeLibrary, and no local FreeLibrary. */
    if (restart_required) {
        fputs(
            "V5.2 result=INDETERMINATE/RESTART_REQUIRED; do not retry in this target process\n",
            stderr);
    }
    return exit_code;
}
#endif

int main(int argc, char **argv)
{
    wchar_t dll_path[32768];
    if (!sibling_dll_path(dll_path, sizeof(dll_path) / sizeof(dll_path[0]))) {
        fputs("failed to resolve sibling V5.2 DLL\n", stderr);
        return 2;
    }
    if (argc == 2 && strcmp(argv[1], "--self-test") == 0) {
        return run_offline_self_test(dll_path);
    }
#if SAN9_V52_LIVE_ENABLED == 1
    {
        LiveArguments arguments;
        if (!parse_live_arguments(argc, argv, &arguments)) {
            fputs(
                "live-optin console prototype is not the main UI and must not be "
                "double-clicked; exact high-friction arguments required, no action taken\n",
                stderr);
            return 2;
        }
        return run_live_bootstrap(&arguments, dll_path);
    }
#else
    (void)argv;
    fputs("OFFLINE_ONLY: this artifact was compiled with SAN9_V52_LIVE_ENABLED=0\n", stderr);
    return 2;
#endif
}
