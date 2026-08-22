#include "san9_p1_m2.h"
#include "sha256.h"

#include <limits.h>
#include <string.h>

#if !defined(__i386__) && !defined(_M_IX86)
#error "San9 P1 M2a is frozen to x86"
#endif

#if defined(__BYTE_ORDER__) && (__BYTE_ORDER__ != __ORDER_LITTLE_ENDIAN__)
#error "San9 P1 M2a requires a little-endian target"
#endif

#define SAN9_P1_M2_RUNTIME_MAGIC UINT32_C(0x52323953)
#define SAN9_P1_M2_OWNER_DOMAIN "SAN9-P1-M2-OWNER-v1"
#define SAN9_P1_M2_RESULT_DOMAIN "SAN9-P1-RESULT-v1"

typedef enum San9P1M2RoundOutcome {
    SAN9_P1_M2_ROUND_NONE = 0,
    SAN9_P1_M2_ROUND_DISCARD = 1,
    SAN9_P1_M2_ROUND_RESPONSE = 2
} San9P1M2RoundOutcome;

typedef enum San9P1M2ProcessingOwner {
    SAN9_P1_M2_PROCESSING_NONE = 0,
    SAN9_P1_M2_PROCESSING_TARGET = 1,
    SAN9_P1_M2_PROCESSING_CONTROLLER = 2,
    SAN9_P1_M2_PROCESSING_POISON = 3
} San9P1M2ProcessingOwner;

typedef struct San9P1M2Runtime {
    uint32_t magic;
    uint32_t session_bound;
    San9P1M2Mailbox *mailbox;
    San9P1RequestGate request_gate;
    San9P1Frame pending_request;
    uint8_t expected_result_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t owner_verifier[SAN9_P1_DIGEST_SIZE];
    _Atomic(uint32_t) active_round_token;
    _Atomic(uint32_t) round_outcome;
    _Atomic(uint32_t) processing_owner;
    uint32_t owner_set;
} San9P1M2Runtime;

_Static_assert(sizeof(void *) == 4u, "M2a runtime ABI requires x86 pointers");
_Static_assert(sizeof(San9P1M2Runtime) <= SAN9_P1_M2_RUNTIME_STORAGE_SIZE,
    "opaque runtime storage is too small");
_Static_assert(_Alignof(San9P1M2RuntimeStorage) >= _Alignof(San9P1M2Runtime),
    "opaque runtime storage alignment is too small");

static int bytes_zero(const uint8_t *bytes, size_t size)
{
    uint8_t aggregate = 0u;
    size_t index;
    if (bytes == NULL) {
        return 0;
    }
    for (index = 0u; index < size; ++index) {
        aggregate = (uint8_t)(aggregate | bytes[index]);
    }
    return aggregate == 0u;
}

static int ranges_overlap(
    const void *left,
    size_t left_size,
    const void *right,
    size_t right_size)
{
    uintptr_t left_start;
    uintptr_t right_start;
    uintptr_t left_end;
    uintptr_t right_end;
    if (left == NULL || right == NULL || left_size == 0u || right_size == 0u) {
        return 0;
    }
    left_start = (uintptr_t)left;
    right_start = (uintptr_t)right;
    if (left_start > UINTPTR_MAX - left_size
        || right_start > UINTPTR_MAX - right_size) {
        return 1;
    }
    left_end = left_start + left_size;
    right_end = right_start + right_size;
    return left_start < right_end && right_start < left_end;
}

static int pointer_aligned(const void *pointer, size_t alignment)
{
    return pointer != NULL && alignment != 0u
        && (uintptr_t)pointer % alignment == 0u;
}

static int key_valid(const uint8_t *key, size_t size)
{
    return key != NULL && size == SAN9_P1_HMAC_KEY_SIZE
        && !bytes_zero(key, size);
}

static void write_u32_le(uint8_t output[4], uint32_t value)
{
    output[0] = (uint8_t)value;
    output[1] = (uint8_t)(value >> 8);
    output[2] = (uint8_t)(value >> 16);
    output[3] = (uint8_t)(value >> 24);
}

static void write_u64_le(uint8_t output[8], uint64_t value)
{
    size_t index;
    for (index = 0u; index < 8u; ++index) {
        output[index] = (uint8_t)(value >> (index * 8u));
    }
}

static int lifecycle_value_valid(uint32_t value)
{
    return value <= (uint32_t)SAN9_P1_M2_LIFECYCLE_POISONED_RESTART;
}

static int slot_value_valid(uint32_t value)
{
    return value <= (uint32_t)SAN9_P1_M2_SLOT_CONSUMED;
}

static int poison_value_valid(uint32_t value)
{
    return value <= (uint32_t)SAN9_P1_M2_POISON_MAILBOX_CORRUPTION;
}

static int mailbox_header_valid(const San9P1M2Mailbox *mailbox)
{
    if (!pointer_aligned(mailbox, _Alignof(San9P1M2Mailbox))) {
        return 0;
    }
    return mailbox->magic == SAN9_P1_M2_MAILBOX_MAGIC
        && mailbox->schema_major == SAN9_P1_M2_SCHEMA_MAJOR
        && mailbox->schema_minor == SAN9_P1_M2_SCHEMA_MINOR
        && mailbox->declared_size == SAN9_P1_M2_MAILBOX_SIZE
        && mailbox->reserved_header == 0u
        && bytes_zero(mailbox->reserved_to_key, sizeof(mailbox->reserved_to_key))
        && key_valid(mailbox->hmac_key, sizeof(mailbox->hmac_key))
        && bytes_zero(mailbox->reserved_to_request,
            sizeof(mailbox->reserved_to_request))
        && bytes_zero(mailbox->reserved_tail, sizeof(mailbox->reserved_tail))
        && atomic_is_lock_free(&mailbox->lifecycle_state)
        && atomic_is_lock_free(&mailbox->request_slot_state)
        && atomic_is_lock_free(&mailbox->response_slot_state)
        && atomic_is_lock_free(&mailbox->poison_code)
        && atomic_is_lock_free(&mailbox->accepted_ping_count)
        && atomic_is_lock_free(&mailbox->controller_ack);
}

