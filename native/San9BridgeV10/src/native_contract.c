#include "san9_v10_native_contract.h"
#include "sha256.h"

#include <string.h>

_Static_assert(sizeof(san9_v10_command_descriptor) == 108U,
    "V10 x86 descriptor ABI drifted");
_Static_assert(sizeof(san9_v10_stage_route) == 40U,
    "V10 x86 stage-route ABI drifted");
_Static_assert(sizeof(san9_v10_cleanup_route) == 24U,
    "V10 x86 cleanup-route ABI drifted");
_Static_assert(sizeof(san9_v10_single_command_request) == 176U,
    "V10 x86 request ABI drifted");
_Static_assert(offsetof(san9_v10_single_command_request, sequence) == 24U,
    "V10 request sequence offset drifted");
_Static_assert(offsetof(san9_v10_single_command_request, officer_ids) == 48U,
    "V10 request officer offset drifted");
_Static_assert(offsetof(san9_v10_single_command_request, context_digest) == 76U,
    "V10 request context offset drifted");
_Static_assert(offsetof(san9_v10_single_command_request, request_fingerprint) == 140U,
    "V10 request fingerprint offset drifted");
_Static_assert(sizeof(san9_v10_contract) == 88U,
    "V10 x86 contract ABI drifted");

static const uint8_t EXACT_EXE_SHA256[SAN9_V10_DIGEST_SIZE] = {
    0xD2, 0x07, 0x94, 0xAE, 0xFF, 0x67, 0x30, 0x1E,
    0xC2, 0xBF, 0x8C, 0x3B, 0xEC, 0xB1, 0xE9, 0x94,
    0x4C, 0x68, 0xC6, 0xC0, 0x58, 0x8F, 0xBF, 0xD4,
    0xBF, 0x04, 0xE8, 0x59, 0x7F, 0x0E, 0x50, 0x28
};

static const san9_v10_command_descriptor DESCRIPTORS[] = {
    {
        .struct_size = sizeof(san9_v10_command_descriptor),
        .command = SAN9_V10_COMMAND_PATROL,
        .key = "patrol",
        .native_command_id = 0U,
        .outer_task_type = SAN9_V10_COMMAND_PATROL,
        .outer_task_type_key = "outer-task-patrol",
        .money_model = 1U,
        .required_officer_count = SAN9_V10_REQUIRED_OFFICERS,
        .expected_cost = 250U,
        .outer_task_vptr = 0x0060B920U,
        .outer_event_entry = 0x004D8BA0U,
        .selector_callsite = 0x004D8C1AU,
        .handler_vptr = 0x00609B90U,
        .handler_can_execute = 0x004C1930U,
        .handler_execute_ui = 0x004C1840U,
        .factory_case = 0x005128B8U,
        .handler_target_offset = 0x60U
    },
    {
        .struct_size = sizeof(san9_v10_command_descriptor),
        .command = SAN9_V10_COMMAND_COMMERCE,
        .key = "commerce",
        .native_command_id = 1U,
        .outer_task_type = SAN9_V10_COMMAND_COMMERCE,
        .outer_task_type_key = "outer-task-commerce",
        .money_model = 1U,
        .required_officer_count = SAN9_V10_REQUIRED_OFFICERS,
        .expected_cost = 250U,
        .outer_task_vptr = 0x0060CCB0U,
        .outer_event_entry = 0x004E72D0U,
        .selector_callsite = 0x004E734AU,
        .handler_vptr = 0x00609F38U,
        .handler_can_execute = 0x004C6400U,
        .handler_execute_ui = 0x004C6310U,
        .factory_case = 0x00512900U,
        .handler_target_offset = 0x60U
    },
    {
        .struct_size = sizeof(san9_v10_command_descriptor),
        .command = SAN9_V10_COMMAND_CULTIVATE,
        .key = "cultivate",
        .native_command_id = 2U,
        .outer_task_type = SAN9_V10_COMMAND_CULTIVATE,
        .outer_task_type_key = "outer-task-cultivate",
        .money_model = 1U,
        .required_officer_count = SAN9_V10_REQUIRED_OFFICERS,
        .expected_cost = 250U,
        .outer_task_vptr = 0x0060BBA0U,
        .outer_event_entry = 0x004DA670U,
        .selector_callsite = 0x004DA6EAU,
        .handler_vptr = 0x00609BF0U,
        .handler_can_execute = 0x004C1F50U,
        .handler_execute_ui = 0x004C1E60U,
        .factory_case = 0x00512948U,
        .handler_target_offset = 0x60U
    },
    {
        .struct_size = sizeof(san9_v10_command_descriptor),
        .command = SAN9_V10_COMMAND_TRAIN,
        .key = "train",
        .native_command_id = 5U,
        .outer_task_type = SAN9_V10_COMMAND_TRAIN,
        .outer_task_type_key = "outer-task-train",
        .money_model = 2U,
        .required_officer_count = SAN9_V10_REQUIRED_OFFICERS,
        .expected_cost = 0U,
        .outer_task_vptr = 0x0060C370U,
        .outer_event_entry = 0x004DF8E0U,
        .selector_callsite = 0x004DF8C1U,
        .handler_vptr = 0x00609CE0U,
        .handler_can_execute = 0x004C3890U,
        .handler_execute_ui = 0x004C37A0U,
        .factory_case = 0x00512A20U,
        .handler_target_offset = 0x60U
    },
    {
        .struct_size = sizeof(san9_v10_command_descriptor),
        .command = SAN9_V10_COMMAND_REPAIR,
        .key = "repair",
        .native_command_id = 3U,
        .outer_task_type = SAN9_V10_COMMAND_REPAIR,
        .outer_task_type_key = "outer-task-repair",
        .money_model = 1U,
        .required_officer_count = SAN9_V10_REQUIRED_OFFICERS,
        .expected_cost = 250U,
        .outer_task_vptr = 0x0060BCE8U,
        .outer_event_entry = 0x004DB170U,
        .selector_callsite = 0x004DB1EAU,
        .handler_vptr = 0x00609C20U,
        .handler_can_execute = 0x004C2270U,
        .handler_execute_ui = 0x004C2180U,
        .factory_case = 0x00512990U,
        .handler_target_offset = 0x60U
    }
};

