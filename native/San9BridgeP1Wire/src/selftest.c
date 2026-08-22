#include "san9_p1_wire.h"
#include "sha256.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static unsigned checks;
static unsigned failures;
static const char *EXPECTED_GOLDEN_SHA256 =
    "acadf1f1c5cf70672484632e32865871058e3193b84ffc5c233990985fbe80d5";

static void check(int condition, const char *name)
{
    ++checks;
    if (!condition) {
        ++failures;
        fprintf(stderr, "FAILED %s\n", name);
    }
}

static void check_indexed(int condition, const char *prefix, size_t index)
{
    char name[96];
    (void)snprintf(name, sizeof(name), "%s%lu", prefix, (unsigned long)index);
    check(condition, name);
}

static void fill(uint8_t *value, size_t size, unsigned seed)
{
    size_t index;
    for (index = 0U; index < size; ++index) {
        value[index] = (uint8_t)(seed + (index * 17U));
    }
}

static void create_key(uint8_t key[SAN9_P1_HMAC_KEY_SIZE])
{
    fill(key, SAN9_P1_HMAC_KEY_SIZE, 0xd0U);
}

static San9P1Frame create_request(void)
{
    San9P1Frame frame;
    memset(&frame, 0, sizeof(frame));
    frame.kind = SAN9_P1_PING_REQUEST;
    frame.state = SAN9_P1_PENDING;
    frame.sequence = 1ULL;
    frame.issued_at_ms = 1000000ULL;
    frame.expires_at_ms = 1004000ULL;
    frame.game_pid = 0x11223344U;
    frame.main_tid = 0x12345678U;
    frame.game_hwnd = 0x00123456U;
    frame.helper_pid = 0x55667788U;
    frame.easy_loader_pid = 0x01020304U;
    frame.game_generation = 0x0102030405060708ULL;
    frame.helper_generation = 0x1112131415161718ULL;
    frame.easy_loader_generation = 0x2122232425262728ULL;
    fill(frame.session_nonce, sizeof(frame.session_nonce), 0x10U);
    fill(frame.request_id, sizeof(frame.request_id), 0x20U);
    fill(frame.easy_epoch_nonce, sizeof(frame.easy_epoch_nonce), 0x30U);
    fill(frame.build_digest, sizeof(frame.build_digest), 0x40U);
    fill(frame.profile_digest, sizeof(frame.profile_digest), 0x50U);
    fill(frame.manifest_digest, sizeof(frame.manifest_digest), 0x60U);
    fill(frame.easy_epoch_digest, sizeof(frame.easy_epoch_digest), 0x70U);
    fill(frame.easy_ticket_digest, sizeof(frame.easy_ticket_digest), 0x80U);
    fill(frame.context_digest, sizeof(frame.context_digest), 0x90U);
    fill(frame.bridge_digest, sizeof(frame.bridge_digest), 0xa0U);
    fill(frame.mapping_digest, sizeof(frame.mapping_digest), 0xb0U);
    fill(frame.challenge_digest, sizeof(frame.challenge_digest), 0xc0U);
    return frame;
}

static San9P1Frame create_response(const San9P1Frame *request)
{
    San9P1Frame response = *request;
    response.kind = SAN9_P1_PING_RESPONSE;
    response.state = SAN9_P1_COMPLETED;
    response.result_code = 0U;
    fill(response.result_digest, sizeof(response.result_digest), 0xe0U);
    return response;
}

static void advance_request(San9P1Frame *request, uint64_t sequence)
{
    request->sequence = sequence;
    request->issued_at_ms = 1000000ULL + (sequence * 10ULL);
    request->expires_at_ms = request->issued_at_ms + 4000ULL;
    fill(request->request_id, sizeof(request->request_id), (unsigned)(0x20ULL + sequence));
}

static uint32_t test_crc32(const uint8_t *bytes, size_t size)
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

static void write_u32(uint8_t *bytes, size_t offset, uint32_t value)
{
    bytes[offset] = (uint8_t)value;
    bytes[offset + 1U] = (uint8_t)(value >> 8);
    bytes[offset + 2U] = (uint8_t)(value >> 16);
    bytes[offset + 3U] = (uint8_t)(value >> 24);
}

static void write_u16(uint8_t *bytes, size_t offset, uint16_t value)
{
    bytes[offset] = (uint8_t)value;
    bytes[offset + 1U] = (uint8_t)(value >> 8);
}

static void write_u64(uint8_t *bytes, size_t offset, uint64_t value)
{
    size_t index;
    for (index = 0U; index < 8U; ++index) {
        bytes[offset + index] = (uint8_t)(value >> (index * 8U));
    }
}

static void reauthenticate(
    uint8_t frame[SAN9_P1_FRAME_SIZE],
    const uint8_t key[SAN9_P1_HMAC_KEY_SIZE])
{
    uint8_t digest[SAN9_P1_DIGEST_SIZE];
    memset(frame + SAN9_P1_CRC32_OFFSET, 0, sizeof(uint32_t));
    memset(frame + SAN9_P1_HMAC_OFFSET, 0, SAN9_P1_DIGEST_SIZE);
    san9_p1_hmac_sha256(key, SAN9_P1_HMAC_KEY_SIZE, frame, SAN9_P1_FRAME_SIZE, digest);
    memcpy(frame + SAN9_P1_HMAC_OFFSET, digest, sizeof(digest));
    san9_p1_secure_zero(digest, sizeof(digest));
    write_u32(frame, SAN9_P1_CRC32_OFFSET, test_crc32(frame, SAN9_P1_FRAME_SIZE));
}