int san9_p1_m2_mailbox_initialize(
    San9P1M2Mailbox *mailbox,
    const uint8_t *hmac_key,
    size_t hmac_key_size)
{
    if (!pointer_aligned(mailbox, _Alignof(San9P1M2Mailbox))
        || !key_valid(hmac_key, hmac_key_size)
        || ranges_overlap(mailbox, sizeof(*mailbox), hmac_key, hmac_key_size)) {
        return 0;
    }
    memset(mailbox, 0, sizeof(*mailbox));
    mailbox->magic = SAN9_P1_M2_MAILBOX_MAGIC;
    mailbox->schema_major = SAN9_P1_M2_SCHEMA_MAJOR;
    mailbox->schema_minor = SAN9_P1_M2_SCHEMA_MINOR;
    mailbox->declared_size = SAN9_P1_M2_MAILBOX_SIZE;
    memcpy(mailbox->hmac_key, hmac_key, SAN9_P1_HMAC_KEY_SIZE);
    atomic_init(&mailbox->lifecycle_state, SAN9_P1_M2_LIFECYCLE_EMPTY);
    atomic_init(&mailbox->request_slot_state, SAN9_P1_M2_SLOT_EMPTY);
    atomic_init(&mailbox->response_slot_state, SAN9_P1_M2_SLOT_EMPTY);
    atomic_init(&mailbox->poison_code, SAN9_P1_M2_POISON_NONE);
    atomic_init(&mailbox->accepted_ping_count, 0u);
    atomic_init(&mailbox->controller_ack, 0u);
    return mailbox_header_valid(mailbox);
}

int san9_p1_m2_mailbox_validate(const San9P1M2Mailbox *mailbox)
{
    uint32_t lifecycle;
    uint32_t request;
    uint32_t response;
    uint32_t poison;
    uint32_t accepted;
    uint32_t ack;
    if (!mailbox_header_valid(mailbox)) {
        return 0;
    }
    lifecycle = atomic_load_explicit(&mailbox->lifecycle_state, memory_order_acquire);
    request = atomic_load_explicit(&mailbox->request_slot_state, memory_order_acquire);
    response = atomic_load_explicit(&mailbox->response_slot_state, memory_order_acquire);
    poison = atomic_load_explicit(&mailbox->poison_code, memory_order_acquire);
    accepted = atomic_load_explicit(&mailbox->accepted_ping_count, memory_order_acquire);
    ack = atomic_load_explicit(&mailbox->controller_ack, memory_order_acquire);
    if (!lifecycle_value_valid(lifecycle) || !slot_value_valid(request)
        || !slot_value_valid(response) || !poison_value_valid(poison)
        || accepted > SAN9_P1_M2_MAXIMUM_ROUNDS
        || ack > SAN9_P1_M2_MAXIMUM_ROUNDS + 1u) {
        return 0;
    }
    if (lifecycle == SAN9_P1_M2_LIFECYCLE_POISONED_RESTART
        && poison == SAN9_P1_M2_POISON_NONE) {
        return 0;
    }
    if (poison != SAN9_P1_M2_POISON_NONE
        && lifecycle < SAN9_P1_M2_LIFECYCLE_COMMITTING) {
        return 0;
    }
    if (request == SAN9_P1_M2_SLOT_EMPTY
        && response != SAN9_P1_M2_SLOT_EMPTY) {
        return 0;
    }
    if (response >= SAN9_P1_M2_SLOT_COMPLETE
        && request < SAN9_P1_M2_SLOT_CLAIMED) {
        return 0;
    }
    if (request == SAN9_P1_M2_SLOT_EMPTY
        && lifecycle < SAN9_P1_M2_LIFECYCLE_READY
        && (!bytes_zero(mailbox->request_frame, sizeof(mailbox->request_frame))
            || !bytes_zero(mailbox->response_frame, sizeof(mailbox->response_frame)))) {
        return 0;
    }
    return 1;
}

static San9P1M2Runtime *runtime_mutable(San9P1M2RuntimeStorage *storage)
{
    San9P1M2Runtime *runtime;
    if (!pointer_aligned(storage, _Alignof(San9P1M2RuntimeStorage))) {
        return NULL;
    }
    runtime = (San9P1M2Runtime *)(void *)storage->bytes;
    if (runtime->magic != SAN9_P1_M2_RUNTIME_MAGIC
        || !mailbox_header_valid(runtime->mailbox)) {
        return NULL;
    }
    return runtime;
}

static const San9P1M2Runtime *runtime_const(const San9P1M2RuntimeStorage *storage)
{
    const San9P1M2Runtime *runtime;
    if (!pointer_aligned(storage, _Alignof(San9P1M2RuntimeStorage))) {
        return NULL;
    }
    runtime = (const San9P1M2Runtime *)(const void *)storage->bytes;
    if (runtime->magic != SAN9_P1_M2_RUNTIME_MAGIC
        || !mailbox_header_valid(runtime->mailbox)) {
        return NULL;
    }
    return runtime;
}

int san9_p1_m2_runtime_initialize(
    San9P1M2RuntimeStorage *storage,
    San9P1M2Mailbox *mailbox)
{
    San9P1M2Runtime *runtime;
    if (!pointer_aligned(storage, _Alignof(San9P1M2RuntimeStorage))
        || !san9_p1_m2_mailbox_validate(mailbox)
        || ranges_overlap(storage, sizeof(*storage), mailbox, sizeof(*mailbox))) {
        return 0;
    }
    memset(storage, 0, sizeof(*storage));
    runtime = (San9P1M2Runtime *)(void *)storage->bytes;
    runtime->magic = SAN9_P1_M2_RUNTIME_MAGIC;
    runtime->mailbox = mailbox;
    atomic_init(&runtime->active_round_token, 0u);
    atomic_init(&runtime->round_outcome, SAN9_P1_M2_ROUND_NONE);
    atomic_init(&runtime->processing_owner, SAN9_P1_M2_PROCESSING_NONE);
    return 1;
}

static int processing_claim(
    San9P1M2Runtime *runtime,
    San9P1M2ProcessingOwner desired_owner)
{
    uint32_t expected = SAN9_P1_M2_PROCESSING_NONE;
    return atomic_compare_exchange_strong_explicit(
        &runtime->processing_owner,
        &expected,
        (uint32_t)desired_owner,
        memory_order_acq_rel,
        memory_order_acquire);
}

static San9P1M2Status processing_release(
    San9P1M2Runtime *runtime,
    San9P1M2Status status)
{
    atomic_store_explicit(&runtime->processing_owner,
        SAN9_P1_M2_PROCESSING_NONE, memory_order_release);
    return status;
}

San9P1M2LifecycleState san9_p1_m2_lifecycle_load(
    const San9P1M2RuntimeStorage *storage)
{
    const San9P1M2Runtime *runtime = runtime_const(storage);
    uint32_t value;
    if (runtime == NULL) {
        return SAN9_P1_M2_LIFECYCLE_POISONED_RESTART;
    }
    value = atomic_load_explicit(
        &runtime->mailbox->lifecycle_state, memory_order_acquire);
    return lifecycle_value_valid(value)
        ? (San9P1M2LifecycleState)value
        : SAN9_P1_M2_LIFECYCLE_POISONED_RESTART;
}

