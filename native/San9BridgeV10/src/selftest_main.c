#include "san9_v10_native_contract.h"

#include <stddef.h>
#include <stdio.h>
#include <string.h>

typedef struct expected_descriptor {
    uint32_t command;
    const char *key;
    uint32_t native_id;
    uint32_t money_model;
    uint32_t cost;
    uint32_t task_vptr;
    uint32_t outer_event;
    uint32_t selector_callsite;
    uint32_t handler_vptr;
    uint32_t can_execute;
    uint32_t execute_ui;
    uint32_t factory_case;
} expected_descriptor;

static const expected_descriptor EXPECTED_DESCRIPTORS[] = {
    {1U, "patrol", 0U, 1U, 250U, 0x0060B920U, 0x004D8BA0U,
        0x004D8C1AU, 0x00609B90U, 0x004C1930U, 0x004C1840U, 0x005128B8U},
    {2U, "commerce", 1U, 1U, 250U, 0x0060CCB0U, 0x004E72D0U,
        0x004E734AU, 0x00609F38U, 0x004C6400U, 0x004C6310U, 0x00512900U},
    {3U, "cultivate", 2U, 1U, 250U, 0x0060BBA0U, 0x004DA670U,
        0x004DA6EAU, 0x00609BF0U, 0x004C1F50U, 0x004C1E60U, 0x00512948U},
    {4U, "train", 5U, 2U, 0U, 0x0060C370U, 0x004DF8E0U,
        0x004DF8C1U, 0x00609CE0U, 0x004C3890U, 0x004C37A0U, 0x00512A20U},
    {5U, "repair", 3U, 1U, 250U, 0x0060BCE8U, 0x004DB170U,
        0x004DB1EAU, 0x00609C20U, 0x004C2270U, 0x004C2180U, 0x00512990U}
};

static int oracle_check(int condition, int *checks)
{
    ++(*checks);
    return condition;
}

