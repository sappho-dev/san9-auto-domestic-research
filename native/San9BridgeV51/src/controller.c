#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "san9_bridge_v51.h"

#include <stdio.h>
#include <string.h>
#include <wchar.h>

typedef uint32_t (*GetContractFunction)(San9BridgeContract *, uint32_t);
typedef uint32_t (*OfflineSelfTestFunction)(San9BridgeSelfTestReport *, uint32_t);
typedef uint32_t (*BootstrapFunction)(const void *, uint32_t);
typedef int (SAN9_FASTCALL *IdleBridgeFunction)(void *, void *, int);

static int sibling_dll_path(wchar_t *output, size_t capacity)
{
    DWORD length;
    wchar_t *separator;
    static const wchar_t dll_name[] = L"San9BridgeV51.dll";
    size_t prefix_size;
    size_t dll_size = sizeof(dll_name) / sizeof(dll_name[0]);

    if (output == NULL || capacity == 0U || capacity > 0xffffffffU) {
        return 0;
    }
    length = GetModuleFileNameW(NULL, output, (DWORD)capacity);
    if (length == 0 || length >= capacity) {
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

static int contract_is_exact(const San9BridgeContract *contract)
{
    return contract->structure_size == sizeof(*contract)
        && contract->contract_magic == SAN9_PING_PROOF_CONTRACT_MAGIC
        && contract->schema_major == 1
        && contract->schema_minor == 0
        && contract->ping_frame_size == SAN9_PING_FRAME_SIZE
        && contract->mailbox_size == SAN9_PING_MAILBOX_SIZE
        && contract->session_size == SAN9_PING_SESSION_SIZE
        && contract->protocol_role == SAN9_PROTOCOL_ROLE_PING_BOOTSTRAP_PROOF
        && contract->ping_only == 1
        && contract->production_business_protocol_compatible == 0
        && contract->csharp_bridge_288_compatible == 0
        && contract->unauthenticated_recovery_requires_ack == 1
        && contract->trusted_monotonic_time_source_required == 1
        && contract->clock_rollback_requires_new_session == 1
        && contract->execution_authorized == 0
        && contract->hot_unload_allowed == 0
        && contract->live_bootstrap_enabled == 0
        && contract->exact_app_object == SAN9_EXACT_APP_OBJECT
        && contract->exact_app_vtable == SAN9_EXACT_APP_VTABLE
        && contract->exact_idle_slot == SAN9_EXACT_IDLE_SLOT
        && contract->exact_original_idle == SAN9_EXACT_ORIGINAL_IDLE
        && contract->exact_idle_caller == SAN9_EXACT_IDLE_CALLER
        && contract->exact_scheduler_vtable == SAN9_EXACT_SCHEDULER_VTABLE
        && contract->exact_controller_vtable == SAN9_EXACT_CONTROLLER_VTABLE;
}

int main(int argc, char **argv)
{
    wchar_t dll_path[32768];
    HMODULE module;
    GetContractFunction get_contract;
    OfflineSelfTestFunction dll_self_test;
    BootstrapFunction bootstrap;
    IdleBridgeFunction idle_bridge;
    San9BridgeContract contract;
    San9BridgeSelfTestReport local_report;
    San9BridgeSelfTestReport dll_report;
    uint32_t contract_result;
    uint32_t dll_result;
    int local_result;
    int failed = 0;

    if (argc != 2 || strcmp(argv[1], "--self-test") != 0) {
        fputs("offline-only: invoke exactly with --self-test\n", stderr);
        return 2;
    }
    if (!sibling_dll_path(dll_path, sizeof(dll_path) / sizeof(dll_path[0]))) {
        fputs("failed to resolve sibling DLL path\n", stderr);
        return 2;
    }

    local_result = san9_protocol_run_self_tests(&local_report);
    if (local_result != SAN9_PING_OK || local_report.failed != 0U) {
        ++failed;
    }

    module = LoadLibraryW(dll_path);
    if (module == NULL) {
        fprintf(stderr, "failed to load offline test DLL: win32=%lu\n", GetLastError());
        return 2;
    }

    get_contract = (GetContractFunction)(void *)GetProcAddress(module, "San9Bridge_GetContract");
    dll_self_test = (OfflineSelfTestFunction)(void *)GetProcAddress(module, "San9Bridge_OfflineSelfTest");
    bootstrap = (BootstrapFunction)(void *)GetProcAddress(module, "San9Bridge_Bootstrap");
    idle_bridge = (IdleBridgeFunction)(void *)GetProcAddress(module, "San9Bridge_IdleBridge");
    if (get_contract == NULL || dll_self_test == NULL || bootstrap == NULL || idle_bridge == NULL) {
        fputs("required clean export missing\n", stderr);
        return 2;
    }

    memset(&contract, 0, sizeof(contract));
    contract_result = get_contract(&contract, (uint32_t)sizeof(contract));
    if (contract_result != SAN9_PING_OK || !contract_is_exact(&contract)) {
        ++failed;
    }
    if (bootstrap(NULL, 0) != SAN9_PING_OFFLINE_ONLY) {
        ++failed;
    }
    if (idle_bridge(NULL, NULL, 0) != 0) {
        ++failed;
    }

    memset(&dll_report, 0, sizeof(dll_report));
    dll_result = dll_self_test(&dll_report, (uint32_t)sizeof(dll_report));
    if (dll_result != SAN9_PING_OK || dll_report.failed != 0U
        || dll_report.module_pinned != 1U || dll_report.ping_only != 1U
        || dll_report.execution_authorized != 0U
        || dll_report.live_bootstrap_enabled != 0U
        || dll_report.original_idle_calls != 2U
        || dll_report.authenticated_pings != 1U
        || dll_report.rejected_pings != 0U) {
        ++failed;
    }

    printf(
        "San9BridgeV51 offline self-test: %s\n"
        "local_protocol: passed=%lu failed=%lu\n"
        "dll: passed=%lu failed=%lu pinned=%lu original_idle_calls=%lu authenticated_pings=%lu\n"
        "contract: ping_bootstrap_proof=1 business_compatible=0 csharp_288_compatible=0 unauth_ack_reset=1 trusted_monotonic=1 clock_fault_new_session=1 live_bootstrap=0 execution_authorized=0\n",
        failed == 0 ? "PASS" : "FAIL",
        (unsigned long)local_report.passed,
        (unsigned long)local_report.failed,
        (unsigned long)dll_report.passed,
        (unsigned long)dll_report.failed,
        (unsigned long)dll_report.module_pinned,
        (unsigned long)dll_report.original_idle_calls,
        (unsigned long)dll_report.authenticated_pings);

    /* The DLL pinned itself during its self-test. There is deliberately no
       FreeLibrary path; process exit is the only teardown in this harness. */
    return failed == 0 ? 0 : 1;
}
