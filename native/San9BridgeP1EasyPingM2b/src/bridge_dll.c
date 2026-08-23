#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <limits.h>
#include <string.h>

#include "san9_p1_m2b.h"
#include "easy_page_normalization.h"
#include "sha256.h"
#include "san9_p1_easy_manifest.gen.h"

#if (defined(S8_BASIC_BATCH_BUILD) && S8_BASIC_BATCH_BUILD) \
    || (defined(S8_WEALTHY_BATCH_BUILD) && S8_WEALTHY_BATCH_BUILD)
#define SAN9_COMBINED_BATCH_BUILD 1
static uint32_t s8_active_native_id(void);
static uint32_t s8_active_request_kind(void);
static uint32_t s8_active_request_flags(void);
static uint32_t s8_active_event_id(void);
static uint32_t s8_active_handler_vptr(void);
static uint32_t s8_active_command_vptr(void);
static uint32_t s8_active_order_flag(void);
static uint32_t s8_active_order_offset(void);
static uint32_t s8_active_order_word(void);
static uint32_t s8_active_cost(void);
static uint32_t s8_active_value_offset(void);
static uint32_t s8_active_value_word(void);
static uint32_t s8_active_maximum_offset(void);
static uint32_t s8_active_maximum_fixed(void);
static uint32_t s8_active_maximum_value(void);
static uint32_t s8_active_ability_offset(void);
static uint32_t s8_active_pre_current(
    const San9S5CurrentContextSnapshot *snapshot);
static uint32_t s8_active_pre_maximum(
    const San9S5CurrentContextSnapshot *snapshot);
static uint32_t s8_active_pre_order(
    const San9S5CurrentContextSnapshot *snapshot);
static void s8_select_exact_vtables(uint32_t native_command_id);
static San9S5NoApplyGate *s8_active_gate(void);
#define SAN9_ACTIVE_GATE (s8_active_gate())
#else
#define SAN9_COMBINED_BATCH_BUILD 0
#define SAN9_ACTIVE_GATE (&g_runtime.s5_gate)
#endif

#if (defined(S5_APPLY_ONCE_BUILD) && S5_APPLY_ONCE_BUILD) \
    || (defined(S6_CULTIVATE_APPLY_ONCE_BUILD) \
        && S6_CULTIVATE_APPLY_ONCE_BUILD) \
    || (defined(S6_PATROL_APPLY_ONCE_BUILD) \
        && S6_PATROL_APPLY_ONCE_BUILD) \
    || (defined(S6_TRAIN_APPLY_ONCE_BUILD) \
        && S6_TRAIN_APPLY_ONCE_BUILD) \
    || (defined(S6_REPAIR_APPLY_ONCE_BUILD) \
        && S6_REPAIR_APPLY_ONCE_BUILD) \
    || SAN9_COMBINED_BATCH_BUILD
#define SAN9_APPLY_ONCE_BUILD 1
#else
#define SAN9_APPLY_ONCE_BUILD 0
#endif

#if SAN9_COMBINED_BATCH_BUILD
#if defined(S8_WEALTHY_BATCH_BUILD) && S8_WEALTHY_BATCH_BUILD
#define SAN9_ACTIVE_APPLY_MODE SAN9_P1_M2B_OPERATION_S8_WEALTHY_BATCH
#define SAN9_ACTIVE_FAILURE SAN9_P1_M2B_S8_WEALTHY_BATCH_FAILED
#else
#define SAN9_ACTIVE_APPLY_MODE SAN9_P1_M2B_OPERATION_S8_BASIC_BATCH
#define SAN9_ACTIVE_FAILURE SAN9_P1_M2B_S8_BASIC_BATCH_FAILED
#endif
#define SAN9_ACTIVE_REQUEST_KIND s8_active_request_kind()
#define SAN9_ACTIVE_REQUEST_FLAGS s8_active_request_flags()
#define SAN9_ACTIVE_NATIVE_ID s8_active_native_id()
#define SAN9_ACTIVE_EVENT_ID s8_active_event_id()
#define SAN9_ACTIVE_HANDLER_VPTR s8_active_handler_vptr()
#define SAN9_ACTIVE_COMMAND_VPTR s8_active_command_vptr()
#define SAN9_ACTIVE_ORDER_FLAG s8_active_order_flag()
#define SAN9_ACTIVE_ORDER_OFFSET s8_active_order_offset()
#define SAN9_ACTIVE_ORDER_WORD s8_active_order_word()
#define SAN9_ACTIVE_COST s8_active_cost()
#define SAN9_ACTIVE_VALUE_OFFSET s8_active_value_offset()
#define SAN9_ACTIVE_VALUE_WORD s8_active_value_word()
#define SAN9_ACTIVE_MAXIMUM_OFFSET s8_active_maximum_offset()
#define SAN9_ACTIVE_MAXIMUM_FIXED s8_active_maximum_fixed()
#define SAN9_ACTIVE_MAXIMUM_VALUE s8_active_maximum_value()
#define SAN9_ACTIVE_ABILITY_OFFSET s8_active_ability_offset()
#define SAN9_ACTIVE_PRE_CURRENT(snapshot) s8_active_pre_current(snapshot)
#define SAN9_ACTIVE_PRE_MAXIMUM(snapshot) s8_active_pre_maximum(snapshot)
#define SAN9_ACTIVE_PRE_ORDER(snapshot) s8_active_pre_order(snapshot)
#elif defined(S6_REPAIR_APPLY_ONCE_BUILD) && S6_REPAIR_APPLY_ONCE_BUILD
#define SAN9_ACTIVE_APPLY_MODE SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE
#define SAN9_ACTIVE_REQUEST_KIND SAN9_S6_REQUEST_KIND_REPAIR_APPLY_ONCE
#define SAN9_ACTIVE_REQUEST_FLAGS (SAN9_S5_FLAG_CURRENT_CITY_ONLY \
    | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_REPAIR)
#define SAN9_ACTIVE_NATIVE_ID SAN9_P1_M2B_REPAIR_NATIVE_ID
#define SAN9_ACTIVE_EVENT_ID SAN9_P1_M2B_REPAIR_EVENT_ID
#define SAN9_ACTIVE_HANDLER_VPTR SAN9_P1_M2B_REPAIR_HANDLER_VPTR
#define SAN9_ACTIVE_COMMAND_VPTR SAN9_P1_M2B_REPAIR_COMMAND_VPTR
#define SAN9_ACTIVE_ORDER_FLAG SAN9_P1_M2B_REPAIR_ORDER_FLAG
#define SAN9_ACTIVE_ORDER_OFFSET SAN9_P1_M2B_REPAIR_ORDER_OFFSET
#define SAN9_ACTIVE_ORDER_WORD 1
#define SAN9_ACTIVE_COST SAN9_P1_M2B_REPAIR_COST
#define SAN9_ACTIVE_VALUE_OFFSET SAN9_P1_M2B_REPAIR_CURRENT_OFFSET
#define SAN9_ACTIVE_VALUE_WORD 1
#define SAN9_ACTIVE_MAXIMUM_OFFSET SAN9_P1_M2B_REPAIR_MAXIMUM_OFFSET
#define SAN9_ACTIVE_MAXIMUM_FIXED 0
#define SAN9_ACTIVE_MAXIMUM_VALUE 0u
#define SAN9_ACTIVE_ABILITY_OFFSET SAN9_P1_M2B_REPAIR_ABILITY_OFFSET
#define SAN9_ACTIVE_FAILURE SAN9_P1_M2B_S6_REPAIR_APPLY_ONCE_FAILED
#define SAN9_ACTIVE_PRE_CURRENT(snapshot) ((snapshot)->repair_current)
#define SAN9_ACTIVE_PRE_MAXIMUM(snapshot) ((snapshot)->repair_maximum)
#define SAN9_ACTIVE_PRE_ORDER(snapshot) ((snapshot)->repair_order_flags)
#elif defined(S6_TRAIN_APPLY_ONCE_BUILD) && S6_TRAIN_APPLY_ONCE_BUILD
#define SAN9_ACTIVE_APPLY_MODE SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE
#define SAN9_ACTIVE_REQUEST_KIND SAN9_S6_REQUEST_KIND_TRAIN_APPLY_ONCE
#define SAN9_ACTIVE_REQUEST_FLAGS (SAN9_S5_FLAG_CURRENT_CITY_ONLY \
    | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_TRAIN)
#define SAN9_ACTIVE_NATIVE_ID SAN9_P1_M2B_TRAIN_NATIVE_ID
#define SAN9_ACTIVE_EVENT_ID SAN9_P1_M2B_TRAIN_EVENT_ID
#define SAN9_ACTIVE_HANDLER_VPTR SAN9_P1_M2B_TRAIN_HANDLER_VPTR
#define SAN9_ACTIVE_COMMAND_VPTR SAN9_P1_M2B_TRAIN_COMMAND_VPTR
#define SAN9_ACTIVE_ORDER_FLAG SAN9_P1_M2B_TRAIN_ORDER_FLAG
#define SAN9_ACTIVE_ORDER_OFFSET SAN9_P1_M2B_TRAIN_ORDER_OFFSET
#define SAN9_ACTIVE_ORDER_WORD 1
#define SAN9_ACTIVE_COST SAN9_P1_M2B_TRAIN_COST
#define SAN9_ACTIVE_VALUE_OFFSET SAN9_P1_M2B_TRAIN_CURRENT_OFFSET
#define SAN9_ACTIVE_VALUE_WORD 0
#define SAN9_ACTIVE_MAXIMUM_OFFSET 0u
#define SAN9_ACTIVE_MAXIMUM_FIXED 1
#define SAN9_ACTIVE_MAXIMUM_VALUE SAN9_P1_M2B_TRAIN_MAXIMUM
#define SAN9_ACTIVE_ABILITY_OFFSET SAN9_P1_M2B_TRAIN_ABILITY_OFFSET
#define SAN9_ACTIVE_FAILURE SAN9_P1_M2B_S6_TRAIN_APPLY_ONCE_FAILED
#define SAN9_ACTIVE_PRE_CURRENT(snapshot) ((snapshot)->train_morale)
#define SAN9_ACTIVE_PRE_MAXIMUM(snapshot) ((snapshot)->train_maximum)
#define SAN9_ACTIVE_PRE_ORDER(snapshot) ((snapshot)->train_order_flags)
#elif defined(S6_PATROL_APPLY_ONCE_BUILD) && S6_PATROL_APPLY_ONCE_BUILD
#define SAN9_ACTIVE_APPLY_MODE SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE
#define SAN9_ACTIVE_REQUEST_KIND SAN9_S6_REQUEST_KIND_PATROL_APPLY_ONCE
#define SAN9_ACTIVE_REQUEST_FLAGS (SAN9_S5_FLAG_CURRENT_CITY_ONLY \
    | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_PATROL)
#define SAN9_ACTIVE_NATIVE_ID SAN9_P1_M2B_PATROL_NATIVE_ID
#define SAN9_ACTIVE_EVENT_ID SAN9_P1_M2B_PATROL_EVENT_ID
#define SAN9_ACTIVE_HANDLER_VPTR SAN9_P1_M2B_PATROL_HANDLER_VPTR
#define SAN9_ACTIVE_COMMAND_VPTR SAN9_P1_M2B_PATROL_COMMAND_VPTR
#define SAN9_ACTIVE_ORDER_FLAG SAN9_P1_M2B_PATROL_ORDER_FLAG
#define SAN9_ACTIVE_ORDER_OFFSET 0x1E0u
#define SAN9_ACTIVE_ORDER_WORD 0
#define SAN9_ACTIVE_COST SAN9_P1_M2B_PATROL_COST
#define SAN9_ACTIVE_VALUE_OFFSET SAN9_P1_M2B_PATROL_CURRENT_OFFSET
#define SAN9_ACTIVE_VALUE_WORD 0
#define SAN9_ACTIVE_MAXIMUM_OFFSET 0u
#define SAN9_ACTIVE_MAXIMUM_FIXED 1
#define SAN9_ACTIVE_MAXIMUM_VALUE SAN9_P1_M2B_PATROL_MAXIMUM
#define SAN9_ACTIVE_ABILITY_OFFSET SAN9_P1_M2B_PATROL_ABILITY_OFFSET
#define SAN9_ACTIVE_FAILURE SAN9_P1_M2B_S6_PATROL_APPLY_ONCE_FAILED
#define SAN9_ACTIVE_PRE_CURRENT(snapshot) ((snapshot)->patrol_current)
#define SAN9_ACTIVE_PRE_MAXIMUM(snapshot) ((snapshot)->patrol_maximum)
#define SAN9_ACTIVE_PRE_ORDER(snapshot) ((snapshot)->order_flags)
#elif defined(S6_CULTIVATE_APPLY_ONCE_BUILD) && S6_CULTIVATE_APPLY_ONCE_BUILD
#define SAN9_ACTIVE_APPLY_MODE SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE
#define SAN9_ACTIVE_REQUEST_KIND SAN9_S6_REQUEST_KIND_CULTIVATE_APPLY_ONCE
#define SAN9_ACTIVE_REQUEST_FLAGS (SAN9_S5_FLAG_CURRENT_CITY_ONLY \
    | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_CULTIVATE)
#define SAN9_ACTIVE_NATIVE_ID SAN9_P1_M2B_CULTIVATE_NATIVE_ID
#define SAN9_ACTIVE_EVENT_ID SAN9_P1_M2B_CULTIVATE_EVENT_ID
#define SAN9_ACTIVE_HANDLER_VPTR SAN9_P1_M2B_CULTIVATE_HANDLER_VPTR
#define SAN9_ACTIVE_COMMAND_VPTR SAN9_P1_M2B_CULTIVATE_COMMAND_VPTR
#define SAN9_ACTIVE_ORDER_FLAG SAN9_P1_M2B_CULTIVATE_ORDER_FLAG
#define SAN9_ACTIVE_ORDER_OFFSET 0x1E0u
#define SAN9_ACTIVE_ORDER_WORD 0
#define SAN9_ACTIVE_COST SAN9_P1_M2B_CULTIVATE_COST
#define SAN9_ACTIVE_VALUE_OFFSET SAN9_P1_M2B_CULTIVATE_CURRENT_OFFSET
#define SAN9_ACTIVE_VALUE_WORD 0
#define SAN9_ACTIVE_MAXIMUM_OFFSET SAN9_P1_M2B_CULTIVATE_MAXIMUM_OFFSET
#define SAN9_ACTIVE_MAXIMUM_FIXED 0
#define SAN9_ACTIVE_MAXIMUM_VALUE 0u
#define SAN9_ACTIVE_ABILITY_OFFSET S5_PERSON_POLITICS_OFFSET
#define SAN9_ACTIVE_FAILURE SAN9_P1_M2B_S6_CULTIVATE_APPLY_ONCE_FAILED
#define SAN9_ACTIVE_PRE_CURRENT(snapshot) ((snapshot)->cultivate_current)
#define SAN9_ACTIVE_PRE_MAXIMUM(snapshot) ((snapshot)->cultivate_maximum)
#define SAN9_ACTIVE_PRE_ORDER(snapshot) ((snapshot)->order_flags)
#else
#define SAN9_ACTIVE_APPLY_MODE SAN9_P1_M2B_OPERATION_S5_APPLY_ONCE
#define SAN9_ACTIVE_REQUEST_KIND SAN9_S5_REQUEST_KIND_APPLY_ONCE
#define SAN9_ACTIVE_REQUEST_FLAGS (SAN9_S5_FLAG_CURRENT_CITY_ONLY \
    | SAN9_S5_FLAG_APPLY_ONCE)
#define SAN9_ACTIVE_NATIVE_ID SAN9_P1_M2B_COMMERCE_NATIVE_ID
#define SAN9_ACTIVE_EVENT_ID SAN9_P1_M2B_COMMERCE_EVENT_ID
#define SAN9_ACTIVE_HANDLER_VPTR SAN9_P1_M2B_COMMERCE_HANDLER_VPTR
#define SAN9_ACTIVE_COMMAND_VPTR SAN9_P1_M2B_COMMAND_VPTR
#define SAN9_ACTIVE_ORDER_FLAG S5_CITY_COMMERCE_ORDER_FLAG
#define SAN9_ACTIVE_ORDER_OFFSET 0x1E0u
#define SAN9_ACTIVE_ORDER_WORD 0
#define SAN9_ACTIVE_COST UINT32_C(250)
#define SAN9_ACTIVE_VALUE_OFFSET S5_CITY_COMMERCE_OFFSET
#define SAN9_ACTIVE_VALUE_WORD 0
#define SAN9_ACTIVE_MAXIMUM_OFFSET S5_CITY_COMMERCE_MAX_OFFSET
#define SAN9_ACTIVE_MAXIMUM_FIXED 0
#define SAN9_ACTIVE_MAXIMUM_VALUE 0u
#define SAN9_ACTIVE_ABILITY_OFFSET S5_PERSON_POLITICS_OFFSET
#define SAN9_ACTIVE_FAILURE SAN9_P1_M2B_S5_APPLY_ONCE_FAILED
#define SAN9_ACTIVE_PRE_CURRENT(snapshot) ((snapshot)->commerce_current)
#define SAN9_ACTIVE_PRE_MAXIMUM(snapshot) ((snapshot)->commerce_maximum)
#define SAN9_ACTIVE_PRE_ORDER(snapshot) ((snapshot)->order_flags)
#endif

typedef struct M2bRuntime {
    HMODULE module;
    HANDLE mapping;
    San9P1M2bShared *shared;
    San9P1M2RuntimeStorage m2;
    San9P1EasyCallbacks easy_callbacks;
    San9P1EasyBinding easy_binding;
    San9P1M2FakeActions actions;
    San9P1Frame binding_frame;
    San9S5NoApplyGate s5_gate;
    San9S5CurrentContextSnapshot s5_pre;
    uint8_t s5_binding_digest[SAN9_S5_DIGEST_SIZE];
#if SAN9_COMBINED_BATCH_BUILD
    San9S5NoApplyRequest s8_request_latch;
    uint32_t s8_native_id_latch;
    uint32_t s8_request_latched;
    San9S5BoundCurrentCity s8_bound_city;
    uint32_t s8_bound_city_valid;
    San9S5NoApplyGate s8_step_gates[5];
#endif
    uint8_t easy_snapshot_digest[SAN9_P1_DIGEST_SIZE];
    uint64_t owner_token;
    uint64_t s5_settle_deadline_ms;
    uint64_t s5_request_expires_at_ms;
    uint32_t main_thread_id;
    volatile LONG installed;
    volatile LONG slot_committed;
    San9P1S3TraceRecord last_trace;
    uint64_t last_easy_check_ms;
    void *modal_menu;
    uint32_t modal_top_hwnd;
    uint32_t modal_flags;
    void *s5_menu;
    uint32_t s5_menu_top_hwnd;
    uint32_t s5_menu_flags;
    int has_last_trace;
} M2bRuntime;

static M2bRuntime g_runtime;
static _Thread_local uint32_t g_idle_depth;
static _Thread_local uint32_t g_hook_depth;
static _Thread_local uint32_t g_modal_wrapper_depth;
static const volatile char g_s5_modal_probe_identity[] =
    "S5_MODAL_PROBE=VTABLE_V7;HOOK_WAKE=1;NULL_HWND=1;VTABLE_BYTES=260;"
    "TICK_OFFSET=164;TICKS=3;ROOT_EVENT=0;S5_PUBLISH=0;APPLY=0;"
    "AUTHORIZATION=0";
#if defined(S8_WEALTHY_BATCH_BUILD) && S8_WEALTHY_BATCH_BUILD
static const volatile char g_s5_v8_identity[] =
    "S8_WEALTHY_BATCH_V1=1;DESCRIPTORS=PATROL,COMMERCE,CULTIVATE,TRAIN,REPAIR;"
    "ONE_MENU_SIGNAL=1;IDLE_CHAIN=1;SKIP_CURRENT_ONLY=1;"
    "HANDLER_EXECUTE_RETURN_COMMAND=1;COMMAND_APPLY_SHADOW=1;"
    "DIRECT_DRIVER=0;DIRECT_VALIDATOR=0;DIRECT_ATTACH=0";
#elif defined(S8_BASIC_BATCH_BUILD) && S8_BASIC_BATCH_BUILD
static const volatile char g_s5_v8_identity[] =
    "S8_BASIC_BATCH_V1=1;DESCRIPTORS=COMMERCE,CULTIVATE;"
    "ONE_MENU_SIGNAL=1;IDLE_CHAIN=1;SKIP_CURRENT_ONLY=1;"
    "HANDLER_EXECUTE_RETURN_COMMAND=1;COMMAND_APPLY_SHADOW=1;"
    "DIRECT_DRIVER=0;DIRECT_VALIDATOR=0;DIRECT_ATTACH=0";
#elif defined(S6_REPAIR_APPLY_ONCE_BUILD) && S6_REPAIR_APPLY_ONCE_BUILD
static const volatile char g_s5_v8_identity[] =
    "S6_REPAIR_APPLY_ONCE_V1=1;HANDLER_EXECUTE_RETURN_COMMAND=1;"
    "COMMAND_APPLY_SHADOW=1;APPLY_EXACT_ONCE=1;DIRECT_DRIVER=0;"
    "DIRECT_VALIDATOR=0;DIRECT_ATTACH=0";
#elif defined(S6_TRAIN_APPLY_ONCE_BUILD) && S6_TRAIN_APPLY_ONCE_BUILD
static const volatile char g_s5_v8_identity[] =
    "S6_TRAIN_APPLY_ONCE_V1=1;HANDLER_EXECUTE_RETURN_COMMAND=1;"
    "COMMAND_APPLY_SHADOW=1;APPLY_EXACT_ONCE=1;DIRECT_DRIVER=0;"
    "DIRECT_VALIDATOR=0;DIRECT_ATTACH=0";
#elif defined(S6_PATROL_APPLY_ONCE_BUILD) && S6_PATROL_APPLY_ONCE_BUILD
static const volatile char g_s5_v8_identity[] =
    "S6_PATROL_APPLY_ONCE_V1=1;HANDLER_EXECUTE_RETURN_COMMAND=1;"
    "COMMAND_APPLY_SHADOW=1;APPLY_EXACT_ONCE=1;DIRECT_DRIVER=0;"
    "DIRECT_VALIDATOR=0;DIRECT_ATTACH=0";
#elif defined(S6_CULTIVATE_APPLY_ONCE_BUILD) && S6_CULTIVATE_APPLY_ONCE_BUILD
static const volatile char g_s5_v8_identity[] =
    "S6_CULTIVATE_APPLY_ONCE_V1=1;HANDLER_EXECUTE_RETURN_COMMAND=1;"
    "COMMAND_APPLY_SHADOW=1;APPLY_EXACT_ONCE=1;DIRECT_DRIVER=0;"
    "DIRECT_VALIDATOR=0;DIRECT_ATTACH=0";
#elif defined(S5_APPLY_ONCE_BUILD) && S5_APPLY_ONCE_BUILD
static const volatile char g_s5_v8_identity[] =
    "S5_APPLY_ONCE_V1=1;HANDLER_EXECUTE_RETURN_COMMAND=1;"
    "COMMAND_APPLY_SHADOW=1;APPLY_EXACT_ONCE=1;DIRECT_DRIVER=0;"
    "DIRECT_VALIDATOR=0;DIRECT_ATTACH=0";
#else
static const volatile char g_s5_v8_identity[] =
    "S5_NO_APPLY_V8=1;MENU_WAKE=1;MENU_SHADOW=1;ROOT_EVENT=1;"
    "HANDLER_SHADOW=1;EXECUTE_ONCE=1;APPLY=0;COMMAND_CTORS=0;"
    "TARGET_BUSINESS_WRITES=0;TERMINAL_CAS=1;SIGNED_DEADLINE=1;"
    "POISON_MONOTONIC=1";
