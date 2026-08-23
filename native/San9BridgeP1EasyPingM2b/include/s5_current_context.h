#ifndef SAN9_S5_CURRENT_CONTEXT_H
#define SAN9_S5_CURRENT_CONTEXT_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32) && !defined(SAN9_S5_CONTEXT_NO_HANDLE)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define SAN9_S5_CURRENT_CONTEXT_SCHEMA_MAJOR 1u
#define SAN9_S5_CURRENT_CONTEXT_SCHEMA_MINOR 0u
#define SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT 5u
#define SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE 32u
#define SAN9_S5_CURRENT_CONTEXT_MAX_TASKS 64u
#define SAN9_S5_CURRENT_CONTEXT_MAX_RESIDENTS 850u
#define SAN9_S5_CURRENT_CONTEXT_TARGET_MUTATION_COUNT 0u

typedef enum San9S5CurrentContextStatus {
    SAN9_S5_CURRENT_CONTEXT_OK = 0,
    SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT = 1,
    SAN9_S5_CURRENT_CONTEXT_BINDING_MISMATCH = 2,
    SAN9_S5_CURRENT_CONTEXT_READ_FAILED = 3,
    SAN9_S5_CURRENT_CONTEXT_APP_CHAIN_INVALID = 4,
    SAN9_S5_CURRENT_CONTEXT_TASK_CHAIN_INVALID = 5,
    SAN9_S5_CURRENT_CONTEXT_CONTROLLER_NOT_UNIQUE = 6,
    SAN9_S5_CURRENT_CONTEXT_CONTROLLER_NOT_IDLE = 7,
    SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID = 8,
    SAN9_S5_CURRENT_CONTEXT_CITY_INVALID = 9,
    SAN9_S5_CURRENT_CONTEXT_CORPS_INVALID = 10,
    SAN9_S5_CURRENT_CONTEXT_CITY_NOT_DIRECT = 11,
    SAN9_S5_CURRENT_CONTEXT_MONEY_INSUFFICIENT = 12,
    SAN9_S5_CURRENT_CONTEXT_COMMERCE_COMPLETE = 13,
    SAN9_S5_CURRENT_CONTEXT_ORDER_CONFLICT = 14,
    SAN9_S5_CURRENT_CONTEXT_RESIDENT_LIST_INVALID = 15,
    SAN9_S5_CURRENT_CONTEXT_PERSON_INVALID = 16,
    SAN9_S5_CURRENT_CONTEXT_READY_TOP5_UNAVAILABLE = 17,
    SAN9_S5_CURRENT_CONTEXT_DIGEST_FAILED = 18,
    SAN9_S5_CURRENT_CONTEXT_AB_MISMATCH = 19,
    SAN9_S5_CURRENT_CONTEXT_FROZEN_POST_INVALID = 20,
    SAN9_S5_CURRENT_CONTEXT_BUSINESS_DRIFT = 21,
    SAN9_S6_CURRENT_CONTEXT_CULTIVATE_COMPLETE = 22,
    SAN9_S6_CURRENT_CONTEXT_PATROL_COMPLETE = 23,
    SAN9_S6_CURRENT_CONTEXT_TRAIN_COMPLETE = 24,
    SAN9_S6_CURRENT_CONTEXT_TRAIN_NO_TROOPS = 25,
    SAN9_S6_CURRENT_CONTEXT_REPAIR_COMPLETE = 26
} San9S5CurrentContextStatus;

/* The generation is the process creation FILETIME represented as uint64. */
typedef struct San9S5ExpectedIdentity {
    uint32_t process_id;
    uint32_t main_thread_id;
    uint64_t process_generation;
    uint64_t window_handle;
} San9S5ExpectedIdentity;

/* Cross-step batch binding.  It is an internal read constraint, not wire
   state and never authorizes writing controller+0x38. */
typedef struct San9S5BoundCurrentCity {
    uint32_t controller_pointer;
    uint32_t city_pointer;
    uint32_t corps_pointer;
} San9S5BoundCurrentCity;

typedef int (*San9S5CurrentContextReadCallback)(
    void *context,
    uint32_t address,
    void *output,
    size_t output_size);

