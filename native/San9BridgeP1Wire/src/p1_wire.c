#include "san9_p1_wire.h"
#include "sha256.h"

#include <string.h>

static uint16_t read_u16(const uint8_t *bytes, size_t offset)
{
    return (uint16_t)((uint16_t)bytes[offset]
        | ((uint16_t)bytes[offset + 1U] << 8));
}

static uint32_t read_u32(const uint8_t *bytes, size_t offset)
{
    return (uint32_t)bytes[offset]
        | ((uint32_t)bytes[offset + 1U] << 8)
        | ((uint32_t)bytes[offset + 2U] << 16)
        | ((uint32_t)bytes[offset + 3U] << 24);
}

static uint64_t read_u64(const uint8_t *bytes, size_t offset)
{
    uint64_t value = 0ULL;
    size_t index;
    for (index = 0U; index < 8U; ++index) {
        value |= (uint64_t)bytes[offset + index] << (index * 8U);
    }
    return value;
}

static void write_u16(uint8_t *bytes, size_t offset, uint16_t value)
{
    bytes[offset] = (uint8_t)value;
    bytes[offset + 1U] = (uint8_t)(value >> 8);
}

static void write_u32(uint8_t *bytes, size_t offset, uint32_t value)
{
    bytes[offset] = (uint8_t)value;
    bytes[offset + 1U] = (uint8_t)(value >> 8);
    bytes[offset + 2U] = (uint8_t)(value >> 16);
    bytes[offset + 3U] = (uint8_t)(value >> 24);
}

static void write_u64(uint8_t *bytes, size_t offset, uint64_t value)
{
    size_t index;
    for (index = 0U; index < 8U; ++index) {
        bytes[offset + index] = (uint8_t)(value >> (index * 8U));
    }
}

static int range_is_zero(const uint8_t *bytes, size_t offset, size_t size)
{
    uint8_t aggregate = 0U;
    size_t index;
    for (index = 0U; index < size; ++index) {
        aggregate = (uint8_t)(aggregate | bytes[offset + index]);
    }
    return aggregate == 0U;
}

static int value_is_zero(const uint8_t *bytes, size_t size)
{
    return range_is_zero(bytes, 0U, size);
}

static int key_is_valid(const uint8_t *key, size_t size)
{
    return key != NULL && size == SAN9_P1_HMAC_KEY_SIZE && !value_is_zero(key, size);
}

static int ranges_overlap(const void *left, size_t left_size, const void *right, size_t right_size)
{
    uintptr_t left_start;
    uintptr_t right_start;
    uintptr_t left_end;
    uintptr_t right_end;
    if (left == NULL || right == NULL || left_size == 0U || right_size == 0U) {
        return 0;
    }
    left_start = (uintptr_t)left;
    right_start = (uintptr_t)right;
    if (left_start > UINTPTR_MAX - left_size || right_start > UINTPTR_MAX - right_size) {
        return 1;
    }
    left_end = left_start + left_size;
    right_end = right_start + right_size;
    return left_start < right_end && right_start < left_end;
}

static uint32_t crc32_compute(const uint8_t *bytes, size_t size)
{
    uint32_t crc = 0xffffffffU;
    size_t index;
    for (index = 0U; index < size; ++index) {
        uint8_t value = index >= SAN9_P1_CRC32_OFFSET
            && index < SAN9_P1_CRC32_OFFSET + sizeof(uint32_t)
            ? 0U
            : bytes[index];
        unsigned bit;
        crc ^= value;
        for (bit = 0U; bit < 8U; ++bit) {
            uint32_t mask = (uint32_t)(0U - (crc & 1U));
            crc = (crc >> 1) ^ (0xedb88320U & mask);
        }
    }
    return ~crc;
}