static int run_independent_oracle(void)
{
    static const uint8_t exact_exe_sha256[SAN9_V10_DIGEST_SIZE] = {
        0xD2, 0x07, 0x94, 0xAE, 0xFF, 0x67, 0x30, 0x1E,
        0xC2, 0xBF, 0x8C, 0x3B, 0xEC, 0xB1, 0xE9, 0x94,
        0x4C, 0x68, 0xC6, 0xC0, 0x58, 0x8F, 0xBF, 0xD4,
        0xBF, 0x04, 0xE8, 0x59, 0x7F, 0x0E, 0x50, 0x28
    };
    static const uint8_t request_id[SAN9_V10_REQUEST_ID_SIZE] = {
        0x33, 0x22, 0x11, 0x00, 0x55, 0x44, 0x77, 0x66,
        0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF
    };
    static const uint8_t expected_fingerprint[SAN9_V10_DIGEST_SIZE] = {
        0xDF, 0xD9, 0x44, 0x56, 0x12, 0x8C, 0xAA, 0xB7,
        0x57, 0xA2, 0x53, 0x89, 0x72, 0x51, 0x84, 0xC5,
        0x25, 0x0B, 0x23, 0x0A, 0x4B, 0xC9, 0x8E, 0x82,
        0xD7, 0xD7, 0x84, 0x78, 0x90, 0x93, 0xE0, 0xB7
    };
    const san9_v10_contract *contract;
    san9_v10_single_command_request request;
    san9_v10_stage_route route;
    san9_v10_cleanup_route cleanup;
    size_t index;
    int checks = 0;

#define ORACLE(expression) do { if (!oracle_check((expression), &checks)) return -checks; } while (0)
    ORACLE(sizeof(san9_v10_command_descriptor) == 108U);
    ORACLE(offsetof(san9_v10_command_descriptor, key) == 8U);
    ORACLE(offsetof(san9_v10_command_descriptor, outer_task_type_key) == 32U);
    ORACLE(offsetof(san9_v10_command_descriptor, handler_target_offset) == 104U);
    ORACLE(sizeof(san9_v10_stage_route) == 40U);
    ORACLE(sizeof(san9_v10_cleanup_route) == 24U);
    ORACLE(sizeof(san9_v10_single_command_request) == 176U);
    ORACLE(offsetof(san9_v10_single_command_request, sequence) == 24U);
    ORACLE(offsetof(san9_v10_single_command_request, officer_ids) == 48U);
    ORACLE(offsetof(san9_v10_single_command_request, context_digest) == 76U);
    ORACLE(offsetof(san9_v10_single_command_request, request_fingerprint) == 140U);
    ORACLE(sizeof(san9_v10_contract) == 88U);
    ORACLE(SAN9_V10_STAGE_BIND_TARGET == 2);
    ORACLE(SAN9_V10_STAGE_OPEN_OUTER == 3);
    ORACLE(SAN9_V10_STAGE_OPEN_SELECTOR == 4);
    ORACLE(SAN9_V10_STAGE_CLEAR == 5);
    ORACLE(SAN9_V10_STAGE_NATIVE_FILL_MAX == 6);
    ORACLE(SAN9_V10_STAGE_VERIFY_EXACTLY_FIVE == 7);
    ORACLE(SAN9_V10_STAGE_ACCEPT_INNER == 8);
    ORACLE(SAN9_V10_STAGE_VERIFY_WORKING == 9);
    ORACLE(SAN9_V10_STAGE_ACCEPT_OUTER == 10);
    ORACLE(SAN9_V10_STAGE_VERIFY_COMMITTED == 11);

    contract = San9BridgeV10_GetContract();
    ORACLE(contract != NULL);
    ORACLE(contract->struct_size == sizeof(*contract));
    ORACLE(contract->contract_version == SAN9_V10_CONTRACT_VERSION);
    ORACLE(contract->request_protocol_version == SAN9_V10_REQUEST_PROTOCOL_VERSION);
    ORACLE(contract->exact_image_base == SAN9_V10_EXACT_IMAGE_BASE);
    ORACLE(contract->exact_image_size == SAN9_V10_EXACT_IMAGE_SIZE);
    ORACLE(memcmp(
        contract->exact_exe_sha256,
        exact_exe_sha256,
        SAN9_V10_DIGEST_SIZE) == 0);
    ORACLE(contract->command_count == 5U && contract->action_stage_count == 7U);
    ORACLE(contract->execution_code_compiled == 0U && contract->live_authorized == 0U);
    ORACLE(contract->target_binding_dynamic_proven == 0U
        && contract->nested_modal_dynamic_proven == 0U
        && contract->native_cleanup_dynamic_proven == 0U);
    ORACLE(contract->process_access_code_compiled == 0U
        && contract->write_or_injection_code_compiled == 0U);

    for (index = 0U; index < sizeof(EXPECTED_DESCRIPTORS) / sizeof(EXPECTED_DESCRIPTORS[0]); ++index) {
        const expected_descriptor *expected = &EXPECTED_DESCRIPTORS[index];
        const san9_v10_command_descriptor *actual =
            San9BridgeV10_GetCommandDescriptor(expected->command);
        char expected_task_key[32];
        ORACLE(actual != NULL);
        ORACLE(actual->struct_size == sizeof(*actual));
        ORACLE(strcmp(actual->key, expected->key) == 0);
        ORACLE(actual->native_command_id == expected->native_id);
        ORACLE(actual->outer_task_type == expected->command);
        if (snprintf(expected_task_key, sizeof(expected_task_key),
            "outer-task-%s", expected->key) <= 0) {
            return -(++checks);
        }
        ORACLE(strcmp(actual->outer_task_type_key, expected_task_key) == 0);
        ORACLE(actual->money_model == expected->money_model);
        ORACLE(actual->required_officer_count == 5U);
        ORACLE(actual->expected_cost == expected->cost);
        ORACLE(actual->outer_task_vptr == expected->task_vptr);
        ORACLE(actual->outer_event_entry == expected->outer_event);
        ORACLE(actual->selector_callsite == expected->selector_callsite);
        ORACLE(actual->handler_vptr == expected->handler_vptr);
        ORACLE(actual->handler_can_execute == expected->can_execute);
        ORACLE(actual->handler_execute_ui == expected->execute_ui);
        ORACLE(actual->factory_case == expected->factory_case);
        ORACLE(actual->handler_target_offset == 0x60U);

        ORACLE(San9BridgeV10_GetStageRoute(
            expected->command, SAN9_V10_STAGE_BIND_TARGET, &route));
        ORACLE(route.route_kind == SAN9_V10_ROUTE_CONDITIONAL_ROOT_TARGET_CAS
            && route.static_entry_address == 0U
            && route.event_id == 0U
            && route.expected_object_vptr == SAN9_V10_ROOT_VPTR
            && route.static_route_confirmed == 0U
            && route.dynamic_lifecycle_confirmed == 0U
            && route.live_authorized == 0U);
        ORACLE(San9BridgeV10_GetStageRoute(
            expected->command, SAN9_V10_STAGE_OPEN_OUTER, &route));
        ORACLE(route.route_kind == SAN9_V10_ROUTE_ROOT_EVENT
            && route.static_entry_address == 0x005179B0U
            && route.event_id == 0x2710U + expected->native_id
            && route.expected_object_vptr == SAN9_V10_ROOT_VPTR
            && route.static_route_confirmed == 1U
            && route.dynamic_lifecycle_confirmed == 0U
            && route.live_authorized == 0U);
        ORACLE(San9BridgeV10_GetStageRoute(
            expected->command, SAN9_V10_STAGE_OPEN_SELECTOR, &route));
        ORACLE(route.route_kind == SAN9_V10_ROUTE_OUTER_TASK_EVENT
            && route.static_entry_address == expected->outer_event
            && route.event_id == 0x03E8U
            && route.expected_object_vptr == expected->task_vptr
            && route.static_route_confirmed == 1U
            && route.dynamic_lifecycle_confirmed == 0U
            && route.live_authorized == 0U);
        ORACLE(San9BridgeV10_GetStageRoute(
            expected->command, SAN9_V10_STAGE_CLEAR, &route));
        ORACLE(route.route_kind == SAN9_V10_ROUTE_SELECTOR_EVENT
            && route.static_entry_address == 0x00578090U
            && route.event_id == 0x1D52U
            && route.expected_object_vptr == SAN9_V10_SELECTOR_VPTR
            && route.static_route_confirmed == 1U
            && route.dynamic_lifecycle_confirmed == 0U
            && route.live_authorized == 0U);
        ORACLE(San9BridgeV10_GetStageRoute(
            expected->command, SAN9_V10_STAGE_NATIVE_FILL_MAX, &route));
        ORACLE(route.route_kind == SAN9_V10_ROUTE_SELECTOR_EVENT
            && route.static_entry_address == 0x00578090U
            && route.event_id == 0x1D51U
            && route.expected_object_vptr == SAN9_V10_SELECTOR_VPTR
            && route.static_route_confirmed == 1U
            && route.dynamic_lifecycle_confirmed == 0U
            && route.live_authorized == 0U);
        ORACLE(San9BridgeV10_GetStageRoute(
            expected->command, SAN9_V10_STAGE_ACCEPT_INNER, &route));
        ORACLE(route.route_kind == SAN9_V10_ROUTE_SELECTOR_EVENT
            && route.static_entry_address == 0x00578090U
            && route.event_id == 0x1D4DU
            && route.expected_object_vptr == SAN9_V10_SELECTOR_VPTR
            && route.static_route_confirmed == 1U
            && route.dynamic_lifecycle_confirmed == 0U
            && route.live_authorized == 0U);
        ORACLE(San9BridgeV10_GetStageRoute(
            expected->command, SAN9_V10_STAGE_ACCEPT_OUTER, &route));
        ORACLE(route.route_kind == SAN9_V10_ROUTE_OUTER_TASK_EVENT
            && route.static_entry_address == expected->outer_event
            && route.event_id == 0x0BB9U
            && route.expected_object_vptr == expected->task_vptr
            && route.static_route_confirmed == 1U
            && route.dynamic_lifecycle_confirmed == 0U
            && route.live_authorized == 0U);
    }

    ORACLE(SAN9_V10_CLEANUP_NO_MUTATION == 1);
    ORACLE(SAN9_V10_CLEANUP_CONDITIONAL_RESTORE_TARGET == 2);
    ORACLE(SAN9_V10_CLEANUP_CANCEL_SELECTOR == 3);
    ORACLE(SAN9_V10_CLEANUP_CANCEL_OUTER == 4);
    ORACLE(SAN9_V10_CLEANUP_DO_NOT_ROLLBACK_AFTER_ACCEPT == 5);
    ORACLE(SAN9_V10_CLEANUP_HALT_RESTART == 6);
    ORACLE(San9BridgeV10_GetCleanupRoute(
        SAN9_V10_CLEANUP_CONDITIONAL_RESTORE_TARGET, &cleanup));
    ORACLE(cleanup.route_kind == SAN9_V10_ROUTE_CONDITIONAL_ROOT_TARGET_CAS
        && cleanup.static_candidate_known == 1U && cleanup.live_authorized == 0U);
    ORACLE(San9BridgeV10_GetCleanupRoute(
        SAN9_V10_CLEANUP_DO_NOT_ROLLBACK_AFTER_ACCEPT, &cleanup));
    ORACLE(cleanup.route_kind == SAN9_V10_ROUTE_NONE
        && cleanup.static_candidate_known == 0U && cleanup.live_authorized == 0U);

    memset(&request, 0, sizeof(request));
    request.struct_size = sizeof(request);
    request.protocol_version = SAN9_V10_REQUEST_PROTOCOL_VERSION;
    memcpy(request.request_id, request_id, sizeof(request_id));
    request.sequence = UINT64_C(0x0102030405060708);
    request.command = SAN9_V10_COMMAND_COMMERCE;
    request.city_id = 30U;
    request.corps_id = 1U;
    request.officer_count = 5U;
    request.officer_ids[0] = 100U;
    request.officer_ids[1] = 101U;
    request.officer_ids[2] = 102U;
    request.officer_ids[3] = 103U;
    request.officer_ids[4] = 104U;
    request.reserve_money = 777U;
    request.time_to_live_ms = 5000U;
    for (index = 0U; index < SAN9_V10_DIGEST_SIZE; ++index) {
        request.context_digest[index] = (uint8_t)(index + 1U);
        request.generation_digest[index] = (uint8_t)(0xA0U + index);
    }
    ORACLE(San9BridgeV10_ComputeRequestFingerprint(
        &request, request.request_fingerprint));
    ORACLE(memcmp(
        request.request_fingerprint,
        expected_fingerprint,
        SAN9_V10_DIGEST_SIZE) == 0);
    ORACLE(San9BridgeV10_ValidateRequest(&request) == SAN9_V10_VALID);
    request.officer_ids[0] = 105U;
    ORACLE(San9BridgeV10_ValidateRequest(&request) == SAN9_V10_INVALID_FINGERPRINT);
#undef ORACLE
    return checks;
}

int main(int argc, char **argv)
{
    int internal_result;
    int oracle_result;
    if (argc != 2 || argv == NULL || argv[1] == NULL
        || strcmp(argv[1], "--self-test") != 0) {
        fputs("OFFLINE_ONLY: use --self-test\n", stderr);
        return 2;
    }

    internal_result = San9BridgeV10_OfflineSelfTest();
    if (internal_result < 0) {
        fprintf(stderr, "V10 offline native contract failed at check %d\n", -internal_result);
        return 1;
    }
    oracle_result = run_independent_oracle();
    if (oracle_result < 0) {
        fprintf(stderr, "V10 independent oracle failed at check %d\n", -oracle_result);
        return 1;
    }

    printf(
        "V10 offline native contract: internal=%d/%d, independent=%d/%d; "
        "execution_code_compiled=0; live_authorized=0; process_accessed=0\n",
        internal_result,
        internal_result,
        oracle_result,
        oracle_result);
    return 0;
}