static const san9_v10_contract CONTRACT = {
    sizeof(san9_v10_contract),
    SAN9_V10_CONTRACT_VERSION,
    SAN9_V10_REQUEST_PROTOCOL_VERSION,
    SAN9_V10_EXACT_IMAGE_BASE,
    SAN9_V10_EXACT_IMAGE_SIZE,
    5U,
    7U,
    0U,
    0U,
    0U,
    0U,
    0U,
    0U,
    0U,
    {
        0xD2, 0x07, 0x94, 0xAE, 0xFF, 0x67, 0x30, 0x1E,
        0xC2, 0xBF, 0x8C, 0x3B, 0xEC, 0xB1, 0xE9, 0x94,
        0x4C, 0x68, 0xC6, 0xC0, 0x58, 0x8F, 0xBF, 0xD4,
        0xBF, 0x04, 0xE8, 0x59, 0x7F, 0x0E, 0x50, 0x28
    }
};

static int bytes_have_nonzero(const uint8_t *value, size_t length)
{
    size_t index;
    uint8_t combined = 0U;
    for (index = 0U; index < length; ++index) {
        combined = (uint8_t)(combined | value[index]);
    }
    return combined != 0U;
}

static const san9_v10_command_descriptor *find_descriptor(uint32_t command)
{
    size_t index;
    for (index = 0U; index < sizeof(DESCRIPTORS) / sizeof(DESCRIPTORS[0]); ++index) {
        if (DESCRIPTORS[index].command == command) {
            return &DESCRIPTORS[index];
        }
    }
    return NULL;
}

static void hash_u32_le(San9Sha256Context *context, uint32_t value)
{
    uint8_t bytes[4];
    bytes[0] = (uint8_t)value;
    bytes[1] = (uint8_t)(value >> 8);
    bytes[2] = (uint8_t)(value >> 16);
    bytes[3] = (uint8_t)(value >> 24);
    san9_sha256_update(context, bytes, sizeof(bytes));
}