static void compute_hmac(
    const uint8_t bytes[SAN9_P1_FRAME_SIZE],
    const uint8_t key[SAN9_P1_HMAC_KEY_SIZE],
    uint8_t output[SAN9_P1_DIGEST_SIZE])
{
    uint8_t canonical[SAN9_P1_FRAME_SIZE];
    memcpy(canonical, bytes, sizeof(canonical));
    memset(canonical + SAN9_P1_CRC32_OFFSET, 0, sizeof(uint32_t));
    memset(canonical + SAN9_P1_HMAC_OFFSET, 0, SAN9_P1_DIGEST_SIZE);
    san9_p1_hmac_sha256(key, SAN9_P1_HMAC_KEY_SIZE, canonical, sizeof(canonical), output);
    san9_p1_secure_zero(canonical, sizeof(canonical));
}

static int required(const uint8_t *value, size_t size)
{
    return !value_is_zero(value, size);
}

static San9P1DecodeStatus validate_frame(const San9P1Frame *frame)
{
    int request;
    int response;
    if (frame == NULL) {
        return SAN9_P1_DECODE_REQUIRED_FIELD_INVALID;
    }

    request = frame->kind == SAN9_P1_PING_REQUEST && frame->state == SAN9_P1_PENDING;
    response = frame->kind == SAN9_P1_PING_RESPONSE
        && (frame->state == SAN9_P1_COMPLETED || frame->state == SAN9_P1_REJECTED);
    if (!request && !response) {
        return SAN9_P1_DECODE_KIND_STATE_INVALID;
    }

    if (frame->flags != 0U
        || frame->sequence == 0ULL
        || frame->game_pid == 0U
        || frame->main_tid == 0U
        || frame->game_hwnd == 0U
        || frame->helper_pid == 0U
        || frame->easy_loader_pid == 0U
        || frame->game_generation == 0ULL
        || frame->helper_generation == 0ULL
        || frame->easy_loader_generation == 0ULL
        || !required(frame->session_nonce, sizeof(frame->session_nonce))
        || !required(frame->request_id, sizeof(frame->request_id))
        || !required(frame->easy_epoch_nonce, sizeof(frame->easy_epoch_nonce))
        || !required(frame->build_digest, sizeof(frame->build_digest))
        || !required(frame->profile_digest, sizeof(frame->profile_digest))
        || !required(frame->manifest_digest, sizeof(frame->manifest_digest))
        || !required(frame->easy_epoch_digest, sizeof(frame->easy_epoch_digest))
        || !required(frame->easy_ticket_digest, sizeof(frame->easy_ticket_digest))
        || !required(frame->context_digest, sizeof(frame->context_digest))
        || !required(frame->bridge_digest, sizeof(frame->bridge_digest))
        || !required(frame->mapping_digest, sizeof(frame->mapping_digest))
        || !required(frame->challenge_digest, sizeof(frame->challenge_digest))) {
        return SAN9_P1_DECODE_REQUIRED_FIELD_INVALID;
    }

    if (frame->issued_at_ms == 0ULL
        || frame->expires_at_ms <= frame->issued_at_ms
        || frame->expires_at_ms - frame->issued_at_ms > SAN9_P1_MAXIMUM_LIFETIME_MS) {
        return SAN9_P1_DECODE_LIFETIME_INVALID;
    }

    if (request && (frame->result_code != 0U
        || !value_is_zero(frame->result_digest, sizeof(frame->result_digest)))) {
        return SAN9_P1_DECODE_REQUEST_SHAPE_INVALID;
    }

    if (response && (value_is_zero(frame->result_digest, sizeof(frame->result_digest))
        || (frame->state == SAN9_P1_COMPLETED && frame->result_code != 0U)
        || (frame->state == SAN9_P1_REJECTED && frame->result_code == 0U))) {
        return SAN9_P1_DECODE_RESPONSE_SHAPE_INVALID;
    }

    return SAN9_P1_DECODE_ACCEPTED;
}

