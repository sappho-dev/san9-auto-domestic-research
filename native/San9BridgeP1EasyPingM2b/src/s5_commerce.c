#include "s5_commerce.h"

#include "san9_p1_wire.h"
#include "sha256.h"

#include <string.h>

#if defined(__BYTE_ORDER__) && (__BYTE_ORDER__ != __ORDER_LITTLE_ENDIAN__)
#error "S5 request images require a little-endian target"
#endif

static const uint8_t g_hmac_domain[] = "SAN9-S5-NO-APPLY-HMAC-v1";
static const uint8_t g_apply_hmac_domain[] = "SAN9-S5-APPLY-ONCE-HMAC-v1";
static const uint8_t g_cultivate_hmac_domain[] =
    "SAN9-S6-CULTIVATE-APPLY-ONCE-HMAC-v1";
static const uint8_t g_patrol_hmac_domain[] =
    "SAN9-S6-PATROL-APPLY-ONCE-HMAC-v1";
static const uint8_t g_train_hmac_domain[] =
    "SAN9-S6-TRAIN-APPLY-ONCE-HMAC-v1";
static const uint8_t g_repair_hmac_domain[] =
    "SAN9-S6-REPAIR-APPLY-ONCE-HMAC-v1";
#if defined(S6_REPAIR_APPLY_ONCE_BUILD) && S6_REPAIR_APPLY_ONCE_BUILD
const char san9_s5_no_apply_build_identity[] =
    "S6_REPAIR_APPLY_ONCE_REQUEST_CORE=1;DISTINCT_KIND=1;"
    "DISTINCT_HMAC_DOMAIN=1";
#elif defined(S6_TRAIN_APPLY_ONCE_BUILD) && S6_TRAIN_APPLY_ONCE_BUILD
const char san9_s5_no_apply_build_identity[] =
    "S6_TRAIN_APPLY_ONCE_REQUEST_CORE=1;DISTINCT_KIND=1;"
    "DISTINCT_HMAC_DOMAIN=1";
#elif defined(S6_PATROL_APPLY_ONCE_BUILD) && S6_PATROL_APPLY_ONCE_BUILD
const char san9_s5_no_apply_build_identity[] =
    "S6_PATROL_APPLY_ONCE_REQUEST_CORE=1;DISTINCT_KIND=1;"
    "DISTINCT_HMAC_DOMAIN=1";
#elif defined(S6_CULTIVATE_APPLY_ONCE_BUILD) && S6_CULTIVATE_APPLY_ONCE_BUILD
const char san9_s5_no_apply_build_identity[] =
    "S6_CULTIVATE_APPLY_ONCE_REQUEST_CORE=1;DISTINCT_KIND=1;"
    "DISTINCT_HMAC_DOMAIN=1";
#elif defined(S5_APPLY_ONCE_BUILD) && S5_APPLY_ONCE_BUILD
const char san9_s5_no_apply_build_identity[] =
    "S5_APPLY_ONCE_REQUEST_CORE=1;DISTINCT_KIND=1;DISTINCT_HMAC_DOMAIN=1";
#else
const char san9_s5_no_apply_build_identity[] =
    "S5_NO_APPLY_BUILD=1;EVENT_CALLS=1;HANDLER_SHADOW=1;"
    "NATIVE_APPLY_CALLS=0;COMMAND_CTORS=0;TARGET_BUSINESS_WRITES=0";
#endif
static const uint8_t g_digest_domain[] = "SAN9-S5-NO-APPLY-DIGEST-v1";
static const uint8_t g_apply_digest_domain[] = "SAN9-S5-APPLY-ONCE-DIGEST-v1";
static const uint8_t g_cultivate_digest_domain[] =
    "SAN9-S6-CULTIVATE-APPLY-ONCE-DIGEST-v1";
static const uint8_t g_patrol_digest_domain[] =
    "SAN9-S6-PATROL-APPLY-ONCE-DIGEST-v1";
static const uint8_t g_train_digest_domain[] =
    "SAN9-S6-TRAIN-APPLY-ONCE-DIGEST-v1";
static const uint8_t g_repair_digest_domain[] =
    "SAN9-S6-REPAIR-APPLY-ONCE-DIGEST-v1";

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

static int key_valid(const uint8_t *key, size_t key_size)
{
    return key != NULL && key_size == SAN9_S5_HMAC_KEY_SIZE
        && !bytes_zero(key, key_size);
}

static int ranges_overlap(
    const void *left,
    size_t left_size,
    const void *right,
    size_t right_size)
{
    uintptr_t left_start;
    uintptr_t right_start;
    if (left == NULL || right == NULL || left_size == 0u || right_size == 0u) {
        return 0;
    }
    left_start = (uintptr_t)left;
    right_start = (uintptr_t)right;
    if (left_start > UINTPTR_MAX - left_size
        || right_start > UINTPTR_MAX - right_size) {
        return 1;
    }
    return left_start < right_start + right_size
        && right_start < left_start + left_size;
}

static int top5_valid(const uint32_t person_ids[SAN9_S5_TOP5_COUNT])
{
    size_t left;
    size_t right;
    if (person_ids == NULL) {
        return 0;
    }
    for (left = 0u; left < SAN9_S5_TOP5_COUNT; ++left) {
        if (person_ids[left] >= 850u) {
            return 0;
        }
        for (right = left + 1u; right < SAN9_S5_TOP5_COUNT; ++right) {
            if (person_ids[left] == person_ids[right]) {
                return 0;
            }
        }
    }
    return 1;
}

static San9S5RequestStatus request_shape_status_kind(
    const San9S5NoApplyRequest *request, uint32_t expected_kind)
{
    uint32_t expected_magic;
    uint32_t expected_flags;
    uint32_t expected_native_id;
    uint32_t expected_event;
    if (request == NULL) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    if (request->reserved_header != 0u
        || request->reserved_alignment != 0u
        || !bytes_zero(request->reserved, sizeof(request->reserved))) {
        return SAN9_S5_REQUEST_RESERVED_NONZERO;
    }
    if (expected_kind == SAN9_S6_REQUEST_KIND_REPAIR_APPLY_ONCE) {
        expected_magic = SAN9_S6_REPAIR_APPLY_ONCE_MAGIC;
        expected_flags = SAN9_S5_FLAG_CURRENT_CITY_ONLY
            | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_REPAIR;
        expected_native_id = SAN9_S6_REPAIR_NATIVE_ID;
        expected_event = SAN9_S6_REPAIR_EVENT_CODE;
    } else if (expected_kind == SAN9_S6_REQUEST_KIND_TRAIN_APPLY_ONCE) {
        expected_magic = SAN9_S6_TRAIN_APPLY_ONCE_MAGIC;
        expected_flags = SAN9_S5_FLAG_CURRENT_CITY_ONLY
            | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_TRAIN;
        expected_native_id = SAN9_S6_TRAIN_NATIVE_ID;
        expected_event = SAN9_S6_TRAIN_EVENT_CODE;
    } else if (expected_kind == SAN9_S6_REQUEST_KIND_PATROL_APPLY_ONCE) {
        expected_magic = SAN9_S6_PATROL_APPLY_ONCE_MAGIC;
        expected_flags = SAN9_S5_FLAG_CURRENT_CITY_ONLY
            | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_PATROL;
        expected_native_id = SAN9_S6_PATROL_NATIVE_ID;
        expected_event = SAN9_S6_PATROL_EVENT_CODE;
    } else if (expected_kind == SAN9_S6_REQUEST_KIND_CULTIVATE_APPLY_ONCE) {
        expected_magic = SAN9_S6_CULTIVATE_APPLY_ONCE_MAGIC;
        expected_flags = SAN9_S5_FLAG_CURRENT_CITY_ONLY
            | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_CULTIVATE;
        expected_native_id = SAN9_S6_CULTIVATE_NATIVE_ID;
        expected_event = SAN9_S6_CULTIVATE_EVENT_CODE;
    } else if (expected_kind == SAN9_S5_REQUEST_KIND_APPLY_ONCE) {
        expected_magic = SAN9_S5_APPLY_ONCE_MAGIC;
        expected_flags = SAN9_S5_FLAG_CURRENT_CITY_ONLY
            | SAN9_S5_FLAG_APPLY_ONCE;
        expected_native_id = SAN9_S5_COMMERCE_NATIVE_ID;
        expected_event = SAN9_S5_COMMERCE_EVENT_CODE;
    } else {
        expected_magic = SAN9_S5_NO_APPLY_MAGIC;
        expected_flags = SAN9_S5_FLAG_CURRENT_CITY_ONLY | SAN9_S5_FLAG_NO_APPLY;
        expected_native_id = SAN9_S5_COMMERCE_NATIVE_ID;
        expected_event = SAN9_S5_COMMERCE_EVENT_CODE;
    }
    if (request->magic != expected_magic
        || request->schema_major != SAN9_S5_SCHEMA_MAJOR
        || request->schema_minor != SAN9_S5_SCHEMA_MINOR
        || request->declared_size != SAN9_S5_REQUEST_SIZE
        || request->kind != expected_kind
        || request->flags != expected_flags
        || request->native_command_id != expected_native_id
        || request->event_code != expected_event
        || request->sequence != SAN9_S5_SEQUENCE_ONE
        || request->expected_city_id >= 50u
        || request->expected_corps_id >= 50u
        || request->exact_person_count != SAN9_S5_TOP5_COUNT
        || !top5_valid(request->expected_person_ids)
        || bytes_zero(request->request_nonce, sizeof(request->request_nonce))
        || bytes_zero(request->p1_binding_digest,
            sizeof(request->p1_binding_digest))
        || bytes_zero(request->precondition_digest,
            sizeof(request->precondition_digest))) {
        return SAN9_S5_REQUEST_SHAPE_INVALID;
    }
    if (request->expires_at_ms <= request->issued_at_ms
        || request->expires_at_ms - request->issued_at_ms
            > SAN9_S5_MAXIMUM_LIFETIME_MS) {
        return SAN9_S5_REQUEST_LIFETIME_INVALID;
    }
    return SAN9_S5_REQUEST_VALID;
}

