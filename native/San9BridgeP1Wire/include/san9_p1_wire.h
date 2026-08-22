#ifndef SAN9_P1_WIRE_H
#define SAN9_P1_WIRE_H

#include <stddef.h>
#include <stdint.h>

#define SAN9_P1_MAGIC 0x31503953U
#define SAN9_P1_SCHEMA_MAJOR 1U
#define SAN9_P1_SCHEMA_MINOR 0U
#define SAN9_P1_FRAME_SIZE 512U
#define SAN9_P1_DIGEST_SIZE 32U
#define SAN9_P1_NONCE_SIZE 16U
#define SAN9_P1_HMAC_KEY_SIZE 32U
#define SAN9_P1_MAXIMUM_LIFETIME_MS 5000ULL
#define SAN9_P1_MAXIMUM_SESSION_PINGS 100U
#define SAN9_P1_LIVE_AUTHORIZATION 0
#define SAN9_P1_CONTAINS_BUSINESS_FIELDS 0
#define SAN9_P1_CONTAINS_NATIVE_ADDRESSES 0

#define SAN9_P1_MAGIC_OFFSET 0U
#define SAN9_P1_SCHEMA_MAJOR_OFFSET 4U
#define SAN9_P1_SCHEMA_MINOR_OFFSET 6U
#define SAN9_P1_DECLARED_SIZE_OFFSET 8U
#define SAN9_P1_KIND_OFFSET 12U
#define SAN9_P1_STATE_OFFSET 14U
#define SAN9_P1_FLAGS_OFFSET 16U
#define SAN9_P1_CRC32_OFFSET 20U
#define SAN9_P1_RESULT_CODE_OFFSET 24U
#define SAN9_P1_RESERVED_HEADER_OFFSET 28U
#define SAN9_P1_RESERVED_HEADER_SIZE 4U
#define SAN9_P1_SEQUENCE_OFFSET 32U
#define SAN9_P1_ISSUED_AT_MS_OFFSET 40U
#define SAN9_P1_EXPIRES_AT_MS_OFFSET 48U
#define SAN9_P1_GAME_PID_OFFSET 56U
#define SAN9_P1_MAIN_TID_OFFSET 60U
#define SAN9_P1_GAME_HWND_OFFSET 64U
#define SAN9_P1_HELPER_PID_OFFSET 68U
#define SAN9_P1_EASY_LOADER_PID_OFFSET 72U
#define SAN9_P1_RESERVED_BINDING_OFFSET 76U
#define SAN9_P1_RESERVED_BINDING_SIZE 4U
#define SAN9_P1_GAME_GENERATION_OFFSET 80U
#define SAN9_P1_HELPER_GENERATION_OFFSET 88U
#define SAN9_P1_EASY_LOADER_GENERATION_OFFSET 96U
#define SAN9_P1_SESSION_NONCE_OFFSET 104U
#define SAN9_P1_REQUEST_ID_OFFSET 120U
#define SAN9_P1_EASY_EPOCH_NONCE_OFFSET 136U
#define SAN9_P1_BUILD_DIGEST_OFFSET 152U
#define SAN9_P1_PROFILE_DIGEST_OFFSET 184U
#define SAN9_P1_MANIFEST_DIGEST_OFFSET 216U
#define SAN9_P1_EASY_EPOCH_DIGEST_OFFSET 248U
#define SAN9_P1_EASY_TICKET_DIGEST_OFFSET 280U
#define SAN9_P1_CONTEXT_DIGEST_OFFSET 312U
#define SAN9_P1_BRIDGE_DIGEST_OFFSET 344U
#define SAN9_P1_MAPPING_DIGEST_OFFSET 376U
#define SAN9_P1_CHALLENGE_DIGEST_OFFSET 408U
#define SAN9_P1_RESULT_DIGEST_OFFSET 440U
#define SAN9_P1_HMAC_OFFSET 472U
#define SAN9_P1_RESERVED_TAIL_OFFSET 504U
#define SAN9_P1_RESERVED_TAIL_SIZE 8U

typedef enum San9P1WireKind {
    SAN9_P1_PING_REQUEST = 1,
    SAN9_P1_PING_RESPONSE = 2
} San9P1WireKind;

typedef enum San9P1WireState {
    SAN9_P1_PENDING = 1,
    SAN9_P1_COMPLETED = 2,
    SAN9_P1_REJECTED = 3
} San9P1WireState;

