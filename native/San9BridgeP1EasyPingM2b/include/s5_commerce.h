#ifndef SAN9_S5_COMMERCE_H
#define SAN9_S5_COMMERCE_H

#include <stddef.h>
#include <stdint.h>
#include <stdatomic.h>

#ifdef __cplusplus
extern "C" {
#endif

#define SAN9_S5_NO_APPLY_MAGIC UINT32_C(0x354E3953)
#define SAN9_S5_APPLY_ONCE_MAGIC UINT32_C(0x35413953)
#define SAN9_S6_CULTIVATE_APPLY_ONCE_MAGIC UINT32_C(0x36413953)
#define SAN9_S6_PATROL_APPLY_ONCE_MAGIC UINT32_C(0x36503953)
#define SAN9_S6_TRAIN_APPLY_ONCE_MAGIC UINT32_C(0x36543953)
#define SAN9_S6_REPAIR_APPLY_ONCE_MAGIC UINT32_C(0x36523953)
#define SAN9_S5_SCHEMA_MAJOR 1u
#define SAN9_S5_SCHEMA_MINOR 0u
#define SAN9_S5_REQUEST_SIZE 256u
#define SAN9_S5_REQUEST_KIND_NO_APPLY 1u
#define SAN9_S5_REQUEST_KIND_APPLY_ONCE 2u
#define SAN9_S6_REQUEST_KIND_CULTIVATE_APPLY_ONCE 3u
#define SAN9_S6_REQUEST_KIND_PATROL_APPLY_ONCE 4u
#define SAN9_S6_REQUEST_KIND_TRAIN_APPLY_ONCE 5u
#define SAN9_S6_REQUEST_KIND_REPAIR_APPLY_ONCE 6u
#define SAN9_S5_FLAG_CURRENT_CITY_ONLY UINT32_C(0x00000001)
#define SAN9_S5_FLAG_NO_APPLY UINT32_C(0x00000002)
#define SAN9_S5_FLAG_APPLY_ONCE UINT32_C(0x00000004)
#define SAN9_S6_FLAG_CULTIVATE UINT32_C(0x00000008)
#define SAN9_S6_FLAG_PATROL UINT32_C(0x00000010)
#define SAN9_S6_FLAG_TRAIN UINT32_C(0x00000020)
#define SAN9_S6_FLAG_REPAIR UINT32_C(0x00000040)
#define SAN9_S5_COMMERCE_NATIVE_ID 1u
#define SAN9_S5_COMMERCE_EVENT_CODE UINT32_C(0x00002711)
#define SAN9_S6_CULTIVATE_NATIVE_ID 2u
#define SAN9_S6_CULTIVATE_EVENT_CODE UINT32_C(0x00002712)
#define SAN9_S6_PATROL_NATIVE_ID 0u
#define SAN9_S6_PATROL_EVENT_CODE UINT32_C(0x00002710)
#define SAN9_S6_TRAIN_NATIVE_ID 5u
#define SAN9_S6_TRAIN_EVENT_CODE UINT32_C(0x00002715)
#define SAN9_S6_REPAIR_NATIVE_ID 3u
#define SAN9_S6_REPAIR_EVENT_CODE UINT32_C(0x00002713)
#define SAN9_S5_SEQUENCE_ONE 1u
#define SAN9_S5_TOP5_COUNT 5u
#define SAN9_S5_NONCE_SIZE 16u
#define SAN9_S5_DIGEST_SIZE 32u
#define SAN9_S5_HMAC_KEY_SIZE 32u
#define SAN9_S5_MAXIMUM_LIFETIME_MS UINT64_C(5000)

/* S5 is a NO_APPLY contract only.  These values must remain false until a
   separately reviewed stage supplies real native bindings and authorization. */
#if defined(S5_APPLY_ONCE_BUILD) && S5_APPLY_ONCE_BUILD
#define SAN9_S5_APPLY_ONCE_AUTHORIZED 1
#else
#define SAN9_S5_APPLY_ONCE_AUTHORIZED 0
#endif
#if defined(S6_CULTIVATE_APPLY_ONCE_BUILD) && S6_CULTIVATE_APPLY_ONCE_BUILD
#define SAN9_S6_CULTIVATE_APPLY_ONCE_AUTHORIZED 1
#else
#define SAN9_S6_CULTIVATE_APPLY_ONCE_AUTHORIZED 0
#endif
#if defined(S6_PATROL_APPLY_ONCE_BUILD) && S6_PATROL_APPLY_ONCE_BUILD
#define SAN9_S6_PATROL_APPLY_ONCE_AUTHORIZED 1
#else
#define SAN9_S6_PATROL_APPLY_ONCE_AUTHORIZED 0
#endif
#if defined(S6_TRAIN_APPLY_ONCE_BUILD) && S6_TRAIN_APPLY_ONCE_BUILD
#define SAN9_S6_TRAIN_APPLY_ONCE_AUTHORIZED 1
#else
#define SAN9_S6_TRAIN_APPLY_ONCE_AUTHORIZED 0
#endif
#if defined(S6_REPAIR_APPLY_ONCE_BUILD) && S6_REPAIR_APPLY_ONCE_BUILD
#define SAN9_S6_REPAIR_APPLY_ONCE_AUTHORIZED 1
#else
#define SAN9_S6_REPAIR_APPLY_ONCE_AUTHORIZED 0
#endif
#define SAN9_S5_CONTAINS_GAME_FUNCTIONS 0
#define SAN9_S5_CONTAINS_GAME_ADDRESSES 0
#define SAN9_S5_CONTAINS_TARGET_WRITES 0

typedef enum San9S5RequestStatus {
    SAN9_S5_REQUEST_VALID = 0,
    SAN9_S5_REQUEST_INVALID_ARGUMENT = 1,
    SAN9_S5_REQUEST_KEY_INVALID = 2,
    SAN9_S5_REQUEST_SHAPE_INVALID = 3,
    SAN9_S5_REQUEST_RESERVED_NONZERO = 4,
    SAN9_S5_REQUEST_LIFETIME_INVALID = 5,
    SAN9_S5_REQUEST_HMAC_MISMATCH = 6,
    SAN9_S5_REQUEST_BINDING_MISMATCH = 7,
    SAN9_S5_REQUEST_FROM_FUTURE = 8,
    SAN9_S5_REQUEST_EXPIRED = 9
} San9S5RequestStatus;

typedef enum San9S5GateStatus {
    SAN9_S5_GATE_ACCEPTED = 0,
    SAN9_S5_GATE_INVALID_ARGUMENT = 1,
    SAN9_S5_GATE_REQUEST_REJECTED = 2,
    SAN9_S5_GATE_REPLAY = 3,
    SAN9_S5_GATE_CLOCK_ROLLBACK = 4,
    SAN9_S5_GATE_CLOCK_FAULTED = 5,
    SAN9_S5_GATE_CORRUPTED = 6
} San9S5GateStatus;

typedef enum San9S5MachineState {
    SAN9_S5_STATE_EMPTY = 0,
    SAN9_S5_STATE_WRITING = 1,
    SAN9_S5_STATE_SEALED = 2,
    SAN9_S5_STATE_CLAIMED = 3,
    SAN9_S5_STATE_PRECHECKED = 4,
    SAN9_S5_STATE_EVENT_ATTEMPTED = 5,
    SAN9_S5_STATE_SHADOW_ARMED = 6,
    SAN9_S5_STATE_EXECUTE_ENTERED = 7,
    SAN9_S5_STATE_VPTR_RESTORED = 8,
    SAN9_S5_STATE_NO_APPLY_PROVEN = 9,
    SAN9_S5_STATE_REJECTED_PRE_EVENT = 10,
    SAN9_S5_STATE_RESTART_REQUIRED = 11
} San9S5MachineState;

typedef enum San9S5FaultCode {
    SAN9_S5_FAULT_NONE = 0,
    SAN9_S5_FAULT_OUT_OF_ORDER = 1,
    SAN9_S5_FAULT_REQUEST_IDENTITY = 2,
    SAN9_S5_FAULT_PRECHECK = 3,
    SAN9_S5_FAULT_EVENT = 4,
    SAN9_S5_FAULT_SHADOW = 5,
    SAN9_S5_FAULT_EXECUTE = 6,
    SAN9_S5_FAULT_RESTORE = 7,
    SAN9_S5_FAULT_APPLY_OBSERVED = 8,
    SAN9_S5_FAULT_COUNTER_INVARIANT = 9,
    SAN9_S5_FAULT_STATE_CORRUPTION = 10
} San9S5FaultCode;

typedef enum San9S5MachineResult {
    SAN9_S5_MACHINE_OK = 0,
    SAN9_S5_MACHINE_INVALID_ARGUMENT = 1,
    SAN9_S5_MACHINE_REJECTED_PRE_EVENT = 2,
    SAN9_S5_MACHINE_RESTART_REQUIRED = 3
} San9S5MachineResult;

/* Frozen little-endian x86 request image.  It contains semantic identifiers,
   never pointers, code addresses, callbacks, or write destinations. */
typedef struct San9S5NoApplyRequest {
    uint32_t magic;
    uint16_t schema_major;
    uint16_t schema_minor;
    uint32_t declared_size;
    uint32_t kind;
    uint32_t flags;
    uint32_t native_command_id;
    uint32_t event_code;
    uint32_t sequence;
    uint32_t expected_city_id;
    uint32_t expected_corps_id;
    uint32_t exact_person_count;
    uint32_t reserved_header;
    uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT];
    uint32_t reserved_alignment;
    uint64_t issued_at_ms;
    uint64_t expires_at_ms;
    uint8_t request_nonce[SAN9_S5_NONCE_SIZE];
    uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t reserved[SAN9_S5_REQUEST_SIZE - 200u];
    uint8_t hmac[SAN9_S5_DIGEST_SIZE];
} San9S5NoApplyRequest;