typedef struct San9S5CurrentContextReader {
    San9S5CurrentContextReadCallback read;
    void *context;
} San9S5CurrentContextReader;

typedef struct San9S5CommerceOfficer {
    uint32_t person_id;
    uint32_t person_pointer;
    uint32_t source_list_index;
    uint32_t effective_politics;
    uint32_t identity;
    uint32_t ready_flags;
    uint32_t residence_pointer;
} San9S5CommerceOfficer;

typedef struct San9S5CurrentContextSnapshot {
    uint32_t structure_size;
    uint32_t schema_major;
    uint32_t schema_minor;
    San9S5ExpectedIdentity binding;

    uint32_t app_pointer;
    uint32_t app_vtable;
    uint32_t window_pointer;
    uint32_t owner_pointer;
    uint32_t scene_pointer;
    uint32_t scheduler_pointer;
    uint32_t task_count;
    uint32_t controller_depth;
    uint32_t controller_pointer;
    uint32_t controller_vtable;
    uint32_t controller_child;
    uint32_t controller_pending;
    uint32_t controller_corps;
    uint32_t controller_state;
    uint32_t controller_target;
    uint32_t current_city_pointer;

    uint32_t city_id;
    uint32_t city_pointer;
    uint32_t city_vtable;
    uint32_t city_type;
    uint32_t city_self_pointer;
    uint32_t city_corps_pointer;
    uint32_t city_residence_pointer;
    uint32_t city_residence_vtable;

    uint32_t corps_id;
    uint32_t corps_pointer;
    uint32_t corps_flags;
    uint32_t corps_money;
    uint32_t corps_main_pointer;
    uint32_t corps_main_id;
    uint32_t corps_leader_pointer;
    uint32_t corps_leader_id;
    uint32_t main_corps_flags;
    uint32_t direct_controlled;

    uint32_t native_command_id;
    uint32_t commerce_current;
    uint32_t commerce_maximum;
    uint32_t cultivate_current;
    uint32_t cultivate_maximum;
    uint32_t patrol_current;
    uint32_t patrol_maximum;
    uint32_t order_flags;
    uint32_t train_troops;
    uint32_t train_morale;
    uint32_t train_maximum;
    uint32_t train_order_flags;
    uint32_t repair_current;
    uint32_t repair_maximum;
    uint32_t repair_order_flags;

    uint32_t resident_first;
    uint32_t resident_last;
    uint32_t resident_count;
    uint32_t ready_count;
    uint32_t exact_top5_count;
    San9S5CommerceOfficer top5[SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT];

    uint8_t task_chain_digest[SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE];
    uint8_t resident_digest[SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE];
    uint8_t canonical_digest[SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE];
} San9S5CurrentContextSnapshot;

/* Post-event proof deliberately follows only pointers frozen by a successful
   pre-capture.  It never rediscovers a target through controller+0x38, which
   is expected to be zero after the native handler exits. */
typedef struct San9S5FrozenPostSnapshot {
    uint32_t structure_size;
    uint32_t schema_major;
    uint32_t schema_minor;
    San9S5ExpectedIdentity binding;
    uint32_t controller_pointer;
    uint32_t controller_vtable;
    uint32_t controller_child;
    uint32_t controller_pending;
    uint32_t controller_corps;
    uint32_t controller_state;
    uint32_t controller_target;
    uint32_t city_id;
    uint32_t city_pointer;
    uint32_t city_vtable;
    uint32_t city_type;
    uint32_t city_self_pointer;
    uint32_t city_corps_pointer;
    uint32_t city_residence_pointer;
    uint32_t city_residence_vtable;
    uint32_t corps_id;
    uint32_t corps_pointer;
    uint32_t corps_flags;
    uint32_t corps_money;
    uint32_t corps_main_pointer;
    uint32_t corps_main_id;
    uint32_t corps_leader_pointer;
    uint32_t corps_leader_id;
    uint32_t main_corps_flags;
    uint32_t native_command_id;
    uint32_t commerce_current;
    uint32_t commerce_maximum;
    uint32_t cultivate_current;
    uint32_t cultivate_maximum;
    uint32_t patrol_current;
    uint32_t patrol_maximum;
    uint32_t order_flags;
    uint32_t train_troops;
    uint32_t train_morale;
    uint32_t train_maximum;
    uint32_t train_order_flags;
    uint32_t repair_current;
    uint32_t repair_maximum;
    uint32_t repair_order_flags;
    uint32_t resident_first;
    uint32_t resident_last;
    uint32_t resident_count;
    uint32_t exact_top5_count;
    San9S5CommerceOfficer top5[SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT];
    uint8_t business_digest[SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE];
} San9S5FrozenPostSnapshot;