static void sha256_hex(const uint8_t *bytes, size_t size, char output[65])
{
    static const char HEX[] = "0123456789abcdef";
    San9P1Sha256Context context;
    uint8_t digest[32];
    size_t index;
    san9_p1_sha256_initialize(&context);
    san9_p1_sha256_update(&context, bytes, size);
    san9_p1_sha256_finish(&context, digest);
    for (index = 0U; index < sizeof(digest); ++index) {
        output[index * 2U] = HEX[digest[index] >> 4];
        output[index * 2U + 1U] = HEX[digest[index] & 0x0fU];
    }
    output[64] = '\0';
    san9_p1_secure_zero(digest, sizeof(digest));
}

static void test_crypto_vectors(void)
{
    static const uint8_t EXPECTED_SHA256[32] = {
        0xbaU, 0x78U, 0x16U, 0xbfU, 0x8fU, 0x01U, 0xcfU, 0xeaU,
        0x41U, 0x41U, 0x40U, 0xdeU, 0x5dU, 0xaeU, 0x22U, 0x23U,
        0xb0U, 0x03U, 0x61U, 0xa3U, 0x96U, 0x17U, 0x7aU, 0x9cU,
        0xb4U, 0x10U, 0xffU, 0x61U, 0xf2U, 0x00U, 0x15U, 0xadU
    };
    static const uint8_t EXPECTED_HMAC[32] = {
        0xb0U, 0x34U, 0x4cU, 0x61U, 0xd8U, 0xdbU, 0x38U, 0x53U,
        0x5cU, 0xa8U, 0xafU, 0xceU, 0xafU, 0x0bU, 0xf1U, 0x2bU,
        0x88U, 0x1dU, 0xc2U, 0x00U, 0xc9U, 0x83U, 0x3dU, 0xa7U,
        0x26U, 0xe9U, 0x37U, 0x6cU, 0x2eU, 0x32U, 0xcfU, 0xf7U
    };
    static const uint8_t ABC[] = { 'a', 'b', 'c' };
    static const uint8_t HI_THERE[] = "Hi There";
    uint8_t key[20];
    uint8_t digest[32];
    San9P1Sha256Context context;
    memset(key, 0x0b, sizeof(key));
    san9_p1_sha256_initialize(&context);
    san9_p1_sha256_update(&context, ABC, sizeof(ABC));
    san9_p1_sha256_finish(&context, digest);
    check(san9_p1_constant_time_equal(digest, EXPECTED_SHA256, sizeof(digest)), "sha256-vector");
    san9_p1_hmac_sha256(key, sizeof(key), HI_THERE, sizeof(HI_THERE) - 1U, digest);
    check(san9_p1_constant_time_equal(digest, EXPECTED_HMAC, sizeof(digest)), "hmac-vector");
    digest[0] ^= 1U;
    check(!san9_p1_constant_time_equal(digest, EXPECTED_HMAC, sizeof(digest)), "constant-time-negative");
    san9_p1_secure_zero(digest, sizeof(digest));
}

static void test_layout_and_golden(uint8_t golden[SAN9_P1_FRAME_SIZE])
{
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    San9P1Frame request = create_request();
    San9P1Frame decoded;
    uint8_t roundtrip[SAN9_P1_FRAME_SIZE];
    char hash[65];
    create_key(key);
    check(SAN9_P1_FRAME_SIZE == 512U, "frame-size");
    check(SAN9_P1_HMAC_OFFSET + SAN9_P1_DIGEST_SIZE == SAN9_P1_RESERVED_TAIL_OFFSET, "hmac-boundary");
    check(SAN9_P1_RESERVED_TAIL_OFFSET + SAN9_P1_RESERVED_TAIL_SIZE == SAN9_P1_FRAME_SIZE, "tail-boundary");
    check(SAN9_P1_LIVE_AUTHORIZATION == 0, "live-authorization-false");
    check(SAN9_P1_CONTAINS_BUSINESS_FIELDS == 0, "no-business-fields");
    check(SAN9_P1_CONTAINS_NATIVE_ADDRESSES == 0, "no-native-addresses");
    check(san9_p1_encode(&request, key, sizeof(key), golden, SAN9_P1_FRAME_SIZE), "golden-encode");
    check(san9_p1_decode(golden, SAN9_P1_FRAME_SIZE, key, sizeof(key), &decoded)
        == SAN9_P1_DECODE_ACCEPTED, "golden-decode");
    check(decoded.sequence == 1ULL, "golden-sequence");
    check(san9_p1_encode(&decoded, key, sizeof(key), roundtrip, sizeof(roundtrip)), "golden-reencode");
    check(san9_p1_constant_time_equal(golden, roundtrip, sizeof(roundtrip)), "golden-canonical-roundtrip");
    sha256_hex(golden, SAN9_P1_FRAME_SIZE, hash);
    if (strcmp(EXPECTED_GOLDEN_SHA256, "PENDING") != 0) {
        check(strcmp(hash, EXPECTED_GOLDEN_SHA256) == 0, "golden-sha256");
    }
}

