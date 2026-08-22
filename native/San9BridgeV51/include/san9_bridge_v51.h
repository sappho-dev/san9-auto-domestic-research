#ifndef SAN9_BRIDGE_V51_H
#define SAN9_BRIDGE_V51_H

#include <stddef.h>
#include <stdint.h>

#if defined(_MSC_VER)
#define SAN9_STDCALL __stdcall
#define SAN9_FASTCALL __fastcall
#define SAN9_THISCALL __thiscall
#if defined(SAN9_EXPORTS_VIA_DEF)
#define SAN9_EXPORT
#else
#define SAN9_EXPORT __declspec(dllexport)
#endif
#elif defined(__GNUC__) || defined(__clang__)
#define SAN9_STDCALL __attribute__((stdcall))
#define SAN9_FASTCALL __attribute__((fastcall))
#define SAN9_THISCALL __attribute__((thiscall))
#if defined(SAN9_EXPORTS_VIA_DEF)
#define SAN9_EXPORT
#else
#define SAN9_EXPORT __declspec(dllexport)
#endif
#else
#error Unsupported x86 Windows compiler
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum {
    /* Independent ping-bootstrap proof protocol. It is not the C# 288-byte
       Bridge.Protocol schema and is never a production business envelope. */
    SAN9_PING_FRAME_SIZE = 256,
    SAN9_PING_MAILBOX_SIZE = 576,
    SAN9_PING_SESSION_SIZE = 144,
    SAN9_PING_KEY_SIZE = 32,
    SAN9_PING_NONCE_SIZE = 16,
    SAN9_PING_REQUEST_ID_SIZE = 16,
    SAN9_PING_DIGEST_SIZE = 32,
    SAN9_PING_MAX_LIFETIME_MS = 5000,
    SAN9_PING_PROOF_CONTRACT_MAGIC = 0x31504753, /* "SGP1" */
    SAN9_PROTOCOL_ROLE_PING_BOOTSTRAP_PROOF = 1,

    SAN9_EXACT_APP_OBJECT = 0x01228340,
    SAN9_EXACT_APP_VTABLE = 0x00604DD0,
    SAN9_EXACT_IDLE_SLOT = 0x00604DF4,
    SAN9_EXACT_ORIGINAL_IDLE = 0x00434100,
    SAN9_EXACT_IDLE_CALLER = 0x005C5D11,
    SAN9_EXACT_SCHEDULER_VTABLE = 0x00607560,
    SAN9_EXACT_CONTROLLER_VTABLE = 0x00610BC8
};

enum San9MailboxState {
    SAN9_MAILBOX_EMPTY = 0,
    SAN9_MAILBOX_READY = 1,
    SAN9_MAILBOX_CLAIMED = 2,
    SAN9_MAILBOX_COMPLETE = 3,
    SAN9_MAILBOX_WRITING = 4,
    SAN9_MAILBOX_REJECTED_UNAUTHENTICATED = 5,
    SAN9_MAILBOX_SESSION_FAULT = 6
};

enum San9PingResult {
    SAN9_PING_OK = 0,
    SAN9_PING_INVALID_FRAME = 1,
    SAN9_PING_AUTH_FAILED = 2,
    SAN9_PING_BINDING_MISMATCH = 3,
    SAN9_PING_EXPIRED = 4,
    SAN9_PING_FROM_FUTURE = 5,
    SAN9_PING_LIFETIME_INVALID = 6,
    SAN9_PING_SEQUENCE_REPLAY = 7,
    SAN9_PING_SEQUENCE_GAP = 8,
    SAN9_PING_GATE_REJECTED = 9,
    SAN9_PING_MAILBOX_BUSY = 10,
    SAN9_PING_RESPONSE_INVALID = 11,
    SAN9_PING_OFFLINE_ONLY = 12,
    SAN9_PING_PIN_FAILED = 13,
    SAN9_PING_CLOCK_ROLLBACK = 14
};

typedef struct San9PingMailbox {
    volatile int32_t request_state;
    volatile int32_t response_state;
    uint8_t reserved_control[56];
    uint8_t request_frame[SAN9_PING_FRAME_SIZE];
    uint8_t response_frame[SAN9_PING_FRAME_SIZE];
} San9PingMailbox;

typedef struct San9PingSession {
    uint8_t key[SAN9_PING_KEY_SIZE];
    uint8_t session_nonce[SAN9_PING_NONCE_SIZE];
    uint8_t target_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t context_digest[SAN9_PING_DIGEST_SIZE];
    uint64_t last_accepted_sequence;
    uint64_t last_observed_time_ms;
    uint32_t expected_thread_id;
    uint32_t expected_idle_bridge;
    uint32_t initialized;
    uint32_t clock_faulted;
} San9PingSession;

typedef struct San9IdleGateSnapshot {
    uint32_t caller;
    uint32_t app_object;
    uint32_t thread_id;
    uint32_t slot_address;
    uint32_t slot_value;
    uint32_t app_vptr;
    int32_t idle_argument;
    uint32_t exact_target_verified;
    uint32_t conflict_free;
    uint32_t process_generation_stable;
} San9IdleGateSnapshot;