/* Pure parser: the callback supplies exact bytes and owns its read context.
   The identity is already trusted by this layer and is bound into the result. */
San9S5CurrentContextStatus san9_s5_current_context_capture_reader(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *output);

San9S5CurrentContextStatus san9_s5_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

San9S5CurrentContextStatus san9_s6_cultivate_current_context_capture_reader(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *output);

San9S5CurrentContextStatus san9_s6_cultivate_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

San9S5CurrentContextStatus san9_s6_patrol_current_context_capture_reader(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *output);

San9S5CurrentContextStatus san9_s6_patrol_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

San9S5CurrentContextStatus san9_s6_train_current_context_capture_reader(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *output);

San9S5CurrentContextStatus san9_s6_train_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

San9S5CurrentContextStatus san9_s6_repair_current_context_capture_reader(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *output);

San9S5CurrentContextStatus san9_s6_repair_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

/* A bound capture substitutes the frozen city only in its local read view
   when the observed controller target is zero.  A non-zero foreign target,
   controller drift, or corps drift returns CURRENT_CITY_INVALID. */
San9S5CurrentContextStatus san9_s5_bound_current_city_normalize(
    const San9S5BoundCurrentCity *bound,
    uint32_t observed_controller_pointer,
    uint32_t observed_controller_corps,
    uint32_t observed_controller_target,
    uint32_t *normalized_city_pointer);

San9S5CurrentContextStatus san9_s5_bound_current_context_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    const San9S5BoundCurrentCity *bound,
    uint32_t native_command_id,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

int san9_s5_current_context_business_digest(
    const San9S5CurrentContextSnapshot *snapshot,
    uint8_t output[SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE]);

San9S5CurrentContextStatus san9_s5_frozen_post_capture_reader(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    const San9S5CurrentContextSnapshot *pre,
    San9S5FrozenPostSnapshot *output);

San9S5CurrentContextStatus san9_s5_frozen_post_capture_reader_ab(
    const San9S5CurrentContextReader *reader,
    const San9S5ExpectedIdentity *identity,
    const San9S5CurrentContextSnapshot *pre,
    San9S5FrozenPostSnapshot *first,
    San9S5FrozenPostSnapshot *second);

int san9_s5_frozen_post_equal(
    const San9S5FrozenPostSnapshot *first,
    const San9S5FrozenPostSnapshot *second);

#if defined(_WIN32) && !defined(SAN9_S5_CONTEXT_NO_HANDLE)
/* Controller wrapper: it verifies PID, creation generation, main TID and HWND
   before and after every capture.  It does not discover or open a process. */
San9S5CurrentContextStatus san9_s5_current_context_capture_handle(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *output);

San9S5CurrentContextStatus san9_s5_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

San9S5CurrentContextStatus san9_s6_cultivate_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

San9S5CurrentContextStatus san9_s6_patrol_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

San9S5CurrentContextStatus san9_s6_train_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

San9S5CurrentContextStatus san9_s6_repair_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);

San9S5CurrentContextStatus san9_s5_bound_current_context_capture_handle_ab(
    HANDLE process,
    uint32_t expected_process_id,
    uint64_t expected_process_generation,
    uint32_t expected_main_thread_id,
    HWND expected_window,
    const San9S5BoundCurrentCity *bound,
    uint32_t native_command_id,
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second);
#endif

int san9_s5_current_context_digest(
    const San9S5CurrentContextSnapshot *snapshot,
    uint8_t output[SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE]);

int san9_s5_current_context_equal(
    const San9S5CurrentContextSnapshot *first,
    const San9S5CurrentContextSnapshot *second);

const char *san9_s5_current_context_status_name(
    San9S5CurrentContextStatus status);

#ifdef __cplusplus
}
#endif

#endif
