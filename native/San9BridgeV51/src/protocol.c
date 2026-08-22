#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "san9_bridge_v51.h"
#include "sha256.h"

#include <string.h>

enum {
    FRAME_MAGIC = 0x47503953U, /* "S9PG" */
    FRAME_SCHEMA_MAJOR = 1,
    FRAME_SCHEMA_MINOR = 0,
    FRAME_KIND_REQUEST = 1,
    FRAME_KIND_RESPONSE = 2,
    FRAME_STATE_PENDING = 1,
    FRAME_STATE_COMPLETED = 2,
    FRAME_STATE_REJECTED = 3,

    OFFSET_MAGIC = 0,
    OFFSET_SCHEMA_MAJOR = 4,
    OFFSET_SCHEMA_MINOR = 6,
    OFFSET_FRAME_SIZE = 8,
    OFFSET_KIND = 12,
    OFFSET_STATE = 14,
    OFFSET_FLAGS = 16,
    OFFSET_RESULT_CODE = 20,
    OFFSET_SEQUENCE = 24,
    OFFSET_ISSUED_AT = 32,
    OFFSET_EXPIRES_AT = 40,
    OFFSET_SESSION_NONCE = 48,
    OFFSET_REQUEST_ID = 64,
    OFFSET_TARGET_DIGEST = 80,
    OFFSET_CONTEXT_DIGEST = 112,
    OFFSET_CHALLENGE = 144,
    OFFSET_REQUEST_MAC = 176,
    OFFSET_RESPONSE_MAC = 208,
    OFFSET_RESERVED = 240,
    RESERVED_SIZE = 16
};

_Static_assert(sizeof(San9PingMailbox) == SAN9_PING_MAILBOX_SIZE, "mailbox size drift");
_Static_assert(sizeof(San9PingSession) == SAN9_PING_SESSION_SIZE, "session size drift");
_Static_assert(offsetof(San9PingMailbox, request_frame) == 64, "request offset drift");
_Static_assert(offsetof(San9PingMailbox, response_frame) == 320, "response offset drift");

static uint16_t read_u16(const uint8_t *buffer, size_t offset)
{
    return (uint16_t)((uint16_t)buffer[offset]
        | ((uint16_t)buffer[offset + 1] << 8));
}

static uint32_t read_u32(const uint8_t *buffer, size_t offset)
{
    return (uint32_t)buffer[offset]
        | ((uint32_t)buffer[offset + 1] << 8)
        | ((uint32_t)buffer[offset + 2] << 16)
        | ((uint32_t)buffer[offset + 3] << 24);
}

static uint64_t read_u64(const uint8_t *buffer, size_t offset)
{
    uint64_t value = 0;
    unsigned index;
    for (index = 0; index < 8; ++index) {
        value |= (uint64_t)buffer[offset + index] << (index * 8U);
    }
    return value;
}

static void write_u16(uint8_t *buffer, size_t offset, uint16_t value)
{
    buffer[offset] = (uint8_t)value;
    buffer[offset + 1] = (uint8_t)(value >> 8);
}

static void write_u32(uint8_t *buffer, size_t offset, uint32_t value)
{
    buffer[offset] = (uint8_t)value;
    buffer[offset + 1] = (uint8_t)(value >> 8);
    buffer[offset + 2] = (uint8_t)(value >> 16);
    buffer[offset + 3] = (uint8_t)(value >> 24);
}

static void write_u64(uint8_t *buffer, size_t offset, uint64_t value)
{
    unsigned index;
    for (index = 0; index < 8; ++index) {
        buffer[offset + index] = (uint8_t)(value >> (index * 8U));
    }
}

static int range_is_zero(const uint8_t *value, size_t size)
{
    uint8_t aggregate = 0;
    size_t index;
    for (index = 0; index < size; ++index) {
        aggregate = (uint8_t)(aggregate | value[index]);
    }
    return aggregate == 0;
}

static void compute_request_mac(
    const San9PingSession *session,
    const uint8_t frame[SAN9_PING_FRAME_SIZE],
    uint8_t output[SAN9_PING_DIGEST_SIZE])
{
    uint8_t canonical[SAN9_PING_FRAME_SIZE];
    memcpy(canonical, frame, sizeof(canonical));
    memset(canonical + OFFSET_REQUEST_MAC, 0, SAN9_PING_DIGEST_SIZE * 2U);
    san9_hmac_sha256(session->key, sizeof(session->key), canonical, sizeof(canonical), output);
    san9_secure_zero(canonical, sizeof(canonical));
}