static void hash_u64_le(San9Sha256Context *context, uint64_t value)
{
    uint8_t bytes[8];
    size_t index;
    for (index = 0U; index < sizeof(bytes); ++index) {
        bytes[index] = (uint8_t)(value >> (index * 8U));
    }
    san9_sha256_update(context, bytes, sizeof(bytes));
}

static int hash_binary_writer_ascii_string(
    San9Sha256Context *context,
    const char *value)
{
    size_t length;
    uint8_t encoded_length;
    if (context == NULL || value == NULL) {
        return 0;
    }
    length = strlen(value);
    if (length > 0x7FU) {
        return 0;
    }
    encoded_length = (uint8_t)length;
    san9_sha256_update(context, &encoded_length, 1U);
    san9_sha256_update(context, (const uint8_t *)value, length);
    return 1;
}

int San9BridgeV10_ComputeRequestFingerprint(
    const san9_v10_single_command_request *request,
    uint8_t output[SAN9_V10_DIGEST_SIZE])
{
    const san9_v10_command_descriptor *descriptor;
    San9Sha256Context context;
    uint32_t index;
    if (request == NULL || output == NULL) {
        return 0;
    }
    descriptor = find_descriptor(request->command);
    if (descriptor == NULL) {
        return 0;
    }

    san9_sha256_initialize(&context);
    hash_u32_le(&context, request->protocol_version);
    san9_sha256_update(&context, request->request_id, SAN9_V10_REQUEST_ID_SIZE);
    hash_u64_le(&context, request->sequence);
    hash_u32_le(&context, descriptor->command);
    if (!hash_binary_writer_ascii_string(&context, descriptor->key)) {
        return 0;
    }
    hash_u32_le(&context, descriptor->native_command_id);
    hash_u32_le(&context, descriptor->outer_task_type);
    if (!hash_binary_writer_ascii_string(&context, descriptor->outer_task_type_key)) {
        return 0;
    }
    hash_u32_le(&context, descriptor->outer_task_vptr);
    hash_u32_le(&context, descriptor->money_model);
    hash_u32_le(&context, descriptor->required_officer_count);
    hash_u32_le(&context, descriptor->expected_cost);
    hash_u32_le(&context, request->city_id);
    hash_u32_le(&context, request->corps_id);
    hash_u32_le(&context, request->officer_count);
    for (index = 0U; index < SAN9_V10_REQUIRED_OFFICERS; ++index) {
        hash_u32_le(&context, request->officer_ids[index]);
    }
    hash_u32_le(&context, request->reserve_money);
    san9_sha256_update(&context, request->context_digest, SAN9_V10_DIGEST_SIZE);
    san9_sha256_update(&context, request->generation_digest, SAN9_V10_DIGEST_SIZE);
    hash_u32_le(&context, request->time_to_live_ms);
    san9_sha256_finish(&context, output);
    return 1;
}

const san9_v10_contract *San9BridgeV10_GetContract(void)
{
    return &CONTRACT;
}

const san9_v10_command_descriptor *San9BridgeV10_GetCommandDescriptor(
    uint32_t command)
{
    return find_descriptor(command);
}

