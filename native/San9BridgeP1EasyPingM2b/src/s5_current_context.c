#include "s5_current_context.h"

#include <string.h>

#include "sha256.h"

#define S5_APP_POINTER UINT32_C(0x01228340)
#define S5_APP_VTABLE UINT32_C(0x00604DD0)
#define S5_SCHEDULER_VTABLE UINT32_C(0x00607560)
#define S5_CONTROLLER_VTABLE UINT32_C(0x00610BC8)
#define S5_IDLE_STATE UINT32_C(0x000003E9)

#define S5_CITY_BASE UINT32_C(0x0124DB58)
#define S5_CITY_STRIDE UINT32_C(0x000001F0)
#define S5_CITY_COUNT UINT32_C(50)
#define S5_CITY_VTABLE UINT32_C(0x00605938)
#define S5_CITY_RESIDENCE_VTABLE UINT32_C(0x00606490)
#define S5_CITY_TYPE UINT32_C(5)

#define S5_CORPS_BASE UINT32_C(0x01253C38)
#define S5_CORPS_STRIDE UINT32_C(0x000000D4)
#define S5_CORPS_COUNT UINT32_C(50)
#define S5_CORPS_PLAYER_FLAG UINT32_C(0x00000001)
#define S5_CORPS_BARBARIAN_FLAG UINT32_C(0x00000004)
#define S5_COMMERCE_COST UINT32_C(250)
#define S5_MAXIMUM_MONEY UINT32_C(1000000)

#define S5_PERSON_BASE UINT32_C(0x01258EE0)
#define S5_PERSON_STRIDE UINT32_C(0x00000128)
#define S5_PERSON_COUNT UINT32_C(850)
#define S5_PERSON_BUSY_FLAG UINT32_C(0x00001000)
#define S5_MAXIMUM_POLITICS UINT32_C(255)

#define S5_MINIMUM_USER_ADDRESS UINT32_C(0x00010000)
#define S5_MAXIMUM_USER_ADDRESS UINT32_C(0x7FFFFFFF)
#define S5_MODULE_BEGIN UINT32_C(0x00400000)
#define S5_MODULE_END UINT32_C(0x01B59000)

#define S5_APP_WINDOW_OFFSET UINT32_C(0x04)
#define S5_WINDOW_OWNER_OFFSET UINT32_C(0x1C)
#define S5_OWNER_SCENE_OFFSET UINT32_C(0x18)
#define S5_SCENE_SCHEDULER_OFFSET UINT32_C(0x8C)
#define S5_TASK_CHILD_OFFSET UINT32_C(0x0C)
#define S5_TASK_PENDING_OFFSET UINT32_C(0x10)
#define S5_CONTROLLER_CORPS_OFFSET UINT32_C(0x30)
#define S5_CONTROLLER_STATE_OFFSET UINT32_C(0x34)
#define S5_CONTROLLER_TARGET_OFFSET UINT32_C(0x38)

#define S5_CITY_TYPE_OFFSET 0x06u
#define S5_CITY_RESIDENCE_OFFSET 0x58u
#define S5_CITY_SELF_OFFSET 0xBCu
#define S5_CITY_CORPS_OFFSET 0xCCu
#define S5_CITY_RESIDENT_FIRST_OFFSET 0xE0u
#define S5_CITY_RESIDENT_LAST_OFFSET 0xE4u
#define S5_CITY_RESIDENT_COUNT_OFFSET 0xE8u
#define S5_CITY_COMMERCE_CURRENT_OFFSET 0x1CCu
#define S5_CITY_COMMERCE_MAXIMUM_OFFSET 0x1D4u
#define S6_CITY_CULTIVATE_CURRENT_OFFSET 0x1D0u
#define S6_CITY_CULTIVATE_MAXIMUM_OFFSET 0x1D8u
#define S6_CITY_PATROL_CURRENT_OFFSET 0x1C4u
#define S6_CITY_PATROL_MAXIMUM UINT32_C(1000)
#define S6_CITY_TRAIN_TROOPS_OFFSET 0x88u
#define S6_CITY_TRAIN_MORALE_OFFSET 0x90u
#define S6_CITY_TRAIN_MAXIMUM UINT32_C(100)
#define S6_CITY_TRAIN_ORDER_OFFSET 0x3Eu
#define S6_CITY_REPAIR_CURRENT_OFFSET 0x3Cu
#define S6_CITY_REPAIR_MAXIMUM_OFFSET 0x1C8u
#define S6_CITY_REPAIR_ORDER_OFFSET 0x3Eu
#define S5_CITY_ORDER_FLAGS_OFFSET 0x1E0u
#define S5_CITY_COMMERCE_ORDER_FLAG UINT32_C(0x00000010)
#define S6_CITY_CULTIVATE_ORDER_FLAG UINT32_C(0x00000020)
#define S6_CITY_PATROL_ORDER_FLAG UINT32_C(0x00000008)
#define S6_CITY_TRAIN_ORDER_FLAG UINT32_C(0x00000002)
#define S6_CITY_REPAIR_ORDER_FLAG UINT32_C(0x00000001)
#define S5_NATIVE_COMMAND_COMMERCE UINT32_C(1)
#define S6_NATIVE_COMMAND_CULTIVATE UINT32_C(2)
#define S6_NATIVE_COMMAND_PATROL UINT32_C(0)
#define S6_NATIVE_COMMAND_TRAIN UINT32_C(5)
#define S6_NATIVE_COMMAND_REPAIR UINT32_C(3)

#define S5_CORPS_MONEY_OFFSET 0x14u
#define S5_CORPS_FLAGS_OFFSET 0x34u
#define S5_CORPS_MAIN_OFFSET 0xB8u
#define S5_CORPS_LEADER_OFFSET 0xBCu

#define S5_PERSON_ID_OFFSET 0x04u
#define S5_PERSON_POLITICS_OFFSET 0x60u
#define S6_PERSON_INTELLIGENCE_OFFSET 0x58u
#define S6_PERSON_STRENGTH_OFFSET 0x50u
#define S6_PERSON_LEADERSHIP_OFFSET 0x68u
#define S5_PERSON_IDENTITY_OFFSET 0x84u
#define S5_PERSON_READY_FLAGS_OFFSET 0xE8u
#define S5_PERSON_RESIDENCE_OFFSET 0xF4u
#define S5_PERSON_CAPTURE_SIZE 0xF8u

#define S5_NODE_NEXT_OFFSET 0x00u
#define S5_NODE_PREVIOUS_OFFSET 0x04u
#define S5_NODE_PERSON_OFFSET 0x08u
#define S5_NODE_SIZE 0x0Cu

#define S5_TASK_DOMAIN UINT32_C(0x3554534B)
#define S5_RESIDENT_DOMAIN UINT32_C(0x35534552)
#define S5_CANONICAL_DOMAIN UINT32_C(0x354E4143)
#define S5_BUSINESS_DOMAIN UINT32_C(0x35535542)

static uint16_t s5_u16(const uint8_t *bytes, size_t offset)
{
    uint16_t value = 0u;
    memcpy(&value, bytes + offset, sizeof(value));
    return value;
}

static uint32_t s5_u32(const uint8_t *bytes, size_t offset)
{
    uint32_t value = 0u;
    memcpy(&value, bytes + offset, sizeof(value));
    return value;
}

static int s5_valid_range(uint32_t address, size_t size)
{
    uint64_t end;
    if (address < S5_MINIMUM_USER_ADDRESS || size == 0u) {
        return 0;
    }
    end = (uint64_t)address + (uint64_t)size;
    return address <= S5_MAXIMUM_USER_ADDRESS
        && end <= (uint64_t)S5_MAXIMUM_USER_ADDRESS + UINT64_C(1);
}

static int s5_valid_pointer(uint32_t pointer)
{
    return s5_valid_range(pointer, 4u) && (pointer & UINT32_C(3)) == 0u;
}

static int s5_add(uint32_t base, uint32_t offset, uint32_t *result)
{
    uint64_t value;
    if (result == NULL || base == 0u) {
        return 0;
    }
    value = (uint64_t)base + (uint64_t)offset;
    if (value > UINT32_MAX) {
        return 0;
    }
    *result = (uint32_t)value;
    return 1;
}

static int s5_read_exact(
    const San9S5CurrentContextReader *reader,
    uint32_t address,
    void *output,
    size_t size)
{
    if (reader == NULL || reader->read == NULL || output == NULL
        || !s5_valid_range(address, size)) {
        return 0;
    }
    memset(output, 0, size);
    return reader->read(reader->context, address, output, size) != 0;
}

static int s5_read_pointer(
    const San9S5CurrentContextReader *reader,
    uint32_t base,
    uint32_t offset,
    uint32_t *value)
{
    uint32_t address;
    uint8_t bytes[4];
    if (value == NULL || !s5_add(base, offset, &address)
        || !s5_read_exact(reader, address, bytes, sizeof(bytes))) {
        return 0;
    }
    *value = s5_u32(bytes, 0u);
    return 1;
}

static int s5_table_index(
    uint32_t pointer,
    uint32_t base,
    uint32_t stride,
    uint32_t count,
    uint32_t *index)
{
    uint32_t relative;
    uint32_t candidate;
    if (pointer < base || stride == 0u || index == NULL) {
        return 0;
    }
    relative = pointer - base;
    if (relative % stride != 0u) {
        return 0;
    }
    candidate = relative / stride;
    if (candidate >= count) {
        return 0;
    }
    *index = candidate;
    return 1;
}

static int s5_bound_current_city_shape_valid(
    const San9S5BoundCurrentCity *bound)
{
    uint32_t city_id;
    uint32_t corps_id;
    return bound != NULL
        && s5_valid_pointer(bound->controller_pointer)
        && s5_table_index(bound->city_pointer,
            S5_CITY_BASE, S5_CITY_STRIDE, S5_CITY_COUNT, &city_id)
        && s5_table_index(bound->corps_pointer,
            S5_CORPS_BASE, S5_CORPS_STRIDE, S5_CORPS_COUNT, &corps_id);
}