#endif
#if SAN9_APPLY_ONCE_BUILD
#if defined(__GNUC__)
__attribute__((used))
#endif
static const volatile char g_s5_apply_once_identity[] =
#if defined(S8_WEALTHY_BATCH_BUILD) && S8_WEALTHY_BATCH_BUILD
    "S8_WEALTHY_BATCH=1;PATROL_COMMERCE_CULTIVATE_TRAIN_REPAIR=1;"
    "CURRENT_CITY=1;TOP5_RECAPTURE_EACH=1;ONE_MENU_SIGNAL=1;DIRECT_APPLY=0";
#elif defined(S8_BASIC_BATCH_BUILD) && S8_BASIC_BATCH_BUILD
    "S8_BASIC_BATCH=1;COMMERCE_THEN_CULTIVATE=1;CURRENT_CITY=1;"
    "TOP5_RECAPTURE_EACH=1;ONE_MENU_SIGNAL=1;DIRECT_APPLY=0";
#elif defined(S6_REPAIR_APPLY_ONCE_BUILD) && S6_REPAIR_APPLY_ONCE_BUILD
    "S6_REPAIR_APPLY_ONCE=1;CURRENT_CITY=1;TOP5=5;APPEND=0046EF70;"
    "COMMAND_CTOR=00489080;RETURN_TO_NATIVE_DRIVER=1;DIRECT_APPLY=0";
#elif defined(S6_TRAIN_APPLY_ONCE_BUILD) && S6_TRAIN_APPLY_ONCE_BUILD
    "S6_TRAIN_APPLY_ONCE=1;CURRENT_CITY=1;TOP5=5;APPEND=0046EF70;"
    "COMMAND_CTOR=00489D80;RETURN_TO_NATIVE_DRIVER=1;DIRECT_APPLY=0";
#elif defined(S6_PATROL_APPLY_ONCE_BUILD) && S6_PATROL_APPLY_ONCE_BUILD
    "S6_PATROL_APPLY_ONCE=1;CURRENT_CITY=1;TOP5=5;APPEND=0046EF70;"
    "COMMAND_CTOR=00488410;RETURN_TO_NATIVE_DRIVER=1;DIRECT_APPLY=0";
#elif defined(S6_CULTIVATE_APPLY_ONCE_BUILD) && S6_CULTIVATE_APPLY_ONCE_BUILD
    "S6_CULTIVATE_APPLY_ONCE=1;CURRENT_CITY=1;TOP5=5;APPEND=0046EF70;"
    "COMMAND_CTOR=00488B00;RETURN_TO_NATIVE_DRIVER=1;DIRECT_APPLY=0";
#else
    "S5_APPLY_ONCE=1;CURRENT_CITY=1;TOP5=5;APPEND=0046EF70;"
    "COMMAND_CTOR=0048B340;RETURN_TO_NATIVE_DRIVER=1;DIRECT_APPLY=0";
#endif
#endif
#if S5_NO_APPLY_BUILD
static uintptr_t g_modal_shadow_vtable[
    SAN9_P1_M2B_DOMESTIC_MENU_VTABLE_SIZE / sizeof(uintptr_t)];
static uintptr_t g_commerce_shadow_vtable[SAN9_P1_M2B_COMMERCE_VTABLE_SLOT_COUNT];
static void *g_commerce_handler;
static volatile LONG g_commerce_live_authorization;
static volatile LONG g_commerce_execute_hits;
static uint32_t g_commerce_vtable_exact[
    SAN9_P1_M2B_COMMERCE_VTABLE_SLOT_COUNT] = {
#if SAN9_COMBINED_BATCH_BUILD
    UINT32_C(0x004C63E0), UINT32_C(0x0059A850),
    UINT32_C(0x0047E4C0), UINT32_C(0x004C5280),
    UINT32_C(0x0050E9D0), UINT32_C(0x005B6930),
    UINT32_C(0x00401290), UINT32_C(0x005B6930),
    UINT32_C(0x004C6400), UINT32_C(0x004C62D0),
    UINT32_C(0x004C6310), UINT32_C(0x0050E9E0)
#elif defined(S6_REPAIR_APPLY_ONCE_BUILD) && S6_REPAIR_APPLY_ONCE_BUILD
    UINT32_C(0x004C2250), UINT32_C(0x0059A850),
    UINT32_C(0x0047E4C0), UINT32_C(0x004C5280),
    UINT32_C(0x0050E9D0), UINT32_C(0x005B6930),
    UINT32_C(0x00401290), UINT32_C(0x005B6930),
    UINT32_C(0x004C2270), UINT32_C(0x004C2140),
    UINT32_C(0x004C2180), UINT32_C(0x0050E9E0)
#elif defined(S6_TRAIN_APPLY_ONCE_BUILD) && S6_TRAIN_APPLY_ONCE_BUILD
    UINT32_C(0x004C3870), UINT32_C(0x0059A850),
    UINT32_C(0x0047E4C0), UINT32_C(0x004C5280),
    UINT32_C(0x0050E9D0), UINT32_C(0x005B6930),
    UINT32_C(0x00401290), UINT32_C(0x005B6930),
    UINT32_C(0x004C3890), UINT32_C(0x004C36D0),
    UINT32_C(0x004C37A0), UINT32_C(0x0050E9E0)
#elif defined(S6_PATROL_APPLY_ONCE_BUILD) && S6_PATROL_APPLY_ONCE_BUILD
    UINT32_C(0x004C1910), UINT32_C(0x0059A850),
    UINT32_C(0x0047E4C0), UINT32_C(0x004C5280),
    UINT32_C(0x0050E9D0), UINT32_C(0x005B6930),
    UINT32_C(0x00401290), UINT32_C(0x005B6930),
    UINT32_C(0x004C1930), UINT32_C(0x004C1800),
    UINT32_C(0x004C1840), UINT32_C(0x0050E9E0)
#elif defined(S6_CULTIVATE_APPLY_ONCE_BUILD) && S6_CULTIVATE_APPLY_ONCE_BUILD
    UINT32_C(0x004C1F30), UINT32_C(0x0059A850),
    UINT32_C(0x0047E4C0), UINT32_C(0x004C5280),
    UINT32_C(0x0050E9D0), UINT32_C(0x005B6930),
    UINT32_C(0x00401290), UINT32_C(0x005B6930),
    UINT32_C(0x004C1F50), UINT32_C(0x004C1E20),
    UINT32_C(0x004C1E60), UINT32_C(0x0050E9E0)
#else
    UINT32_C(0x004C63E0), UINT32_C(0x0059A850),
    UINT32_C(0x0047E4C0), UINT32_C(0x004C5280),
    UINT32_C(0x0050E9D0), UINT32_C(0x005B6930),
    UINT32_C(0x00401290), UINT32_C(0x005B6930),
    UINT32_C(0x004C6400), UINT32_C(0x004C62D0),
    UINT32_C(0x004C6310), UINT32_C(0x0050E9E0)
#endif
};
#if SAN9_APPLY_ONCE_BUILD
static uintptr_t g_command_shadow_vtable[SAN9_P1_M2B_COMMAND_VTABLE_SLOT_COUNT];
static void *g_apply_command;
static volatile LONG g_apply_live_authorization;
static volatile LONG g_apply_enter_hits;
static volatile LONG g_apply_return_hits;
static uint32_t g_command_vtable_exact[
    SAN9_P1_M2B_COMMAND_VTABLE_SLOT_COUNT] = {
#if SAN9_COMBINED_BATCH_BUILD
    UINT32_C(0x0048B380), UINT32_C(0x0059A850),
    UINT32_C(0x00485D00), UINT32_C(0x00485D20),
    UINT32_C(0x0045DE60), UINT32_C(0x005B6930),
    UINT32_C(0x00401290), UINT32_C(0x005B6930),
    UINT32_C(0x0048B4B0), UINT32_C(0x005B4BB0),
    UINT32_C(0x0059A850), UINT32_C(0x0059A850),
    UINT32_C(0x0048B5D0), UINT32_C(0x0045DE60),
    UINT32_C(0x004024D0), UINT32_C(0x0059A850),
    UINT32_C(0x0059A850), UINT32_C(0x0059A850),
    UINT32_C(0x0045DE60), UINT32_C(0x0046ACB0),
    UINT32_C(0x0046ACB0), UINT32_C(0x00593EA0)
#elif defined(S6_REPAIR_APPLY_ONCE_BUILD) && S6_REPAIR_APPLY_ONCE_BUILD
    UINT32_C(0x004890C0), UINT32_C(0x0059A850),
    UINT32_C(0x00485D00), UINT32_C(0x00485D20),
    UINT32_C(0x0045DE60), UINT32_C(0x005B6930),
    UINT32_C(0x00401290), UINT32_C(0x005B6930),
    UINT32_C(0x004890E0), UINT32_C(0x005B4BB0),
    UINT32_C(0x0059A850), UINT32_C(0x0059A850),
    UINT32_C(0x00489310), UINT32_C(0x0045DE60),
    UINT32_C(0x004024D0), UINT32_C(0x0059A850),
    UINT32_C(0x0059A850), UINT32_C(0x0059A850),
    UINT32_C(0x0045DE60), UINT32_C(0x0046ACB0),
    UINT32_C(0x0046ACB0), UINT32_C(0x00593EA0)
#elif defined(S6_TRAIN_APPLY_ONCE_BUILD) && S6_TRAIN_APPLY_ONCE_BUILD
    UINT32_C(0x00489DC0), UINT32_C(0x0059A850),
    UINT32_C(0x00485D00), UINT32_C(0x00485D20),
    UINT32_C(0x0045DE60), UINT32_C(0x005B6930),
    UINT32_C(0x00401290), UINT32_C(0x005B6930),
    UINT32_C(0x00489F10), UINT32_C(0x005B4BB0),
    UINT32_C(0x0059A850), UINT32_C(0x0059A850),
    UINT32_C(0x00489FF0), UINT32_C(0x0045DE60),
    UINT32_C(0x004024D0), UINT32_C(0x0059A850),
    UINT32_C(0x0059A850), UINT32_C(0x0059A850),
    UINT32_C(0x0045DE60), UINT32_C(0x0046ACB0),
    UINT32_C(0x0046ACB0), UINT32_C(0x00593EA0)
#elif defined(S6_PATROL_APPLY_ONCE_BUILD) && S6_PATROL_APPLY_ONCE_BUILD
    UINT32_C(0x00488460), UINT32_C(0x0059A850),
    UINT32_C(0x00485D00), UINT32_C(0x00485D20),
    UINT32_C(0x0045DE60), UINT32_C(0x005B6930),
    UINT32_C(0x00401290), UINT32_C(0x005B6930),
    UINT32_C(0x00488580), UINT32_C(0x005B4BB0),
    UINT32_C(0x0059A850), UINT32_C(0x0059A850),
    UINT32_C(0x00488690), UINT32_C(0x0045DE60),
    UINT32_C(0x004024D0), UINT32_C(0x0059A850),
    UINT32_C(0x0059A850), UINT32_C(0x0059A850),
    UINT32_C(0x0045DE60), UINT32_C(0x0046ACB0),
    UINT32_C(0x0046ACB0), UINT32_C(0x00593EA0)
#elif defined(S6_CULTIVATE_APPLY_ONCE_BUILD) && S6_CULTIVATE_APPLY_ONCE_BUILD
    UINT32_C(0x00488B40), UINT32_C(0x0059A850),
    UINT32_C(0x00485D00), UINT32_C(0x00485D20),
    UINT32_C(0x0045DE60), UINT32_C(0x005B6930),
    UINT32_C(0x00401290), UINT32_C(0x005B6930),
    UINT32_C(0x00488C70), UINT32_C(0x005B4BB0),
    UINT32_C(0x0059A850), UINT32_C(0x0059A850),
    UINT32_C(0x00488D90), UINT32_C(0x0045DE60),
    UINT32_C(0x004024D0), UINT32_C(0x0059A850),
    UINT32_C(0x0059A850), UINT32_C(0x0059A850),
    UINT32_C(0x0045DE60), UINT32_C(0x0046ACB0),
    UINT32_C(0x0046ACB0), UINT32_C(0x00593EA0)
#else
    UINT32_C(0x0048B380), UINT32_C(0x0059A850),
    UINT32_C(0x00485D00), UINT32_C(0x00485D20),
    UINT32_C(0x0045DE60), UINT32_C(0x005B6930),
    UINT32_C(0x00401290), UINT32_C(0x005B6930),
    UINT32_C(0x0048B4B0), UINT32_C(0x005B4BB0),
    UINT32_C(0x0059A850), UINT32_C(0x0059A850),
    UINT32_C(0x0048B5D0), UINT32_C(0x0045DE60),
    UINT32_C(0x004024D0), UINT32_C(0x0059A850),
    UINT32_C(0x0059A850), UINT32_C(0x0059A850),
    UINT32_C(0x0045DE60), UINT32_C(0x0046ACB0),
    UINT32_C(0x0046ACB0), UINT32_C(0x00593EA0)
#endif
};
#if SAN9_COMBINED_BATCH_BUILD
typedef struct S8NativeDescriptor {
    uint32_t native_id;
    uint32_t request_kind;
    uint32_t request_flags;
    uint32_t event_id;
    uint32_t handler_vptr;
    uint32_t command_vptr;
    uint32_t order_flag;
    uint32_t order_offset;
    uint32_t order_word;
    uint32_t cost;
    uint32_t value_offset;
    uint32_t value_word;
    uint32_t maximum_offset;
    uint32_t maximum_fixed;
    uint32_t maximum_value;
    uint32_t ability_offset;
    uint32_t handler_slot0;
    uint32_t handler_slot8;
    uint32_t handler_slot9;
    uint32_t handler_slot10;
    uint32_t command_slot0;
    uint32_t command_slot8;
    uint32_t command_slot12;
} S8NativeDescriptor;

static const S8NativeDescriptor g_s8_descriptors[5] = {
    { SAN9_P1_M2B_PATROL_NATIVE_ID, SAN9_S6_REQUEST_KIND_PATROL_APPLY_ONCE,
      SAN9_S5_FLAG_CURRENT_CITY_ONLY | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_PATROL,
      SAN9_P1_M2B_PATROL_EVENT_ID, SAN9_P1_M2B_PATROL_HANDLER_VPTR,
      SAN9_P1_M2B_PATROL_COMMAND_VPTR, SAN9_P1_M2B_PATROL_ORDER_FLAG, 0x1E0u, 0u,
      SAN9_P1_M2B_PATROL_COST, SAN9_P1_M2B_PATROL_CURRENT_OFFSET, 0u, 0u, 1u,
      SAN9_P1_M2B_PATROL_MAXIMUM, SAN9_P1_M2B_PATROL_ABILITY_OFFSET,
      UINT32_C(0x004C1910), UINT32_C(0x004C1930), UINT32_C(0x004C1800),
      UINT32_C(0x004C1840), UINT32_C(0x00488460), UINT32_C(0x00488580),
      UINT32_C(0x00488690) },
    { SAN9_P1_M2B_COMMERCE_NATIVE_ID, SAN9_S5_REQUEST_KIND_APPLY_ONCE,
      SAN9_S5_FLAG_CURRENT_CITY_ONLY | SAN9_S5_FLAG_APPLY_ONCE,
      SAN9_P1_M2B_COMMERCE_EVENT_ID, SAN9_P1_M2B_COMMERCE_HANDLER_VPTR,
      SAN9_P1_M2B_COMMAND_VPTR, UINT32_C(0x10), 0x1E0u, 0u, UINT32_C(250),
      0x1CCu, 0u, 0x1D4u, 0u, 0u, 0x60u,
      UINT32_C(0x004C63E0), UINT32_C(0x004C6400), UINT32_C(0x004C62D0),
      UINT32_C(0x004C6310), UINT32_C(0x0048B380), UINT32_C(0x0048B4B0),
      UINT32_C(0x0048B5D0) },
    { SAN9_P1_M2B_CULTIVATE_NATIVE_ID, SAN9_S6_REQUEST_KIND_CULTIVATE_APPLY_ONCE,
      SAN9_S5_FLAG_CURRENT_CITY_ONLY | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_CULTIVATE,
      SAN9_P1_M2B_CULTIVATE_EVENT_ID, SAN9_P1_M2B_CULTIVATE_HANDLER_VPTR,
      SAN9_P1_M2B_CULTIVATE_COMMAND_VPTR, SAN9_P1_M2B_CULTIVATE_ORDER_FLAG,
      0x1E0u, 0u, SAN9_P1_M2B_CULTIVATE_COST,
      SAN9_P1_M2B_CULTIVATE_CURRENT_OFFSET, 0u,
      SAN9_P1_M2B_CULTIVATE_MAXIMUM_OFFSET, 0u, 0u, 0x60u,
      UINT32_C(0x004C1F30), UINT32_C(0x004C1F50), UINT32_C(0x004C1E20),
      UINT32_C(0x004C1E60), UINT32_C(0x00488B40), UINT32_C(0x00488C70),
      UINT32_C(0x00488D90) },
    { SAN9_P1_M2B_TRAIN_NATIVE_ID, SAN9_S6_REQUEST_KIND_TRAIN_APPLY_ONCE,
      SAN9_S5_FLAG_CURRENT_CITY_ONLY | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_TRAIN,
      SAN9_P1_M2B_TRAIN_EVENT_ID, SAN9_P1_M2B_TRAIN_HANDLER_VPTR,
      SAN9_P1_M2B_TRAIN_COMMAND_VPTR, SAN9_P1_M2B_TRAIN_ORDER_FLAG,
      SAN9_P1_M2B_TRAIN_ORDER_OFFSET, 1u, SAN9_P1_M2B_TRAIN_COST,
      SAN9_P1_M2B_TRAIN_CURRENT_OFFSET, 0u, 0u, 1u,
      SAN9_P1_M2B_TRAIN_MAXIMUM, SAN9_P1_M2B_TRAIN_ABILITY_OFFSET,
      UINT32_C(0x004C3870), UINT32_C(0x004C3890), UINT32_C(0x004C36D0),
      UINT32_C(0x004C37A0), UINT32_C(0x00489DC0), UINT32_C(0x00489F10),
      UINT32_C(0x00489FF0) },
    { SAN9_P1_M2B_REPAIR_NATIVE_ID, SAN9_S6_REQUEST_KIND_REPAIR_APPLY_ONCE,
      SAN9_S5_FLAG_CURRENT_CITY_ONLY | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_REPAIR,
      SAN9_P1_M2B_REPAIR_EVENT_ID, SAN9_P1_M2B_REPAIR_HANDLER_VPTR,
      SAN9_P1_M2B_REPAIR_COMMAND_VPTR, SAN9_P1_M2B_REPAIR_ORDER_FLAG,
      SAN9_P1_M2B_REPAIR_ORDER_OFFSET, 1u, SAN9_P1_M2B_REPAIR_COST,
      SAN9_P1_M2B_REPAIR_CURRENT_OFFSET, 1u,
      SAN9_P1_M2B_REPAIR_MAXIMUM_OFFSET, 0u, 0u,
      SAN9_P1_M2B_REPAIR_ABILITY_OFFSET,
      UINT32_C(0x004C2250), UINT32_C(0x004C2270), UINT32_C(0x004C2140),
      UINT32_C(0x004C2180), UINT32_C(0x004890C0), UINT32_C(0x004890E0),
      UINT32_C(0x00489310) }
};

static uint32_t s8_latched_or_shared_native_id(void)
{
    return g_runtime.s8_request_latched != 0u
        ? g_runtime.s8_native_id_latch
        : g_runtime.shared != NULL
            ? g_runtime.shared->operation.s5.request.native_command_id
            : UINT32_MAX;
}

static const S8NativeDescriptor *s8_descriptor(uint32_t native_id)
{
    uint32_t index;
    for (index = 0u; index < 5u; ++index) {
        if (g_s8_descriptors[index].native_id == native_id) {
            return &g_s8_descriptors[index];
        }
    }
    return NULL;
}

static const S8NativeDescriptor *s8_active_descriptor(void)
{
    return s8_descriptor(s8_latched_or_shared_native_id());
}

#define S8_DESC_GETTER(name, field) \
    static uint32_t name(void) { \
        const S8NativeDescriptor *descriptor = s8_active_descriptor(); \
        return descriptor == NULL ? 0u : descriptor->field; \
    }
S8_DESC_GETTER(s8_active_native_id, native_id)
S8_DESC_GETTER(s8_active_request_kind, request_kind)
S8_DESC_GETTER(s8_active_request_flags, request_flags)
S8_DESC_GETTER(s8_active_event_id, event_id)
S8_DESC_GETTER(s8_active_handler_vptr, handler_vptr)
S8_DESC_GETTER(s8_active_command_vptr, command_vptr)
S8_DESC_GETTER(s8_active_order_flag, order_flag)
S8_DESC_GETTER(s8_active_order_offset, order_offset)
S8_DESC_GETTER(s8_active_order_word, order_word)
S8_DESC_GETTER(s8_active_cost, cost)
S8_DESC_GETTER(s8_active_value_offset, value_offset)
S8_DESC_GETTER(s8_active_value_word, value_word)
S8_DESC_GETTER(s8_active_maximum_offset, maximum_offset)
S8_DESC_GETTER(s8_active_maximum_fixed, maximum_fixed)
S8_DESC_GETTER(s8_active_maximum_value, maximum_value)
S8_DESC_GETTER(s8_active_ability_offset, ability_offset)
#undef S8_DESC_GETTER

static int s8_request_still_latched(void)
{
    return g_runtime.shared != NULL && g_runtime.s8_request_latched != 0u
        && memcmp(&g_runtime.shared->operation.s5.request,
            &g_runtime.s8_request_latch,
            sizeof(g_runtime.s8_request_latch)) == 0;
}

static San9S5NoApplyGate *s8_active_gate(void)
{
    const S8NativeDescriptor *descriptor = s8_active_descriptor();
    size_t index = descriptor == NULL ? 0u
        : (size_t)(descriptor - g_s8_descriptors);
    return &g_runtime.s8_step_gates[index];
}

static uint32_t s8_active_pre_current(
    const San9S5CurrentContextSnapshot *snapshot)
{
    uint32_t native_id = s8_active_native_id();
    if (snapshot == NULL) return 0u;
    if (native_id == SAN9_P1_M2B_PATROL_NATIVE_ID) return snapshot->patrol_current;
    if (native_id == SAN9_P1_M2B_CULTIVATE_NATIVE_ID) return snapshot->cultivate_current;
    if (native_id == SAN9_P1_M2B_TRAIN_NATIVE_ID) return snapshot->train_morale;
    if (native_id == SAN9_P1_M2B_REPAIR_NATIVE_ID) return snapshot->repair_current;
    return snapshot->commerce_current;
}

static uint32_t s8_active_pre_maximum(
    const San9S5CurrentContextSnapshot *snapshot)
{
    uint32_t native_id = s8_active_native_id();
    if (snapshot == NULL) return 0u;
    if (native_id == SAN9_P1_M2B_PATROL_NATIVE_ID) return snapshot->patrol_maximum;
    if (native_id == SAN9_P1_M2B_CULTIVATE_NATIVE_ID) return snapshot->cultivate_maximum;
    if (native_id == SAN9_P1_M2B_TRAIN_NATIVE_ID) return snapshot->train_maximum;
    if (native_id == SAN9_P1_M2B_REPAIR_NATIVE_ID) return snapshot->repair_maximum;
    return snapshot->commerce_maximum;
}

static uint32_t s8_active_pre_order(
    const San9S5CurrentContextSnapshot *snapshot)
{
    uint32_t native_id = s8_active_native_id();
    if (snapshot == NULL) return 0u;
    if (native_id == SAN9_P1_M2B_TRAIN_NATIVE_ID) return snapshot->train_order_flags;
    if (native_id == SAN9_P1_M2B_REPAIR_NATIVE_ID) return snapshot->repair_order_flags;
    return snapshot->order_flags;
}

