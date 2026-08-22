#ifndef SAN9_BRIDGE_V52_H
#define SAN9_BRIDGE_V52_H

#include "san9_bridge_v51.h"

#include <stddef.h>
#include <stdint.h>

#ifndef SAN9_V52_LIVE_ENABLED
#define SAN9_V52_LIVE_ENABLED 0
#endif

#if SAN9_V52_LIVE_ENABLED != 0 && SAN9_V52_LIVE_ENABLED != 1
#error SAN9_V52_LIVE_ENABLED must be exactly 0 or 1
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum {
    SAN9_V52_BOOTSTRAP_MAGIC = 0x32504253, /* "SBP2" */
    SAN9_V52_CONTRACT_MAGIC = 0x32563553, /* "S5V2" */
    SAN9_V52_SCHEMA_MAJOR = 1,
    SAN9_V52_SCHEMA_MINOR = 1,
    SAN9_V52_ENVELOPE_SIZE = 512,
    SAN9_V52_SHARED_SIZE = 4096,
    SAN9_V52_CONTROL_SIZE = 64,
    SAN9_V52_MAX_BOOTSTRAP_LIFETIME_MS = 5000,
    SAN9_V52_COMMIT_WATCHDOG_MS = 5000,
    SAN9_V52_ROOT_KEY_SIZE = 32,
    SAN9_V52_RANDOM_NAME_BYTES = 16,
    SAN9_V52_MAPPING_NAME_MAX = 96,
    SAN9_V52_PROFILE_OFFLINE = 0,
    SAN9_V52_PROFILE_LIVE_OPTIN = 1
};

enum San9V52BootstrapState {
    SAN9_V52_BOOTSTRAP_EMPTY = 0,
    SAN9_V52_BOOTSTRAP_WRITING = 1,
    SAN9_V52_BOOTSTRAP_SEALED = 2,
    SAN9_V52_BOOTSTRAP_CLAIMED = 3,
    SAN9_V52_BOOTSTRAP_INSTALLING = 4,
    SAN9_V52_BOOTSTRAP_COMMITTING = 5,
    SAN9_V52_BOOTSTRAP_REJECTING = 6,
    SAN9_V52_BOOTSTRAP_READY = 7,
    SAN9_V52_BOOTSTRAP_REJECTED = 8,
    SAN9_V52_BOOTSTRAP_STOPPED = 9
};

enum San9V52Result {
    SAN9_V52_OK = 0,
    SAN9_V52_INVALID = 1,
    SAN9_V52_AUTH_FAILED = 2,
    SAN9_V52_BINDING_FAILED = 3,
    SAN9_V52_EXPIRED = 4,
    SAN9_V52_SEQUENCE_FAILED = 5,
    SAN9_V52_TARGET_FAILED = 6,
    SAN9_V52_GENERATION_FAILED = 7,
    SAN9_V52_THREAD_FAILED = 8,
    SAN9_V52_CONFLICT_FAILED = 9,
    SAN9_V52_PIN_FAILED = 10,
    SAN9_V52_SLOT_FAILED = 11,
    SAN9_V52_MAPPING_FAILED = 12,
    SAN9_V52_OFFLINE_ONLY = 13,
    SAN9_V52_TIMEOUT = 14,
    SAN9_V52_INTERNAL_FAILED = 15,
    SAN9_V52_INDETERMINATE = 16,
    SAN9_V52_RESTART_REQUIRED = 17,
    SAN9_V52_CLEANUP_INCOMPLETE = 18
};

typedef struct San9V52BootstrapEnvelope {
    uint32_t magic;
    uint16_t schema_major;
    uint16_t schema_minor;
    uint32_t structure_size;
    uint32_t flags;
    uint64_t sequence;
    uint64_t issued_at_ms;
    uint64_t expires_at_ms;
    uint32_t controller_pid;
    uint32_t target_pid;
    uint32_t target_thread_id;
    uint32_t registered_message;
    uint32_t mapping_atom;
    uint32_t message_tag;
    uint64_t target_creation_time;
    uint64_t controller_creation_time;
    uint32_t exact_app_object;
    uint32_t exact_app_vtable;
    uint32_t exact_idle_slot;
    uint32_t exact_original_idle;
    uint32_t exact_idle_caller;
    uint32_t exact_scheduler_vtable;
    uint32_t exact_controller_vtable;
    uint32_t target_hwnd;
    uint8_t session_nonce[SAN9_PING_NONCE_SIZE];
    uint8_t request_id[SAN9_PING_REQUEST_ID_SIZE];
    uint8_t target_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t context_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t exe_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t dll_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t session_key[SAN9_PING_KEY_SIZE];
    uint8_t mapping_name_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t challenge[SAN9_PING_DIGEST_SIZE];
    uint8_t bootstrap_mac[SAN9_PING_DIGEST_SIZE];
    uint8_t reserved[112];
} San9V52BootstrapEnvelope;