San9S5CurrentContextStatus san9_s5_bound_current_city_normalize(
    const San9S5BoundCurrentCity *bound,
    uint32_t observed_controller_pointer,
    uint32_t observed_controller_corps,
    uint32_t observed_controller_target,
    uint32_t *normalized_city_pointer)
{
    if (normalized_city_pointer != NULL) {
        *normalized_city_pointer = 0u;
    }
    if (!s5_bound_current_city_shape_valid(bound)
        || normalized_city_pointer == NULL) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
    if (observed_controller_pointer != bound->controller_pointer
        || observed_controller_corps != bound->corps_pointer
        || (observed_controller_target != 0u
            && observed_controller_target != bound->city_pointer)) {
        return SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID;
    }
    *normalized_city_pointer = bound->city_pointer;
    return SAN9_S5_CURRENT_CONTEXT_OK;
}

static void s5_hash_u16(San9P1Sha256Context *sha, uint16_t value)
{
    uint8_t bytes[2];
    bytes[0] = (uint8_t)(value & UINT16_C(0x00FF));
    bytes[1] = (uint8_t)((value >> 8u) & UINT16_C(0x00FF));
    san9_p1_sha256_update(sha, bytes, sizeof(bytes));
}

static void s5_hash_u32(San9P1Sha256Context *sha, uint32_t value)
{
    uint8_t bytes[4];
    bytes[0] = (uint8_t)(value & UINT32_C(0x000000FF));
    bytes[1] = (uint8_t)((value >> 8u) & UINT32_C(0x000000FF));
    bytes[2] = (uint8_t)((value >> 16u) & UINT32_C(0x000000FF));
    bytes[3] = (uint8_t)((value >> 24u) & UINT32_C(0x000000FF));
    san9_p1_sha256_update(sha, bytes, sizeof(bytes));
}

static void s5_hash_u64(San9P1Sha256Context *sha, uint64_t value)
{
    uint8_t bytes[8];
    uint32_t index;
    for (index = 0u; index < 8u; ++index) {
        bytes[index] = (uint8_t)((value >> (index * 8u)) & UINT64_C(0xFF));
    }
    san9_p1_sha256_update(sha, bytes, sizeof(bytes));
}

static void s5_hash_officer(
    San9P1Sha256Context *sha,
    const San9S5CommerceOfficer *officer)
{
    s5_hash_u32(sha, officer->person_id);
    s5_hash_u32(sha, officer->person_pointer);
    s5_hash_u32(sha, officer->source_list_index);
    s5_hash_u32(sha, officer->effective_politics);
    s5_hash_u32(sha, officer->identity);
    s5_hash_u32(sha, officer->ready_flags);
    s5_hash_u32(sha, officer->residence_pointer);
}

static int s5_identity_shape_valid(const San9S5ExpectedIdentity *identity)
{
    return identity != NULL
        && identity->process_id != 0u
        && identity->process_generation != 0u
        && identity->main_thread_id != 0u
        && identity->window_handle != 0u;
}

static void s5_insert_top5(
    San9S5CommerceOfficer top5[SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT],
    uint32_t ready_before,
    const San9S5CommerceOfficer *candidate)
{
    uint32_t occupied = ready_before < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT
        ? ready_before
        : SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT;
    uint32_t position = occupied;
    while (position > 0u
        && candidate->effective_politics
            > top5[position - 1u].effective_politics) {
        if (position < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT) {
            top5[position] = top5[position - 1u];
        }
        --position;
    }
    if (position < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT) {
        top5[position] = *candidate;
    }
}

static size_t s5_person_ability_offset(uint32_t native_command_id)
{
    if (native_command_id == S6_NATIVE_COMMAND_TRAIN) {
        return S6_PERSON_STRENGTH_OFFSET;
    }
    if (native_command_id == S6_NATIVE_COMMAND_REPAIR) {
        return S6_PERSON_LEADERSHIP_OFFSET;
    }
    return native_command_id == S6_NATIVE_COMMAND_PATROL
        ? S6_PERSON_INTELLIGENCE_OFFSET : S5_PERSON_POLITICS_OFFSET;
}

static uint32_t s5_command_order_flag(uint32_t native_command_id)
{
    if (native_command_id == S6_NATIVE_COMMAND_PATROL) {
        return S6_CITY_PATROL_ORDER_FLAG;
    }
    if (native_command_id == S6_NATIVE_COMMAND_TRAIN) {
        return S6_CITY_TRAIN_ORDER_FLAG;
    }
    if (native_command_id == S6_NATIVE_COMMAND_REPAIR) {
        return S6_CITY_REPAIR_ORDER_FLAG;
    }
    return native_command_id == S5_NATIVE_COMMAND_COMMERCE
        ? S5_CITY_COMMERCE_ORDER_FLAG : S6_CITY_CULTIVATE_ORDER_FLAG;
}

int san9_s5_current_context_digest(
    const San9S5CurrentContextSnapshot *snapshot,
    uint8_t output[SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE])
{
    San9P1Sha256Context sha;
    uint32_t index;
    if (snapshot == NULL || output == NULL) {
        return 0;
    }
    san9_p1_sha256_initialize(&sha);
    s5_hash_u32(&sha, S5_CANONICAL_DOMAIN);
    s5_hash_u32(&sha, snapshot->structure_size);
    s5_hash_u32(&sha, snapshot->schema_major);
    s5_hash_u32(&sha, snapshot->schema_minor);
    s5_hash_u32(&sha, snapshot->binding.process_id);
    s5_hash_u32(&sha, snapshot->binding.main_thread_id);
    s5_hash_u64(&sha, snapshot->binding.process_generation);
    s5_hash_u64(&sha, snapshot->binding.window_handle);

#define S5_HASH_SNAPSHOT_U32(field) s5_hash_u32(&sha, snapshot->field)
    S5_HASH_SNAPSHOT_U32(app_pointer);
    S5_HASH_SNAPSHOT_U32(app_vtable);
    S5_HASH_SNAPSHOT_U32(window_pointer);
    S5_HASH_SNAPSHOT_U32(owner_pointer);
    S5_HASH_SNAPSHOT_U32(scene_pointer);
    S5_HASH_SNAPSHOT_U32(scheduler_pointer);
    S5_HASH_SNAPSHOT_U32(task_count);
    S5_HASH_SNAPSHOT_U32(controller_depth);
    S5_HASH_SNAPSHOT_U32(controller_pointer);
    S5_HASH_SNAPSHOT_U32(controller_vtable);
    S5_HASH_SNAPSHOT_U32(controller_child);
    S5_HASH_SNAPSHOT_U32(controller_pending);
    S5_HASH_SNAPSHOT_U32(controller_corps);
    S5_HASH_SNAPSHOT_U32(controller_state);
    S5_HASH_SNAPSHOT_U32(controller_target);
    S5_HASH_SNAPSHOT_U32(current_city_pointer);
    S5_HASH_SNAPSHOT_U32(city_id);
    S5_HASH_SNAPSHOT_U32(city_pointer);
    S5_HASH_SNAPSHOT_U32(city_vtable);
    S5_HASH_SNAPSHOT_U32(city_type);
    S5_HASH_SNAPSHOT_U32(city_self_pointer);
    S5_HASH_SNAPSHOT_U32(city_corps_pointer);
    S5_HASH_SNAPSHOT_U32(city_residence_pointer);
    S5_HASH_SNAPSHOT_U32(city_residence_vtable);
    S5_HASH_SNAPSHOT_U32(corps_id);
    S5_HASH_SNAPSHOT_U32(corps_pointer);
    S5_HASH_SNAPSHOT_U32(corps_flags);
    S5_HASH_SNAPSHOT_U32(corps_money);
    S5_HASH_SNAPSHOT_U32(corps_main_pointer);
    S5_HASH_SNAPSHOT_U32(corps_main_id);
    S5_HASH_SNAPSHOT_U32(corps_leader_pointer);
    S5_HASH_SNAPSHOT_U32(corps_leader_id);
    S5_HASH_SNAPSHOT_U32(main_corps_flags);
    S5_HASH_SNAPSHOT_U32(direct_controlled);
    S5_HASH_SNAPSHOT_U32(native_command_id);
    S5_HASH_SNAPSHOT_U32(commerce_current);
    S5_HASH_SNAPSHOT_U32(commerce_maximum);
    S5_HASH_SNAPSHOT_U32(cultivate_current);
    S5_HASH_SNAPSHOT_U32(cultivate_maximum);
    S5_HASH_SNAPSHOT_U32(patrol_current);
    S5_HASH_SNAPSHOT_U32(patrol_maximum);
    S5_HASH_SNAPSHOT_U32(order_flags);
    S5_HASH_SNAPSHOT_U32(train_troops);
    S5_HASH_SNAPSHOT_U32(train_morale);
    S5_HASH_SNAPSHOT_U32(train_maximum);
    S5_HASH_SNAPSHOT_U32(train_order_flags);
    S5_HASH_SNAPSHOT_U32(repair_current);
    S5_HASH_SNAPSHOT_U32(repair_maximum);
    S5_HASH_SNAPSHOT_U32(repair_order_flags);
    S5_HASH_SNAPSHOT_U32(resident_first);
    S5_HASH_SNAPSHOT_U32(resident_last);
    S5_HASH_SNAPSHOT_U32(resident_count);
    S5_HASH_SNAPSHOT_U32(ready_count);
    S5_HASH_SNAPSHOT_U32(exact_top5_count);
#undef S5_HASH_SNAPSHOT_U32

    for (index = 0u; index < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT; ++index) {
        s5_hash_officer(&sha, &snapshot->top5[index]);
    }
    san9_p1_sha256_update(
        &sha, snapshot->task_chain_digest,
        SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE);
    san9_p1_sha256_update(
        &sha, snapshot->resident_digest,
        SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE);
    san9_p1_sha256_finish(&sha, output);
    return 1;
}