static void s8_select_exact_vtables(uint32_t native_command_id)
{
    const S8NativeDescriptor *descriptor = s8_descriptor(native_command_id);
    if (descriptor == NULL) return;
    g_commerce_vtable_exact[0] = descriptor->handler_slot0;
    g_commerce_vtable_exact[8] = descriptor->handler_slot8;
    g_commerce_vtable_exact[9] = descriptor->handler_slot9;
    g_commerce_vtable_exact[10] = descriptor->handler_slot10;
    g_command_vtable_exact[0] = descriptor->command_slot0;
    g_command_vtable_exact[8] = descriptor->command_slot8;
    g_command_vtable_exact[12] = descriptor->command_slot12;
}

static San9S5GateStatus s8_gate_accept(
    const San9S5NoApplyRequest *request,
    const uint8_t *key,
    size_t key_size,
    uint64_t now_ms,
    San9S5RequestStatus *request_status)
{
    switch (s8_active_native_id()) {
    case SAN9_P1_M2B_PATROL_NATIVE_ID:
        return san9_s6_patrol_apply_once_gate_accept(SAN9_ACTIVE_GATE,
            request, key, key_size, now_ms, request_status);
    case SAN9_P1_M2B_COMMERCE_NATIVE_ID:
        return san9_s5_apply_once_gate_accept(SAN9_ACTIVE_GATE,
            request, key, key_size, now_ms, request_status);
    case SAN9_P1_M2B_CULTIVATE_NATIVE_ID:
        return san9_s6_cultivate_apply_once_gate_accept(SAN9_ACTIVE_GATE,
            request, key, key_size, now_ms, request_status);
    case SAN9_P1_M2B_TRAIN_NATIVE_ID:
        return san9_s6_train_apply_once_gate_accept(SAN9_ACTIVE_GATE,
            request, key, key_size, now_ms, request_status);
    case SAN9_P1_M2B_REPAIR_NATIVE_ID:
        return san9_s6_repair_apply_once_gate_accept(SAN9_ACTIVE_GATE,
            request, key, key_size, now_ms, request_status);
    default:
        if (request_status != NULL) {
            *request_status = SAN9_S5_REQUEST_INVALID_ARGUMENT;
        }
        return SAN9_S5_GATE_INVALID_ARGUMENT;
    }
}

static int s8_request_digest(const San9S5NoApplyRequest *request,
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    switch (s8_active_native_id()) {
    case SAN9_P1_M2B_PATROL_NATIVE_ID:
        return san9_s6_patrol_apply_once_request_digest(request, output);
    case SAN9_P1_M2B_COMMERCE_NATIVE_ID:
        return san9_s5_apply_once_request_digest(request, output);
    case SAN9_P1_M2B_CULTIVATE_NATIVE_ID:
        return san9_s6_cultivate_apply_once_request_digest(request, output);
    case SAN9_P1_M2B_TRAIN_NATIVE_ID:
        return san9_s6_train_apply_once_request_digest(request, output);
    case SAN9_P1_M2B_REPAIR_NATIVE_ID:
        return san9_s6_repair_apply_once_request_digest(request, output);
    default:
        return 0;
    }
}
#endif
#endif
#endif

extern uint32_t san9_p1_s5_call_root_event(void *root, uint32_t event_code);
#if SAN9_APPLY_ONCE_BUILD
extern void *san9_p1_s5_native_list_ctor(void *list);
extern void san9_p1_s5_native_list_append(void *list, void *person);
extern void san9_p1_s5_native_list_dtor(void *list);
extern void *san9_p1_s5_native_alloc(uint32_t size);
extern void san9_p1_s5_native_free(void *allocation);
extern void *san9_p1_s5_native_commerce_ctor(
    void *allocation, void *handler, void *selection);
extern void *san9_p1_s6_native_cultivate_ctor(
    void *allocation, void *handler, void *selection);
extern void *san9_p1_s6_native_patrol_ctor(
    void *allocation, void *handler, void *selection);
extern void *san9_p1_s6_native_train_ctor(
    void *allocation, void *handler, void *selection);
extern void *san9_p1_s6_native_repair_ctor(
    void *allocation, void *handler, void *selection);
extern void san9_p1_s5_call_saved_command_apply(
    void *command, void *saved_original);
#endif

typedef enum S5StartOutcome {
    S5_START_REJECTED_PRE_EVENT = 0,
    S5_START_HANDLER_ARMED = 1,
    S5_START_RESTART_REQUIRED = 2
} S5StartOutcome;

static S5StartOutcome s5_start_no_apply(uint64_t now_ms);

static LONG shared_state(const San9P1M2bShared *shared)
{
    return InterlockedCompareExchange(
        (volatile LONG *)&shared->bootstrap_state, 0, 0);
}

static int readable_protection(DWORD protection)
{
    DWORD base = protection & 0xffu;
    return (protection & (PAGE_GUARD | PAGE_NOACCESS)) == 0u
        && (base == PAGE_READONLY || base == PAGE_READWRITE
            || base == PAGE_WRITECOPY || base == PAGE_EXECUTE_READ
            || base == PAGE_EXECUTE_READWRITE || base == PAGE_EXECUTE_WRITECOPY);
}

static int live_read(
    void *context,
    uint32_t address,
    uint8_t *output,
    size_t output_size)
{
    MEMORY_BASIC_INFORMATION info;
    uintptr_t start = address;
    uintptr_t end;
    uintptr_t region_end;
    (void)context;
    if (output == NULL || output_size == 0u || output_size > UINT32_MAX
        || start > UINT32_MAX - output_size
        || VirtualQuery((const void *)start, &info, sizeof(info)) != sizeof(info)
        || info.State != MEM_COMMIT || !readable_protection(info.Protect)) {
        return 0;
    }
    end = start + output_size;
    region_end = (uintptr_t)info.BaseAddress + info.RegionSize;
    if (region_end < (uintptr_t)info.BaseAddress || end > region_end) {
        return 0;
    }
    memcpy(output, (const void *)start, output_size);
    return 1;
}

static int live_query(
    void *context,
    uint32_t address,
    San9P1EasyMemoryRegion *output)
{
    MEMORY_BASIC_INFORMATION info;
    uintptr_t base;
    (void)context;
    if (output == NULL
        || VirtualQuery((const void *)(uintptr_t)address,
            &info, sizeof(info)) != sizeof(info)) {
        return 0;
    }
    base = (uintptr_t)info.BaseAddress;
    if (base > UINT32_MAX || info.RegionSize > UINT32_MAX
        || (uint64_t)base + (uint64_t)info.RegionSize > UINT64_C(0x100000000)) {
        return 0;
    }
    output->base_address = (uint32_t)base;
    output->region_size = (uint32_t)info.RegionSize;
    output->state = info.State;
    output->protection = san9_p1_m2b_canonical_easy_page_protection(
        address, output->base_address, output->region_size,
        (uint32_t)(uintptr_t)info.AllocationBase, info.AllocationProtect,
        info.State, info.Type, info.Protect);
    return 1;
}

static int snapshot_digest(
    const San9P1EasyBinding *binding,
    const San9P1EasyCallbacks *callbacks,
    int bridge_slot_installed,
    uint8_t output[SAN9_P1_DIGEST_SIZE])
{
    San9P1EasySnapshot first;
    San9P1EasySnapshot second;
    San9P1EasyReport report;
    San9P1Sha256Context sha;
    int ok = 0;
    memset(&first, 0, sizeof(first));
    memset(&second, 0, sizeof(second));
    memset(&report, 0, sizeof(report));
    if (san9_p1_easy_capture(binding, callbacks, &first)
        && san9_p1_easy_capture(binding, callbacks, &second)) {
        if (bridge_slot_installed) {
            uint32_t wrapper = (uint32_t)(uintptr_t)(void *)&San9BridgeP1M2b_IdleBridge;
            uint32_t first_slot = 0u;
            uint32_t second_slot = 0u;
            memcpy(&first_slot,
                first.idle_anchor_bytes[SAN9_P1_EASY_IDLE_SLOT_INDEX],
                sizeof(first_slot));
            memcpy(&second_slot,
                second.idle_anchor_bytes[SAN9_P1_EASY_IDLE_SLOT_INDEX],
                sizeof(second_slot));
            if (first_slot != wrapper || second_slot != wrapper
                || memcmp(first.idle_anchor_bytes[SAN9_P1_EASY_IDLE_SLOT_INDEX] + 4,
                    (const uint8_t[8]){0}, 8u) != 0
                || memcmp(second.idle_anchor_bytes[SAN9_P1_EASY_IDLE_SLOT_INDEX] + 4,
                    (const uint8_t[8]){0}, 8u) != 0) {
                goto cleanup;
            }
            memcpy(first.idle_anchor_bytes[SAN9_P1_EASY_IDLE_SLOT_INDEX],
                san9_p1_easy_generated_idle_anchors[
                    SAN9_P1_EASY_IDLE_SLOT_INDEX].expected,
                SAN9_P1_EASY_IDLE_BYTE_CAPACITY);
            memcpy(second.idle_anchor_bytes[SAN9_P1_EASY_IDLE_SLOT_INDEX],
                san9_p1_easy_generated_idle_anchors[
                    SAN9_P1_EASY_IDLE_SLOT_INDEX].expected,
                SAN9_P1_EASY_IDLE_BYTE_CAPACITY);
        }
        if (san9_p1_easy_verify_buffers(binding, &first, &second, &report)
            == SAN9_P1_EASY_INSTALLED
            && report.compatible_for_future_bridge == 1u) {
            if (bridge_slot_installed) {
                uint32_t wrapper = (uint32_t)(uintptr_t)(void *)&San9BridgeP1M2b_IdleBridge;
                memcpy(second.idle_anchor_bytes[SAN9_P1_EASY_IDLE_SLOT_INDEX],
                    &wrapper, sizeof(wrapper));
            }
            san9_p1_sha256_initialize(&sha);
            san9_p1_sha256_update(&sha, (const uint8_t *)&second, sizeof(second));
            san9_p1_sha256_finish(&sha, output);
            ok = 1;
        }
    }
cleanup:
    san9_p1_secure_zero(&first, sizeof(first));
    san9_p1_secure_zero(&second, sizeof(second));
    san9_p1_secure_zero(&report, sizeof(report));
    return ok;
}

static int exact_module_identity(const San9P1EasyBinding *binding)
{
    HMODULE game;
    HMODULE easy = NULL;
    MEMORY_BASIC_INFORMATION game_info;
    MEMORY_BASIC_INFORMATION easy_info;
    if (binding == NULL || !san9_p1_easy_binding_is_exact(binding)) {
        return 0;
    }
    game = GetModuleHandleW(NULL);
    if ((uintptr_t)game != binding->game_image_base
        || !GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS
                | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            (LPCWSTR)(uintptr_t)binding->easy_image_base, &easy)
        || (uintptr_t)easy != binding->easy_image_base
        || VirtualQuery((const void *)(uintptr_t)binding->game_image_base,
            &game_info, sizeof(game_info)) != sizeof(game_info)
        || VirtualQuery((const void *)(uintptr_t)binding->easy_image_base,
            &easy_info, sizeof(easy_info)) != sizeof(easy_info)) {
        return 0;
    }
    return game_info.Type == MEM_IMAGE && easy_info.Type == MEM_IMAGE
        && (uintptr_t)game_info.AllocationBase == binding->game_image_base
        && (uintptr_t)easy_info.AllocationBase == binding->easy_image_base;
}

static int runtime_action(
    void *context,
    San9P1M2FakeActionKind action,
    const San9P1Frame *authenticated_request,
    San9P1M2ResultEvidence *evidence)
{
    M2bRuntime *runtime = (M2bRuntime *)context;
    HMODULE pinned = NULL;
    LONG previous;
    uint32_t ordinal;
    if (runtime == NULL || authenticated_request == NULL) {
        return 0;
    }
    if (action == SAN9_P1_M2_FAKE_PRECOMMIT_VALIDATE) {
        return runtime->shared != NULL
            && runtime->owner_token != 0u
            && GetCurrentThreadId() == runtime->main_thread_id
            && exact_module_identity(&runtime->easy_binding)
            && IsWindow((HWND)(uintptr_t)runtime->shared->target_hwnd)
            && *(volatile uint32_t *)(uintptr_t)SAN9_P1_M2B_EXACT_APP_OBJECT
                == SAN9_P1_M2B_EXACT_APP_VTABLE
            && *(volatile uint32_t *)(uintptr_t)SAN9_P1_M2B_EXACT_IDLE_SLOT
                == SAN9_P1_M2B_EXACT_ORIGINAL_IDLE;
    }
    if (action == SAN9_P1_M2_FAKE_BEFORE_COMMIT_CAS) {
        return shared_state(runtime->shared) == SAN9_P1_M2B_BOOTSTRAP_COMMITTING
            && (uintptr_t)(void *)&San9BridgeP1M2b_IdleBridge <= UINT32_MAX;
    }
    if (action == SAN9_P1_M2_FAKE_COMMIT_SIDE_EFFECT) {
        if (!GetModuleHandleExW(
                GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS
                    | GET_MODULE_HANDLE_EX_FLAG_PIN,
                (LPCWSTR)(const void *)&San9BridgeP1M2b_GetMsgProc, &pinned)
            || pinned != runtime->module) {
            return 0;
        }
        previous = InterlockedCompareExchange(
            (volatile LONG *)(uintptr_t)SAN9_P1_M2B_EXACT_IDLE_SLOT,
            (LONG)(uintptr_t)(void *)&San9BridgeP1M2b_IdleBridge,
            (LONG)SAN9_P1_M2B_EXACT_ORIGINAL_IDLE);
        if ((uint32_t)previous != SAN9_P1_M2B_EXACT_ORIGINAL_IDLE) {
            return 0;
        }
        InterlockedExchange(&runtime->slot_committed, 1);
        return 1;
    }
    if (action != SAN9_P1_M2_FAKE_PING || evidence == NULL
        || runtime->shared == NULL || runtime->slot_committed != 1) {
        return 0;
    }
    ordinal = (uint32_t)InterlockedCompareExchange(
        (volatile LONG *)&runtime->shared->mailbox.accepted_ping_count, 0, 0);
    if (ordinal == 0u || ordinal > SAN9_P1_M2B_PING_COUNT) {
        return 0;
    }
    if (!snapshot_digest(&runtime->easy_binding, &runtime->easy_callbacks, 1,
            runtime->easy_snapshot_digest)) {
        return 0;
    }
    evidence->caller = SAN9_P1_M2B_EXACT_IDLE_CALLER;
    memcpy(evidence->easy_snapshot_digest, runtime->easy_snapshot_digest,
        sizeof(evidence->easy_snapshot_digest));
    runtime->shared->last_evidence_ordinal = ordinal;
    runtime->shared->last_evidence_caller = evidence->caller;
    memcpy(runtime->shared->last_easy_snapshot_digest,
        evidence->easy_snapshot_digest,
        sizeof(runtime->shared->last_easy_snapshot_digest));
    MemoryBarrier();
    return 1;
}

static void reject_bootstrap(
    San9P1M2bShared *shared,
    San9P1M2bResult result,
    int restart_required)
{
    if (shared == NULL) {
        return;
    }
    InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
    if (restart_required) {
        /* State is authoritative for readers.  Publish the restart result on
           both sides of the monotonic poison transition so a racing ordinary
           rejection cannot leave a non-restart terminal result behind. */
        InterlockedExchange((volatile LONG *)&shared->bootstrap_result,
            SAN9_P1_M2B_RESTART_REQUIRED);
        MemoryBarrier();
        InterlockedExchange((volatile LONG *)&shared->bootstrap_state,
            SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART);
        MemoryBarrier();
        InterlockedExchange((volatile LONG *)&shared->bootstrap_result,
            SAN9_P1_M2B_RESTART_REQUIRED);
        return;
    }
    for (;;) {
        LONG state = shared_state(shared);
        if (state == SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART) {
            InterlockedExchange((volatile LONG *)&shared->bootstrap_result,
                SAN9_P1_M2B_RESTART_REQUIRED);
            return;
        }
        if (InterlockedCompareExchange(
                (volatile LONG *)&shared->bootstrap_state,
                SAN9_P1_M2B_BOOTSTRAP_REJECTED, state) == state) {
            if (shared_state(shared)
                    == SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART) {
                InterlockedExchange((volatile LONG *)&shared->bootstrap_result,
                    SAN9_P1_M2B_RESTART_REQUIRED);
            } else {
                InterlockedExchange((volatile LONG *)&shared->bootstrap_result,
                    (LONG)result);
                if (shared_state(shared)
                        == SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART) {
                    InterlockedExchange(
                        (volatile LONG *)&shared->bootstrap_result,
                        SAN9_P1_M2B_RESTART_REQUIRED);
                }
            }
            return;
        }
    }
}

#if S5_NO_APPLY_BUILD
#define S5_HANDLER_CORPS_OFFSET 0x38u
#define S5_HANDLER_LIST_OFFSET 0x40u
#define S5_HANDLER_TARGET_OFFSET 0x60u
#define S5_MENU_FLAGS_OFFSET 0x0Cu
#define S5_MENU_ACTIVE_BIT 0x10u
#define S5_PERSON_BASE UINT32_C(0x01258EE0)
#define S5_PERSON_STRIDE UINT32_C(0x128)
#define S5_PERSON_COUNT 850u
#define S5_PERSON_LIST_VPTR UINT32_C(0x00606C8C)
#define S5_GLOBAL_SELECTED UINT32_C(0x015455AC)
#define S5_LIST_COUNT_OFFSET 0x0Cu
#define S5_LIST_NODE_SIZE 0x0Cu
#define S5_PERSON_ID_OFFSET 0x04u
#define S5_PERSON_POLITICS_OFFSET 0x60u
#define S5_PERSON_IDENTITY_OFFSET 0x84u
#define S5_PERSON_READY_FLAGS_OFFSET 0xE8u
#define S5_PERSON_RESIDENCE_OFFSET 0xF4u
#define S5_PERSON_CAPTURE_SIZE 0xF8u
#define S5_CONTROLLER_VPTR UINT32_C(0x00610BC8)
#define S5_CONTROLLER_STATE_OFFSET 0x34u
#define S5_CONTROLLER_TARGET_OFFSET 0x38u
#define S5_CONTROLLER_IDLE_STATE UINT32_C(0x3E9)
#define S5_CITY_VPTR UINT32_C(0x00605938)
#define S5_CITY_TYPE_OFFSET 0x06u
#define S5_CITY_TYPE 5u
#define S5_CITY_SELF_OFFSET 0xBCu
#define S5_CITY_CORPS_OFFSET 0xCCu
#define S5_CITY_COMMERCE_OFFSET 0x1CCu
#define S5_CITY_COMMERCE_MAX_OFFSET 0x1D4u
#define S5_CITY_ORDER_FLAGS_OFFSET 0x1E0u
#define S5_CITY_COMMERCE_ORDER_FLAG UINT32_C(0x10)
#define S5_CORPS_MONEY_OFFSET 0x14u
#define S5_COMMERCE_COST UINT32_C(250)

static int s5_business_mode(const San9P1M2bShared *shared)
{
    if (shared == NULL) {
        return 0;
    }
    if (shared->operation_mode == SAN9_P1_M2B_OPERATION_S5_NO_APPLY) {
        return 1;
    }
#if SAN9_APPLY_ONCE_BUILD
    return shared->operation_mode == SAN9_ACTIVE_APPLY_MODE;
#else
    return 0;
#endif
}

static int s5_direct_read(
    void *context, uint32_t address, void *output, size_t output_size)
{
    return live_read(context, address, (uint8_t *)output, output_size);
}

static LONG s5_terminal_read(const San9P1M2bShared *shared)
{
    return shared == NULL ? SAN9_P1_S5_TERMINAL_RESTART
        : InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state, 0, 0);
}

static LONG s5_terminal_mark_restart(San9P1M2bShared *shared)
{
    LONG state;
    if (shared == NULL) {
        return SAN9_P1_S5_TERMINAL_RESTART;
    }
    state = s5_terminal_read(shared);
    for (;;) {
        if (state == SAN9_P1_S5_TERMINAL_SUCCESS
            || state == SAN9_P1_S5_TERMINAL_REJECTED
            || state == SAN9_P1_S5_TERMINAL_TIMEOUT
            || state == SAN9_P1_S5_TERMINAL_RESTART) {
            return state;
        }
        {
            LONG observed = InterlockedCompareExchange(
                (volatile LONG *)&shared->operation.s5.terminal_state,
                SAN9_P1_S5_TERMINAL_RESTART, state);
            if (observed == state) {
                return SAN9_P1_S5_TERMINAL_RESTART;
            }
            state = observed;
        }
    }
}

static void s5_poison(San9S5FaultCode fault)
{
    San9P1M2bShared *shared = g_runtime.shared;
    uint32_t result = shared != NULL
            && shared->operation_mode == SAN9_ACTIVE_APPLY_MODE
        ? SAN9_ACTIVE_FAILURE
        : SAN9_P1_M2B_S5_NO_APPLY_FAILED;
    LONG terminal = s5_terminal_mark_restart(shared);
    if (terminal == SAN9_P1_S5_TERMINAL_SUCCESS
        || terminal == SAN9_P1_S5_TERMINAL_REJECTED) {
        return;
    }
    if (shared != NULL) {
        (void)san9_s5_no_apply_machine_fail(
            &shared->operation.s5.machine, fault);
        atomic_store_explicit(&shared->operation.s5.machine.state,
            SAN9_S5_STATE_RESTART_REQUIRED, memory_order_release);
        shared->operation.s5.evidence.restart_required = 1u;
        shared->operation.s5.evidence.machine_state =
            (uint32_t)san9_s5_no_apply_machine_state(
                &shared->operation.s5.machine);
        (void)san9_p1_s5_evidence_sign(&shared->operation.s5.evidence,
            shared->mailbox.hmac_key, sizeof(shared->mailbox.hmac_key));
    }
    reject_bootstrap(shared, result, 1);
}

static int s5_context_matches_request(
    const San9S5CurrentContextSnapshot *snapshot,
    const San9S5NoApplyRequest *request)
{
    uint32_t index;
    if (snapshot == NULL || request == NULL
        || snapshot->city_id != request->expected_city_id
        || snapshot->corps_id != request->expected_corps_id
        || snapshot->exact_top5_count != SAN9_S5_TOP5_COUNT
        || !san9_p1_constant_time_equal(snapshot->canonical_digest,
            request->precondition_digest, SAN9_S5_DIGEST_SIZE)) {
        return 0;
    }
    for (index = 0u; index < SAN9_S5_TOP5_COUNT; ++index) {
        if (snapshot->top5[index].person_id != request->expected_person_ids[index]) {
            return 0;
        }
    }
    return 1;
}

static int s5_read_root_handler(void *root, void **handler_output)
{
    uint32_t handler = 0u;
    if (root == NULL || handler_output == NULL
        || !live_read(NULL,
            (uint32_t)(uintptr_t)root + SAN9_P1_M2B_ROOT_HANDLER_OFFSET,
            (uint8_t *)&handler, sizeof(handler))
        || handler < UINT32_C(0x10000) || (handler & 3u) != 0u) {
        return 0;
    }
    *handler_output = (void *)(uintptr_t)handler;
    return 1;
}

#if SAN9_APPLY_ONCE_BUILD
static void SAN9_P1_M2B_FASTCALL s5_command_apply_shadow(
    void *command, void *unused_edx);

static uint16_t s5_bridge_u16(const uint8_t *bytes, size_t offset)
{
    uint16_t value = 0u;
    memcpy(&value, bytes + offset, sizeof(value));
    return value;
}

static uint32_t s5_bridge_u32(const uint8_t *bytes, size_t offset)
{
    uint32_t value = 0u;
    memcpy(&value, bytes + offset, sizeof(value));
    return value;
}