typedef struct San9BridgeContract {
    uint32_t structure_size;
    uint32_t contract_magic;
    uint16_t schema_major;
    uint16_t schema_minor;
    uint32_t ping_frame_size;
    uint32_t mailbox_size;
    uint32_t session_size;
    uint32_t protocol_role;
    uint32_t ping_only;
    uint32_t production_business_protocol_compatible;
    uint32_t csharp_bridge_288_compatible;
    uint32_t unauthenticated_recovery_requires_ack;
    uint32_t trusted_monotonic_time_source_required;
    uint32_t clock_rollback_requires_new_session;
    uint32_t execution_authorized;
    uint32_t hot_unload_allowed;
    uint32_t live_bootstrap_enabled;
    uint32_t exact_app_object;
    uint32_t exact_app_vtable;
    uint32_t exact_idle_slot;
    uint32_t exact_original_idle;
    uint32_t exact_idle_caller;
    uint32_t exact_scheduler_vtable;
    uint32_t exact_controller_vtable;
} San9BridgeContract;

typedef struct San9BridgeSelfTestReport {
    uint32_t structure_size;
    uint32_t passed;
    uint32_t failed;
    uint32_t module_pinned;
    uint32_t ping_only;
    uint32_t execution_authorized;
    uint32_t live_bootstrap_enabled;
    uint32_t original_idle_calls;
    uint32_t authenticated_pings;
    uint32_t rejected_pings;
} San9BridgeSelfTestReport;

typedef int (SAN9_THISCALL *San9OriginalIdle)(void *app, int flag);

int san9_ping_session_initialize(
    San9PingSession *session,
    const uint8_t key[SAN9_PING_KEY_SIZE],
    const uint8_t session_nonce[SAN9_PING_NONCE_SIZE],
    const uint8_t target_digest[SAN9_PING_DIGEST_SIZE],
    const uint8_t context_digest[SAN9_PING_DIGEST_SIZE],
    uint32_t expected_thread_id,
    uint32_t expected_idle_bridge);

void san9_ping_session_clear(San9PingSession *session);
void san9_ping_mailbox_initialize(San9PingMailbox *mailbox);

int san9_ping_acknowledge_rejected_request(San9PingMailbox *mailbox);

int san9_ping_reset_clock_fault(
    San9PingSession *session,
    San9PingMailbox *mailbox,
    const uint8_t key[SAN9_PING_KEY_SIZE],
    const uint8_t new_session_nonce[SAN9_PING_NONCE_SIZE],
    const uint8_t target_digest[SAN9_PING_DIGEST_SIZE],
    const uint8_t context_digest[SAN9_PING_DIGEST_SIZE],
    uint32_t expected_thread_id,
    uint32_t expected_idle_bridge);

int san9_ping_make_request(
    const San9PingSession *session,
    uint64_t sequence,
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_id[SAN9_PING_REQUEST_ID_SIZE],
    const uint8_t challenge[SAN9_PING_DIGEST_SIZE],
    uint8_t output[SAN9_PING_FRAME_SIZE]);

int san9_ping_publish_request(
    San9PingMailbox *mailbox,
    const uint8_t request[SAN9_PING_FRAME_SIZE]);

int san9_ping_process_one(
    San9PingSession *session,
    San9PingMailbox *mailbox,
    uint64_t now_ms,
    const San9IdleGateSnapshot *snapshot);

int san9_ping_take_response(
    San9PingMailbox *mailbox,
    uint8_t output[SAN9_PING_FRAME_SIZE]);

int san9_ping_verify_response(
    const San9PingSession *session,
    const uint8_t request[SAN9_PING_FRAME_SIZE],
    const uint8_t response[SAN9_PING_FRAME_SIZE]);

int san9_idle_gate_validate(
    const San9PingSession *session,
    const San9IdleGateSnapshot *snapshot);

int san9_protocol_run_self_tests(San9BridgeSelfTestReport *report);

SAN9_EXPORT intptr_t SAN9_STDCALL San9Bridge_GetMsgProc(
    int code,
    uintptr_t w_param,
    intptr_t l_param);

SAN9_EXPORT intptr_t SAN9_STDCALL San9Bridge_ForegroundIdleProc(
    int code,
    uintptr_t w_param,
    intptr_t l_param);

SAN9_EXPORT int SAN9_FASTCALL San9Bridge_IdleBridge(
    void *app,
    void *unused_edx,
    int flag);

SAN9_EXPORT uint32_t San9Bridge_Bootstrap(const void *request, uint32_t request_size);
SAN9_EXPORT uint32_t San9Bridge_GetContract(San9BridgeContract *contract, uint32_t size);
SAN9_EXPORT uint32_t San9Bridge_OfflineSelfTest(
    San9BridgeSelfTestReport *report,
    uint32_t size);

#ifdef __cplusplus
}
#endif

#endif
