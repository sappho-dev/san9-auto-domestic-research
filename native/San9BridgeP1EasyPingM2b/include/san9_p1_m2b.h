#ifndef SAN9_P1_M2B_H
#define SAN9_P1_M2B_H

#include <stddef.h>
#include <stdint.h>

#include "san9_p1_m2.h"
#include "s3_readonly.h"
#include "s5_commerce.h"
#include "s5_current_context.h"

#ifdef __cplusplus
extern "C" {
#endif

#define SAN9_P1_M2B_SHARED_MAGIC UINT32_C(0x42325031)
#define SAN9_P1_M2B_CONTRACT_MAGIC UINT32_C(0x43425031)
#define SAN9_P1_M2B_CONFIG_MAGIC UINT32_C(0x47425031)
#define SAN9_P1_M2B_SCHEMA_MAJOR 1u
#define SAN9_P1_M2B_SCHEMA_MINOR 11u
#define SAN9_P1_M2B_BOOTSTRAP_SIZE 4096u
#define SAN9_P1_M2B_SHARED_SIZE 8192u
#define SAN9_P1_M2B_PING_COUNT 100u
#define SAN9_P1_M2B_PROBE0_MIN_TICKS 3u
#define SAN9_P1_M2B_CONFIRM_WORD "I_ACCEPT_PROBE0_PINNED_UNTIL_GAME_RESTART"
#define SAN9_P1_M2B_PING_CONFIRM_WORD "I_ACCEPT_PING_ONLY_PINNED_UNTIL_GAME_RESTART"
#define SAN9_P1_M2B_OBSERVE_CONFIRM_WORD "I_ACCEPT_S3_READONLY_OBSERVATION_PINNED_UNTIL_GAME_RESTART"
#define SAN9_P1_M2B_S5_NO_APPLY_CONFIRM_WORD "I_ACCEPT_S5_NO_APPLY_TIMING_PINNED_UNTIL_GAME_RESTART"
#define SAN9_P1_M2B_S5_MODAL_PROBE_CONFIRM_WORD "I_ACCEPT_S5_MODAL_PROBE_PINNED_UNTIL_GAME_RESTART"
#define SAN9_P1_M2B_S5_APPLY_ONCE_CONFIRM_WORD "I_ACCEPT_ONE_CURRENT_CITY_COMMERCE_APPLY_PINNED_UNTIL_GAME_RESTART"
#define SAN9_P1_M2B_S6_CULTIVATE_APPLY_ONCE_CONFIRM_WORD "I_ACCEPT_ONE_CURRENT_CITY_CULTIVATE_APPLY_PINNED_UNTIL_GAME_RESTART"
#define SAN9_P1_M2B_S6_PATROL_APPLY_ONCE_CONFIRM_WORD "I_ACCEPT_ONE_CURRENT_CITY_PATROL_APPLY_PINNED_UNTIL_GAME_RESTART"
#define SAN9_P1_M2B_S6_TRAIN_APPLY_ONCE_CONFIRM_WORD "I_ACCEPT_ONE_CURRENT_CITY_TRAIN_APPLY_PINNED_UNTIL_GAME_RESTART"
#define SAN9_P1_M2B_S6_REPAIR_APPLY_ONCE_CONFIRM_WORD "I_ACCEPT_ONE_CURRENT_CITY_REPAIR_APPLY_PINNED_UNTIL_GAME_RESTART"
#define SAN9_P1_M2B_S8_BASIC_BATCH_CONFIRM_WORD "I_ACCEPT_BASIC_BATCH_COMMERCE_CULTIVATE_PINNED_UNTIL_GAME_RESTART"
#define SAN9_P1_M2B_S8_WEALTHY_BATCH_CONFIRM_WORD "I_ACCEPT_WEALTHY_BATCH_PATROL_COMMERCE_CULTIVATE_TRAIN_REPAIR_PINNED_UNTIL_GAME_RESTART"
#define SAN9_P1_M2B_S5_MODAL_PROBE_SIGNAL "CURRENT_CITY_MENU_READY"
#define SAN9_P1_M2B_OBSERVE_TIMEOUT_MS 180000u
#define SAN9_P1_M2B_LIVE_EXECUTION_DEFAULT 0u
#define SAN9_P1_M2B_UI_CONNECTED 0u
#define SAN9_P1_M2B_COMMERCE_ABI_READY 1u
#if (defined(S5_APPLY_ONCE_BUILD) && S5_APPLY_ONCE_BUILD) \
    || (defined(S8_BASIC_BATCH_BUILD) && S8_BASIC_BATCH_BUILD) \
    || (defined(S8_WEALTHY_BATCH_BUILD) && S8_WEALTHY_BATCH_BUILD)
#define SAN9_P1_M2B_COMMERCE_LIVE_AUTHORIZATION 1u
#define SAN9_P1_M2B_COMMERCE_COMMAND_CONSTRUCTION_READY 1u
#else
#define SAN9_P1_M2B_COMMERCE_LIVE_AUTHORIZATION 0u
#define SAN9_P1_M2B_COMMERCE_COMMAND_CONSTRUCTION_READY 0u
#endif
#if (defined(S6_CULTIVATE_APPLY_ONCE_BUILD) \
        && S6_CULTIVATE_APPLY_ONCE_BUILD) \
    || (defined(S8_BASIC_BATCH_BUILD) && S8_BASIC_BATCH_BUILD) \
    || (defined(S8_WEALTHY_BATCH_BUILD) && S8_WEALTHY_BATCH_BUILD)
#define SAN9_P1_M2B_CULTIVATE_LIVE_AUTHORIZATION 1u
#define SAN9_P1_M2B_CULTIVATE_COMMAND_CONSTRUCTION_READY 1u
#else
#define SAN9_P1_M2B_CULTIVATE_LIVE_AUTHORIZATION 0u
#define SAN9_P1_M2B_CULTIVATE_COMMAND_CONSTRUCTION_READY 0u
#endif
#if (defined(S6_PATROL_APPLY_ONCE_BUILD) && S6_PATROL_APPLY_ONCE_BUILD) \
    || (defined(S8_WEALTHY_BATCH_BUILD) && S8_WEALTHY_BATCH_BUILD)