static int lifecycle_cas(
    San9P1M2Mailbox *mailbox,
    San9P1M2LifecycleState expected_state,
    San9P1M2LifecycleState desired_state)
{
    uint32_t expected = (uint32_t)expected_state;
    return atomic_compare_exchange_strong_explicit(
        &mailbox->lifecycle_state,
        &expected,
        (uint32_t)desired_state,
        memory_order_acq_rel,
        memory_order_acquire);
}

San9P1M2Status san9_p1_m2_lifecycle_begin_write(
    San9P1M2RuntimeStorage *storage)
{
    San9P1M2Runtime *runtime = runtime_mutable(storage);
    if (runtime == NULL) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    return lifecycle_cas(runtime->mailbox, SAN9_P1_M2_LIFECYCLE_EMPTY,
        SAN9_P1_M2_LIFECYCLE_WRITING)
        ? SAN9_P1_M2_OK : SAN9_P1_M2_CAS_LOST;
}

San9P1M2Status san9_p1_m2_session_bind_authenticated(
    San9P1M2RuntimeStorage *storage,
    const uint8_t *binding_frame,
    size_t binding_frame_size,
    San9P1DecodeStatus *decode_status)
{
    San9P1M2Runtime *runtime = runtime_mutable(storage);
    San9P1Frame decoded;
    San9P1DecodeStatus status;
    if (runtime == NULL) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (decode_status != NULL
        && (ranges_overlap(decode_status, sizeof(*decode_status),
                storage, sizeof(*storage))
            || ranges_overlap(decode_status, sizeof(*decode_status),
                runtime->mailbox, sizeof(*runtime->mailbox))
            || ranges_overlap(decode_status, sizeof(*decode_status),
                binding_frame, binding_frame_size))) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (decode_status != NULL) {
        *decode_status = SAN9_P1_DECODE_NULL_FRAME;
    }
    if (binding_frame == NULL
        || binding_frame_size != SAN9_P1_FRAME_SIZE
        || ranges_overlap(binding_frame, binding_frame_size,
            storage, sizeof(*storage))
        || ranges_overlap(binding_frame, binding_frame_size,
            runtime->mailbox, sizeof(*runtime->mailbox))) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (atomic_load_explicit(&runtime->mailbox->lifecycle_state,
            memory_order_acquire) != SAN9_P1_M2_LIFECYCLE_WRITING
        || runtime->session_bound != 0u) {
        return SAN9_P1_M2_STATE_REJECTED;
    }
    status = san9_p1_decode(binding_frame, binding_frame_size,
        runtime->mailbox->hmac_key, sizeof(runtime->mailbox->hmac_key), &decoded);
    if (decode_status != NULL) {
        *decode_status = status;
    }
    if (status != SAN9_P1_DECODE_ACCEPTED
        || !san9_p1_request_gate_initialize(&runtime->request_gate, &decoded)) {
        san9_p1_secure_zero(&decoded, sizeof(decoded));
        return SAN9_P1_M2_AUTH_REJECTED;
    }
    runtime->session_bound = 1u;
    san9_p1_secure_zero(&decoded, sizeof(decoded));
    return SAN9_P1_M2_OK;
}

San9P1M2Status san9_p1_m2_lifecycle_seal(
    San9P1M2RuntimeStorage *storage)
{
    San9P1M2Runtime *runtime = runtime_mutable(storage);
    if (runtime == NULL) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (runtime->session_bound == 0u) {
        return SAN9_P1_M2_STATE_REJECTED;
    }
    return lifecycle_cas(runtime->mailbox, SAN9_P1_M2_LIFECYCLE_WRITING,
        SAN9_P1_M2_LIFECYCLE_SEALED)
        ? SAN9_P1_M2_OK : SAN9_P1_M2_CAS_LOST;
}

San9P1M2Status san9_p1_m2_lifecycle_cancel_precommit(
    San9P1M2RuntimeStorage *storage)
{
    San9P1M2Runtime *runtime = runtime_mutable(storage);
    uint32_t observed;
    if (runtime == NULL) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    observed = atomic_load_explicit(
        &runtime->mailbox->lifecycle_state, memory_order_acquire);
    for (;;) {
        if (observed != SAN9_P1_M2_LIFECYCLE_WRITING
            && observed != SAN9_P1_M2_LIFECYCLE_SEALED
            && observed != SAN9_P1_M2_LIFECYCLE_CLAIMED
            && observed != SAN9_P1_M2_LIFECYCLE_VALIDATING) {
            return SAN9_P1_M2_STATE_REJECTED;
        }
        if (atomic_compare_exchange_weak_explicit(
                &runtime->mailbox->lifecycle_state,
                &observed,
                SAN9_P1_M2_LIFECYCLE_STOPPED_PRECOMMIT,
                memory_order_acq_rel,
                memory_order_acquire)) {
            return SAN9_P1_M2_CANCELLED_PRECOMMIT;
        }
    }
}

static void owner_verifier(uint64_t owner_token, uint8_t output[SAN9_P1_DIGEST_SIZE])
{
    static const uint8_t domain[] = SAN9_P1_M2_OWNER_DOMAIN;
    San9P1Sha256Context context;
    uint8_t encoded[8];
    write_u64_le(encoded, owner_token);
    san9_p1_sha256_initialize(&context);
    san9_p1_sha256_update(&context, domain, sizeof(domain) - 1u);
    san9_p1_sha256_update(&context, encoded, sizeof(encoded));
    san9_p1_sha256_finish(&context, output);
    san9_p1_secure_zero(encoded, sizeof(encoded));
}

static int owner_matches(const San9P1M2Runtime *runtime, uint64_t owner_token)
{
    uint8_t candidate[SAN9_P1_DIGEST_SIZE];
    int matches;
    if (runtime->owner_set == 0u || owner_token == 0u) {
        return 0;
    }
    owner_verifier(owner_token, candidate);
    matches = san9_p1_constant_time_equal(candidate, runtime->owner_verifier,
        sizeof(candidate));
    san9_p1_secure_zero(candidate, sizeof(candidate));
    return matches;
}

