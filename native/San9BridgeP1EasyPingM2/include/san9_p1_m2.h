#ifndef SAN9_P1_M2_H
#define SAN9_P1_M2_H

#include <stddef.h>
#include <stdint.h>
#include <stdatomic.h>

#include "san9_p1_easy_gate.h"
#include "san9_p1_wire.h"

#ifdef __cplusplus
extern "C" {
#endif

#define SAN9_P1_M2_MAILBOX_MAGIC UINT32_C(0x324D3953)
#define SAN9_P1_M2_SCHEMA_MAJOR 1u
#define SAN9_P1_M2_SCHEMA_MINOR 0u
#define SAN9_P1_M2_MAILBOX_SIZE 4096u
#define SAN9_P1_M2_RUNTIME_STORAGE_SIZE 4096u
#define SAN9_P1_M2_LIVE_AUTHORIZATION 0
#define SAN9_P1_M2_CONTAINS_BUSINESS_FIELDS 0
#define SAN9_P1_M2_SYSTEM_ACTIONS_ARE_FAKE_CALLBACKS 1
#define SAN9_P1_M2_MAXIMUM_ROUNDS SAN9_P1_MAXIMUM_SESSION_PINGS

#define SAN9_P1_M2_LIFECYCLE_OFFSET 16u
#define SAN9_P1_M2_REQUEST_SLOT_STATE_OFFSET 20u
#define SAN9_P1_M2_RESPONSE_SLOT_STATE_OFFSET 24u
#define SAN9_P1_M2_POISON_CODE_OFFSET 28u
#define SAN9_P1_M2_ACCEPTED_PING_COUNT_OFFSET 32u
#define SAN9_P1_M2_CONTROLLER_ACK_OFFSET 36u
#define SAN9_P1_M2_HMAC_KEY_OFFSET 64u
#define SAN9_P1_M2_REQUEST_FRAME_OFFSET 512u
#define SAN9_P1_M2_RESPONSE_FRAME_OFFSET 1024u
#define SAN9_P1_M2_RESERVED_TAIL_OFFSET 1536u

typedef enum San9P1M2LifecycleState {
    SAN9_P1_M2_LIFECYCLE_EMPTY = 0,
    SAN9_P1_M2_LIFECYCLE_WRITING = 1,
    SAN9_P1_M2_LIFECYCLE_SEALED = 2,
    SAN9_P1_M2_LIFECYCLE_CLAIMED = 3,
    SAN9_P1_M2_LIFECYCLE_VALIDATING = 4,
    SAN9_P1_M2_LIFECYCLE_COMMITTING = 5,
    SAN9_P1_M2_LIFECYCLE_READY = 6,
    SAN9_P1_M2_LIFECYCLE_QUIESCENT_PINNED = 7,
    SAN9_P1_M2_LIFECYCLE_STOPPED_PRECOMMIT = 8,
    SAN9_P1_M2_LIFECYCLE_REJECTED_PRECOMMIT = 9,
    SAN9_P1_M2_LIFECYCLE_POISONED_RESTART = 10
} San9P1M2LifecycleState;

typedef enum San9P1M2SlotState {
    SAN9_P1_M2_SLOT_EMPTY = 0,
    SAN9_P1_M2_SLOT_WRITING = 1,
    SAN9_P1_M2_SLOT_READY = 2,
    SAN9_P1_M2_SLOT_CLAIMED = 3,
    SAN9_P1_M2_SLOT_COMPLETE = 4,
    SAN9_P1_M2_SLOT_CONSUMED = 5
} San9P1M2SlotState;

typedef enum San9P1M2PoisonCode {
    SAN9_P1_M2_POISON_NONE = 0,
    SAN9_P1_M2_POISON_POSTCOMMIT_ACTION = 1,
    SAN9_P1_M2_POISON_OWNER_MISMATCH = 2,
    SAN9_P1_M2_POISON_STATE_CORRUPTION = 3,
    SAN9_P1_M2_POISON_CLOCK_FAULT = 4,
    SAN9_P1_M2_POISON_RESPONSE_AUTH = 5,
    SAN9_P1_M2_POISON_RESULT_DIGEST = 6,
    SAN9_P1_M2_POISON_LATE_RESPONSE = 7,
    SAN9_P1_M2_POISON_MAILBOX_CORRUPTION = 8
} San9P1M2PoisonCode;

typedef enum San9P1M2Status {
    SAN9_P1_M2_OK = 0,
    SAN9_P1_M2_INVALID_ARGUMENT = 1,
    SAN9_P1_M2_ABI_REJECTED = 2,
    SAN9_P1_M2_STATE_REJECTED = 3,
    SAN9_P1_M2_CAS_LOST = 4,
    SAN9_P1_M2_CANCELLED_PRECOMMIT = 5,
    SAN9_P1_M2_REJECTED_PRECOMMIT = 6,
    SAN9_P1_M2_POISONED = 7,
    SAN9_P1_M2_OWNER_MISMATCH = 8,
    SAN9_P1_M2_EASY_REJECTED = 9,
    SAN9_P1_M2_ACTION_REJECTED = 10,
    SAN9_P1_M2_SLOT_BUSY = 11,
    SAN9_P1_M2_AUTH_REJECTED = 12,
    SAN9_P1_M2_GATE_REJECTED = 13,
    SAN9_P1_M2_NO_PENDING = 14,
    SAN9_P1_M2_LATE_RESPONSE = 15,
    SAN9_P1_M2_RESULT_REJECTED = 16,
    SAN9_P1_M2_BUDGET_EXHAUSTED = 17,
    SAN9_P1_M2_QUIESCENT = 18
} San9P1M2Status;

typedef enum San9P1M2FakeActionKind {
    SAN9_P1_M2_FAKE_PRECOMMIT_VALIDATE = 1,
    SAN9_P1_M2_FAKE_BEFORE_COMMIT_CAS = 2,
    SAN9_P1_M2_FAKE_COMMIT_SIDE_EFFECT = 3,
    SAN9_P1_M2_FAKE_PING = 4
} San9P1M2FakeActionKind;

typedef struct San9P1M2ResultEvidence {
    uint32_t caller;
    uint8_t easy_snapshot_digest[SAN9_P1_DIGEST_SIZE];
} San9P1M2ResultEvidence;

typedef int (*San9P1M2FakeActionCallback)(
    void *context,
    San9P1M2FakeActionKind action,
    const San9P1Frame *authenticated_request,
    San9P1M2ResultEvidence *result_evidence);

typedef struct San9P1M2FakeActions {
    San9P1M2FakeActionCallback invoke;
    void *context;
} San9P1M2FakeActions;

typedef struct San9P1M2Mailbox {
    uint32_t magic;
    uint16_t schema_major;
    uint16_t schema_minor;
    uint32_t declared_size;
    uint32_t reserved_header;
    _Atomic(uint32_t) lifecycle_state;
    _Atomic(uint32_t) request_slot_state;
    _Atomic(uint32_t) response_slot_state;
    _Atomic(uint32_t) poison_code;
    _Atomic(uint32_t) accepted_ping_count;
    _Atomic(uint32_t) controller_ack;
    uint8_t reserved_to_key[24];
    uint8_t hmac_key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t reserved_to_request[416];
    uint8_t request_frame[SAN9_P1_FRAME_SIZE];
    uint8_t response_frame[SAN9_P1_FRAME_SIZE];
    uint8_t reserved_tail[2560];
} San9P1M2Mailbox;

typedef union San9P1M2RuntimeStorage {
    max_align_t alignment;
    uint8_t bytes[SAN9_P1_M2_RUNTIME_STORAGE_SIZE];
} San9P1M2RuntimeStorage;

_Static_assert(sizeof(_Atomic(uint32_t)) == 4u, "M2 requires four-byte C11 atomic uint32");
_Static_assert(ATOMIC_INT_LOCK_FREE == 2, "M2 requires always-lock-free 32-bit atomics");
_Static_assert(_Alignof(San9P1M2Mailbox) >= _Alignof(_Atomic(uint32_t)),
    "mailbox alignment is insufficient for C11 atomics");
_Static_assert(offsetof(San9P1M2Mailbox, lifecycle_state) == SAN9_P1_M2_LIFECYCLE_OFFSET,
    "lifecycle ABI offset changed");
_Static_assert(offsetof(San9P1M2Mailbox, request_slot_state) == SAN9_P1_M2_REQUEST_SLOT_STATE_OFFSET,
    "request slot ABI offset changed");
_Static_assert(offsetof(San9P1M2Mailbox, response_slot_state) == SAN9_P1_M2_RESPONSE_SLOT_STATE_OFFSET,
    "response slot ABI offset changed");
_Static_assert(offsetof(San9P1M2Mailbox, poison_code) == SAN9_P1_M2_POISON_CODE_OFFSET,
    "poison ABI offset changed");
_Static_assert(offsetof(San9P1M2Mailbox, accepted_ping_count) == SAN9_P1_M2_ACCEPTED_PING_COUNT_OFFSET,
    "ping count ABI offset changed");
_Static_assert(offsetof(San9P1M2Mailbox, controller_ack) == SAN9_P1_M2_CONTROLLER_ACK_OFFSET,
    "ack ABI offset changed");
_Static_assert(offsetof(San9P1M2Mailbox, hmac_key) == SAN9_P1_M2_HMAC_KEY_OFFSET,
    "key ABI offset changed");
_Static_assert(offsetof(San9P1M2Mailbox, request_frame) == SAN9_P1_M2_REQUEST_FRAME_OFFSET,
    "request frame ABI offset changed");
_Static_assert(offsetof(San9P1M2Mailbox, response_frame) == SAN9_P1_M2_RESPONSE_FRAME_OFFSET,
    "response frame ABI offset changed");
_Static_assert(offsetof(San9P1M2Mailbox, reserved_tail) == SAN9_P1_M2_RESERVED_TAIL_OFFSET,
    "tail ABI offset changed");
_Static_assert(sizeof(San9P1M2Mailbox) == SAN9_P1_M2_MAILBOX_SIZE,
    "mailbox ABI must be exactly 4096 bytes");
_Static_assert(sizeof(San9P1M2RuntimeStorage) == SAN9_P1_M2_RUNTIME_STORAGE_SIZE,
    "runtime opaque storage ABI changed");
_Static_assert(SAN9_P1_FRAME_SIZE == 512u, "M2 requires the frozen 512-byte wire");
_Static_assert(SAN9_P1_LIVE_AUTHORIZATION == 0, "wire live authorization must remain false");

int san9_p1_m2_mailbox_initialize(
    San9P1M2Mailbox *mailbox,
    const uint8_t *hmac_key,
    size_t hmac_key_size);

int san9_p1_m2_mailbox_validate(const San9P1M2Mailbox *mailbox);

int san9_p1_m2_runtime_initialize(
    San9P1M2RuntimeStorage *storage,
    San9P1M2Mailbox *mailbox);

San9P1M2LifecycleState san9_p1_m2_lifecycle_load(
    const San9P1M2RuntimeStorage *storage);

San9P1M2Status san9_p1_m2_lifecycle_begin_write(
    San9P1M2RuntimeStorage *storage);

San9P1M2Status san9_p1_m2_lifecycle_seal(
    San9P1M2RuntimeStorage *storage);

San9P1M2Status san9_p1_m2_lifecycle_cancel_precommit(
    San9P1M2RuntimeStorage *storage);

San9P1M2Status san9_p1_m2_lifecycle_validate_and_commit(
    San9P1M2RuntimeStorage *storage,
    const San9P1EasyBinding *easy_binding,
    const San9P1EasyCallbacks *easy_callbacks,
    const San9P1M2FakeActions *fake_actions,
    uint64_t commit_owner_token);

San9P1M2Status san9_p1_m2_lifecycle_poison(
    San9P1M2RuntimeStorage *storage,
    San9P1M2PoisonCode poison_code,
    uint64_t commit_owner_token);

San9P1M2Status san9_p1_m2_session_bind_authenticated(
    San9P1M2RuntimeStorage *storage,
    const uint8_t *binding_frame,
    size_t binding_frame_size,
    San9P1DecodeStatus *decode_status);

San9P1M2Status san9_p1_m2_controller_publish_request(
    San9P1M2RuntimeStorage *storage,
    const uint8_t *request_frame,
    size_t request_frame_size,
    uint64_t *round_token);

San9P1M2Status san9_p1_m2_target_process_request(
    San9P1M2RuntimeStorage *storage,
    uint64_t round_token,
    uint64_t now_ms,
    const San9P1M2FakeActions *fake_actions,
    San9P1DecodeStatus *decode_status,
    San9P1GateStatus *gate_status);

San9P1M2Status san9_p1_m2_controller_discard_failed_request(
    San9P1M2RuntimeStorage *storage,
    uint64_t round_token);

San9P1M2Status san9_p1_m2_controller_consume_response(
    San9P1M2RuntimeStorage *storage,
    uint64_t round_token,
    San9P1Frame *verified_response,
    San9P1DecodeStatus *decode_status);

int san9_p1_m2_result_digest(
    const San9P1Frame *authenticated_request,
    uint32_t caller,
    const uint8_t easy_snapshot_digest[SAN9_P1_DIGEST_SIZE],
    uint32_t ping_ordinal,
    uint8_t output[SAN9_P1_DIGEST_SIZE]);

#ifdef __cplusplus
}
#endif

#endif