static int s5_person_still_frozen(
    const San9S5CommerceOfficer *officer, uint32_t expected_id)
{
    uint8_t person[S5_PERSON_CAPTURE_SIZE];
    uint32_t table_id;
    if (officer == NULL || officer->person_pointer < S5_PERSON_BASE
        || (officer->person_pointer - S5_PERSON_BASE) % S5_PERSON_STRIDE != 0u) {
        return 0;
    }
    table_id = (officer->person_pointer - S5_PERSON_BASE) / S5_PERSON_STRIDE;
    return table_id == expected_id && table_id < S5_PERSON_COUNT
        && live_read(NULL, officer->person_pointer, person, sizeof(person))
        && s5_bridge_u16(person, S5_PERSON_ID_OFFSET) == expected_id
        && s5_bridge_u32(person, SAN9_ACTIVE_ABILITY_OFFSET)
            == officer->effective_politics
        && s5_bridge_u32(person, S5_PERSON_IDENTITY_OFFSET)
            == officer->identity
        && s5_bridge_u32(person, S5_PERSON_READY_FLAGS_OFFSET)
            == officer->ready_flags
        && (officer->ready_flags & UINT32_C(0x1000)) == 0u
        && s5_bridge_u32(person, S5_PERSON_RESIDENCE_OFFSET)
            == officer->residence_pointer;
}

static int s5_list_contains_exact_five(
    uint32_t list_address,
    const uint32_t expected[SAN9_S5_TOP5_COUNT],
    int require_exact_order)
{
    uint8_t list[SAN9_P1_M2B_PERSON_LIST_SIZE];
    uint8_t node[S5_LIST_NODE_SIZE];
    uint32_t current;
    uint32_t previous = 0u;
    uint32_t tail;
    uint32_t count;
    uint32_t index;
    uint32_t seen = 0u;
    if (expected == NULL
        || !live_read(NULL, list_address, list, sizeof(list))
        || s5_bridge_u32(list, 0u) != S5_PERSON_LIST_VPTR) {
        return 0;
    }
    current = s5_bridge_u32(list, 4u);
    tail = s5_bridge_u32(list, 8u);
    count = s5_bridge_u32(list, S5_LIST_COUNT_OFFSET);
    if (count < SAN9_S5_TOP5_COUNT || count > S5_PERSON_COUNT
        || (require_exact_order && count != SAN9_S5_TOP5_COUNT)) {
        return 0;
    }
    for (index = 0u; index < count; ++index) {
        uint32_t next;
        uint32_t person;
        uint32_t expected_index;
        if (current < UINT32_C(0x10000)
            || !live_read(NULL, current, node, sizeof(node))
            || s5_bridge_u32(node, 4u) != previous) {
            return 0;
        }
        next = s5_bridge_u32(node, 0u);
        person = s5_bridge_u32(node, 8u);
        if (require_exact_order) {
            if (person != expected[index]) {
                return 0;
            }
        } else {
            for (expected_index = 0u;
                    expected_index < SAN9_S5_TOP5_COUNT; ++expected_index) {
                if (person == expected[expected_index]) {
                    if ((seen & (1u << expected_index)) != 0u) {
                        return 0;
                    }
                    seen |= 1u << expected_index;
                }
            }
        }
        previous = current;
        current = next;
    }
    return current == 0u && previous == tail
        && (require_exact_order || seen == UINT32_C(0x1F));
}

static int s5_command_shadow_arm(void *command)
{
    uint32_t original[SAN9_P1_M2B_COMMAND_VTABLE_SLOT_COUNT];
    LONG previous;
    if (command == NULL
        || (uintptr_t)(void *)g_command_shadow_vtable > UINT32_MAX
        || !live_read(NULL, SAN9_ACTIVE_COMMAND_VPTR,
            (uint8_t *)original, sizeof(original))
        || memcmp(original, g_command_vtable_exact, sizeof(original)) != 0) {
        return 0;
    }
    memcpy(g_command_shadow_vtable, original, sizeof(g_command_shadow_vtable));
    g_command_shadow_vtable[
        SAN9_P1_M2B_COMMAND_APPLY_SLOT_OFFSET / sizeof(uintptr_t)] =
        (uintptr_t)(void *)&s5_command_apply_shadow;
    g_apply_command = command;
    InterlockedExchange(&g_apply_enter_hits, 0);
    InterlockedExchange(&g_apply_return_hits, 0);
    InterlockedExchange(&g_apply_live_authorization, 1);
    MemoryBarrier();
    previous = InterlockedCompareExchange((volatile LONG *)command,
        (LONG)(uintptr_t)(void *)g_command_shadow_vtable,
        (LONG)SAN9_ACTIVE_COMMAND_VPTR);
    if ((uint32_t)previous != SAN9_ACTIVE_COMMAND_VPTR) {
        g_apply_command = NULL;
        InterlockedExchange(&g_apply_live_authorization, 0);
        return 0;
    }
    return 1;
}

static void SAN9_P1_M2B_FASTCALL s5_command_apply_shadow(
    void *command, void *unused_edx)
{
    San9P1M2bShared *shared = g_runtime.shared;
    LONG previous;
    int exact;
    (void)unused_edx;
    exact = command != NULL && command == g_apply_command && shared != NULL
        && shared->operation_mode == SAN9_ACTIVE_APPLY_MODE
#if SAN9_COMBINED_BATCH_BUILD
        && s8_request_still_latched()
#endif
        && GetCurrentThreadId() == g_runtime.main_thread_id
        && InterlockedCompareExchange(&g_apply_live_authorization, 0, 1) == 1
        && InterlockedCompareExchange(&g_apply_enter_hits, 1, 0) == 0;
    previous = command == NULL ? 0 : InterlockedCompareExchange(
        (volatile LONG *)command, (LONG)SAN9_ACTIVE_COMMAND_VPTR,
        (LONG)(uintptr_t)(void *)g_command_shadow_vtable);
    if ((uintptr_t)(uint32_t)previous
            != (uintptr_t)(void *)g_command_shadow_vtable) {
        exact = 0;
    }
    g_apply_command = NULL;
    if (exact) {
        san9_p1_s5_call_saved_command_apply(command,
            (void *)(uintptr_t)g_command_vtable_exact[
                SAN9_P1_M2B_COMMAND_APPLY_SLOT_OFFSET / sizeof(uintptr_t)]);
        (void)InterlockedIncrement(&g_apply_return_hits);
    }
    if (shared != NULL) {
        shared->operation.s5.evidence.observed_apply_count =
            (uint32_t)InterlockedCompareExchange(&g_apply_return_hits, 0, 0);
        if (exact) {
            uint32_t zero = 0u;
            (void)atomic_compare_exchange_strong_explicit(
                &shared->operation.s5.machine.observed_apply_count,
                &zero, 1u, memory_order_acq_rel, memory_order_acquire);
        }
    }
    if (!exact) {
        s5_poison(SAN9_S5_FAULT_APPLY_OBSERVED);
    }
}

static void *s5_construct_apply_once_command(
    void *handler, San9P1M2bShared *shared)
{
    _Alignas(4) uint8_t selection[SAN9_P1_M2B_PERSON_LIST_SIZE];
    uint8_t handler_prefix[0x70u];
    uint8_t command[SAN9_P1_M2B_COMMAND_ALLOCATION_SIZE];
    uint32_t expected[SAN9_S5_TOP5_COUNT];
    void *allocation = NULL;
    void *constructed = NULL;
    uint32_t index;
    int list_constructed = 0;
    const San9S5NoApplyRequest *active_request = shared == NULL ? NULL
        : &shared->operation.s5.request;
#if SAN9_COMBINED_BATCH_BUILD
    if (shared != NULL && shared->operation_mode
            == SAN9_ACTIVE_APPLY_MODE) {
        active_request = &g_runtime.s8_request_latch;
    }
#endif
    if (handler == NULL || shared == NULL
        || shared->operation_mode != SAN9_ACTIVE_APPLY_MODE
        || active_request == NULL
        || active_request->kind != SAN9_ACTIVE_REQUEST_KIND
        || active_request->flags != SAN9_ACTIVE_REQUEST_FLAGS
#if SAN9_COMBINED_BATCH_BUILD
        || !s8_request_still_latched()
#endif
        || !live_read(NULL, (uint32_t)(uintptr_t)handler,
            handler_prefix, sizeof(handler_prefix))
        || s5_bridge_u32(handler_prefix, 0u)
            != SAN9_ACTIVE_HANDLER_VPTR
        || s5_bridge_u32(handler_prefix, 0x0Cu) != 0u
        || s5_bridge_u32(handler_prefix, 0x10u)
            != (uint32_t)(uintptr_t)handler
        || s5_bridge_u32(handler_prefix, S5_HANDLER_CORPS_OFFSET)
            != g_runtime.s5_pre.corps_pointer
        || s5_bridge_u32(handler_prefix, S5_HANDLER_TARGET_OFFSET)
            != g_runtime.s5_pre.city_pointer) {
        return NULL;
    }
    for (index = 0u; index < SAN9_S5_TOP5_COUNT; ++index) {
        expected[index] = g_runtime.s5_pre.top5[index].person_pointer;
        if (g_runtime.s5_pre.top5[index].person_id
                != active_request->expected_person_ids[index]
            || !s5_person_still_frozen(&g_runtime.s5_pre.top5[index],
                active_request->expected_person_ids[index])) {
            return NULL;
        }
    }
    if (!s5_list_contains_exact_five(
            (uint32_t)(uintptr_t)handler + S5_HANDLER_LIST_OFFSET,
            expected, 0)) {
        return NULL;
    }
    memset(selection, 0, sizeof(selection));
    if (san9_p1_s5_native_list_ctor(selection) != selection) {
        return NULL;
    }
    list_constructed = 1;
    for (index = 0u; index < SAN9_S5_TOP5_COUNT; ++index) {
        san9_p1_s5_native_list_append(selection,
            (void *)(uintptr_t)expected[index]);
    }
    if (!s5_list_contains_exact_five(
            (uint32_t)(uintptr_t)(void *)selection, expected, 1)) {
        goto fail;
    }
    allocation = san9_p1_s5_native_alloc(
        SAN9_P1_M2B_COMMAND_ALLOCATION_SIZE);
    if (allocation == NULL
        || !san9_p1_s5_deadline_is_fresh(GetTickCount64(),
            g_runtime.s5_request_expires_at_ms)
        || shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY) {
        goto fail;
    }
#if SAN9_COMBINED_BATCH_BUILD
    switch (SAN9_ACTIVE_NATIVE_ID) {
    case SAN9_P1_M2B_PATROL_NATIVE_ID:
        constructed = san9_p1_s6_native_patrol_ctor(
            allocation, handler, selection);
        break;
    case SAN9_P1_M2B_COMMERCE_NATIVE_ID:
        constructed = san9_p1_s5_native_commerce_ctor(
            allocation, handler, selection);
        break;
    case SAN9_P1_M2B_CULTIVATE_NATIVE_ID:
        constructed = san9_p1_s6_native_cultivate_ctor(
            allocation, handler, selection);
        break;
    case SAN9_P1_M2B_TRAIN_NATIVE_ID:
        constructed = san9_p1_s6_native_train_ctor(
            allocation, handler, selection);
        break;
    case SAN9_P1_M2B_REPAIR_NATIVE_ID:
        constructed = san9_p1_s6_native_repair_ctor(
            allocation, handler, selection);
        break;
    default:
        constructed = NULL;
        break;
    }
#elif defined(S6_REPAIR_APPLY_ONCE_BUILD) && S6_REPAIR_APPLY_ONCE_BUILD
    constructed = san9_p1_s6_native_repair_ctor(
        allocation, handler, selection);
#elif defined(S6_TRAIN_APPLY_ONCE_BUILD) && S6_TRAIN_APPLY_ONCE_BUILD
    constructed = san9_p1_s6_native_train_ctor(
        allocation, handler, selection);
#elif defined(S6_PATROL_APPLY_ONCE_BUILD) && S6_PATROL_APPLY_ONCE_BUILD
    constructed = san9_p1_s6_native_patrol_ctor(
        allocation, handler, selection);
#elif defined(S6_CULTIVATE_APPLY_ONCE_BUILD) && S6_CULTIVATE_APPLY_ONCE_BUILD
    constructed = san9_p1_s6_native_cultivate_ctor(
        allocation, handler, selection);
#else
    constructed = san9_p1_s5_native_commerce_ctor(
        allocation, handler, selection);
#endif
    san9_p1_s5_native_list_dtor(selection);
    list_constructed = 0;
    if (constructed != allocation
        || !live_read(NULL, (uint32_t)(uintptr_t)constructed,
            command, sizeof(command))
        || s5_bridge_u32(command, 0u) != SAN9_ACTIVE_COMMAND_VPTR
        || s5_bridge_u32(command, 0x30u) != UINT32_C(0x3E8)
        || s5_bridge_u32(command, 0x34u) != SAN9_ACTIVE_NATIVE_ID
        || s5_bridge_u32(command, 0x38u) != 0u
        || s5_bridge_u32(command, 0x3Cu) != 2u
        || !s5_list_contains_exact_five(S5_GLOBAL_SELECTED, expected, 1)
        || !s5_command_shadow_arm(constructed)) {
        return NULL;
    }
    return constructed;
fail:
    if (allocation != NULL && constructed == NULL) {
        san9_p1_s5_native_free(allocation);
    }
    if (list_constructed) {
        san9_p1_s5_native_list_dtor(selection);
    }
    return NULL;
}
#endif

#if defined(__GNUC__)
__attribute__((used, noinline))
#endif
static void *SAN9_P1_M2B_FASTCALL commerce_shadow_execute(
    void *handler,
    void *unused_edx)
{
    LONG previous;
#if SAN9_APPLY_ONCE_BUILD
    void *command = NULL;
#endif
    (void)unused_edx;
    San9P1M2bShared *shared = g_runtime.shared;
    if (handler == NULL || handler != g_commerce_handler || shared == NULL
        || !s5_business_mode(shared)
        || GetCurrentThreadId() != g_runtime.main_thread_id
        || shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY
        || s5_terminal_read(shared) != SAN9_P1_S5_TERMINAL_OPEN
        || !san9_p1_s5_deadline_is_fresh(GetTickCount64(),
            g_runtime.s5_request_expires_at_ms)
        || GetTickCount64() > g_runtime.s5_settle_deadline_ms
        || san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
            != SAN9_S5_STATE_SHADOW_ARMED
        || InterlockedCompareExchange(
            &g_commerce_live_authorization, 0, 1) != 1
        || InterlockedCompareExchange(&g_commerce_execute_hits, 1, 0) != 0) {
        if (handler != NULL) {
            (void)InterlockedCompareExchange((volatile LONG *)handler,
                (LONG)SAN9_ACTIVE_HANDLER_VPTR,
                (LONG)(uintptr_t)(void *)g_commerce_shadow_vtable);
        }
        g_commerce_handler = NULL;
        s5_poison(SAN9_S5_FAULT_EXECUTE);
        return NULL;
    }
    previous = InterlockedCompareExchange((volatile LONG *)handler,
        (LONG)SAN9_ACTIVE_HANDLER_VPTR,
        (LONG)(uintptr_t)(void *)g_commerce_shadow_vtable);
    if ((uintptr_t)(uint32_t)previous
            != (uintptr_t)(void *)g_commerce_shadow_vtable) {
        g_commerce_handler = NULL;
        s5_poison(SAN9_S5_FAULT_RESTORE);
        return NULL;
    }
    g_commerce_handler = NULL;
    if (san9_s5_no_apply_machine_mark_execute_entered(
            &shared->operation.s5.machine) != SAN9_S5_MACHINE_OK
        || san9_s5_no_apply_machine_mark_vptr_restored(
            &shared->operation.s5.machine) != SAN9_S5_MACHINE_OK) {
        s5_poison(SAN9_S5_FAULT_RESTORE);
        return NULL;
    }
    shared->operation.s5.evidence.execute_enter_count = 1u;
    shared->operation.s5.evidence.restore_count = 1u;
#if SAN9_APPLY_ONCE_BUILD
    if (shared->operation_mode == SAN9_ACTIVE_APPLY_MODE) {
        command = s5_construct_apply_once_command(handler, shared);
        if (command == NULL) {
            s5_poison(SAN9_S5_FAULT_EXECUTE);
        }
        return command;
    }
#endif
    return NULL;
}

#if defined(__GNUC__)
__attribute__((used, noinline))
#endif
static uint32_t commerce_shadow_arm(
    void *root,
    void *handler,
    const San9S5NoApplyRequest *request,
    uint32_t event_id)
{
    void *current_handler = NULL;
    uint32_t original_vtable[SAN9_P1_M2B_COMMERCE_VTABLE_SLOT_COUNT];
    LONG previous;
    if (InterlockedCompareExchange(&g_commerce_live_authorization, 0, 0) != 1
        || root == NULL || handler == NULL || request == NULL
        || event_id != SAN9_ACTIVE_EVENT_ID
        || (uintptr_t)(void *)g_commerce_shadow_vtable > UINT32_MAX) {
        return SAN9_P1_M2B_COMMERCE_ABI_PENDING;
    }
    memset(original_vtable, 0, sizeof(original_vtable));
    if (!s5_read_root_handler(root, &current_handler)
        || current_handler != handler
        || !live_read(NULL, SAN9_ACTIVE_HANDLER_VPTR,
            (uint8_t *)original_vtable, sizeof(original_vtable))
        || memcmp(original_vtable, g_commerce_vtable_exact,
            sizeof(original_vtable)) != 0) {
        return SAN9_P1_M2B_RESTART_REQUIRED;
    }
    memcpy(g_commerce_shadow_vtable, original_vtable,
        sizeof(g_commerce_shadow_vtable));
    g_commerce_shadow_vtable[
        SAN9_P1_M2B_COMMERCE_EXECUTE_SLOT_OFFSET / sizeof(uintptr_t)] =
        (uintptr_t)(void *)&commerce_shadow_execute;
    g_commerce_handler = handler;
    MemoryBarrier();
    if (!s5_read_root_handler(root, &current_handler)
        || current_handler != handler) {
        g_commerce_handler = NULL;
        return SAN9_P1_M2B_RESTART_REQUIRED;
    }
    previous = InterlockedCompareExchange((volatile LONG *)handler,
        (LONG)(uintptr_t)(void *)g_commerce_shadow_vtable,
        (LONG)SAN9_ACTIVE_HANDLER_VPTR);
    if ((uint32_t)previous != SAN9_ACTIVE_HANDLER_VPTR) {
        g_commerce_handler = NULL;
        reject_bootstrap(g_runtime.shared, SAN9_P1_M2B_RESTART_REQUIRED, 1);
        return SAN9_P1_M2B_RESTART_REQUIRED;
    }
    return SAN9_P1_M2B_OK;
}
#endif

#define S3_SCHEDULER_VPTR UINT32_C(0x00607560)
#define S3_CONTROLLER_VPTR UINT32_C(0x00610BC8)
#define S3_COMMERCE_OUTER_VPTR UINT32_C(0x0060CCB0)
#define S3_SELECTOR_VPTR UINT32_C(0x0061F0B8)
#define S3_PERSON_LIST_VPTR UINT32_C(0x00606C8C)
#define S3_CURRENT_BUILDING UINT32_C(0x01232474)
#define S3_GLOBAL_SELECTED UINT32_C(0x015455AC)
#define S3_MAX_CHAIN_DEPTH 64u

static uint32_t s3_u32(const uint8_t *bytes, size_t offset)
{
    uint32_t value = 0u;
    memcpy(&value, bytes + offset, sizeof(value));
    return value;
}

static int s3_read(
    San9P1S3ReadContext *context,
    uint32_t opcode,
    uint8_t *bytes,
    size_t capacity,
    size_t expected)
{
    size_t actual = 0u;
    memset(bytes, 0, capacity);
    return san9_p1_s3_read_index(context, opcode, bytes, capacity, &actual)
        && actual == expected;
}

static uint32_t s3_chain_hash(uint32_t hash, uint32_t value)
{
    hash ^= value;
    hash *= UINT32_C(16777619);
    return hash;
}