int san9_s5_current_context_business_digest(
    const San9S5CurrentContextSnapshot *snapshot,
    uint8_t output[SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE])
{
    San9P1Sha256Context sha;
    uint32_t index;
    if (snapshot == NULL || output == NULL
        || snapshot->structure_size != sizeof(*snapshot)
        || snapshot->schema_major != SAN9_S5_CURRENT_CONTEXT_SCHEMA_MAJOR
        || snapshot->schema_minor != SAN9_S5_CURRENT_CONTEXT_SCHEMA_MINOR
        || snapshot->exact_top5_count != SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT) {
        return 0;
    }
    san9_p1_sha256_initialize(&sha);
    s5_hash_u32(&sha, S5_BUSINESS_DOMAIN);
    s5_hash_u32(&sha, snapshot->binding.process_id);
    s5_hash_u32(&sha, snapshot->binding.main_thread_id);
    s5_hash_u64(&sha, snapshot->binding.process_generation);
    s5_hash_u64(&sha, snapshot->binding.window_handle);
#define S5_HASH_BUSINESS_U32(field) s5_hash_u32(&sha, snapshot->field)
    S5_HASH_BUSINESS_U32(controller_pointer);
    S5_HASH_BUSINESS_U32(controller_vtable);
    S5_HASH_BUSINESS_U32(controller_corps);
    S5_HASH_BUSINESS_U32(controller_state);
    S5_HASH_BUSINESS_U32(city_id);
    S5_HASH_BUSINESS_U32(city_pointer);
    S5_HASH_BUSINESS_U32(city_vtable);
    S5_HASH_BUSINESS_U32(city_type);
    S5_HASH_BUSINESS_U32(city_self_pointer);
    S5_HASH_BUSINESS_U32(city_corps_pointer);
    S5_HASH_BUSINESS_U32(city_residence_pointer);
    S5_HASH_BUSINESS_U32(city_residence_vtable);
    S5_HASH_BUSINESS_U32(corps_id);
    S5_HASH_BUSINESS_U32(corps_pointer);
    S5_HASH_BUSINESS_U32(corps_flags);
    S5_HASH_BUSINESS_U32(corps_money);
    S5_HASH_BUSINESS_U32(corps_main_pointer);
    S5_HASH_BUSINESS_U32(corps_main_id);
    S5_HASH_BUSINESS_U32(corps_leader_pointer);
    S5_HASH_BUSINESS_U32(corps_leader_id);
    S5_HASH_BUSINESS_U32(main_corps_flags);
    S5_HASH_BUSINESS_U32(native_command_id);
    S5_HASH_BUSINESS_U32(commerce_current);
    S5_HASH_BUSINESS_U32(commerce_maximum);
    S5_HASH_BUSINESS_U32(cultivate_current);
    S5_HASH_BUSINESS_U32(cultivate_maximum);
    S5_HASH_BUSINESS_U32(patrol_current);
    S5_HASH_BUSINESS_U32(patrol_maximum);
    S5_HASH_BUSINESS_U32(order_flags);
    S5_HASH_BUSINESS_U32(train_troops);
    S5_HASH_BUSINESS_U32(train_morale);
    S5_HASH_BUSINESS_U32(train_maximum);
    S5_HASH_BUSINESS_U32(train_order_flags);
    S5_HASH_BUSINESS_U32(repair_current);
    S5_HASH_BUSINESS_U32(repair_maximum);
    S5_HASH_BUSINESS_U32(repair_order_flags);
    S5_HASH_BUSINESS_U32(resident_first);
    S5_HASH_BUSINESS_U32(resident_last);
    S5_HASH_BUSINESS_U32(resident_count);
    S5_HASH_BUSINESS_U32(exact_top5_count);
#undef S5_HASH_BUSINESS_U32
    for (index = 0u; index < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT; ++index) {
        s5_hash_officer(&sha, &snapshot->top5[index]);
    }
    san9_p1_sha256_finish(&sha, output);
    return 1;
}

San9S5CurrentContextStatus san9_s5_frozen_post_capture_reader(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    const San9S5CurrentContextSnapshot *pre,
    San9S5FrozenPostSnapshot *output)
{
    uint8_t root[0x3Cu];
    uint8_t city[0x1E4u];
    uint8_t corps[0xC0u];
    uint8_t main_corps[0xC0u];
    uint8_t person[S5_PERSON_CAPTURE_SIZE];
    uint8_t expected_digest[SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE];
    San9S5CurrentContextSnapshot observed;
    San9S5FrozenPostSnapshot post;
    uint32_t index;
    uint32_t table_id;
    uint32_t main_id;
    uint32_t leader_id;
    uint32_t main_leader_id;
    if (output != NULL) {
        memset(output, 0, sizeof(*output));
    }
    if (reader == NULL || reader->read == NULL || output == NULL
        || !s5_identity_shape_valid(identity) || pre == NULL
        || pre->structure_size != sizeof(*pre)
        || pre->schema_major != SAN9_S5_CURRENT_CONTEXT_SCHEMA_MAJOR
        || pre->schema_minor != SAN9_S5_CURRENT_CONTEXT_SCHEMA_MINOR
        || memcmp(&pre->binding, identity, sizeof(*identity)) != 0
        || pre->controller_vtable != S5_CONTROLLER_VTABLE
        || pre->controller_state != S5_IDLE_STATE
        || pre->exact_top5_count != SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT
        || !s5_valid_pointer(pre->controller_pointer)
        || !s5_valid_pointer(pre->city_pointer)
        || !s5_valid_pointer(pre->corps_pointer)) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
    memset(&post, 0, sizeof(post));
    memset(&observed, 0, sizeof(observed));
    memset(expected_digest, 0, sizeof(expected_digest));
    if (!s5_read_exact(reader, pre->controller_pointer, root, sizeof(root))) {
        return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
    }
    post.structure_size = sizeof(post);
    post.schema_major = SAN9_S5_CURRENT_CONTEXT_SCHEMA_MAJOR;
    post.schema_minor = SAN9_S5_CURRENT_CONTEXT_SCHEMA_MINOR;
    post.binding = *identity;
    post.controller_pointer = pre->controller_pointer;
    post.controller_vtable = s5_u32(root, 0u);
    post.controller_child = s5_u32(root, S5_TASK_CHILD_OFFSET);
    post.controller_pending = s5_u32(root, S5_TASK_PENDING_OFFSET);
    post.controller_corps = s5_u32(root, S5_CONTROLLER_CORPS_OFFSET);
    post.controller_state = s5_u32(root, S5_CONTROLLER_STATE_OFFSET);
    post.controller_target = s5_u32(root, S5_CONTROLLER_TARGET_OFFSET);
    if (post.controller_vtable != S5_CONTROLLER_VTABLE
        || post.controller_vtable != pre->controller_vtable
        || post.controller_child != 0u
        || post.controller_pending != pre->controller_pointer
        || post.controller_corps != pre->controller_corps
        || post.controller_state != pre->controller_state
        || (post.controller_target != 0u
            && post.controller_target != pre->city_pointer)) {
        return SAN9_S5_CURRENT_CONTEXT_FROZEN_POST_INVALID;
    }
    if (!s5_read_exact(reader, pre->city_pointer, city, sizeof(city))) {
        return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
    }
    post.city_id = pre->city_id;
    post.city_pointer = pre->city_pointer;
    post.city_vtable = s5_u32(city, 0u);
    post.city_type = city[S5_CITY_TYPE_OFFSET];
    post.city_self_pointer = s5_u32(city, S5_CITY_SELF_OFFSET);
    post.city_corps_pointer = s5_u32(city, S5_CITY_CORPS_OFFSET);
    post.city_residence_pointer = pre->city_pointer + S5_CITY_RESIDENCE_OFFSET;
    post.city_residence_vtable = s5_u32(city, S5_CITY_RESIDENCE_OFFSET);
    post.native_command_id = pre->native_command_id;
    post.commerce_current = s5_u32(city, S5_CITY_COMMERCE_CURRENT_OFFSET);
    post.commerce_maximum = s5_u32(city, S5_CITY_COMMERCE_MAXIMUM_OFFSET);
    post.cultivate_current = s5_u32(city, S6_CITY_CULTIVATE_CURRENT_OFFSET);
    post.cultivate_maximum = s5_u32(city, S6_CITY_CULTIVATE_MAXIMUM_OFFSET);
    post.patrol_current = s5_u32(city, S6_CITY_PATROL_CURRENT_OFFSET);
    post.patrol_maximum = S6_CITY_PATROL_MAXIMUM;
    post.order_flags = s5_u32(city, S5_CITY_ORDER_FLAGS_OFFSET);
    post.train_troops = s5_u32(city, S6_CITY_TRAIN_TROOPS_OFFSET);
    post.train_morale = s5_u32(city, S6_CITY_TRAIN_MORALE_OFFSET);
    post.train_maximum = S6_CITY_TRAIN_MAXIMUM;
    post.train_order_flags = s5_u16(city, S6_CITY_TRAIN_ORDER_OFFSET);
    post.repair_current = s5_u16(city, S6_CITY_REPAIR_CURRENT_OFFSET);
    post.repair_maximum = s5_u32(city, S6_CITY_REPAIR_MAXIMUM_OFFSET);
    post.repair_order_flags = s5_u16(city, S6_CITY_REPAIR_ORDER_OFFSET);
    post.resident_first = s5_u32(city, S5_CITY_RESIDENT_FIRST_OFFSET);
    post.resident_last = s5_u32(city, S5_CITY_RESIDENT_LAST_OFFSET);
    post.resident_count = s5_u32(city, S5_CITY_RESIDENT_COUNT_OFFSET);
    if (!s5_table_index(post.city_pointer, S5_CITY_BASE, S5_CITY_STRIDE,
            S5_CITY_COUNT, &table_id)
        || table_id != pre->city_id
        || post.city_vtable != pre->city_vtable
        || post.city_type != pre->city_type
        || post.city_self_pointer != pre->city_self_pointer
        || post.city_corps_pointer != pre->city_corps_pointer
        || post.city_residence_pointer != pre->city_residence_pointer
        || post.city_residence_vtable != pre->city_residence_vtable
        || post.native_command_id != pre->native_command_id
        || post.commerce_current != pre->commerce_current
        || post.commerce_maximum != pre->commerce_maximum
        || post.cultivate_current != pre->cultivate_current
        || post.cultivate_maximum != pre->cultivate_maximum
        || post.patrol_current != pre->patrol_current
        || post.patrol_maximum != pre->patrol_maximum
        || post.order_flags != pre->order_flags
        || post.train_troops != pre->train_troops
        || post.train_morale != pre->train_morale
        || post.train_maximum != pre->train_maximum
        || post.train_order_flags != pre->train_order_flags
        || post.repair_current != pre->repair_current
        || post.repair_maximum != pre->repair_maximum
        || post.repair_order_flags != pre->repair_order_flags
        || post.resident_first != pre->resident_first
        || post.resident_last != pre->resident_last
        || post.resident_count != pre->resident_count) {
        return SAN9_S5_CURRENT_CONTEXT_BUSINESS_DRIFT;
    }
    if (!s5_read_exact(reader, pre->corps_pointer, corps, sizeof(corps))
        || !s5_read_exact(reader, pre->corps_main_pointer,
            main_corps, sizeof(main_corps))) {
        return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
    }
    post.corps_id = pre->corps_id;
    post.corps_pointer = pre->corps_pointer;
    post.corps_money = s5_u32(corps, S5_CORPS_MONEY_OFFSET);
    post.corps_flags = s5_u32(corps, S5_CORPS_FLAGS_OFFSET);
    post.corps_main_pointer = s5_u32(corps, S5_CORPS_MAIN_OFFSET);
    post.corps_leader_pointer = s5_u32(corps, S5_CORPS_LEADER_OFFSET);
    post.main_corps_flags = s5_u32(main_corps, S5_CORPS_FLAGS_OFFSET);
    post.corps_main_id = pre->corps_main_id;
    post.corps_leader_id = pre->corps_leader_id;
    if (!s5_table_index(post.corps_pointer, S5_CORPS_BASE, S5_CORPS_STRIDE,
            S5_CORPS_COUNT, &table_id)
        || !s5_table_index(post.corps_main_pointer,
            S5_CORPS_BASE, S5_CORPS_STRIDE, S5_CORPS_COUNT, &main_id)
        || !s5_table_index(post.corps_leader_pointer,
            S5_PERSON_BASE, S5_PERSON_STRIDE, S5_PERSON_COUNT, &leader_id)
        || !s5_table_index(s5_u32(main_corps, S5_CORPS_LEADER_OFFSET),
            S5_PERSON_BASE, S5_PERSON_STRIDE,
            S5_PERSON_COUNT, &main_leader_id)
        || s5_u32(main_corps, S5_CORPS_MAIN_OFFSET)
            != pre->corps_main_pointer
        || table_id != pre->corps_id || main_id != pre->corps_main_id
        || leader_id != pre->corps_leader_id
        || post.corps_money != pre->corps_money
        || post.corps_flags != pre->corps_flags
        || post.corps_main_pointer != pre->corps_main_pointer
        || post.corps_leader_pointer != pre->corps_leader_pointer
        || post.main_corps_flags != pre->main_corps_flags
        || main_leader_id >= S5_PERSON_COUNT) {
        return SAN9_S5_CURRENT_CONTEXT_BUSINESS_DRIFT;
    }
    post.exact_top5_count = SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT;
    for (index = 0u; index < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT; ++index) {
        San9S5CommerceOfficer *candidate = &post.top5[index];
        uint16_t stored_id;
        if (!s5_table_index(pre->top5[index].person_pointer,
                S5_PERSON_BASE, S5_PERSON_STRIDE, S5_PERSON_COUNT, &table_id)
            || table_id != pre->top5[index].person_id
            || !s5_read_exact(reader, pre->top5[index].person_pointer,
                person, sizeof(person))) {
            return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
        }
        stored_id = s5_u16(person, S5_PERSON_ID_OFFSET);
        *candidate = pre->top5[index];
        candidate->person_id = table_id;
        candidate->effective_politics =
            s5_u32(person, s5_person_ability_offset(pre->native_command_id));
        candidate->identity = s5_u32(person, S5_PERSON_IDENTITY_OFFSET);
        candidate->ready_flags = s5_u32(person, S5_PERSON_READY_FLAGS_OFFSET);
        candidate->residence_pointer =
            s5_u32(person, S5_PERSON_RESIDENCE_OFFSET);
        if (stored_id != table_id
            || candidate->effective_politics
                != pre->top5[index].effective_politics
            || candidate->identity != pre->top5[index].identity
            || candidate->ready_flags != pre->top5[index].ready_flags
            || (candidate->ready_flags & S5_PERSON_BUSY_FLAG) != 0u
            || candidate->residence_pointer
                != pre->top5[index].residence_pointer
            || candidate->residence_pointer != pre->city_residence_pointer) {
            return SAN9_S5_CURRENT_CONTEXT_BUSINESS_DRIFT;
        }
    }
    observed = *pre;
    observed.controller_vtable = post.controller_vtable;
    observed.controller_corps = post.controller_corps;
    observed.controller_state = post.controller_state;
    observed.city_vtable = post.city_vtable;
    observed.city_type = post.city_type;
    observed.city_self_pointer = post.city_self_pointer;
    observed.city_corps_pointer = post.city_corps_pointer;
    observed.city_residence_pointer = post.city_residence_pointer;
    observed.city_residence_vtable = post.city_residence_vtable;
    observed.corps_flags = post.corps_flags;
    observed.corps_money = post.corps_money;
    observed.corps_main_pointer = post.corps_main_pointer;
    observed.corps_leader_pointer = post.corps_leader_pointer;
    observed.main_corps_flags = post.main_corps_flags;
    observed.native_command_id = post.native_command_id;
    observed.commerce_current = post.commerce_current;
    observed.commerce_maximum = post.commerce_maximum;
    observed.cultivate_current = post.cultivate_current;
    observed.cultivate_maximum = post.cultivate_maximum;
    observed.patrol_current = post.patrol_current;
    observed.patrol_maximum = post.patrol_maximum;
    observed.order_flags = post.order_flags;
    observed.train_troops = post.train_troops;
    observed.train_morale = post.train_morale;
    observed.train_maximum = post.train_maximum;
    observed.train_order_flags = post.train_order_flags;
    observed.repair_current = post.repair_current;
    observed.repair_maximum = post.repair_maximum;
    observed.repair_order_flags = post.repair_order_flags;
    observed.resident_first = post.resident_first;
    observed.resident_last = post.resident_last;
    observed.resident_count = post.resident_count;
    memcpy(observed.top5, post.top5, sizeof(observed.top5));
    if (!san9_s5_current_context_business_digest(pre, expected_digest)
        || !san9_s5_current_context_business_digest(
            &observed, post.business_digest)
        || memcmp(expected_digest, post.business_digest,
            sizeof(expected_digest)) != 0) {
        return SAN9_S5_CURRENT_CONTEXT_BUSINESS_DRIFT;
    }
    *output = post;
    return SAN9_S5_CURRENT_CONTEXT_OK;
}