static San9P1M2Status force_poison(
    San9P1M2Runtime *runtime,
    San9P1M2PoisonCode poison_code)
{
    uint32_t expected_poison = SAN9_P1_M2_POISON_NONE;
    uint32_t observed;
    if (runtime == NULL || poison_code == SAN9_P1_M2_POISON_NONE
        || !poison_value_valid((uint32_t)poison_code)) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    observed = atomic_load_explicit(
        &runtime->mailbox->lifecycle_state, memory_order_acquire);
    if (observed != SAN9_P1_M2_LIFECYCLE_COMMITTING
        && observed != SAN9_P1_M2_LIFECYCLE_READY
        && observed != SAN9_P1_M2_LIFECYCLE_QUIESCENT_PINNED
        && observed != SAN9_P1_M2_LIFECYCLE_POISONED_RESTART) {
        return SAN9_P1_M2_STATE_REJECTED;
    }
    (void)atomic_compare_exchange_strong_explicit(
        &runtime->mailbox->poison_code,
        &expected_poison,
        (uint32_t)poison_code,
        memory_order_acq_rel,
        memory_order_acquire);
    for (;;) {
        if (observed == SAN9_P1_M2_LIFECYCLE_POISONED_RESTART) {
            return SAN9_P1_M2_POISONED;
        }
        if (observed != SAN9_P1_M2_LIFECYCLE_COMMITTING
            && observed != SAN9_P1_M2_LIFECYCLE_READY
            && observed != SAN9_P1_M2_LIFECYCLE_QUIESCENT_PINNED) {
            return SAN9_P1_M2_STATE_REJECTED;
        }
        if (atomic_compare_exchange_weak_explicit(
                &runtime->mailbox->lifecycle_state,
                &observed,
                SAN9_P1_M2_LIFECYCLE_POISONED_RESTART,
                memory_order_acq_rel,
                memory_order_acquire)) {
            san9_p1_secure_zero(&runtime->pending_request,
                sizeof(runtime->pending_request));
            san9_p1_secure_zero(runtime->expected_result_digest,
                sizeof(runtime->expected_result_digest));
            return SAN9_P1_M2_POISONED;
        }
    }
}

San9P1M2Status san9_p1_m2_lifecycle_poison(
    San9P1M2RuntimeStorage *storage,
    San9P1M2PoisonCode poison_code,
    uint64_t commit_owner_token)
{
    San9P1M2Runtime *runtime = runtime_mutable(storage);
    uint32_t lifecycle;
    if (runtime == NULL || poison_code == SAN9_P1_M2_POISON_NONE
        || !poison_value_valid((uint32_t)poison_code)) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (!processing_claim(runtime, SAN9_P1_M2_PROCESSING_POISON)) {
        return SAN9_P1_M2_SLOT_BUSY;
    }
    lifecycle = atomic_load_explicit(
        &runtime->mailbox->lifecycle_state, memory_order_acquire);
    if (lifecycle == SAN9_P1_M2_LIFECYCLE_POISONED_RESTART) {
        return processing_release(runtime, SAN9_P1_M2_POISONED);
    }
    if (lifecycle != SAN9_P1_M2_LIFECYCLE_COMMITTING
        && lifecycle != SAN9_P1_M2_LIFECYCLE_READY
        && lifecycle != SAN9_P1_M2_LIFECYCLE_QUIESCENT_PINNED) {
        return processing_release(runtime, SAN9_P1_M2_STATE_REJECTED);
    }
    return processing_release(runtime, force_poison(runtime,
        owner_matches(runtime, commit_owner_token)
            ? poison_code : SAN9_P1_M2_POISON_OWNER_MISMATCH));
}

static San9P1M2Status reject_precommit(San9P1M2Runtime *runtime)
{
    if (lifecycle_cas(runtime->mailbox, SAN9_P1_M2_LIFECYCLE_VALIDATING,
            SAN9_P1_M2_LIFECYCLE_REJECTED_PRECOMMIT)) {
        return SAN9_P1_M2_REJECTED_PRECOMMIT;
    }
    return atomic_load_explicit(&runtime->mailbox->lifecycle_state,
        memory_order_acquire) == SAN9_P1_M2_LIFECYCLE_STOPPED_PRECOMMIT
        ? SAN9_P1_M2_CANCELLED_PRECOMMIT : SAN9_P1_M2_CAS_LOST;
}

San9P1M2Status san9_p1_m2_lifecycle_validate_and_commit(
    San9P1M2RuntimeStorage *storage,
    const San9P1EasyBinding *easy_binding,
    const San9P1EasyCallbacks *easy_callbacks,
    const San9P1M2FakeActions *fake_actions,
    uint64_t commit_owner_token)
{
    San9P1M2Runtime *runtime = runtime_mutable(storage);
    San9P1EasyReport report;
    San9P1Frame callback_binding;
    uint8_t verifier[SAN9_P1_DIGEST_SIZE];
    if (runtime == NULL || easy_binding == NULL || easy_callbacks == NULL
        || fake_actions == NULL || fake_actions->invoke == NULL
        || commit_owner_token == 0u || runtime->session_bound == 0u) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (!lifecycle_cas(runtime->mailbox, SAN9_P1_M2_LIFECYCLE_SEALED,
            SAN9_P1_M2_LIFECYCLE_CLAIMED)) {
        return SAN9_P1_M2_CAS_LOST;
    }
    if (!lifecycle_cas(runtime->mailbox, SAN9_P1_M2_LIFECYCLE_CLAIMED,
            SAN9_P1_M2_LIFECYCLE_VALIDATING)) {
        return SAN9_P1_M2_CAS_LOST;
    }
    callback_binding = runtime->request_gate.binding;
    if (fake_actions->invoke(fake_actions->context,
            SAN9_P1_M2_FAKE_PRECOMMIT_VALIDATE,
            &callback_binding, NULL) == 0) {
        san9_p1_secure_zero(&callback_binding, sizeof(callback_binding));
        return reject_precommit(runtime);
    }
    if (atomic_load_explicit(&runtime->mailbox->lifecycle_state,
            memory_order_acquire) != SAN9_P1_M2_LIFECYCLE_VALIDATING) {
        san9_p1_secure_zero(&callback_binding, sizeof(callback_binding));
        return SAN9_P1_M2_CANCELLED_PRECOMMIT;
    }
    if (fake_actions->invoke(fake_actions->context,
            SAN9_P1_M2_FAKE_BEFORE_COMMIT_CAS,
            &callback_binding, NULL) == 0) {
        san9_p1_secure_zero(&callback_binding, sizeof(callback_binding));
        return reject_precommit(runtime);
    }
    if (atomic_load_explicit(&runtime->mailbox->lifecycle_state,
            memory_order_acquire) != SAN9_P1_M2_LIFECYCLE_VALIDATING) {
        san9_p1_secure_zero(&callback_binding, sizeof(callback_binding));
        return SAN9_P1_M2_CANCELLED_PRECOMMIT;
    }
    if (san9_p1_easy_verify_callbacks(easy_binding, easy_callbacks, &report)
            != SAN9_P1_EASY_INSTALLED
        || report.compatible_for_future_bridge == 0u) {
        san9_p1_secure_zero(&callback_binding, sizeof(callback_binding));
        return reject_precommit(runtime);
    }
    if (atomic_load_explicit(&runtime->mailbox->lifecycle_state,
            memory_order_acquire) != SAN9_P1_M2_LIFECYCLE_VALIDATING) {
        san9_p1_secure_zero(&callback_binding, sizeof(callback_binding));
        return SAN9_P1_M2_CANCELLED_PRECOMMIT;
    }
    san9_p1_secure_zero(&callback_binding, sizeof(callback_binding));
    owner_verifier(commit_owner_token, verifier);
    memcpy(runtime->owner_verifier, verifier, sizeof(verifier));
    runtime->owner_set = 1u;
    san9_p1_secure_zero(verifier, sizeof(verifier));
    if (!lifecycle_cas(runtime->mailbox, SAN9_P1_M2_LIFECYCLE_VALIDATING,
            SAN9_P1_M2_LIFECYCLE_COMMITTING)) {
        san9_p1_secure_zero(runtime->owner_verifier,
            sizeof(runtime->owner_verifier));
        runtime->owner_set = 0u;
        return atomic_load_explicit(&runtime->mailbox->lifecycle_state,
            memory_order_acquire) == SAN9_P1_M2_LIFECYCLE_STOPPED_PRECOMMIT
            ? SAN9_P1_M2_CANCELLED_PRECOMMIT : SAN9_P1_M2_CAS_LOST;
    }
    callback_binding = runtime->request_gate.binding;
    if (fake_actions->invoke(fake_actions->context,
            SAN9_P1_M2_FAKE_COMMIT_SIDE_EFFECT,
            &callback_binding, NULL) == 0) {
        san9_p1_secure_zero(&callback_binding, sizeof(callback_binding));
        return force_poison(runtime, SAN9_P1_M2_POISON_POSTCOMMIT_ACTION);
    }
    san9_p1_secure_zero(&callback_binding, sizeof(callback_binding));
    if (!owner_matches(runtime, commit_owner_token)
        || !lifecycle_cas(runtime->mailbox, SAN9_P1_M2_LIFECYCLE_COMMITTING,
            SAN9_P1_M2_LIFECYCLE_READY)) {
        return force_poison(runtime, SAN9_P1_M2_POISON_STATE_CORRUPTION);
    }
    return SAN9_P1_M2_OK;
}