/* Gate ownership is single-threaded.  It accepts exactly one authenticated
   request for one P1 binding and permanently faults on clock rollback. */
typedef struct San9S5NoApplyGate {
    uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t accepted_nonce[SAN9_S5_NONCE_SIZE];
    uint8_t accepted_request_digest[SAN9_S5_DIGEST_SIZE];
    uint64_t last_observed_now_ms;
    uint32_t accepted_count;
    uint32_t has_observed_time;
    uint32_t clock_faulted;
} San9S5NoApplyGate;

/* The controller only publishes.  One target-side owner claims and advances
   the remaining states.  EVENT_ATTEMPTED is the irreversible fault boundary. */
typedef struct San9S5NoApplyMachine {
    _Atomic(uint32_t) state;
    _Atomic(uint32_t) first_fault;
    _Atomic(uint32_t) event_attempt_count;
    _Atomic(uint32_t) shadow_arm_count;
    _Atomic(uint32_t) execute_enter_count;
    _Atomic(uint32_t) restore_count;
    _Atomic(uint32_t) observed_apply_count;
    uint8_t request_nonce[SAN9_S5_NONCE_SIZE];
    uint8_t request_digest[SAN9_S5_DIGEST_SIZE];
} San9S5NoApplyMachine;

_Static_assert(sizeof(San9S5NoApplyRequest) == SAN9_S5_REQUEST_SIZE,
    "S5 request ABI must be exactly 256 bytes");
