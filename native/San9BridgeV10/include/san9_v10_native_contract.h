#ifndef SAN9_V10_NATIVE_CONTRACT_H
#define SAN9_V10_NATIVE_CONTRACT_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32) && !defined(SAN9_V10_NO_EXPORTS)
#define SAN9_V10_EXPORT __declspec(dllexport)
#else
#define SAN9_V10_EXPORT
#endif

#define SAN9_V10_CONTRACT_VERSION 1U
#define SAN9_V10_REQUEST_PROTOCOL_VERSION 2U
#define SAN9_V10_REQUIRED_OFFICERS 5U
#define SAN9_V10_DIGEST_SIZE 32U
#define SAN9_V10_REQUEST_ID_SIZE 16U

#define SAN9_V10_EXACT_IMAGE_BASE 0x00400000U
#define SAN9_V10_EXACT_IMAGE_SIZE 0x01759000U
#define SAN9_V10_ROOT_VPTR 0x00610BC8U
#define SAN9_V10_SELECTOR_VPTR 0x0061F0B8U
#define SAN9_V10_CITY_VPTR 0x00605938U
#define SAN9_V10_ROOT_TARGET_OFFSET 0x38U

typedef enum san9_v10_command {
    SAN9_V10_COMMAND_INVALID = 0,
    SAN9_V10_COMMAND_PATROL = 1,
    SAN9_V10_COMMAND_COMMERCE = 2,
    SAN9_V10_COMMAND_CULTIVATE = 3,
    SAN9_V10_COMMAND_TRAIN = 4,
    SAN9_V10_COMMAND_REPAIR = 5
} san9_v10_command;

typedef enum san9_v10_stage {
    SAN9_V10_STAGE_INVALID = 0,
    SAN9_V10_STAGE_BIND_TARGET = 2,
    SAN9_V10_STAGE_OPEN_OUTER = 3,
    SAN9_V10_STAGE_OPEN_SELECTOR = 4,
    SAN9_V10_STAGE_CLEAR = 5,
    SAN9_V10_STAGE_NATIVE_FILL_MAX = 6,
    SAN9_V10_STAGE_VERIFY_EXACTLY_FIVE = 7,
    SAN9_V10_STAGE_ACCEPT_INNER = 8,
    SAN9_V10_STAGE_VERIFY_WORKING = 9,
    SAN9_V10_STAGE_ACCEPT_OUTER = 10,
    SAN9_V10_STAGE_VERIFY_COMMITTED = 11
} san9_v10_stage;

typedef enum san9_v10_route_kind {
    SAN9_V10_ROUTE_NONE = 0,
    SAN9_V10_ROUTE_CONDITIONAL_ROOT_TARGET_CAS = 1,
    SAN9_V10_ROUTE_ROOT_EVENT = 2,
    SAN9_V10_ROUTE_OUTER_TASK_EVENT = 3,
    SAN9_V10_ROUTE_SELECTOR_EVENT = 4,
    SAN9_V10_ROUTE_READ_ONLY_VERIFICATION = 5
} san9_v10_route_kind;

/* Numeric values deliberately match V8's public AbortCleanupKind. */
typedef enum san9_v10_abort_cleanup_kind {
    SAN9_V10_CLEANUP_INVALID = 0,
    SAN9_V10_CLEANUP_NO_MUTATION = 1,
    SAN9_V10_CLEANUP_CONDITIONAL_RESTORE_TARGET = 2,
    SAN9_V10_CLEANUP_CANCEL_SELECTOR = 3,
    SAN9_V10_CLEANUP_CANCEL_OUTER = 4,
    SAN9_V10_CLEANUP_DO_NOT_ROLLBACK_AFTER_ACCEPT = 5,
    SAN9_V10_CLEANUP_HALT_RESTART = 6
} san9_v10_abort_cleanup_kind;

typedef enum san9_v10_validation_result {
    SAN9_V10_VALID = 0,
    SAN9_V10_INVALID_NULL = 1,
    SAN9_V10_INVALID_STRUCT_SIZE = 2,
    SAN9_V10_INVALID_PROTOCOL = 3,
    SAN9_V10_INVALID_COMMAND = 4,
    SAN9_V10_INVALID_CITY = 5,
    SAN9_V10_INVALID_CORPS = 6,
    SAN9_V10_INVALID_OFFICER = 7,
    SAN9_V10_DUPLICATE_OFFICER = 8,
    SAN9_V10_INVALID_SEQUENCE = 9,
    SAN9_V10_INVALID_TTL = 10,
    SAN9_V10_INVALID_DIGEST = 11,
    SAN9_V10_INVALID_REQUEST_ID = 12,
    SAN9_V10_INVALID_RESERVE_MONEY = 13,
    SAN9_V10_INVALID_FINGERPRINT = 14
} san9_v10_validation_result;