int san9_s5_frozen_post_equal(
    const San9S5FrozenPostSnapshot *first,
    const San9S5FrozenPostSnapshot *second)
{
    return first != NULL && second != NULL
        && memcmp(first, second, sizeof(*first)) == 0;
}

San9S5CurrentContextStatus san9_s5_frozen_post_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    const San9S5CurrentContextSnapshot *pre,
    San9S5FrozenPostSnapshot *first,
    San9S5FrozenPostSnapshot *second)
{
    San9S5CurrentContextStatus status;
    if (first == NULL || second == NULL || first == second) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
    memset(first, 0, sizeof(*first));
    memset(second, 0, sizeof(*second));
    status = san9_s5_frozen_post_capture_reader(reader, identity, pre, first);
    if (status != SAN9_S5_CURRENT_CONTEXT_OK) {
        return status;
    }
    status = san9_s5_frozen_post_capture_reader(reader, identity, pre, second);
    if (status != SAN9_S5_CURRENT_CONTEXT_OK) {
        return status;
    }
    return san9_s5_frozen_post_equal(first, second)
        ? SAN9_S5_CURRENT_CONTEXT_OK : SAN9_S5_CURRENT_CONTEXT_AB_MISMATCH;
}

static San9S5CurrentContextStatus s5_current_context_capture_reader_kind(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *output,
    uint32_t native_command_id)
{
    San9S5CurrentContextSnapshot snapshot;
    San9P1Sha256Context task_sha;
    San9P1Sha256Context resident_sha;
    uint8_t app[8];
    uint8_t task[0x14];
    uint8_t controller_fields[0x0C];
    uint8_t city[S5_CITY_STRIDE];
    uint8_t corps[S5_CORPS_STRIDE];
    uint8_t main_corps[S5_CORPS_STRIDE];
    uint8_t node[S5_NODE_SIZE];
    uint8_t person[S5_PERSON_CAPTURE_SIZE];
    uint32_t visited_tasks[SAN9_S5_CURRENT_CONTEXT_MAX_TASKS];
    uint32_t visited_nodes[SAN9_S5_CURRENT_CONTEXT_MAX_RESIDENTS];
    uint8_t visited_persons[SAN9_S5_CURRENT_CONTEXT_MAX_RESIDENTS];
    uint32_t current;
    uint32_t depth;
    uint32_t controller_count = 0u;
    uint32_t controller_pointer = 0u;
    uint32_t controller_depth = 0u;
    uint32_t controller_child = 0u;
    uint32_t controller_pending = 0u;
    uint32_t controller_corps = 0u;
    uint32_t controller_state = 0u;
    uint32_t controller_target = 0u;
    uint32_t city_id;
    uint32_t corps_id;
    uint32_t main_corps_id;
    uint32_t leader_id;
    uint32_t main_leader_id;
    uint32_t main_pointer;
    uint32_t leader_pointer;
    uint32_t main_main_pointer;
    uint32_t main_leader_pointer;
    uint32_t current_node;
    uint32_t previous_node = 0u;
    uint32_t ready_count = 0u;
    uint32_t index;

    if (output != NULL) {
        memset(output, 0, sizeof(*output));
    }
    if (reader == NULL || reader->read == NULL || output == NULL
        || !s5_identity_shape_valid(identity)
        || (native_command_id != S5_NATIVE_COMMAND_COMMERCE
            && native_command_id != S6_NATIVE_COMMAND_CULTIVATE
            && native_command_id != S6_NATIVE_COMMAND_PATROL
            && native_command_id != S6_NATIVE_COMMAND_TRAIN
            && native_command_id != S6_NATIVE_COMMAND_REPAIR)) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }

    memset(&snapshot, 0, sizeof(snapshot));
    memset(visited_tasks, 0, sizeof(visited_tasks));
    memset(visited_nodes, 0, sizeof(visited_nodes));
    memset(visited_persons, 0, sizeof(visited_persons));
    snapshot.structure_size = (uint32_t)sizeof(snapshot);
    snapshot.schema_major = SAN9_S5_CURRENT_CONTEXT_SCHEMA_MAJOR;
    snapshot.schema_minor = SAN9_S5_CURRENT_CONTEXT_SCHEMA_MINOR;
    snapshot.binding = *identity;
    snapshot.app_pointer = S5_APP_POINTER;

    if (!s5_read_exact(reader, S5_APP_POINTER, app, sizeof(app))) {
        return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
    }
    snapshot.app_vtable = s5_u32(app, 0u);
    snapshot.window_pointer = s5_u32(app, S5_APP_WINDOW_OFFSET);
    if (snapshot.app_vtable != S5_APP_VTABLE
        || !s5_valid_pointer(snapshot.window_pointer)) {
        return SAN9_S5_CURRENT_CONTEXT_APP_CHAIN_INVALID;
    }
    if (!s5_read_pointer(
            reader, snapshot.window_pointer, S5_WINDOW_OWNER_OFFSET,
            &snapshot.owner_pointer)
        || !s5_read_pointer(
            reader, snapshot.owner_pointer, S5_OWNER_SCENE_OFFSET,
            &snapshot.scene_pointer)
        || !s5_read_pointer(
            reader, snapshot.scene_pointer, S5_SCENE_SCHEDULER_OFFSET,
            &snapshot.scheduler_pointer)) {
        return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
    }
    if (!s5_valid_pointer(snapshot.owner_pointer)
        || !s5_valid_pointer(snapshot.scene_pointer)
        || !s5_valid_pointer(snapshot.scheduler_pointer)) {
        return SAN9_S5_CURRENT_CONTEXT_APP_CHAIN_INVALID;
    }

    san9_p1_sha256_initialize(&task_sha);
    s5_hash_u32(&task_sha, S5_TASK_DOMAIN);
    current = snapshot.scheduler_pointer;
    for (depth = 0u; depth < SAN9_S5_CURRENT_CONTEXT_MAX_TASKS; ++depth) {
        uint32_t vtable;
        uint32_t child;
        uint32_t pending;
        uint32_t prior;
        if (!s5_valid_pointer(current)) {
            return SAN9_S5_CURRENT_CONTEXT_TASK_CHAIN_INVALID;
        }
        for (prior = 0u; prior < depth; ++prior) {
            if (visited_tasks[prior] == current) {
                return SAN9_S5_CURRENT_CONTEXT_TASK_CHAIN_INVALID;
            }
        }
        visited_tasks[depth] = current;
        if (!s5_read_exact(reader, current, task, sizeof(task))) {
            return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
        }
        vtable = s5_u32(task, 0u);
        child = s5_u32(task, S5_TASK_CHILD_OFFSET);
        pending = s5_u32(task, S5_TASK_PENDING_OFFSET);
        if (vtable < S5_MODULE_BEGIN || vtable >= S5_MODULE_END
            || (depth == 0u && vtable != S5_SCHEDULER_VTABLE)) {
            return SAN9_S5_CURRENT_CONTEXT_TASK_CHAIN_INVALID;
        }
        s5_hash_u32(&task_sha, current);
        s5_hash_u32(&task_sha, vtable);
        s5_hash_u32(&task_sha, child);
        s5_hash_u32(&task_sha, pending);
        if (vtable == S5_CONTROLLER_VTABLE) {
            ++controller_count;
            if (controller_count != 1u) {
                return SAN9_S5_CURRENT_CONTEXT_CONTROLLER_NOT_UNIQUE;
            }
            controller_pointer = current;
            controller_depth = depth;
            controller_child = child;
            controller_pending = pending;
            if (!s5_read_exact(
                    reader, current + S5_CONTROLLER_CORPS_OFFSET,
                    controller_fields, sizeof(controller_fields))) {
                return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
            }
            controller_corps = s5_u32(controller_fields, 0u);
            controller_state = s5_u32(controller_fields, 4u);
            controller_target = s5_u32(controller_fields, 8u);
        }
        if (child == 0u) {
            snapshot.task_count = depth + 1u;
            break;
        }
        if (!s5_valid_pointer(child)) {
            return SAN9_S5_CURRENT_CONTEXT_TASK_CHAIN_INVALID;
        }
        current = child;
    }
    if (depth == SAN9_S5_CURRENT_CONTEXT_MAX_TASKS) {
        return SAN9_S5_CURRENT_CONTEXT_TASK_CHAIN_INVALID;
    }
    if (controller_count != 1u) {
        return SAN9_S5_CURRENT_CONTEXT_CONTROLLER_NOT_UNIQUE;
    }
    san9_p1_sha256_finish(&task_sha, snapshot.task_chain_digest);
    snapshot.controller_depth = controller_depth;
    snapshot.controller_pointer = controller_pointer;
    snapshot.controller_vtable = S5_CONTROLLER_VTABLE;
    snapshot.controller_child = controller_child;
    snapshot.controller_pending = controller_pending;
    snapshot.controller_corps = controller_corps;
    snapshot.controller_state = controller_state;
    snapshot.controller_target = controller_target;

    snapshot.current_city_pointer = snapshot.controller_target;
    if (!s5_table_index(
            snapshot.current_city_pointer, S5_CITY_BASE, S5_CITY_STRIDE,
            S5_CITY_COUNT, &city_id)) {
        return SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID;
    }
    if (snapshot.controller_child != 0u
        || snapshot.controller_pending != snapshot.controller_pointer
        || snapshot.controller_state != S5_IDLE_STATE) {
        return SAN9_S5_CURRENT_CONTEXT_CONTROLLER_NOT_IDLE;
    }

    snapshot.city_id = city_id;
    snapshot.city_pointer = snapshot.current_city_pointer;
    if (!s5_read_exact(reader, snapshot.city_pointer, city, sizeof(city))) {
        return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
    }
    snapshot.city_vtable = s5_u32(city, 0u);
    snapshot.city_type = city[S5_CITY_TYPE_OFFSET];
    snapshot.city_residence_pointer =
        snapshot.city_pointer + S5_CITY_RESIDENCE_OFFSET;
    snapshot.city_residence_vtable =
        s5_u32(city, S5_CITY_RESIDENCE_OFFSET);
    snapshot.city_self_pointer = s5_u32(city, S5_CITY_SELF_OFFSET);
    snapshot.city_corps_pointer = s5_u32(city, S5_CITY_CORPS_OFFSET);
    if (snapshot.city_vtable != S5_CITY_VTABLE
        || snapshot.city_type != S5_CITY_TYPE
        || snapshot.city_residence_vtable != S5_CITY_RESIDENCE_VTABLE
        || snapshot.city_self_pointer != snapshot.city_pointer
        || !s5_table_index(
            snapshot.city_corps_pointer, S5_CORPS_BASE, S5_CORPS_STRIDE,
            S5_CORPS_COUNT, &corps_id)) {
        return SAN9_S5_CURRENT_CONTEXT_CITY_INVALID;
    }
    if (snapshot.controller_corps != snapshot.city_corps_pointer) {
        return SAN9_S5_CURRENT_CONTEXT_CONTROLLER_NOT_IDLE;
    }

    snapshot.corps_id = corps_id;
    snapshot.corps_pointer = snapshot.city_corps_pointer;
    if (!s5_read_exact(reader, snapshot.corps_pointer, corps, sizeof(corps))) {
        return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
    }
    snapshot.corps_money = s5_u32(corps, S5_CORPS_MONEY_OFFSET);
    snapshot.corps_flags = s5_u32(corps, S5_CORPS_FLAGS_OFFSET);
    main_pointer = s5_u32(corps, S5_CORPS_MAIN_OFFSET);
    leader_pointer = s5_u32(corps, S5_CORPS_LEADER_OFFSET);
    if (!s5_table_index(
            main_pointer, S5_CORPS_BASE, S5_CORPS_STRIDE,
            S5_CORPS_COUNT, &main_corps_id)
        || !s5_table_index(
            leader_pointer, S5_PERSON_BASE, S5_PERSON_STRIDE,
            S5_PERSON_COUNT, &leader_id)
        || snapshot.corps_money > S5_MAXIMUM_MONEY) {
        return SAN9_S5_CURRENT_CONTEXT_CORPS_INVALID;
    }
    if (!s5_read_exact(reader, main_pointer, main_corps, sizeof(main_corps))) {
        return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
    }
    main_main_pointer = s5_u32(main_corps, S5_CORPS_MAIN_OFFSET);
    main_leader_pointer = s5_u32(main_corps, S5_CORPS_LEADER_OFFSET);
    snapshot.main_corps_flags = s5_u32(main_corps, S5_CORPS_FLAGS_OFFSET);
    if (main_main_pointer != main_pointer
        || !s5_table_index(
            main_leader_pointer, S5_PERSON_BASE, S5_PERSON_STRIDE,
            S5_PERSON_COUNT, &main_leader_id)) {
        return SAN9_S5_CURRENT_CONTEXT_CORPS_INVALID;
    }
    snapshot.corps_main_pointer = main_pointer;
    snapshot.corps_main_id = main_corps_id;
    snapshot.corps_leader_pointer = leader_pointer;
    snapshot.corps_leader_id = leader_id;
    if ((snapshot.corps_flags & S5_CORPS_PLAYER_FLAG) == 0u
        || (snapshot.main_corps_flags & S5_CORPS_PLAYER_FLAG) == 0u
        || (snapshot.corps_flags & S5_CORPS_BARBARIAN_FLAG) != 0u
        || (snapshot.main_corps_flags & S5_CORPS_BARBARIAN_FLAG) != 0u) {
        return SAN9_S5_CURRENT_CONTEXT_CITY_NOT_DIRECT;
    }
    snapshot.direct_controlled = 1u;
    if (native_command_id != S6_NATIVE_COMMAND_TRAIN
        && snapshot.corps_money < S5_COMMERCE_COST) {
        return SAN9_S5_CURRENT_CONTEXT_MONEY_INSUFFICIENT;
    }

    snapshot.native_command_id = native_command_id;
    snapshot.commerce_current =
        s5_u32(city, S5_CITY_COMMERCE_CURRENT_OFFSET);
    snapshot.commerce_maximum =
        s5_u32(city, S5_CITY_COMMERCE_MAXIMUM_OFFSET);
    snapshot.cultivate_current =
        s5_u32(city, S6_CITY_CULTIVATE_CURRENT_OFFSET);
    snapshot.cultivate_maximum =
        s5_u32(city, S6_CITY_CULTIVATE_MAXIMUM_OFFSET);
    snapshot.patrol_current =
        s5_u32(city, S6_CITY_PATROL_CURRENT_OFFSET);
    snapshot.patrol_maximum = S6_CITY_PATROL_MAXIMUM;
    snapshot.order_flags = s5_u32(city, S5_CITY_ORDER_FLAGS_OFFSET);
    snapshot.train_troops = s5_u32(city, S6_CITY_TRAIN_TROOPS_OFFSET);
    snapshot.train_morale = s5_u32(city, S6_CITY_TRAIN_MORALE_OFFSET);
    snapshot.train_maximum = S6_CITY_TRAIN_MAXIMUM;
    snapshot.train_order_flags = s5_u16(city, S6_CITY_TRAIN_ORDER_OFFSET);
    snapshot.repair_current = s5_u16(city, S6_CITY_REPAIR_CURRENT_OFFSET);
    snapshot.repair_maximum = s5_u32(city, S6_CITY_REPAIR_MAXIMUM_OFFSET);
    snapshot.repair_order_flags = s5_u16(city, S6_CITY_REPAIR_ORDER_OFFSET);
    if (native_command_id == S5_NATIVE_COMMAND_COMMERCE
        && snapshot.commerce_current >= snapshot.commerce_maximum) {
        return SAN9_S5_CURRENT_CONTEXT_COMMERCE_COMPLETE;
    }
    if (native_command_id == S6_NATIVE_COMMAND_CULTIVATE
        && snapshot.cultivate_current >= snapshot.cultivate_maximum) {
        return SAN9_S6_CURRENT_CONTEXT_CULTIVATE_COMPLETE;
    }
    if (native_command_id == S6_NATIVE_COMMAND_PATROL
        && snapshot.patrol_current >= snapshot.patrol_maximum) {
        return SAN9_S6_CURRENT_CONTEXT_PATROL_COMPLETE;
    }
    if (native_command_id == S6_NATIVE_COMMAND_TRAIN
        && snapshot.train_troops == 0u) {
        return SAN9_S6_CURRENT_CONTEXT_TRAIN_NO_TROOPS;
    }
    if (native_command_id == S6_NATIVE_COMMAND_TRAIN
        && snapshot.train_morale >= snapshot.train_maximum) {
        return SAN9_S6_CURRENT_CONTEXT_TRAIN_COMPLETE;
    }
    if (native_command_id == S6_NATIVE_COMMAND_REPAIR
        && snapshot.repair_current >= snapshot.repair_maximum) {
        return SAN9_S6_CURRENT_CONTEXT_REPAIR_COMPLETE;
    }
    if ((((native_command_id == S6_NATIVE_COMMAND_TRAIN)
                ? snapshot.train_order_flags
                : (native_command_id == S6_NATIVE_COMMAND_REPAIR)
                    ? snapshot.repair_order_flags : snapshot.order_flags)
            & s5_command_order_flag(native_command_id))
            != 0u) {
        return SAN9_S5_CURRENT_CONTEXT_ORDER_CONFLICT;
    }

    snapshot.resident_first = s5_u32(city, S5_CITY_RESIDENT_FIRST_OFFSET);
    snapshot.resident_last = s5_u32(city, S5_CITY_RESIDENT_LAST_OFFSET);
    snapshot.resident_count = s5_u32(city, S5_CITY_RESIDENT_COUNT_OFFSET);
    if (snapshot.resident_count > SAN9_S5_CURRENT_CONTEXT_MAX_RESIDENTS) {
        return SAN9_S5_CURRENT_CONTEXT_RESIDENT_LIST_INVALID;
    }
    if (snapshot.resident_count == 0u) {
        if (snapshot.resident_first != 0u || snapshot.resident_last != 0u) {
            return SAN9_S5_CURRENT_CONTEXT_RESIDENT_LIST_INVALID;
        }
        return SAN9_S5_CURRENT_CONTEXT_READY_TOP5_UNAVAILABLE;
    }
    if (!s5_valid_pointer(snapshot.resident_first)
        || !s5_valid_pointer(snapshot.resident_last)) {
        return SAN9_S5_CURRENT_CONTEXT_RESIDENT_LIST_INVALID;
    }

    san9_p1_sha256_initialize(&resident_sha);
    s5_hash_u32(&resident_sha, S5_RESIDENT_DOMAIN);
    s5_hash_u32(&resident_sha, snapshot.city_id);
    s5_hash_u32(&resident_sha, snapshot.resident_first);
    s5_hash_u32(&resident_sha, snapshot.resident_last);
    s5_hash_u32(&resident_sha, snapshot.resident_count);
    current_node = snapshot.resident_first;
    for (index = 0u; index < snapshot.resident_count; ++index) {
        San9S5CommerceOfficer candidate;
        uint32_t next_node;
        uint32_t node_previous;
        uint32_t person_pointer;
        uint32_t person_id;
        uint32_t prior;
        uint16_t stored_person_id;

        if (!s5_valid_pointer(current_node)) {
            return SAN9_S5_CURRENT_CONTEXT_RESIDENT_LIST_INVALID;
        }
        for (prior = 0u; prior < index; ++prior) {
            if (visited_nodes[prior] == current_node) {
                return SAN9_S5_CURRENT_CONTEXT_RESIDENT_LIST_INVALID;
            }
        }
        visited_nodes[index] = current_node;
        if (!s5_read_exact(reader, current_node, node, sizeof(node))) {
            return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
        }
        next_node = s5_u32(node, S5_NODE_NEXT_OFFSET);
        node_previous = s5_u32(node, S5_NODE_PREVIOUS_OFFSET);
        person_pointer = s5_u32(node, S5_NODE_PERSON_OFFSET);
        if (node_previous != previous_node
            || (next_node != 0u && !s5_valid_pointer(next_node))) {
            return SAN9_S5_CURRENT_CONTEXT_RESIDENT_LIST_INVALID;
        }
        if (!s5_table_index(
                person_pointer, S5_PERSON_BASE, S5_PERSON_STRIDE,
                S5_PERSON_COUNT, &person_id)
            || visited_persons[person_id] != 0u) {
            return SAN9_S5_CURRENT_CONTEXT_PERSON_INVALID;
        }
        visited_persons[person_id] = 1u;
        if (!s5_read_exact(reader, person_pointer, person, sizeof(person))) {
            return SAN9_S5_CURRENT_CONTEXT_READ_FAILED;
        }
        stored_person_id = s5_u16(person, S5_PERSON_ID_OFFSET);
        memset(&candidate, 0, sizeof(candidate));
        candidate.person_id = person_id;
        candidate.person_pointer = person_pointer;
        candidate.source_list_index = index;
        candidate.effective_politics =
            s5_u32(person, s5_person_ability_offset(native_command_id));
        candidate.identity = s5_u32(person, S5_PERSON_IDENTITY_OFFSET);
        candidate.ready_flags = s5_u32(person, S5_PERSON_READY_FLAGS_OFFSET);
        candidate.residence_pointer =
            s5_u32(person, S5_PERSON_RESIDENCE_OFFSET);
        if (stored_person_id != person_id
            || candidate.identity > UINT32_C(3)
            || candidate.residence_pointer != snapshot.city_residence_pointer
            || candidate.effective_politics > S5_MAXIMUM_POLITICS) {
            return SAN9_S5_CURRENT_CONTEXT_PERSON_INVALID;
        }

        s5_hash_u32(&resident_sha, index);
        s5_hash_u32(&resident_sha, current_node);
        s5_hash_u32(&resident_sha, next_node);
        s5_hash_u32(&resident_sha, node_previous);
        s5_hash_u16(&resident_sha, stored_person_id);
        s5_hash_officer(&resident_sha, &candidate);
        if ((candidate.ready_flags & S5_PERSON_BUSY_FLAG) == 0u) {
            s5_insert_top5(snapshot.top5, ready_count, &candidate);
            ++ready_count;
        }
        previous_node = current_node;
        current_node = next_node;
    }
    if (current_node != 0u || previous_node != snapshot.resident_last) {
        return SAN9_S5_CURRENT_CONTEXT_RESIDENT_LIST_INVALID;
    }
    snapshot.ready_count = ready_count;
    if (ready_count < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT) {
        return SAN9_S5_CURRENT_CONTEXT_READY_TOP5_UNAVAILABLE;
    }
    snapshot.exact_top5_count = SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT;
    s5_hash_u32(&resident_sha, snapshot.ready_count);
    s5_hash_u32(&resident_sha, snapshot.exact_top5_count);
    for (index = 0u; index < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT; ++index) {
        s5_hash_officer(&resident_sha, &snapshot.top5[index]);
    }
    san9_p1_sha256_finish(&resident_sha, snapshot.resident_digest);

    if (!san9_s5_current_context_digest(
            &snapshot, snapshot.canonical_digest)) {
        return SAN9_S5_CURRENT_CONTEXT_DIGEST_FAILED;
    }
    *output = snapshot;
    return SAN9_S5_CURRENT_CONTEXT_OK;
}