static int s3_capture_record(San9P1S3TraceRecord *record)
{
    San9P1S3ReadContext context;
    uint8_t bytes[SAN9_P1_S3_READ_MAX];
    uint32_t visited[S3_MAX_CHAIN_DEPTH];
    uint32_t current;
    uint32_t depth;
    uint32_t child = 0u;
    uint32_t pending = 0u;
    uint32_t vptr = 0u;
    uint32_t hash = UINT32_C(2166136261);
    memset(record, 0, sizeof(*record));
    memset(&context, 0, sizeof(context));
    memset(visited, 0, sizeof(visited));
    context.read = live_read;
    context.bases[SAN9_P1_S3_SYMBOL_APP] = SAN9_P1_M2B_EXACT_APP_OBJECT;
    context.bases[SAN9_P1_S3_SYMBOL_GLOBAL_SELECTED] = S3_GLOBAL_SELECTED;
    context.bases[SAN9_P1_S3_SYMBOL_CURRENT_BUILDING] = S3_CURRENT_BUILDING;
    if (!s3_read(&context, SAN9_P1_S3_READ_APP_HEADER, bytes,
            sizeof(bytes), 8u)
        || s3_u32(bytes, 0u) != SAN9_P1_M2B_EXACT_APP_VTABLE) {
        record->failure_code = 1u;
        return 0;
    }
    context.bases[SAN9_P1_S3_SYMBOL_WINDOW] = s3_u32(bytes, 4u);
    if (!s3_read(&context, SAN9_P1_S3_READ_WINDOW_OWNER, bytes,
            sizeof(bytes), 4u)) {
        record->failure_code = 2u;
        return 0;
    }
    context.bases[SAN9_P1_S3_SYMBOL_OWNER] = s3_u32(bytes, 0u);
    if (!s3_read(&context, SAN9_P1_S3_READ_OWNER_SCENE, bytes,
            sizeof(bytes), 4u)) {
        record->failure_code = 3u;
        return 0;
    }
    context.bases[SAN9_P1_S3_SYMBOL_SCENE] = s3_u32(bytes, 0u);
    if (!s3_read(&context, SAN9_P1_S3_READ_SCENE_ROOT, bytes,
            sizeof(bytes), 4u)) {
        record->failure_code = 4u;
        return 0;
    }
    current = s3_u32(bytes, 0u);
    record->scheduler_pointer = current;
    for (depth = 0u; depth < S3_MAX_CHAIN_DEPTH; ++depth) {
        uint32_t index;
        if (current == 0u || (current & 3u) != 0u) {
            record->failure_code = 5u;
            return 0;
        }
        for (index = 0u; index < depth; ++index) {
            if (visited[index] == current) {
                record->failure_code = 6u;
                return 0;
            }
        }
        visited[depth] = current;
        context.bases[SAN9_P1_S3_SYMBOL_TASK] = current;
        if (!s3_read(&context, SAN9_P1_S3_READ_TASK_HEADER, bytes,
                sizeof(bytes), 0x14u)) {
            record->failure_code = 7u;
            return 0;
        }
        vptr = s3_u32(bytes, 0u);
        child = s3_u32(bytes, 0x0Cu);
        pending = s3_u32(bytes, 0x10u);
        if (vptr < UINT32_C(0x00400000)
            || vptr >= UINT32_C(0x01B59000)
            || (depth == 0u && vptr != S3_SCHEDULER_VPTR)) {
            record->failure_code = 8u;
            return 0;
        }
        hash = s3_chain_hash(hash, current);
        hash = s3_chain_hash(hash, vptr);
        hash = s3_chain_hash(hash, child);
        hash = s3_chain_hash(hash, pending);
        if (depth < SAN9_P1_S3_CHAIN_VPTR_CAPACITY) {
            record->chain_vptr[depth] = vptr;
        }
        if (depth == 0u) {
            record->scheduler_pending = pending;
        }
        if (vptr == S3_CONTROLLER_VPTR) {
            if (record->controller_pointer != 0u) {
                record->failure_code = 9u;
                return 0;
            }
            record->controller_pointer = current;
            record->controller_child = child;
            record->controller_pending = pending;
            context.bases[SAN9_P1_S3_SYMBOL_CONTROLLER] = current;
            if (!s3_read(&context, SAN9_P1_S3_READ_CONTROLLER_FIELDS, bytes,
                    sizeof(bytes), 0x0Cu)) {
                record->failure_code = 10u;
                return 0;
            }
            record->controller_corps = s3_u32(bytes, 0u);
            record->controller_state = s3_u32(bytes, 4u);
            record->controller_target = s3_u32(bytes, 8u);
            record->flags |= SAN9_P1_S3_FLAG_CONTROLLER;
        } else if (vptr == SAN9_P1_M2B_COMMERCE_HANDLER_VPTR) {
            context.bases[SAN9_P1_S3_SYMBOL_HANDLER] = current;
            if (!s3_read(&context, SAN9_P1_S3_READ_HANDLER_PREFIX, bytes,
                    sizeof(bytes), 0x70u)) {
                record->failure_code = 11u;
                return 0;
            }
            record->handler_pointer = current;
            record->handler_vptr = s3_u32(bytes, 0u);
            record->handler_state = s3_u32(bytes, 0x34u);
            record->handler_corps = s3_u32(bytes, 0x38u);
            record->handler_list_vptr = s3_u32(bytes, 0x40u);
            record->handler_list_first = s3_u32(bytes, 0x44u);
            record->handler_list_last = s3_u32(bytes, 0x48u);
            record->handler_list_count = s3_u32(bytes, 0x4Cu);
            memcpy(record->handler_list_tail, bytes + 0x50u,
                sizeof(record->handler_list_tail));
            record->handler_target = s3_u32(bytes, 0x60u);
            record->flags |= SAN9_P1_S3_FLAG_COMMERCE_HANDLER;
        } else if (vptr == S3_COMMERCE_OUTER_VPTR) {
            record->outer_pointer = current;
            record->outer_vptr = vptr;
            context.bases[SAN9_P1_S3_SYMBOL_OUTER] = current;
            if (!s3_read(&context, SAN9_P1_S3_READ_OUTER_RESULT, bytes,
                    sizeof(bytes), 4u)) {
                record->failure_code = 12u;
                return 0;
            }
            record->outer_result = s3_u32(bytes, 0u);
            if (!s3_read(&context, SAN9_P1_S3_READ_OUTER_COMMITTED, bytes,
                    sizeof(bytes), 0x10u)) {
                record->failure_code = 13u;
                return 0;
            }
            record->outer_committed_count = s3_u32(bytes, 0x0Cu);
            if (!s3_read(&context, SAN9_P1_S3_READ_OUTER_SOURCE, bytes,
                    sizeof(bytes), 0x10u)) {
                record->failure_code = 14u;
                return 0;
            }
            record->outer_source_count = s3_u32(bytes, 0x0Cu);
            if (!s3_read(&context, SAN9_P1_S3_READ_OUTER_WORKING, bytes,
                    sizeof(bytes), 0x10u)) {
                record->failure_code = 15u;
                return 0;
            }
            record->outer_working_count = s3_u32(bytes, 0x0Cu);
            record->flags |= SAN9_P1_S3_FLAG_COMMERCE_OUTER;
        } else if (vptr == S3_SELECTOR_VPTR) {
            record->selector_pointer = current;
            record->selector_vptr = vptr;
            context.bases[SAN9_P1_S3_SYMBOL_SELECTOR] = current;
            if (!s3_read(&context, SAN9_P1_S3_READ_SELECTOR_SELECTION, bytes,
                    sizeof(bytes), 0x30u)) {
                record->failure_code = 16u;
                return 0;
            }
            record->selector_maximum = s3_u32(bytes, 0x2Cu);
            record->flags |= SAN9_P1_S3_FLAG_SELECTOR;
        } else if (vptr == SAN9_P1_M2B_COMMAND_VPTR) {
            record->command_pointer = current;
            record->command_vptr = vptr;
            record->flags |= SAN9_P1_S3_FLAG_COMMAND;
        }
        record->leaf_pointer = current;
        record->leaf_vptr = vptr;
        record->leaf_child = child;
        record->leaf_pending = pending;
        if (child == 0u) {
            record->task_depth = depth;
            break;
        }
        current = child;
    }
    if (depth == S3_MAX_CHAIN_DEPTH) {
        record->failure_code = 17u;
        return 0;
    }
    context.bases[SAN9_P1_S3_SYMBOL_GLOBAL_SELECTED] = S3_GLOBAL_SELECTED;
    if (!s3_read(&context, SAN9_P1_S3_READ_GLOBAL_SELECTED, bytes,
            sizeof(bytes), 0x10u)) {
        record->failure_code = 18u;
        return 0;
    }
    record->global_selected_vptr = s3_u32(bytes, 0u);
    record->global_selected_first = s3_u32(bytes, 4u);
    record->global_selected_last = s3_u32(bytes, 8u);
    record->global_selected_count = s3_u32(bytes, 0x0Cu);
    context.bases[SAN9_P1_S3_SYMBOL_CURRENT_BUILDING] = S3_CURRENT_BUILDING;
    if (!s3_read(&context, SAN9_P1_S3_READ_CURRENT_BUILDING, bytes,
            sizeof(bytes), 4u)) {
        record->failure_code = 19u;
        return 0;
    }
    record->current_building = s3_u32(bytes, 0u);
    if (record->global_selected_count == SAN9_P1_M2B_SELECTION_LIMIT
        || record->outer_working_count == SAN9_P1_M2B_SELECTION_LIMIT
        || record->outer_committed_count == SAN9_P1_M2B_SELECTION_LIMIT) {
        record->flags |= SAN9_P1_S3_FLAG_SELECTED_FIVE;
    }
    record->chain_signature = hash;
    return 1;
}

static void s3_trace_initialize(San9P1S3TraceArea *trace)
{
    memset(trace, 0, sizeof(*trace));
    trace->magic = SAN9_P1_S3_TRACE_MAGIC;
    trace->schema_major = 1u;
    trace->schema_minor = 0u;
    trace->record_size = sizeof(San9P1S3TraceRecord);
    trace->capacity = SAN9_P1_S3_TRACE_CAPACITY;
    trace->readonly_dispatch_count = san9_p1_s3_read_dispatch_count();
    trace->write_dispatch_count = san9_p1_s3_write_dispatch_count();
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&trace->ready, 1);
}

static void s3_trace_publish(San9P1S3TraceArea *trace, San9P1S3TraceRecord *record)
{
    uint32_t written = (uint32_t)InterlockedCompareExchange(
        (volatile LONG *)&trace->write_sequence, 0, 0);
    uint32_t read = (uint32_t)InterlockedCompareExchange(
        (volatile LONG *)&trace->read_sequence, 0, 0);
    uint32_t sequence;
    San9P1S3TraceRecord *slot;
    if (written - read >= SAN9_P1_S3_TRACE_CAPACITY) {
        (void)InterlockedIncrement((volatile LONG *)&trace->dropped_records);
        return;
    }
    sequence = written + 1u;
    record->sequence = sequence;
    record->published_sequence = 0u;
    slot = &trace->records[written % SAN9_P1_S3_TRACE_CAPACITY];
    memcpy(slot, record, sizeof(*record));
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&slot->published_sequence,
        (LONG)sequence);
    InterlockedExchange((volatile LONG *)&trace->write_sequence,
        (LONG)sequence);
}

static void s3_observe_tick(San9P1S3TraceArea *trace)
{
    San9P1S3TraceRecord record;
    uint64_t now = GetTickCount64();
    uint32_t current_flags;
    uint32_t cumulative_flags;
    size_t semantic_offset = offsetof(San9P1S3TraceRecord, flags);
    if (InterlockedCompareExchange((volatile LONG *)&trace->stop_requested,
            0, 0) != 0) {
        return;
    }
    if (now - g_runtime.last_easy_check_ms >= 250u) {
        uint8_t digest[SAN9_P1_DIGEST_SIZE];
        if (!snapshot_digest(&g_runtime.easy_binding, &g_runtime.easy_callbacks,
                1, digest)) {
            reject_bootstrap(g_runtime.shared,
                SAN9_P1_M2B_RESTART_REQUIRED, 1);
            return;
        }
        g_runtime.last_easy_check_ms = now;
        san9_p1_secure_zero(digest, sizeof(digest));
    }
    if (!s3_capture_record(&record)) {
        (void)InterlockedIncrement((volatile LONG *)&trace->capture_failures);
        return;
    }
    record.tick_ms = now;
    current_flags = record.flags;
    cumulative_flags = (uint32_t)InterlockedCompareExchange(
        (volatile LONG *)&trace->cumulative_flags, 0, 0);
    if ((current_flags & SAN9_P1_S3_FLAG_COMMERCE_HANDLER) == 0u
        && (current_flags & (SAN9_P1_S3_FLAG_COMMERCE_OUTER
            | SAN9_P1_S3_FLAG_SELECTOR | SAN9_P1_S3_FLAG_COMMAND)) == 0u
        && (current_flags & SAN9_P1_S3_FLAG_CONTROLLER) != 0u
        && (cumulative_flags & (SAN9_P1_S3_FLAG_COMMERCE_HANDLER
            | SAN9_P1_S3_FLAG_COMMERCE_OUTER | SAN9_P1_S3_FLAG_SELECTOR
            | SAN9_P1_S3_FLAG_COMMAND | SAN9_P1_S3_FLAG_SELECTED_FIVE)) != 0u) {
        current_flags |= SAN9_P1_S3_FLAG_RETURNED_IDLE;
    }
    record.flags = current_flags | cumulative_flags;
    InterlockedOr((volatile LONG *)&trace->cumulative_flags,
        (LONG)record.flags);
    if (!g_runtime.has_last_trace
        || memcmp((const uint8_t *)&record + semantic_offset,
            (const uint8_t *)&g_runtime.last_trace + semantic_offset,
            sizeof(record) - semantic_offset) != 0) {
        record.change_mask = g_runtime.has_last_trace ? UINT32_MAX : 1u;
        s3_trace_publish(trace, &record);
        g_runtime.last_trace = record;
        g_runtime.has_last_trace = 1;
    }
}

static int exact_shared(
    const San9P1M2bShared *shared,
    UINT message,
    ATOM atom,
    uint32_t challenge)
{
    DWORD pid = 0u;
    DWORD tid;
    if (shared == NULL || shared->magic != SAN9_P1_M2B_SHARED_MAGIC
        || shared->schema_major != SAN9_P1_M2B_SCHEMA_MAJOR
        || shared->schema_minor != SAN9_P1_M2B_SCHEMA_MINOR
        || shared->declared_size != sizeof(*shared)
        || shared->registered_message != message
        || shared->mapping_atom != atom || shared->challenge != challenge
        || (shared->operation_mode != SAN9_P1_M2B_OPERATION_PROBE0
            && shared->operation_mode != SAN9_P1_M2B_OPERATION_PING
            && shared->operation_mode != SAN9_P1_M2B_OPERATION_OBSERVE
            && shared->operation_mode != SAN9_P1_M2B_OPERATION_S5_NO_APPLY
#if SAN9_APPLY_ONCE_BUILD
            && shared->operation_mode != SAN9_ACTIVE_APPLY_MODE
#endif
            && shared->operation_mode != SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE)
        || shared->target_pid != GetCurrentProcessId()
        || shared->target_thread_id != GetCurrentThreadId()
        || shared->target_hwnd == 0u || shared->owner_token == 0u
        || !san9_p1_m2_mailbox_validate(&shared->mailbox)
        || !san9_p1_easy_binding_is_exact(&shared->easy_binding)
        || shared->easy_binding.game_hwnd != shared->target_hwnd) {
        return 0;
    }
    tid = GetWindowThreadProcessId((HWND)(uintptr_t)shared->target_hwnd, &pid);
    return tid == shared->target_thread_id && pid == shared->target_pid;
}

static int exact_message_equal(const MSG *left, const MSG *right)
{
    return left != NULL && right != NULL
        && left->hwnd == right->hwnd
        && left->message == right->message
        && left->wParam == right->wParam
        && left->lParam == right->lParam
        && left->time == right->time
        && left->pt.x == right->pt.x
        && left->pt.y == right->pt.y;
}

typedef uint32_t (SAN9_P1_M2B_STDCALL *ModalTopHwnd)(void);
typedef void *(SAN9_P1_M2B_STDCALL *ModalPermanentCWnd)(uint32_t hwnd);

static int modal_reacquire(uint32_t expected_vptr, void **menu_output,
    uint32_t *hwnd_output, uint32_t *flags_output)
{
    ModalTopHwnd top = (ModalTopHwnd)(uintptr_t)SAN9_P1_M2B_MODAL_TOP_HWND;
    ModalPermanentCWnd permanent =
        (ModalPermanentCWnd)(uintptr_t)SAN9_P1_M2B_MODAL_PERMANENT_CWND;
    uint32_t header[4];
    uint32_t hwnd;
    void *menu;
    memset(header, 0, sizeof(header));
    hwnd = top();
    menu = hwnd != 0u ? permanent(hwnd) : NULL;
    if (hwnd == 0u || menu == NULL || (uintptr_t)menu > UINT32_MAX
        || !live_read(NULL, (uint32_t)(uintptr_t)menu,
            (uint8_t *)header, sizeof(header))
        || header[0] != expected_vptr || header[1] != hwnd
        || (header[3] & 0x10u) == 0u) {
        return 0;
    }
    *menu_output = menu;
    *hwnd_output = hwnd;
    *flags_output = header[3];
    return 1;
}

static void modal_publish_terminal(int accepted)
{
    San9P1M2bShared *shared = g_runtime.shared;
    San9P1S5ModalProbeEvidence *evidence;
    if (shared == NULL) {
        return;
    }
    evidence = &shared->operation.modal.evidence;
    evidence->terminal_code = accepted
        ? SAN9_P1_M2B_OK : SAN9_P1_M2B_S5_MODAL_PROBE_FAILED;
    evidence->restart_required = accepted ? 0u : 1u;
    evidence->state = accepted
        ? SAN9_P1_S5_MODAL_COMPLETE : SAN9_P1_S5_MODAL_REJECTED;
    if (!san9_p1_s5_modal_evidence_sign(evidence,
            shared->mailbox.hmac_key, sizeof(shared->mailbox.hmac_key))) {
        accepted = 0;
        evidence->terminal_code = SAN9_P1_M2B_S5_MODAL_PROBE_FAILED;
        evidence->restart_required = 1u;
        evidence->state = SAN9_P1_S5_MODAL_REJECTED;
    }
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&shared->operation.modal.state,
        (LONG)evidence->state);
    if (!accepted) {
        reject_bootstrap(shared, SAN9_P1_M2B_S5_MODAL_PROBE_FAILED, 1);
    }
}

static void modal_reject_and_restore(void *menu, int restore_not_attempted)
{
    San9P1M2bShared *shared = g_runtime.shared;
    San9P1S5ModalProbeEvidence *evidence;
    if (shared == NULL) {
        return;
    }
    evidence = &shared->operation.modal.evidence;
    (void)InterlockedIncrement(
        (volatile LONG *)&evidence->identity_reject_count);
    if (restore_not_attempted && menu != NULL
        && menu == g_runtime.modal_menu) {
        LONG previous = InterlockedCompareExchange((volatile LONG *)menu,
            (LONG)SAN9_P1_M2B_DOMESTIC_MENU_VPTR,
            (LONG)(uintptr_t)(void *)g_modal_shadow_vtable);
        if ((uint32_t)previous
                == (uint32_t)(uintptr_t)(void *)g_modal_shadow_vtable) {
            (void)InterlockedIncrement(
                (volatile LONG *)&evidence->restore_count);
        }
    }
    g_runtime.modal_menu = NULL;
    modal_publish_terminal(0);
}

static void modal_probe_arm(const MSG *before, int code,
    uintptr_t remove_flag, uint32_t depth, int callnext_stable)
{
    San9P1M2bShared *shared = g_runtime.shared;
    San9P1S5ModalProbeEvidence *evidence;
    uint8_t first[SAN9_P1_DIGEST_SIZE];
    uint8_t second[SAN9_P1_DIGEST_SIZE];
    uintptr_t wrapper = (uintptr_t)(void *)&san9_p1_s5_modal_tick_bridge;
    void *menu = NULL;
    uint32_t hwnd = 0u;
    uint32_t flags = 0u;
    uint32_t original_tick = 0u;
    int valid;
    if (shared == NULL
        || shared->operation_mode != SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.modal.state, 0, 0)
                != SAN9_P1_S5_MODAL_WAKE_CLAIMED) {
        return;
    }
    evidence = &shared->operation.modal.evidence;
    memset(first, 0, sizeof(first));
    memset(second, 0, sizeof(second));
    evidence->hook_code = (uint32_t)code;
    evidence->remove_flag = (uint32_t)remove_flag;
    evidence->hook_depth = depth;
    evidence->callnext_stable = callnext_stable ? 1u : 0u;
    if (before != NULL) {
        evidence->message_hwnd = (uint32_t)(uintptr_t)before->hwnd;
        evidence->message = before->message;
        evidence->atom = (uint32_t)before->wParam;
        evidence->challenge = (uint32_t)before->lParam;
    }
    evidence->first_tid = GetCurrentThreadId();
    evidence->last_tid = GetCurrentThreadId();
    valid = depth == 1u && callnext_stable && before != NULL
        && before->hwnd == NULL && wrapper <= UINT32_MAX
        && GetCurrentThreadId() == g_runtime.main_thread_id
        && modal_reacquire(SAN9_P1_M2B_DOMESTIC_MENU_VPTR,
            &menu, &hwnd, &flags)
        && live_read(NULL, SAN9_P1_M2B_DOMESTIC_MENU_TICK_SLOT,
            (uint8_t *)&original_tick, sizeof(original_tick))
        && original_tick == SAN9_P1_M2B_DOMESTIC_MENU_TICK_ORIGINAL
        && live_read(NULL, SAN9_P1_M2B_DOMESTIC_MENU_VPTR,
            (uint8_t *)g_modal_shadow_vtable,
            sizeof(g_modal_shadow_vtable))
        && snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 1, first)
        && snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 1, second)
        && san9_p1_constant_time_equal(first, second, sizeof(first));
    if (!valid) {
        san9_p1_secure_zero(first, sizeof(first));
        san9_p1_secure_zero(second, sizeof(second));
        modal_reject_and_restore(NULL, 0);
        return;
    }
    g_modal_shadow_vtable[
        SAN9_P1_M2B_DOMESTIC_MENU_TICK_OFFSET / sizeof(uintptr_t)] = wrapper;
    g_runtime.modal_menu = menu;
    g_runtime.modal_top_hwnd = hwnd;
    g_runtime.modal_flags = flags;
    evidence->top_hwnd = hwnd;
    evidence->menu_vptr = SAN9_P1_M2B_DOMESTIC_MENU_VPTR;
    evidence->modal_flags = flags;
    evidence->menu_pointer = (uint32_t)(uintptr_t)menu;
    memcpy(evidence->arm_easy_digest, first, sizeof(first));
    MemoryBarrier();
    if (!san9_p1_s5_modal_pre_cas_gate_is_open(
            shared_state(shared),
            InterlockedCompareExchange(
                (volatile LONG *)&shared->operation.modal.state, 0, 0))) {
        g_runtime.modal_menu = NULL;
        san9_p1_secure_zero(first, sizeof(first));
        san9_p1_secure_zero(second, sizeof(second));
        modal_reject_and_restore(NULL, 0);
        return;
    }
    if ((uint32_t)InterlockedCompareExchange((volatile LONG *)menu,
            (LONG)(uintptr_t)(void *)g_modal_shadow_vtable,
            (LONG)SAN9_P1_M2B_DOMESTIC_MENU_VPTR)
            != SAN9_P1_M2B_DOMESTIC_MENU_VPTR) {
        g_runtime.modal_menu = NULL;
        san9_p1_secure_zero(first, sizeof(first));
        san9_p1_secure_zero(second, sizeof(second));
        modal_reject_and_restore(NULL, 0);
        return;
    }
    evidence->shadow_arm_count = 1u;
    san9_p1_secure_zero(first, sizeof(first));
    san9_p1_secure_zero(second, sizeof(second));
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&shared->operation.modal.state,
        SAN9_P1_S5_MODAL_SHADOW_ARMED);
}

static void s5_v8_menu_restart(San9S5FaultCode fault)
{
    San9P1M2bShared *shared = g_runtime.shared;
    if (shared == NULL) {
        return;
    }
    InterlockedExchange(
        (volatile LONG *)&shared->operation.s5.menu.restart_required, 1);
    InterlockedExchange((volatile LONG *)&shared->operation.s5.menu.state,
        SAN9_P1_S5_MENU_REJECTED);
    g_runtime.s5_menu = NULL;
    s5_poison(fault);
}

static void s5_v8_menu_shadow_arm(const MSG *before, int code,
    uintptr_t remove_flag, uint32_t depth, int callnext_stable)
{
    San9P1M2bShared *shared = g_runtime.shared;
    San9P1S5MenuHandoff *handoff;
    uint8_t first[SAN9_P1_DIGEST_SIZE];
    uint8_t second[SAN9_P1_DIGEST_SIZE];
    uintptr_t wrapper = (uintptr_t)(void *)&san9_p1_s5_v8_menu_tick_bridge;
    void *menu = NULL;
    uint32_t hwnd = 0u;
    uint32_t flags = 0u;
    uint32_t original_tick = 0u;
    int valid;
    (void)code;
    (void)remove_flag;
    if (!s5_business_mode(shared)) {
        return;
    }
    handoff = &shared->operation.s5.menu;
    memset(first, 0, sizeof(first));
    memset(second, 0, sizeof(second));
    valid = InterlockedCompareExchange(
            (volatile LONG *)&handoff->state, 0, 0)
                == SAN9_P1_S5_MENU_WAKE_CLAIMED
        && depth == 1u && callnext_stable && before != NULL
        && before->hwnd == NULL && wrapper <= UINT32_MAX
        && GetCurrentThreadId() == g_runtime.main_thread_id
        && shared_state(shared) == SAN9_P1_M2B_BOOTSTRAP_READY
        && InterlockedCompareExchange(
            (volatile LONG *)&shared->claim_enabled, 0, 0) == 1
        && san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
            == SAN9_S5_STATE_SEALED
        && modal_reacquire(SAN9_P1_M2B_DOMESTIC_MENU_VPTR,
            &menu, &hwnd, &flags)
        && live_read(NULL, SAN9_P1_M2B_DOMESTIC_MENU_TICK_SLOT,
            (uint8_t *)&original_tick, sizeof(original_tick))
        && original_tick == SAN9_P1_M2B_DOMESTIC_MENU_TICK_ORIGINAL
        && live_read(NULL, SAN9_P1_M2B_DOMESTIC_MENU_VPTR,
            (uint8_t *)g_modal_shadow_vtable,
            sizeof(g_modal_shadow_vtable))
        && snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 1, first)
        && snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 1, second)
        && san9_p1_constant_time_equal(first, second, sizeof(first));
    if (!valid) {
        san9_p1_secure_zero(first, sizeof(first));
        san9_p1_secure_zero(second, sizeof(second));
        s5_v8_menu_restart(SAN9_S5_FAULT_PRECHECK);
        return;
    }
    g_modal_shadow_vtable[
        SAN9_P1_M2B_DOMESTIC_MENU_TICK_OFFSET / sizeof(uintptr_t)] = wrapper;
    g_runtime.s5_menu = menu;
    g_runtime.s5_menu_top_hwnd = hwnd;
    g_runtime.s5_menu_flags = flags;
    handoff->menu_pointer = (uint32_t)(uintptr_t)menu;
    handoff->top_hwnd = hwnd;
    handoff->modal_flags = flags;
    handoff->first_tid = GetCurrentThreadId();
    handoff->last_tid = GetCurrentThreadId();
    memcpy(handoff->arm_easy_digest, first, sizeof(first));
    MemoryBarrier();
    if (shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY
        || InterlockedCompareExchange(
            (volatile LONG *)&handoff->state, 0, 0)
                != SAN9_P1_S5_MENU_WAKE_CLAIMED
        || san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
            != SAN9_S5_STATE_SEALED
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->claim_enabled, 0, 0) != 1
        || (uint32_t)InterlockedCompareExchange((volatile LONG *)menu,
            (LONG)(uintptr_t)(void *)g_modal_shadow_vtable,
            (LONG)SAN9_P1_M2B_DOMESTIC_MENU_VPTR)
                != SAN9_P1_M2B_DOMESTIC_MENU_VPTR) {
        g_runtime.s5_menu = NULL;
        san9_p1_secure_zero(first, sizeof(first));
        san9_p1_secure_zero(second, sizeof(second));
        s5_v8_menu_restart(SAN9_S5_FAULT_SHADOW);
        return;
    }
    (void)InterlockedIncrement((volatile LONG *)&handoff->shadow_arm_count);
    san9_p1_secure_zero(first, sizeof(first));
    san9_p1_secure_zero(second, sizeof(second));
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&handoff->state,
        SAN9_P1_S5_MENU_SHADOW_ARMED);
}