static int frame_output_aliases_runtime(
    const San9P1M2RuntimeStorage *storage,
    const San9P1M2Mailbox *mailbox,
    const void *output,
    size_t output_size)
{
    return ranges_overlap(output, output_size, storage, sizeof(*storage))
        || ranges_overlap(output, output_size, mailbox, sizeof(*mailbox));
}

San9P1M2Status san9_p1_m2_controller_publish_request(
    San9P1M2RuntimeStorage *storage,
    const uint8_t *request_frame,
    size_t request_frame_size,
    uint64_t *round_token)
{
    San9P1M2Runtime *runtime = runtime_mutable(storage);
    uint8_t copy[SAN9_P1_FRAME_SIZE];
    uint32_t expected;
    uint32_t ack;
    uint32_t lifecycle;
    if (runtime == NULL || round_token == NULL) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (frame_output_aliases_runtime(storage, runtime->mailbox,
            round_token, sizeof(*round_token))
        || ranges_overlap(round_token, sizeof(*round_token),
            request_frame, request_frame_size)
        || ranges_overlap(request_frame, request_frame_size,
            storage, sizeof(*storage))
        || ranges_overlap(request_frame, request_frame_size,
            runtime->mailbox, sizeof(*runtime->mailbox))) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    *round_token = 0u;
    if (request_frame == NULL || request_frame_size != SAN9_P1_FRAME_SIZE) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (!processing_claim(runtime, SAN9_P1_M2_PROCESSING_CONTROLLER)) {
        return SAN9_P1_M2_SLOT_BUSY;
    }
    lifecycle = atomic_load_explicit(&runtime->mailbox->lifecycle_state,
        memory_order_acquire);
    if (lifecycle == SAN9_P1_M2_LIFECYCLE_QUIESCENT_PINNED) {
        return processing_release(runtime, SAN9_P1_M2_QUIESCENT);
    }
    if (lifecycle != SAN9_P1_M2_LIFECYCLE_READY
        || runtime->session_bound == 0u) {
        return processing_release(runtime,
            lifecycle == SAN9_P1_M2_LIFECYCLE_POISONED_RESTART
                ? SAN9_P1_M2_POISONED : SAN9_P1_M2_STATE_REJECTED);
    }
    if (atomic_load_explicit(&runtime->active_round_token,
            memory_order_acquire) != 0u
        || atomic_load_explicit(&runtime->mailbox->request_slot_state,
            memory_order_acquire) != SAN9_P1_M2_SLOT_EMPTY
        || atomic_load_explicit(&runtime->mailbox->response_slot_state,
            memory_order_acquire) != SAN9_P1_M2_SLOT_EMPTY) {
        return processing_release(runtime, SAN9_P1_M2_SLOT_BUSY);
    }
    ack = atomic_load_explicit(&runtime->mailbox->controller_ack,
        memory_order_acquire);
    if (ack > SAN9_P1_M2_MAXIMUM_ROUNDS) {
        return processing_release(runtime, SAN9_P1_M2_BUDGET_EXHAUSTED);
    }
    memcpy(copy, request_frame, sizeof(copy));
    expected = SAN9_P1_M2_SLOT_EMPTY;
    if (!atomic_compare_exchange_strong_explicit(
            &runtime->mailbox->request_slot_state,
            &expected,
            SAN9_P1_M2_SLOT_WRITING,
            memory_order_acq_rel,
            memory_order_acquire)) {
        san9_p1_secure_zero(copy, sizeof(copy));
        return processing_release(runtime, SAN9_P1_M2_CAS_LOST);
    }
    atomic_store_explicit(&runtime->active_round_token, ack + 1u,
        memory_order_relaxed);
    atomic_store_explicit(&runtime->round_outcome, SAN9_P1_M2_ROUND_NONE,
        memory_order_relaxed);
    memcpy(runtime->mailbox->request_frame, copy, sizeof(copy));
    atomic_store_explicit(&runtime->mailbox->request_slot_state,
        SAN9_P1_M2_SLOT_READY, memory_order_release);
    *round_token = (uint64_t)ack + 1u;
    san9_p1_secure_zero(copy, sizeof(copy));
    return processing_release(runtime, SAN9_P1_M2_OK);
}

static San9P1M2Status target_reject_request(
    San9P1M2Runtime *runtime,
    San9P1GateStatus gate_status)
{
    if (gate_status == SAN9_P1_GATE_CLOCK_ROLLBACK
        || gate_status == SAN9_P1_GATE_CLOCK_FAULTED) {
        return force_poison(runtime, SAN9_P1_M2_POISON_CLOCK_FAULT);
    }
    atomic_store_explicit(&runtime->round_outcome, SAN9_P1_M2_ROUND_DISCARD,
        memory_order_relaxed);
    if (gate_status == SAN9_P1_GATE_BUDGET_EXHAUSTED) {
        (void)lifecycle_cas(runtime->mailbox, SAN9_P1_M2_LIFECYCLE_READY,
            SAN9_P1_M2_LIFECYCLE_QUIESCENT_PINNED);
    }
    atomic_store_explicit(&runtime->mailbox->request_slot_state,
        SAN9_P1_M2_SLOT_COMPLETE, memory_order_release);
    if (gate_status == SAN9_P1_GATE_BUDGET_EXHAUSTED) {
        return SAN9_P1_M2_BUDGET_EXHAUSTED;
    }
    return gate_status == SAN9_P1_GATE_AUTHENTICATED_FRAME_REJECTED
        ? SAN9_P1_M2_AUTH_REJECTED : SAN9_P1_M2_GATE_REJECTED;
}