int san9_p1_encode(
    const San9P1Frame *frame,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint8_t *output,
    size_t output_size)
{
    uint8_t mac[SAN9_P1_DIGEST_SIZE];
    size_t clear_size;
    if (output == NULL) {
        return 0;
    }
    /* Reject aliases before touching output: clearing an aliased output would
       silently mutate the frame or the authentication key used by this call. */
    if (ranges_overlap(frame, sizeof(*frame), output, output_size)
        || ranges_overlap(hmac_key, hmac_key_size, output, output_size)) {
        return 0;
    }
    clear_size = output_size < SAN9_P1_FRAME_SIZE ? output_size : SAN9_P1_FRAME_SIZE;
    if (clear_size != 0U) {
        memset(output, 0, clear_size);
    }
    if (output_size != SAN9_P1_FRAME_SIZE
        || !key_is_valid(hmac_key, hmac_key_size)
        || validate_frame(frame) != SAN9_P1_DECODE_ACCEPTED) {
        return 0;
    }

    write_u32(output, SAN9_P1_MAGIC_OFFSET, SAN9_P1_MAGIC);
    write_u16(output, SAN9_P1_SCHEMA_MAJOR_OFFSET, SAN9_P1_SCHEMA_MAJOR);
    write_u16(output, SAN9_P1_SCHEMA_MINOR_OFFSET, SAN9_P1_SCHEMA_MINOR);
    write_u32(output, SAN9_P1_DECLARED_SIZE_OFFSET, SAN9_P1_FRAME_SIZE);
    write_u16(output, SAN9_P1_KIND_OFFSET, frame->kind);
    write_u16(output, SAN9_P1_STATE_OFFSET, frame->state);
    write_u32(output, SAN9_P1_FLAGS_OFFSET, frame->flags);
    write_u32(output, SAN9_P1_RESULT_CODE_OFFSET, frame->result_code);
    write_u64(output, SAN9_P1_SEQUENCE_OFFSET, frame->sequence);
    write_u64(output, SAN9_P1_ISSUED_AT_MS_OFFSET, frame->issued_at_ms);
    write_u64(output, SAN9_P1_EXPIRES_AT_MS_OFFSET, frame->expires_at_ms);
    write_u32(output, SAN9_P1_GAME_PID_OFFSET, frame->game_pid);
    write_u32(output, SAN9_P1_MAIN_TID_OFFSET, frame->main_tid);
    write_u32(output, SAN9_P1_GAME_HWND_OFFSET, frame->game_hwnd);
    write_u32(output, SAN9_P1_HELPER_PID_OFFSET, frame->helper_pid);
    write_u32(output, SAN9_P1_EASY_LOADER_PID_OFFSET, frame->easy_loader_pid);
    write_u64(output, SAN9_P1_GAME_GENERATION_OFFSET, frame->game_generation);
    write_u64(output, SAN9_P1_HELPER_GENERATION_OFFSET, frame->helper_generation);
    write_u64(output, SAN9_P1_EASY_LOADER_GENERATION_OFFSET, frame->easy_loader_generation);
    memcpy(output + SAN9_P1_SESSION_NONCE_OFFSET, frame->session_nonce, sizeof(frame->session_nonce));
    memcpy(output + SAN9_P1_REQUEST_ID_OFFSET, frame->request_id, sizeof(frame->request_id));
    memcpy(output + SAN9_P1_EASY_EPOCH_NONCE_OFFSET, frame->easy_epoch_nonce, sizeof(frame->easy_epoch_nonce));
    memcpy(output + SAN9_P1_BUILD_DIGEST_OFFSET, frame->build_digest, sizeof(frame->build_digest));
    memcpy(output + SAN9_P1_PROFILE_DIGEST_OFFSET, frame->profile_digest, sizeof(frame->profile_digest));
    memcpy(output + SAN9_P1_MANIFEST_DIGEST_OFFSET, frame->manifest_digest, sizeof(frame->manifest_digest));
    memcpy(output + SAN9_P1_EASY_EPOCH_DIGEST_OFFSET, frame->easy_epoch_digest, sizeof(frame->easy_epoch_digest));
    memcpy(output + SAN9_P1_EASY_TICKET_DIGEST_OFFSET, frame->easy_ticket_digest, sizeof(frame->easy_ticket_digest));
    memcpy(output + SAN9_P1_CONTEXT_DIGEST_OFFSET, frame->context_digest, sizeof(frame->context_digest));
    memcpy(output + SAN9_P1_BRIDGE_DIGEST_OFFSET, frame->bridge_digest, sizeof(frame->bridge_digest));
    memcpy(output + SAN9_P1_MAPPING_DIGEST_OFFSET, frame->mapping_digest, sizeof(frame->mapping_digest));
    memcpy(output + SAN9_P1_CHALLENGE_DIGEST_OFFSET, frame->challenge_digest, sizeof(frame->challenge_digest));
    memcpy(output + SAN9_P1_RESULT_DIGEST_OFFSET, frame->result_digest, sizeof(frame->result_digest));

    compute_hmac(output, hmac_key, mac);
    memcpy(output + SAN9_P1_HMAC_OFFSET, mac, sizeof(mac));
    san9_p1_secure_zero(mac, sizeof(mac));
    write_u32(output, SAN9_P1_CRC32_OFFSET, crc32_compute(output, SAN9_P1_FRAME_SIZE));
    return 1;
}