static void test_mutations(const uint8_t golden[SAN9_P1_FRAME_SIZE])
{
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t mutated[SAN9_P1_FRAME_SIZE];
    San9P1Frame decoded;
    size_t offset;
    create_key(key);
    for (offset = 0U; offset < SAN9_P1_FRAME_SIZE; ++offset) {
        memcpy(mutated, golden, sizeof(mutated));
        mutated[offset] ^= 1U;
        check_indexed(
            san9_p1_decode(mutated, sizeof(mutated), key, sizeof(key), &decoded) != SAN9_P1_DECODE_ACCEPTED,
            "raw-mutation-",
            offset);
    }
    for (offset = 0U; offset < SAN9_P1_FRAME_SIZE; ++offset) {
        if (offset >= SAN9_P1_CRC32_OFFSET && offset < SAN9_P1_CRC32_OFFSET + sizeof(uint32_t)) {
            continue;
        }
        memcpy(mutated, golden, sizeof(mutated));
        mutated[offset] ^= 1U;
        write_u32(mutated, SAN9_P1_CRC32_OFFSET, test_crc32(mutated, sizeof(mutated)));
        check_indexed(
            san9_p1_decode(mutated, sizeof(mutated), key, sizeof(key), &decoded) != SAN9_P1_DECODE_ACCEPTED,
            "crc-repaired-mutation-",
            offset);
    }
}

static void test_lengths_and_keys(const uint8_t golden[SAN9_P1_FRAME_SIZE])
{
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t wrong_key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t zero_key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t overlong[SAN9_P1_FRAME_SIZE + 1U];
    uint8_t double_frame[SAN9_P1_FRAME_SIZE * 2U];
    San9P1Frame decoded;
    size_t size;
    create_key(key);
    memset(zero_key, 0, sizeof(zero_key));
    for (size = 0U; size < SAN9_P1_FRAME_SIZE; ++size) {
        check_indexed(
            san9_p1_decode(golden, size, key, sizeof(key), &decoded) == SAN9_P1_DECODE_SIZE_MISMATCH,
            "truncation-",
            size);
    }
    memcpy(overlong, golden, SAN9_P1_FRAME_SIZE);
    overlong[SAN9_P1_FRAME_SIZE] = 0U;
    check(san9_p1_decode(overlong, sizeof(overlong), key, sizeof(key), &decoded)
        == SAN9_P1_DECODE_SIZE_MISMATCH, "overlong-513");
    memcpy(double_frame, golden, SAN9_P1_FRAME_SIZE);
    memset(double_frame + SAN9_P1_FRAME_SIZE, 0, SAN9_P1_FRAME_SIZE);
    check(san9_p1_decode(double_frame, sizeof(double_frame), key, sizeof(key), &decoded)
        == SAN9_P1_DECODE_SIZE_MISMATCH, "overlong-1024");
    check(san9_p1_decode(NULL, SAN9_P1_FRAME_SIZE, key, sizeof(key), &decoded)
        == SAN9_P1_DECODE_NULL_FRAME, "null-frame");
    check(san9_p1_decode(golden, SAN9_P1_FRAME_SIZE, zero_key, sizeof(zero_key), &decoded)
        == SAN9_P1_DECODE_KEY_INVALID, "zero-key");
    check(san9_p1_decode(golden, SAN9_P1_FRAME_SIZE, key, sizeof(key) - 1U, &decoded)
        == SAN9_P1_DECODE_KEY_INVALID, "short-key");
    memcpy(wrong_key, key, sizeof(wrong_key));
    wrong_key[0] ^= 0x80U;
    decoded = create_request();
    check(san9_p1_decode(golden, SAN9_P1_FRAME_SIZE, wrong_key, sizeof(wrong_key), &decoded)
        == SAN9_P1_DECODE_HMAC_MISMATCH, "wrong-key");
    check(decoded.sequence == 0ULL && decoded.game_pid == 0U, "decode-failure-clears-output");
}

static void check_authenticated_status(
    uint8_t frame[SAN9_P1_FRAME_SIZE],
    const uint8_t key[SAN9_P1_HMAC_KEY_SIZE],
    San9P1DecodeStatus expected,
    const char *name)
{
    San9P1Frame decoded;
    reauthenticate(frame, key);
    check(san9_p1_decode(frame, SAN9_P1_FRAME_SIZE, key, SAN9_P1_HMAC_KEY_SIZE, &decoded)
        == expected, name);
}