San9P1M2Status san9_p1_m2_target_process_request(
    San9P1M2RuntimeStorage *storage,
    uint64_t round_token,
    uint64_t now_ms,
    const San9P1M2FakeActions *fake_actions,
    San9P1DecodeStatus *decode_status,
    San9P1GateStatus *gate_status)
{
    San9P1M2Runtime *runtime = runtime_mutable(storage);
    San9P1Frame accepted;
    San9P1Frame callback_request;
    San9P1Frame response;
    San9P1M2ResultEvidence evidence;
    uint8_t raw_request[SAN9_P1_FRAME_SIZE];
    uint8_t raw_response[SAN9_P1_FRAME_SIZE];
    uint8_t digest[SAN9_P1_DIGEST_SIZE];
    uint32_t expected;
    San9P1GateStatus gate;
    if (runtime == NULL) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if ((decode_status != NULL
            && frame_output_aliases_runtime(storage, runtime->mailbox,
                decode_status, sizeof(*decode_status)))
        || (gate_status != NULL
            && frame_output_aliases_runtime(storage, runtime->mailbox,
                gate_status, sizeof(*gate_status)))
        || ranges_overlap(decode_status, decode_status == NULL
                ? 0u : sizeof(*decode_status),
            gate_status, gate_status == NULL ? 0u : sizeof(*gate_status))) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (decode_status != NULL) {
        *decode_status = SAN9_P1_DECODE_NULL_FRAME;
    }
    if (gate_status != NULL) {
        *gate_status = SAN9_P1_GATE_WRONG_SHAPE;
    }
    if (fake_actions == NULL || fake_actions->invoke == NULL
        || round_token == 0u) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (!processing_claim(runtime, SAN9_P1_M2_PROCESSING_TARGET)) {
        return SAN9_P1_M2_SLOT_BUSY;
    }
    if (atomic_load_explicit(&runtime->mailbox->request_slot_state,
            memory_order_acquire) != SAN9_P1_M2_SLOT_READY) {
        return processing_release(runtime, SAN9_P1_M2_CAS_LOST);
    }
    if (round_token > UINT32_MAX
        || (uint32_t)round_token != atomic_load_explicit(
            &runtime->active_round_token, memory_order_acquire)) {
        return processing_release(runtime, SAN9_P1_M2_LATE_RESPONSE);
    }
    if (atomic_load_explicit(&runtime->mailbox->lifecycle_state,
            memory_order_acquire) != SAN9_P1_M2_LIFECYCLE_READY) {
        return processing_release(runtime, SAN9_P1_M2_STATE_REJECTED);
    }
    expected = SAN9_P1_M2_SLOT_READY;
    if (!atomic_compare_exchange_strong_explicit(
            &runtime->mailbox->request_slot_state,
            &expected,
            SAN9_P1_M2_SLOT_CLAIMED,
            memory_order_acquire,
            memory_order_acquire)) {
        return processing_release(runtime, SAN9_P1_M2_CAS_LOST);
    }
    memcpy(raw_request, runtime->mailbox->request_frame, sizeof(raw_request));
    gate = san9_p1_decode_and_accept(&runtime->request_gate,
        raw_request, sizeof(raw_request), runtime->mailbox->hmac_key,
        sizeof(runtime->mailbox->hmac_key), now_ms, decode_status, &accepted);
    if (gate_status != NULL) {
        *gate_status = gate;
    }
    san9_p1_secure_zero(raw_request, sizeof(raw_request));
    if (gate != SAN9_P1_GATE_ACCEPTED) {
        san9_p1_secure_zero(&accepted, sizeof(accepted));
        return processing_release(runtime, target_reject_request(runtime, gate));
    }
    runtime->pending_request = accepted;
    atomic_store_explicit(&runtime->mailbox->accepted_ping_count,
        runtime->request_gate.accepted_count, memory_order_release);
    memset(&evidence, 0, sizeof(evidence));
    callback_request = runtime->pending_request;
    if (fake_actions->invoke(fake_actions->context, SAN9_P1_M2_FAKE_PING,
            &callback_request, &evidence) == 0) {
        san9_p1_secure_zero(&accepted, sizeof(accepted));
        san9_p1_secure_zero(&callback_request, sizeof(callback_request));
        san9_p1_secure_zero(&evidence, sizeof(evidence));
        return processing_release(runtime,
            force_poison(runtime, SAN9_P1_M2_POISON_POSTCOMMIT_ACTION));
    }
    san9_p1_secure_zero(&callback_request, sizeof(callback_request));
    if (!san9_p1_m2_result_digest(&runtime->pending_request, evidence.caller,
            evidence.easy_snapshot_digest, runtime->request_gate.accepted_count,
            digest)) {
        san9_p1_secure_zero(&accepted, sizeof(accepted));
        san9_p1_secure_zero(&evidence, sizeof(evidence));
        return processing_release(runtime,
            force_poison(runtime, SAN9_P1_M2_POISON_RESULT_DIGEST));
    }
    response = runtime->pending_request;
    response.kind = SAN9_P1_PING_RESPONSE;
    response.state = SAN9_P1_COMPLETED;
    response.result_code = 0u;
    memcpy(response.result_digest, digest, sizeof(response.result_digest));
    if (!san9_p1_encode(&response, runtime->mailbox->hmac_key,
            sizeof(runtime->mailbox->hmac_key), raw_response,
            sizeof(raw_response))) {
        san9_p1_secure_zero(&accepted, sizeof(accepted));
        san9_p1_secure_zero(&response, sizeof(response));
        san9_p1_secure_zero(&evidence, sizeof(evidence));
        san9_p1_secure_zero(digest, sizeof(digest));
        return processing_release(runtime,
            force_poison(runtime, SAN9_P1_M2_POISON_RESPONSE_AUTH));
    }
    expected = SAN9_P1_M2_SLOT_EMPTY;
    if (!atomic_compare_exchange_strong_explicit(
            &runtime->mailbox->response_slot_state,
            &expected,
            SAN9_P1_M2_SLOT_WRITING,
            memory_order_acq_rel,
            memory_order_acquire)) {
        san9_p1_secure_zero(&accepted, sizeof(accepted));
        san9_p1_secure_zero(&response, sizeof(response));
        san9_p1_secure_zero(&evidence, sizeof(evidence));
        san9_p1_secure_zero(digest, sizeof(digest));
        san9_p1_secure_zero(raw_response, sizeof(raw_response));
        return processing_release(runtime,
            force_poison(runtime, SAN9_P1_M2_POISON_STATE_CORRUPTION));
    }
    memcpy(runtime->mailbox->response_frame, raw_response, sizeof(raw_response));
    memcpy(runtime->expected_result_digest, digest, sizeof(digest));
    atomic_store_explicit(&runtime->round_outcome, SAN9_P1_M2_ROUND_RESPONSE,
        memory_order_relaxed);
    atomic_store_explicit(&runtime->mailbox->response_slot_state,
        SAN9_P1_M2_SLOT_COMPLETE, memory_order_release);
    atomic_store_explicit(&runtime->mailbox->request_slot_state,
        SAN9_P1_M2_SLOT_COMPLETE, memory_order_release);
    san9_p1_secure_zero(&accepted, sizeof(accepted));
    san9_p1_secure_zero(&response, sizeof(response));
    san9_p1_secure_zero(&evidence, sizeof(evidence));
    san9_p1_secure_zero(digest, sizeof(digest));
    san9_p1_secure_zero(raw_response, sizeof(raw_response));
    return processing_release(runtime, SAN9_P1_M2_OK);
}