#define SAN9_P1_M2B_PATROL_LIVE_AUTHORIZATION 1u
#define SAN9_P1_M2B_PATROL_COMMAND_CONSTRUCTION_READY 1u
#else
#define SAN9_P1_M2B_PATROL_LIVE_AUTHORIZATION 0u
#define SAN9_P1_M2B_PATROL_COMMAND_CONSTRUCTION_READY 0u
#endif
#if (defined(S6_TRAIN_APPLY_ONCE_BUILD) && S6_TRAIN_APPLY_ONCE_BUILD) \
    || (defined(S8_WEALTHY_BATCH_BUILD) && S8_WEALTHY_BATCH_BUILD)
#define SAN9_P1_M2B_TRAIN_LIVE_AUTHORIZATION 1u
#define SAN9_P1_M2B_TRAIN_COMMAND_CONSTRUCTION_READY 1u
#else
#define SAN9_P1_M2B_TRAIN_LIVE_AUTHORIZATION 0u
#define SAN9_P1_M2B_TRAIN_COMMAND_CONSTRUCTION_READY 0u
#endif
#if (defined(S6_REPAIR_APPLY_ONCE_BUILD) && S6_REPAIR_APPLY_ONCE_BUILD) \
    || (defined(S8_WEALTHY_BATCH_BUILD) && S8_WEALTHY_BATCH_BUILD)
#define SAN9_P1_M2B_REPAIR_LIVE_AUTHORIZATION 1u
#define SAN9_P1_M2B_REPAIR_COMMAND_CONSTRUCTION_READY 1u
#else
#define SAN9_P1_M2B_REPAIR_LIVE_AUTHORIZATION 0u
#define SAN9_P1_M2B_REPAIR_COMMAND_CONSTRUCTION_READY 0u
#endif
#define SAN9_P1_M2B_HAS_FIVE_DOMESTIC_COMMANDS 0u
#define SAN9_P1_M2B_HAS_ALL_CITY_LOOP 0u
#define SAN9_P1_M2B_HAS_HOT_UNLOAD 0u
#define SAN9_P1_M2B_S5_MODAL_PROBE_PROOF_FROZEN 0u
#define SAN9_P1_M2B_S5_MODAL_WAKE_AUTHORIZATION 0u
#define SAN9_P1_M2B_S5_MODAL_VTABLE_BUILD 1u
#define SAN9_P1_M2B_S5_MODAL_ROOT_EVENT_CALLS 0u
#define SAN9_P1_M2B_S5_MODAL_PUBLISH_CALLS 0u
#define SAN9_P1_M2B_S5_MODAL_APPLY_CALLS 0u
#define SAN9_P1_M2B_S5_V8_MENU_EVENT_CALLS 1u
#define SAN9_P1_M2B_S5_V8_APPLY_CALLS 0u
#define SAN9_P1_M2B_S5_SETTLE_TIMEOUT_MS UINT64_C(3000)
#define SAN9_P1_M2B_S5_APPLY_COMPLETION_TIMEOUT_MS UINT64_C(15000)

#define SAN9_P1_M2B_EXACT_APP_OBJECT UINT32_C(0x01228340)
#define SAN9_P1_M2B_EXACT_APP_VTABLE UINT32_C(0x00604DD0)
#define SAN9_P1_M2B_EXACT_IDLE_SLOT UINT32_C(0x00604DF4)
#define SAN9_P1_M2B_EXACT_ORIGINAL_IDLE UINT32_C(0x00434100)
#define SAN9_P1_M2B_EXACT_IDLE_CALLER UINT32_C(0x005C5D11)
#define SAN9_P1_M2B_MODAL_TOP_HWND UINT32_C(0x005CA7D0)
#define SAN9_P1_M2B_MODAL_PERMANENT_CWND UINT32_C(0x005CD460)
#define SAN9_P1_M2B_DOMESTIC_MENU_VPTR UINT32_C(0x0060A238)
#define SAN9_P1_M2B_DOMESTIC_MENU_TICK_SLOT UINT32_C(0x0060A2DC)
#define SAN9_P1_M2B_DOMESTIC_MENU_TICK_ORIGINAL UINT32_C(0x004175C0)
#define SAN9_P1_M2B_DOMESTIC_MENU_TICK_CALLER UINT32_C(0x005B67C3)
#define SAN9_P1_M2B_MODAL_STACK_COUNT UINT32_C(0x01B41890)
#define SAN9_P1_M2B_DOMESTIC_MENU_VTABLE_SIZE 0x104u
#define SAN9_P1_M2B_DOMESTIC_MENU_TICK_OFFSET 0xA4u
#define SAN9_P1_M2B_S5_MODAL_PROBE_TICKS 3u

/* Static reverse-engineering candidates only.  They are deliberately not
   callable while COMMERCE_ABI_READY is zero. */