static void test_authenticated_structure_rejections(
    const uint8_t golden[SAN9_P1_FRAME_SIZE])
{
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t changed[SAN9_P1_FRAME_SIZE];
    uint8_t response[SAN9_P1_FRAME_SIZE];
    San9P1Frame request = create_request();
    San9P1Frame response_frame = create_response(&request);
    create_key(key);

    memcpy(changed, golden, sizeof(changed));
    changed[SAN9_P1_RESERVED_HEADER_OFFSET] = 1U;
    check_authenticated_status(changed, key, SAN9_P1_DECODE_RESERVED_OR_FLAGS_INVALID, "auth-reserved-header");
    memcpy(changed, golden, sizeof(changed));
    changed[SAN9_P1_RESERVED_BINDING_OFFSET] = 1U;
    check_authenticated_status(changed, key, SAN9_P1_DECODE_RESERVED_OR_FLAGS_INVALID, "auth-reserved-binding");
    memcpy(changed, golden, sizeof(changed));
    changed[SAN9_P1_RESERVED_TAIL_OFFSET] = 1U;
    check_authenticated_status(changed, key, SAN9_P1_DECODE_RESERVED_OR_FLAGS_INVALID, "auth-reserved-tail");
    memcpy(changed, golden, sizeof(changed));
    write_u32(changed, SAN9_P1_FLAGS_OFFSET, 1U);
    check_authenticated_status(changed, key, SAN9_P1_DECODE_RESERVED_OR_FLAGS_INVALID, "auth-flags");
    memcpy(changed, golden, sizeof(changed));
    write_u16(changed, SAN9_P1_KIND_OFFSET, 3U);
    check_authenticated_status(changed, key, SAN9_P1_DECODE_KIND_STATE_INVALID, "auth-kind");
    memcpy(changed, golden, sizeof(changed));
    write_u64(changed, SAN9_P1_SEQUENCE_OFFSET, 0ULL);
    check_authenticated_status(changed, key, SAN9_P1_DECODE_REQUIRED_FIELD_INVALID, "auth-zero-sequence");
    memcpy(changed, golden, sizeof(changed));
    memset(changed + SAN9_P1_MANIFEST_DIGEST_OFFSET, 0, SAN9_P1_DIGEST_SIZE);
    check_authenticated_status(changed, key, SAN9_P1_DECODE_REQUIRED_FIELD_INVALID, "auth-zero-manifest");
    memcpy(changed, golden, sizeof(changed));
    write_u64(changed, SAN9_P1_EXPIRES_AT_MS_OFFSET, 1005001ULL);
    check_authenticated_status(changed, key, SAN9_P1_DECODE_LIFETIME_INVALID, "auth-lifetime");
    memcpy(changed, golden, sizeof(changed));
    write_u32(changed, SAN9_P1_RESULT_CODE_OFFSET, 1U);
    check_authenticated_status(changed, key, SAN9_P1_DECODE_REQUEST_SHAPE_INVALID, "auth-request-result-code");
    memcpy(changed, golden, sizeof(changed));
    changed[SAN9_P1_RESULT_DIGEST_OFFSET] = 1U;
    check_authenticated_status(changed, key, SAN9_P1_DECODE_REQUEST_SHAPE_INVALID, "auth-request-result-digest");

    check(san9_p1_encode(&response_frame, key, sizeof(key), response, sizeof(response)), "auth-response-encode");
    memcpy(changed, response, sizeof(changed));
    write_u32(changed, SAN9_P1_RESULT_CODE_OFFSET, 1U);
    check_authenticated_status(changed, key, SAN9_P1_DECODE_RESPONSE_SHAPE_INVALID, "auth-completed-result-code");
    memcpy(changed, response, sizeof(changed));
    memset(changed + SAN9_P1_RESULT_DIGEST_OFFSET, 0, SAN9_P1_DIGEST_SIZE);
    check_authenticated_status(changed, key, SAN9_P1_DECODE_RESPONSE_SHAPE_INVALID, "auth-zero-response-result-digest");
}

static void check_encode_rejected(const San9P1Frame *frame, const char *name)
{
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t encoded[SAN9_P1_FRAME_SIZE];
    create_key(key);
    memset(encoded, 0xa5, sizeof(encoded));
    check(!san9_p1_encode(frame, key, sizeof(key), encoded, sizeof(encoded)), name);
    check(san9_p1_constant_time_equal(encoded, (const uint8_t[SAN9_P1_FRAME_SIZE]){0}, sizeof(encoded)),
        "rejected-encode-clears-output");
}

static void test_strict_shapes(void)
{
    San9P1Frame original = create_request();
    San9P1Frame changed = original;
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t encoded[SAN9_P1_FRAME_SIZE];
    changed.kind = 3U;
    check_encode_rejected(&changed, "reject-unknown-kind");
    changed = original;
    changed.state = SAN9_P1_COMPLETED;
    check_encode_rejected(&changed, "reject-request-state");
    changed = original;
    changed.flags = 1U;
    check_encode_rejected(&changed, "reject-flags");
    changed = original;
    changed.sequence = 0ULL;
    check_encode_rejected(&changed, "reject-zero-sequence");
    changed = original;
    changed.result_code = 1U;
    check_encode_rejected(&changed, "reject-request-result-code");
    changed = original;
    changed.result_digest[0] = 1U;
    check_encode_rejected(&changed, "reject-request-result-digest");
    changed = original;
    changed.expires_at_ms += 2000ULL;
    check_encode_rejected(&changed, "reject-lifetime-too-long");
    changed = original;
    memset(changed.session_nonce, 0, sizeof(changed.session_nonce));
    check_encode_rejected(&changed, "reject-zero-session");
    changed = create_response(&original);
    create_key(key);
    check(san9_p1_encode(&changed, key, sizeof(key), encoded, sizeof(encoded)), "valid-response");
    memset(changed.result_digest, 0, sizeof(changed.result_digest));
    check_encode_rejected(&changed, "reject-zero-response-result-digest");
}