San9P1DecodeStatus san9_p1_decode(
    const uint8_t *bytes,
    size_t size,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    San9P1Frame *frame)
{
    uint8_t expected_mac[SAN9_P1_DIGEST_SIZE];
    San9P1Frame decoded;
    San9P1DecodeStatus validation;
    if (frame == NULL) {
        return SAN9_P1_DECODE_NULL_FRAME;
    }
    memset(frame, 0, sizeof(*frame));
    if (bytes == NULL) {
        return SAN9_P1_DECODE_NULL_FRAME;
    }
    if (size != SAN9_P1_FRAME_SIZE) {
        return SAN9_P1_DECODE_SIZE_MISMATCH;
    }
    if (!key_is_valid(hmac_key, hmac_key_size)) {
        return SAN9_P1_DECODE_KEY_INVALID;
    }
    if (read_u32(bytes, SAN9_P1_MAGIC_OFFSET) != SAN9_P1_MAGIC) {
        return SAN9_P1_DECODE_MAGIC_MISMATCH;
    }
    if (read_u16(bytes, SAN9_P1_SCHEMA_MAJOR_OFFSET) != SAN9_P1_SCHEMA_MAJOR
        || read_u16(bytes, SAN9_P1_SCHEMA_MINOR_OFFSET) != SAN9_P1_SCHEMA_MINOR) {
        return SAN9_P1_DECODE_SCHEMA_MISMATCH;
    }
    if (read_u32(bytes, SAN9_P1_DECLARED_SIZE_OFFSET) != SAN9_P1_FRAME_SIZE) {
        return SAN9_P1_DECODE_DECLARED_SIZE_MISMATCH;
    }
    if (read_u32(bytes, SAN9_P1_CRC32_OFFSET) != crc32_compute(bytes, size)) {
        return SAN9_P1_DECODE_CRC_MISMATCH;
    }

    compute_hmac(bytes, hmac_key, expected_mac);
    if (!san9_p1_constant_time_equal(
            expected_mac,
            bytes + SAN9_P1_HMAC_OFFSET,
            SAN9_P1_DIGEST_SIZE)) {
        san9_p1_secure_zero(expected_mac, sizeof(expected_mac));
        return SAN9_P1_DECODE_HMAC_MISMATCH;
    }
    san9_p1_secure_zero(expected_mac, sizeof(expected_mac));

    if (read_u32(bytes, SAN9_P1_FLAGS_OFFSET) != 0U
        || !range_is_zero(bytes, SAN9_P1_RESERVED_HEADER_OFFSET, SAN9_P1_RESERVED_HEADER_SIZE)
        || !range_is_zero(bytes, SAN9_P1_RESERVED_BINDING_OFFSET, SAN9_P1_RESERVED_BINDING_SIZE)
        || !range_is_zero(bytes, SAN9_P1_RESERVED_TAIL_OFFSET, SAN9_P1_RESERVED_TAIL_SIZE)) {
        return SAN9_P1_DECODE_RESERVED_OR_FLAGS_INVALID;
    }

    memset(&decoded, 0, sizeof(decoded));
    decoded.kind = read_u16(bytes, SAN9_P1_KIND_OFFSET);
    decoded.state = read_u16(bytes, SAN9_P1_STATE_OFFSET);
    decoded.flags = read_u32(bytes, SAN9_P1_FLAGS_OFFSET);
    decoded.result_code = read_u32(bytes, SAN9_P1_RESULT_CODE_OFFSET);
    decoded.sequence = read_u64(bytes, SAN9_P1_SEQUENCE_OFFSET);
    decoded.issued_at_ms = read_u64(bytes, SAN9_P1_ISSUED_AT_MS_OFFSET);
    decoded.expires_at_ms = read_u64(bytes, SAN9_P1_EXPIRES_AT_MS_OFFSET);
    decoded.game_pid = read_u32(bytes, SAN9_P1_GAME_PID_OFFSET);
    decoded.main_tid = read_u32(bytes, SAN9_P1_MAIN_TID_OFFSET);
    decoded.game_hwnd = read_u32(bytes, SAN9_P1_GAME_HWND_OFFSET);
    decoded.helper_pid = read_u32(bytes, SAN9_P1_HELPER_PID_OFFSET);
    decoded.easy_loader_pid = read_u32(bytes, SAN9_P1_EASY_LOADER_PID_OFFSET);
    decoded.game_generation = read_u64(bytes, SAN9_P1_GAME_GENERATION_OFFSET);
    decoded.helper_generation = read_u64(bytes, SAN9_P1_HELPER_GENERATION_OFFSET);
    decoded.easy_loader_generation = read_u64(bytes, SAN9_P1_EASY_LOADER_GENERATION_OFFSET);
    memcpy(decoded.session_nonce, bytes + SAN9_P1_SESSION_NONCE_OFFSET, sizeof(decoded.session_nonce));
    memcpy(decoded.request_id, bytes + SAN9_P1_REQUEST_ID_OFFSET, sizeof(decoded.request_id));
    memcpy(decoded.easy_epoch_nonce, bytes + SAN9_P1_EASY_EPOCH_NONCE_OFFSET, sizeof(decoded.easy_epoch_nonce));
    memcpy(decoded.build_digest, bytes + SAN9_P1_BUILD_DIGEST_OFFSET, sizeof(decoded.build_digest));
    memcpy(decoded.profile_digest, bytes + SAN9_P1_PROFILE_DIGEST_OFFSET, sizeof(decoded.profile_digest));
    memcpy(decoded.manifest_digest, bytes + SAN9_P1_MANIFEST_DIGEST_OFFSET, sizeof(decoded.manifest_digest));
    memcpy(decoded.easy_epoch_digest, bytes + SAN9_P1_EASY_EPOCH_DIGEST_OFFSET, sizeof(decoded.easy_epoch_digest));
    memcpy(decoded.easy_ticket_digest, bytes + SAN9_P1_EASY_TICKET_DIGEST_OFFSET, sizeof(decoded.easy_ticket_digest));
    memcpy(decoded.context_digest, bytes + SAN9_P1_CONTEXT_DIGEST_OFFSET, sizeof(decoded.context_digest));
    memcpy(decoded.bridge_digest, bytes + SAN9_P1_BRIDGE_DIGEST_OFFSET, sizeof(decoded.bridge_digest));
    memcpy(decoded.mapping_digest, bytes + SAN9_P1_MAPPING_DIGEST_OFFSET, sizeof(decoded.mapping_digest));
    memcpy(decoded.challenge_digest, bytes + SAN9_P1_CHALLENGE_DIGEST_OFFSET, sizeof(decoded.challenge_digest));
    memcpy(decoded.result_digest, bytes + SAN9_P1_RESULT_DIGEST_OFFSET, sizeof(decoded.result_digest));

    validation = validate_frame(&decoded);
    if (validation != SAN9_P1_DECODE_ACCEPTED) {
        san9_p1_secure_zero(&decoded, sizeof(decoded));
        return validation;
    }
    *frame = decoded;
    return SAN9_P1_DECODE_ACCEPTED;
}