San9S5CurrentContextStatus san9_s5_current_context_capture_reader(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *output)
{
    return s5_current_context_capture_reader_kind(reader, identity, output,
        S5_NATIVE_COMMAND_COMMERCE);
}

San9S5CurrentContextStatus san9_s6_cultivate_current_context_capture_reader(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *output)
{
    return s5_current_context_capture_reader_kind(reader, identity, output,
        S6_NATIVE_COMMAND_CULTIVATE);
}

San9S5CurrentContextStatus san9_s6_patrol_current_context_capture_reader(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *output)
{
    return s5_current_context_capture_reader_kind(reader, identity, output,
        S6_NATIVE_COMMAND_PATROL);
}

San9S5CurrentContextStatus san9_s6_train_current_context_capture_reader(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *output)
{
    return s5_current_context_capture_reader_kind(reader, identity, output,
        S6_NATIVE_COMMAND_TRAIN);
}

San9S5CurrentContextStatus san9_s6_repair_current_context_capture_reader(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *output)
{
    return s5_current_context_capture_reader_kind(reader, identity, output,
        S6_NATIVE_COMMAND_REPAIR);
}

int san9_s5_current_context_equal(
    const San9S5CurrentContextSnapshot *first,
    const San9S5CurrentContextSnapshot *second)
{
    uint32_t index;
    if (first == NULL || second == NULL) {
        return 0;
    }
#define S5_EQUAL_U32(field) \
    do { if (first->field != second->field) { return 0; } } while (0)
    S5_EQUAL_U32(structure_size);
    S5_EQUAL_U32(schema_major);
    S5_EQUAL_U32(schema_minor);
    S5_EQUAL_U32(binding.process_id);
    S5_EQUAL_U32(binding.main_thread_id);
    if (first->binding.process_generation
            != second->binding.process_generation
        || first->binding.window_handle != second->binding.window_handle) {
        return 0;
    }
    S5_EQUAL_U32(app_pointer);
    S5_EQUAL_U32(app_vtable);
    S5_EQUAL_U32(window_pointer);
    S5_EQUAL_U32(owner_pointer);
    S5_EQUAL_U32(scene_pointer);
    S5_EQUAL_U32(scheduler_pointer);
    S5_EQUAL_U32(task_count);
    S5_EQUAL_U32(controller_depth);
    S5_EQUAL_U32(controller_pointer);
    S5_EQUAL_U32(controller_vtable);
    S5_EQUAL_U32(controller_child);
    S5_EQUAL_U32(controller_pending);
    S5_EQUAL_U32(controller_corps);
    S5_EQUAL_U32(controller_state);
    S5_EQUAL_U32(controller_target);
    S5_EQUAL_U32(current_city_pointer);
    S5_EQUAL_U32(city_id);
    S5_EQUAL_U32(city_pointer);
    S5_EQUAL_U32(city_vtable);
    S5_EQUAL_U32(city_type);
    S5_EQUAL_U32(city_self_pointer);
    S5_EQUAL_U32(city_corps_pointer);
    S5_EQUAL_U32(city_residence_pointer);
    S5_EQUAL_U32(city_residence_vtable);
    S5_EQUAL_U32(corps_id);
    S5_EQUAL_U32(corps_pointer);
    S5_EQUAL_U32(corps_flags);
    S5_EQUAL_U32(corps_money);
    S5_EQUAL_U32(corps_main_pointer);
    S5_EQUAL_U32(corps_main_id);
    S5_EQUAL_U32(corps_leader_pointer);
    S5_EQUAL_U32(corps_leader_id);
    S5_EQUAL_U32(main_corps_flags);
    S5_EQUAL_U32(direct_controlled);
    S5_EQUAL_U32(native_command_id);
    S5_EQUAL_U32(commerce_current);
    S5_EQUAL_U32(commerce_maximum);
    S5_EQUAL_U32(cultivate_current);
    S5_EQUAL_U32(cultivate_maximum);
    S5_EQUAL_U32(patrol_current);
    S5_EQUAL_U32(patrol_maximum);
    S5_EQUAL_U32(order_flags);
    S5_EQUAL_U32(train_troops);
    S5_EQUAL_U32(train_morale);
    S5_EQUAL_U32(train_maximum);
    S5_EQUAL_U32(train_order_flags);
    S5_EQUAL_U32(repair_current);
    S5_EQUAL_U32(repair_maximum);
    S5_EQUAL_U32(repair_order_flags);
    S5_EQUAL_U32(resident_first);
    S5_EQUAL_U32(resident_last);
    S5_EQUAL_U32(resident_count);
    S5_EQUAL_U32(ready_count);
    S5_EQUAL_U32(exact_top5_count);
#undef S5_EQUAL_U32
    for (index = 0u; index < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT; ++index) {
        const San9S5CommerceOfficer *a = &first->top5[index];
        const San9S5CommerceOfficer *b = &second->top5[index];
        if (a->person_id != b->person_id
            || a->person_pointer != b->person_pointer
            || a->source_list_index != b->source_list_index
            || a->effective_politics != b->effective_politics
            || a->identity != b->identity
            || a->ready_flags != b->ready_flags
            || a->residence_pointer != b->residence_pointer) {
            return 0;
        }
    }
    return memcmp(
            first->task_chain_digest, second->task_chain_digest,
            SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE) == 0
        && memcmp(
            first->resident_digest, second->resident_digest,
            SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE) == 0
        && memcmp(
            first->canonical_digest, second->canonical_digest,
            SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE) == 0;
}