uint32_t san9_p1_s5_v8_menu_entry(void *menu, uint32_t caller)
{
    const uint32_t token = UINT32_C(0x38565335);
    San9P1M2bShared *shared = g_runtime.shared;
    San9P1S5MenuHandoff *handoff;
    uint8_t first[SAN9_P1_DIGEST_SIZE];
    uint8_t second[SAN9_P1_DIGEST_SIZE];
    void *current = NULL;
    uint32_t hwnd = 0u;
    uint32_t flags = 0u;
    uint32_t shadow = (uint32_t)(uintptr_t)(void *)g_modal_shadow_vtable;
    int valid;
    if (shared == NULL) {
        return 0u;
    }
    handoff = &shared->operation.s5.menu;
    if (InterlockedCompareExchange((volatile LONG *)&handoff->state,
            SAN9_P1_S5_MENU_ENTRY_CLAIMED,
            SAN9_P1_S5_MENU_SHADOW_ARMED)
            != SAN9_P1_S5_MENU_SHADOW_ARMED) {
        (void)InterlockedIncrement((volatile LONG *)&handoff->duplicate_count);
        if (menu != NULL) {
            (void)InterlockedCompareExchange((volatile LONG *)menu,
                (LONG)SAN9_P1_M2B_DOMESTIC_MENU_VPTR, (LONG)shadow);
        }
        s5_v8_menu_restart(SAN9_S5_FAULT_OUT_OF_ORDER);
        return 0u;
    }
    (void)InterlockedIncrement((volatile LONG *)&handoff->entry_claim_count);
    memset(first, 0, sizeof(first));
    memset(second, 0, sizeof(second));
    valid = s5_business_mode(shared)
        && shared_state(shared) == SAN9_P1_M2B_BOOTSTRAP_READY
        && InterlockedCompareExchange(&g_runtime.installed, 0, 0) == 1
        && GetCurrentThreadId() == g_runtime.main_thread_id
        && caller == SAN9_P1_M2B_DOMESTIC_MENU_TICK_CALLER
        && menu != NULL && menu == g_runtime.s5_menu
        && san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
            == SAN9_S5_STATE_SEALED
        && InterlockedCompareExchange(
            (volatile LONG *)&shared->claim_enabled, 0, 0) == 1
        && modal_reacquire(shadow, &current, &hwnd, &flags)
        && current == menu && hwnd == g_runtime.s5_menu_top_hwnd
        && (flags & 0x10u) != 0u
        && snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 1, first)
        && snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 1, second)
        && san9_p1_constant_time_equal(first, second, sizeof(first))
        && san9_p1_constant_time_equal(first, handoff->arm_easy_digest,
            sizeof(first));
    if (!valid || (uint32_t)InterlockedCompareExchange((volatile LONG *)menu,
            (LONG)SAN9_P1_M2B_DOMESTIC_MENU_VPTR, (LONG)shadow) != shadow) {
        if (menu != NULL) {
            (void)InterlockedCompareExchange((volatile LONG *)menu,
                (LONG)SAN9_P1_M2B_DOMESTIC_MENU_VPTR, (LONG)shadow);
        }
        san9_p1_secure_zero(first, sizeof(first));
        san9_p1_secure_zero(second, sizeof(second));
        s5_v8_menu_restart(SAN9_S5_FAULT_RESTORE);
        return 0u;
    }
    memcpy(handoff->entry_easy_digest, second, sizeof(second));
    handoff->last_tid = GetCurrentThreadId();
    handoff->last_caller = caller;
    (void)InterlockedIncrement(
        (volatile LONG *)&handoff->menu_restore_count);
    if (((uint32_t)InterlockedAnd(
            (volatile LONG *)((uint8_t *)menu + S5_MENU_FLAGS_OFFSET),
            (LONG)~S5_MENU_ACTIVE_BIT) & S5_MENU_ACTIVE_BIT) == 0u) {
        san9_p1_secure_zero(first, sizeof(first));
        san9_p1_secure_zero(second, sizeof(second));
        s5_v8_menu_restart(SAN9_S5_FAULT_PRECHECK);
        return 0u;
    }
    g_runtime.s5_menu = NULL;
    san9_p1_secure_zero(first, sizeof(first));
    san9_p1_secure_zero(second, sizeof(second));
    MemoryBarrier();
    InterlockedExchange((volatile LONG *)&handoff->state,
        SAN9_P1_S5_MENU_VPTR_RESTORED);
    return token;
}

void san9_p1_s5_v8_after_original(uint32_t entry_token, uint32_t caller)
{
    const uint32_t token = UINT32_C(0x38565335);
    San9P1M2bShared *shared = g_runtime.shared;
    San9P1S5MenuHandoff *handoff;
    uint8_t digest[SAN9_P1_DIGEST_SIZE];
    S5StartOutcome outcome;
    if (shared == NULL) {
        return;
    }
    handoff = &shared->operation.s5.menu;
    (void)InterlockedIncrement(
        (volatile LONG *)&handoff->original_return_count);
    handoff->last_tid = GetCurrentThreadId();
    handoff->last_caller = caller;
    if (entry_token != token
        || GetCurrentThreadId() != g_runtime.main_thread_id
        || caller != SAN9_P1_M2B_DOMESTIC_MENU_TICK_CALLER
        || shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY
        || san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
            != SAN9_S5_STATE_SEALED
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->claim_enabled, 0, 0) != 1
        || InterlockedCompareExchange((volatile LONG *)&handoff->state,
            SAN9_P1_S5_MENU_ORIGINAL_RETURNED,
            SAN9_P1_S5_MENU_VPTR_RESTORED)
                != SAN9_P1_S5_MENU_VPTR_RESTORED
        || !snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 1, digest)
        || !san9_p1_constant_time_equal(digest,
            handoff->entry_easy_digest, sizeof(digest))
        || InterlockedCompareExchange((volatile LONG *)&handoff->state,
            SAN9_P1_S5_MENU_START_CLAIMED,
            SAN9_P1_S5_MENU_ORIGINAL_RETURNED)
                != SAN9_P1_S5_MENU_ORIGINAL_RETURNED) {
        san9_p1_secure_zero(digest, sizeof(digest));
        if (entry_token == token) {
            s5_v8_menu_restart(SAN9_S5_FAULT_PRECHECK);
        }
        return;
    }
    san9_p1_secure_zero(digest, sizeof(digest));
    outcome = s5_start_no_apply(GetTickCount64());
    if (outcome == S5_START_HANDLER_ARMED) {
        (void)InterlockedIncrement((volatile LONG *)&handoff->start_count);
        InterlockedExchange((volatile LONG *)&handoff->state,
            SAN9_P1_S5_MENU_STARTED);
    } else if (outcome == S5_START_REJECTED_PRE_EVENT) {
        InterlockedExchange((volatile LONG *)&handoff->state,
            SAN9_P1_S5_MENU_REJECTED);
    } else {
        s5_v8_menu_restart(SAN9_S5_FAULT_STATE_CORRUPTION);
    }
}

void san9_p1_s5_modal_after_original(void *menu, uint32_t caller)
{
    San9P1M2bShared *shared = g_runtime.shared;
    San9P1S5ModalProbeEvidence *evidence;
    uint8_t first[SAN9_P1_DIGEST_SIZE];
    uint8_t second[SAN9_P1_DIGEST_SIZE];
    void *current = NULL;
    uint32_t hwnd = 0u;
    uint32_t flags = 0u;
    uint32_t wrapper_count;
    uint32_t depth = ++g_modal_wrapper_depth;
    uint32_t shadow = (uint32_t)(uintptr_t)(void *)g_modal_shadow_vtable;
    int valid;
    if (shared == NULL) {
        --g_modal_wrapper_depth;
        return;
    }
    evidence = &shared->operation.modal.evidence;
    wrapper_count = (uint32_t)InterlockedIncrement(
        (volatile LONG *)&evidence->wrapper_enter_count);
    (void)InterlockedIncrement(
        (volatile LONG *)&evidence->original_return_count);
    if (depth != 1u) {
        (void)InterlockedIncrement(
            (volatile LONG *)&evidence->wrapper_reentry_count);
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.modal.state,
            SAN9_P1_S5_MODAL_CAPTURING,
            SAN9_P1_S5_MODAL_SHADOW_ARMED)
            != SAN9_P1_S5_MODAL_SHADOW_ARMED) {
        --g_modal_wrapper_depth;
        modal_reject_and_restore(menu, 1);
        return;
    }
    memset(first, 0, sizeof(first));
    memset(second, 0, sizeof(second));
    valid = shared->operation_mode == SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE
        && shared_state(shared) == SAN9_P1_M2B_BOOTSTRAP_READY
        && InterlockedCompareExchange(&g_runtime.installed, 0, 0) == 1
        && depth == 1u && wrapper_count <= SAN9_P1_M2B_S5_MODAL_PROBE_TICKS
        && GetCurrentThreadId() == g_runtime.main_thread_id
        && caller == SAN9_P1_M2B_DOMESTIC_MENU_TICK_CALLER
        && menu != NULL && menu == g_runtime.modal_menu
        && modal_reacquire(shadow, &current, &hwnd, &flags)
        && current == menu && hwnd == g_runtime.modal_top_hwnd
        && (flags & 0x10u) != 0u
        && snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 1, first)
        && snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 1, second)
        && san9_p1_constant_time_equal(first, second, sizeof(first))
        && san9_p1_constant_time_equal(first, evidence->arm_easy_digest,
            sizeof(first));
    if (!valid) {
        san9_p1_secure_zero(first, sizeof(first));
        san9_p1_secure_zero(second, sizeof(second));
        --g_modal_wrapper_depth;
        modal_reject_and_restore(menu, 1);
        return;
    }
    evidence->last_tid = GetCurrentThreadId();
    evidence->last_caller = caller;
    evidence->top_hwnd = hwnd;
    evidence->modal_flags = flags;
    memcpy(evidence->last_easy_digest, second, sizeof(second));
    (void)InterlockedIncrement(
        (volatile LONG *)&evidence->easy_stable_count);
    san9_p1_secure_zero(first, sizeof(first));
    san9_p1_secure_zero(second, sizeof(second));
    if (wrapper_count < SAN9_P1_M2B_S5_MODAL_PROBE_TICKS) {
        --g_modal_wrapper_depth;
        MemoryBarrier();
        InterlockedExchange((volatile LONG *)&shared->operation.modal.state,
            SAN9_P1_S5_MODAL_SHADOW_ARMED);
        return;
    }
    if ((uint32_t)InterlockedCompareExchange((volatile LONG *)menu,
            (LONG)SAN9_P1_M2B_DOMESTIC_MENU_VPTR, (LONG)shadow) != shadow) {
        --g_modal_wrapper_depth;
        modal_reject_and_restore(menu, 0);
        return;
    }
    (void)InterlockedIncrement((volatile LONG *)&evidence->restore_count);
    g_runtime.modal_menu = NULL;
    --g_modal_wrapper_depth;
    modal_publish_terminal(1);
}

static void handle_bootstrap_message(UINT message, WPARAM w_param, LPARAM l_param)
{
    wchar_t mapping_name[64];
    int name_length;
    ATOM atom = (ATOM)w_param;
    uint32_t challenge = (uint32_t)l_param;
    HANDLE mapping = NULL;
    San9P1M2bShared *shared = NULL;
    San9P1DecodeStatus decode;
    San9P1M2Status status;
    if (message < 0xC000u || atom == 0 || challenge == 0u
        || InterlockedCompareExchange(&g_runtime.installed, 0, 0) != 0) {
        return;
    }
    memset(mapping_name, 0, sizeof(mapping_name));
    name_length = GlobalGetAtomNameW(atom, mapping_name,
        (int)(sizeof(mapping_name) / sizeof(mapping_name[0])));
    if (name_length <= 0
        || wcsncmp(mapping_name, L"Local\\San9P1M2b-", 16u) != 0) {
        return;
    }
    mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE,
        mapping_name);
    if (mapping == NULL) {
        return;
    }
    shared = (San9P1M2bShared *)MapViewOfFile(mapping,
        FILE_MAP_READ | FILE_MAP_WRITE, 0u, 0u, SAN9_P1_M2B_SHARED_SIZE);
    if (shared == NULL) {
        (void)CloseHandle(mapping);
        return;
    }
    if (InterlockedCompareExchange((volatile LONG *)&shared->bootstrap_state,
            SAN9_P1_M2B_BOOTSTRAP_CLAIMED, SAN9_P1_M2B_BOOTSTRAP_SEALED)
            != SAN9_P1_M2B_BOOTSTRAP_SEALED
        || !exact_shared(shared, message, atom, challenge)) {
        reject_bootstrap(shared, SAN9_P1_M2B_AUTH_REJECTED, 0);
        (void)UnmapViewOfFile(shared);
        (void)CloseHandle(mapping);
        return;
    }

    memset(&g_runtime.m2, 0, sizeof(g_runtime.m2));
    g_runtime.mapping = mapping;
    g_runtime.shared = shared;
    g_runtime.easy_binding = shared->easy_binding;
    g_runtime.easy_callbacks.read = live_read;
    g_runtime.easy_callbacks.query = live_query;
    g_runtime.easy_callbacks.context = NULL;
    g_runtime.actions.invoke = runtime_action;
    g_runtime.actions.context = &g_runtime;
    g_runtime.owner_token = shared->owner_token;
    g_runtime.main_thread_id = shared->target_thread_id;
#if SAN9_COMBINED_BATCH_BUILD
    memset(&g_runtime.s8_bound_city, 0, sizeof(g_runtime.s8_bound_city));
    g_runtime.s8_bound_city_valid = 0u;
#endif
    if (shared->operation_mode == SAN9_P1_M2B_OPERATION_OBSERVE) {
        s3_trace_initialize(&shared->operation.s3.trace);
    }

    if (!san9_p1_m2_runtime_initialize(&g_runtime.m2, &shared->mailbox)
        || san9_p1_m2_lifecycle_begin_write(&g_runtime.m2) != SAN9_P1_M2_OK
        || san9_p1_m2_session_bind_authenticated(&g_runtime.m2,
            shared->binding_frame, sizeof(shared->binding_frame), &decode)
            != SAN9_P1_M2_OK
        || decode != SAN9_P1_DECODE_ACCEPTED
        || san9_p1_decode(shared->binding_frame, sizeof(shared->binding_frame),
            shared->mailbox.hmac_key, sizeof(shared->mailbox.hmac_key),
            &g_runtime.binding_frame) != SAN9_P1_DECODE_ACCEPTED
        || san9_p1_m2_lifecycle_seal(&g_runtime.m2) != SAN9_P1_M2_OK
        || !exact_module_identity(&g_runtime.easy_binding)
        || !snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 0, g_runtime.easy_snapshot_digest)
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->bootstrap_state,
            SAN9_P1_M2B_BOOTSTRAP_COMMITTING,
            SAN9_P1_M2B_BOOTSTRAP_CLAIMED) != SAN9_P1_M2B_BOOTSTRAP_CLAIMED) {
        reject_bootstrap(shared, SAN9_P1_M2B_EASY_REJECTED, 0);
        memset(&g_runtime, 0, sizeof(g_runtime));
        (void)UnmapViewOfFile(shared);
        (void)CloseHandle(mapping);
        return;
    }
    status = san9_p1_m2_lifecycle_validate_and_commit(&g_runtime.m2,
        &g_runtime.easy_binding, &g_runtime.easy_callbacks, &g_runtime.actions,
        g_runtime.owner_token);
    if (status != SAN9_P1_M2_OK) {
        int committed = InterlockedCompareExchange(
            &g_runtime.slot_committed, 0, 0) == 1;
        reject_bootstrap(shared,
            committed ? SAN9_P1_M2B_RESTART_REQUIRED : SAN9_P1_M2B_SLOT_CONFLICT,
            committed);
        if (!committed) {
            memset(&g_runtime, 0, sizeof(g_runtime));
            (void)UnmapViewOfFile(shared);
            (void)CloseHandle(mapping);
        }
        return;
    }
#if S5_NO_APPLY_BUILD
    if (s5_business_mode(shared)) {
        San9P1Sha256Context binding_sha;
        uint8_t binding_digest[SAN9_S5_DIGEST_SIZE];
        int gates_ok;
        san9_p1_sha256_initialize(&binding_sha);
        san9_p1_sha256_update(&binding_sha, shared->binding_frame,
            sizeof(shared->binding_frame));
        san9_p1_sha256_finish(&binding_sha, binding_digest);
        gates_ok = san9_s5_no_apply_gate_initialize(
            &g_runtime.s5_gate, binding_digest);
#if SAN9_COMBINED_BATCH_BUILD
        {
            uint32_t gate_index;
            for (gate_index = 0u; gate_index < 5u && gates_ok; ++gate_index) {
                gates_ok = san9_s5_no_apply_gate_initialize(
                    &g_runtime.s8_step_gates[gate_index], binding_digest);
            }
        }
#endif
        if (!gates_ok) {
            san9_p1_secure_zero(binding_digest, sizeof(binding_digest));
            reject_bootstrap(shared, SAN9_P1_M2B_RESTART_REQUIRED, 1);
            return;
        }
        memcpy(g_runtime.s5_binding_digest, binding_digest,
            sizeof(g_runtime.s5_binding_digest));
        san9_p1_secure_zero(binding_digest, sizeof(binding_digest));
    }
#endif
    InterlockedExchange(&g_runtime.installed, 1);
    InterlockedExchange((volatile LONG *)&shared->claim_enabled, 0);
    InterlockedExchange((volatile LONG *)&shared->bootstrap_result,
        SAN9_P1_M2B_OK);
    MemoryBarrier();
    if (InterlockedCompareExchange((volatile LONG *)&shared->bootstrap_state,
            SAN9_P1_M2B_BOOTSTRAP_READY,
            SAN9_P1_M2B_BOOTSTRAP_COMMITTING)
            != SAN9_P1_M2B_BOOTSTRAP_COMMITTING) {
        reject_bootstrap(shared, SAN9_P1_M2B_RESTART_REQUIRED, 1);
    }
}

SAN9_P1_M2B_EXPORT intptr_t SAN9_P1_M2B_STDCALL San9BridgeP1M2b_GetMsgProc(
    int code,
    uintptr_t w_param,
    intptr_t l_param)
{
    MSG before;
    MSG after;
    intptr_t next;
    int claimed = 0;
    int s5_claimed = 0;
    int s5_duplicate = 0;
    uint32_t depth;
    LONG installed_before;
    memset(&before, 0, sizeof(before));
    memset(&after, 0, sizeof(after));
    depth = ++g_hook_depth;
    installed_before = InterlockedCompareExchange(&g_runtime.installed, 0, 0);
    if (code >= 0 && l_param != 0) {
        const MSG *message = (const MSG *)(uintptr_t)l_param;
        handle_bootstrap_message(message->message,
            (WPARAM)message->wParam, (LPARAM)message->lParam);
        if (installed_before == 1
            && g_runtime.shared != NULL
            && g_runtime.shared->operation_mode
                == SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE
            && shared_state(g_runtime.shared) == SAN9_P1_M2B_BOOTSTRAP_READY
            && code == HC_ACTION && w_param == PM_REMOVE
            && GetCurrentThreadId() == g_runtime.main_thread_id
            && message->hwnd == NULL
            && message->message == g_runtime.shared->registered_message
            && (uint32_t)message->wParam == g_runtime.shared->mapping_atom
            && (uint32_t)message->lParam == g_runtime.shared->challenge) {
            San9P1S5ModalProbeEvidence *evidence =
                &g_runtime.shared->operation.modal.evidence;
            before = *message;
            (void)InterlockedIncrement(
                (volatile LONG *)&evidence->hook_entry_count);
            (void)InterlockedIncrement(
                (volatile LONG *)&evidence->exact_wake_count);
            if (depth != 1u) {
                (void)InterlockedIncrement(
                    (volatile LONG *)&evidence->hook_reentry_count);
            }
            if (InterlockedCompareExchange(
                    (volatile LONG *)&g_runtime.shared->operation.modal.state,
                    SAN9_P1_S5_MODAL_WAKE_CLAIMED,
                    SAN9_P1_S5_MODAL_ARMED) == SAN9_P1_S5_MODAL_ARMED) {
                (void)InterlockedIncrement(
                    (volatile LONG *)&evidence->wake_claim_count);
                claimed = 1;
            } else {
                (void)InterlockedIncrement(
                    (volatile LONG *)&evidence->duplicate_count);
            }
        } else if (installed_before == 1
            && g_runtime.shared != NULL
            && s5_business_mode(g_runtime.shared)
            && shared_state(g_runtime.shared) == SAN9_P1_M2B_BOOTSTRAP_READY
            && code == HC_ACTION && w_param == PM_REMOVE
            && GetCurrentThreadId() == g_runtime.main_thread_id
            && message->hwnd == NULL
            && message->message == g_runtime.shared->registered_message
            && (uint32_t)message->wParam == g_runtime.shared->mapping_atom
            && (uint32_t)message->lParam == g_runtime.shared->challenge) {
            San9P1S5MenuHandoff *handoff =
                &g_runtime.shared->operation.s5.menu;
            before = *message;
            (void)InterlockedIncrement(
                (volatile LONG *)&handoff->hook_entry_count);
            (void)InterlockedIncrement(
                (volatile LONG *)&handoff->exact_wake_count);
            if (InterlockedCompareExchange((volatile LONG *)&handoff->state,
                    SAN9_P1_S5_MENU_WAKE_CLAIMED,
                    SAN9_P1_S5_MENU_ARMED) == SAN9_P1_S5_MENU_ARMED) {
                s5_claimed = 1;
            } else {
                (void)InterlockedIncrement(
                    (volatile LONG *)&handoff->duplicate_count);
                s5_duplicate = 1;
            }
        }
    }
    next = (intptr_t)CallNextHookEx(NULL, code,
        (WPARAM)w_param, (LPARAM)l_param);
    if (claimed) {
        after = *(const MSG *)(uintptr_t)l_param;
        modal_probe_arm(&before, code, w_param, depth,
            exact_message_equal(&before, &after));
    } else if (s5_claimed) {
        after = *(const MSG *)(uintptr_t)l_param;
        s5_v8_menu_shadow_arm(&before, code, w_param, depth,
            exact_message_equal(&before, &after));
    } else if (s5_duplicate) {
        s5_v8_menu_restart(SAN9_S5_FAULT_OUT_OF_ORDER);
    }
    if (g_hook_depth == depth) {
        --g_hook_depth;
    }
    return next;
}

uint32_t san9_p1_m2b_enter_idle_depth(void)
{
    if (g_idle_depth == UINT32_MAX) {
        return 0u;
    }
    ++g_idle_depth;
    return g_idle_depth;
}

void san9_p1_m2b_leave_idle_depth(uint32_t token)
{
    if (token != 0u && g_idle_depth == token) {
        --g_idle_depth;
    }
}

static int exact_idle_identity(void *app, int flag, uint32_t caller)
{
    return app != NULL && flag == 0
        && caller == SAN9_P1_M2B_EXACT_IDLE_CALLER
        && app == (void *)(uintptr_t)SAN9_P1_M2B_EXACT_APP_OBJECT
        && GetCurrentThreadId() == g_runtime.main_thread_id
        && *(volatile uint32_t *)(uintptr_t)SAN9_P1_M2B_EXACT_APP_OBJECT
            == SAN9_P1_M2B_EXACT_APP_VTABLE
        && *(volatile uint32_t *)(uintptr_t)SAN9_P1_M2B_EXACT_IDLE_SLOT
            == (uint32_t)(uintptr_t)(void *)&San9BridgeP1M2b_IdleBridge;
}