static San9S5RequestStatus request_shape_status(
    const San9S5NoApplyRequest *request)
{
    return request_shape_status_kind(request, SAN9_S5_REQUEST_KIND_NO_APPLY);
}

static San9S5RequestStatus apply_request_shape_status(
    const San9S5NoApplyRequest *request)
{
    return request_shape_status_kind(request, SAN9_S5_REQUEST_KIND_APPLY_ONCE);
}

static San9S5RequestStatus cultivate_request_shape_status(
    const San9S5NoApplyRequest *request)
{
    return request_shape_status_kind(request,
        SAN9_S6_REQUEST_KIND_CULTIVATE_APPLY_ONCE);
}

static San9S5RequestStatus patrol_request_shape_status(
    const San9S5NoApplyRequest *request)
{
    return request_shape_status_kind(request,
        SAN9_S6_REQUEST_KIND_PATROL_APPLY_ONCE);
}

static San9S5RequestStatus train_request_shape_status(
    const San9S5NoApplyRequest *request)
{
    return request_shape_status_kind(request,
        SAN9_S6_REQUEST_KIND_TRAIN_APPLY_ONCE);
}

static San9S5RequestStatus repair_request_shape_status(
    const San9S5NoApplyRequest *request)
{
    return request_shape_status_kind(request,
        SAN9_S6_REQUEST_KIND_REPAIR_APPLY_ONCE);
}

static San9S5RequestStatus supported_request_shape_status(
    const San9S5NoApplyRequest *request)
{
    if (request != NULL
        && request->kind == SAN9_S5_REQUEST_KIND_APPLY_ONCE) {
        return apply_request_shape_status(request);
    }
    if (request != NULL
        && request->kind == SAN9_S6_REQUEST_KIND_CULTIVATE_APPLY_ONCE) {
        return cultivate_request_shape_status(request);
    }
    if (request != NULL
        && request->kind == SAN9_S6_REQUEST_KIND_PATROL_APPLY_ONCE) {
        return patrol_request_shape_status(request);
    }
    if (request != NULL
        && request->kind == SAN9_S6_REQUEST_KIND_TRAIN_APPLY_ONCE) {
        return train_request_shape_status(request);
    }
    if (request != NULL
        && request->kind == SAN9_S6_REQUEST_KIND_REPAIR_APPLY_ONCE) {
        return repair_request_shape_status(request);
    }
    return request_shape_status(request);
}

static void request_hmac(
    const San9S5NoApplyRequest *request,
    const uint8_t key[SAN9_S5_HMAC_KEY_SIZE],
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    uint8_t material[(sizeof(g_hmac_domain) - 1u) + SAN9_S5_REQUEST_SIZE];
    const size_t request_offset = sizeof(g_hmac_domain) - 1u;
    memcpy(material, g_hmac_domain, request_offset);
    memcpy(material + request_offset, request, sizeof(*request));
    memset(material + request_offset + offsetof(San9S5NoApplyRequest, hmac),
        0, SAN9_S5_DIGEST_SIZE);
    san9_p1_hmac_sha256(key, SAN9_S5_HMAC_KEY_SIZE,
        material, sizeof(material), output);
    san9_p1_secure_zero(material, sizeof(material));
}

static void apply_request_hmac(
    const San9S5NoApplyRequest *request,
    const uint8_t key[SAN9_S5_HMAC_KEY_SIZE],
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    uint8_t material[(sizeof(g_apply_hmac_domain) - 1u)
        + SAN9_S5_REQUEST_SIZE];
    const size_t request_offset = sizeof(g_apply_hmac_domain) - 1u;
    memcpy(material, g_apply_hmac_domain, request_offset);
    memcpy(material + request_offset, request, sizeof(*request));
    memset(material + request_offset + offsetof(San9S5NoApplyRequest, hmac),
        0, SAN9_S5_DIGEST_SIZE);
    san9_p1_hmac_sha256(key, SAN9_S5_HMAC_KEY_SIZE,
        material, sizeof(material), output);
    san9_p1_secure_zero(material, sizeof(material));
}

static void cultivate_request_hmac(
    const San9S5NoApplyRequest *request,
    const uint8_t key[SAN9_S5_HMAC_KEY_SIZE],
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    uint8_t material[(sizeof(g_cultivate_hmac_domain) - 1u)
        + SAN9_S5_REQUEST_SIZE];
    const size_t request_offset = sizeof(g_cultivate_hmac_domain) - 1u;
    memcpy(material, g_cultivate_hmac_domain, request_offset);
    memcpy(material + request_offset, request, sizeof(*request));
    memset(material + request_offset + offsetof(San9S5NoApplyRequest, hmac),
        0, SAN9_S5_DIGEST_SIZE);
    san9_p1_hmac_sha256(key, SAN9_S5_HMAC_KEY_SIZE,
        material, sizeof(material), output);
    san9_p1_secure_zero(material, sizeof(material));
}

static void patrol_request_hmac(
    const San9S5NoApplyRequest *request,
    const uint8_t key[SAN9_S5_HMAC_KEY_SIZE],
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    uint8_t material[(sizeof(g_patrol_hmac_domain) - 1u)
        + SAN9_S5_REQUEST_SIZE];
    const size_t request_offset = sizeof(g_patrol_hmac_domain) - 1u;
    memcpy(material, g_patrol_hmac_domain, request_offset);
    memcpy(material + request_offset, request, sizeof(*request));
    memset(material + request_offset + offsetof(San9S5NoApplyRequest, hmac),
        0, SAN9_S5_DIGEST_SIZE);
    san9_p1_hmac_sha256(key, SAN9_S5_HMAC_KEY_SIZE,
        material, sizeof(material), output);
    san9_p1_secure_zero(material, sizeof(material));
}

static void train_request_hmac(
    const San9S5NoApplyRequest *request,
    const uint8_t key[SAN9_S5_HMAC_KEY_SIZE],
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    uint8_t material[(sizeof(g_train_hmac_domain) - 1u)
        + SAN9_S5_REQUEST_SIZE];
    const size_t request_offset = sizeof(g_train_hmac_domain) - 1u;
    memcpy(material, g_train_hmac_domain, request_offset);
    memcpy(material + request_offset, request, sizeof(*request));
    memset(material + request_offset + offsetof(San9S5NoApplyRequest, hmac),
        0, SAN9_S5_DIGEST_SIZE);
    san9_p1_hmac_sha256(key, SAN9_S5_HMAC_KEY_SIZE,
        material, sizeof(material), output);
    san9_p1_secure_zero(material, sizeof(material));
}

static void repair_request_hmac(
    const San9S5NoApplyRequest *request,
    const uint8_t key[SAN9_S5_HMAC_KEY_SIZE],
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    uint8_t material[(sizeof(g_repair_hmac_domain) - 1u)
        + SAN9_S5_REQUEST_SIZE];
    const size_t request_offset = sizeof(g_repair_hmac_domain) - 1u;
    memcpy(material, g_repair_hmac_domain, request_offset);
    memcpy(material + request_offset, request, sizeof(*request));
    memset(material + request_offset + offsetof(San9S5NoApplyRequest, hmac),
        0, SAN9_S5_DIGEST_SIZE);
    san9_p1_hmac_sha256(key, SAN9_S5_HMAC_KEY_SIZE,
        material, sizeof(material), output);
    san9_p1_secure_zero(material, sizeof(material));
}

San9S5RequestStatus san9_s5_no_apply_request_initialize(
    San9S5NoApplyRequest *request,
    uint32_t expected_city_id,
    uint32_t expected_corps_id,
    const uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT],
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_nonce[SAN9_S5_NONCE_SIZE],
    const uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    const uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE])
{
    San9S5NoApplyRequest candidate;
    San9S5RequestStatus status;
    if (request == NULL || expected_person_ids == NULL || request_nonce == NULL
        || p1_binding_digest == NULL || precondition_digest == NULL) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    memset(&candidate, 0, sizeof(candidate));
    candidate.magic = SAN9_S5_NO_APPLY_MAGIC;
    candidate.schema_major = SAN9_S5_SCHEMA_MAJOR;
    candidate.schema_minor = SAN9_S5_SCHEMA_MINOR;
    candidate.declared_size = SAN9_S5_REQUEST_SIZE;
    candidate.kind = SAN9_S5_REQUEST_KIND_NO_APPLY;
    candidate.flags = SAN9_S5_FLAG_CURRENT_CITY_ONLY | SAN9_S5_FLAG_NO_APPLY;
    candidate.native_command_id = SAN9_S5_COMMERCE_NATIVE_ID;
    candidate.event_code = SAN9_S5_COMMERCE_EVENT_CODE;
    candidate.sequence = SAN9_S5_SEQUENCE_ONE;
    candidate.expected_city_id = expected_city_id;
    candidate.expected_corps_id = expected_corps_id;
    candidate.exact_person_count = SAN9_S5_TOP5_COUNT;
    memcpy(candidate.expected_person_ids, expected_person_ids,
        sizeof(candidate.expected_person_ids));
    candidate.issued_at_ms = issued_at_ms;
    candidate.expires_at_ms = expires_at_ms;
    memcpy(candidate.request_nonce, request_nonce,
        sizeof(candidate.request_nonce));
    memcpy(candidate.p1_binding_digest, p1_binding_digest,
        sizeof(candidate.p1_binding_digest));
    memcpy(candidate.precondition_digest, precondition_digest,
        sizeof(candidate.precondition_digest));
    status = request_shape_status(&candidate);
    if (status == SAN9_S5_REQUEST_VALID) {
        memcpy(request, &candidate, sizeof(candidate));
    }
    san9_p1_secure_zero(&candidate, sizeof(candidate));
    return status;
}