static San9S5CurrentContextStatus s5_current_context_capture_reader_ab_kind(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second,
    uint32_t native_command_id)
{
    San9S5CurrentContextStatus status;
    if (first == NULL || second == NULL || first == second) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
    memset(first, 0, sizeof(*first));
    memset(second, 0, sizeof(*second));
    status = s5_current_context_capture_reader_kind(
        reader, identity, first, native_command_id);
    if (status != SAN9_S5_CURRENT_CONTEXT_OK) {
        return status;
    }
    status = s5_current_context_capture_reader_kind(
        reader, identity, second, native_command_id);
    if (status != SAN9_S5_CURRENT_CONTEXT_OK) {
        return status;
    }
    if (!san9_s5_current_context_equal(first, second)) {
        return SAN9_S5_CURRENT_CONTEXT_AB_MISMATCH;
    }
    return SAN9_S5_CURRENT_CONTEXT_OK;
}

San9S5CurrentContextStatus san9_s5_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    return s5_current_context_capture_reader_ab_kind(
        reader, identity, first, second, S5_NATIVE_COMMAND_COMMERCE);
}

San9S5CurrentContextStatus san9_s6_cultivate_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    return s5_current_context_capture_reader_ab_kind(
        reader, identity, first, second, S6_NATIVE_COMMAND_CULTIVATE);
}