static San9P1GateStatus accept_authenticated(
    San9P1RequestGate *gate,
    const San9P1Frame *request,
    uint64_t now_ms)
{
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t encoded[SAN9_P1_FRAME_SIZE];
    San9P1Frame accepted;
    San9P1DecodeStatus decode_status = SAN9_P1_DECODE_NULL_FRAME;
    create_key(key);
    if (!san9_p1_encode(request, key, sizeof(key), encoded, sizeof(encoded))) {
        return SAN9_P1_GATE_AUTHENTICATED_FRAME_REJECTED;
    }
    return san9_p1_decode_and_accept(
        gate,
        encoded,
        sizeof(encoded),
        key,
        sizeof(key),
        now_ms,
        &decode_status,
        &accepted);
}

static int verify_authenticated_response(
    const San9P1Frame *request,
    const San9P1Frame *response)
{
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t encoded[SAN9_P1_FRAME_SIZE];
    San9P1Frame verified;
    San9P1DecodeStatus decode_status = SAN9_P1_DECODE_NULL_FRAME;
    create_key(key);
    if (!san9_p1_encode(response, key, sizeof(key), encoded, sizeof(encoded))) {
        return 0;
    }
    return san9_p1_decode_and_verify_response(
        request,
        encoded,
        sizeof(encoded),
        key,
        sizeof(key),
        &decode_status,
        &verified);
}

static void check_binding_rejected(
    const San9P1Frame *binding,
    const San9P1Frame *changed,
    const char *name)
{
    San9P1RequestGate gate;
    check(san9_p1_request_gate_initialize(&gate, binding), "binding-gate-initialize");
    check(accept_authenticated(&gate, changed, changed->issued_at_ms)
        == SAN9_P1_GATE_BINDING_MISMATCH, name);
}

static void test_gate_binding_mutations(const San9P1Frame *binding)
{
    San9P1Frame changed = *binding;
    advance_request(&changed, 1ULL);
    ++changed.game_pid;
    check_binding_rejected(binding, &changed, "binding-game-pid");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.main_tid;
    check_binding_rejected(binding, &changed, "binding-main-tid");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.game_hwnd;
    check_binding_rejected(binding, &changed, "binding-hwnd");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.helper_pid;
    check_binding_rejected(binding, &changed, "binding-helper-pid");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.easy_loader_pid;
    check_binding_rejected(binding, &changed, "binding-loader-pid");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.game_generation;
    check_binding_rejected(binding, &changed, "binding-game-generation");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.helper_generation;
    check_binding_rejected(binding, &changed, "binding-helper-generation");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.easy_loader_generation;
    check_binding_rejected(binding, &changed, "binding-loader-generation");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.session_nonce[0];
    check_binding_rejected(binding, &changed, "binding-session-nonce");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.easy_epoch_nonce[0];
    check_binding_rejected(binding, &changed, "binding-easy-epoch-nonce");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.build_digest[0];
    check_binding_rejected(binding, &changed, "binding-build-digest");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.profile_digest[0];
    check_binding_rejected(binding, &changed, "binding-profile-digest");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.manifest_digest[0];
    check_binding_rejected(binding, &changed, "binding-manifest-digest");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.easy_epoch_digest[0];
    check_binding_rejected(binding, &changed, "binding-epoch-digest");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.easy_ticket_digest[0];
    check_binding_rejected(binding, &changed, "binding-ticket-digest");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.context_digest[0];
    check_binding_rejected(binding, &changed, "binding-context-digest");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.bridge_digest[0];
    check_binding_rejected(binding, &changed, "binding-bridge-digest");
    changed = *binding; advance_request(&changed, 1ULL); ++changed.mapping_digest[0];
    check_binding_rejected(binding, &changed, "binding-mapping-digest");
}