#if S5_NO_APPLY_BUILD
static San9S5CurrentContextStatus s5_capture_ab(
    San9S5CurrentContextSnapshot *first,
    San9S5CurrentContextSnapshot *second)
{
    San9S5CurrentContextReader reader;
    San9S5ExpectedIdentity identity;
    memset(&reader, 0, sizeof(reader));
    memset(&identity, 0, sizeof(identity));
    reader.read = s5_direct_read;
    identity.process_id = g_runtime.binding_frame.game_pid;
    identity.main_thread_id = g_runtime.binding_frame.main_tid;
    identity.process_generation = g_runtime.binding_frame.game_generation;
    identity.window_handle = g_runtime.binding_frame.game_hwnd;
#if SAN9_COMBINED_BATCH_BUILD
    if (g_runtime.shared != NULL && g_runtime.shared->operation_mode
            == SAN9_ACTIVE_APPLY_MODE) {
        if (g_runtime.s8_bound_city_valid != 0u) {
            return san9_s5_bound_current_context_capture_reader_ab(
                &reader, &identity, &g_runtime.s8_bound_city,
                s8_active_native_id(), first, second);
        }
        switch (s8_active_native_id()) {
        case SAN9_P1_M2B_PATROL_NATIVE_ID:
            return san9_s6_patrol_current_context_capture_reader_ab(
                &reader, &identity, first, second);
        case SAN9_P1_M2B_COMMERCE_NATIVE_ID:
            return san9_s5_current_context_capture_reader_ab(
                &reader, &identity, first, second);
        case SAN9_P1_M2B_CULTIVATE_NATIVE_ID:
            return san9_s6_cultivate_current_context_capture_reader_ab(
                &reader, &identity, first, second);
        case SAN9_P1_M2B_TRAIN_NATIVE_ID:
            return san9_s6_train_current_context_capture_reader_ab(
                &reader, &identity, first, second);
        case SAN9_P1_M2B_REPAIR_NATIVE_ID:
            return san9_s6_repair_current_context_capture_reader_ab(
                &reader, &identity, first, second);
        default:
            return SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT;
        }
    }
#endif
    if (g_runtime.shared != NULL && g_runtime.shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE) {
        return san9_s6_repair_current_context_capture_reader_ab(
            &reader, &identity, first, second);
    }
    if (g_runtime.shared != NULL && g_runtime.shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE) {
        return san9_s6_train_current_context_capture_reader_ab(
            &reader, &identity, first, second);
    }
    if (g_runtime.shared != NULL && g_runtime.shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE) {
        return san9_s6_patrol_current_context_capture_reader_ab(
            &reader, &identity, first, second);
    }
    return g_runtime.shared != NULL
            && g_runtime.shared->operation_mode
                == SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE
        ? san9_s6_cultivate_current_context_capture_reader_ab(
            &reader, &identity, first, second)
        : san9_s5_current_context_capture_reader_ab(
            &reader, &identity, first, second);
}

static void s5_fill_pre_evidence(
    San9P1S5NoApplyEvidence *evidence,
    const San9S5CurrentContextSnapshot *snapshot,
    const San9S5NoApplyRequest *request,
    const uint8_t request_digest[SAN9_S5_DIGEST_SIZE])
{
    uint32_t index;
    memset(evidence, 0, sizeof(*evidence));
    evidence->magic = SAN9_P1_S5_EVIDENCE_MAGIC;
    evidence->schema_major = SAN9_P1_M2B_SCHEMA_MAJOR;
    evidence->schema_minor = SAN9_P1_M2B_SCHEMA_MINOR;
    evidence->structure_size = sizeof(*evidence);
    evidence->first_tid = GetCurrentThreadId();
    evidence->last_tid = GetCurrentThreadId();
    evidence->last_caller = SAN9_P1_M2B_EXACT_IDLE_CALLER;
    evidence->root_pointer = snapshot->controller_pointer;
    evidence->city_pointer = snapshot->city_pointer;
    evidence->corps_pointer = snapshot->corps_pointer;
    evidence->native_command_id = snapshot->native_command_id;
    evidence->pre_commerce = snapshot->commerce_current;
    evidence->commerce_maximum = snapshot->commerce_maximum;
    if (snapshot->native_command_id == SAN9_P1_M2B_REPAIR_NATIVE_ID) {
        evidence->pre_command_value = snapshot->repair_current;
        evidence->command_maximum = snapshot->repair_maximum;
        evidence->command_order_mask = SAN9_P1_M2B_REPAIR_ORDER_FLAG;
    } else if (snapshot->native_command_id == SAN9_P1_M2B_TRAIN_NATIVE_ID) {
        evidence->pre_command_value = snapshot->train_morale;
        evidence->command_maximum = snapshot->train_maximum;
        evidence->command_order_mask = SAN9_P1_M2B_TRAIN_ORDER_FLAG;
    } else if (snapshot->native_command_id == SAN9_P1_M2B_PATROL_NATIVE_ID) {
        evidence->pre_command_value = snapshot->patrol_current;
        evidence->command_maximum = snapshot->patrol_maximum;
        evidence->command_order_mask = SAN9_P1_M2B_PATROL_ORDER_FLAG;
    } else if (snapshot->native_command_id
            == SAN9_P1_M2B_CULTIVATE_NATIVE_ID) {
        evidence->pre_command_value = snapshot->cultivate_current;
        evidence->command_maximum = snapshot->cultivate_maximum;
        evidence->command_order_mask = SAN9_P1_M2B_CULTIVATE_ORDER_FLAG;
    } else {
        evidence->pre_command_value = snapshot->commerce_current;
        evidence->command_maximum = snapshot->commerce_maximum;
        evidence->command_order_mask = S5_CITY_COMMERCE_ORDER_FLAG;
    }
    evidence->pre_money = snapshot->corps_money;
    evidence->pre_order_flags = snapshot->native_command_id
            == SAN9_P1_M2B_REPAIR_NATIVE_ID
        ? snapshot->repair_order_flags
        : snapshot->native_command_id == SAN9_P1_M2B_TRAIN_NATIVE_ID
            ? snapshot->train_order_flags : snapshot->order_flags;
    memcpy(evidence->request_digest, request_digest,
        SAN9_S5_DIGEST_SIZE);
    evidence->menu_wake_count =
        g_runtime.shared->operation.s5.menu.exact_wake_count;
    evidence->menu_restore_count =
        g_runtime.shared->operation.s5.menu.menu_restore_count;
    (void)san9_s5_current_context_business_digest(snapshot,
        evidence->pre_business_digest);
    for (index = 0u; index < SAN9_S5_TOP5_COUNT; ++index) {
        evidence->expected_person_ids[index] = request->expected_person_ids[index];
        evidence->observed_person_ids[index] = snapshot->top5[index].person_id;
    }
}

static int s5_reject_before_event_result(
    San9S5FaultCode fault,
    San9P1M2bResult terminal_code)
{
    San9P1M2bShared *shared = g_runtime.shared;
    if (shared == NULL) {
        return 0;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_REJECTED,
            SAN9_P1_S5_TERMINAL_START_PRE_EVENT)
            != SAN9_P1_S5_TERMINAL_START_PRE_EVENT) {
        s5_poison(fault);
        return 0;
    }
    (void)san9_s5_no_apply_machine_fail(
        &shared->operation.s5.machine, fault);
    shared->operation.s5.evidence.terminal_code = terminal_code;
    shared->operation.s5.evidence.machine_state =
        (uint32_t)san9_s5_no_apply_machine_state(
            &shared->operation.s5.machine);
    (void)san9_p1_s5_evidence_sign(&shared->operation.s5.evidence,
        shared->mailbox.hmac_key, sizeof(shared->mailbox.hmac_key));
    reject_bootstrap(shared, terminal_code, 0);
    return 1;
}

static int s5_reject_before_event(San9S5FaultCode fault)
{
    San9P1M2bShared *shared = g_runtime.shared;
    San9P1M2bResult terminal_code = shared != NULL
            && shared->operation_mode == SAN9_ACTIVE_APPLY_MODE
        ? SAN9_ACTIVE_FAILURE
        : SAN9_P1_M2B_S5_NO_APPLY_FAILED;
    return s5_reject_before_event_result(fault, terminal_code);
}

static S5StartOutcome s5_start_no_apply(uint64_t now_ms)
{
    San9P1M2bShared *shared = g_runtime.shared;
    San9S5CurrentContextSnapshot first;
    San9S5CurrentContextSnapshot second;
    San9S5CurrentContextStatus context_status;
    San9S5RequestStatus request_status = SAN9_S5_REQUEST_INVALID_ARGUMENT;
    San9S5GateStatus gate_status;
    int request_digest_ok;
    uint8_t request_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t handler_prefix[0x70u];
    void *root;
    void *handler;
    const San9S5NoApplyRequest *request;
    if (shared == NULL) {
        return S5_START_RESTART_REQUIRED;
    }
    request = &shared->operation.s5.request;
#if SAN9_COMBINED_BATCH_BUILD
    if (shared->operation_mode == SAN9_ACTIVE_APPLY_MODE) {
        memcpy(&g_runtime.s8_request_latch, request,
            sizeof(g_runtime.s8_request_latch));
        g_runtime.s8_native_id_latch =
            g_runtime.s8_request_latch.native_command_id;
        g_runtime.s8_request_latched = 1u;
        request = &g_runtime.s8_request_latch;
        if (s8_descriptor(g_runtime.s8_native_id_latch) == NULL) {
            g_runtime.s8_request_latched = 0u;
            g_runtime.s8_native_id_latch = 0u;
            return S5_START_REJECTED_PRE_EVENT;
        }
    }
#endif
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_START_PRE_EVENT,
            SAN9_P1_S5_TERMINAL_OPEN) != SAN9_P1_S5_TERMINAL_OPEN
        || shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->claim_enabled, 0, 0) != 1) {
        s5_poison(SAN9_S5_FAULT_PRECHECK);
        return S5_START_RESTART_REQUIRED;
    }
    memset(&first, 0, sizeof(first));
    memset(&second, 0, sizeof(second));
    memset(request_digest, 0, sizeof(request_digest));
    if (shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE) {
        gate_status = san9_s6_repair_apply_once_gate_accept(
            SAN9_ACTIVE_GATE, request,
            shared->mailbox.hmac_key, sizeof(shared->mailbox.hmac_key),
            now_ms, &request_status);
    } else if (shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE) {
        gate_status = san9_s6_train_apply_once_gate_accept(
            SAN9_ACTIVE_GATE, request,
            shared->mailbox.hmac_key, sizeof(shared->mailbox.hmac_key),
            now_ms, &request_status);
    } else if (shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE) {
        gate_status = san9_s6_patrol_apply_once_gate_accept(
            SAN9_ACTIVE_GATE, request,
            shared->mailbox.hmac_key, sizeof(shared->mailbox.hmac_key),
            now_ms, &request_status);
    } else if (shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE) {
        gate_status = san9_s6_cultivate_apply_once_gate_accept(
            SAN9_ACTIVE_GATE, request,
            shared->mailbox.hmac_key, sizeof(shared->mailbox.hmac_key),
            now_ms, &request_status);
    } else if (shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S5_APPLY_ONCE) {
        gate_status = san9_s5_apply_once_gate_accept(SAN9_ACTIVE_GATE,
            request, shared->mailbox.hmac_key,
            sizeof(shared->mailbox.hmac_key), now_ms, &request_status);
#if SAN9_COMBINED_BATCH_BUILD
    } else if (shared->operation_mode
            == SAN9_ACTIVE_APPLY_MODE) {
        gate_status = s8_gate_accept(request, shared->mailbox.hmac_key,
            sizeof(shared->mailbox.hmac_key), now_ms, &request_status);
#endif
    } else {
        gate_status = san9_s5_no_apply_gate_accept(SAN9_ACTIVE_GATE,
            request, shared->mailbox.hmac_key,
            sizeof(shared->mailbox.hmac_key), now_ms, &request_status);
    }
    if (gate_status != SAN9_S5_GATE_ACCEPTED) {
        if (request_status == SAN9_S5_REQUEST_EXPIRED
            || !san9_p1_s5_deadline_is_fresh(GetTickCount64(),
                request->expires_at_ms)
            || shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY
            || s5_terminal_read(shared)
                != SAN9_P1_S5_TERMINAL_START_PRE_EVENT) {
            s5_poison(SAN9_S5_FAULT_REQUEST_IDENTITY);
            return S5_START_RESTART_REQUIRED;
        }
        return s5_reject_before_event(SAN9_S5_FAULT_REQUEST_IDENTITY)
            ? S5_START_REJECTED_PRE_EVENT : S5_START_RESTART_REQUIRED;
    }
    g_runtime.s5_request_expires_at_ms =
        request->expires_at_ms;
    if (!san9_p1_s5_deadline_is_fresh(GetTickCount64(),
            g_runtime.s5_request_expires_at_ms)
        || shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY
        || s5_terminal_read(shared) != SAN9_P1_S5_TERMINAL_START_PRE_EVENT) {
        s5_poison(SAN9_S5_FAULT_REQUEST_IDENTITY);
        return S5_START_RESTART_REQUIRED;
    }
    if (shared->operation_mode == SAN9_P1_M2B_OPERATION_S6_REPAIR_APPLY_ONCE) {
        request_digest_ok = san9_s6_repair_apply_once_request_digest(
            request, request_digest);
    } else if (shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_TRAIN_APPLY_ONCE) {
        request_digest_ok = san9_s6_train_apply_once_request_digest(
            request, request_digest);
    } else if (shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_PATROL_APPLY_ONCE) {
        request_digest_ok = san9_s6_patrol_apply_once_request_digest(
            request, request_digest);
    } else if (shared->operation_mode
            == SAN9_P1_M2B_OPERATION_S6_CULTIVATE_APPLY_ONCE) {
        request_digest_ok = san9_s6_cultivate_apply_once_request_digest(
            request, request_digest);
    } else if (shared->operation_mode == SAN9_P1_M2B_OPERATION_S5_APPLY_ONCE) {
        request_digest_ok = san9_s5_apply_once_request_digest(
            request, request_digest);
#if SAN9_COMBINED_BATCH_BUILD
    } else if (shared->operation_mode == SAN9_ACTIVE_APPLY_MODE) {
        request_digest_ok = s8_request_digest(request, request_digest);
#endif
    } else {
        request_digest_ok = san9_s5_no_apply_request_digest(
            request, request_digest);
    }
    if (san9_s5_no_apply_machine_claim_authenticated(
            &shared->operation.s5.machine, SAN9_ACTIVE_GATE,
            request) != SAN9_S5_MACHINE_OK || !request_digest_ok) {
        return s5_reject_before_event(SAN9_S5_FAULT_REQUEST_IDENTITY)
            ? S5_START_REJECTED_PRE_EVENT : S5_START_RESTART_REQUIRED;
    }
    context_status = s5_capture_ab(&first, &second);
#if SAN9_COMBINED_BATCH_BUILD
    if (shared->operation_mode == SAN9_ACTIVE_APPLY_MODE
        && g_runtime.s8_bound_city_valid != 0u
        && context_status == SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID) {
        return s5_reject_before_event_result(
            SAN9_S5_FAULT_PRECHECK,
            SAN9_P1_M2B_BATCH_REBIND_REQUIRED)
            ? S5_START_REJECTED_PRE_EVENT : S5_START_RESTART_REQUIRED;
    }
#endif
    if (context_status != SAN9_S5_CURRENT_CONTEXT_OK
        || !s5_context_matches_request(
            &first, request)
#if SAN9_COMBINED_BATCH_BUILD
        || (shared->operation_mode == SAN9_ACTIVE_APPLY_MODE
            && !s8_request_still_latched())
#endif
        || san9_s5_no_apply_machine_mark_prechecked(
            &shared->operation.s5.machine) != SAN9_S5_MACHINE_OK
        || !snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 1, g_runtime.easy_snapshot_digest)) {
        return s5_reject_before_event(SAN9_S5_FAULT_PRECHECK)
            ? S5_START_REJECTED_PRE_EVENT : S5_START_RESTART_REQUIRED;
    }
#if SAN9_COMBINED_BATCH_BUILD
    if (shared->operation_mode == SAN9_ACTIVE_APPLY_MODE
        && g_runtime.s8_bound_city_valid == 0u) {
        g_runtime.s8_bound_city.controller_pointer = first.controller_pointer;
        g_runtime.s8_bound_city.city_pointer = first.city_pointer;
        g_runtime.s8_bound_city.corps_pointer = first.corps_pointer;
        MemoryBarrier();
        g_runtime.s8_bound_city_valid = 1u;
    }
#endif
    g_runtime.s5_pre = first;
#if SAN9_COMBINED_BATCH_BUILD
    s8_select_exact_vtables(first.native_command_id);
#endif
    s5_fill_pre_evidence(&shared->operation.s5.evidence, &first,
        request, request_digest);
    memcpy(shared->operation.s5.evidence.pre_easy_digest,
        g_runtime.easy_snapshot_digest, SAN9_S5_DIGEST_SIZE);
    root = (void *)(uintptr_t)first.controller_pointer;
    now_ms = GetTickCount64();
    if (!san9_p1_s5_deadline_is_fresh(now_ms,
            g_runtime.s5_request_expires_at_ms)
        || shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->claim_enabled, 0, 0) != 1
#if SAN9_COMBINED_BATCH_BUILD
        || (shared->operation_mode == SAN9_ACTIVE_APPLY_MODE
            && !s8_request_still_latched())
#endif
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_EVENT_INFLIGHT,
            SAN9_P1_S5_TERMINAL_START_PRE_EVENT)
                != SAN9_P1_S5_TERMINAL_START_PRE_EVENT
        || san9_s5_no_apply_machine_mark_event_attempted(
            &shared->operation.s5.machine) != SAN9_S5_MACHINE_OK) {
        s5_poison(SAN9_S5_FAULT_EVENT);
        return S5_START_RESTART_REQUIRED;
    }
    shared->operation.s5.evidence.event_attempt_count = 1u;
    g_runtime.s5_settle_deadline_ms =
        now_ms + (shared->operation_mode == SAN9_ACTIVE_APPLY_MODE
            ? SAN9_P1_M2B_S5_APPLY_COMPLETION_TIMEOUT_MS
            : SAN9_P1_M2B_S5_SETTLE_TIMEOUT_MS);
    InterlockedExchange(&g_commerce_execute_hits, 0);
    InterlockedExchange(&g_commerce_live_authorization, 1);
    (void)san9_p1_s5_call_root_event(root,
        shared->operation_mode == SAN9_ACTIVE_APPLY_MODE
            ? SAN9_ACTIVE_EVENT_ID : SAN9_P1_M2B_COMMERCE_EVENT_ID);
    if (!s5_read_root_handler(root, &handler)
        || !live_read(NULL, (uint32_t)(uintptr_t)handler,
            handler_prefix, sizeof(handler_prefix))
        || *(const uint32_t *)(const void *)handler_prefix
            != (shared->operation_mode == SAN9_ACTIVE_APPLY_MODE
                ? SAN9_ACTIVE_HANDLER_VPTR
                : SAN9_P1_M2B_COMMERCE_HANDLER_VPTR)
        || *(const uint32_t *)(const void *)(handler_prefix + S5_HANDLER_CORPS_OFFSET)
            != first.corps_pointer
        || *(const uint32_t *)(const void *)(handler_prefix + S5_HANDLER_TARGET_OFFSET)
            != first.city_pointer
        || commerce_shadow_arm(root, handler,
            request,
            shared->operation_mode == SAN9_ACTIVE_APPLY_MODE
                ? SAN9_ACTIVE_EVENT_ID : SAN9_P1_M2B_COMMERCE_EVENT_ID)
            != SAN9_P1_M2B_OK
        || san9_s5_no_apply_machine_mark_shadow_armed(
            &shared->operation.s5.machine) != SAN9_S5_MACHINE_OK) {
        s5_poison(SAN9_S5_FAULT_SHADOW);
        return S5_START_RESTART_REQUIRED;
    }
    shared->operation.s5.evidence.handler_pointer =
        (uint32_t)(uintptr_t)handler;
    shared->operation.s5.evidence.shadow_arm_count = 1u;
    if (!san9_p1_s5_deadline_is_fresh(GetTickCount64(),
            g_runtime.s5_request_expires_at_ms)
        || shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_OPEN,
            SAN9_P1_S5_TERMINAL_EVENT_INFLIGHT)
                != SAN9_P1_S5_TERMINAL_EVENT_INFLIGHT) {
        s5_poison(SAN9_S5_FAULT_EVENT);
        return S5_START_RESTART_REQUIRED;
    }
    san9_p1_secure_zero(request_digest, sizeof(request_digest));
    return S5_START_HANDLER_ARMED;
}

#if SAN9_APPLY_ONCE_BUILD
typedef struct S5ApplyPost {
    uint32_t controller_target;
    uint32_t command_value;
    uint32_t command_maximum;
    uint32_t money;
    uint32_t order_flags;
    uint32_t ready_flags[SAN9_S5_TOP5_COUNT];
} S5ApplyPost;

typedef enum S5ApplyPostStatus {
    S5_APPLY_POST_PENDING = 0,
    S5_APPLY_POST_OK = 1,
    S5_APPLY_POST_INVALID = 2
} S5ApplyPostStatus;