static void compute_response_mac(
    const San9PingSession *session,
    const uint8_t frame[SAN9_PING_FRAME_SIZE],
    uint8_t output[SAN9_PING_DIGEST_SIZE])
{
    uint8_t canonical[SAN9_PING_FRAME_SIZE];
    memcpy(canonical, frame, sizeof(canonical));
    memset(canonical + OFFSET_RESPONSE_MAC, 0, SAN9_PING_DIGEST_SIZE);
    san9_hmac_sha256(session->key, sizeof(session->key), canonical, sizeof(canonical), output);
    san9_secure_zero(canonical, sizeof(canonical));
}

static int frame_header_is_valid(const uint8_t *frame, uint16_t kind, uint16_t state)
{
    return read_u32(frame, OFFSET_MAGIC) == FRAME_MAGIC
        && read_u16(frame, OFFSET_SCHEMA_MAJOR) == FRAME_SCHEMA_MAJOR
        && read_u16(frame, OFFSET_SCHEMA_MINOR) == FRAME_SCHEMA_MINOR
        && read_u32(frame, OFFSET_FRAME_SIZE) == SAN9_PING_FRAME_SIZE
        && read_u16(frame, OFFSET_KIND) == kind
        && read_u16(frame, OFFSET_STATE) == state
        && read_u32(frame, OFFSET_FLAGS) == 0
        && range_is_zero(frame + OFFSET_RESERVED, RESERVED_SIZE);
}

static int validate_authenticated_request(
    const San9PingSession *session,
    const uint8_t request[SAN9_PING_FRAME_SIZE],
    uint64_t now_ms,
    int *authenticated)
{
    uint8_t expected_mac[SAN9_PING_DIGEST_SIZE];
    uint64_t sequence;
    uint64_t issued_at;
    uint64_t expires_at;
    int result;

    *authenticated = 0;
    if (!frame_header_is_valid(request, FRAME_KIND_REQUEST, FRAME_STATE_PENDING)
        || read_u32(request, OFFSET_RESULT_CODE) != 0
        || !range_is_zero(request + OFFSET_RESPONSE_MAC, SAN9_PING_DIGEST_SIZE)
        || range_is_zero(request + OFFSET_SESSION_NONCE, SAN9_PING_NONCE_SIZE)
        || range_is_zero(request + OFFSET_REQUEST_ID, SAN9_PING_REQUEST_ID_SIZE)
        || range_is_zero(request + OFFSET_TARGET_DIGEST, SAN9_PING_DIGEST_SIZE)
        || range_is_zero(request + OFFSET_CONTEXT_DIGEST, SAN9_PING_DIGEST_SIZE)
        || range_is_zero(request + OFFSET_CHALLENGE, SAN9_PING_DIGEST_SIZE)) {
        return SAN9_PING_INVALID_FRAME;
    }

    compute_request_mac(session, request, expected_mac);
    if (!san9_constant_time_equal(
            expected_mac,
            request + OFFSET_REQUEST_MAC,
            SAN9_PING_DIGEST_SIZE)) {
        san9_secure_zero(expected_mac, sizeof(expected_mac));
        return SAN9_PING_AUTH_FAILED;
    }
    san9_secure_zero(expected_mac, sizeof(expected_mac));
    *authenticated = 1;

    if (!san9_constant_time_equal(
            request + OFFSET_SESSION_NONCE,
            session->session_nonce,
            SAN9_PING_NONCE_SIZE)
        || !san9_constant_time_equal(
            request + OFFSET_TARGET_DIGEST,
            session->target_digest,
            SAN9_PING_DIGEST_SIZE)
        || !san9_constant_time_equal(
            request + OFFSET_CONTEXT_DIGEST,
            session->context_digest,
            SAN9_PING_DIGEST_SIZE)) {
        return SAN9_PING_BINDING_MISMATCH;
    }

    issued_at = read_u64(request, OFFSET_ISSUED_AT);
    expires_at = read_u64(request, OFFSET_EXPIRES_AT);
    if (issued_at == 0 || expires_at <= issued_at
        || expires_at - issued_at > SAN9_PING_MAX_LIFETIME_MS) {
        return SAN9_PING_LIFETIME_INVALID;
    }
    if (now_ms < issued_at) {
        return SAN9_PING_FROM_FUTURE;
    }
    if (now_ms >= expires_at) {
        return SAN9_PING_EXPIRED;
    }

    sequence = read_u64(request, OFFSET_SEQUENCE);
    if (sequence == 0 || session->last_accepted_sequence == UINT64_MAX
        || sequence <= session->last_accepted_sequence) {
        return SAN9_PING_SEQUENCE_REPLAY;
    }
    if (sequence != session->last_accepted_sequence + 1U) {
        return SAN9_PING_SEQUENCE_GAP;
    }
    result = SAN9_PING_OK;
    return result;
}