San9S5RequestStatus san9_s5_apply_once_request_initialize(
    San9S5NoApplyRequest *request,
    uint32_t expected_city_id,
    uint32_t expected_corps_id,
    const uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT],
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_nonce[SAN9_S5_NONCE_SIZE],
    const uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    const uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE])
{
    San9S5NoApplyRequest candidate;
    San9S5RequestStatus status;
    if (request == NULL || expected_person_ids == NULL || request_nonce == NULL
        || p1_binding_digest == NULL || precondition_digest == NULL) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    memset(&candidate, 0, sizeof(candidate));
    candidate.magic = SAN9_S5_APPLY_ONCE_MAGIC;
    candidate.schema_major = SAN9_S5_SCHEMA_MAJOR;
    candidate.schema_minor = SAN9_S5_SCHEMA_MINOR;
    candidate.declared_size = SAN9_S5_REQUEST_SIZE;
    candidate.kind = SAN9_S5_REQUEST_KIND_APPLY_ONCE;
    candidate.flags = SAN9_S5_FLAG_CURRENT_CITY_ONLY
        | SAN9_S5_FLAG_APPLY_ONCE;
    candidate.native_command_id = SAN9_S5_COMMERCE_NATIVE_ID;
    candidate.event_code = SAN9_S5_COMMERCE_EVENT_CODE;
    candidate.sequence = SAN9_S5_SEQUENCE_ONE;
    candidate.expected_city_id = expected_city_id;
    candidate.expected_corps_id = expected_corps_id;
    candidate.exact_person_count = SAN9_S5_TOP5_COUNT;
    memcpy(candidate.expected_person_ids, expected_person_ids,
        sizeof(candidate.expected_person_ids));
    candidate.issued_at_ms = issued_at_ms;
    candidate.expires_at_ms = expires_at_ms;
    memcpy(candidate.request_nonce, request_nonce,
        sizeof(candidate.request_nonce));
    memcpy(candidate.p1_binding_digest, p1_binding_digest,
        sizeof(candidate.p1_binding_digest));
    memcpy(candidate.precondition_digest, precondition_digest,
        sizeof(candidate.precondition_digest));
    status = apply_request_shape_status(&candidate);
    if (status == SAN9_S5_REQUEST_VALID) {
        memcpy(request, &candidate, sizeof(candidate));
    }
    san9_p1_secure_zero(&candidate, sizeof(candidate));
    return status;
}

San9S5RequestStatus san9_s6_cultivate_apply_once_request_initialize(
    San9S5NoApplyRequest *request,
    uint32_t expected_city_id,
    uint32_t expected_corps_id,
    const uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT],
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_nonce[SAN9_S5_NONCE_SIZE],
    const uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    const uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE])
{
    San9S5NoApplyRequest candidate;
    San9S5RequestStatus status;
    if (request == NULL || expected_person_ids == NULL || request_nonce == NULL
        || p1_binding_digest == NULL || precondition_digest == NULL) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    memset(&candidate, 0, sizeof(candidate));
    candidate.magic = SAN9_S6_CULTIVATE_APPLY_ONCE_MAGIC;
    candidate.schema_major = SAN9_S5_SCHEMA_MAJOR;
    candidate.schema_minor = SAN9_S5_SCHEMA_MINOR;
    candidate.declared_size = SAN9_S5_REQUEST_SIZE;
    candidate.kind = SAN9_S6_REQUEST_KIND_CULTIVATE_APPLY_ONCE;
    candidate.flags = SAN9_S5_FLAG_CURRENT_CITY_ONLY
        | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_CULTIVATE;
    candidate.native_command_id = SAN9_S6_CULTIVATE_NATIVE_ID;
    candidate.event_code = SAN9_S6_CULTIVATE_EVENT_CODE;
    candidate.sequence = SAN9_S5_SEQUENCE_ONE;
    candidate.expected_city_id = expected_city_id;
    candidate.expected_corps_id = expected_corps_id;
    candidate.exact_person_count = SAN9_S5_TOP5_COUNT;
    memcpy(candidate.expected_person_ids, expected_person_ids,
        sizeof(candidate.expected_person_ids));
    candidate.issued_at_ms = issued_at_ms;
    candidate.expires_at_ms = expires_at_ms;
    memcpy(candidate.request_nonce, request_nonce, sizeof(candidate.request_nonce));
    memcpy(candidate.p1_binding_digest, p1_binding_digest,
        sizeof(candidate.p1_binding_digest));
    memcpy(candidate.precondition_digest, precondition_digest,
        sizeof(candidate.precondition_digest));
    status = cultivate_request_shape_status(&candidate);
    if (status == SAN9_S5_REQUEST_VALID) {
        memcpy(request, &candidate, sizeof(candidate));
    }
    san9_p1_secure_zero(&candidate, sizeof(candidate));
    return status;
}

San9S5RequestStatus san9_s6_patrol_apply_once_request_initialize(
    San9S5NoApplyRequest *request,
    uint32_t expected_city_id,
    uint32_t expected_corps_id,
    const uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT],
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_nonce[SAN9_S5_NONCE_SIZE],
    const uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    const uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE])
{
    San9S5NoApplyRequest candidate;
    San9S5RequestStatus status;
    if (request == NULL || expected_person_ids == NULL || request_nonce == NULL
        || p1_binding_digest == NULL || precondition_digest == NULL) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    memset(&candidate, 0, sizeof(candidate));
    candidate.magic = SAN9_S6_PATROL_APPLY_ONCE_MAGIC;
    candidate.schema_major = SAN9_S5_SCHEMA_MAJOR;
    candidate.schema_minor = SAN9_S5_SCHEMA_MINOR;
    candidate.declared_size = SAN9_S5_REQUEST_SIZE;
    candidate.kind = SAN9_S6_REQUEST_KIND_PATROL_APPLY_ONCE;
    candidate.flags = SAN9_S5_FLAG_CURRENT_CITY_ONLY
        | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_PATROL;
    candidate.native_command_id = SAN9_S6_PATROL_NATIVE_ID;
    candidate.event_code = SAN9_S6_PATROL_EVENT_CODE;
    candidate.sequence = SAN9_S5_SEQUENCE_ONE;
    candidate.expected_city_id = expected_city_id;
    candidate.expected_corps_id = expected_corps_id;
    candidate.exact_person_count = SAN9_S5_TOP5_COUNT;
    memcpy(candidate.expected_person_ids, expected_person_ids,
        sizeof(candidate.expected_person_ids));
    candidate.issued_at_ms = issued_at_ms;
    candidate.expires_at_ms = expires_at_ms;
    memcpy(candidate.request_nonce, request_nonce, sizeof(candidate.request_nonce));
    memcpy(candidate.p1_binding_digest, p1_binding_digest,
        sizeof(candidate.p1_binding_digest));
    memcpy(candidate.precondition_digest, precondition_digest,
        sizeof(candidate.precondition_digest));
    status = patrol_request_shape_status(&candidate);
    if (status == SAN9_S5_REQUEST_VALID) {
        memcpy(request, &candidate, sizeof(candidate));
    }
    san9_p1_secure_zero(&candidate, sizeof(candidate));
    return status;
}

San9S5RequestStatus san9_s6_train_apply_once_request_initialize(
    San9S5NoApplyRequest *request,
    uint32_t expected_city_id,
    uint32_t expected_corps_id,
    const uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT],
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_nonce[SAN9_S5_NONCE_SIZE],
    const uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    const uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE])
{
    San9S5NoApplyRequest candidate;
    San9S5RequestStatus status;
    if (request == NULL || expected_person_ids == NULL || request_nonce == NULL
        || p1_binding_digest == NULL || precondition_digest == NULL) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    memset(&candidate, 0, sizeof(candidate));
    candidate.magic = SAN9_S6_TRAIN_APPLY_ONCE_MAGIC;
    candidate.schema_major = SAN9_S5_SCHEMA_MAJOR;
    candidate.schema_minor = SAN9_S5_SCHEMA_MINOR;
    candidate.declared_size = SAN9_S5_REQUEST_SIZE;
    candidate.kind = SAN9_S6_REQUEST_KIND_TRAIN_APPLY_ONCE;
    candidate.flags = SAN9_S5_FLAG_CURRENT_CITY_ONLY
        | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_TRAIN;
    candidate.native_command_id = SAN9_S6_TRAIN_NATIVE_ID;
    candidate.event_code = SAN9_S6_TRAIN_EVENT_CODE;
    candidate.sequence = SAN9_S5_SEQUENCE_ONE;
    candidate.expected_city_id = expected_city_id;
    candidate.expected_corps_id = expected_corps_id;
    candidate.exact_person_count = SAN9_S5_TOP5_COUNT;
    memcpy(candidate.expected_person_ids, expected_person_ids,
        sizeof(candidate.expected_person_ids));
    candidate.issued_at_ms = issued_at_ms;
    candidate.expires_at_ms = expires_at_ms;
    memcpy(candidate.request_nonce, request_nonce, sizeof(candidate.request_nonce));
    memcpy(candidate.p1_binding_digest, p1_binding_digest,
        sizeof(candidate.p1_binding_digest));
    memcpy(candidate.precondition_digest, precondition_digest,
        sizeof(candidate.precondition_digest));
    status = train_request_shape_status(&candidate);
    if (status == SAN9_S5_REQUEST_VALID) {
        memcpy(request, &candidate, sizeof(candidate));
    }
    san9_p1_secure_zero(&candidate, sizeof(candidate));
    return status;
}