typedef struct san9_v10_command_descriptor {
    uint32_t struct_size;
    uint32_t command;
    char key[16];
    uint32_t native_command_id;
    uint32_t outer_task_type;
    char outer_task_type_key[32];
    uint32_t money_model;
    uint32_t required_officer_count;
    uint32_t expected_cost;
    uint32_t outer_task_vptr;
    uint32_t outer_event_entry;
    uint32_t selector_callsite;
    uint32_t handler_vptr;
    uint32_t handler_can_execute;
    uint32_t handler_execute_ui;
    uint32_t factory_case;
    uint32_t handler_target_offset;
} san9_v10_command_descriptor;

typedef struct san9_v10_stage_route {
    uint32_t struct_size;
    uint32_t command;
    uint32_t stage;
    uint32_t route_kind;
    uint32_t static_entry_address;
    uint32_t event_id;
    uint32_t expected_object_vptr;
    uint32_t static_route_confirmed;
    uint32_t dynamic_lifecycle_confirmed;
    uint32_t live_authorized;
} san9_v10_stage_route;

typedef struct san9_v10_cleanup_route {
    uint32_t struct_size;
    uint32_t kind;
    uint32_t route_kind;
    uint32_t static_candidate_known;
    uint32_t dynamic_lifecycle_confirmed;
    uint32_t live_authorized;
} san9_v10_cleanup_route;

typedef struct san9_v10_single_command_request {
    uint32_t struct_size;
    uint32_t protocol_version;
    /* Exact bytes returned by .NET Guid.ToByteArray(), not textual UUID order. */
    uint8_t request_id[SAN9_V10_REQUEST_ID_SIZE];
    uint64_t sequence;
    uint32_t command;
    uint32_t city_id;
    uint32_t corps_id;
    uint32_t officer_count;
    uint32_t officer_ids[SAN9_V10_REQUIRED_OFFICERS];
    uint32_t reserve_money;
    uint32_t time_to_live_ms;
    uint8_t context_digest[SAN9_V10_DIGEST_SIZE];
    uint8_t generation_digest[SAN9_V10_DIGEST_SIZE];
    uint8_t request_fingerprint[SAN9_V10_DIGEST_SIZE];
} san9_v10_single_command_request;

/*
 * This is a frozen x86 in-process contract shape, not an IPC wire encoding.
 * On the target toolchain sizeof(request)==176 and the key offsets are:
 * sequence=24, officer_ids=48, context_digest=76, request_fingerprint=140.
 * Any future IPC codec must encode fields explicitly in little-endian order
 * and must not memcpy this compiler-owned structure across a trust boundary.
 */

typedef struct san9_v10_contract {
    uint32_t struct_size;
    uint32_t contract_version;
    uint32_t request_protocol_version;
    uint32_t exact_image_base;
    uint32_t exact_image_size;
    uint32_t command_count;
    uint32_t action_stage_count;
    uint32_t execution_code_compiled;
    uint32_t live_authorized;
    uint32_t target_binding_dynamic_proven;
    uint32_t nested_modal_dynamic_proven;
    uint32_t native_cleanup_dynamic_proven;
    uint32_t process_access_code_compiled;
    uint32_t write_or_injection_code_compiled;
    uint8_t exact_exe_sha256[SAN9_V10_DIGEST_SIZE];
} san9_v10_contract;

SAN9_V10_EXPORT const san9_v10_contract *San9BridgeV10_GetContract(void);
SAN9_V10_EXPORT const san9_v10_command_descriptor *
San9BridgeV10_GetCommandDescriptor(uint32_t command);
SAN9_V10_EXPORT int San9BridgeV10_GetStageRoute(
    uint32_t command,
    uint32_t stage,
    san9_v10_stage_route *route);
SAN9_V10_EXPORT int San9BridgeV10_GetCleanupRoute(
    uint32_t kind,
    san9_v10_cleanup_route *route);
SAN9_V10_EXPORT san9_v10_validation_result San9BridgeV10_ValidateRequest(
    const san9_v10_single_command_request *request);
SAN9_V10_EXPORT int San9BridgeV10_ComputeRequestFingerprint(
    const san9_v10_single_command_request *request,
    uint8_t output[SAN9_V10_DIGEST_SIZE]);
SAN9_V10_EXPORT int San9BridgeV10_OfflineSelfTest(void);

#endif