#define SAN9_P1_M2B_COMMERCE_HANDLER UINT32_C(0x004C61F0)
#define SAN9_P1_M2B_COMMERCE_HANDLER_VPTR UINT32_C(0x00609F38)
#define SAN9_P1_M2B_COMMERCE_CAN_EXECUTE UINT32_C(0x004C6400)
#define SAN9_P1_M2B_PERSON_LIST_CTOR UINT32_C(0x00470DF0)
#define SAN9_P1_M2B_PERSON_LIST_DTOR UINT32_C(0x00470E10)
#define SAN9_P1_M2B_COPY_FIRST_N UINT32_C(0x0046EF80)
#define SAN9_P1_M2B_PERSON_LIST_SORT_FIELD UINT32_C(0x0046F640)
#define SAN9_P1_M2B_PERSON_LIST_SORT_KEY UINT32_C(0x0046F610)
#define SAN9_P1_M2B_COMMERCE_PERSON_KEY UINT32_C(0x00471D80)
#define SAN9_P1_M2B_ALLOC UINT32_C(0x005DEC20)
#define SAN9_P1_M2B_COMMAND_CTOR UINT32_C(0x0048B340)
#define SAN9_P1_M2B_COMMAND_VPTR UINT32_C(0x00607D98)
#define SAN9_P1_M2B_COMMAND_VALIDATE UINT32_C(0x0047E510)
#define SAN9_P1_M2B_COMMAND_ATTACH UINT32_C(0x0047E6F0)
#define SAN9_P1_M2B_HANDLER_DRIVER UINT32_C(0x0050EAB0)
#define SAN9_P1_M2B_COMMERCE_NATIVE_APPLY UINT32_C(0x0048B5D0)
#define SAN9_P1_M2B_COMMAND_ALLOCATION_SIZE 0x40u
#define SAN9_P1_M2B_SELECTION_LIMIT 5u
#define SAN9_P1_M2B_COMMERCE_NATIVE_ID 1u
#define SAN9_P1_M2B_COMMERCE_EVENT_ID UINT32_C(0x2711)
#define SAN9_P1_M2B_CULTIVATE_HANDLER UINT32_C(0x004C1D40)
#define SAN9_P1_M2B_CULTIVATE_HANDLER_VPTR UINT32_C(0x00609BF0)
#define SAN9_P1_M2B_CULTIVATE_CAN_EXECUTE UINT32_C(0x004C1F50)
#define SAN9_P1_M2B_CULTIVATE_EXECUTE UINT32_C(0x004C1E60)
#define SAN9_P1_M2B_CULTIVATE_OUTER_VPTR UINT32_C(0x0060BBA0)
#define SAN9_P1_M2B_CULTIVATE_OUTER_EVENT UINT32_C(0x004DA670)
#define SAN9_P1_M2B_CULTIVATE_COMMAND_CTOR UINT32_C(0x00488B00)
#define SAN9_P1_M2B_CULTIVATE_COMMAND_VPTR UINT32_C(0x00607B30)
#define SAN9_P1_M2B_CULTIVATE_NATIVE_APPLY UINT32_C(0x00488D90)
#define SAN9_P1_M2B_CULTIVATE_NATIVE_ID 2u
#define SAN9_P1_M2B_CULTIVATE_EVENT_ID UINT32_C(0x2712)
#define SAN9_P1_M2B_CULTIVATE_CURRENT_OFFSET 0x1D0u
#define SAN9_P1_M2B_CULTIVATE_MAXIMUM_OFFSET 0x1D8u
#define SAN9_P1_M2B_CULTIVATE_ORDER_FLAG UINT32_C(0x20)
#define SAN9_P1_M2B_CULTIVATE_COST UINT32_C(250)
#define SAN9_P1_M2B_PATROL_HANDLER UINT32_C(0x004C1720)
#define SAN9_P1_M2B_PATROL_HANDLER_VPTR UINT32_C(0x00609B90)
#define SAN9_P1_M2B_PATROL_CAN_EXECUTE UINT32_C(0x004C1930)
#define SAN9_P1_M2B_PATROL_EXECUTE UINT32_C(0x004C1840)
#define SAN9_P1_M2B_PATROL_OUTER_VPTR UINT32_C(0x0060B920)
#define SAN9_P1_M2B_PATROL_OUTER_EVENT UINT32_C(0x004D8BA0)
#define SAN9_P1_M2B_PATROL_COMMAND_CTOR UINT32_C(0x00488410)
#define SAN9_P1_M2B_PATROL_COMMAND_VPTR UINT32_C(0x00607A80)
#define SAN9_P1_M2B_PATROL_COMMAND_VALIDATE UINT32_C(0x00488580)
#define SAN9_P1_M2B_PATROL_NATIVE_APPLY UINT32_C(0x00488690)
#define SAN9_P1_M2B_PATROL_NATIVE_ID 0u
#define SAN9_P1_M2B_PATROL_EVENT_ID UINT32_C(0x2710)
#define SAN9_P1_M2B_PATROL_CURRENT_OFFSET 0x1C4u
#define SAN9_P1_M2B_PATROL_MAXIMUM UINT32_C(1000)
#define SAN9_P1_M2B_PATROL_ORDER_FLAG UINT32_C(0x08)
#define SAN9_P1_M2B_PATROL_COST UINT32_C(250)
#define SAN9_P1_M2B_PATROL_ABILITY_OFFSET 0x58u
#define SAN9_P1_M2B_TRAIN_HANDLER UINT32_C(0x004C35F0)
#define SAN9_P1_M2B_TRAIN_HANDLER_VPTR UINT32_C(0x00609CE0)
#define SAN9_P1_M2B_TRAIN_CAN_EXECUTE UINT32_C(0x004C3890)
#define SAN9_P1_M2B_TRAIN_EXECUTE UINT32_C(0x004C37A0)
#define SAN9_P1_M2B_TRAIN_COMMAND_CTOR UINT32_C(0x00489D80)
#define SAN9_P1_M2B_TRAIN_COMMAND_VPTR UINT32_C(0x00607C38)
#define SAN9_P1_M2B_TRAIN_COMMAND_VALIDATE UINT32_C(0x00489F10)
#define SAN9_P1_M2B_TRAIN_NATIVE_APPLY UINT32_C(0x00489FF0)
#define SAN9_P1_M2B_TRAIN_NATIVE_ID 5u
#define SAN9_P1_M2B_TRAIN_EVENT_ID UINT32_C(0x2715)
#define SAN9_P1_M2B_TRAIN_TROOPS_OFFSET 0x88u
#define SAN9_P1_M2B_TRAIN_CURRENT_OFFSET 0x90u
#define SAN9_P1_M2B_TRAIN_MAXIMUM UINT32_C(100)
#define SAN9_P1_M2B_TRAIN_ORDER_OFFSET 0x3Eu
#define SAN9_P1_M2B_TRAIN_ORDER_FLAG UINT32_C(0x02)
#define SAN9_P1_M2B_TRAIN_COST UINT32_C(0)
#define SAN9_P1_M2B_TRAIN_ABILITY_OFFSET 0x50u
#define SAN9_P1_M2B_REPAIR_HANDLER UINT32_C(0x004C2060)
#define SAN9_P1_M2B_REPAIR_HANDLER_VPTR UINT32_C(0x00609C20)
#define SAN9_P1_M2B_REPAIR_CAN_EXECUTE UINT32_C(0x004C2270)
#define SAN9_P1_M2B_REPAIR_EXECUTE UINT32_C(0x004C2180)
#define SAN9_P1_M2B_REPAIR_COMMAND_CTOR UINT32_C(0x00489080)
#define SAN9_P1_M2B_REPAIR_COMMAND_VPTR UINT32_C(0x00607B88)
#define SAN9_P1_M2B_REPAIR_COMMAND_VALIDATE UINT32_C(0x004890E0)
#define SAN9_P1_M2B_REPAIR_NATIVE_APPLY UINT32_C(0x00489310)
#define SAN9_P1_M2B_REPAIR_NATIVE_ID 3u
#define SAN9_P1_M2B_REPAIR_EVENT_ID UINT32_C(0x2713)
#define SAN9_P1_M2B_REPAIR_CURRENT_OFFSET 0x3Cu
#define SAN9_P1_M2B_REPAIR_MAXIMUM_OFFSET 0x1C8u
#define SAN9_P1_M2B_REPAIR_ORDER_OFFSET 0x3Eu
#define SAN9_P1_M2B_REPAIR_ORDER_FLAG UINT32_C(0x01)
#define SAN9_P1_M2B_REPAIR_COST UINT32_C(250)
#define SAN9_P1_M2B_REPAIR_ABILITY_OFFSET 0x68u
#define SAN9_P1_M2B_ROOT_HANDLER_OFFSET 0x10u
#define SAN9_P1_M2B_HANDLER_SELECTION_OFFSET 0x40u
#define SAN9_P1_M2B_COMMERCE_VTABLE_SLOT_COUNT 12u
#define SAN9_P1_M2B_COMMERCE_VTABLE_SIZE 0x30u
#define SAN9_P1_M2B_COMMERCE_EXECUTE_SLOT_OFFSET 0x28u
#define SAN9_P1_M2B_COMMAND_VTABLE_SLOT_COUNT 22u
#define SAN9_P1_M2B_COMMAND_VTABLE_SIZE 0x58u
#define SAN9_P1_M2B_COMMAND_APPLY_SLOT_OFFSET 0x30u
#define SAN9_P1_M2B_PERSON_LIST_SIZE 0x20u