typedef enum San9P1DecodeStatus {
    SAN9_P1_DECODE_ACCEPTED = 0,
    SAN9_P1_DECODE_NULL_FRAME = 1,
    SAN9_P1_DECODE_SIZE_MISMATCH = 2,
    SAN9_P1_DECODE_MAGIC_MISMATCH = 3,
    SAN9_P1_DECODE_SCHEMA_MISMATCH = 4,
    SAN9_P1_DECODE_DECLARED_SIZE_MISMATCH = 5,
    SAN9_P1_DECODE_CRC_MISMATCH = 6,
    SAN9_P1_DECODE_HMAC_MISMATCH = 7,
    SAN9_P1_DECODE_RESERVED_OR_FLAGS_INVALID = 8,
    SAN9_P1_DECODE_KIND_STATE_INVALID = 9,
    SAN9_P1_DECODE_REQUIRED_FIELD_INVALID = 10,
    SAN9_P1_DECODE_LIFETIME_INVALID = 11,
    SAN9_P1_DECODE_REQUEST_SHAPE_INVALID = 12,
    SAN9_P1_DECODE_RESPONSE_SHAPE_INVALID = 13,
    SAN9_P1_DECODE_KEY_INVALID = 14
} San9P1DecodeStatus;

typedef enum San9P1GateStatus {
    SAN9_P1_GATE_ACCEPTED = 0,
    SAN9_P1_GATE_WRONG_SHAPE = 1,
    SAN9_P1_GATE_BINDING_MISMATCH = 2,
    SAN9_P1_GATE_REPLAY = 3,
    SAN9_P1_GATE_SEQUENCE_GAP = 4,
    SAN9_P1_GATE_REQUEST_ID_REPLAY = 5,
    SAN9_P1_GATE_EXPIRED = 6,
    SAN9_P1_GATE_FROM_FUTURE = 7,
    SAN9_P1_GATE_BUDGET_EXHAUSTED = 8,
    SAN9_P1_GATE_CLOCK_ROLLBACK = 9,
    SAN9_P1_GATE_CLOCK_FAULTED = 10,
    SAN9_P1_GATE_AUTHENTICATED_FRAME_REJECTED = 11
} San9P1GateStatus;

typedef struct San9P1Frame {
    uint16_t kind;
    uint16_t state;
    uint32_t flags;
    uint32_t result_code;
    uint64_t sequence;
    uint64_t issued_at_ms;
    uint64_t expires_at_ms;
    uint32_t game_pid;
    uint32_t main_tid;
    uint32_t game_hwnd;
    uint32_t helper_pid;
    uint32_t easy_loader_pid;
    uint64_t game_generation;
    uint64_t helper_generation;
    uint64_t easy_loader_generation;
    uint8_t session_nonce[SAN9_P1_NONCE_SIZE];
    uint8_t request_id[SAN9_P1_NONCE_SIZE];
    uint8_t easy_epoch_nonce[SAN9_P1_NONCE_SIZE];
    uint8_t build_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t profile_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t manifest_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t easy_epoch_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t easy_ticket_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t context_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t bridge_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t mapping_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t challenge_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t result_digest[SAN9_P1_DIGEST_SIZE];
} San9P1Frame;

typedef struct San9P1RequestGate {
    San9P1Frame binding;
    uint8_t accepted_request_ids[SAN9_P1_MAXIMUM_SESSION_PINGS][SAN9_P1_NONCE_SIZE];
    uint64_t last_sequence;
    uint64_t last_observed_now;
    uint32_t accepted_count;
    uint8_t has_observed_now;
    uint8_t clock_faulted;
} San9P1RequestGate;

int san9_p1_encode(
    const San9P1Frame *frame,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint8_t *output,
    size_t output_size);

San9P1DecodeStatus san9_p1_decode(
    const uint8_t *bytes,
    size_t size,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    San9P1Frame *frame);

int san9_p1_constant_time_equal(const uint8_t *left, const uint8_t *right, size_t size);

int san9_p1_request_gate_initialize(San9P1RequestGate *gate, const San9P1Frame *binding);

San9P1GateStatus san9_p1_decode_and_accept(
    San9P1RequestGate *gate,
    const uint8_t *bytes,
    size_t size,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    uint64_t now_ms,
    San9P1DecodeStatus *decode_status,
    San9P1Frame *accepted_request);

int san9_p1_decode_and_verify_response(
    const San9P1Frame *request,
    const uint8_t *bytes,
    size_t size,
    const uint8_t *hmac_key,
    size_t hmac_key_size,
    San9P1DecodeStatus *decode_status,
    San9P1Frame *verified_response);

#endif