static San9P1M2Status controller_finish_round(
    San9P1M2Runtime *runtime,
    uint64_t round_token)
{
    uint32_t expected_ack;
    if (round_token == 0u || round_token > UINT32_MAX) {
        return force_poison(runtime, SAN9_P1_M2_POISON_STATE_CORRUPTION);
    }
    memset(runtime->mailbox->request_frame, 0,
        sizeof(runtime->mailbox->request_frame));
    memset(runtime->mailbox->response_frame, 0,
        sizeof(runtime->mailbox->response_frame));
    san9_p1_secure_zero(&runtime->pending_request,
        sizeof(runtime->pending_request));
    san9_p1_secure_zero(runtime->expected_result_digest,
        sizeof(runtime->expected_result_digest));
    atomic_store_explicit(&runtime->round_outcome, SAN9_P1_M2_ROUND_NONE,
        memory_order_relaxed);
    atomic_store_explicit(&runtime->active_round_token, 0u,
        memory_order_relaxed);
    expected_ack = (uint32_t)round_token - 1u;
    if (!atomic_compare_exchange_strong_explicit(
            &runtime->mailbox->controller_ack,
            &expected_ack,
            (uint32_t)round_token,
            memory_order_acq_rel,
            memory_order_acquire)) {
        return force_poison(runtime, SAN9_P1_M2_POISON_STATE_CORRUPTION);
    }
    atomic_store_explicit(&runtime->mailbox->response_slot_state,
        SAN9_P1_M2_SLOT_EMPTY, memory_order_release);
    atomic_store_explicit(&runtime->mailbox->request_slot_state,
        SAN9_P1_M2_SLOT_EMPTY, memory_order_release);
    return SAN9_P1_M2_OK;
}

San9P1M2Status san9_p1_m2_controller_discard_failed_request(
    San9P1M2RuntimeStorage *storage,
    uint64_t round_token)
{
    San9P1M2Runtime *runtime = runtime_mutable(storage);
    uint32_t expected;
    if (runtime == NULL || round_token == 0u) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (!processing_claim(runtime, SAN9_P1_M2_PROCESSING_CONTROLLER)) {
        return SAN9_P1_M2_SLOT_BUSY;
    }
    if (round_token > UINT32_MAX
        || (uint32_t)round_token != atomic_load_explicit(
            &runtime->active_round_token, memory_order_acquire)) {
        return processing_release(runtime, SAN9_P1_M2_LATE_RESPONSE);
    }
    {
        uint32_t lifecycle = atomic_load_explicit(
            &runtime->mailbox->lifecycle_state, memory_order_acquire);
        if (lifecycle != SAN9_P1_M2_LIFECYCLE_READY
            && lifecycle != SAN9_P1_M2_LIFECYCLE_QUIESCENT_PINNED) {
            return processing_release(runtime,
                lifecycle == SAN9_P1_M2_LIFECYCLE_POISONED_RESTART
                    ? SAN9_P1_M2_POISONED : SAN9_P1_M2_STATE_REJECTED);
        }
    }
    if (atomic_load_explicit(&runtime->mailbox->request_slot_state,
            memory_order_acquire) != SAN9_P1_M2_SLOT_COMPLETE
        || atomic_load_explicit(&runtime->round_outcome,
            memory_order_acquire) != SAN9_P1_M2_ROUND_DISCARD
        || atomic_load_explicit(&runtime->mailbox->response_slot_state,
            memory_order_acquire) != SAN9_P1_M2_SLOT_EMPTY) {
        return processing_release(runtime, SAN9_P1_M2_STATE_REJECTED);
    }
    expected = SAN9_P1_M2_SLOT_COMPLETE;
    if (!atomic_compare_exchange_strong_explicit(
            &runtime->mailbox->request_slot_state,
            &expected,
            SAN9_P1_M2_SLOT_CONSUMED,
            memory_order_acquire,
            memory_order_acquire)) {
        return processing_release(runtime, SAN9_P1_M2_CAS_LOST);
    }
    return processing_release(runtime,
        controller_finish_round(runtime, round_token));
}