#if defined(SAN9_P1_M2B_EXPORTS_VIA_DEF)
#define SAN9_P1_M2B_EXPORT
#define SAN9_P1_M2B_STDCALL __stdcall
#define SAN9_P1_M2B_FASTCALL __fastcall
#elif defined(_WIN32)
#define SAN9_P1_M2B_EXPORT __declspec(dllexport)
#define SAN9_P1_M2B_STDCALL __stdcall
#define SAN9_P1_M2B_FASTCALL __fastcall
#else
#define SAN9_P1_M2B_EXPORT
#define SAN9_P1_M2B_STDCALL
#define SAN9_P1_M2B_FASTCALL
#endif

typedef enum San9P1M2bBootstrapState {
    SAN9_P1_M2B_BOOTSTRAP_EMPTY = 0,
    SAN9_P1_M2B_BOOTSTRAP_WRITING = 1,
    SAN9_P1_M2B_BOOTSTRAP_SEALED = 2,
    SAN9_P1_M2B_BOOTSTRAP_CLAIMED = 3,
    SAN9_P1_M2B_BOOTSTRAP_COMMITTING = 4,
    SAN9_P1_M2B_BOOTSTRAP_READY = 5,
    SAN9_P1_M2B_BOOTSTRAP_QUIESCENT_PINNED = 6,
    SAN9_P1_M2B_BOOTSTRAP_REJECTED = 7,
    SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART = 8
} San9P1M2bBootstrapState;

typedef enum San9P1M2bResult {
    SAN9_P1_M2B_OK = 0,
    SAN9_P1_M2B_INVALID = 1,
    SAN9_P1_M2B_AUTH_REJECTED = 2,
    SAN9_P1_M2B_ACL_FAILED = 3,
    SAN9_P1_M2B_MAPPING_FAILED = 4,
    SAN9_P1_M2B_HOOK_FAILED = 5,
    SAN9_P1_M2B_EASY_REJECTED = 6,
    SAN9_P1_M2B_SLOT_CONFLICT = 7,
    SAN9_P1_M2B_PING_FAILED = 8,
    SAN9_P1_M2B_TIMEOUT = 9,
    SAN9_P1_M2B_RESTART_REQUIRED = 10,
    SAN9_P1_M2B_COMMERCE_ABI_PENDING = 11,
    SAN9_P1_M2B_DISCOVERY_FAILED = 12,
    SAN9_P1_M2B_PROBE0_FAILED = 13,
    SAN9_P1_M2B_OBSERVE_FAILED = 14,
    SAN9_P1_M2B_S5_NO_APPLY_FAILED = 15,
    SAN9_P1_M2B_S5_MODAL_PROBE_FAILED = 16,
    SAN9_P1_M2B_S5_APPLY_ONCE_FAILED = 17,
    SAN9_P1_M2B_S6_CULTIVATE_APPLY_ONCE_FAILED = 18,
    SAN9_P1_M2B_S6_PATROL_APPLY_ONCE_FAILED = 19,
    SAN9_P1_M2B_S6_TRAIN_APPLY_ONCE_FAILED = 20,
    SAN9_P1_M2B_S6_REPAIR_APPLY_ONCE_FAILED = 21,
    SAN9_P1_M2B_S8_BASIC_BATCH_FAILED = 22,
    SAN9_P1_M2B_BATCH_REBIND_REQUIRED = 23,
    SAN9_P1_M2B_S8_WEALTHY_BATCH_FAILED = 24
} San9P1M2bResult;