static void test_request_gate(void)
{
    San9P1Frame first = create_request();
    San9P1Frame changed;
    San9P1RequestGate gate;
    uint64_t sequence;
    check(san9_p1_request_gate_initialize(&gate, &first), "gate-initialize");
    check(accept_authenticated(&gate, &first, first.issued_at_ms)
        == SAN9_P1_GATE_ACCEPTED, "gate-first");
    check(accept_authenticated(&gate, &first, first.issued_at_ms)
        == SAN9_P1_GATE_REPLAY, "gate-replay");
    changed = first;
    advance_request(&changed, 3ULL);
    check(accept_authenticated(&gate, &changed, first.issued_at_ms + 1ULL)
        == SAN9_P1_GATE_SEQUENCE_GAP, "gate-gap");
    changed = first;
    advance_request(&changed, 2ULL);
    check(accept_authenticated(&gate, &changed, changed.issued_at_ms - 1ULL)
        == SAN9_P1_GATE_FROM_FUTURE, "gate-future");
    check(accept_authenticated(&gate, &changed, changed.issued_at_ms)
        == SAN9_P1_GATE_ACCEPTED, "gate-second");
    changed = first;
    advance_request(&changed, 3ULL);
    memcpy(changed.request_id, first.request_id, sizeof(changed.request_id));
    check(accept_authenticated(&gate, &changed, changed.issued_at_ms)
        == SAN9_P1_GATE_REQUEST_ID_REPLAY, "gate-request-id-replay");

    check(san9_p1_request_gate_initialize(&gate, &first), "expiry-gate-initialize");
    check(accept_authenticated(&gate, &first, first.expires_at_ms)
        == SAN9_P1_GATE_EXPIRED, "gate-expired");

    check(san9_p1_request_gate_initialize(&gate, &first), "clock-gate-initialize");
    check(accept_authenticated(&gate, &first, first.issued_at_ms + 100ULL)
        == SAN9_P1_GATE_ACCEPTED, "gate-clock-first");
    changed = first;
    advance_request(&changed, 2ULL);
    check(accept_authenticated(&gate, &changed, first.issued_at_ms + 200ULL)
        == SAN9_P1_GATE_ACCEPTED, "gate-clock-second");
    changed = first;
    advance_request(&changed, 3ULL);
    check(accept_authenticated(&gate, &changed, first.issued_at_ms + 199ULL)
        == SAN9_P1_GATE_CLOCK_ROLLBACK, "gate-clock-rollback");
    check(gate.clock_faulted != 0U, "gate-clock-fault-latched");
    check(accept_authenticated(&gate, &changed, first.issued_at_ms + 300ULL)
        == SAN9_P1_GATE_CLOCK_FAULTED, "gate-clock-permanent-fault");

    test_gate_binding_mutations(&first);
    check(san9_p1_request_gate_initialize(&gate, &first), "budget-gate-initialize");
    for (sequence = 1ULL; sequence <= SAN9_P1_MAXIMUM_SESSION_PINGS; ++sequence) {
        changed = first;
        advance_request(&changed, sequence);
        check_indexed(
            accept_authenticated(&gate, &changed, changed.issued_at_ms) == SAN9_P1_GATE_ACCEPTED,
            "budget-accept-",
            (size_t)sequence);
    }
    changed = first;
    advance_request(&changed, SAN9_P1_MAXIMUM_SESSION_PINGS + 1ULL);
    check(accept_authenticated(&gate, &changed, changed.issued_at_ms)
        == SAN9_P1_GATE_BUDGET_EXHAUSTED, "budget-exhausted");
}

static void test_response_verify(void)
{
    San9P1Frame request = create_request();
    San9P1Frame response = create_response(&request);
    check(verify_authenticated_response(&request, &response), "response-match");
    response.request_id[0] ^= 1U;
    check(!verify_authenticated_response(&request, &response), "response-request-id-mismatch");
    response = create_response(&request);
    response.challenge_digest[0] ^= 1U;
    check(!verify_authenticated_response(&request, &response), "response-challenge-mismatch");
    response = create_response(&request);
    ++response.sequence;
    check(!verify_authenticated_response(&request, &response), "response-sequence-mismatch");
    response = create_response(&request);
    memset(response.result_digest, 0, sizeof(response.result_digest));
    check(!verify_authenticated_response(&request, &response), "response-zero-result");
}

static void test_encode_capacity_and_overlap(void)
{
    San9P1Frame request = create_request();
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t short_output[SAN9_P1_FRAME_SIZE];
    uint8_t long_output[SAN9_P1_FRAME_SIZE + 1U];
    union {
        max_align_t alignment;
        uint8_t bytes[sizeof(San9P1Frame) + SAN9_P1_FRAME_SIZE];
    } storage;
    uint8_t storage_before[sizeof(storage.bytes)];
    uint8_t key_overlap_storage[SAN9_P1_FRAME_SIZE + SAN9_P1_HMAC_KEY_SIZE];
    uint8_t key_overlap_before[sizeof(key_overlap_storage)];
    San9P1Frame *overlap_frame = (San9P1Frame *)storage.bytes;
    uint8_t *overlap_output = storage.bytes + 1U;
    uint8_t *partial_overlap_key = key_overlap_storage + SAN9_P1_FRAME_SIZE - 12U;
    size_t index;
    int all_cleared;
    create_key(key);

    memset(short_output, 0xa5, sizeof(short_output));
    check(!san9_p1_encode(&request, key, sizeof(key), short_output,
        SAN9_P1_FRAME_SIZE - 1U), "encode-reject-short-output-capacity");
    all_cleared = 1;
    for (index = 0U; index < SAN9_P1_FRAME_SIZE - 1U; ++index) {
        all_cleared = all_cleared && short_output[index] == 0U;
    }
    check(all_cleared && short_output[SAN9_P1_FRAME_SIZE - 1U] == 0xa5U,
        "encode-short-output-clears-known-capacity-only");

    memset(long_output, 0xa5, sizeof(long_output));
    check(!san9_p1_encode(&request, key, sizeof(key), long_output,
        sizeof(long_output)), "encode-reject-long-output-capacity");
    all_cleared = 1;
    for (index = 0U; index < SAN9_P1_FRAME_SIZE; ++index) {
        all_cleared = all_cleared && long_output[index] == 0U;
    }
    check(all_cleared && long_output[SAN9_P1_FRAME_SIZE] == 0xa5U,
        "encode-long-output-clears-frame-prefix-only");

    memset(storage.bytes, 0, sizeof(storage.bytes));
    *overlap_frame = request;
    memcpy(storage_before, storage.bytes, sizeof(storage_before));
    check(!san9_p1_encode(overlap_frame, key, sizeof(key), overlap_output,
        SAN9_P1_FRAME_SIZE), "encode-reject-overlapping-frame-output");
    check(memcmp(storage.bytes, storage_before, sizeof(storage_before)) == 0,
        "encode-frame-alias-rejected-before-write");

    memset(key_overlap_storage, 0xa5, sizeof(key_overlap_storage));
    create_key(partial_overlap_key);
    memcpy(key_overlap_before, key_overlap_storage, sizeof(key_overlap_before));
    check(!san9_p1_encode(&request, partial_overlap_key, SAN9_P1_HMAC_KEY_SIZE,
        key_overlap_storage, SAN9_P1_FRAME_SIZE),
        "encode-reject-partial-key-output-overlap");
    check(memcmp(key_overlap_storage, key_overlap_before, sizeof(key_overlap_before)) == 0,
        "encode-partial-key-alias-rejected-before-write");

    memset(key_overlap_storage, 0xa5, sizeof(key_overlap_storage));
    create_key(key_overlap_storage + 64U);
    memcpy(key_overlap_before, key_overlap_storage, sizeof(key_overlap_before));
    check(!san9_p1_encode(&request, key_overlap_storage + 64U,
        SAN9_P1_HMAC_KEY_SIZE, key_overlap_storage, SAN9_P1_FRAME_SIZE),
        "encode-reject-contained-key-output-overlap");
    check(memcmp(key_overlap_storage, key_overlap_before, sizeof(key_overlap_before)) == 0,
        "encode-contained-key-alias-rejected-before-write");
}