San9S5RequestStatus san9_s6_repair_apply_once_request_initialize(
    San9S5NoApplyRequest *request,
    uint32_t expected_city_id,
    uint32_t expected_corps_id,
    const uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT],
    uint64_t issued_at_ms,
    uint64_t expires_at_ms,
    const uint8_t request_nonce[SAN9_S5_NONCE_SIZE],
    const uint8_t p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    const uint8_t precondition_digest[SAN9_S5_DIGEST_SIZE])
{
    San9S5NoApplyRequest candidate;
    San9S5RequestStatus status;
    if (request == NULL || expected_person_ids == NULL || request_nonce == NULL
        || p1_binding_digest == NULL || precondition_digest == NULL) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    memset(&candidate, 0, sizeof(candidate));
    candidate.magic = SAN9_S6_REPAIR_APPLY_ONCE_MAGIC;
    candidate.schema_major = SAN9_S5_SCHEMA_MAJOR;
    candidate.schema_minor = SAN9_S5_SCHEMA_MINOR;
    candidate.declared_size = SAN9_S5_REQUEST_SIZE;
    candidate.kind = SAN9_S6_REQUEST_KIND_REPAIR_APPLY_ONCE;
    candidate.flags = SAN9_S5_FLAG_CURRENT_CITY_ONLY
        | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_REPAIR;
    candidate.native_command_id = SAN9_S6_REPAIR_NATIVE_ID;
    candidate.event_code = SAN9_S6_REPAIR_EVENT_CODE;
    candidate.sequence = SAN9_S5_SEQUENCE_ONE;
    candidate.expected_city_id = expected_city_id;
    candidate.expected_corps_id = expected_corps_id;
    candidate.exact_person_count = SAN9_S5_TOP5_COUNT;
    memcpy(candidate.expected_person_ids, expected_person_ids,
        sizeof(candidate.expected_person_ids));
    candidate.issued_at_ms = issued_at_ms;
    candidate.expires_at_ms = expires_at_ms;
    memcpy(candidate.request_nonce, request_nonce, sizeof(candidate.request_nonce));
    memcpy(candidate.p1_binding_digest, p1_binding_digest,
        sizeof(candidate.p1_binding_digest));
    memcpy(candidate.precondition_digest, precondition_digest,
        sizeof(candidate.precondition_digest));
    status = repair_request_shape_status(&candidate);
    if (status == SAN9_S5_REQUEST_VALID) {
        memcpy(request, &candidate, sizeof(candidate));
    }
    san9_p1_secure_zero(&candidate, sizeof(candidate));
    return status;
}