typedef struct San9V52SharedBlock {
    volatile int32_t bootstrap_state;
    volatile int32_t claim_enabled;
    volatile int32_t bootstrap_result;
    volatile int32_t ping_count;
    volatile int32_t controller_acknowledged;
    uint8_t reserved_control[44];
    San9V52BootstrapEnvelope envelope;
    San9PingMailbox mailbox;
    uint8_t reserved_tail[2944];
} San9V52SharedBlock;

typedef struct San9V52BootstrapExpectations {
    uint64_t now_ms;
    uint64_t last_sequence;
    uint64_t target_creation_time;
    uint64_t controller_creation_time;
    uint32_t controller_pid;
    uint32_t target_pid;
    uint32_t target_thread_id;
    uint32_t target_hwnd;
    uint32_t registered_message;
    uint32_t mapping_atom;
    uint32_t message_tag;
    uint8_t target_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t context_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t exe_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t dll_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t mapping_name_digest[SAN9_PING_DIGEST_SIZE];
} San9V52BootstrapExpectations;

typedef struct San9V52Contract {
    uint32_t structure_size;
    uint32_t contract_magic;
    uint32_t compiled_profile;
    uint32_t live_bootstrap_compiled;
    uint32_t default_execution_authorized;
    uint32_t ping_only;
    uint32_t business_fields_present;
    uint32_t hot_unload_allowed;
    uint32_t slot_restore_allowed;
    uint32_t idle_calls_original_exactly_once;
    uint32_t outer_depth_max_ping_count;
    uint32_t bootstrap_envelope_size;
    uint32_t shared_block_size;
    uint32_t ping_frame_size;
    uint32_t exact_app_object;
    uint32_t exact_app_vtable;
    uint32_t exact_idle_slot;
    uint32_t exact_original_idle;
    uint32_t exact_idle_caller;
    uint32_t exact_scheduler_vtable;
    uint32_t exact_controller_vtable;
    uint32_t csharp_bridge_288_compatible;
    uint32_t live_dynamic_safety_proven;
} San9V52Contract;

typedef struct San9V52SelfTestReport {
    uint32_t structure_size;
    uint32_t passed;
    uint32_t failed;
    uint32_t inherited_v51_passed;
    uint32_t bootstrap_tests;
    uint32_t loader_tests;
    uint32_t race_tests;
    uint32_t idle_tests;
    uint32_t original_idle_calls;
    uint32_t authenticated_pings;
    uint32_t slot_installs;
    uint32_t unhook_after_ready;
    uint32_t live_code_executed;
} San9V52SelfTestReport;

int san9_v52_bootstrap_seal(
    San9V52BootstrapEnvelope *envelope,
    const uint8_t root_key[SAN9_V52_ROOT_KEY_SIZE]);

int san9_v52_bootstrap_validate(
    const San9V52BootstrapEnvelope *envelope,
    const uint8_t root_key[SAN9_V52_ROOT_KEY_SIZE],
    const San9V52BootstrapExpectations *expected);

int san9_v52_run_synthetic_tests(San9V52SelfTestReport *report);

uint32_t san9_v52_enter_idle_depth(void);
void san9_v52_after_original(
    void *app,
    int flag,
    uint32_t depth,
    uint32_t caller);

SAN9_EXPORT intptr_t SAN9_STDCALL San9BridgeV52_GetMsgProc(
    int code,
    uintptr_t w_param,
    intptr_t l_param);

SAN9_EXPORT intptr_t SAN9_STDCALL San9BridgeV52_ForegroundIdleProc(
    int code,
    uintptr_t w_param,
    intptr_t l_param);

SAN9_EXPORT int SAN9_FASTCALL San9BridgeV52_IdleBridge(
    void *app,
    void *unused_edx,
    int flag);

SAN9_EXPORT uint32_t San9BridgeV52_Bootstrap(const void *request, uint32_t size);
SAN9_EXPORT uint32_t San9BridgeV52_GetContract(San9V52Contract *contract, uint32_t size);
SAN9_EXPORT uint32_t San9BridgeV52_OfflineSelfTest(
    San9V52SelfTestReport *report,
    uint32_t size);

#ifdef __cplusplus
}
#endif

#endif