static void test_authenticated_entrypoints(void)
{
    San9P1Frame request = create_request();
    San9P1Frame response = create_response(&request);
    San9P1Frame accepted;
    San9P1Frame verified;
    San9P1RequestGate gate;
    San9P1DecodeStatus decode_status = SAN9_P1_DECODE_NULL_FRAME;
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t wrong_key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t request_bytes[SAN9_P1_FRAME_SIZE];
    uint8_t response_bytes[SAN9_P1_FRAME_SIZE];
    create_key(key);
    memcpy(wrong_key, key, sizeof(wrong_key));
    wrong_key[0] ^= 0x80U;
    check(san9_p1_encode(&request, key, sizeof(key), request_bytes,
        sizeof(request_bytes)), "authenticated-request-encode");
    check(san9_p1_request_gate_initialize(&gate, &request),
        "authenticated-gate-initialize");
    check(san9_p1_decode_and_accept(
            &gate, request_bytes, sizeof(request_bytes), key, sizeof(key),
            request.issued_at_ms, &decode_status, &accepted)
        == SAN9_P1_GATE_ACCEPTED,
        "authenticated-decode-and-accept");
    check(decode_status == SAN9_P1_DECODE_ACCEPTED
            && accepted.sequence == request.sequence,
        "authenticated-accepted-output");

    accepted = create_response(&request);
    check(san9_p1_decode_and_accept(
            &gate, request_bytes, sizeof(request_bytes), wrong_key, sizeof(wrong_key),
            request.issued_at_ms, &decode_status, &accepted)
        == SAN9_P1_GATE_AUTHENTICATED_FRAME_REJECTED,
        "authenticated-wrong-key-rejected-before-gate");
    check(decode_status == SAN9_P1_DECODE_HMAC_MISMATCH
            && accepted.sequence == 0ULL && accepted.game_pid == 0U,
        "authenticated-rejection-clears-output");

    check(san9_p1_encode(&response, key, sizeof(key), response_bytes,
        sizeof(response_bytes)), "authenticated-response-encode");
    check(san9_p1_decode_and_verify_response(
            &request, response_bytes, sizeof(response_bytes), key, sizeof(key),
            &decode_status, &verified),
        "authenticated-decode-and-verify-response");
    check(decode_status == SAN9_P1_DECODE_ACCEPTED
            && verified.sequence == request.sequence,
        "authenticated-response-output");
    verified = request;
    check(!san9_p1_decode_and_verify_response(
            &request, response_bytes, sizeof(response_bytes), wrong_key, sizeof(wrong_key),
            &decode_status, &verified),
        "authenticated-response-wrong-key-rejected");
    check(decode_status == SAN9_P1_DECODE_HMAC_MISMATCH
            && verified.sequence == 0ULL && verified.game_pid == 0U,
        "authenticated-response-rejection-clears-output");
}

static int run_all(uint8_t golden[SAN9_P1_FRAME_SIZE])
{
    checks = 0U;
    failures = 0U;
    test_crypto_vectors();
    test_layout_and_golden(golden);
    test_mutations(golden);
    test_authenticated_structure_rejections(golden);
    test_lengths_and_keys(golden);
    test_strict_shapes();
    test_request_gate();
    test_response_verify();
    test_encode_capacity_and_overlap();
    test_authenticated_entrypoints();
    if (failures != 0U) {
        fprintf(stderr, "P1WIRE_C_SELFTEST FAIL failures=%u checks=%u\n", failures, checks);
        return 0;
    }
    return 1;
}