int San9BridgeV10_GetStageRoute(
    uint32_t command,
    uint32_t stage,
    san9_v10_stage_route *route)
{
    const san9_v10_command_descriptor *descriptor = find_descriptor(command);
    if (descriptor == NULL || route == NULL) {
        return 0;
    }

    memset(route, 0, sizeof(*route));
    route->struct_size = sizeof(*route);
    route->command = command;
    route->stage = stage;
    route->live_authorized = 0U;
    route->dynamic_lifecycle_confirmed = 0U;

    switch (stage) {
        case SAN9_V10_STAGE_BIND_TARGET:
            route->route_kind = SAN9_V10_ROUTE_CONDITIONAL_ROOT_TARGET_CAS;
            route->expected_object_vptr = SAN9_V10_ROOT_VPTR;
            route->static_route_confirmed = 0U;
            return 1;
        case SAN9_V10_STAGE_OPEN_OUTER:
            route->route_kind = SAN9_V10_ROUTE_ROOT_EVENT;
            route->static_entry_address = 0x005179B0U;
            route->event_id = 0x2710U + descriptor->native_command_id;
            route->expected_object_vptr = SAN9_V10_ROOT_VPTR;
            route->static_route_confirmed = 1U;
            return 1;
        case SAN9_V10_STAGE_OPEN_SELECTOR:
            route->route_kind = SAN9_V10_ROUTE_OUTER_TASK_EVENT;
            route->static_entry_address = descriptor->outer_event_entry;
            route->event_id = 0x03E8U;
            route->expected_object_vptr = descriptor->outer_task_vptr;
            route->static_route_confirmed = 1U;
            return 1;
        case SAN9_V10_STAGE_CLEAR:
            route->route_kind = SAN9_V10_ROUTE_SELECTOR_EVENT;
            route->static_entry_address = 0x00578090U;
            route->event_id = 0x1D52U;
            route->expected_object_vptr = SAN9_V10_SELECTOR_VPTR;
            route->static_route_confirmed = 1U;
            return 1;
        case SAN9_V10_STAGE_NATIVE_FILL_MAX:
            route->route_kind = SAN9_V10_ROUTE_SELECTOR_EVENT;
            route->static_entry_address = 0x00578090U;
            route->event_id = 0x1D51U;
            route->expected_object_vptr = SAN9_V10_SELECTOR_VPTR;
            route->static_route_confirmed = 1U;
            return 1;
        case SAN9_V10_STAGE_ACCEPT_INNER:
            route->route_kind = SAN9_V10_ROUTE_SELECTOR_EVENT;
            route->static_entry_address = 0x00578090U;
            route->event_id = 0x1D4DU;
            route->expected_object_vptr = SAN9_V10_SELECTOR_VPTR;
            route->static_route_confirmed = 1U;
            return 1;
        case SAN9_V10_STAGE_ACCEPT_OUTER:
            route->route_kind = SAN9_V10_ROUTE_OUTER_TASK_EVENT;
            route->static_entry_address = descriptor->outer_event_entry;
            route->event_id = 0x0BB9U;
            route->expected_object_vptr = descriptor->outer_task_vptr;
            route->static_route_confirmed = 1U;
            return 1;
        case SAN9_V10_STAGE_VERIFY_EXACTLY_FIVE:
        case SAN9_V10_STAGE_VERIFY_WORKING:
        case SAN9_V10_STAGE_VERIFY_COMMITTED:
            route->route_kind = SAN9_V10_ROUTE_READ_ONLY_VERIFICATION;
            route->static_route_confirmed = 0U;
            return 1;
        default:
            memset(route, 0, sizeof(*route));
            return 0;
    }
}

int San9BridgeV10_GetCleanupRoute(
    uint32_t kind,
    san9_v10_cleanup_route *route)
{
    if (route == NULL) {
        return 0;
    }

    memset(route, 0, sizeof(*route));
    route->struct_size = sizeof(*route);
    route->kind = kind;
    route->live_authorized = 0U;
    route->dynamic_lifecycle_confirmed = 0U;

    switch (kind) {
        case SAN9_V10_CLEANUP_NO_MUTATION:
        case SAN9_V10_CLEANUP_HALT_RESTART:
            route->route_kind = SAN9_V10_ROUTE_NONE;
            route->static_candidate_known = 1U;
            return 1;
        case SAN9_V10_CLEANUP_CONDITIONAL_RESTORE_TARGET:
            route->route_kind = SAN9_V10_ROUTE_CONDITIONAL_ROOT_TARGET_CAS;
            route->static_candidate_known = 1U;
            return 1;
        case SAN9_V10_CLEANUP_CANCEL_SELECTOR:
        case SAN9_V10_CLEANUP_CANCEL_OUTER:
        case SAN9_V10_CLEANUP_DO_NOT_ROLLBACK_AFTER_ACCEPT:
            route->route_kind = SAN9_V10_ROUTE_NONE;
            route->static_candidate_known = 0U;
            return 1;
        default:
            memset(route, 0, sizeof(*route));
            return 0;
    }
}