San9S5RequestStatus san9_s5_no_apply_request_sign(
    San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status = request_shape_status(request);
    if (status != SAN9_S5_REQUEST_VALID) {
        return status;
    }
    if (!key_valid(hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_KEY_INVALID;
    }
    if (ranges_overlap(request, sizeof(*request), hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    request_hmac(request, hmac_key, digest);
    memcpy(request->hmac, digest, sizeof(request->hmac));
    san9_p1_secure_zero(digest, sizeof(digest));
    return SAN9_S5_REQUEST_VALID;
}

San9S5RequestStatus san9_s5_apply_once_request_sign(
    San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status = apply_request_shape_status(request);
    if (status != SAN9_S5_REQUEST_VALID) {
        return status;
    }
    if (!key_valid(hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_KEY_INVALID;
    }
    if (ranges_overlap(request, sizeof(*request), hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    apply_request_hmac(request, hmac_key, digest);
    memcpy(request->hmac, digest, sizeof(request->hmac));
    san9_p1_secure_zero(digest, sizeof(digest));
    return SAN9_S5_REQUEST_VALID;
}

San9S5RequestStatus san9_s6_cultivate_apply_once_request_sign(
    San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status = cultivate_request_shape_status(request);
    if (status != SAN9_S5_REQUEST_VALID) {
        return status;
    }
    if (!key_valid(hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_KEY_INVALID;
    }
    if (ranges_overlap(request, sizeof(*request), hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    cultivate_request_hmac(request, hmac_key, digest);
    memcpy(request->hmac, digest, sizeof(request->hmac));
    san9_p1_secure_zero(digest, sizeof(digest));
    return SAN9_S5_REQUEST_VALID;
}

San9S5RequestStatus san9_s6_patrol_apply_once_request_sign(
    San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status = patrol_request_shape_status(request);
    if (status != SAN9_S5_REQUEST_VALID) {
        return status;
    }
    if (!key_valid(hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_KEY_INVALID;
    }
    if (ranges_overlap(request, sizeof(*request), hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    patrol_request_hmac(request, hmac_key, digest);
    memcpy(request->hmac, digest, sizeof(request->hmac));
    san9_p1_secure_zero(digest, sizeof(digest));
    return SAN9_S5_REQUEST_VALID;
}

San9S5RequestStatus san9_s6_train_apply_once_request_sign(
    San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status = train_request_shape_status(request);
    if (status != SAN9_S5_REQUEST_VALID) {
        return status;
    }
    if (!key_valid(hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_KEY_INVALID;
    }
    if (ranges_overlap(request, sizeof(*request), hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    train_request_hmac(request, hmac_key, digest);
    memcpy(request->hmac, digest, sizeof(request->hmac));
    san9_p1_secure_zero(digest, sizeof(digest));
    return SAN9_S5_REQUEST_VALID;
}

San9S5RequestStatus san9_s6_repair_apply_once_request_sign(
    San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status = repair_request_shape_status(request);
    if (status != SAN9_S5_REQUEST_VALID) return status;
    if (!key_valid(hmac_key, hmac_key_size)) return SAN9_S5_REQUEST_KEY_INVALID;
    if (ranges_overlap(request, sizeof(*request), hmac_key, hmac_key_size))
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    repair_request_hmac(request, hmac_key, digest);
    memcpy(request->hmac, digest, sizeof(request->hmac));
    san9_p1_secure_zero(digest, sizeof(digest));
    return SAN9_S5_REQUEST_VALID;
}

San9S5RequestStatus san9_s5_no_apply_request_verify(
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    uint64_t now_ms)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status = request_shape_status(request);
    if (status != SAN9_S5_REQUEST_VALID) {
        return status;
    }
    if (!key_valid(hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_KEY_INVALID;
    }
    if (ranges_overlap(request, sizeof(*request), hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    if (expected_p1_binding_digest == NULL
        || bytes_zero(expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE)) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    request_hmac(request, hmac_key, digest);
    if (!san9_p1_constant_time_equal(
            request->hmac, digest, sizeof(request->hmac))) {
        san9_p1_secure_zero(digest, sizeof(digest));
        return SAN9_S5_REQUEST_HMAC_MISMATCH;
    }
    san9_p1_secure_zero(digest, sizeof(digest));
    if (!san9_p1_constant_time_equal(request->p1_binding_digest,
            expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE)) {
        return SAN9_S5_REQUEST_BINDING_MISMATCH;
    }
    if (now_ms < request->issued_at_ms) {
        return SAN9_S5_REQUEST_FROM_FUTURE;
    }
    if (now_ms > request->expires_at_ms) {
        return SAN9_S5_REQUEST_EXPIRED;
    }
    return SAN9_S5_REQUEST_VALID;
}

San9S5RequestStatus san9_s5_apply_once_request_verify(
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    uint64_t now_ms)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status = apply_request_shape_status(request);
    if (status != SAN9_S5_REQUEST_VALID) {
        return status;
    }
    if (!key_valid(hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_KEY_INVALID;
    }
    if (ranges_overlap(request, sizeof(*request), hmac_key, hmac_key_size)
        || expected_p1_binding_digest == NULL
        || bytes_zero(expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE)) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    apply_request_hmac(request, hmac_key, digest);
    if (!san9_p1_constant_time_equal(
            request->hmac, digest, sizeof(request->hmac))) {
        san9_p1_secure_zero(digest, sizeof(digest));
        return SAN9_S5_REQUEST_HMAC_MISMATCH;
    }
    san9_p1_secure_zero(digest, sizeof(digest));
    if (!san9_p1_constant_time_equal(request->p1_binding_digest,
            expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE)) {
        return SAN9_S5_REQUEST_BINDING_MISMATCH;
    }
    if (now_ms < request->issued_at_ms) {
        return SAN9_S5_REQUEST_FROM_FUTURE;
    }
    return now_ms > request->expires_at_ms
        ? SAN9_S5_REQUEST_EXPIRED : SAN9_S5_REQUEST_VALID;
}

San9S5RequestStatus san9_s6_cultivate_apply_once_request_verify(
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    uint64_t now_ms)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status = cultivate_request_shape_status(request);
    if (status != SAN9_S5_REQUEST_VALID) {
        return status;
    }
    if (!key_valid(hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_KEY_INVALID;
    }
    if (ranges_overlap(request, sizeof(*request), hmac_key, hmac_key_size)
        || expected_p1_binding_digest == NULL
        || bytes_zero(expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE)) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    cultivate_request_hmac(request, hmac_key, digest);
    if (!san9_p1_constant_time_equal(request->hmac, digest,
            sizeof(request->hmac))) {
        san9_p1_secure_zero(digest, sizeof(digest));
        return SAN9_S5_REQUEST_HMAC_MISMATCH;
    }
    san9_p1_secure_zero(digest, sizeof(digest));
    if (!san9_p1_constant_time_equal(request->p1_binding_digest,
            expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE)) {
        return SAN9_S5_REQUEST_BINDING_MISMATCH;
    }
    if (now_ms < request->issued_at_ms) {
        return SAN9_S5_REQUEST_FROM_FUTURE;
    }
    return now_ms > request->expires_at_ms
        ? SAN9_S5_REQUEST_EXPIRED : SAN9_S5_REQUEST_VALID;
}

San9S5RequestStatus san9_s6_patrol_apply_once_request_verify(
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    uint64_t now_ms)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status = patrol_request_shape_status(request);
    if (status != SAN9_S5_REQUEST_VALID) {
        return status;
    }
    if (!key_valid(hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_KEY_INVALID;
    }
    if (ranges_overlap(request, sizeof(*request), hmac_key, hmac_key_size)
        || expected_p1_binding_digest == NULL
        || bytes_zero(expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE)) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    patrol_request_hmac(request, hmac_key, digest);
    if (!san9_p1_constant_time_equal(request->hmac, digest,
            sizeof(request->hmac))) {
        san9_p1_secure_zero(digest, sizeof(digest));
        return SAN9_S5_REQUEST_HMAC_MISMATCH;
    }
    san9_p1_secure_zero(digest, sizeof(digest));
    if (!san9_p1_constant_time_equal(request->p1_binding_digest,
            expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE)) {
        return SAN9_S5_REQUEST_BINDING_MISMATCH;
    }
    if (now_ms < request->issued_at_ms) {
        return SAN9_S5_REQUEST_FROM_FUTURE;
    }
    return now_ms > request->expires_at_ms
        ? SAN9_S5_REQUEST_EXPIRED : SAN9_S5_REQUEST_VALID;
}

San9S5RequestStatus san9_s6_train_apply_once_request_verify(
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    uint64_t now_ms)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status = train_request_shape_status(request);
    if (status != SAN9_S5_REQUEST_VALID) {
        return status;
    }
    if (!key_valid(hmac_key, hmac_key_size)) {
        return SAN9_S5_REQUEST_KEY_INVALID;
    }
    if (ranges_overlap(request, sizeof(*request), hmac_key, hmac_key_size)
        || expected_p1_binding_digest == NULL
        || bytes_zero(expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE)) {
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    train_request_hmac(request, hmac_key, digest);
    if (!san9_p1_constant_time_equal(request->hmac, digest,
            sizeof(request->hmac))) {
        san9_p1_secure_zero(digest, sizeof(digest));
        return SAN9_S5_REQUEST_HMAC_MISMATCH;
    }
    san9_p1_secure_zero(digest, sizeof(digest));
    if (!san9_p1_constant_time_equal(request->p1_binding_digest,
            expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE)) {
        return SAN9_S5_REQUEST_BINDING_MISMATCH;
    }
    if (now_ms < request->issued_at_ms) {
        return SAN9_S5_REQUEST_FROM_FUTURE;
    }
    return now_ms > request->expires_at_ms
        ? SAN9_S5_REQUEST_EXPIRED : SAN9_S5_REQUEST_VALID;
}

San9S5RequestStatus san9_s6_repair_apply_once_request_verify(
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE],
    uint64_t now_ms)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status = repair_request_shape_status(request);
    if (status != SAN9_S5_REQUEST_VALID) return status;
    if (!key_valid(hmac_key, hmac_key_size)) return SAN9_S5_REQUEST_KEY_INVALID;
    if (ranges_overlap(request, sizeof(*request), hmac_key, hmac_key_size)
        || expected_p1_binding_digest == NULL
        || bytes_zero(expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE))
        return SAN9_S5_REQUEST_INVALID_ARGUMENT;
    repair_request_hmac(request, hmac_key, digest);
    if (!san9_p1_constant_time_equal(request->hmac, digest,
            sizeof(request->hmac))) {
        san9_p1_secure_zero(digest, sizeof(digest));
        return SAN9_S5_REQUEST_HMAC_MISMATCH;
    }
    san9_p1_secure_zero(digest, sizeof(digest));
    if (!san9_p1_constant_time_equal(request->p1_binding_digest,
            expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE))
        return SAN9_S5_REQUEST_BINDING_MISMATCH;
    if (now_ms < request->issued_at_ms) return SAN9_S5_REQUEST_FROM_FUTURE;
    return now_ms > request->expires_at_ms
        ? SAN9_S5_REQUEST_EXPIRED : SAN9_S5_REQUEST_VALID;
}

int san9_s5_no_apply_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    San9P1Sha256Context sha;
    if (request == NULL || output == NULL
        || ranges_overlap(request, sizeof(*request), output,
            SAN9_S5_DIGEST_SIZE)) {
        return 0;
    }
    san9_p1_sha256_initialize(&sha);
    san9_p1_sha256_update(&sha, g_digest_domain,
        sizeof(g_digest_domain) - 1u);
    san9_p1_sha256_update(&sha, (const uint8_t *)request,
        sizeof(*request));
    san9_p1_sha256_finish(&sha, output);
    return 1;
}

int san9_s5_apply_once_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    San9P1Sha256Context sha;
    if (apply_request_shape_status(request) != SAN9_S5_REQUEST_VALID
        || output == NULL
        || ranges_overlap(request, sizeof(*request), output,
            SAN9_S5_DIGEST_SIZE)) {
        return 0;
    }
    san9_p1_sha256_initialize(&sha);
    san9_p1_sha256_update(&sha, g_apply_digest_domain,
        sizeof(g_apply_digest_domain) - 1u);
    san9_p1_sha256_update(&sha, (const uint8_t *)request,
        sizeof(*request));
    san9_p1_sha256_finish(&sha, output);
    return 1;
}

int san9_s6_cultivate_apply_once_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    San9P1Sha256Context sha;
    if (cultivate_request_shape_status(request) != SAN9_S5_REQUEST_VALID
        || output == NULL
        || ranges_overlap(request, sizeof(*request), output,
            SAN9_S5_DIGEST_SIZE)) {
        return 0;
    }
    san9_p1_sha256_initialize(&sha);
    san9_p1_sha256_update(&sha, g_cultivate_digest_domain,
        sizeof(g_cultivate_digest_domain) - 1u);
    san9_p1_sha256_update(&sha, (const uint8_t *)request, sizeof(*request));
    san9_p1_sha256_finish(&sha, output);
    return 1;
}

int san9_s6_patrol_apply_once_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    San9P1Sha256Context sha;
    if (patrol_request_shape_status(request) != SAN9_S5_REQUEST_VALID
        || output == NULL
        || ranges_overlap(request, sizeof(*request), output,
            SAN9_S5_DIGEST_SIZE)) {
        return 0;
    }
    san9_p1_sha256_initialize(&sha);
    san9_p1_sha256_update(&sha, g_patrol_digest_domain,
        sizeof(g_patrol_digest_domain) - 1u);
    san9_p1_sha256_update(&sha, (const uint8_t *)request, sizeof(*request));
    san9_p1_sha256_finish(&sha, output);
    return 1;
}

int san9_s6_train_apply_once_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    San9P1Sha256Context sha;
    if (train_request_shape_status(request) != SAN9_S5_REQUEST_VALID
        || output == NULL
        || ranges_overlap(request, sizeof(*request), output,
            SAN9_S5_DIGEST_SIZE)) {
        return 0;
    }
    san9_p1_sha256_initialize(&sha);
    san9_p1_sha256_update(&sha, g_train_digest_domain,
        sizeof(g_train_digest_domain) - 1u);
    san9_p1_sha256_update(&sha, (const uint8_t *)request, sizeof(*request));
    san9_p1_sha256_finish(&sha, output);
    return 1;
}

int san9_s6_repair_apply_once_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    San9P1Sha256Context sha;
    if (repair_request_shape_status(request) != SAN9_S5_REQUEST_VALID
        || output == NULL || ranges_overlap(request, sizeof(*request), output,
            SAN9_S5_DIGEST_SIZE)) return 0;
    san9_p1_sha256_initialize(&sha);
    san9_p1_sha256_update(&sha, g_repair_digest_domain,
        sizeof(g_repair_digest_domain) - 1u);
    san9_p1_sha256_update(&sha, (const uint8_t *)request, sizeof(*request));
    san9_p1_sha256_finish(&sha, output);
    return 1;
}

static int supported_request_digest(
    const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    if (request != NULL
        && request->kind == SAN9_S5_REQUEST_KIND_APPLY_ONCE) {
        return san9_s5_apply_once_request_digest(request, output);
    }
    if (request != NULL
        && request->kind == SAN9_S6_REQUEST_KIND_CULTIVATE_APPLY_ONCE) {
        return san9_s6_cultivate_apply_once_request_digest(request, output);
    }
    if (request != NULL
        && request->kind == SAN9_S6_REQUEST_KIND_PATROL_APPLY_ONCE) {
        return san9_s6_patrol_apply_once_request_digest(request, output);
    }
    if (request != NULL
        && request->kind == SAN9_S6_REQUEST_KIND_TRAIN_APPLY_ONCE) {
        return san9_s6_train_apply_once_request_digest(request, output);
    }
    if (request != NULL
        && request->kind == SAN9_S6_REQUEST_KIND_REPAIR_APPLY_ONCE) {
        return san9_s6_repair_apply_once_request_digest(request, output);
    }
    return san9_s5_no_apply_request_digest(request, output);
}

int san9_s5_no_apply_gate_initialize(
    San9S5NoApplyGate *gate,
    const uint8_t expected_p1_binding_digest[SAN9_S5_DIGEST_SIZE])
{
    uint8_t binding[SAN9_S5_DIGEST_SIZE];
    if (gate == NULL || expected_p1_binding_digest == NULL
        || bytes_zero(expected_p1_binding_digest, SAN9_S5_DIGEST_SIZE)) {
        return 0;
    }
    memcpy(binding, expected_p1_binding_digest, sizeof(binding));
    memset(gate, 0, sizeof(*gate));
    memcpy(gate->expected_p1_binding_digest, binding, sizeof(binding));
    san9_p1_secure_zero(binding, sizeof(binding));
    return 1;
}

static int gate_shape_valid(const San9S5NoApplyGate *gate)
{
    if (gate == NULL
        || bytes_zero(gate->expected_p1_binding_digest,
            sizeof(gate->expected_p1_binding_digest))
        || gate->accepted_count > 1u || gate->has_observed_time > 1u
        || gate->clock_faulted > 1u) {
        return 0;
    }
    if (gate->accepted_count == 0u) {
        return bytes_zero(gate->accepted_nonce, sizeof(gate->accepted_nonce))
            && bytes_zero(gate->accepted_request_digest,
                sizeof(gate->accepted_request_digest));
    }
    return !bytes_zero(gate->accepted_nonce, sizeof(gate->accepted_nonce))
        && !bytes_zero(gate->accepted_request_digest,
            sizeof(gate->accepted_request_digest));
}

San9S5GateStatus san9_s5_no_apply_gate_accept(
    San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status;
    if ((request_status != NULL
            && (ranges_overlap(request_status, sizeof(*request_status), gate,
                    sizeof(*gate))
                || ranges_overlap(request_status, sizeof(*request_status),
                    request, sizeof(*request))
                || ranges_overlap(request_status, sizeof(*request_status),
                    hmac_key, hmac_key_size)))
        || ranges_overlap(gate, sizeof(*gate), request, sizeof(*request))
        || ranges_overlap(gate, sizeof(*gate), hmac_key, hmac_key_size)) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    if (request_status != NULL) {
        *request_status = SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    if (gate == NULL || request == NULL) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    if (!gate_shape_valid(gate)) {
        gate->clock_faulted = 1u;
        return SAN9_S5_GATE_CORRUPTED;
    }
    if (gate->clock_faulted != 0u) {
        return SAN9_S5_GATE_CLOCK_FAULTED;
    }
    if (gate->has_observed_time != 0u
        && now_ms < gate->last_observed_now_ms) {
        gate->clock_faulted = 1u;
        return SAN9_S5_GATE_CLOCK_ROLLBACK;
    }
    gate->last_observed_now_ms = now_ms;
    gate->has_observed_time = 1u;
    if (gate->accepted_count != 0u) {
        return SAN9_S5_GATE_REPLAY;
    }
    status = san9_s5_no_apply_request_verify(request, hmac_key,
        hmac_key_size, gate->expected_p1_binding_digest, now_ms);
    if (request_status != NULL) {
        *request_status = status;
    }
    if (status != SAN9_S5_REQUEST_VALID) {
        return SAN9_S5_GATE_REQUEST_REJECTED;
    }
    if (!san9_s5_no_apply_request_digest(request, digest)) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    memcpy(gate->accepted_nonce, request->request_nonce,
        sizeof(gate->accepted_nonce));
    memcpy(gate->accepted_request_digest, digest,
        sizeof(gate->accepted_request_digest));
    gate->accepted_count = 1u;
    san9_p1_secure_zero(digest, sizeof(digest));
    return SAN9_S5_GATE_ACCEPTED;
}

San9S5GateStatus san9_s5_apply_once_gate_accept(
    San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status;
    if ((request_status != NULL
            && (ranges_overlap(request_status, sizeof(*request_status), gate,
                    sizeof(*gate))
                || ranges_overlap(request_status, sizeof(*request_status),
                    request, sizeof(*request))
                || ranges_overlap(request_status, sizeof(*request_status),
                    hmac_key, hmac_key_size)))
        || ranges_overlap(gate, sizeof(*gate), request, sizeof(*request))
        || ranges_overlap(gate, sizeof(*gate), hmac_key, hmac_key_size)) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    if (request_status != NULL) {
        *request_status = SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    if (gate == NULL || request == NULL) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    if (!gate_shape_valid(gate)) {
        gate->clock_faulted = 1u;
        return SAN9_S5_GATE_CORRUPTED;
    }
    if (gate->clock_faulted != 0u) {
        return SAN9_S5_GATE_CLOCK_FAULTED;
    }
    if (gate->has_observed_time != 0u
        && now_ms < gate->last_observed_now_ms) {
        gate->clock_faulted = 1u;
        return SAN9_S5_GATE_CLOCK_ROLLBACK;
    }
    gate->last_observed_now_ms = now_ms;
    gate->has_observed_time = 1u;
    if (gate->accepted_count != 0u) {
        return SAN9_S5_GATE_REPLAY;
    }
    status = san9_s5_apply_once_request_verify(request, hmac_key,
        hmac_key_size, gate->expected_p1_binding_digest, now_ms);
    if (request_status != NULL) {
        *request_status = status;
    }
    if (status != SAN9_S5_REQUEST_VALID) {
        return SAN9_S5_GATE_REQUEST_REJECTED;
    }
    if (!san9_s5_apply_once_request_digest(request, digest)) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    memcpy(gate->accepted_nonce, request->request_nonce,
        sizeof(gate->accepted_nonce));
    memcpy(gate->accepted_request_digest, digest,
        sizeof(gate->accepted_request_digest));
    gate->accepted_count = 1u;
    san9_p1_secure_zero(digest, sizeof(digest));
    return SAN9_S5_GATE_ACCEPTED;
}

San9S5GateStatus san9_s6_cultivate_apply_once_gate_accept(
    San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status;
    if ((request_status != NULL
            && (ranges_overlap(request_status, sizeof(*request_status), gate,
                    sizeof(*gate))
                || ranges_overlap(request_status, sizeof(*request_status),
                    request, sizeof(*request))
                || ranges_overlap(request_status, sizeof(*request_status),
                    hmac_key, hmac_key_size)))
        || ranges_overlap(gate, sizeof(*gate), request, sizeof(*request))
        || ranges_overlap(gate, sizeof(*gate), hmac_key, hmac_key_size)) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    if (request_status != NULL) {
        *request_status = SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    if (gate == NULL || request == NULL) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    if (!gate_shape_valid(gate)) {
        gate->clock_faulted = 1u;
        return SAN9_S5_GATE_CORRUPTED;
    }
    if (gate->clock_faulted != 0u) {
        return SAN9_S5_GATE_CLOCK_FAULTED;
    }
    if (gate->has_observed_time != 0u
        && now_ms < gate->last_observed_now_ms) {
        gate->clock_faulted = 1u;
        return SAN9_S5_GATE_CLOCK_ROLLBACK;
    }
    gate->last_observed_now_ms = now_ms;
    gate->has_observed_time = 1u;
    if (gate->accepted_count != 0u) {
        return SAN9_S5_GATE_REPLAY;
    }
    status = san9_s6_cultivate_apply_once_request_verify(request, hmac_key,
        hmac_key_size, gate->expected_p1_binding_digest, now_ms);
    if (request_status != NULL) {
        *request_status = status;
    }
    if (status != SAN9_S5_REQUEST_VALID) {
        return SAN9_S5_GATE_REQUEST_REJECTED;
    }
    if (!san9_s6_cultivate_apply_once_request_digest(request, digest)) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    memcpy(gate->accepted_nonce, request->request_nonce,
        sizeof(gate->accepted_nonce));
    memcpy(gate->accepted_request_digest, digest,
        sizeof(gate->accepted_request_digest));
    gate->accepted_count = 1u;
    san9_p1_secure_zero(digest, sizeof(digest));
    return SAN9_S5_GATE_ACCEPTED;
}

San9S5GateStatus san9_s6_patrol_apply_once_gate_accept(
    San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status;
    if ((request_status != NULL
            && (ranges_overlap(request_status, sizeof(*request_status), gate,
                    sizeof(*gate))
                || ranges_overlap(request_status, sizeof(*request_status),
                    request, sizeof(*request))
                || ranges_overlap(request_status, sizeof(*request_status),
                    hmac_key, hmac_key_size)))
        || ranges_overlap(gate, sizeof(*gate), request, sizeof(*request))
        || ranges_overlap(gate, sizeof(*gate), hmac_key, hmac_key_size)) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    if (request_status != NULL) {
        *request_status = SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    if (gate == NULL || request == NULL) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    if (!gate_shape_valid(gate)) {
        gate->clock_faulted = 1u;
        return SAN9_S5_GATE_CORRUPTED;
    }
    if (gate->clock_faulted != 0u) {
        return SAN9_S5_GATE_CLOCK_FAULTED;
    }
    if (gate->has_observed_time != 0u
        && now_ms < gate->last_observed_now_ms) {
        gate->clock_faulted = 1u;
        return SAN9_S5_GATE_CLOCK_ROLLBACK;
    }
    gate->last_observed_now_ms = now_ms;
    gate->has_observed_time = 1u;
    if (gate->accepted_count != 0u) {
        return SAN9_S5_GATE_REPLAY;
    }
    status = san9_s6_patrol_apply_once_request_verify(request, hmac_key,
        hmac_key_size, gate->expected_p1_binding_digest, now_ms);
    if (request_status != NULL) {
        *request_status = status;
    }
    if (status != SAN9_S5_REQUEST_VALID) {
        return SAN9_S5_GATE_REQUEST_REJECTED;
    }
    if (!san9_s6_patrol_apply_once_request_digest(request, digest)) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    memcpy(gate->accepted_nonce, request->request_nonce,
        sizeof(gate->accepted_nonce));
    memcpy(gate->accepted_request_digest, digest,
        sizeof(gate->accepted_request_digest));
    gate->accepted_count = 1u;
    san9_p1_secure_zero(digest, sizeof(digest));
    return SAN9_S5_GATE_ACCEPTED;
}

San9S5GateStatus san9_s6_train_apply_once_gate_accept(
    San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status;
    if ((request_status != NULL
            && (ranges_overlap(request_status, sizeof(*request_status), gate,
                    sizeof(*gate))
                || ranges_overlap(request_status, sizeof(*request_status),
                    request, sizeof(*request))
                || ranges_overlap(request_status, sizeof(*request_status),
                    hmac_key, hmac_key_size)))
        || ranges_overlap(gate, sizeof(*gate), request, sizeof(*request))
        || ranges_overlap(gate, sizeof(*gate), hmac_key, hmac_key_size)) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    if (request_status != NULL) {
        *request_status = SAN9_S5_REQUEST_INVALID_ARGUMENT;
    }
    if (gate == NULL || request == NULL) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    if (!gate_shape_valid(gate)) {
        gate->clock_faulted = 1u;
        return SAN9_S5_GATE_CORRUPTED;
    }
    if (gate->clock_faulted != 0u) {
        return SAN9_S5_GATE_CLOCK_FAULTED;
    }
    if (gate->has_observed_time != 0u
        && now_ms < gate->last_observed_now_ms) {
        gate->clock_faulted = 1u;
        return SAN9_S5_GATE_CLOCK_ROLLBACK;
    }
    gate->last_observed_now_ms = now_ms;
    gate->has_observed_time = 1u;
    if (gate->accepted_count != 0u) {
        return SAN9_S5_GATE_REPLAY;
    }
    status = san9_s6_train_apply_once_request_verify(request, hmac_key,
        hmac_key_size, gate->expected_p1_binding_digest, now_ms);
    if (request_status != NULL) {
        *request_status = status;
    }
    if (status != SAN9_S5_REQUEST_VALID) {
        return SAN9_S5_GATE_REQUEST_REJECTED;
    }
    if (!san9_s6_train_apply_once_request_digest(request, digest)) {
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
    memcpy(gate->accepted_nonce, request->request_nonce,
        sizeof(gate->accepted_nonce));
    memcpy(gate->accepted_request_digest, digest,
        sizeof(gate->accepted_request_digest));
    gate->accepted_count = 1u;
    san9_p1_secure_zero(digest, sizeof(digest));
    return SAN9_S5_GATE_ACCEPTED;
}

San9S5GateStatus san9_s6_repair_apply_once_gate_accept(
    San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    San9S5RequestStatus status;
    if ((request_status != NULL
            && (ranges_overlap(request_status, sizeof(*request_status), gate,
                    sizeof(*gate))
                || ranges_overlap(request_status, sizeof(*request_status),
                    request, sizeof(*request))
                || ranges_overlap(request_status, sizeof(*request_status),
                    hmac_key, hmac_key_size)))
        || ranges_overlap(gate, sizeof(*gate), request, sizeof(*request))
        || ranges_overlap(gate, sizeof(*gate), hmac_key, hmac_key_size))
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    if (request_status != NULL) *request_status = SAN9_S5_REQUEST_INVALID_ARGUMENT;
    if (gate == NULL || request == NULL) return SAN9_S5_GATE_INVALID_ARGUMENT;
    if (!gate_shape_valid(gate)) { gate->clock_faulted = 1u; return SAN9_S5_GATE_CORRUPTED; }
    if (gate->clock_faulted != 0u) return SAN9_S5_GATE_CLOCK_FAULTED;
    if (gate->has_observed_time != 0u && now_ms < gate->last_observed_now_ms) {
        gate->clock_faulted = 1u; return SAN9_S5_GATE_CLOCK_ROLLBACK;
    }
    gate->last_observed_now_ms = now_ms;
    gate->has_observed_time = 1u;
    if (gate->accepted_count != 0u) return SAN9_S5_GATE_REPLAY;
    status = san9_s6_repair_apply_once_request_verify(request, hmac_key,
        hmac_key_size, gate->expected_p1_binding_digest, now_ms);
    if (request_status != NULL) *request_status = status;
    if (status != SAN9_S5_REQUEST_VALID) return SAN9_S5_GATE_REQUEST_REJECTED;
    if (!san9_s6_repair_apply_once_request_digest(request, digest))
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    memcpy(gate->accepted_nonce, request->request_nonce,
        sizeof(gate->accepted_nonce));
    memcpy(gate->accepted_request_digest, digest,
        sizeof(gate->accepted_request_digest));
    gate->accepted_count = 1u;
    san9_p1_secure_zero(digest, sizeof(digest));
    return SAN9_S5_GATE_ACCEPTED;
}

static int machine_pointer_valid(const San9S5NoApplyMachine *machine)
{
    return machine != NULL
        && (uintptr_t)machine % _Alignof(San9S5NoApplyMachine) == 0u;
}

int san9_s5_no_apply_machine_initialize(San9S5NoApplyMachine *machine)
{
    if (!machine_pointer_valid(machine)) {
        return 0;
    }
    memset(machine->request_nonce, 0, sizeof(machine->request_nonce));
    memset(machine->request_digest, 0, sizeof(machine->request_digest));
    atomic_init(&machine->state, SAN9_S5_STATE_EMPTY);
    atomic_init(&machine->first_fault, SAN9_S5_FAULT_NONE);
    atomic_init(&machine->event_attempt_count, 0u);
    atomic_init(&machine->shadow_arm_count, 0u);
    atomic_init(&machine->execute_enter_count, 0u);
    atomic_init(&machine->restore_count, 0u);
    atomic_init(&machine->observed_apply_count, 0u);
    return atomic_is_lock_free(&machine->state)
        && atomic_is_lock_free(&machine->first_fault)
        && atomic_is_lock_free(&machine->event_attempt_count)
        && atomic_is_lock_free(&machine->shadow_arm_count)
        && atomic_is_lock_free(&machine->execute_enter_count)
        && atomic_is_lock_free(&machine->restore_count)
        && atomic_is_lock_free(&machine->observed_apply_count);
}

static void record_first_fault(
    San9S5NoApplyMachine *machine,
    San9S5FaultCode fault)
{
    uint32_t expected = SAN9_S5_FAULT_NONE;
    (void)atomic_compare_exchange_strong_explicit(&machine->first_fault,
        &expected, (uint32_t)fault, memory_order_acq_rel, memory_order_acquire);
}

static San9S5MachineResult state_result(uint32_t state)
{
    return state == SAN9_S5_STATE_RESTART_REQUIRED
        ? SAN9_S5_MACHINE_RESTART_REQUIRED
        : state == SAN9_S5_STATE_REJECTED_PRE_EVENT
            ? SAN9_S5_MACHINE_REJECTED_PRE_EVENT : SAN9_S5_MACHINE_OK;
}

San9S5MachineResult san9_s5_no_apply_machine_fail(
    San9S5NoApplyMachine *machine,
    San9S5FaultCode fault)
{
    uint32_t observed;
    uint32_t target;
    if (!machine_pointer_valid(machine) || fault <= SAN9_S5_FAULT_NONE
        || fault > SAN9_S5_FAULT_STATE_CORRUPTION) {
        return SAN9_S5_MACHINE_INVALID_ARGUMENT;
    }
    record_first_fault(machine, fault);
    observed = atomic_load_explicit(&machine->state, memory_order_acquire);
    for (;;) {
        if (observed == SAN9_S5_STATE_RESTART_REQUIRED) {
            return SAN9_S5_MACHINE_RESTART_REQUIRED;
        }
        if (observed == SAN9_S5_STATE_REJECTED_PRE_EVENT
            && fault != SAN9_S5_FAULT_APPLY_OBSERVED) {
            return SAN9_S5_MACHINE_REJECTED_PRE_EVENT;
        }
        target = fault == SAN9_S5_FAULT_APPLY_OBSERVED
                || observed >= SAN9_S5_STATE_EVENT_ATTEMPTED
            ? SAN9_S5_STATE_RESTART_REQUIRED
            : SAN9_S5_STATE_REJECTED_PRE_EVENT;
        if (observed > SAN9_S5_STATE_RESTART_REQUIRED) {
            target = SAN9_S5_STATE_RESTART_REQUIRED;
        }
        if (atomic_compare_exchange_weak_explicit(&machine->state, &observed,
                target, memory_order_acq_rel, memory_order_acquire)) {
            return state_result(target);
        }
    }
}

static San9S5MachineResult transition(
    San9S5NoApplyMachine *machine,
    San9S5MachineState expected_state,
    San9S5MachineState desired_state)
{
    uint32_t expected = (uint32_t)expected_state;
    if (!machine_pointer_valid(machine)) {
        return SAN9_S5_MACHINE_INVALID_ARGUMENT;
    }
    if (!atomic_compare_exchange_strong_explicit(&machine->state, &expected,
            (uint32_t)desired_state, memory_order_acq_rel,
            memory_order_acquire)) {
        return san9_s5_no_apply_machine_fail(machine,
            SAN9_S5_FAULT_OUT_OF_ORDER);
    }
    return SAN9_S5_MACHINE_OK;
}

static San9S5MachineResult transition_with_counter(
    San9S5NoApplyMachine *machine,
    San9S5MachineState expected_state,
    San9S5MachineState desired_state,
    _Atomic(uint32_t) *counter)
{
    uint32_t zero = 0u;
    San9S5MachineResult result = transition(machine, expected_state,
        desired_state);
    if (result != SAN9_S5_MACHINE_OK) {
        return result;
    }
    if (!atomic_compare_exchange_strong_explicit(counter, &zero, 1u,
            memory_order_acq_rel, memory_order_acquire)) {
        return san9_s5_no_apply_machine_fail(machine,
            SAN9_S5_FAULT_COUNTER_INVARIANT);
    }
    return SAN9_S5_MACHINE_OK;
}

San9S5MachineResult san9_s5_no_apply_machine_publish(
    San9S5NoApplyMachine *machine,
    const San9S5NoApplyRequest *request)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    uint8_t nonce[SAN9_S5_NONCE_SIZE];
    uint32_t expected = SAN9_S5_STATE_EMPTY;
    San9S5RequestStatus request_status;
    if (!machine_pointer_valid(machine) || request == NULL
        || ranges_overlap(machine, sizeof(*machine), request,
            sizeof(*request))) {
        return SAN9_S5_MACHINE_INVALID_ARGUMENT;
    }
    request_status = supported_request_shape_status(request);
    if (request_status != SAN9_S5_REQUEST_VALID
        || !supported_request_digest(request, digest)) {
        return san9_s5_no_apply_machine_fail(machine,
            SAN9_S5_FAULT_REQUEST_IDENTITY);
    }
    memcpy(nonce, request->request_nonce, sizeof(nonce));
    if (!atomic_compare_exchange_strong_explicit(&machine->state, &expected,
            SAN9_S5_STATE_WRITING, memory_order_acq_rel,
            memory_order_acquire)) {
        san9_p1_secure_zero(digest, sizeof(digest));
        san9_p1_secure_zero(nonce, sizeof(nonce));
        return san9_s5_no_apply_machine_fail(machine,
            SAN9_S5_FAULT_OUT_OF_ORDER);
    }
    memcpy(machine->request_nonce, nonce, sizeof(machine->request_nonce));
    memcpy(machine->request_digest, digest, sizeof(machine->request_digest));
    san9_p1_secure_zero(digest, sizeof(digest));
    san9_p1_secure_zero(nonce, sizeof(nonce));
    return transition(machine, SAN9_S5_STATE_WRITING, SAN9_S5_STATE_SEALED);
}

San9S5MachineResult san9_s5_no_apply_machine_claim_authenticated(
    San9S5NoApplyMachine *machine,
    const San9S5NoApplyGate *gate,
    const San9S5NoApplyRequest *request)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    int matches;
    if (!machine_pointer_valid(machine) || gate == NULL || request == NULL
        || ranges_overlap(machine, sizeof(*machine), request,
            sizeof(*request))
        || ranges_overlap(machine, sizeof(*machine), gate, sizeof(*gate))) {
        return SAN9_S5_MACHINE_INVALID_ARGUMENT;
    }
    if (!gate_shape_valid(gate) || gate->clock_faulted != 0u
        || gate->accepted_count != 1u
        || !supported_request_digest(request, digest)) {
        return san9_s5_no_apply_machine_fail(machine,
            SAN9_S5_FAULT_REQUEST_IDENTITY);
    }
    matches = san9_p1_constant_time_equal(gate->accepted_nonce,
            request->request_nonce, SAN9_S5_NONCE_SIZE)
        && san9_p1_constant_time_equal(gate->accepted_request_digest,
            digest, SAN9_S5_DIGEST_SIZE)
        && san9_p1_constant_time_equal(machine->request_nonce,
            request->request_nonce, SAN9_S5_NONCE_SIZE)
        && san9_p1_constant_time_equal(machine->request_digest,
            digest, SAN9_S5_DIGEST_SIZE);
    san9_p1_secure_zero(digest, sizeof(digest));
    if (!matches) {
        return san9_s5_no_apply_machine_fail(machine,
            SAN9_S5_FAULT_REQUEST_IDENTITY);
    }
    return transition(machine, SAN9_S5_STATE_SEALED, SAN9_S5_STATE_CLAIMED);
}

San9S5MachineResult san9_s5_no_apply_machine_mark_prechecked(
    San9S5NoApplyMachine *machine)
{
    return transition(machine, SAN9_S5_STATE_CLAIMED,
        SAN9_S5_STATE_PRECHECKED);
}

San9S5MachineResult san9_s5_no_apply_machine_mark_event_attempted(
    San9S5NoApplyMachine *machine)
{
    return transition_with_counter(machine, SAN9_S5_STATE_PRECHECKED,
        SAN9_S5_STATE_EVENT_ATTEMPTED, &machine->event_attempt_count);
}

San9S5MachineResult san9_s5_no_apply_machine_mark_shadow_armed(
    San9S5NoApplyMachine *machine)
{
    return transition_with_counter(machine, SAN9_S5_STATE_EVENT_ATTEMPTED,
        SAN9_S5_STATE_SHADOW_ARMED, &machine->shadow_arm_count);
}

San9S5MachineResult san9_s5_no_apply_machine_mark_execute_entered(
    San9S5NoApplyMachine *machine)
{
    return transition_with_counter(machine, SAN9_S5_STATE_SHADOW_ARMED,
        SAN9_S5_STATE_EXECUTE_ENTERED, &machine->execute_enter_count);
}

San9S5MachineResult san9_s5_no_apply_machine_mark_vptr_restored(
    San9S5NoApplyMachine *machine)
{
    return transition_with_counter(machine, SAN9_S5_STATE_EXECUTE_ENTERED,
        SAN9_S5_STATE_VPTR_RESTORED, &machine->restore_count);
}

San9S5MachineResult san9_s5_no_apply_machine_prove_no_apply(
    San9S5NoApplyMachine *machine)
{
    if (!machine_pointer_valid(machine)) {
        return SAN9_S5_MACHINE_INVALID_ARGUMENT;
    }
    if (atomic_load_explicit(&machine->state, memory_order_acquire)
            != SAN9_S5_STATE_VPTR_RESTORED) {
        return san9_s5_no_apply_machine_fail(machine,
            SAN9_S5_FAULT_OUT_OF_ORDER);
    }
    if (atomic_load_explicit(&machine->event_attempt_count,
            memory_order_acquire) != 1u
        || atomic_load_explicit(&machine->shadow_arm_count,
            memory_order_acquire) != 1u
        || atomic_load_explicit(&machine->execute_enter_count,
            memory_order_acquire) != 1u
        || atomic_load_explicit(&machine->restore_count,
            memory_order_acquire) != 1u
        || atomic_load_explicit(&machine->observed_apply_count,
            memory_order_acquire) != 0u) {
        return san9_s5_no_apply_machine_fail(machine,
            SAN9_S5_FAULT_COUNTER_INVARIANT);
    }
    return transition(machine, SAN9_S5_STATE_VPTR_RESTORED,
        SAN9_S5_STATE_NO_APPLY_PROVEN);
}

San9S5MachineResult san9_s5_no_apply_machine_note_apply_observed(
    San9S5NoApplyMachine *machine)
{
    uint32_t observed;
    if (!machine_pointer_valid(machine)) {
        return SAN9_S5_MACHINE_INVALID_ARGUMENT;
    }
    observed = atomic_load_explicit(&machine->observed_apply_count,
        memory_order_acquire);
    while (observed != UINT32_MAX
        && !atomic_compare_exchange_weak_explicit(
            &machine->observed_apply_count, &observed, observed + 1u,
            memory_order_acq_rel, memory_order_acquire)) {
    }
    return san9_s5_no_apply_machine_fail(machine,
        SAN9_S5_FAULT_APPLY_OBSERVED);
}

San9S5MachineState san9_s5_no_apply_machine_state(
    const San9S5NoApplyMachine *machine)
{
    uint32_t state;
    if (!machine_pointer_valid(machine)) {
        return SAN9_S5_STATE_RESTART_REQUIRED;
    }
    state = atomic_load_explicit(&machine->state, memory_order_acquire);
    return state <= SAN9_S5_STATE_RESTART_REQUIRED
        ? (San9S5MachineState)state : SAN9_S5_STATE_RESTART_REQUIRED;
}

San9S5FaultCode san9_s5_no_apply_machine_first_fault(
    const San9S5NoApplyMachine *machine)
{
    uint32_t fault;
    if (!machine_pointer_valid(machine)) {
        return SAN9_S5_FAULT_STATE_CORRUPTION;
    }
    fault = atomic_load_explicit(&machine->first_fault, memory_order_acquire);
    return fault <= SAN9_S5_FAULT_STATE_CORRUPTION
        ? (San9S5FaultCode)fault : SAN9_S5_FAULT_STATE_CORRUPTION;
}