typedef enum San9P1M2bOperationMode {
    SAN9_P1_M2B_OPERATION_NONE = 0,
    SAN9_P1_M2B_OPERATION_PROBE0 = 1,
    SAN9_P1_M2B_OPERATION_PING = 2,
    SAN9_P1_M2B_OPERATION_OBSERVE = 3,
    SAN9_P1_M2B_OPERATION_S5_NO_APPLY = 4,
    SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE = 5,
    SAN9_P1_M2B_OPERATION_S5_APPLY_ONCE = 6,
    SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE = 7,
    SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE = 8,
    SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE = 9,
    SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE = 10,
    SAN9_P1_M2B_OPERATION_S8_BASIC_BATCH = 11,
    SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH = 12
} San9P1M2bOperationMode;

typedef enum San9P1S5ModalProbeState {
    SAN9_P1_S5_MODAL_EMPTY = 0,
    SAN9_P1_S5_MODAL_ARMED = 1,
    SAN9_P1_S5_MODAL_WAKE_CLAIMED = 2,
    SAN9_P1_S5_MODAL_SHADOW_ARMED = 3,
    SAN9_P1_S5_MODAL_CAPTURING = 4,
    SAN9_P1_S5_MODAL_COMPLETE = 5,
    SAN9_P1_S5_MODAL_REJECTED = 6
} San9P1S5ModalProbeState;

typedef enum San9P1S5MenuHandoffState {
    SAN9_P1_S5_MENU_EMPTY = 0,
    SAN9_P1_S5_MENU_ARMED = 1,
    SAN9_P1_S5_MENU_WAKE_CLAIMED = 2,
    SAN9_P1_S5_MENU_SHADOW_ARMED = 3,
    SAN9_P1_S5_MENU_ENTRY_CLAIMED = 4,
    SAN9_P1_S5_MENU_VPTR_RESTORED = 5,
    SAN9_P1_S5_MENU_ORIGINAL_RETURNED = 6,
    SAN9_P1_S5_MENU_START_CLAIMED = 7,
    SAN9_P1_S5_MENU_STARTED = 8,
    SAN9_P1_S5_MENU_REJECTED = 9,
    SAN9_P1_S5_MENU_BATCH_IDLE_ARMED = 10,
    SAN9_P1_S5_MENU_BATCH_IDLE_CLAIMED = 11,
    SAN9_P1_S5_MENU_BATCH_RESET_REQUESTED = 12,
    SAN9_P1_S5_MENU_BATCH_RESET_ACK = 13
} San9P1S5MenuHandoffState;

/* One shared linearization word arbitrates controller timeout against the
   target's two irreversible boundaries.  EVENT_INFLIGHT and FINISH_COMMIT
   may only be completed by their current target-side owner. */
typedef enum San9P1S5TerminalState {
    SAN9_P1_S5_TERMINAL_EMPTY = 0,
    SAN9_P1_S5_TERMINAL_OPEN = 1,
    SAN9_P1_S5_TERMINAL_START_PRE_EVENT = 2,
    SAN9_P1_S5_TERMINAL_EVENT_INFLIGHT = 3,
    SAN9_P1_S5_TERMINAL_FINISH_VERIFY = 4,
    SAN9_P1_S5_TERMINAL_FINISH_COMMIT = 5,
    SAN9_P1_S5_TERMINAL_SUCCESS = 6,
    SAN9_P1_S5_TERMINAL_REJECTED = 7,
    SAN9_P1_S5_TERMINAL_TIMEOUT = 8,
    SAN9_P1_S5_TERMINAL_RESTART = 9
} San9P1S5TerminalState;

static inline int san9_p1_s5_deadline_is_fresh(
    uint64_t now_ms, uint64_t signed_expires_at_ms)
{
    return signed_expires_at_ms != 0u && now_ms < signed_expires_at_ms;
}

static inline int san9_p1_s5_menu_timeout_requires_restart(int32_t state)
{
    return state >= (int32_t)SAN9_P1_S5_MENU_ARMED;
}

static inline int san9_p1_s5_modal_timeout_requires_restart(int32_t state)
{
    return state >= (int32_t)SAN9_P1_S5_MODAL_WAKE_CLAIMED;
}

static inline int san9_p1_s5_modal_pre_cas_gate_is_open(
    int32_t bootstrap_state, int32_t operation_state)
{
    return bootstrap_state == (int32_t)SAN9_P1_M2B_BOOTSTRAP_READY
        && operation_state == (int32_t)SAN9_P1_S5_MODAL_WAKE_CLAIMED;
}

#define SAN9_P1_S3_TRACE_MAGIC UINT32_C(0x54523353)
#define SAN9_P1_S3_TRACE_CAPACITY 10u
#define SAN9_P1_S3_CHAIN_VPTR_CAPACITY 7u
#define SAN9_P1_S3_FLAG_CONTROLLER UINT32_C(0x00000001)
#define SAN9_P1_S3_FLAG_COMMERCE_HANDLER UINT32_C(0x00000002)
#define SAN9_P1_S3_FLAG_COMMERCE_OUTER UINT32_C(0x00000004)
#define SAN9_P1_S3_FLAG_SELECTOR UINT32_C(0x00000008)
#define SAN9_P1_S3_FLAG_SELECTED_FIVE UINT32_C(0x00000010)
#define SAN9_P1_S3_FLAG_COMMAND UINT32_C(0x00000020)
#define SAN9_P1_S3_FLAG_RETURNED_IDLE UINT32_C(0x00000040)