san9_v10_validation_result San9BridgeV10_ValidateRequest(
    const san9_v10_single_command_request *request)
{
    uint8_t expected_fingerprint[SAN9_V10_DIGEST_SIZE];
    uint32_t left;
    uint32_t right;
    if (request == NULL) {
        return SAN9_V10_INVALID_NULL;
    }
    if (request->struct_size != sizeof(*request)) {
        return SAN9_V10_INVALID_STRUCT_SIZE;
    }
    if (request->protocol_version != SAN9_V10_REQUEST_PROTOCOL_VERSION) {
        return SAN9_V10_INVALID_PROTOCOL;
    }
    if (find_descriptor(request->command) == NULL) {
        return SAN9_V10_INVALID_COMMAND;
    }
    if (request->city_id >= 50U) {
        return SAN9_V10_INVALID_CITY;
    }
    if (request->corps_id >= 50U) {
        return SAN9_V10_INVALID_CORPS;
    }
    if (request->officer_count != SAN9_V10_REQUIRED_OFFICERS) {
        return SAN9_V10_INVALID_OFFICER;
    }
    for (left = 0U; left < SAN9_V10_REQUIRED_OFFICERS; ++left) {
        if (request->officer_ids[left] >= 850U) {
            return SAN9_V10_INVALID_OFFICER;
        }
        for (right = left + 1U; right < SAN9_V10_REQUIRED_OFFICERS; ++right) {
            if (request->officer_ids[left] == request->officer_ids[right]) {
                return SAN9_V10_DUPLICATE_OFFICER;
            }
        }
    }
    if (request->sequence == 0U) {
        return SAN9_V10_INVALID_SEQUENCE;
    }
    if (request->time_to_live_ms == 0U || request->time_to_live_ms > 10000U) {
        return SAN9_V10_INVALID_TTL;
    }
    if (request->reserve_money > 1000000U) {
        return SAN9_V10_INVALID_RESERVE_MONEY;
    }
    if (!bytes_have_nonzero(request->request_id, SAN9_V10_REQUEST_ID_SIZE)) {
        return SAN9_V10_INVALID_REQUEST_ID;
    }
    if (!bytes_have_nonzero(request->context_digest, SAN9_V10_DIGEST_SIZE)
        || !bytes_have_nonzero(request->generation_digest, SAN9_V10_DIGEST_SIZE)) {
        return SAN9_V10_INVALID_DIGEST;
    }
    if (!San9BridgeV10_ComputeRequestFingerprint(request, expected_fingerprint)
        || !san9_constant_time_equal(
            expected_fingerprint,
            request->request_fingerprint,
            SAN9_V10_DIGEST_SIZE)) {
        san9_secure_zero(expected_fingerprint, sizeof(expected_fingerprint));
        return SAN9_V10_INVALID_FINGERPRINT;
    }
    san9_secure_zero(expected_fingerprint, sizeof(expected_fingerprint));
    return SAN9_V10_VALID;
}