San9P1M2Status san9_p1_m2_controller_consume_response(
    San9P1M2RuntimeStorage *storage,
    uint64_t round_token,
    San9P1Frame *verified_response,
    San9P1DecodeStatus *decode_status)
{
    San9P1M2Runtime *runtime = runtime_mutable(storage);
    San9P1Frame verified;
    uint8_t raw_response[SAN9_P1_FRAME_SIZE];
    uint32_t expected_response;
    uint32_t expected_request;
    if (runtime == NULL || verified_response == NULL) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (frame_output_aliases_runtime(storage, runtime->mailbox,
            verified_response, sizeof(*verified_response))
        || (decode_status != NULL
            && frame_output_aliases_runtime(storage, runtime->mailbox,
                decode_status, sizeof(*decode_status)))
        || ranges_overlap(verified_response, sizeof(*verified_response),
            decode_status, decode_status == NULL ? 0u : sizeof(*decode_status))) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    if (decode_status != NULL) {
        *decode_status = SAN9_P1_DECODE_NULL_FRAME;
    }
    if (round_token == 0u) {
        return SAN9_P1_M2_INVALID_ARGUMENT;
    }
    memset(verified_response, 0, sizeof(*verified_response));
    if (!processing_claim(runtime, SAN9_P1_M2_PROCESSING_CONTROLLER)) {
        return SAN9_P1_M2_SLOT_BUSY;
    }
    if (round_token > UINT32_MAX
        || (uint32_t)round_token != atomic_load_explicit(
            &runtime->active_round_token, memory_order_acquire)) {
        return processing_release(runtime, SAN9_P1_M2_LATE_RESPONSE);
    }
    {
        uint32_t lifecycle = atomic_load_explicit(
            &runtime->mailbox->lifecycle_state, memory_order_acquire);
        if (lifecycle != SAN9_P1_M2_LIFECYCLE_READY) {
            return processing_release(runtime,
                lifecycle == SAN9_P1_M2_LIFECYCLE_POISONED_RESTART
                    ? SAN9_P1_M2_POISONED : SAN9_P1_M2_STATE_REJECTED);
        }
    }
    if (atomic_load_explicit(&runtime->mailbox->request_slot_state,
            memory_order_acquire) != SAN9_P1_M2_SLOT_COMPLETE
        || atomic_load_explicit(&runtime->round_outcome,
            memory_order_acquire) != SAN9_P1_M2_ROUND_RESPONSE
        || atomic_load_explicit(&runtime->mailbox->response_slot_state,
            memory_order_acquire) != SAN9_P1_M2_SLOT_COMPLETE) {
        return processing_release(runtime, SAN9_P1_M2_NO_PENDING);
    }
    memcpy(raw_response, runtime->mailbox->response_frame, sizeof(raw_response));
    expected_response = SAN9_P1_M2_SLOT_COMPLETE;
    if (!atomic_compare_exchange_strong_explicit(
            &runtime->mailbox->response_slot_state,
            &expected_response,
            SAN9_P1_M2_SLOT_CONSUMED,
            memory_order_acquire,
            memory_order_acquire)) {
        san9_p1_secure_zero(raw_response, sizeof(raw_response));
        return processing_release(runtime, SAN9_P1_M2_CAS_LOST);
    }
    expected_request = SAN9_P1_M2_SLOT_COMPLETE;
    if (!atomic_compare_exchange_strong_explicit(
            &runtime->mailbox->request_slot_state,
            &expected_request,
            SAN9_P1_M2_SLOT_CONSUMED,
            memory_order_acquire,
            memory_order_acquire)) {
        san9_p1_secure_zero(raw_response, sizeof(raw_response));
        return processing_release(runtime,
            force_poison(runtime, SAN9_P1_M2_POISON_STATE_CORRUPTION));
    }
    if (!san9_p1_decode_and_verify_response(&runtime->pending_request,
            raw_response, sizeof(raw_response), runtime->mailbox->hmac_key,
            sizeof(runtime->mailbox->hmac_key), decode_status, &verified)) {
        san9_p1_secure_zero(raw_response, sizeof(raw_response));
        san9_p1_secure_zero(&verified, sizeof(verified));
        return processing_release(runtime,
            force_poison(runtime, SAN9_P1_M2_POISON_RESPONSE_AUTH));
    }
    if (!san9_p1_constant_time_equal(verified.result_digest,
            runtime->expected_result_digest, SAN9_P1_DIGEST_SIZE)) {
        san9_p1_secure_zero(raw_response, sizeof(raw_response));
        san9_p1_secure_zero(&verified, sizeof(verified));
        return processing_release(runtime,
            force_poison(runtime, SAN9_P1_M2_POISON_RESULT_DIGEST));
    }
    *verified_response = verified;
    san9_p1_secure_zero(raw_response, sizeof(raw_response));
    san9_p1_secure_zero(&verified, sizeof(verified));
    return processing_release(runtime,
        controller_finish_round(runtime, round_token));
}

int san9_p1_m2_result_digest(
    const San9P1Frame *authenticated_request,
    uint32_t caller,
    const uint8_t easy_snapshot_digest[SAN9_P1_DIGEST_SIZE],
    uint32_t ping_ordinal,
    uint8_t output[SAN9_P1_DIGEST_SIZE])
{
    static const uint8_t domain[] = SAN9_P1_M2_RESULT_DOMAIN;
    San9P1Sha256Context context;
    uint8_t sequence[8];
    uint8_t main_tid[4];
    uint8_t encoded_caller[4];
    uint8_t ordinal[4];
    if (output == NULL
        || ranges_overlap(output, SAN9_P1_DIGEST_SIZE,
            authenticated_request, sizeof(*authenticated_request))
        || ranges_overlap(output, SAN9_P1_DIGEST_SIZE,
            easy_snapshot_digest, SAN9_P1_DIGEST_SIZE)) {
        return 0;
    }
    memset(output, 0, SAN9_P1_DIGEST_SIZE);
    if (authenticated_request == NULL || easy_snapshot_digest == NULL
        || authenticated_request->kind != SAN9_P1_PING_REQUEST
        || authenticated_request->state != SAN9_P1_PENDING
        || authenticated_request->sequence == 0u
        || authenticated_request->main_tid == 0u || caller == 0u
        || ping_ordinal == 0u || ping_ordinal > SAN9_P1_M2_MAXIMUM_ROUNDS
        || bytes_zero(authenticated_request->request_id, SAN9_P1_NONCE_SIZE)
        || bytes_zero(authenticated_request->challenge_digest,
            SAN9_P1_DIGEST_SIZE)
        || bytes_zero(easy_snapshot_digest, SAN9_P1_DIGEST_SIZE)) {
        return 0;
    }
    write_u64_le(sequence, authenticated_request->sequence);
    write_u32_le(main_tid, authenticated_request->main_tid);
    write_u32_le(encoded_caller, caller);
    write_u32_le(ordinal, ping_ordinal);
    san9_p1_sha256_initialize(&context);
    san9_p1_sha256_update(&context, domain, sizeof(domain) - 1u);
    san9_p1_sha256_update(&context, authenticated_request->request_id,
        SAN9_P1_NONCE_SIZE);
    san9_p1_sha256_update(&context, sequence, sizeof(sequence));
    san9_p1_sha256_update(&context, authenticated_request->challenge_digest,
        SAN9_P1_DIGEST_SIZE);
    san9_p1_sha256_update(&context, main_tid, sizeof(main_tid));
    san9_p1_sha256_update(&context, encoded_caller, sizeof(encoded_caller));
    san9_p1_sha256_update(&context, easy_snapshot_digest,
        SAN9_P1_DIGEST_SIZE);
    san9_p1_sha256_update(&context, ordinal, sizeof(ordinal));
    san9_p1_sha256_finish(&context, output);
    san9_p1_secure_zero(sequence, sizeof(sequence));
    san9_p1_secure_zero(main_tid, sizeof(main_tid));
    san9_p1_secure_zero(encoded_caller, sizeof(encoded_caller));
    san9_p1_secure_zero(ordinal, sizeof(ordinal));
    return 1;
}