typedef struct San9P1S3TraceRecord {
    volatile uint32_t published_sequence;
    uint32_t sequence;
    uint64_t tick_ms;
    uint32_t change_mask;
    uint32_t flags;
    uint32_t failure_code;
    uint32_t task_depth;
    uint32_t chain_signature;
    uint32_t scheduler_pointer;
    uint32_t scheduler_pending;
    uint32_t controller_pointer;
    uint32_t controller_child;
    uint32_t controller_pending;
    uint32_t controller_corps;
    uint32_t controller_state;
    uint32_t controller_target;
    uint32_t leaf_pointer;
    uint32_t leaf_vptr;
    uint32_t leaf_child;
    uint32_t leaf_pending;
    uint32_t handler_pointer;
    uint32_t handler_vptr;
    uint32_t handler_state;
    uint32_t handler_corps;
    uint32_t handler_target;
    uint32_t handler_list_vptr;
    uint32_t handler_list_first;
    uint32_t handler_list_last;
    uint32_t handler_list_count;
    uint32_t handler_list_tail[4];
    uint32_t outer_pointer;
    uint32_t outer_vptr;
    uint32_t outer_result;
    uint32_t outer_source_count;
    uint32_t outer_working_count;
    uint32_t outer_committed_count;
    uint32_t selector_pointer;
    uint32_t selector_vptr;
    uint32_t selector_maximum;
    uint32_t command_pointer;
    uint32_t command_vptr;
    uint32_t global_selected_vptr;
    uint32_t global_selected_first;
    uint32_t global_selected_last;
    uint32_t global_selected_count;
    uint32_t current_building;
    uint32_t chain_vptr[SAN9_P1_S3_CHAIN_VPTR_CAPACITY];
} San9P1S3TraceRecord;

typedef struct San9P1S3TraceArea {
    uint32_t magic;
    uint16_t schema_major;
    uint16_t schema_minor;
    uint32_t record_size;
    uint32_t capacity;
    uint32_t readonly_dispatch_count;
    uint32_t write_dispatch_count;
    volatile uint32_t write_sequence;
    volatile uint32_t read_sequence;
    volatile uint32_t dropped_records;
    volatile uint32_t capture_failures;
    volatile uint32_t cumulative_flags;
    volatile uint32_t stop_requested;
    volatile uint32_t ready;
    uint32_t reserved[3];
    San9P1S3TraceRecord records[SAN9_P1_S3_TRACE_CAPACITY];
} San9P1S3TraceArea;

typedef struct San9P1M2bContract {
    uint32_t magic;
    uint16_t schema_major;
    uint16_t schema_minor;
    uint32_t structure_size;
    uint32_t shared_size;
    uint32_t mailbox_offset;
    uint32_t ping_count;
    uint32_t live_execution_default;
    uint32_t ui_connected;
    uint32_t commerce_abi_ready;
    uint32_t commerce_live_authorization;
    uint32_t exact_idle_slot;
    uint32_t exact_original_idle;
    uint32_t exact_idle_caller;
    uint32_t s3_readonly_build;
    uint32_t readonly_dispatch_count;
    uint32_t write_dispatch_count;
    uint32_t readonly_max_bytes;
    uint32_t s5_modal_wake_authorization;
} San9P1M2bContract;

typedef struct San9P1M2bBootstrapConfig {
    uint32_t magic;
    uint16_t schema_major;
    uint16_t schema_minor;
    uint32_t structure_size;
    uint32_t target_pid;
    uint32_t target_thread_id;
    uint32_t target_hwnd;
    uint32_t timeout_ms;
    uint32_t operation_mode;
    uint32_t reserved_config;
    uint64_t owner_token;
    San9P1EasyBinding easy_binding;
    uint8_t hmac_key[SAN9_P1_HMAC_KEY_SIZE];
    uint8_t binding_frame[SAN9_P1_FRAME_SIZE];
    San9S5NoApplyRequest s5_request;
    wchar_t dll_path[260];
} San9P1M2bBootstrapConfig;

/* ABI is deliberately inert until the reverse-engineered Commerce primitive
   is independently supplied and authenticated.  It is never placed on P1Wire. */
typedef struct San9P1M2bCommercePrimitive {
    uint32_t structure_size;
    uint32_t current_city_only;
    uint32_t command_kind;
    uint32_t abi_ready;
    uint32_t handler;
    uint32_t handler_vptr;
    uint32_t can_execute;
    uint32_t person_list_ctor;
    uint32_t person_list_dtor;
    uint32_t copy_first_n;
    uint32_t allocation;
    uint32_t allocation_size;
    uint32_t selection_limit;
    uint32_t command_ctor;
    uint32_t command_vptr;
    uint32_t validator;
    uint32_t attach;
    uint32_t handler_driver;
    uint32_t native_apply;
    uint32_t event_id;
    uint32_t root_handler_offset;
    uint32_t handler_selection_offset;
    uint32_t vtable_slot_count;
    uint32_t execute_slot_offset;
} San9P1M2bCommercePrimitive;

#define SAN9_P1_S5_EVIDENCE_MAGIC UINT32_C(0x45533553)

typedef struct San9P1S5NoApplyEvidence {
    uint32_t magic;
    uint16_t schema_major;
    uint16_t schema_minor;
    uint32_t structure_size;
    uint32_t terminal_code;
    uint32_t restart_required;
    uint32_t machine_state;
    uint32_t first_tid;
    uint32_t last_tid;
    uint32_t last_caller;
    uint32_t root_pointer;
    uint32_t city_pointer;
    uint32_t corps_pointer;
    uint32_t handler_pointer;
    uint32_t event_attempt_count;
    uint32_t shadow_arm_count;
    uint32_t execute_enter_count;
    uint32_t restore_count;
    uint32_t observed_apply_count;
    uint32_t controller_ack;
    uint32_t expected_person_ids[SAN9_S5_TOP5_COUNT];
    uint32_t observed_person_ids[SAN9_S5_TOP5_COUNT];
    uint32_t pre_commerce;
    uint32_t post_commerce;
    uint32_t commerce_maximum;
    uint32_t pre_money;
    uint32_t post_money;
    uint32_t pre_order_flags;
    uint32_t post_order_flags;
    uint32_t pre_global_selected_count;
    uint32_t post_global_selected_count;
    uint32_t native_command_id;
    uint32_t pre_command_value;
    uint32_t post_command_value;
    uint32_t command_maximum;
    uint32_t command_order_mask;
    uint32_t menu_wake_count;
    uint32_t menu_restore_count;
    uint8_t request_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t pre_business_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t post_business_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t pre_easy_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t post_easy_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t hmac[SAN9_S5_DIGEST_SIZE];
} San9P1S5NoApplyEvidence;