int San9BridgeV10_OfflineSelfTest(void)
{
    static const uint8_t expected_fingerprint[SAN9_V10_DIGEST_SIZE] = {
        0xDF, 0xD9, 0x44, 0x56, 0x12, 0x8C, 0xAA, 0xB7,
        0x57, 0xA2, 0x53, 0x89, 0x72, 0x51, 0x84, 0xC5,
        0x25, 0x0B, 0x23, 0x0A, 0x4B, 0xC9, 0x8E, 0x82,
        0xD7, 0xD7, 0x84, 0x78, 0x90, 0x93, 0xE0, 0xB7
    };
    static const uint8_t request_id[SAN9_V10_REQUEST_ID_SIZE] = {
        0x33, 0x22, 0x11, 0x00, 0x55, 0x44, 0x77, 0x66,
        0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF
    };
    san9_v10_single_command_request request;
    san9_v10_stage_route route;
    san9_v10_cleanup_route cleanup;
    uint8_t saved_fingerprint[SAN9_V10_DIGEST_SIZE];
    size_t index;
    int checks = 0;

    memset(&request, 0, sizeof(request));
    request.struct_size = sizeof(request);
    request.protocol_version = SAN9_V10_REQUEST_PROTOCOL_VERSION;
    memcpy(request.request_id, request_id, sizeof(request_id));
    request.sequence = UINT64_C(0x0102030405060708);
    request.command = SAN9_V10_COMMAND_COMMERCE;
    request.city_id = 30U;
    request.corps_id = 1U;
    request.officer_count = SAN9_V10_REQUIRED_OFFICERS;
    for (index = 0U; index < SAN9_V10_REQUIRED_OFFICERS; ++index) {
        request.officer_ids[index] = (uint32_t)(100U + index);
    }
    request.reserve_money = 777U;
    request.time_to_live_ms = 5000U;
    for (index = 0U; index < SAN9_V10_DIGEST_SIZE; ++index) {
        request.context_digest[index] = (uint8_t)(index + 1U);
        request.generation_digest[index] = (uint8_t)(0xA0U + index);
    }
    if (!San9BridgeV10_ComputeRequestFingerprint(&request, request.request_fingerprint)) {
        return -1;
    }

#define CHECK(expression) do { if (!(expression)) return -(++checks); ++checks; } while (0)
    CHECK(memcmp(CONTRACT.exact_exe_sha256, EXACT_EXE_SHA256, SAN9_V10_DIGEST_SIZE) == 0);
    CHECK(memcmp(request.request_fingerprint, expected_fingerprint, SAN9_V10_DIGEST_SIZE) == 0);
    CHECK(CONTRACT.execution_code_compiled == 0U && CONTRACT.live_authorized == 0U);
    CHECK(CONTRACT.process_access_code_compiled == 0U
        && CONTRACT.write_or_injection_code_compiled == 0U);
    CHECK(sizeof(DESCRIPTORS) / sizeof(DESCRIPTORS[0]) == 5U);
    CHECK(find_descriptor(SAN9_V10_COMMAND_PATROL)->native_command_id == 0U);
    CHECK(find_descriptor(SAN9_V10_COMMAND_COMMERCE)->native_command_id == 1U);
    CHECK(find_descriptor(SAN9_V10_COMMAND_CULTIVATE)->native_command_id == 2U);
    CHECK(find_descriptor(SAN9_V10_COMMAND_TRAIN)->native_command_id == 5U);
    CHECK(find_descriptor(SAN9_V10_COMMAND_REPAIR)->native_command_id == 3U);
    CHECK(strcmp(find_descriptor(SAN9_V10_COMMAND_COMMERCE)->key, "commerce") == 0);
    CHECK(strcmp(
        find_descriptor(SAN9_V10_COMMAND_COMMERCE)->outer_task_type_key,
        "outer-task-commerce") == 0);
    CHECK(find_descriptor(SAN9_V10_COMMAND_COMMERCE)->outer_task_type
        == SAN9_V10_COMMAND_COMMERCE);
    CHECK(find_descriptor(SAN9_V10_COMMAND_TRAIN)->money_model == 2U);
    CHECK(find_descriptor(SAN9_V10_COMMAND_PATROL)->required_officer_count == 5U);
    CHECK(find_descriptor(SAN9_V10_COMMAND_TRAIN)->expected_cost == 0U);
    CHECK(find_descriptor(SAN9_V10_COMMAND_REPAIR)->expected_cost == 250U);
    CHECK(find_descriptor(99U) == NULL);
    CHECK(San9BridgeV10_GetStageRoute(
        SAN9_V10_COMMAND_COMMERCE, SAN9_V10_STAGE_OPEN_OUTER, &route));
    CHECK(route.route_kind == SAN9_V10_ROUTE_ROOT_EVENT
        && route.static_entry_address == 0x005179B0U
        && route.event_id == 0x2711U
        && route.live_authorized == 0U);
    CHECK(San9BridgeV10_GetStageRoute(
        SAN9_V10_COMMAND_TRAIN, SAN9_V10_STAGE_OPEN_SELECTOR, &route));
    CHECK(route.static_entry_address == 0x004DF8E0U
        && route.event_id == 0x03E8U
        && route.expected_object_vptr == 0x0060C370U);
    CHECK(San9BridgeV10_GetStageRoute(
        SAN9_V10_COMMAND_PATROL, SAN9_V10_STAGE_CLEAR, &route));
    CHECK(route.static_entry_address == 0x00578090U
        && route.event_id == 0x1D52U
        && route.expected_object_vptr == SAN9_V10_SELECTOR_VPTR);
    CHECK(San9BridgeV10_GetStageRoute(
        SAN9_V10_COMMAND_CULTIVATE, SAN9_V10_STAGE_NATIVE_FILL_MAX, &route));
    CHECK(route.event_id == 0x1D51U);
    CHECK(San9BridgeV10_GetStageRoute(
        SAN9_V10_COMMAND_REPAIR, SAN9_V10_STAGE_ACCEPT_INNER, &route));
    CHECK(route.event_id == 0x1D4DU);
    CHECK(San9BridgeV10_GetStageRoute(
        SAN9_V10_COMMAND_COMMERCE, SAN9_V10_STAGE_ACCEPT_OUTER, &route));
    CHECK(route.event_id == 0x0BB9U && route.expected_object_vptr == 0x0060CCB0U);
    CHECK(San9BridgeV10_GetStageRoute(
        SAN9_V10_COMMAND_COMMERCE, SAN9_V10_STAGE_BIND_TARGET, &route));
    CHECK(route.route_kind == SAN9_V10_ROUTE_CONDITIONAL_ROOT_TARGET_CAS
        && route.static_route_confirmed == 0U
        && route.dynamic_lifecycle_confirmed == 0U);
    CHECK(!San9BridgeV10_GetStageRoute(SAN9_V10_COMMAND_COMMERCE, 999U, &route));
    CHECK(San9BridgeV10_GetCleanupRoute(
        SAN9_V10_CLEANUP_CONDITIONAL_RESTORE_TARGET, &cleanup));
    CHECK(cleanup.static_candidate_known == 1U && cleanup.live_authorized == 0U);
    CHECK(San9BridgeV10_GetCleanupRoute(SAN9_V10_CLEANUP_CANCEL_SELECTOR, &cleanup));
    CHECK(cleanup.route_kind == SAN9_V10_ROUTE_NONE && cleanup.static_candidate_known == 0U);
    CHECK(San9BridgeV10_ValidateRequest(&request) == SAN9_V10_VALID);
    memcpy(saved_fingerprint, request.request_fingerprint, sizeof(saved_fingerprint));
    request.city_id++;
    CHECK(San9BridgeV10_ValidateRequest(&request) == SAN9_V10_INVALID_FINGERPRINT);
    request.city_id--;
    CHECK(San9BridgeV10_ValidateRequest(&request) == SAN9_V10_VALID);
    request.officer_ids[4] = request.officer_ids[0];
    CHECK(San9BridgeV10_ValidateRequest(&request) == SAN9_V10_DUPLICATE_OFFICER);
    request.officer_ids[4] = 104U;
    request.command = 99U;
    CHECK(San9BridgeV10_ValidateRequest(&request) == SAN9_V10_INVALID_COMMAND);
    request.command = SAN9_V10_COMMAND_COMMERCE;
    request.time_to_live_ms = 10001U;
    CHECK(San9BridgeV10_ValidateRequest(&request) == SAN9_V10_INVALID_TTL);
    request.time_to_live_ms = 5000U;
    request.reserve_money = 1000001U;
    CHECK(San9BridgeV10_ValidateRequest(&request) == SAN9_V10_INVALID_RESERVE_MONEY);
    request.reserve_money = 777U;
    memset(request.context_digest, 0, sizeof(request.context_digest));
    CHECK(San9BridgeV10_ValidateRequest(&request) == SAN9_V10_INVALID_DIGEST);
    memcpy(request.request_fingerprint, saved_fingerprint, sizeof(saved_fingerprint));
#undef CHECK
    return checks;
}