static int write_golden(const char *path, const uint8_t golden[SAN9_P1_FRAME_SIZE])
{
    FILE *file = fopen(path, "wb");
    size_t written;
    if (file == NULL) {
        return 0;
    }
    written = fwrite(golden, 1U, SAN9_P1_FRAME_SIZE, file);
    if (fclose(file) != 0) {
        return 0;
    }
    return written == SAN9_P1_FRAME_SIZE;
}

static int read_exact_golden(const char *path, uint8_t output[SAN9_P1_FRAME_SIZE])
{
    FILE *file = fopen(path, "rb");
    size_t read;
    int next;
    if (file == NULL) {
        return 0;
    }
    read = fread(output, 1U, SAN9_P1_FRAME_SIZE, file);
    next = fgetc(file);
    if (fclose(file) != 0) {
        return 0;
    }
    return read == SAN9_P1_FRAME_SIZE && next == EOF;
}

static int create_response_golden(uint8_t output[SAN9_P1_FRAME_SIZE])
{
    San9P1Frame request = create_request();
    San9P1Frame response = create_response(&request);
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    create_key(key);
    return san9_p1_encode(&response, key, sizeof(key), output, SAN9_P1_FRAME_SIZE);
}

int main(int argc, char **argv)
{
    uint8_t golden[SAN9_P1_FRAME_SIZE];
    char hash[65];
    if (argc == 1 || (argc == 2 && strcmp(argv[1], "--self-test") == 0)) {
        if (!run_all(golden)) {
            return 1;
        }
        printf("P1WIRE_C_SELFTEST PASS checks=%u\n", checks);
        return 0;
    }
    if (argc == 3 && strcmp(argv[1], "--write-golden") == 0) {
        if (!run_all(golden) || !write_golden(argv[2], golden)) {
            fprintf(stderr, "P1WIRE_C_GOLDEN_WRITE FAIL\n");
            return 1;
        }
        sha256_hex(golden, sizeof(golden), hash);
        printf("P1WIRE_C_GOLDEN_WRITTEN %s sha256=%s\n", argv[2], hash);
        return 0;
    }
    if (argc == 3 && strcmp(argv[1], "--verify-golden") == 0) {
        uint8_t candidate[SAN9_P1_FRAME_SIZE];
        uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
        San9P1Frame decoded;
        if (!run_all(golden) || !read_exact_golden(argv[2], candidate)) {
            fprintf(stderr, "P1WIRE_C_GOLDEN_READ FAIL\n");
            return 1;
        }
        create_key(key);
        if (san9_p1_decode(candidate, sizeof(candidate), key, sizeof(key), &decoded)
                != SAN9_P1_DECODE_ACCEPTED
            || !san9_p1_constant_time_equal(candidate, golden, sizeof(golden))) {
            fprintf(stderr, "P1WIRE_C_GOLDEN_VERIFY FAIL\n");
            return 1;
        }
        sha256_hex(candidate, sizeof(candidate), hash);
        printf("P1WIRE_C_GOLDEN_VERIFIED %s sha256=%s\n", argv[2], hash);
        return 0;
    }
    if (argc == 3 && strcmp(argv[1], "--write-response-golden") == 0) {
        uint8_t response_golden[SAN9_P1_FRAME_SIZE];
        if (!run_all(golden)
            || !create_response_golden(response_golden)
            || !write_golden(argv[2], response_golden)) {
            fprintf(stderr, "P1WIRE_C_RESPONSE_GOLDEN_WRITE FAIL\n");
            return 1;
        }
        sha256_hex(response_golden, sizeof(response_golden), hash);
        printf("P1WIRE_C_RESPONSE_GOLDEN_WRITTEN %s sha256=%s\n", argv[2], hash);
        return 0;
    }
    if (argc == 3 && strcmp(argv[1], "--verify-response-golden") == 0) {
        uint8_t candidate[SAN9_P1_FRAME_SIZE];
        uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
        San9P1Frame request = create_request();
        San9P1Frame verified;
        San9P1DecodeStatus decode_status = SAN9_P1_DECODE_NULL_FRAME;
        if (!run_all(golden)
            || !read_exact_golden(argv[2], candidate)) {
            fprintf(stderr, "P1WIRE_C_RESPONSE_GOLDEN_READ FAIL\n");
            return 1;
        }
        create_key(key);
        if (!san9_p1_decode_and_verify_response(
                &request,
                candidate,
                sizeof(candidate),
                key,
                sizeof(key),
                &decode_status,
                &verified)
            || decode_status != SAN9_P1_DECODE_ACCEPTED) {
            fprintf(stderr, "P1WIRE_C_RESPONSE_GOLDEN_VERIFY FAIL\n");
            return 1;
        }
        sha256_hex(candidate, sizeof(candidate), hash);
        printf("P1WIRE_C_RESPONSE_GOLDEN_VERIFIED %s sha256=%s\n", argv[2], hash);
        return 0;
    }
    fprintf(stderr,
        "usage: selftest [--self-test | --write-golden PATH | --verify-golden PATH"
        " | --write-response-golden PATH | --verify-response-golden PATH]\n");
    return 2;
}