static void make_response(
    const San9PingSession *session,
    const uint8_t request[SAN9_PING_FRAME_SIZE],
    int result_code,
    uint8_t response[SAN9_PING_FRAME_SIZE])
{
    uint8_t response_mac[SAN9_PING_DIGEST_SIZE];
    memcpy(response, request, SAN9_PING_FRAME_SIZE);
    write_u16(response, OFFSET_KIND, FRAME_KIND_RESPONSE);
    write_u16(
        response,
        OFFSET_STATE,
        (uint16_t)(result_code == SAN9_PING_OK ? FRAME_STATE_COMPLETED : FRAME_STATE_REJECTED));
    write_u32(response, OFFSET_RESULT_CODE, (uint32_t)result_code);
    memset(response + OFFSET_RESPONSE_MAC, 0, SAN9_PING_DIGEST_SIZE);
    compute_response_mac(session, response, response_mac);
    memcpy(response + OFFSET_RESPONSE_MAC, response_mac, sizeof(response_mac));
    san9_secure_zero(response_mac, sizeof(response_mac));
}

int san9_ping_session_initialize(
    San9PingSession *session,
    const uint8_t key[SAN9_PING_KEY_SIZE],
    const uint8_t session_nonce[SAN9_PING_NONCE_SIZE],
    const uint8_t target_digest[SAN9_PING_DIGEST_SIZE],
    const uint8_t context_digest[SAN9_PING_DIGEST_SIZE],
    uint32_t expected_thread_id,
    uint32_t expected_idle_bridge)
{
    if (session == NULL || key == NULL || session_nonce == NULL
        || target_digest == NULL || context_digest == NULL
        || range_is_zero(key, SAN9_PING_KEY_SIZE)
        || range_is_zero(session_nonce, SAN9_PING_NONCE_SIZE)
        || range_is_zero(target_digest, SAN9_PING_DIGEST_SIZE)
        || range_is_zero(context_digest, SAN9_PING_DIGEST_SIZE)
        || expected_thread_id == 0 || expected_idle_bridge == 0) {
        return SAN9_PING_INVALID_FRAME;
    }
    memset(session, 0, sizeof(*session));
    memcpy(session->key, key, SAN9_PING_KEY_SIZE);
    memcpy(session->session_nonce, session_nonce, SAN9_PING_NONCE_SIZE);
    memcpy(session->target_digest, target_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(session->context_digest, context_digest, SAN9_PING_DIGEST_SIZE);
    session->expected_thread_id = expected_thread_id;
    session->expected_idle_bridge = expected_idle_bridge;
    session->initialized = 1;
    return SAN9_PING_OK;
}

void san9_ping_session_clear(San9PingSession *session)
{
    if (session != NULL) {
        san9_secure_zero(session, sizeof(*session));
    }
}

void san9_ping_mailbox_initialize(San9PingMailbox *mailbox)
{
    if (mailbox != NULL) {
        memset(mailbox, 0, sizeof(*mailbox));
    }
}

int san9_ping_acknowledge_rejected_request(San9PingMailbox *mailbox)
{
    if (mailbox == NULL) {
        return SAN9_PING_INVALID_FRAME;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&mailbox->response_state,
            SAN9_MAILBOX_EMPTY,
            SAN9_MAILBOX_EMPTY) != SAN9_MAILBOX_EMPTY) {
        return SAN9_PING_MAILBOX_BUSY;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&mailbox->request_state,
            SAN9_MAILBOX_WRITING,
            SAN9_MAILBOX_REJECTED_UNAUTHENTICATED)
        != SAN9_MAILBOX_REJECTED_UNAUTHENTICATED) {
        return SAN9_PING_MAILBOX_BUSY;
    }
    MemoryBarrier();
    san9_secure_zero(mailbox->request_frame, SAN9_PING_FRAME_SIZE);
    san9_secure_zero(mailbox->response_frame, SAN9_PING_FRAME_SIZE);
    san9_secure_zero(mailbox->reserved_control, sizeof(mailbox->reserved_control));
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&mailbox->response_state, SAN9_MAILBOX_EMPTY);
    InterlockedExchange((volatile LONG *)&mailbox->request_state, SAN9_MAILBOX_EMPTY);
    return SAN9_PING_OK;
}