int san9_p1_constant_time_equal(const uint8_t *left, const uint8_t *right, size_t size)
{
    uint8_t difference = 0U;
    size_t index;
    if (left == NULL || right == NULL) {
        return 0;
    }
    for (index = 0U; index < size; ++index) {
        difference = (uint8_t)(difference | (uint8_t)(left[index] ^ right[index]));
    }
    return difference == 0U;
}

static int binding_matches(const San9P1Frame *expected, const San9P1Frame *actual)
{
    return expected->game_pid == actual->game_pid
        && expected->main_tid == actual->main_tid
        && expected->game_hwnd == actual->game_hwnd
        && expected->helper_pid == actual->helper_pid
        && expected->easy_loader_pid == actual->easy_loader_pid
        && expected->game_generation == actual->game_generation
        && expected->helper_generation == actual->helper_generation
        && expected->easy_loader_generation == actual->easy_loader_generation
        && san9_p1_constant_time_equal(expected->session_nonce, actual->session_nonce, sizeof(expected->session_nonce))
        && san9_p1_constant_time_equal(expected->easy_epoch_nonce, actual->easy_epoch_nonce, sizeof(expected->easy_epoch_nonce))
        && san9_p1_constant_time_equal(expected->build_digest, actual->build_digest, sizeof(expected->build_digest))
        && san9_p1_constant_time_equal(expected->profile_digest, actual->profile_digest, sizeof(expected->profile_digest))
        && san9_p1_constant_time_equal(expected->manifest_digest, actual->manifest_digest, sizeof(expected->manifest_digest))
        && san9_p1_constant_time_equal(expected->easy_epoch_digest, actual->easy_epoch_digest, sizeof(expected->easy_epoch_digest))
        && san9_p1_constant_time_equal(expected->easy_ticket_digest, actual->easy_ticket_digest, sizeof(expected->easy_ticket_digest))
        && san9_p1_constant_time_equal(expected->context_digest, actual->context_digest, sizeof(expected->context_digest))
        && san9_p1_constant_time_equal(expected->bridge_digest, actual->bridge_digest, sizeof(expected->bridge_digest))
        && san9_p1_constant_time_equal(expected->mapping_digest, actual->mapping_digest, sizeof(expected->mapping_digest));
}