static S5ApplyPostStatus s5_capture_apply_post(S5ApplyPost *output)
{
    San9P1M2bShared *shared = g_runtime.shared;
    const San9S5CurrentContextSnapshot *pre = &g_runtime.s5_pre;
    uint8_t root[0x3Cu];
    uint8_t city[0x1E4u];
    uint8_t corps[S5_CORPS_MONEY_OFFSET + sizeof(uint32_t)];
    uint8_t person[S5_PERSON_CAPTURE_SIZE];
    S5ApplyPost post;
    uint32_t index;
    if (output == NULL || shared == NULL
        || pre->exact_top5_count != SAN9_S5_TOP5_COUNT
        || !live_read(NULL, pre->controller_pointer, root, sizeof(root))) {
        return S5_APPLY_POST_INVALID;
    }
    if (s5_bridge_u32(root, 0u) != S5_CONTROLLER_VPTR
        || s5_bridge_u32(root, 0x30u) != pre->controller_corps) {
        return S5_APPLY_POST_INVALID;
    }
    if (s5_bridge_u32(root, 0x0Cu) != 0u
        || s5_bridge_u32(root, 0x10u) != pre->controller_pointer
        || s5_bridge_u32(root, S5_CONTROLLER_STATE_OFFSET)
            != S5_CONTROLLER_IDLE_STATE) {
        return S5_APPLY_POST_PENDING;
    }
    memset(&post, 0, sizeof(post));
    post.controller_target =
        s5_bridge_u32(root, S5_CONTROLLER_TARGET_OFFSET);
    if (post.controller_target != 0u
        && post.controller_target != pre->city_pointer) {
        return S5_APPLY_POST_INVALID;
    }
    if (!live_read(NULL, pre->city_pointer, city, sizeof(city))
        || !live_read(NULL, pre->corps_pointer, corps, sizeof(corps))
        || s5_bridge_u32(city, 0u) != S5_CITY_VPTR
        || city[S5_CITY_TYPE_OFFSET] != S5_CITY_TYPE
        || s5_bridge_u32(city, S5_CITY_SELF_OFFSET) != pre->city_pointer
        || s5_bridge_u32(city, S5_CITY_CORPS_OFFSET) != pre->corps_pointer) {
        return S5_APPLY_POST_INVALID;
    }
    post.command_value = SAN9_ACTIVE_VALUE_WORD
        ? s5_bridge_u16(city, SAN9_ACTIVE_VALUE_OFFSET)
        : s5_bridge_u32(city, SAN9_ACTIVE_VALUE_OFFSET);
    post.command_maximum = SAN9_ACTIVE_MAXIMUM_FIXED
        ? SAN9_ACTIVE_MAXIMUM_VALUE
        : s5_bridge_u32(city, SAN9_ACTIVE_MAXIMUM_OFFSET);
    post.order_flags = SAN9_ACTIVE_ORDER_WORD
        ? s5_bridge_u16(city, SAN9_ACTIVE_ORDER_OFFSET)
        : s5_bridge_u32(city, SAN9_ACTIVE_ORDER_OFFSET);
    post.money = s5_bridge_u32(corps, S5_CORPS_MONEY_OFFSET);
    if (pre->corps_money < SAN9_ACTIVE_COST
        || post.money != pre->corps_money - SAN9_ACTIVE_COST
        || post.command_value <= SAN9_ACTIVE_PRE_CURRENT(pre)
        || post.command_value > SAN9_ACTIVE_PRE_MAXIMUM(pre)
        || post.command_maximum != SAN9_ACTIVE_PRE_MAXIMUM(pre)
        || post.order_flags
            != (SAN9_ACTIVE_PRE_ORDER(pre) | SAN9_ACTIVE_ORDER_FLAG)) {
        return S5_APPLY_POST_INVALID;
    }
    for (index = 0u; index < SAN9_S5_TOP5_COUNT; ++index) {
        const San9S5CommerceOfficer *officer = &pre->top5[index];
        if (!live_read(NULL, officer->person_pointer,
                person, sizeof(person))
            || s5_bridge_u16(person, S5_PERSON_ID_OFFSET)
                != officer->person_id
            || s5_bridge_u32(person, SAN9_ACTIVE_ABILITY_OFFSET)
                != officer->effective_politics
            || s5_bridge_u32(person, S5_PERSON_IDENTITY_OFFSET)
                != officer->identity
            || s5_bridge_u32(person, S5_PERSON_RESIDENCE_OFFSET)
                != officer->residence_pointer) {
            return S5_APPLY_POST_INVALID;
        }
        post.ready_flags[index] =
            s5_bridge_u32(person, S5_PERSON_READY_FLAGS_OFFSET);
        if (post.ready_flags[index]
                != (officer->ready_flags | UINT32_C(0x1000))) {
            return S5_APPLY_POST_INVALID;
        }
    }
    *output = post;
    return S5_APPLY_POST_OK;
}

static void s5_try_finish_apply(uint64_t now_ms)
{
    San9P1M2bShared *shared = g_runtime.shared;
    San9P1S5NoApplyEvidence *evidence;
    S5ApplyPost first;
    S5ApplyPost second;
    S5ApplyPostStatus status;
    uint8_t easy_digest[SAN9_P1_DIGEST_SIZE];
    if (shared == NULL) {
        return;
    }
    if (InterlockedCompareExchange(&g_apply_enter_hits, 0, 0) != 1
        || InterlockedCompareExchange(&g_apply_return_hits, 0, 0) != 1
        || InterlockedCompareExchange(&g_apply_live_authorization, 0, 0) != 0
        || g_apply_command != NULL) {
        if (now_ms > g_runtime.s5_settle_deadline_ms) {
            s5_poison(SAN9_S5_FAULT_APPLY_OBSERVED);
        }
        return;
    }
    memset(&first, 0, sizeof(first));
    memset(&second, 0, sizeof(second));
    status = s5_capture_apply_post(&first);
    if (status == S5_APPLY_POST_PENDING
        && now_ms <= g_runtime.s5_settle_deadline_ms) {
        return;
    }
    if (status != S5_APPLY_POST_OK
        || s5_capture_apply_post(&second) != S5_APPLY_POST_OK
        || memcmp(&first, &second, sizeof(first)) != 0
        || shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY
        || !snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 1, easy_digest)
        || !san9_p1_constant_time_equal(easy_digest,
            shared->operation.s5.evidence.pre_easy_digest,
            sizeof(easy_digest))
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_FINISH_COMMIT,
            SAN9_P1_S5_TERMINAL_OPEN) != SAN9_P1_S5_TERMINAL_OPEN) {
        san9_p1_secure_zero(easy_digest, sizeof(easy_digest));
        s5_poison(SAN9_S5_FAULT_APPLY_OBSERVED);
        return;
    }
    evidence = &shared->operation.s5.evidence;
    evidence->post_command_value = first.command_value;
    evidence->command_maximum = first.command_maximum;
    evidence->native_command_id = SAN9_ACTIVE_NATIVE_ID;
    evidence->command_order_mask = SAN9_ACTIVE_ORDER_FLAG;
    if (SAN9_ACTIVE_NATIVE_ID == SAN9_P1_M2B_COMMERCE_NATIVE_ID) {
        evidence->post_commerce = first.command_value;
    }
    evidence->post_money = first.money;
    evidence->post_order_flags = first.order_flags;
    evidence->observed_apply_count = 1u;
    evidence->terminal_code = SAN9_P1_M2B_OK;
    evidence->machine_state = SAN9_S5_STATE_VPTR_RESTORED;
    evidence->last_tid = GetCurrentThreadId();
    memcpy(evidence->post_easy_digest, easy_digest, sizeof(easy_digest));
    san9_p1_secure_zero(easy_digest, sizeof(easy_digest));
    if (!san9_p1_s5_evidence_sign(evidence,
            shared->mailbox.hmac_key, sizeof(shared->mailbox.hmac_key))
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_SUCCESS,
            SAN9_P1_S5_TERMINAL_FINISH_COMMIT)
                != SAN9_P1_S5_TERMINAL_FINISH_COMMIT) {
        s5_poison(SAN9_S5_FAULT_STATE_CORRUPTION);
    }
}
#if SAN9_COMBINED_BATCH_BUILD
static void s8_try_start_batch_idle(void)
{
    San9P1M2bShared *shared = g_runtime.shared;
    San9P1S5MenuHandoff *handoff;
    S5StartOutcome outcome;
    if (shared == NULL || shared->operation_mode != SAN9_ACTIVE_APPLY_MODE) {
        return;
    }
    handoff = &shared->operation.s5.menu;
    if (InterlockedCompareExchange((volatile LONG *)&handoff->state, 0, 0)
            != SAN9_P1_S5_MENU_BATCH_IDLE_ARMED) {
        return;
    }
    if (shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY
        || GetCurrentThreadId() != g_runtime.main_thread_id
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->claim_enabled, 0, 0) != 1
        || san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
            != SAN9_S5_STATE_SEALED
        || s5_terminal_read(shared) != SAN9_P1_S5_TERMINAL_OPEN
        || g_commerce_handler != NULL || g_apply_command != NULL
        || InterlockedCompareExchange(&g_commerce_live_authorization, 0, 0) != 0
        || InterlockedCompareExchange(&g_apply_live_authorization, 0, 0) != 0
        || InterlockedCompareExchange((volatile LONG *)&handoff->state,
            SAN9_P1_S5_MENU_BATCH_IDLE_CLAIMED,
            SAN9_P1_S5_MENU_BATCH_IDLE_ARMED)
                != SAN9_P1_S5_MENU_BATCH_IDLE_ARMED) {
        s5_poison(SAN9_S5_FAULT_PRECHECK);
        return;
    }
    outcome = s5_start_no_apply(GetTickCount64());
    if (outcome == S5_START_HANDLER_ARMED) {
        (void)InterlockedIncrement((volatile LONG *)&handoff->start_count);
        InterlockedExchange((volatile LONG *)&handoff->state,
            SAN9_P1_S5_MENU_STARTED);
    } else if (outcome == S5_START_REJECTED_PRE_EVENT) {
        InterlockedExchange((volatile LONG *)&handoff->state,
            SAN9_P1_S5_MENU_REJECTED);
    } else {
        s5_poison(SAN9_S5_FAULT_STATE_CORRUPTION);
    }
}
#endif
#endif

static void s5_try_finish_no_apply(uint64_t now_ms)
{
    San9P1M2bShared *shared = g_runtime.shared;
    San9S5CurrentContextReader reader;
    San9S5ExpectedIdentity identity;
    San9S5FrozenPostSnapshot first;
    San9S5FrozenPostSnapshot second;
    San9S5CurrentContextStatus status;
    San9S5CurrentContextStatus second_status;
    San9P1S5NoApplyEvidence *evidence;
    if (shared == NULL) {
        return;
    }
    (void)now_ms;
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_FINISH_VERIFY,
            SAN9_P1_S5_TERMINAL_OPEN) != SAN9_P1_S5_TERMINAL_OPEN
        || shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY
        || !san9_p1_s5_deadline_is_fresh(GetTickCount64(),
            g_runtime.s5_request_expires_at_ms)) {
        s5_poison(SAN9_S5_FAULT_APPLY_OBSERVED);
        return;
    }
    evidence = &shared->operation.s5.evidence;
    memset(&reader, 0, sizeof(reader));
    memset(&identity, 0, sizeof(identity));
    memset(&first, 0, sizeof(first));
    memset(&second, 0, sizeof(second));
    reader.read = s5_direct_read;
    identity.process_id = g_runtime.binding_frame.game_pid;
    identity.main_thread_id = g_runtime.binding_frame.main_tid;
    identity.process_generation = g_runtime.binding_frame.game_generation;
    identity.window_handle = g_runtime.binding_frame.game_hwnd;
    status = san9_s5_frozen_post_capture_reader(
        &reader, &identity, &g_runtime.s5_pre, &first);
    if (status == SAN9_S5_CURRENT_CONTEXT_FROZEN_POST_INVALID
        && GetTickCount64() <= g_runtime.s5_settle_deadline_ms
        && san9_p1_s5_deadline_is_fresh(GetTickCount64(),
            g_runtime.s5_request_expires_at_ms)
        && shared_state(shared) == SAN9_P1_M2B_BOOTSTRAP_READY
        && g_commerce_handler == NULL
        && InterlockedCompareExchange(
            &g_commerce_live_authorization, 0, 0) == 0
        && InterlockedCompareExchange(&g_commerce_execute_hits, 0, 0) == 1) {
        if (InterlockedCompareExchange(
                (volatile LONG *)&shared->operation.s5.terminal_state,
                SAN9_P1_S5_TERMINAL_OPEN,
                SAN9_P1_S5_TERMINAL_FINISH_VERIFY)
                == SAN9_P1_S5_TERMINAL_FINISH_VERIFY) {
            return;
        }
        s5_poison(SAN9_S5_FAULT_APPLY_OBSERVED);
        return;
    }
    if (status != SAN9_S5_CURRENT_CONTEXT_OK
        || GetTickCount64() > g_runtime.s5_settle_deadline_ms
        || g_commerce_handler != NULL
        || InterlockedCompareExchange(
            &g_commerce_live_authorization, 0, 0) != 0
        || InterlockedCompareExchange(&g_commerce_execute_hits, 0, 0) != 1
        || !snapshot_digest(&g_runtime.easy_binding,
            &g_runtime.easy_callbacks, 1, evidence->post_easy_digest)
        || !san9_p1_constant_time_equal(evidence->pre_easy_digest,
            evidence->post_easy_digest, SAN9_S5_DIGEST_SIZE)) {
        s5_poison(SAN9_S5_FAULT_APPLY_OBSERVED);
        return;
    }
    second_status = san9_s5_frozen_post_capture_reader(
        &reader, &identity, &g_runtime.s5_pre, &second);
    if ((second_status == SAN9_S5_CURRENT_CONTEXT_FROZEN_POST_INVALID
            || (second_status == SAN9_S5_CURRENT_CONTEXT_OK
                && !san9_s5_frozen_post_equal(&first, &second)))
        && GetTickCount64() <= g_runtime.s5_settle_deadline_ms
        && san9_p1_s5_deadline_is_fresh(GetTickCount64(),
            g_runtime.s5_request_expires_at_ms)
        && shared_state(shared) == SAN9_P1_M2B_BOOTSTRAP_READY) {
        if (InterlockedCompareExchange(
                (volatile LONG *)&shared->operation.s5.terminal_state,
                SAN9_P1_S5_TERMINAL_OPEN,
                SAN9_P1_S5_TERMINAL_FINISH_VERIFY)
                == SAN9_P1_S5_TERMINAL_FINISH_VERIFY) {
            return;
        }
        s5_poison(SAN9_S5_FAULT_APPLY_OBSERVED);
        return;
    }
    if (second_status != SAN9_S5_CURRENT_CONTEXT_OK
        || !san9_s5_frozen_post_equal(&first, &second)
        || !san9_p1_constant_time_equal(evidence->pre_business_digest,
            first.business_digest, SAN9_S5_DIGEST_SIZE)) {
        s5_poison(SAN9_S5_FAULT_APPLY_OBSERVED);
        return;
    }
    if (!san9_p1_s5_deadline_is_fresh(GetTickCount64(),
            g_runtime.s5_request_expires_at_ms)
        || shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_FINISH_COMMIT,
            SAN9_P1_S5_TERMINAL_FINISH_VERIFY)
                != SAN9_P1_S5_TERMINAL_FINISH_VERIFY
        || san9_s5_no_apply_machine_prove_no_apply(
            &shared->operation.s5.machine) != SAN9_S5_MACHINE_OK) {
        s5_poison(SAN9_S5_FAULT_APPLY_OBSERVED);
        return;
    }
    evidence->post_commerce = first.commerce_current;
    evidence->post_money = first.corps_money;
    evidence->post_order_flags = first.order_flags;
    evidence->observed_apply_count = 0u;
    evidence->terminal_code = SAN9_P1_M2B_OK;
    evidence->machine_state = SAN9_S5_STATE_NO_APPLY_PROVEN;
    evidence->last_tid = GetCurrentThreadId();
    memcpy(evidence->post_business_digest, first.business_digest,
        SAN9_S5_DIGEST_SIZE);
    if (!san9_p1_s5_evidence_sign(evidence,
            shared->mailbox.hmac_key, sizeof(shared->mailbox.hmac_key))) {
        s5_poison(SAN9_S5_FAULT_STATE_CORRUPTION);
        return;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.terminal_state,
            SAN9_P1_S5_TERMINAL_SUCCESS,
            SAN9_P1_S5_TERMINAL_FINISH_COMMIT)
            != SAN9_P1_S5_TERMINAL_FINISH_COMMIT) {
        s5_poison(SAN9_S5_FAULT_STATE_CORRUPTION);
    }
}
#endif

void san9_p1_m2b_after_original(
    void *app,
    int flag,
    uint32_t depth,
    uint32_t caller)
{
    uint64_t token;
    uint8_t raw_request[SAN9_P1_FRAME_SIZE];
    uint8_t raw_response[SAN9_P1_FRAME_SIZE];
    San9P1Frame verified;
    San9P1DecodeStatus decode;
    San9P1GateStatus gate;
    San9P1M2Status status;
    San9P1M2bShared *shared = g_runtime.shared;
    if (InterlockedCompareExchange(&g_runtime.installed, 0, 0) != 1
        || shared == NULL
        || (shared->operation_mode != SAN9_P1_M2B_OPERATION_PROBE0
            && shared->operation_mode != SAN9_P1_M2B_OPERATION_PING
            && shared->operation_mode != SAN9_P1_M2B_OPERATION_OBSERVE
            && shared->operation_mode != SAN9_P1_M2B_OPERATION_S5_NO_APPLY
#if SAN9_APPLY_ONCE_BUILD
            && shared->operation_mode != SAN9_ACTIVE_APPLY_MODE
#endif
            && shared->operation_mode != SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE)) {
        return;
    }
    if (shared_state(shared) != SAN9_P1_M2B_BOOTSTRAP_READY) {
        return;
    }
    (void)InterlockedIncrement(
        (volatile LONG *)&shared->probe0_original_return_count);
    if (depth == 0u || depth != g_idle_depth) {
        (void)InterlockedIncrement((volatile LONG *)&shared->probe0_nested_count);
        return;
    }
    if (!exact_idle_identity(app, flag, caller)) {
        (void)InterlockedIncrement(
            (volatile LONG *)&shared->probe0_identity_reject_count);
        return;
    }
    if (depth != 1u) {
        (void)InterlockedIncrement((volatile LONG *)&shared->probe0_nested_count);
        if (shared->operation_mode == SAN9_P1_M2B_OPERATION_OBSERVE) {
            s3_observe_tick(&shared->operation.s3.trace);
        }
        return;
    }
    (void)InterlockedCompareExchange((volatile LONG *)&shared->probe0_first_tid,
        (LONG)GetCurrentThreadId(), 0);
    InterlockedExchange((volatile LONG *)&shared->probe0_last_tid,
        (LONG)GetCurrentThreadId());
    InterlockedExchange((volatile LONG *)&shared->probe0_last_caller,
        (LONG)caller);
    InterlockedExchange((volatile LONG *)&shared->probe0_last_app,
        (LONG)(uintptr_t)app);
    (void)InterlockedIncrement((volatile LONG *)&shared->probe0_outer_count);
    (void)InterlockedIncrement((volatile LONG *)&shared->probe0_idle_count);

    if (shared->operation_mode == SAN9_P1_M2B_OPERATION_OBSERVE) {
        s3_observe_tick(&shared->operation.s3.trace);
        return;
    }

#if S5_NO_APPLY_BUILD
    if (shared->operation_mode == SAN9_P1_M2B_OPERATION_S5_NO_APPLY) {
        San9S5MachineState machine_state = san9_s5_no_apply_machine_state(
            &shared->operation.s5.machine);
        if (machine_state == SAN9_S5_STATE_VPTR_RESTORED) {
            s5_try_finish_no_apply(GetTickCount64());
        }
        return;
    }
#if SAN9_APPLY_ONCE_BUILD
    if (shared->operation_mode == SAN9_ACTIVE_APPLY_MODE) {
#if SAN9_COMBINED_BATCH_BUILD
        LONG menu_state = InterlockedCompareExchange(
            (volatile LONG *)&shared->operation.s5.menu.state, 0, 0);
        if (menu_state == SAN9_P1_S5_MENU_BATCH_RESET_REQUESTED) {
            (void)InterlockedCompareExchange(
                (volatile LONG *)&shared->operation.s5.menu.state,
                SAN9_P1_S5_MENU_BATCH_RESET_ACK,
                SAN9_P1_S5_MENU_BATCH_RESET_REQUESTED);
            return;
        }
        if (menu_state == SAN9_P1_S5_MENU_BATCH_RESET_ACK) {
            return;
        }
#endif
        if (san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
                == SAN9_S5_STATE_VPTR_RESTORED) {
            s5_try_finish_apply(GetTickCount64());
        }
#if SAN9_COMBINED_BATCH_BUILD
        else if (san9_s5_no_apply_machine_state(&shared->operation.s5.machine)
                == SAN9_S5_STATE_SEALED) {
            s8_try_start_batch_idle();
        }
#endif
        return;
    }
#endif
#endif

    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->claim_enabled, 0, 0) != 1
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->controller_request_state,
            0, 0) != SAN9_P1_M2_SLOT_READY) {
        return;
    }
    if (InterlockedCompareExchange(
            (volatile LONG *)&shared->controller_request_state,
            SAN9_P1_M2_SLOT_CLAIMED, SAN9_P1_M2_SLOT_READY)
            != SAN9_P1_M2_SLOT_READY) {
        return;
    }
    memcpy(raw_request, shared->controller_request_frame,
        sizeof(raw_request));
    token = 0u;
    status = san9_p1_m2_controller_publish_request(&g_runtime.m2,
        raw_request, sizeof(raw_request), &token);
    san9_p1_secure_zero(raw_request, sizeof(raw_request));
    if (status != SAN9_P1_M2_OK || token == 0u
        || token > SAN9_P1_M2B_PING_COUNT) {
        reject_bootstrap(g_runtime.shared, SAN9_P1_M2B_PING_FAILED, 1);
        return;
    }
    status = san9_p1_m2_target_process_request(&g_runtime.m2, token,
        GetTickCount64(), &g_runtime.actions, &decode, &gate);
    if (status != SAN9_P1_M2_OK) {
        reject_bootstrap(g_runtime.shared, SAN9_P1_M2B_PING_FAILED, 1);
        return;
    }
    memset(&verified, 0, sizeof(verified));
    status = san9_p1_m2_controller_consume_response(&g_runtime.m2, token,
        &verified, &decode);
    if (status != SAN9_P1_M2_OK
        || !san9_p1_encode(&verified, g_runtime.shared->mailbox.hmac_key,
            sizeof(g_runtime.shared->mailbox.hmac_key), raw_response,
            sizeof(raw_response))
        || InterlockedCompareExchange(
            (volatile LONG *)&shared->controller_response_state,
            SAN9_P1_M2_SLOT_WRITING, SAN9_P1_M2_SLOT_EMPTY)
            != SAN9_P1_M2_SLOT_EMPTY) {
        san9_p1_secure_zero(&verified, sizeof(verified));
        san9_p1_secure_zero(raw_response, sizeof(raw_response));
        reject_bootstrap(shared, SAN9_P1_M2B_PING_FAILED, 1);
        return;
    }
    memcpy(shared->controller_response_frame, raw_response,
        sizeof(raw_response));
    san9_p1_secure_zero(&verified, sizeof(verified));
    san9_p1_secure_zero(raw_response, sizeof(raw_response));
    MemoryBarrier();
    InterlockedExchange(
        (volatile LONG *)&shared->controller_response_state,
        SAN9_P1_M2_SLOT_COMPLETE);
    InterlockedExchange(
        (volatile LONG *)&shared->controller_request_state,
        SAN9_P1_M2_SLOT_COMPLETE);
}

SAN9_P1_M2B_EXPORT uint32_t San9BridgeP1M2b_Contract(
    San9P1M2bContract *output,
    uint32_t output_size)
{
    if (output == NULL || output_size != sizeof(*output)) {
        return SAN9_P1_M2B_INVALID;
    }
    memset(output, 0, sizeof(*output));
    output->magic = SAN9_P1_M2B_CONTRACT_MAGIC;
    output->schema_major = SAN9_P1_M2B_SCHEMA_MAJOR;
    output->schema_minor = SAN9_P1_M2B_SCHEMA_MINOR;
    output->structure_size = sizeof(*output);
    output->shared_size = sizeof(San9P1M2bShared);
    output->mailbox_offset = offsetof(San9P1M2bShared, mailbox);
    output->ping_count = SAN9_P1_M2B_PING_COUNT;
    output->live_execution_default = SAN9_P1_M2B_LIVE_EXECUTION_DEFAULT;
    output->ui_connected = SAN9_P1_M2B_UI_CONNECTED;
    output->commerce_abi_ready = SAN9_P1_M2B_COMMERCE_ABI_READY;
    output->commerce_live_authorization =
        SAN9_P1_M2B_COMMERCE_LIVE_AUTHORIZATION;
    output->exact_idle_slot = SAN9_P1_M2B_EXACT_IDLE_SLOT;
    output->exact_original_idle = SAN9_P1_M2B_EXACT_ORIGINAL_IDLE;
    output->exact_idle_caller = SAN9_P1_M2B_EXACT_IDLE_CALLER;
    output->s3_readonly_build = S3_READONLY_BUILD;
    output->readonly_dispatch_count = san9_p1_s3_read_dispatch_count();
    output->write_dispatch_count = san9_p1_s3_write_dispatch_count();
    output->readonly_max_bytes = SAN9_P1_S3_READ_MAX;
    output->s5_modal_wake_authorization =
        SAN9_P1_M2B_S5_MODAL_WAKE_AUTHORIZATION;
    if (san9_p1_s3_build_identity()[0] != 'S') {
        return SAN9_P1_M2B_INVALID;
    }
    if (g_s5_modal_probe_identity[0] != 'S'
        || g_s5_v8_identity[0] != 'S') {
        return SAN9_P1_M2B_INVALID;
    }
    return SAN9_P1_M2B_OK;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        memset(&g_runtime, 0, sizeof(g_runtime));
        g_runtime.module = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}