int san9_ping_reset_clock_fault(
    San9PingSession *session,
    San9PingMailbox *mailbox,
    const uint8_t key[SAN9_PING_KEY_SIZE],
    const uint8_t new_session_nonce[SAN9_PING_NONCE_SIZE],
    const uint8_t target_digest[SAN9_PING_DIGEST_SIZE],
    const uint8_t context_digest[SAN9_PING_DIGEST_SIZE],
    uint32_t expected_thread_id,
    uint32_t expected_idle_bridge)
{
    San9PingSession replacement;
    int result;

    if (session == NULL || mailbox == NULL || !session->initialized
        || session->clock_faulted != 1U || new_session_nonce == NULL
        || san9_constant_time_equal(
            session->session_nonce,
            new_session_nonce,
            SAN9_PING_NONCE_SIZE)) {
        return SAN9_PING_INVALID_FRAME;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&mailbox->response_state,
            SAN9_MAILBOX_EMPTY,
            SAN9_MAILBOX_EMPTY) != SAN9_MAILBOX_EMPTY) {
        return SAN9_PING_MAILBOX_BUSY;
    }
    result = san9_ping_session_initialize(
        &replacement,
        key,
        new_session_nonce,
        target_digest,
        context_digest,
        expected_thread_id,
        expected_idle_bridge);
    if (result != SAN9_PING_OK) {
        return result;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&mailbox->request_state,
            SAN9_MAILBOX_WRITING,
            SAN9_MAILBOX_SESSION_FAULT) != SAN9_MAILBOX_SESSION_FAULT) {
        san9_ping_session_clear(&replacement);
        return SAN9_PING_MAILBOX_BUSY;
    }

    san9_secure_zero(mailbox->request_frame, SAN9_PING_FRAME_SIZE);
    san9_secure_zero(mailbox->response_frame, SAN9_PING_FRAME_SIZE);
    san9_secure_zero(mailbox->reserved_control, sizeof(mailbox->reserved_control));
    *session = replacement;
    san9_ping_session_clear(&replacement);
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&mailbox->response_state, SAN9_MAILBOX_EMPTY);
    InterlockedExchange((volatile LONG *)&mailbox->request_state, SAN9_MAILBOX_EMPTY);
    return SAN9_PING_OK;
}

int san9_ping_make_request(
    const San9PingSession *session,
    uint64_t sequence,
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_id[SAN9_PING_REQUEST_ID_SIZE],
    const uint8_t challenge[SAN9_PING_DIGEST_SIZE],
    uint8_t output[SAN9_PING_FRAME_SIZE])
{
    uint8_t request_mac[SAN9_PING_DIGEST_SIZE];
    if (session == NULL || !session->initialized || request_id == NULL
        || challenge == NULL || output == NULL || sequence == 0
        || issued_at_ms == 0 || expires_at_ms <= issued_at_ms
        || expires_at_ms - issued_at_ms > SAN9_PING_MAX_LIFETIME_MS
        || range_is_zero(request_id, SAN9_PING_REQUEST_ID_SIZE)
        || range_is_zero(challenge, SAN9_PING_DIGEST_SIZE)) {
        return SAN9_PING_INVALID_FRAME;
    }
    memset(output, 0, SAN9_PING_FRAME_SIZE);
    write_u32(output, OFFSET_MAGIC, FRAME_MAGIC);
    write_u16(output, OFFSET_SCHEMA_MAJOR, FRAME_SCHEMA_MAJOR);
    write_u16(output, OFFSET_SCHEMA_MINOR, FRAME_SCHEMA_MINOR);
    write_u32(output, OFFSET_FRAME_SIZE, SAN9_PING_FRAME_SIZE);
    write_u16(output, OFFSET_KIND, FRAME_KIND_REQUEST);
    write_u16(output, OFFSET_STATE, FRAME_STATE_PENDING);
    write_u64(output, OFFSET_SEQUENCE, sequence);
    write_u64(output, OFFSET_ISSUED_AT, issued_at_ms);
    write_u64(output, OFFSET_EXPIRES_AT, expires_at_ms);
    memcpy(output + OFFSET_SESSION_NONCE, session->session_nonce, SAN9_PING_NONCE_SIZE);
    memcpy(output + OFFSET_REQUEST_ID, request_id, SAN9_PING_REQUEST_ID_SIZE);
    memcpy(output + OFFSET_TARGET_DIGEST, session->target_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(output + OFFSET_CONTEXT_DIGEST, session->context_digest, SAN9_PING_DIGEST_SIZE);
    memcpy(output + OFFSET_CHALLENGE, challenge, SAN9_PING_DIGEST_SIZE);
    compute_request_mac(session, output, request_mac);
    memcpy(output + OFFSET_REQUEST_MAC, request_mac, sizeof(request_mac));
    san9_secure_zero(request_mac, sizeof(request_mac));
    return SAN9_PING_OK;
}

int san9_ping_publish_request(
    San9PingMailbox *mailbox,
    const uint8_t request[SAN9_PING_FRAME_SIZE])
{
    if (mailbox == NULL || request == NULL) {
        return SAN9_PING_INVALID_FRAME;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&mailbox->request_state,
            SAN9_MAILBOX_WRITING,
            SAN9_MAILBOX_EMPTY) != SAN9_MAILBOX_EMPTY) {
        return SAN9_PING_MAILBOX_BUSY;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&mailbox->response_state,
            SAN9_MAILBOX_EMPTY,
            SAN9_MAILBOX_EMPTY) != SAN9_MAILBOX_EMPTY) {
        InterlockedExchange((volatile LONG *)&mailbox->request_state, SAN9_MAILBOX_EMPTY);
        return SAN9_PING_MAILBOX_BUSY;
    }
    memcpy(mailbox->request_frame, request, SAN9_PING_FRAME_SIZE);
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&mailbox->request_state, SAN9_MAILBOX_READY);
    return SAN9_PING_OK;
}