typedef struct San9P1S5MenuHandoff {
    volatile int32_t state;
    volatile uint32_t wake_post_count;
    volatile uint32_t hook_entry_count;
    volatile uint32_t exact_wake_count;
    volatile uint32_t duplicate_count;
    volatile uint32_t shadow_arm_count;
    volatile uint32_t entry_claim_count;
    volatile uint32_t menu_restore_count;
    volatile uint32_t original_return_count;
    volatile uint32_t start_count;
    volatile uint32_t restart_required;
    uint32_t first_tid;
    uint32_t last_tid;
    uint32_t last_caller;
    uint32_t menu_pointer;
    uint32_t top_hwnd;
    uint32_t modal_flags;
    uint8_t arm_easy_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t entry_easy_digest[SAN9_P1_DIGEST_SIZE];
} San9P1S5MenuHandoff;

#define SAN9_P1_S5_MODAL_EVIDENCE_MAGIC UINT32_C(0x4D355353)

typedef struct San9P1S5ModalProbeEvidence {
    uint32_t magic;
    uint16_t schema_major;
    uint16_t schema_minor;
    uint32_t structure_size;
    uint32_t terminal_code;
    uint32_t restart_required;
    uint32_t state;
    uint32_t wake_post_count;
    uint32_t hook_entry_count;
    uint32_t exact_wake_count;
    uint32_t wake_claim_count;
    uint32_t duplicate_count;
    uint32_t hook_depth;
    uint32_t hook_reentry_count;
    uint32_t hook_code;
    uint32_t remove_flag;
    uint32_t message;
    uint32_t atom;
    uint32_t challenge;
    uint32_t callnext_stable;
    uint32_t shadow_arm_count;
    uint32_t wrapper_enter_count;
    uint32_t original_return_count;
    uint32_t restore_count;
    uint32_t identity_reject_count;
    uint32_t wrapper_reentry_count;
    uint32_t first_tid;
    uint32_t last_tid;
    uint32_t last_caller;
    uint32_t message_hwnd;
    uint32_t top_hwnd;
    uint32_t menu_vptr;
    uint32_t modal_flags;
    uint32_t easy_stable_count;
    uint32_t controller_ack;
    uint8_t arm_easy_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t last_easy_digest[SAN9_P1_DIGEST_SIZE];
    uint32_t menu_pointer;
    uint32_t reserved[5];
    uint8_t hmac[SAN9_P1_DIGEST_SIZE];
} San9P1S5ModalProbeEvidence;

typedef struct San9P1S3OperationStorage {
    San9P1S3TraceArea trace;
    uint8_t reserved[8];
} San9P1S3OperationStorage;

typedef struct San9P1S5OperationStorage {
    San9S5NoApplyRequest request;
    San9S5NoApplyMachine machine;
    San9P1S5NoApplyEvidence evidence;
    San9P1S5MenuHandoff menu;
    volatile int32_t terminal_state;
    uint8_t reserved[2392u - sizeof(San9S5NoApplyRequest)
        - sizeof(San9S5NoApplyMachine) - sizeof(San9P1S5NoApplyEvidence)
        - sizeof(San9P1S5MenuHandoff) - sizeof(int32_t)];
} San9P1S5OperationStorage;

typedef struct San9P1S5ModalProbeOperationStorage {
    volatile int32_t state;
    San9P1S5ModalProbeEvidence evidence;
    uint8_t reserved[2392u - sizeof(int32_t)
        - sizeof(San9P1S5ModalProbeEvidence)];
} San9P1S5ModalProbeOperationStorage;

typedef union San9P1M2bOperationStorage {
    San9P1S3OperationStorage s3;
    San9P1S5OperationStorage s5;
    San9P1S5ModalProbeOperationStorage modal;
    uint8_t bytes[2392];
} San9P1M2bOperationStorage;

typedef struct San9P1M2bShared {
    uint32_t magic;
    uint16_t schema_major;
    uint16_t schema_minor;
    uint32_t declared_size;
    volatile int32_t bootstrap_state;
    volatile int32_t bootstrap_result;
    uint32_t target_pid;
    uint32_t target_thread_id;
    uint32_t target_hwnd;
    uint32_t registered_message;
    uint32_t mapping_atom;
    uint32_t challenge;
    uint32_t operation_mode;
    uint64_t owner_token;
    San9P1EasyBinding easy_binding;
    uint8_t binding_frame[SAN9_P1_FRAME_SIZE];
    volatile int32_t claim_enabled;
    volatile int32_t controller_request_state;
    volatile int32_t controller_response_state;
    volatile uint32_t last_evidence_ordinal;
    volatile uint32_t last_evidence_caller;
    uint8_t last_easy_snapshot_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t controller_request_frame[SAN9_P1_FRAME_SIZE];
    uint8_t controller_response_frame[SAN9_P1_FRAME_SIZE];
    volatile uint32_t probe0_idle_count;
    volatile uint32_t probe0_original_return_count;
    volatile uint32_t probe0_outer_count;
    volatile uint32_t probe0_nested_count;
    volatile uint32_t probe0_identity_reject_count;
    volatile uint32_t probe0_first_tid;
    volatile uint32_t probe0_last_tid;
    volatile uint32_t probe0_last_caller;
    volatile uint32_t probe0_last_app;
    San9P1M2bOperationStorage operation;
    San9P1M2Mailbox mailbox;
} San9P1M2bShared;

typedef struct San9P1M2bProbe0Evidence {
    uint32_t start_idle_count;
    uint32_t end_idle_count;
    uint32_t original_return_count;
    uint32_t outer_count;
    uint32_t nested_count;
    uint32_t identity_reject_count;
    uint32_t first_tid;
    uint32_t last_tid;
    uint32_t last_caller;
    uint32_t last_app;
} San9P1M2bProbe0Evidence;

typedef struct San9P1M2bPingEvidence {
    uint32_t requested_count;
    uint32_t completed_count;
    uint32_t verified_count;
    uint32_t accepted_count;
    uint32_t controller_ack;
    uint32_t stable_digest_count;
    uint32_t last_caller;
    uint32_t reserved;
    uint64_t first_sequence;
    uint64_t last_sequence;
    uint8_t easy_snapshot_digest[SAN9_P1_DIGEST_SIZE];
} San9P1M2bPingEvidence;