San9S5CurrentContextStatus san9_s6_patrol_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    return s5_current_context_capture_reader_ab_kind(
        reader, identity, first, second, S6_NATIVE_COMMAND_PATROL);
}

San9S5CurrentContextStatus san9_s6_train_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    return s5_current_context_capture_reader_ab_kind(
        reader, identity, first, second, S6_NATIVE_COMMAND_TRAIN);
}

San9S5CurrentContextStatus san9_s6_repair_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    return s5_current_context_capture_reader_ab_kind(
        reader, identity, first, second, S6_NATIVE_COMMAND_REPAIR);
}

typedef struct S5BoundReaderContext {
    const San9S5CurrentContextReader *inner;
    const San9S5BoundCurrentCity *bound;
    int binding_drift;
} S5BoundReaderContext;

static int s5_bound_reader_read(
    void *context,
    uint32_t address,
    void *output,
    size_t output_size)
{
    S5BoundReaderContext *bound_context =
        (S5BoundReaderContext *)context;
    uint32_t controller_fields_address;
    if (bound_context == NULL || bound_context->inner == NULL
        || bound_context->inner->read == NULL
        || bound_context->bound == NULL || output == NULL
        || !bound_context->inner->read(
            bound_context->inner->context, address, output, output_size)) {
        return 0;
    }
    if (s5_add(bound_context->bound->controller_pointer,
            S5_CONTROLLER_CORPS_OFFSET, &controller_fields_address)
        && address == controller_fields_address
        && output_size == sizeof(uint32_t) * 3u) {
        uint8_t *bytes = (uint8_t *)output;
        uint32_t normalized_city = 0u;
        San9S5CurrentContextStatus status =
            san9_s5_bound_current_city_normalize(
                bound_context->bound,
                bound_context->bound->controller_pointer,
                s5_u32(bytes, 0u),
                s5_u32(bytes, 8u),
                &normalized_city);
        if (status != SAN9_S5_CURRENT_CONTEXT_OK) {
            bound_context->binding_drift = 1;
            normalized_city = bound_context->bound->city_pointer;
        }
        memcpy(bytes + 8u, &normalized_city, sizeof(normalized_city));
    }
    return 1;
}

static San9S5CurrentContextStatus s5_capture_reader_ab_native(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    uint32_t native_command_id,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    switch (native_command_id) {
    case S6_NATIVE_COMMAND_PATROL:
        return san9_s6_patrol_current_context_capture_reader_ab(
            reader, identity, first, second);
    case S5_NATIVE_COMMAND_COMMERCE:
        return san9_s5_current_context_capture_reader_ab(
            reader, identity, first, second);
    case S6_NATIVE_COMMAND_CULTIVATE:
        return san9_s6_cultivate_current_context_capture_reader_ab(
            reader, identity, first, second);
    case S6_NATIVE_COMMAND_REPAIR:
        return san9_s6_repair_current_context_capture_reader_ab(
            reader, identity, first, second);
    case S6_NATIVE_COMMAND_TRAIN:
        return san9_s6_train_current_context_capture_reader_ab(
            reader, identity, first, second);
    default:
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
}

San9S5CurrentContextStatus san9_s5_bound_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    const San9S5BoundCurrentCity *bound,
    uint32_t native_command_id,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    S5BoundReaderContext bound_context;
    San9S5CurrentContextReader bound_reader;
    San9S5CurrentContextStatus status;
    if (first != NULL) {
        memset(first, 0, sizeof(*first));
    }
    if (second != NULL) {
        memset(second, 0, sizeof(*second));
    }
    if (reader == NULL || reader->read == NULL
        || !s5_identity_shape_valid(identity)
        || !s5_bound_current_city_shape_valid(bound)
        || first == NULL || second == NULL || first == second) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
    memset(&bound_context, 0, sizeof(bound_context));
    memset(&bound_reader, 0, sizeof(bound_reader));
    bound_context.inner = reader;
    bound_context.bound = bound;
    bound_reader.read = s5_bound_reader_read;
    bound_reader.context = &bound_context;
    status = s5_capture_reader_ab_native(
        &bound_reader, identity, native_command_id, first, second);
    if (bound_context.binding_drift
        || (status == SAN9_S5_CURRENT_CONTEXT_OK
            && (first->controller_pointer != bound->controller_pointer
                || first->city_pointer != bound->city_pointer
                || first->corps_pointer != bound->corps_pointer
                || second->controller_pointer != bound->controller_pointer
                || second->city_pointer != bound->city_pointer
                || second->corps_pointer != bound->corps_pointer))) {
        memset(first, 0, sizeof(*first));
        memset(second, 0, sizeof(*second));
        return SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID;
    }
    return status;
}

#if defined(_WIN32) && !defined(SAN9_S5_CONTEXT_NO_HANDLE)
typedef struct San9S5HandleReadContext {
    HANDLE process;
} San9S5HandleReadContext;

static int s5_handle_read(
    void *context,
    uint32_t address,
    void *output,
    size_t output_size)
{
    San9S5HandleReadContext *read_context =
        (San9S5HandleReadContext *)context;
    SIZE_T actual = 0u;
    if (read_context == NULL || read_context->process == NULL
        || output == NULL || output_size == 0u) {
        return 0;
    }
    return ReadProcessMemory(
            read_context->process, (LPCVOID)(uintptr_t)address,
            output, output_size, &actual) != FALSE
        && actual == output_size;
}