int san9_p1_request_gate_initialize(San9P1RequestGate *gate, const San9P1Frame *binding)
{
    if (gate == NULL) {
        return 0;
    }
    memset(gate, 0, sizeof(*gate));
    if (validate_frame(binding) != SAN9_P1_DECODE_ACCEPTED
        || binding->kind != SAN9_P1_PING_REQUEST
        || binding->state != SAN9_P1_PENDING) {
        return 0;
    }
    gate->binding = *binding;
    return 1;
}

static San9P1GateStatus request_gate_accept_decoded(
    San9P1RequestGate *gate,
    const San9P1Frame *request,
    uint64_t now_ms)
{
    uint32_t index;
    if (gate == NULL) {
        return SAN9_P1_GATE_WRONG_SHAPE;
    }
    if (gate->clock_faulted != 0U) {
        return SAN9_P1_GATE_CLOCK_FAULTED;
    }
    if (gate->has_observed_now != 0U && now_ms < gate->last_observed_now) {
        gate->clock_faulted = 1U;
        return SAN9_P1_GATE_CLOCK_ROLLBACK;
    }
    gate->has_observed_now = 1U;
    gate->last_observed_now = now_ms;
    if (validate_frame(request) != SAN9_P1_DECODE_ACCEPTED
        || request->kind != SAN9_P1_PING_REQUEST
        || request->state != SAN9_P1_PENDING) {
        return SAN9_P1_GATE_WRONG_SHAPE;
    }
    if (!binding_matches(&gate->binding, request)) {
        return SAN9_P1_GATE_BINDING_MISMATCH;
    }
    if (gate->accepted_count >= SAN9_P1_MAXIMUM_SESSION_PINGS) {
        return SAN9_P1_GATE_BUDGET_EXHAUSTED;
    }
    if (request->sequence <= gate->last_sequence) {
        return SAN9_P1_GATE_REPLAY;
    }
    if (request->sequence != gate->last_sequence + 1ULL) {
        return SAN9_P1_GATE_SEQUENCE_GAP;
    }
    if (now_ms < request->issued_at_ms) {
        return SAN9_P1_GATE_FROM_FUTURE;
    }
    if (now_ms >= request->expires_at_ms) {
        return SAN9_P1_GATE_EXPIRED;
    }
    for (index = 0U; index < gate->accepted_count; ++index) {
        if (san9_p1_constant_time_equal(
                gate->accepted_request_ids[index],
                request->request_id,
                SAN9_P1_NONCE_SIZE)) {
            return SAN9_P1_GATE_REQUEST_ID_REPLAY;
        }
    }
    memcpy(
        gate->accepted_request_ids[gate->accepted_count],
        request->request_id,
        SAN9_P1_NONCE_SIZE);
    ++gate->accepted_count;
    gate->last_sequence = request->sequence;
    return SAN9_P1_GATE_ACCEPTED;
}