int san9_idle_gate_validate(
    const San9PingSession *session,
    const San9IdleGateSnapshot *snapshot)
{
    if (session == NULL || snapshot == NULL || !session->initialized) {
        return 0;
    }
    return snapshot->caller == SAN9_EXACT_IDLE_CALLER
        && snapshot->app_object == SAN9_EXACT_APP_OBJECT
        && snapshot->thread_id == session->expected_thread_id
        && snapshot->slot_address == SAN9_EXACT_IDLE_SLOT
        && snapshot->slot_value == session->expected_idle_bridge
        && snapshot->app_vptr == SAN9_EXACT_APP_VTABLE
        && snapshot->idle_argument == 0
        && snapshot->exact_target_verified == 1
        && snapshot->conflict_free == 1
        && snapshot->process_generation_stable == 1;
}

int san9_ping_process_one(
    San9PingSession *session,
    San9PingMailbox *mailbox,
    uint64_t now_ms,
    const San9IdleGateSnapshot *snapshot)
{
    int result;
    int authenticated;
    if (!san9_idle_gate_validate(session, snapshot)) {
        return SAN9_PING_GATE_REJECTED;
    }
    if (mailbox == NULL || now_ms == 0) {
        return SAN9_PING_INVALID_FRAME;
    }
    if (session->clock_faulted != 0U) {
        return SAN9_PING_CLOCK_ROLLBACK;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&mailbox->response_state,
            SAN9_MAILBOX_EMPTY,
            SAN9_MAILBOX_EMPTY) != SAN9_MAILBOX_EMPTY) {
        return SAN9_PING_MAILBOX_BUSY;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&mailbox->request_state,
            SAN9_MAILBOX_CLAIMED,
            SAN9_MAILBOX_READY) != SAN9_MAILBOX_READY) {
        return SAN9_PING_MAILBOX_BUSY;
    }
    MemoryBarrier();
    if (session->last_observed_time_ms != 0U
        && now_ms < session->last_observed_time_ms) {
        session->clock_faulted = 1U;
        InterlockedExchange(
            (volatile LONG *)&mailbox->request_state,
            SAN9_MAILBOX_SESSION_FAULT);
        return SAN9_PING_CLOCK_ROLLBACK;
    }
    session->last_observed_time_ms = now_ms;
    result = validate_authenticated_request(
        session,
        mailbox->request_frame,
        now_ms,
        &authenticated);
    if (!authenticated) {
        InterlockedExchange(
            (volatile LONG *)&mailbox->request_state,
            SAN9_MAILBOX_REJECTED_UNAUTHENTICATED);
        return result;
    }

    make_response(session, mailbox->request_frame, result, mailbox->response_frame);
    if (result == SAN9_PING_OK) {
        session->last_accepted_sequence = read_u64(mailbox->request_frame, OFFSET_SEQUENCE);
    }
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&mailbox->response_state, SAN9_MAILBOX_READY);
    InterlockedExchange((volatile LONG *)&mailbox->request_state, SAN9_MAILBOX_COMPLETE);
    return result;
}