_Static_assert(offsetof(San9S5NoApplyRequest, expected_city_id) == 32u,
    "S5 expected-city offset changed");
_Static_assert(offsetof(San9S5NoApplyRequest, expected_person_ids) == 48u,
    "S5 top-five offset changed");
_Static_assert(offsetof(San9S5NoApplyRequest, issued_at_ms) == 72u,
    "S5 issued-at offset changed");
_Static_assert(offsetof(San9S5NoApplyRequest, request_nonce) == 88u,
    "S5 nonce offset changed");
_Static_assert(offsetof(San9S5NoApplyRequest, p1_binding_digest) == 104u,
    "S5 P1-binding offset changed");
_Static_assert(offsetof(San9S5NoApplyRequest, precondition_digest) == 136u,
    "S5 precondition offset changed");
_Static_assert(offsetof(San9S5NoApplyRequest, reserved) == 168u,
    "S5 reserved offset changed");
_Static_assert(offsetof(San9S5NoApplyRequest, hmac) == 224u,
    "S5 HMAC offset changed");
_Static_assert(sizeof(_Atomic(uint32_t)) == 4u,
    "S5 requires four-byte C11 atomic uint32");
_Static_assert(ATOMIC_INT_LOCK_FREE == 2,
    "S5 requires always-lock-free 32-bit atomics");

San9S5RequestStatus san9_s5_no_apply_request_initialize(
    San9S5NoApplyRequest *request,
    uint32_t expected_city_id,
    uint32_t expected_corps_id,
    const uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT],
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_nonce[SAN9_S5_NONCE_SIZE],
    const uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    const uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE]);

San9S5RequestStatus san9_s5_no_apply_request_sign(
    San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size);

San9S5RequestStatus san9_s5_no_apply_request_verify(
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    uint64_t now_ms);

San9S5RequestStatus san9_s5_apply_once_request_initialize(
    San9S5NoApplyRequest *request,
    uint32_t expected_city_id,
    uint32_t expected_corps_id,
    const uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT],
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_nonce[SAN9_S5_NONCE_SIZE],
    const uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    const uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE]);

San9S5RequestStatus san9_s5_apply_once_request_sign(
    San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size);

San9S5RequestStatus san9_s5_apply_once_request_verify(
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    uint64_t now_ms);