typedef struct San9P1M2bObserveEvidence {
    uint32_t records;
    uint32_t dropped_records;
    uint32_t capture_failures;
    uint32_t cumulative_flags;
    uint32_t readonly_dispatch_count;
    uint32_t write_dispatch_count;
    uint32_t elapsed_ms;
    uint32_t restart_required;
} San9P1M2bObserveEvidence;

_Static_assert(sizeof(uintptr_t) == 4u, "M2b live bridge is x86 only");
_Static_assert(offsetof(San9P1M2bShared, mailbox) == SAN9_P1_M2B_BOOTSTRAP_SIZE,
    "M2b mailbox offset changed");
_Static_assert(sizeof(San9P1M2bShared) == SAN9_P1_M2B_SHARED_SIZE,
    "M2b shared mapping must be exactly 8192 bytes");
_Static_assert(sizeof(San9P1S3TraceRecord) == 232u,
    "S3 trace record layout changed");
_Static_assert(sizeof(San9P1S3TraceArea) == 2384u,
    "S3 trace area layout changed");
_Static_assert(sizeof(San9P1S3OperationStorage) == 2392u,
    "S3 operation storage layout changed");
_Static_assert(sizeof(San9P1S5NoApplyEvidence) == 372u,
    "S5 evidence layout changed");
_Static_assert(sizeof(San9P1S5MenuHandoff) == 132u,
    "S5 menu handoff layout changed");
_Static_assert(sizeof(San9P1S5ModalProbeEvidence) == 256u,
    "S5 modal probe evidence layout changed");
_Static_assert(sizeof(San9P1S5OperationStorage) == 2392u,
    "S5 operation storage layout changed");
_Static_assert(sizeof(San9P1S5ModalProbeOperationStorage) == 2392u,
    "S5 modal probe storage layout changed");
_Static_assert(sizeof(San9P1M2bOperationStorage) == 2392u,
    "M2b operation union layout changed");
_Static_assert(SAN9_P1_M2_MAILBOX_SIZE == 4096u, "frozen M2a mailbox changed");
_Static_assert(SAN9_P1_M2_LIVE_AUTHORIZATION == 0,
    "frozen M2a remains non-authorizing");
_Static_assert(SAN9_P1_CONTAINS_BUSINESS_FIELDS == 0,
    "Commerce must not enter the P1 ping wire");

SAN9_P1_M2B_EXPORT intptr_t SAN9_P1_M2B_STDCALL San9BridgeP1M2b_GetMsgProc(
    int code, uintptr_t w_param, intptr_t l_param);
SAN9_P1_M2B_EXPORT int SAN9_P1_M2B_FASTCALL San9BridgeP1M2b_IdleBridge(
    void *app, void *unused_edx, int flag);
SAN9_P1_M2B_EXPORT uint32_t San9BridgeP1M2b_Contract(
    San9P1M2bContract *output, uint32_t output_size);

uint32_t san9_p1_m2b_controller_probe0(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *evidence);
uint32_t san9_p1_m2b_controller_ping(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1M2bPingEvidence *ping_evidence);
uint32_t san9_p1_m2b_controller_observe(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1M2bObserveEvidence *observe_evidence);
uint32_t san9_p1_m2b_controller_s5_no_apply(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence);
uint32_t san9_p1_m2b_controller_s5_apply_once(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence);
uint32_t san9_p1_m2b_controller_s6_cultivate_apply_once(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence);
uint32_t san9_p1_m2b_controller_s6_patrol_apply_once(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence);
uint32_t san9_p1_m2b_controller_s6_train_apply_once(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence);
uint32_t san9_p1_m2b_controller_s6_repair_apply_once(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence);
uint32_t san9_p1_m2b_controller_s8_basic_batch(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence);
uint32_t san9_p1_m2b_controller_s8_wealthy_batch(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5NoApplyEvidence *evidence);
uint32_t san9_p1_m2b_controller_s5_modal_probe(
    const San9P1M2bBootstrapConfig *config,
    San9P1M2bProbe0Evidence *probe_evidence,
    San9P1S5ModalProbeEvidence *evidence);
int san9_p1_s5_evidence_sign(
    San9P1S5NoApplyEvidence *evidence,
    const uint8_t *hmac_key,
    size_t hmac_key_size);
int san9_p1_s5_evidence_verify(
    const San9P1S5NoApplyEvidence *evidence,
    const uint8_t *hmac_key,
    size_t hmac_key_size);
int san9_p1_s5_modal_evidence_sign(
    San9P1S5ModalProbeEvidence *evidence,
    const uint8_t *hmac_key,
    size_t hmac_key_size);
int san9_p1_s5_modal_evidence_verify(
    const San9P1S5ModalProbeEvidence *evidence,
    const uint8_t *hmac_key,
    size_t hmac_key_size);
int san9_p1_s5_modal_evidence_is_vtable_exact(
    const San9P1S5ModalProbeEvidence *evidence,
    uint32_t expected_tid,
    uint32_t expected_message,
    uint32_t expected_atom,
    uint32_t expected_challenge);
uint32_t SAN9_P1_M2B_FASTCALL san9_p1_s5_modal_tick_bridge(
    void *menu, void *unused_edx);
void san9_p1_s5_modal_after_original(void *menu, uint32_t caller);
uint32_t SAN9_P1_M2B_FASTCALL san9_p1_s5_v8_menu_tick_bridge(
    void *menu, void *unused_edx);
uint32_t san9_p1_s5_v8_menu_entry(void *menu, uint32_t caller);
void san9_p1_s5_v8_after_original(uint32_t entry_token, uint32_t caller);
uint32_t san9_p1_m2b_enter_idle_depth(void);
void san9_p1_m2b_leave_idle_depth(uint32_t token);
void san9_p1_m2b_after_original(
    void *app, int flag, uint32_t depth, uint32_t caller);

#ifdef __cplusplus
}
#endif

#endif