static int s5_handle_binding_matches(
    HANDLE process,
    const San9S5ExpectedIdentity *identity,
    HWND expected_window)
{
    FILETIME creation;
    FILETIME exit_time;
    FILETIME kernel;
    FILETIME user;
    DWORD window_process_id = 0u;
    DWORD window_thread_id;
    uint64_t generation;
    if (process == NULL || process == INVALID_HANDLE_VALUE
        || !s5_identity_shape_valid(identity)
        || expected_window == NULL
        || (uint64_t)(uintptr_t)expected_window != identity->window_handle) {
        return 0;
    }
    if ((uint32_t)GetProcessId(process) != identity->process_id
        || GetProcessTimes(
            process, &creation, &exit_time, &kernel, &user) == FALSE) {
        return 0;
    }
    generation = ((uint64_t)creation.dwHighDateTime << 32u)
        | (uint64_t)creation.dwLowDateTime;
    if (generation != identity->process_generation
        || IsWindow(expected_window) == FALSE) {
        return 0;
    }
    window_thread_id =
        GetWindowThreadProcessId(expected_window, &window_process_id);
    return window_thread_id == identity->main_thread_id
        && window_process_id == identity->process_id;
}

static San9S5CurrentContextStatus s5_current_context_capture_handle_kind(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *output,
    uint32_t native_command_id)
{
    San9S5ExpectedIdentity identity;
    San9S5HandleReadContext read_context;
    San9S5CurrentContextReader reader;
    San9S5CurrentContextStatus status;
    if (output != NULL) {
        memset(output, 0, sizeof(*output));
    }
    if (output == NULL || process == NULL || process == INVALID_HANDLE_VALUE
        || expected_process_id == 0u || expected_process_generation == 0u
        || expected_main_thread_id == 0u || expected_window == NULL) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
    memset(&identity, 0, sizeof(identity));
    identity.process_id = expected_process_id;
    identity.process_generation = expected_process_generation;
    identity.main_thread_id = expected_main_thread_id;
    identity.window_handle = (uint64_t)(uintptr_t)expected_window;
    if (!s5_handle_binding_matches(process, &identity, expected_window)) {
        return SAN9_S5_CURRENT_CONTEXT_BINDING_MISMATCH;
    }
    read_context.process = process;
    reader.read = s5_handle_read;
    reader.context = &read_context;
    status = s5_current_context_capture_reader_kind(
        &reader, &identity, output, native_command_id);
    if (!s5_handle_binding_matches(process, &identity, expected_window)) {
        memset(output, 0, sizeof(*output));
        return SAN9_S5_CURRENT_CONTEXT_BINDING_MISMATCH;
    }
    return status;
}

San9S5CurrentContextStatus san9_s5_current_context_capture_handle(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *output)
{
    return s5_current_context_capture_handle_kind(process, expected_process_id,
        expected_process_generation, expected_main_thread_id, expected_window,
        output, S5_NATIVE_COMMAND_COMMERCE);
}

static San9S5CurrentContextStatus s5_current_context_capture_handle_ab_kind(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second,
    uint32_t native_command_id)
{
    San9S5CurrentContextStatus status;
    if (first == NULL || second == NULL || first == second) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
    memset(first, 0, sizeof(*first));
    memset(second, 0, sizeof(*second));
    status = s5_current_context_capture_handle_kind(
        process, expected_process_id, expected_process_generation,
        expected_main_thread_id, expected_window, first, native_command_id);
    if (status != SAN9_S5_CURRENT_CONTEXT_OK) {
        return status;
    }
    status = s5_current_context_capture_handle_kind(
        process, expected_process_id, expected_process_generation,
        expected_main_thread_id, expected_window, second, native_command_id);
    if (status != SAN9_S5_CURRENT_CONTEXT_OK) {
        return status;
    }
    if (!san9_s5_current_context_equal(first, second)) {
        return SAN9_S5_CURRENT_CONTEXT_AB_MISMATCH;
    }
    return SAN9_S5_CURRENT_CONTEXT_OK;
}

San9S5CurrentContextStatus san9_s5_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    return s5_current_context_capture_handle_ab_kind(process,
        expected_process_id, expected_process_generation,
        expected_main_thread_id, expected_window, first, second,
        S5_NATIVE_COMMAND_COMMERCE);
}

San9S5CurrentContextStatus san9_s6_cultivate_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    return s5_current_context_capture_handle_ab_kind(process,
        expected_process_id, expected_process_generation,
        expected_main_thread_id, expected_window, first, second,
        S6_NATIVE_COMMAND_CULTIVATE);
}

San9S5CurrentContextStatus san9_s6_patrol_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    return s5_current_context_capture_handle_ab_kind(process,
        expected_process_id, expected_process_generation,
        expected_main_thread_id, expected_window, first, second,
        S6_NATIVE_COMMAND_PATROL);
}

San9S5CurrentContextStatus san9_s6_train_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    return s5_current_context_capture_handle_ab_kind(process,
        expected_process_id, expected_process_generation,
        expected_main_thread_id, expected_window, first, second,
        S6_NATIVE_COMMAND_TRAIN);
}

San9S5CurrentContextStatus san9_s6_repair_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    return s5_current_context_capture_handle_ab_kind(process,
        expected_process_id, expected_process_generation,
        expected_main_thread_id, expected_window, first, second,
        S6_NATIVE_COMMAND_REPAIR);
}
San9S5CurrentContextStatus san9_s5_bound_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    const San9S5BoundCurrentCity *bound,
    uint32_t native_command_id,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    San9S5ExpectedIdentity identity;
    San9S5HandleReadContext read_context;
    San9S5CurrentContextReader reader;
    San9S5CurrentContextStatus status;
    if (first != NULL) {
        memset(first, 0, sizeof(*first));
    }
    if (second != NULL) {
        memset(second, 0, sizeof(*second));
    }
    if (process == NULL || process == INVALID_HANDLE_VALUE
        || expected_process_id == 0u || expected_process_generation == 0u
        || expected_main_thread_id == 0u || expected_window == NULL
        || first == NULL || second == NULL || first == second) {
        return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
    }
    memset(&identity, 0, sizeof(identity));
    memset(&read_context, 0, sizeof(read_context));
    memset(&reader, 0, sizeof(reader));
    identity.process_id = expected_process_id;
    identity.process_generation = expected_process_generation;
    identity.main_thread_id = expected_main_thread_id;
    identity.window_handle = (uint64_t)(uintptr_t)expected_window;
    if (!s5_handle_binding_matches(process, &identity, expected_window)) {
        return SAN9_S5_CURRENT_CONTEXT_BINDING_MISMATCH;
    }
    read_context.process = process;
    reader.read = s5_handle_read;
    reader.context = &read_context;
    status = san9_s5_bound_current_context_capture_reader_ab(
        &reader, &identity, bound, native_command_id, first, second);
    if (!s5_handle_binding_matches(process, &identity, expected_window)) {
        memset(first, 0, sizeof(*first));
        memset(second, 0, sizeof(*second));
        return SAN9_S5_CURRENT_CONTEXT_BINDING_MISMATCH;
    }
    return status;
}
#endif

const char *san9_s5_current_context_status_name(
    San9S5CurrentContextStatus status)
{
    switch (status) {
    case SAN9_S5_CURRENT_CONTEXT_OK:
        return "OK";
    case SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT:
        return "INVALID_ARGUMENT";
    case SAN9_S5_CURRENT_CONTEXT_BINDING_MISMATCH:
        return "BINDING_MISMATCH";
    case SAN9_S5_CURRENT_CONTEXT_READ_FAILED:
        return "READ_FAILED";
    case SAN9_S5_CURRENT_CONTEXT_APP_CHAIN_INVALID:
        return "APP_CHAIN_INVALID";
    case SAN9_S5_CURRENT_CONTEXT_TASK_CHAIN_INVALID:
        return "TASK_CHAIN_INVALID";
    case SAN9_S5_CURRENT_CONTEXT_CONTROLLER_NOT_UNIQUE:
        return "CONTROLLER_NOT_UNIQUE";
    case SAN9_S5_CURRENT_CONTEXT_CONTROLLER_NOT_IDLE:
        return "CONTROLLER_NOT_IDLE";
    case SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID:
        return "CURRENT_CITY_INVALID";
    case SAN9_S5_CURRENT_CONTEXT_CITY_INVALID:
        return "CITY_INVALID";
    case SAN9_S5_CURRENT_CONTEXT_CORPS_INVALID:
        return "CORPS_INVALID";
    case SAN9_S5_CURRENT_CONTEXT_CITY_NOT_DIRECT:
        return "CITY_NOT_DIRECT";
    case SAN9_S5_CURRENT_CONTEXT_MONEY_INSUFFICIENT:
        return "MONEY_INSUFFICIENT";
    case SAN9_S5_CURRENT_CONTEXT_COMMERCE_COMPLETE:
        return "COMMERCE_COMPLETE";
    case SAN9_S5_CURRENT_CONTEXT_ORDER_CONFLICT:
        return "ORDER_CONFLICT";
    case SAN9_S5_CURRENT_CONTEXT_RESIDENT_LIST_INVALID:
        return "RESIDENT_LIST_INVALID";
    case SAN9_S5_CURRENT_CONTEXT_PERSON_INVALID:
        return "PERSON_INVALID";
    case SAN9_S5_CURRENT_CONTEXT_READY_TOP5_UNAVAILABLE:
        return "READY_TOP5_UNAVAILABLE";
    case SAN9_S5_CURRENT_CONTEXT_DIGEST_FAILED:
        return "DIGEST_FAILED";
    case SAN9_S5_CURRENT_CONTEXT_AB_MISMATCH:
        return "AB_MISMATCH";
    case SAN9_S5_CURRENT_CONTEXT_FROZEN_POST_INVALID:
        return "FROZEN_POST_INVALID";
    case SAN9_S5_CURRENT_CONTEXT_BUSINESS_DRIFT:
        return "BUSINESS_DRIFT";
    case SAN9_S6_CURRENT_CONTEXT_CULTIVATE_COMPLETE:
        return "CULTIVATE_COMPLETE";
    case SAN9_S6_CURRENT_CONTEXT_PATROL_COMPLETE:
        return "PATROL_COMPLETE";
    case SAN9_S6_CURRENT_CONTEXT_TRAIN_COMPLETE:
        return "TRAIN_COMPLETE";
    case SAN9_S6_CURRENT_CONTEXT_TRAIN_NO_TROOPS:
        return "TRAIN_NO_TROOPS";
    case SAN9_S6_CURRENT_CONTEXT_REPAIR_COMPLETE:
        return "REPAIR_COMPLETE";
    default:
        return "UNKNOWN";
    }
}