San9S5RequestStatus san9_s6_cultivate_apply_once_request_initialize(
    San9S5NoApplyRequest *request,
    uint32_t expected_city_id,
    uint32_t expected_corps_id,
    const uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT],
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_nonce[SAN9_S5_NONCE_SIZE],
    const uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    const uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE]);

San9S5RequestStatus san9_s6_cultivate_apply_once_request_sign(
    San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size);

San9S5RequestStatus san9_s6_cultivate_apply_once_request_verify(
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    uint64_t now_ms);

San9S5RequestStatus san9_s6_patrol_apply_once_request_initialize(
    San9S5NoApplyRequest *request,
    uint32_t expected_city_id,
    uint32_t expected_corps_id,
    const uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT],
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_nonce[SAN9_S5_NONCE_SIZE],
    const uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    const uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE]);

San9S5RequestStatus san9_s6_patrol_apply_once_request_sign(
    San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size);

San9S5RequestStatus san9_s6_patrol_apply_once_request_verify(
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    uint64_t now_ms);

San9S5RequestStatus san9_s6_train_apply_once_request_initialize(
    San9S5NoApplyRequest *request,
    uint32_t expected_city_id,
    uint32_t expected_corps_id,
    const uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT],
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_nonce[SAN9_S5_NONCE_SIZE],
    const uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    const uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE]);

San9S5RequestStatus san9_s6_train_apply_once_request_sign(
    San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size);

San9S5RequestStatus san9_s6_train_apply_once_request_verify(
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    uint64_t now_ms);

San9S5RequestStatus san9_s6_repair_apply_once_request_initialize(
    San9S5NoApplyRequest *request,
    uint32_t expected_city_id,
    uint32_t expected_corps_id,
    const uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT],
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_nonce[SAN9_S5_NONCE_SIZE],
    const uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    const uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE]);

San9S5RequestStatus san9_s6_repair_apply_once_request_sign(
    San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size);

San9S5RequestStatus san9_s6_repair_apply_once_request_verify(
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    uint64_t now_ms);

int san9_s5_apply_once_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE]);

int san9_s6_cultivate_apply_once_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE]);

int san9_s6_patrol_apply_once_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE]);

int san9_s6_train_apply_once_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE]);

int san9_s6_repair_apply_once_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE]);

int san9_s5_no_apply_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE]);

int san9_s5_no_apply_gate_initialize(
    San9S5NoApplyGate *gate,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE]);

San9S5GateStatus san9_s5_no_apply_gate_accept(
    San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status);

San9S5GateStatus san9_s5_apply_once_gate_accept(
    San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status);

San9S5GateStatus san9_s6_cultivate_apply_once_gate_accept(
    San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status);

San9S5GateStatus san9_s6_patrol_apply_once_gate_accept(
    San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status);

San9S5GateStatus san9_s6_train_apply_once_gate_accept(
    San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status);

San9S5GateStatus san9_s6_repair_apply_once_gate_accept(
    San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status);

int san9_s5_no_apply_machine_initialize(San9S5NoApplyMachine *machine);

San9S5MachineResult san9_s5_no_apply_machine_publish(
    San9S5NoApplyMachine *machine,
    const San9S5NoApplyRequest *request);

San9S5MachineResult san9_s5_no_apply_machine_claim_authenticated(
    San9S5NoApplyMachine *machine,
    const San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request);

San9S5MachineResult san9_s5_no_apply_machine_mark_prechecked(
    San9S5NoApplyMachine *machine);
San9S5MachineResult san9_s5_no_apply_machine_mark_event_attempted(
    San9S5NoApplyMachine *machine);
San9S5MachineResult san9_s5_no_apply_machine_mark_shadow_armed(
    San9S5NoApplyMachine *machine);
San9S5MachineResult san9_s5_no_apply_machine_mark_execute_entered(
    San9S5NoApplyMachine *machine);
San9S5MachineResult san9_s5_no_apply_machine_mark_vptr_restored(
    San9S5NoApplyMachine *machine);
San9S5MachineResult san9_s5_no_apply_machine_prove_no_apply(
    San9S5NoApplyMachine *machine);
San9S5MachineResult san9_s5_no_apply_machine_fail(
    San9S5NoApplyMachine *machine,
    San9S5FaultCode fault);
San9S5MachineResult san9_s5_no_apply_machine_note_apply_observed(
    San9S5NoApplyMachine *machine);

San9S5MachineState san9_s5_no_apply_machine_state(
    const San9S5NoApplyMachine *machine);
San9S5FaultCode san9_s5_no_apply_machine_first_fault(
    const San9S5NoApplyMachine *machine);

#ifdef __cplusplus
}
#endif

#endif