int san9_ping_take_response(
    San9PingMailbox *mailbox,
    uint8_t output[SAN9_PING_FRAME_SIZE])
{
    if (mailbox == NULL || output == NULL) {
        return SAN9_PING_INVALID_FRAME;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&mailbox->response_state,
            SAN9_MAILBOX_CLAIMED,
            SAN9_MAILBOX_READY) != SAN9_MAILBOX_READY) {
        return SAN9_PING_MAILBOX_BUSY;
    }
    MemoryBarrier();
    memcpy(output, mailbox->response_frame, SAN9_PING_FRAME_SIZE);
    san9_secure_zero(mailbox->request_frame, SAN9_PING_FRAME_SIZE);
    san9_secure_zero(mailbox->response_frame, SAN9_PING_FRAME_SIZE);
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&mailbox->response_state, SAN9_MAILBOX_EMPTY);
    InterlockedExchange((volatile LONG *)&mailbox->request_state, SAN9_MAILBOX_EMPTY);
    return SAN9_PING_OK;
}

int san9_ping_verify_response(
    const San9PingSession *session,
    const uint8_t request[SAN9_PING_FRAME_SIZE],
    const uint8_t response[SAN9_PING_FRAME_SIZE])
{
    uint8_t expected_mac[SAN9_PING_DIGEST_SIZE];
    uint16_t state;
    uint32_t result;
    if (session == NULL || !session->initialized || request == NULL || response == NULL
        || !frame_header_is_valid(response, FRAME_KIND_RESPONSE, read_u16(response, OFFSET_STATE))) {
        return SAN9_PING_RESPONSE_INVALID;
    }
    state = read_u16(response, OFFSET_STATE);
    result = read_u32(response, OFFSET_RESULT_CODE);
    if (result > SAN9_PING_CLOCK_ROLLBACK
        || (state == FRAME_STATE_COMPLETED && result != SAN9_PING_OK)
        || (state == FRAME_STATE_REJECTED && result == SAN9_PING_OK)
        || (state != FRAME_STATE_COMPLETED && state != FRAME_STATE_REJECTED)
        || !san9_constant_time_equal(
            request + OFFSET_REQUEST_MAC,
            response + OFFSET_REQUEST_MAC,
            SAN9_PING_DIGEST_SIZE)
        || !san9_constant_time_equal(
            request + OFFSET_SESSION_NONCE,
            response + OFFSET_SESSION_NONCE,
            SAN9_PING_NONCE_SIZE)
        || !san9_constant_time_equal(
            request + OFFSET_REQUEST_ID,
            response + OFFSET_REQUEST_ID,
            SAN9_PING_REQUEST_ID_SIZE)
        || !san9_constant_time_equal(
            request + OFFSET_TARGET_DIGEST,
            response + OFFSET_TARGET_DIGEST,
            SAN9_PING_DIGEST_SIZE)
        || !san9_constant_time_equal(
            request + OFFSET_CONTEXT_DIGEST,
            response + OFFSET_CONTEXT_DIGEST,
            SAN9_PING_DIGEST_SIZE)
        || !san9_constant_time_equal(
            request + OFFSET_CHALLENGE,
            response + OFFSET_CHALLENGE,
            SAN9_PING_DIGEST_SIZE)
        || read_u64(request, OFFSET_SEQUENCE) != read_u64(response, OFFSET_SEQUENCE)
        || read_u64(request, OFFSET_ISSUED_AT) != read_u64(response, OFFSET_ISSUED_AT)
        || read_u64(request, OFFSET_EXPIRES_AT) != read_u64(response, OFFSET_EXPIRES_AT)) {
        return SAN9_PING_RESPONSE_INVALID;
    }
    compute_response_mac(session, response, expected_mac);
    if (!san9_constant_time_equal(
            expected_mac,
            response + OFFSET_RESPONSE_MAC,
            SAN9_PING_DIGEST_SIZE)) {
        san9_secure_zero(expected_mac, sizeof(expected_mac));
        return SAN9_PING_RESPONSE_INVALID;
    }
    san9_secure_zero(expected_mac, sizeof(expected_mac));
    return (int)result;
}