San9P1GateStatus san9_p1_decode_and_accept(
    San9P1RequestGate *gate,
    const uint8_t *bytes,
    size_t size,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9P1DecodeStatus *decode_status,
    San9P1Frame *accepted_request)
{
    San9P1Frame decoded;
    San9P1DecodeStatus status;
    San9P1GateStatus gate_status;
    if (accepted_request == NULL) {
        return SAN9_P1_GATE_AUTHENTICATED_FRAME_REJECTED;
    }
    memset(accepted_request, 0, sizeof(*accepted_request));
    status = san9_p1_decode(bytes, size, hmac_key, hmac_key_size, &decoded);
    if (decode_status != NULL) {
        *decode_status = status;
    }
    if (status != SAN9_P1_DECODE_ACCEPTED) {
        return SAN9_P1_GATE_AUTHENTICATED_FRAME_REJECTED;
    }
    gate_status = request_gate_accept_decoded(gate, &decoded, now_ms);
    if (gate_status == SAN9_P1_GATE_ACCEPTED) {
        *accepted_request = decoded;
    } else {
        san9_p1_secure_zero(&decoded, sizeof(decoded));
    }
    return gate_status;
}

static int response_verify_decoded(const San9P1Frame *request, const San9P1Frame *response)
{
    if (validate_frame(request) != SAN9_P1_DECODE_ACCEPTED
        || validate_frame(response) != SAN9_P1_DECODE_ACCEPTED
        || request->kind != SAN9_P1_PING_REQUEST
        || request->state != SAN9_P1_PENDING
        || response->kind != SAN9_P1_PING_RESPONSE
        || (response->state != SAN9_P1_COMPLETED && response->state != SAN9_P1_REJECTED)
        || !binding_matches(request, response)
        || request->sequence != response->sequence
        || request->issued_at_ms != response->issued_at_ms
        || request->expires_at_ms != response->expires_at_ms
        || !san9_p1_constant_time_equal(request->request_id, response->request_id, SAN9_P1_NONCE_SIZE)
        || !san9_p1_constant_time_equal(request->challenge_digest, response->challenge_digest, SAN9_P1_DIGEST_SIZE)) {
        return 0;
    }
    return response->state == SAN9_P1_COMPLETED
        ? response->result_code == 0U
        : response->result_code != 0U;
}

int san9_p1_decode_and_verify_response(
    const San9P1Frame *request,
    const uint8_t *bytes,
    size_t size,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    San9P1DecodeStatus *decode_status,
    San9P1Frame *verified_response)
{
    San9P1Frame decoded;
    San9P1DecodeStatus status;
    if (verified_response == NULL) {
        return 0;
    }
    memset(verified_response, 0, sizeof(*verified_response));
    status = san9_p1_decode(bytes, size, hmac_key, hmac_key_size, &decoded);
    if (decode_status != NULL) {
        *decode_status = status;
    }
    if (status != SAN9_P1_DECODE_ACCEPTED || !response_verify_decoded(request, &decoded)) {
        san9_p1_secure_zero(&decoded, sizeof(decoded));
        return 0;
    }
    *verified_response = decoded;
    return 1;
}
