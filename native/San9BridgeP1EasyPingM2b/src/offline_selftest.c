#include <stdio.h>
#include <stdatomic.h>
#include <string.h>
#if SAN9_S5_MODAL_OFFLINE_FAKE
#include <xmmintrin.h>
#endif

#include "san9_p1_m2b.h"
#include "easy_page_normalization.h"

static unsigned int passed;
static unsigned int failed;
static void check(int condition, const char *name);

#if SAN9_S5_MODAL_OFFLINE_FAKE
static uint32_t modal_fake_sequence;
static uint32_t modal_fake_original_count;
static uint32_t modal_fake_after_count;
static uint32_t modal_fake_failure;
static uint32_t modal_fake_caller;
static void *modal_fake_original_self;
static void *modal_fake_after_self;
static uint32_t modal_fake_original_mxcsr;
static uint32_t modal_fake_after_mxcsr;
static uint32_t v8_fake_sequence;
static uint32_t v8_fake_entry_count;
static uint32_t v8_fake_original_count;
static uint32_t v8_fake_after_count;
static uint32_t v8_fake_failure;
static uint32_t v8_fake_entry_token;
static uint32_t v8_fake_caller;
static uint32_t v8_fake_original_entry_mxcsr;
static uint32_t v8_fake_original_exit_mxcsr;
static uint32_t v8_fake_after_mxcsr;
static void *v8_fake_entry_self;
static void *v8_fake_original_self;

uint32_t SAN9_P1_M2B_FASTCALL san9_p1_s5_modal_fake_original(
    void *menu, void *unused_edx)
{
    (void)unused_edx;
    if (modal_fake_sequence != 0u) {
        modal_fake_failure = 1u;
    }
    modal_fake_sequence = 1u;
    ++modal_fake_original_count;
    modal_fake_original_self = menu;
    _mm_setcsr(modal_fake_original_mxcsr);
    return UINT32_C(0xA55AA55A);
}

void san9_p1_s5_modal_after_original(void *menu, uint32_t caller)
{
    if (modal_fake_sequence != 1u) {
        modal_fake_failure = 1u;
    }
    modal_fake_sequence = 2u;
    ++modal_fake_after_count;
    modal_fake_after_self = menu;
    modal_fake_caller = caller;
    _mm_setcsr(modal_fake_after_mxcsr);
}

uint32_t san9_p1_s5_v8_menu_entry(void *menu, uint32_t caller)
{
    if (v8_fake_sequence != 0u || menu == NULL || caller == 0u) {
        v8_fake_failure = 1u;
    }
    v8_fake_sequence = 1u;
    ++v8_fake_entry_count;
    v8_fake_entry_self = menu;
    v8_fake_caller = caller;
    _mm_setcsr(v8_fake_after_mxcsr);
    return v8_fake_entry_token;
}

uint32_t SAN9_P1_M2B_FASTCALL san9_p1_s5_v8_fake_original(
    void *menu, void *unused_edx)
{
    (void)unused_edx;
    if (v8_fake_sequence != 1u || menu != v8_fake_entry_self
        || _mm_getcsr() != v8_fake_original_entry_mxcsr) {
        v8_fake_failure = 1u;
    }
    v8_fake_sequence = 2u;
    ++v8_fake_original_count;
    v8_fake_original_self = menu;
    _mm_setcsr(v8_fake_original_exit_mxcsr);
    return UINT32_C(0x5AA55AA5);
}

void san9_p1_s5_v8_after_original(uint32_t entry_token, uint32_t caller)
{
    if (v8_fake_sequence != 2u || entry_token != v8_fake_entry_token
        || caller != v8_fake_caller) {
        v8_fake_failure = 1u;
    }
    v8_fake_sequence = 3u;
    ++v8_fake_after_count;
    _mm_setcsr(v8_fake_after_mxcsr);
}
#endif

typedef struct FrozenFixture {
    uint32_t root_address;
    uint32_t city_address;
    uint32_t corps_address;
    uint32_t person_address[SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT];
    uint8_t root[0x3Cu];
    uint8_t city[0x1E4u];
    uint8_t corps[0xC0u];
    uint8_t person[SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT][0xF8u];
} FrozenFixture;

static void fixture_u16(uint8_t *bytes, size_t offset, uint16_t value)
{
    memcpy(bytes + offset, &value, sizeof(value));
}

static void fixture_u32(uint8_t *bytes, size_t offset, uint32_t value)
{
    memcpy(bytes + offset, &value, sizeof(value));
}

static int frozen_fixture_read(
    void *context, uint32_t address, void *output, size_t output_size)
{
    FrozenFixture *fixture = (FrozenFixture *)context;
    uint32_t index;
    if (fixture == NULL || output == NULL) {
        return 0;
    }
    if (address == fixture->root_address
        && output_size == sizeof(fixture->root)) {
        memcpy(output, fixture->root, output_size);
        return 1;
    }
    if (address == fixture->city_address
        && output_size == sizeof(fixture->city)) {
        memcpy(output, fixture->city, output_size);
        return 1;
    }
    if (address == fixture->corps_address
        && output_size == sizeof(fixture->corps)) {
        memcpy(output, fixture->corps, output_size);
        return 1;
    }
    for (index = 0u; index < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT; ++index) {
        if (address == fixture->person_address[index]
            && output_size == sizeof(fixture->person[index])) {
            memcpy(output, fixture->person[index], output_size);
            return 1;
        }
    }
    return 0;
}

static void test_s5_v8_frozen_post(void)
{
    FrozenFixture fixture;
    San9S5CurrentContextReader reader;
    San9S5ExpectedIdentity identity;
    San9S5CurrentContextSnapshot pre;
    San9S5FrozenPostSnapshot first;
    San9S5FrozenPostSnapshot second;
    uint8_t expected_digest[SAN9_S5_CURRENT_CONTEXT_DIGEST_SIZE];
    uint32_t index;
    memset(&fixture, 0, sizeof(fixture));
    memset(&reader, 0, sizeof(reader));
    memset(&identity, 0, sizeof(identity));
    memset(&pre, 0, sizeof(pre));
    memset(&first, 0, sizeof(first));
    memset(&second, 0, sizeof(second));
    memset(expected_digest, 0, sizeof(expected_digest));
    fixture.root_address = UINT32_C(0x00100000);
    fixture.city_address = UINT32_C(0x0124DB58) + UINT32_C(0x1F0);
    fixture.corps_address = UINT32_C(0x01253C38) + UINT32_C(0xD4);
    identity.process_id = 10u;
    identity.main_thread_id = 11u;
    identity.process_generation = UINT64_C(12);
    identity.window_handle = UINT64_C(13);
    reader.read = frozen_fixture_read;
    reader.context = &fixture;
    fixture_u32(fixture.root, 0x00u, UINT32_C(0x00610BC8));
    fixture_u32(fixture.root, 0x0Cu, 0u);
    fixture_u32(fixture.root, 0x10u, fixture.root_address);
    fixture_u32(fixture.root, 0x30u, fixture.corps_address);
    fixture_u32(fixture.root, 0x34u, UINT32_C(0x3E9));
    fixture_u32(fixture.root, 0x38u, 0u);
    fixture_u32(fixture.city, 0x00u, UINT32_C(0x00605938));
    fixture.city[0x06u] = 5u;
    fixture_u32(fixture.city, 0x58u, UINT32_C(0x00606490));
    fixture_u32(fixture.city, 0xBCu, fixture.city_address);
    fixture_u32(fixture.city, 0xCCu, fixture.corps_address);
    fixture_u32(fixture.city, 0xE0u, UINT32_C(0x00200000));
    fixture_u32(fixture.city, 0xE4u, UINT32_C(0x00200100));
    fixture_u32(fixture.city, 0xE8u, 5u);
    fixture_u32(fixture.city, 0x1CCu, 100u);
    fixture_u32(fixture.city, 0x1D4u, 500u);
    fixture_u32(fixture.city, 0x1E0u, 0u);
    fixture_u32(fixture.city, 0x88u, 10000u);
    fixture_u32(fixture.city, 0x90u, 80u);
    fixture_u16(fixture.city, 0x3Eu, 0u);
    fixture_u16(fixture.city, 0x3Cu, 500u);
    fixture_u32(fixture.city, 0x1C8u, 1000u);
    fixture_u32(fixture.corps, 0x14u, 1000u);
    fixture_u32(fixture.corps, 0x34u, 1u);
    fixture_u32(fixture.corps, 0xB8u, fixture.corps_address);
    fixture_u32(fixture.corps, 0xBCu, UINT32_C(0x01258EE0));
    pre.structure_size = sizeof(pre);
    pre.schema_major = SAN9_S5_CURRENT_CONTEXT_SCHEMA_MAJOR;
    pre.schema_minor = SAN9_S5_CURRENT_CONTEXT_SCHEMA_MINOR;
    pre.binding = identity;
    pre.controller_pointer = fixture.root_address;
    pre.controller_vtable = UINT32_C(0x00610BC8);
    pre.controller_corps = fixture.corps_address;
    pre.controller_state = UINT32_C(0x3E9);
    pre.city_id = 1u;
    pre.city_pointer = fixture.city_address;
    pre.city_vtable = UINT32_C(0x00605938);
    pre.city_type = 5u;
    pre.city_self_pointer = fixture.city_address;
    pre.city_corps_pointer = fixture.corps_address;
    pre.city_residence_pointer = fixture.city_address + 0x58u;
    pre.city_residence_vtable = UINT32_C(0x00606490);
    pre.corps_id = 1u;
    pre.corps_pointer = fixture.corps_address;
    pre.corps_flags = 1u;
    pre.corps_money = 1000u;
    pre.corps_main_pointer = fixture.corps_address;
    pre.corps_main_id = 1u;
    pre.corps_leader_pointer = UINT32_C(0x01258EE0);
    pre.corps_leader_id = 0u;
    pre.main_corps_flags = 1u;
    pre.native_command_id = SAN9_S5_COMMERCE_NATIVE_ID;
    pre.commerce_current = 100u;
    pre.commerce_maximum = 500u;
    pre.patrol_current = 0u;
    pre.patrol_maximum = 1000u;
    pre.order_flags = 0u;
    pre.train_troops = 10000u;
    pre.train_morale = 80u;
    pre.train_maximum = 100u;
    pre.train_order_flags = 0u;
    pre.repair_current = 500u;
    pre.repair_maximum = 1000u;
    pre.repair_order_flags = 0u;
    pre.resident_first = UINT32_C(0x00200000);
    pre.resident_last = UINT32_C(0x00200100);
    pre.resident_count = 5u;
    pre.exact_top5_count = SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT;
    for (index = 0u; index < SAN9_S5_CURRENT_CONTEXT_TOP5_COUNT; ++index) {
        San9S5CommerceOfficer *officer = &pre.top5[index];
        fixture.person_address[index] =
            UINT32_C(0x01258EE0) + index * UINT32_C(0x128);
        fixture_u16(fixture.person[index], 0x04u, (uint16_t)index);
        fixture_u32(fixture.person[index], 0x60u, 100u - index);
        fixture_u32(fixture.person[index], 0x84u, 0u);
        fixture_u32(fixture.person[index], 0xE8u, 0u);
        fixture_u32(fixture.person[index], 0xF4u,
            pre.city_residence_pointer);
        officer->person_id = index;
        officer->person_pointer = fixture.person_address[index];
        officer->source_list_index = index;
        officer->effective_politics = 100u - index;
        officer->identity = 0u;
        officer->ready_flags = 0u;
        officer->residence_pointer = pre.city_residence_pointer;
    }
    check(san9_s5_current_context_business_digest(&pre, expected_digest)
        && san9_s5_frozen_post_capture_reader_ab(
            &reader, &identity, &pre, &first, &second)
                == SAN9_S5_CURRENT_CONTEXT_OK
        && first.controller_target == 0u
        && san9_p1_constant_time_equal(expected_digest,
            first.business_digest, sizeof(expected_digest)),
        "s5-v8-frozen-post-ab-happy-root-target-zero");
    fixture_u32(fixture.city, 0x1CCu, 101u);
    check(san9_s5_frozen_post_capture_reader(
            &reader, &identity, &pre, &first)
            == SAN9_S5_CURRENT_CONTEXT_BUSINESS_DRIFT,
        "s5-v8-frozen-post-commerce-drift-rejected");
    fixture_u32(fixture.city, 0x1CCu, 100u);
    fixture_u32(fixture.root, 0x10u, 0u);
    check(san9_s5_frozen_post_capture_reader(
            &reader, &identity, &pre, &first)
            == SAN9_S5_CURRENT_CONTEXT_FROZEN_POST_INVALID,
        "s5-v8-frozen-post-null-handler-exit-identity-rejected");
    fixture_u32(fixture.root, 0x10u, fixture.root_address);
    fixture_u32(fixture.person[0], 0xE8u, UINT32_C(0x1000));
    check(san9_s5_frozen_post_capture_reader(
            &reader, &identity, &pre, &first)
            == SAN9_S5_CURRENT_CONTEXT_BUSINESS_DRIFT,
        "s5-v8-frozen-post-ready-busy-drift-rejected");
}

static void test_s8_bound_current_city_policy(void)
{
    San9S5BoundCurrentCity bound;
    uint32_t normalized = UINT32_C(0xFFFFFFFF);
    memset(&bound, 0, sizeof(bound));
    bound.controller_pointer = UINT32_C(0x00100000);
    bound.city_pointer = UINT32_C(0x0124DB58) + UINT32_C(0x1F0);
    bound.corps_pointer = UINT32_C(0x01253C38) + UINT32_C(0xD4);
    check(san9_s5_bound_current_city_normalize(&bound,
            bound.controller_pointer, bound.corps_pointer, 0u, &normalized)
            == SAN9_S5_CURRENT_CONTEXT_OK
        && normalized == bound.city_pointer,
        "s8-bound-target-zero-normalized-to-frozen-city");
    normalized = 0u;
    check(san9_s5_bound_current_city_normalize(&bound,
            bound.controller_pointer, bound.corps_pointer,
            bound.city_pointer, &normalized) == SAN9_S5_CURRENT_CONTEXT_OK
        && normalized == bound.city_pointer,
        "s8-bound-same-city-target-accepted");
    normalized = UINT32_C(0xFFFFFFFF);
    check(san9_s5_bound_current_city_normalize(&bound,
            bound.controller_pointer, bound.corps_pointer,
            bound.city_pointer + UINT32_C(0x1F0), &normalized)
            == SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID
        && normalized == 0u,
        "s8-bound-foreign-nonzero-target-rejected");
    normalized = UINT32_C(0xFFFFFFFF);
    check(san9_s5_bound_current_city_normalize(&bound,
            bound.controller_pointer + 4u, bound.corps_pointer,
            0u, &normalized)
            == SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID
        && normalized == 0u,
        "s8-bound-controller-drift-rejected");
    normalized = UINT32_C(0xFFFFFFFF);
    check(san9_s5_bound_current_city_normalize(&bound,
            bound.controller_pointer, bound.corps_pointer + UINT32_C(0xD4),
            0u, &normalized)
            == SAN9_S5_CURRENT_CONTEXT_CURRENT_CITY_INVALID
        && normalized == 0u,
        "s8-bound-corps-drift-rejected");
}

typedef struct FakeRead {
    uint32_t last_address;
    size_t last_size;
    unsigned int calls;
} FakeRead;

static int fake_read(
    void *context,
    uint32_t address,
    uint8_t *output,
    size_t output_size)
{
    FakeRead *fake = (FakeRead *)context;
    size_t index;
    if (fake == NULL || output == NULL || output_size == 0u) {
        return 0;
    }
    fake->last_address = address;
    fake->last_size = output_size;
    ++fake->calls;
    for (index = 0u; index < output_size; ++index) {
        output[index] = (uint8_t)(address + index);
    }
    return 1;
}

static void check(int condition, const char *name)
{
    if (condition) {
        ++passed;
    } else {
        ++failed;
        fprintf(stderr, "FAIL %s\n", name);
    }
}

static void test_s5_modal_shadow_contract(void)
{
    uintptr_t original[SAN9_P1_M2B_DOMESTIC_MENU_VTABLE_SIZE
        / sizeof(uintptr_t)];
    uintptr_t shadow[SAN9_P1_M2B_DOMESTIC_MENU_VTABLE_SIZE
        / sizeof(uintptr_t)];
    atomic_uintptr_t object_vptr;
    uintptr_t expected;
    uintptr_t original_vptr = SAN9_P1_M2B_DOMESTIC_MENU_VPTR;
    uintptr_t shadow_vptr = UINT32_C(0x10102020);
    size_t index;
    int exact = 1;
    for (index = 0u; index < sizeof(original) / sizeof(original[0]); ++index) {
        original[index] = UINT32_C(0x00400000) + (uint32_t)index * 0x10u;
    }
    original[SAN9_P1_M2B_DOMESTIC_MENU_TICK_OFFSET / sizeof(uintptr_t)] =
        SAN9_P1_M2B_DOMESTIC_MENU_TICK_ORIGINAL;
    memcpy(shadow, original, sizeof(shadow));
    shadow[SAN9_P1_M2B_DOMESTIC_MENU_TICK_OFFSET / sizeof(uintptr_t)] =
        UINT32_C(0x10203040);
    for (index = 0u; index < sizeof(original) / sizeof(original[0]); ++index) {
        if (index == SAN9_P1_M2B_DOMESTIC_MENU_TICK_OFFSET
                / sizeof(uintptr_t)) {
            exact = exact && shadow[index] == UINT32_C(0x10203040);
        } else {
            exact = exact && shadow[index] == original[index];
        }
    }
    check(exact && sizeof(shadow) == 0x104u,
        "s5-modal-shadow-only-a4-differs");
    atomic_init(&object_vptr, shadow_vptr);
    expected = shadow_vptr;
    check(atomic_compare_exchange_strong_explicit(&object_vptr, &expected,
            original_vptr, memory_order_acq_rel, memory_order_acquire)
        && atomic_load_explicit(&object_vptr, memory_order_acquire)
            == original_vptr,
        "s5-modal-third-tick-restore-cas-once");
    atomic_store_explicit(&object_vptr, UINT32_C(0x30304040),
        memory_order_release);
    expected = shadow_vptr;
    check(!atomic_compare_exchange_strong_explicit(&object_vptr, &expected,
            original_vptr, memory_order_acq_rel, memory_order_acquire)
        && atomic_load_explicit(&object_vptr, memory_order_acquire)
            == UINT32_C(0x30304040),
        "s5-modal-restore-conflict-does-not-overwrite");
    check(!san9_p1_s5_modal_timeout_requires_restart(
            SAN9_P1_S5_MODAL_ARMED)
        && san9_p1_s5_modal_timeout_requires_restart(
            SAN9_P1_S5_MODAL_WAKE_CLAIMED)
        && san9_p1_s5_modal_timeout_requires_restart(
            SAN9_P1_S5_MODAL_SHADOW_ARMED),
        "s5-modal-timeout-wake-claimed-requires-restart");
    atomic_store_explicit(&object_vptr, original_vptr, memory_order_release);
    check(!san9_p1_s5_modal_pre_cas_gate_is_open(
            SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART,
            SAN9_P1_S5_MODAL_WAKE_CLAIMED)
        && !san9_p1_s5_modal_pre_cas_gate_is_open(
            SAN9_P1_M2B_BOOTSTRAP_READY,
            SAN9_P1_S5_MODAL_REJECTED)
        && atomic_load_explicit(&object_vptr, memory_order_acquire)
            == original_vptr,
        "s5-modal-pre-cas-concurrent-poison-zero-menu-write");
}

#if SAN9_S5_MODAL_OFFLINE_FAKE
static void test_s5_modal_assembly_abi(void)
{
    uint32_t saved_mxcsr = _mm_getcsr();
    uint32_t result;
    uint32_t observed_mxcsr;
    uint32_t sentinel = UINT32_C(0x13572468);
    modal_fake_sequence = 0u;
    modal_fake_original_count = 0u;
    modal_fake_after_count = 0u;
    modal_fake_failure = 0u;
    modal_fake_caller = 0u;
    modal_fake_original_self = NULL;
    modal_fake_after_self = NULL;
    modal_fake_original_mxcsr = (saved_mxcsr & ~UINT32_C(0x6000))
        | UINT32_C(0x2000);
    modal_fake_after_mxcsr = (saved_mxcsr & ~UINT32_C(0x6000))
        | UINT32_C(0x4000);
    result = san9_p1_s5_modal_tick_bridge(&sentinel, NULL);
    observed_mxcsr = _mm_getcsr();
    _mm_setcsr(saved_mxcsr);
    check(result == UINT32_C(0xA55AA55A)
        && modal_fake_sequence == 2u
        && modal_fake_original_count == 1u
        && modal_fake_after_count == 1u
        && modal_fake_failure == 0u
        && modal_fake_original_self == &sentinel
        && modal_fake_after_self == &sentinel
        && modal_fake_caller != 0u,
        "s5-modal-assembly-original-first-once-plain-return");
    check(observed_mxcsr == modal_fake_original_mxcsr,
        "s5-modal-assembly-restores-original-mxcsr");
}

static void test_s5_v8_menu_assembly_abi(void)
{
    uint32_t saved_mxcsr = _mm_getcsr();
    uint32_t result;
    uint32_t observed_mxcsr;
    uint32_t sentinel = UINT32_C(0x24681357);
    v8_fake_sequence = 0u;
    v8_fake_entry_count = 0u;
    v8_fake_original_count = 0u;
    v8_fake_after_count = 0u;
    v8_fake_failure = 0u;
    v8_fake_entry_token = UINT32_C(0x38565335);
    v8_fake_caller = 0u;
    v8_fake_entry_self = NULL;
    v8_fake_original_self = NULL;
    v8_fake_original_entry_mxcsr = saved_mxcsr;
    v8_fake_original_exit_mxcsr = (saved_mxcsr & ~UINT32_C(0x6000))
        | UINT32_C(0x2000);
    v8_fake_after_mxcsr = (saved_mxcsr & ~UINT32_C(0x6000))
        | UINT32_C(0x4000);
    result = san9_p1_s5_v8_menu_tick_bridge(&sentinel, NULL);
    observed_mxcsr = _mm_getcsr();
    _mm_setcsr(saved_mxcsr);
    check(result == UINT32_C(0x5AA55AA5)
        && v8_fake_sequence == 3u
        && v8_fake_entry_count == 1u
        && v8_fake_original_count == 1u
        && v8_fake_after_count == 1u
        && v8_fake_failure == 0u
        && v8_fake_entry_self == &sentinel
        && v8_fake_original_self == &sentinel
        && v8_fake_caller != 0u,
        "s5-v8-menu-entry-restore-original-once-no-self-post");
    check(observed_mxcsr == v8_fake_original_exit_mxcsr,
        "s5-v8-menu-restores-original-mxcsr");
}
#endif

static void test_s5_no_apply_contract(void)
{
    San9S5NoApplyRequest request;
    San9S5NoApplyRequest tampered;
    San9S5NoApplyRequest apply_request;
    San9S5NoApplyRequest cultivate_request;
    San9S5NoApplyRequest patrol_request;
    San9S5NoApplyRequest train_request;
    San9S5NoApplyRequest repair_request;
    San9S5NoApplyGate gate;
    San9S5NoApplyGate second_gate;
    San9S5NoApplyGate apply_gate;
    San9S5NoApplyGate cultivate_gate;
    San9S5NoApplyGate patrol_gate;
    San9S5NoApplyGate train_gate;
    San9S5NoApplyGate repair_gate;
    San9S5NoApplyMachine machine;
    San9S5NoApplyMachine rejected;
    San9S5NoApplyMachine post_event;
    San9S5NoApplyMachine apply_machine;
    San9S5NoApplyMachine cultivate_machine;
    San9S5NoApplyMachine patrol_machine;
    San9S5NoApplyMachine train_machine;
    San9S5NoApplyMachine repair_machine;
    San9P1S5NoApplyEvidence evidence;
    San9P1S5ModalProbeEvidence modal_evidence;
    San9S5CurrentContextSnapshot context_snapshot;
    San9P1S5MenuHandoff menu_handoff;
    atomic_uint handoff_state;
    uint32_t handoff_expected;
    atomic_uintptr_t v8_menu_vptr;
    uintptr_t v8_expected_vptr;
    atomic_int v8_execute_permit;
    atomic_uint terminal_state;
    atomic_uint bootstrap_state;
    atomic_uint bootstrap_result;
    uint32_t terminal_expected;
    San9S5RequestStatus request_status;
    uint32_t people[SAN9_S5_TOP5_COUNT] = { 7u, 3u, 11u, 2u, 19u };
    uint32_t invalid_people[SAN9_S5_TOP5_COUNT] = { 7u, 3u, 850u, 2u, 19u };
    uint8_t key[SAN9_S5_HMAC_KEY_SIZE];
    uint8_t nonce[SAN9_S5_NONCE_SIZE];
    uint8_t binding[SAN9_S5_DIGEST_SIZE];
    uint8_t precondition[SAN9_S5_DIGEST_SIZE];
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    uint8_t apply_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t cultivate_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t patrol_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t train_digest[SAN9_S5_DIGEST_SIZE];
    uint8_t repair_digest[SAN9_S5_DIGEST_SIZE];

    memset(&request, 0, sizeof(request));
    memset(&tampered, 0, sizeof(tampered));
    memset(&apply_request, 0, sizeof(apply_request));
    memset(&cultivate_request, 0, sizeof(cultivate_request));
    memset(&patrol_request, 0, sizeof(patrol_request));
    memset(&train_request, 0, sizeof(train_request));
    memset(&repair_request, 0, sizeof(repair_request));
    memset(&gate, 0, sizeof(gate));
    memset(&second_gate, 0, sizeof(second_gate));
    memset(&apply_gate, 0, sizeof(apply_gate));
    memset(&cultivate_gate, 0, sizeof(cultivate_gate));
    memset(&patrol_gate, 0, sizeof(patrol_gate));
    memset(&train_gate, 0, sizeof(train_gate));
    memset(&repair_gate, 0, sizeof(repair_gate));
    memset(&machine, 0, sizeof(machine));
    memset(&rejected, 0, sizeof(rejected));
    memset(&post_event, 0, sizeof(post_event));
    memset(&apply_machine, 0, sizeof(apply_machine));
    memset(&cultivate_machine, 0, sizeof(cultivate_machine));
    memset(&patrol_machine, 0, sizeof(patrol_machine));
    memset(&train_machine, 0, sizeof(train_machine));
    memset(&repair_machine, 0, sizeof(repair_machine));
    memset(&evidence, 0, sizeof(evidence));
    memset(&modal_evidence, 0, sizeof(modal_evidence));
    memset(&context_snapshot, 0xA5, sizeof(context_snapshot));
    memset(&menu_handoff, 0, sizeof(menu_handoff));
    memset(key, 0x5Au, sizeof(key));
    memset(nonce, 0xA5, sizeof(nonce));
    memset(binding, 0x11, sizeof(binding));
    memset(precondition, 0x22, sizeof(precondition));
    memset(digest, 0, sizeof(digest));
    memset(apply_digest, 0, sizeof(apply_digest));
    memset(cultivate_digest, 0, sizeof(cultivate_digest));
    memset(patrol_digest, 0, sizeof(patrol_digest));
    memset(train_digest, 0, sizeof(train_digest));
    memset(repair_digest, 0, sizeof(repair_digest));

    check(sizeof(San9S5NoApplyRequest) == 256u
        && sizeof(San9P1S5NoApplyEvidence) == 372u
        && sizeof(San9P1S5MenuHandoff) == 132u
        && sizeof(San9P1S5OperationStorage) == 2392u
        && SAN9_S5_APPLY_ONCE_AUTHORIZED == 0
        && SAN9_S6_CULTIVATE_APPLY_ONCE_AUTHORIZED == 0
        && SAN9_S6_PATROL_APPLY_ONCE_AUTHORIZED == 0
        && SAN9_S6_TRAIN_APPLY_ONCE_AUTHORIZED == 0
        && SAN9_S6_REPAIR_APPLY_ONCE_AUTHORIZED == 0
        && SAN9_S5_CONTAINS_GAME_FUNCTIONS == 0
        && SAN9_S5_CONTAINS_GAME_ADDRESSES == 0
        && SAN9_S5_CONTAINS_TARGET_WRITES == 0,
        "s5-no-apply-layout-and-capability-boundary");
    check(san9_s5_no_apply_request_initialize(&request, 46u, 1u, people,
            UINT64_C(1000), UINT64_C(4000), nonce, binding, precondition)
            == SAN9_S5_REQUEST_VALID
        && request.native_command_id == 1u
        && request.event_code == UINT32_C(0x2711),
        "s5-request-initialize-exact-commerce");
    check(san9_s5_no_apply_request_sign(&request, key, sizeof(key))
            == SAN9_S5_REQUEST_VALID
        && san9_s5_no_apply_request_verify(&request, key, sizeof(key),
            binding, UINT64_C(2000)) == SAN9_S5_REQUEST_VALID,
        "s5-request-sign-verify");
    check(san9_s5_no_apply_request_digest(&request, digest)
        && memcmp(digest, (const uint8_t[SAN9_S5_DIGEST_SIZE]){0},
            sizeof(digest)) != 0,
        "s5-request-domain-digest");
    check(san9_s5_apply_once_request_initialize(&apply_request,
            46u, 1u, people, UINT64_C(1000), UINT64_C(4000), nonce,
            binding, precondition) == SAN9_S5_REQUEST_VALID
        && apply_request.magic == SAN9_S5_APPLY_ONCE_MAGIC
        && apply_request.kind == SAN9_S5_REQUEST_KIND_APPLY_ONCE
        && apply_request.flags == (SAN9_S5_FLAG_CURRENT_CITY_ONLY
            | SAN9_S5_FLAG_APPLY_ONCE)
        && san9_s5_apply_once_request_sign(
            &apply_request, key, sizeof(key)) == SAN9_S5_REQUEST_VALID
        && san9_s5_apply_once_request_verify(&apply_request,
            key, sizeof(key), binding, UINT64_C(2000))
                == SAN9_S5_REQUEST_VALID
        && san9_s5_no_apply_request_verify(&apply_request,
            key, sizeof(key), binding, UINT64_C(2000))
                == SAN9_S5_REQUEST_SHAPE_INVALID
        && san9_s5_apply_once_request_digest(&apply_request, apply_digest)
        && memcmp(digest, apply_digest, sizeof(digest)) != 0,
        "s5-apply-once-request-domain-separated");
    check(san9_s5_no_apply_gate_initialize(&apply_gate, binding)
        && san9_s5_apply_once_gate_accept(&apply_gate, &apply_request,
            key, sizeof(key), UINT64_C(2000), &request_status)
                == SAN9_S5_GATE_ACCEPTED
        && san9_s5_no_apply_machine_initialize(&apply_machine)
        && san9_s5_no_apply_machine_publish(&apply_machine, &apply_request)
                == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_claim_authenticated(
            &apply_machine, &apply_gate, &apply_request)
                == SAN9_S5_MACHINE_OK,
        "s5-apply-once-authenticated-publish-claim");
    check(san9_s6_cultivate_apply_once_request_initialize(
            &cultivate_request, 46u, 1u, people, UINT64_C(1000),
            UINT64_C(4000), nonce, binding, precondition)
                == SAN9_S5_REQUEST_VALID
        && cultivate_request.magic == SAN9_S6_CULTIVATE_APPLY_ONCE_MAGIC
        && cultivate_request.kind
            == SAN9_S6_REQUEST_KIND_CULTIVATE_APPLY_ONCE
        && cultivate_request.flags == (SAN9_S5_FLAG_CURRENT_CITY_ONLY
            | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_CULTIVATE)
        && cultivate_request.native_command_id == SAN9_S6_CULTIVATE_NATIVE_ID
        && cultivate_request.event_code == SAN9_S6_CULTIVATE_EVENT_CODE
        && san9_s6_cultivate_apply_once_request_sign(
            &cultivate_request, key, sizeof(key)) == SAN9_S5_REQUEST_VALID
        && san9_s6_cultivate_apply_once_request_verify(&cultivate_request,
            key, sizeof(key), binding, UINT64_C(2000))
                == SAN9_S5_REQUEST_VALID
        && san9_s5_apply_once_request_verify(&cultivate_request,
            key, sizeof(key), binding, UINT64_C(2000))
                == SAN9_S5_REQUEST_SHAPE_INVALID
        && san9_s6_cultivate_apply_once_request_digest(
            &cultivate_request, cultivate_digest)
        && memcmp(digest, cultivate_digest, sizeof(digest)) != 0
        && memcmp(apply_digest, cultivate_digest, sizeof(apply_digest)) != 0,
        "s6-cultivate-request-exact-and-domain-separated");
    check(san9_s5_no_apply_gate_initialize(&cultivate_gate, binding)
        && san9_s6_cultivate_apply_once_gate_accept(&cultivate_gate,
            &cultivate_request, key, sizeof(key), UINT64_C(2000),
            &request_status) == SAN9_S5_GATE_ACCEPTED
        && san9_s5_no_apply_machine_initialize(&cultivate_machine)
        && san9_s5_no_apply_machine_publish(&cultivate_machine,
            &cultivate_request) == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_claim_authenticated(&cultivate_machine,
            &cultivate_gate, &cultivate_request) == SAN9_S5_MACHINE_OK,
        "s6-cultivate-authenticated-publish-claim");
    check(san9_s6_patrol_apply_once_request_initialize(&patrol_request,
            46u, 1u, people, UINT64_C(1000), UINT64_C(4000), nonce,
            binding, precondition) == SAN9_S5_REQUEST_VALID
        && patrol_request.magic == SAN9_S6_PATROL_APPLY_ONCE_MAGIC
        && patrol_request.kind == SAN9_S6_REQUEST_KIND_PATROL_APPLY_ONCE
        && patrol_request.flags == (SAN9_S5_FLAG_CURRENT_CITY_ONLY
            | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_PATROL)
        && patrol_request.native_command_id == SAN9_S6_PATROL_NATIVE_ID
        && patrol_request.event_code == SAN9_S6_PATROL_EVENT_CODE
        && san9_s6_patrol_apply_once_request_sign(&patrol_request,
            key, sizeof(key)) == SAN9_S5_REQUEST_VALID
        && san9_s6_patrol_apply_once_request_verify(&patrol_request,
            key, sizeof(key), binding, UINT64_C(2000))
                == SAN9_S5_REQUEST_VALID
        && san9_s5_apply_once_request_verify(&patrol_request,
            key, sizeof(key), binding, UINT64_C(2000))
                == SAN9_S5_REQUEST_SHAPE_INVALID
        && san9_s6_patrol_apply_once_request_digest(
            &patrol_request, patrol_digest)
        && memcmp(digest, patrol_digest, sizeof(digest)) != 0
        && memcmp(apply_digest, patrol_digest, sizeof(apply_digest)) != 0
        && memcmp(cultivate_digest, patrol_digest, sizeof(cultivate_digest))
            != 0,
        "s6-patrol-request-exact-and-domain-separated");
    check(san9_s5_no_apply_gate_initialize(&patrol_gate, binding)
        && san9_s6_patrol_apply_once_gate_accept(&patrol_gate,
            &patrol_request, key, sizeof(key), UINT64_C(2000),
            &request_status) == SAN9_S5_GATE_ACCEPTED
        && san9_s5_no_apply_machine_initialize(&patrol_machine)
        && san9_s5_no_apply_machine_publish(&patrol_machine,
            &patrol_request) == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_claim_authenticated(&patrol_machine,
            &patrol_gate, &patrol_request) == SAN9_S5_MACHINE_OK,
        "s6-patrol-authenticated-publish-claim");
    check(san9_s6_train_apply_once_request_initialize(&train_request,
            46u, 1u, people, UINT64_C(1000), UINT64_C(4000), nonce,
            binding, precondition) == SAN9_S5_REQUEST_VALID
        && train_request.magic == SAN9_S6_TRAIN_APPLY_ONCE_MAGIC
        && train_request.kind == SAN9_S6_REQUEST_KIND_TRAIN_APPLY_ONCE
        && train_request.flags == (SAN9_S5_FLAG_CURRENT_CITY_ONLY
            | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_TRAIN)
        && train_request.native_command_id == SAN9_S6_TRAIN_NATIVE_ID
        && train_request.event_code == SAN9_S6_TRAIN_EVENT_CODE
        && san9_s6_train_apply_once_request_sign(&train_request,
            key, sizeof(key)) == SAN9_S5_REQUEST_VALID
        && san9_s6_train_apply_once_request_verify(&train_request,
            key, sizeof(key), binding, UINT64_C(2000))
                == SAN9_S5_REQUEST_VALID
        && san9_s5_apply_once_request_verify(&train_request,
            key, sizeof(key), binding, UINT64_C(2000))
                == SAN9_S5_REQUEST_SHAPE_INVALID
        && san9_s6_train_apply_once_request_digest(
            &train_request, train_digest)
        && memcmp(patrol_digest, train_digest, sizeof(train_digest)) != 0,
        "s6-train-request-exact-and-domain-separated");
    check(san9_s5_no_apply_gate_initialize(&train_gate, binding)
        && san9_s6_train_apply_once_gate_accept(&train_gate,
            &train_request, key, sizeof(key), UINT64_C(2000),
            &request_status) == SAN9_S5_GATE_ACCEPTED
        && san9_s5_no_apply_machine_initialize(&train_machine)
        && san9_s5_no_apply_machine_publish(&train_machine,
            &train_request) == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_claim_authenticated(&train_machine,
            &train_gate, &train_request) == SAN9_S5_MACHINE_OK,
        "s6-train-authenticated-publish-claim");
    check(san9_s6_repair_apply_once_request_initialize(&repair_request,
            46u, 1u, people, UINT64_C(1000), UINT64_C(4000), nonce,
            binding, precondition) == SAN9_S5_REQUEST_VALID
        && repair_request.magic == SAN9_S6_REPAIR_APPLY_ONCE_MAGIC
        && repair_request.kind == SAN9_S6_REQUEST_KIND_REPAIR_APPLY_ONCE
        && repair_request.flags == (SAN9_S5_FLAG_CURRENT_CITY_ONLY
            | SAN9_S5_FLAG_APPLY_ONCE | SAN9_S6_FLAG_REPAIR)
        && repair_request.native_command_id == SAN9_S6_REPAIR_NATIVE_ID
        && repair_request.event_code == SAN9_S6_REPAIR_EVENT_CODE
        && san9_s6_repair_apply_once_request_sign(&repair_request,
            key, sizeof(key)) == SAN9_S5_REQUEST_VALID
        && san9_s6_repair_apply_once_request_verify(&repair_request,
            key, sizeof(key), binding, UINT64_C(2000))
                == SAN9_S5_REQUEST_VALID
        && san9_s5_apply_once_request_verify(&repair_request,
            key, sizeof(key), binding, UINT64_C(2000))
                == SAN9_S5_REQUEST_SHAPE_INVALID
        && san9_s6_repair_apply_once_request_digest(
            &repair_request, repair_digest)
        && memcmp(train_digest, repair_digest, sizeof(repair_digest)) != 0,
        "s6-repair-request-exact-and-domain-separated");
    check(san9_s5_no_apply_gate_initialize(&repair_gate, binding)
        && san9_s6_repair_apply_once_gate_accept(&repair_gate,
            &repair_request, key, sizeof(key), UINT64_C(2000),
            &request_status) == SAN9_S5_GATE_ACCEPTED
        && san9_s5_no_apply_machine_initialize(&repair_machine)
        && san9_s5_no_apply_machine_publish(&repair_machine,
            &repair_request) == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_claim_authenticated(&repair_machine,
            &repair_gate, &repair_request) == SAN9_S5_MACHINE_OK,
        "s6-repair-authenticated-publish-claim");
    tampered = request;
    tampered.event_code = UINT32_C(0x2712);
    check(san9_s5_no_apply_request_verify(&tampered, key, sizeof(key),
            binding, UINT64_C(2000)) == SAN9_S5_REQUEST_SHAPE_INVALID,
        "s5-wrong-native-event-rejected");
    tampered = request;
    tampered.reserved[0] = 1u;
    check(san9_s5_no_apply_request_verify(&tampered, key, sizeof(key),
            binding, UINT64_C(2000)) == SAN9_S5_REQUEST_RESERVED_NONZERO,
        "s5-reserved-nonzero-rejected");
    tampered = request;
    tampered.hmac[0] ^= 1u;
    check(san9_s5_no_apply_request_verify(&tampered, key, sizeof(key),
            binding, UINT64_C(2000)) == SAN9_S5_REQUEST_HMAC_MISMATCH,
        "s5-hmac-mutation-rejected");
    check(san9_s5_no_apply_request_verify(&request, key, sizeof(key),
            binding, UINT64_C(5000)) == SAN9_S5_REQUEST_EXPIRED,
        "s5-expired-rejected");
    check(san9_s5_no_apply_request_initialize(&tampered, 46u, 1u,
            invalid_people, UINT64_C(1000), UINT64_C(4000), nonce, binding,
            precondition) == SAN9_S5_REQUEST_SHAPE_INVALID,
        "s5-person-id-range-rejected");
    check(san9_s5_no_apply_request_sign(&request, request.hmac,
            sizeof(request.hmac)) == SAN9_S5_REQUEST_INVALID_ARGUMENT,
        "s5-key-request-alias-rejected");
    check(!san9_s5_no_apply_request_digest(&request,
            (uint8_t *)(void *)&request),
        "s5-digest-request-alias-rejected");

    check(san9_s5_no_apply_gate_initialize(&gate, binding)
        && san9_s5_no_apply_gate_accept(&gate, &request, key, sizeof(key),
            UINT64_C(2000), &request_status) == SAN9_S5_GATE_ACCEPTED
        && request_status == SAN9_S5_REQUEST_VALID
        && san9_s5_no_apply_gate_accept(&gate, &request, key, sizeof(key),
            UINT64_C(2100), &request_status) == SAN9_S5_GATE_REPLAY,
        "s5-gate-accepts-once");
    check(san9_s5_no_apply_gate_initialize(&second_gate, binding)
        && san9_s5_no_apply_gate_accept(&second_gate, &request, key,
            sizeof(key), UINT64_C(2000), &request_status)
                == SAN9_S5_GATE_ACCEPTED
        && san9_s5_no_apply_gate_accept(&second_gate, &request, key,
            sizeof(key), UINT64_C(1900), &request_status)
                == SAN9_S5_GATE_CLOCK_ROLLBACK
        && san9_s5_no_apply_gate_accept(&second_gate, &request, key,
            sizeof(key), UINT64_C(2200), &request_status)
                == SAN9_S5_GATE_CLOCK_FAULTED,
        "s5-clock-rollback-latches");

    check(san9_s5_no_apply_machine_initialize(&machine)
        && san9_s5_no_apply_machine_publish(&machine, &request)
            == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_claim_authenticated(&machine, &gate,
            &request) == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_mark_prechecked(&machine)
            == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_mark_event_attempted(&machine)
            == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_mark_shadow_armed(&machine)
            == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_mark_execute_entered(&machine)
            == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_mark_vptr_restored(&machine)
            == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_prove_no_apply(&machine)
            == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_state(&machine)
            == SAN9_S5_STATE_NO_APPLY_PROVEN
        && atomic_load_explicit(&machine.event_attempt_count,
            memory_order_acquire) == 1u
        && atomic_load_explicit(&machine.shadow_arm_count,
            memory_order_acquire) == 1u
        && atomic_load_explicit(&machine.execute_enter_count,
            memory_order_acquire) == 1u
        && atomic_load_explicit(&machine.restore_count,
            memory_order_acquire) == 1u
        && atomic_load_explicit(&machine.observed_apply_count,
            memory_order_acquire) == 0u,
        "s5-v8-happy-event1-handler1-execute1-restore1-apply0");
    atomic_init(&handoff_state, SAN9_P1_S5_MENU_ARMED);
    handoff_expected = SAN9_P1_S5_MENU_ARMED;
    check(atomic_compare_exchange_strong_explicit(&handoff_state,
            &handoff_expected, SAN9_P1_S5_MENU_WAKE_CLAIMED,
            memory_order_acq_rel, memory_order_acquire)
        && san9_p1_s5_menu_timeout_requires_restart(
            SAN9_P1_S5_MENU_ARMED)
        && san9_p1_s5_menu_timeout_requires_restart(
            (int32_t)atomic_load_explicit(
                &handoff_state, memory_order_acquire)),
        "s5-v8-wake-claim-timeout-restart-boundary");
    handoff_expected = SAN9_P1_S5_MENU_ARMED;
    check(!atomic_compare_exchange_strong_explicit(&handoff_state,
            &handoff_expected, SAN9_P1_S5_MENU_WAKE_CLAIMED,
            memory_order_acq_rel, memory_order_acquire)
        && atomic_load_explicit(&handoff_state, memory_order_acquire)
            == SAN9_P1_S5_MENU_WAKE_CLAIMED,
        "s5-v8-duplicate-wake-cannot-reclaim");
    atomic_init(&v8_menu_vptr, UINT32_C(0x20203030));
    v8_expected_vptr = UINT32_C(0x10102020);
    check(!atomic_compare_exchange_strong_explicit(&v8_menu_vptr,
            &v8_expected_vptr, SAN9_P1_M2B_DOMESTIC_MENU_VPTR,
            memory_order_acq_rel, memory_order_acquire)
        && atomic_load_explicit(&v8_menu_vptr, memory_order_acquire)
            == UINT32_C(0x20203030),
        "s5-v8-menu-restore-cas-conflict-no-overwrite");
    atomic_init(&v8_execute_permit, 1);
    check(atomic_exchange_explicit(&v8_execute_permit, 0,
            memory_order_acq_rel) == 1
        && atomic_exchange_explicit(&v8_execute_permit, 0,
            memory_order_acq_rel) == 0,
        "s5-v8-handler-permit-consumed-once");
    memset(menu_handoff.arm_easy_digest, 0x11,
        sizeof(menu_handoff.arm_easy_digest));
    memset(menu_handoff.entry_easy_digest, 0x11,
        sizeof(menu_handoff.entry_easy_digest));
    menu_handoff.entry_easy_digest[0] ^= 1u;
    check(!san9_p1_constant_time_equal(menu_handoff.arm_easy_digest,
            menu_handoff.entry_easy_digest, SAN9_P1_DIGEST_SIZE),
        "s5-v8-menu-easy-drift-rejected");
    atomic_init(&terminal_state, SAN9_P1_S5_TERMINAL_START_PRE_EVENT);
    terminal_expected = SAN9_P1_S5_TERMINAL_START_PRE_EVENT;
    check(atomic_compare_exchange_strong_explicit(&terminal_state,
            &terminal_expected, SAN9_P1_S5_TERMINAL_TIMEOUT,
            memory_order_acq_rel, memory_order_acquire)
        && (terminal_expected = SAN9_P1_S5_TERMINAL_START_PRE_EVENT,
            !atomic_compare_exchange_strong_explicit(&terminal_state,
                &terminal_expected, SAN9_P1_S5_TERMINAL_EVENT_INFLIGHT,
                memory_order_acq_rel, memory_order_acquire))
        && atomic_load_explicit(&terminal_state, memory_order_acquire)
            == SAN9_P1_S5_TERMINAL_TIMEOUT,
        "s5-v8-suspended-start-controller-timeout-wins");
    atomic_init(&terminal_state, SAN9_P1_S5_TERMINAL_START_PRE_EVENT);
    terminal_expected = SAN9_P1_S5_TERMINAL_START_PRE_EVENT;
    check(atomic_compare_exchange_strong_explicit(&terminal_state,
            &terminal_expected, SAN9_P1_S5_TERMINAL_EVENT_INFLIGHT,
            memory_order_acq_rel, memory_order_acquire)
        && (terminal_expected = SAN9_P1_S5_TERMINAL_START_PRE_EVENT,
            !atomic_compare_exchange_strong_explicit(&terminal_state,
                &terminal_expected, SAN9_P1_S5_TERMINAL_TIMEOUT,
                memory_order_acq_rel, memory_order_acquire))
        && (terminal_expected = SAN9_P1_S5_TERMINAL_EVENT_INFLIGHT,
            atomic_compare_exchange_strong_explicit(&terminal_state,
                &terminal_expected, SAN9_P1_S5_TERMINAL_RESTART,
                memory_order_acq_rel, memory_order_acquire)),
        "s5-v8-event-inflight-arms-then-restarts-after-timeout");
    atomic_init(&terminal_state, SAN9_P1_S5_TERMINAL_FINISH_VERIFY);
    terminal_expected = SAN9_P1_S5_TERMINAL_FINISH_VERIFY;
    check(atomic_compare_exchange_strong_explicit(&terminal_state,
            &terminal_expected, SAN9_P1_S5_TERMINAL_TIMEOUT,
            memory_order_acq_rel, memory_order_acquire)
        && (terminal_expected = SAN9_P1_S5_TERMINAL_FINISH_VERIFY,
            !atomic_compare_exchange_strong_explicit(&terminal_state,
                &terminal_expected, SAN9_P1_S5_TERMINAL_FINISH_COMMIT,
                memory_order_acq_rel, memory_order_acquire)),
        "s5-v8-suspended-finish-controller-timeout-wins");
    atomic_init(&terminal_state, SAN9_P1_S5_TERMINAL_FINISH_VERIFY);
    terminal_expected = SAN9_P1_S5_TERMINAL_FINISH_VERIFY;
    check(atomic_compare_exchange_strong_explicit(&terminal_state,
            &terminal_expected, SAN9_P1_S5_TERMINAL_FINISH_COMMIT,
            memory_order_acq_rel, memory_order_acquire)
        && (terminal_expected = SAN9_P1_S5_TERMINAL_FINISH_VERIFY,
            !atomic_compare_exchange_strong_explicit(&terminal_state,
                &terminal_expected, SAN9_P1_S5_TERMINAL_TIMEOUT,
                memory_order_acq_rel, memory_order_acquire))
        && (terminal_expected = SAN9_P1_S5_TERMINAL_FINISH_COMMIT,
            atomic_compare_exchange_strong_explicit(&terminal_state,
                &terminal_expected, SAN9_P1_S5_TERMINAL_SUCCESS,
                memory_order_acq_rel, memory_order_acquire)),
        "s5-v8-finish-commit-success-cannot-be-overwritten");
    check(san9_p1_s5_deadline_is_fresh(UINT64_C(1249), UINT64_C(1250))
        && !san9_p1_s5_deadline_is_fresh(UINT64_C(1250), UINT64_C(1250))
        && UINT64_C(1250) - UINT64_C(1000) <
            SAN9_S5_MAXIMUM_LIFETIME_MS,
        "s5-v8-config-sub-five-second-deadline-is-binding");
    atomic_init(&bootstrap_state,
        SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART);
    atomic_init(&bootstrap_result, SAN9_P1_M2B_RESTART_REQUIRED);
    terminal_expected = SAN9_P1_M2B_BOOTSTRAP_READY;
    check(!atomic_compare_exchange_strong_explicit(&bootstrap_state,
            &terminal_expected, SAN9_P1_M2B_BOOTSTRAP_REJECTED,
            memory_order_acq_rel, memory_order_acquire)
        && atomic_load_explicit(&bootstrap_state, memory_order_acquire)
            == SAN9_P1_M2B_BOOTSTRAP_POISONED_RESTART
        && atomic_load_explicit(&bootstrap_result, memory_order_acquire)
            == SAN9_P1_M2B_RESTART_REQUIRED,
        "s5-v8-poisoned-bootstrap-cannot-downgrade-to-rejected");
    check(san9_s5_no_apply_machine_initialize(&rejected)
        && san9_s5_no_apply_machine_mark_shadow_armed(&rejected)
            == SAN9_S5_MACHINE_REJECTED_PRE_EVENT
        && san9_s5_no_apply_machine_state(&rejected)
            == SAN9_S5_STATE_REJECTED_PRE_EVENT,
        "s5-out-of-order-before-event-rejects-safely");
    check(san9_s5_no_apply_machine_initialize(&post_event)
        && san9_s5_no_apply_machine_publish(&post_event, &request)
            == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_claim_authenticated(&post_event, &gate,
            &request) == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_mark_prechecked(&post_event)
            == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_mark_event_attempted(&post_event)
            == SAN9_S5_MACHINE_OK
        && san9_s5_no_apply_machine_fail(&post_event, SAN9_S5_FAULT_SHADOW)
            == SAN9_S5_MACHINE_RESTART_REQUIRED
        && san9_s5_no_apply_machine_state(&post_event)
            == SAN9_S5_STATE_RESTART_REQUIRED,
        "s5-any-post-event-fault-requires-restart");
    evidence.magic = SAN9_P1_S5_EVIDENCE_MAGIC;
    evidence.structure_size = sizeof(evidence);
    evidence.machine_state = SAN9_S5_STATE_NO_APPLY_PROVEN;
    check(san9_p1_s5_evidence_sign(&evidence, key, sizeof(key))
        && san9_p1_s5_evidence_verify(&evidence, key, sizeof(key)),
        "s5-evidence-hmac-roundtrip");
    evidence.observed_apply_count = 1u;
    check(!san9_p1_s5_evidence_verify(&evidence, key, sizeof(key)),
        "s5-evidence-mutation-rejected");
    check(san9_s5_current_context_capture_reader(NULL, NULL,
            &context_snapshot) == SAN9_S5_CURRENT_CONTEXT_INVALID_ARGUMENT
        && memcmp(&context_snapshot,
            &(San9S5CurrentContextSnapshot){0}, sizeof(context_snapshot)) == 0,
        "s5-context-invalid-clears-output");
    modal_evidence.magic = SAN9_P1_S5_MODAL_EVIDENCE_MAGIC;
    modal_evidence.schema_major = SAN9_P1_M2B_SCHEMA_MAJOR;
    modal_evidence.schema_minor = SAN9_P1_M2B_SCHEMA_MINOR;
    modal_evidence.structure_size = sizeof(modal_evidence);
    modal_evidence.state = SAN9_P1_S5_MODAL_COMPLETE;
    modal_evidence.wake_post_count = 1u;
    modal_evidence.hook_entry_count = 1u;
    modal_evidence.exact_wake_count = 1u;
    modal_evidence.wake_claim_count = 1u;
    modal_evidence.hook_depth = 1u;
    modal_evidence.remove_flag = 1u;
    modal_evidence.message = UINT32_C(0xC001);
    modal_evidence.atom = 7u;
    modal_evidence.challenge = UINT32_C(0x12345678);
    modal_evidence.callnext_stable = 1u;
    modal_evidence.shadow_arm_count = 1u;
    modal_evidence.wrapper_enter_count = SAN9_P1_M2B_S5_MODAL_PROBE_TICKS;
    modal_evidence.original_return_count = SAN9_P1_M2B_S5_MODAL_PROBE_TICKS;
    modal_evidence.restore_count = 1u;
    modal_evidence.first_tid = 91u;
    modal_evidence.last_tid = 91u;
    modal_evidence.last_caller = SAN9_P1_M2B_DOMESTIC_MENU_TICK_CALLER;
    modal_evidence.top_hwnd = UINT32_C(0x00002468);
    modal_evidence.menu_vptr = SAN9_P1_M2B_DOMESTIC_MENU_VPTR;
    modal_evidence.modal_flags = 0x18u;
    modal_evidence.easy_stable_count = SAN9_P1_M2B_S5_MODAL_PROBE_TICKS;
    modal_evidence.menu_pointer = UINT32_C(0x10101010);
    memset(modal_evidence.arm_easy_digest, 0x3Cu,
        sizeof(modal_evidence.arm_easy_digest));
    memcpy(modal_evidence.last_easy_digest, modal_evidence.arm_easy_digest,
        sizeof(modal_evidence.last_easy_digest));
    check(sizeof(San9P1S5ModalProbeEvidence) == 256u
        && sizeof(San9P1S5ModalProbeOperationStorage) == 2392u
        && SAN9_P1_M2B_S5_MODAL_PROBE_PROOF_FROZEN == 0u
        && SAN9_P1_M2B_S5_MODAL_WAKE_AUTHORIZATION == 0u
        && SAN9_P1_M2B_S5_MODAL_VTABLE_BUILD == 1u
        && SAN9_P1_M2B_DOMESTIC_MENU_VTABLE_SIZE == 0x104u
        && SAN9_P1_M2B_DOMESTIC_MENU_TICK_OFFSET == 0xA4u,
        "s5-modal-probe-frozen-vtable-v7-boundary");
    check(san9_p1_s5_modal_evidence_is_vtable_exact(&modal_evidence,
            91u, UINT32_C(0xC001), 7u, UINT32_C(0x12345678)),
        "s5-modal-vtable-null-hwnd-exact-shape");
    modal_evidence.message_hwnd = 1u;
    check(!san9_p1_s5_modal_evidence_is_vtable_exact(&modal_evidence,
            91u, UINT32_C(0xC001), 7u, UINT32_C(0x12345678)),
        "s5-modal-probe-nonnull-hwnd-rejected");
    modal_evidence.message_hwnd = 0u;
    modal_evidence.wrapper_enter_count = 4u;
    check(!san9_p1_s5_modal_evidence_is_vtable_exact(&modal_evidence,
            91u, UINT32_C(0xC001), 7u, UINT32_C(0x12345678)),
        "s5-modal-fourth-tick-rejected");
    modal_evidence.wrapper_enter_count = SAN9_P1_M2B_S5_MODAL_PROBE_TICKS;
    modal_evidence.last_caller ^= 1u;
    check(!san9_p1_s5_modal_evidence_is_vtable_exact(&modal_evidence,
            91u, UINT32_C(0xC001), 7u, UINT32_C(0x12345678)),
        "s5-modal-wrong-caller-rejected");
    modal_evidence.last_caller = SAN9_P1_M2B_DOMESTIC_MENU_TICK_CALLER;
    modal_evidence.last_easy_digest[0] ^= 1u;
    check(!san9_p1_s5_modal_evidence_is_vtable_exact(&modal_evidence,
            91u, UINT32_C(0xC001), 7u, UINT32_C(0x12345678)),
        "s5-modal-easy-drift-rejected");
    modal_evidence.last_easy_digest[0] ^= 1u;
    modal_evidence.restore_count = 0u;
    check(!san9_p1_s5_modal_evidence_is_vtable_exact(&modal_evidence,
            91u, UINT32_C(0xC001), 7u, UINT32_C(0x12345678)),
        "s5-modal-restore-conflict-rejected");
    modal_evidence.restore_count = 1u;
    check(SAN9_P1_M2B_S5_MODAL_ROOT_EVENT_CALLS == 0u
        && SAN9_P1_M2B_S5_MODAL_PUBLISH_CALLS == 0u
        && SAN9_P1_M2B_S5_MODAL_APPLY_CALLS == 0u
        && modal_evidence.controller_ack == 0u,
        "s5-modal-zero-business-matrix");
    check(san9_p1_s5_modal_evidence_sign(&modal_evidence, key, sizeof(key))
        && san9_p1_s5_modal_evidence_verify(
            &modal_evidence, key, sizeof(key)),
        "s5-modal-probe-evidence-hmac-roundtrip");
    modal_evidence.shadow_arm_count = 2u;
    check(!san9_p1_s5_modal_evidence_verify(
            &modal_evidence, key, sizeof(key)),
        "s5-modal-probe-shadow-mutation-rejected");
}

int main(void)
{
    San9P1M2Mailbox mailbox;
    San9P1M2RuntimeStorage runtime;
    San9P1S3ReadContext read_context;
    FakeRead fake;
    uint8_t read_output[SAN9_P1_S3_READ_MAX];
    size_t read_size = 99u;
    uint32_t index;
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    memset(key, 0x5Au, sizeof(key));
    memset(&read_context, 0, sizeof(read_context));
    memset(&fake, 0, sizeof(fake));
    memset(read_output, 0xA5, sizeof(read_output));
    test_s5_modal_shadow_contract();
#if SAN9_S5_MODAL_OFFLINE_FAKE
    test_s5_modal_assembly_abi();
    test_s5_v8_menu_assembly_abi();
#endif
    test_s5_v8_frozen_post();
    test_s8_bound_current_city_policy();
    test_s5_no_apply_contract();
    check(sizeof(San9P1M2bShared) == 8192u, "shared-size");
    check(offsetof(San9P1M2bShared, mailbox) == 4096u, "mailbox-offset");
    check(sizeof(mailbox) == 4096u, "frozen-mailbox-size");
    check(SAN9_P1_M2B_LIVE_EXECUTION_DEFAULT == 0u, "default-no-live");
    check(SAN9_P1_M2B_UI_CONNECTED == 0u, "ui-disconnected");
    check(SAN9_P1_M2B_COMMERCE_ABI_READY == 1u
        && SAN9_P1_M2B_COMMERCE_LIVE_AUTHORIZATION == 0u,
        "commerce-abi-frozen-live-no-go");
    check(SAN9_P1_M2B_HAS_FIVE_DOMESTIC_COMMANDS == 0u, "no-five-items");
    check(SAN9_P1_M2B_HAS_ALL_CITY_LOOP == 0u, "no-city-loop");
    check(SAN9_P1_M2B_HAS_HOT_UNLOAD == 0u, "no-hot-unload");
    check(SAN9_P1_M2B_OPERATION_NONE == 0u
        && SAN9_P1_M2B_OPERATION_PROBE0 == 1u
        && SAN9_P1_M2B_OPERATION_PING == 2u
        && SAN9_P1_M2B_OPERATION_OBSERVE == 3u
        && SAN9_P1_M2B_OPERATION_S5_NO_APPLY == 4u
        && SAN9_P1_M2B_OPERATION_S5_MODAL_PROBE == 5u
        && SAN9_P1_M2B_PROBE0_MIN_TICKS == 3u,
        "probe0-operation-contract");
    check(strcmp(SAN9_P1_M2B_CONFIRM_WORD,
        "I_ACCEPT_PROBE0_PINNED_UNTIL_GAME_RESTART") == 0,
        "probe0-confirm-word-exact");
    check(strcmp(SAN9_P1_M2B_PING_CONFIRM_WORD,
        "I_ACCEPT_PING_ONLY_PINNED_UNTIL_GAME_RESTART") == 0,
        "ping-confirm-word-exact");
    check(strcmp(SAN9_P1_M2B_OBSERVE_CONFIRM_WORD,
        "I_ACCEPT_S3_READONLY_OBSERVATION_PINNED_UNTIL_GAME_RESTART") == 0,
        "observe-confirm-word-exact");
    check(sizeof(San9P1M2bPingEvidence) == 80u
        && SAN9_P1_M2B_PING_COUNT == 100u,
        "ping-evidence-contract");
    check(offsetof(San9P1M2bShared, probe0_idle_count)
            < SAN9_P1_M2B_BOOTSTRAP_SIZE
        && offsetof(San9P1M2bShared, operation)
            < SAN9_P1_M2B_BOOTSTRAP_SIZE,
        "probe0-counters-inside-bootstrap-page");
    check(sizeof(San9P1S3TraceRecord) == 232u
        && sizeof(San9P1S3TraceArea) == 2384u
        && offsetof(San9P1M2bShared, operation)
            + sizeof(San9P1M2bOperationStorage)
                <= SAN9_P1_M2B_BOOTSTRAP_SIZE,
        "s3-trace-layout");
    check(S3_READONLY_BUILD == 1
        && SAN9_P1_S3_READONLY_MARKER == UINT32_C(0x524F5333)
        && SAN9_P1_S3_READ_MAX == 256u
        && san9_p1_s3_read_dispatch_count() == SAN9_P1_S3_READ_OPCODE_COUNT
        && san9_p1_s3_write_dispatch_count() == 0u,
        "s3-readonly-dispatch-contract");
    for (index = 0u; index < san9_p1_s3_read_dispatch_count(); ++index) {
        const San9P1S3ReadDescriptor *descriptor =
            san9_p1_s3_read_descriptor(index);
        check(descriptor != NULL
            && descriptor->symbol < SAN9_P1_S3_SYMBOL_COUNT
            && descriptor->length > 0u
            && descriptor->length <= SAN9_P1_S3_READ_MAX
            && descriptor->readonly_marker == UINT16_C(0x5333),
            "s3-each-dispatch-is-bounded-readonly");
    }
    read_context.read = fake_read;
    read_context.read_context = &fake;
    for (index = 0u; index < SAN9_P1_S3_SYMBOL_COUNT; ++index) {
        read_context.bases[index] = UINT32_C(0x00100000) + index * 0x1000u;
    }
    check(san9_p1_s3_read_index(&read_context,
            SAN9_P1_S3_READ_HANDLER_PREFIX, read_output,
            sizeof(read_output), &read_size)
        && read_size == 0x70u
        && fake.calls == 1u
        && fake.last_address == read_context.bases[SAN9_P1_S3_SYMBOL_HANDLER]
        && fake.last_size == 0x70u,
        "s3-index-resolves-compiled-tuple");
    read_size = 99u;
    check(!san9_p1_s3_read_index(&read_context,
            SAN9_P1_S3_READ_OPCODE_COUNT, read_output,
            sizeof(read_output), &read_size)
        && read_size == 0u && fake.calls == 1u,
        "s3-unknown-index-zero-output");
    read_size = 99u;
    check(!san9_p1_s3_read_index(&read_context,
            SAN9_P1_S3_READ_HANDLER_PREFIX, read_output, 0x6Fu, &read_size)
        && read_size == 0u && fake.calls == 1u,
        "s3-short-output-rejected-before-read");
    check(SAN9_P1_M2B_EXACT_IDLE_SLOT == UINT32_C(0x00604DF4)
        && SAN9_P1_M2B_EXACT_ORIGINAL_IDLE == UINT32_C(0x00434100)
        && SAN9_P1_M2B_EXACT_IDLE_CALLER == UINT32_C(0x005C5D11),
        "exact-idle-contract");
    check(san9_p1_m2b_canonical_easy_page_protection(
            UINT32_C(0x0061C000), UINT32_C(0x0061C000), UINT32_C(0x2F000),
            UINT32_C(0x00400000), UINT32_C(0x80), UINT32_C(0x1000),
            UINT32_C(0x01000000), UINT32_C(0x08)) == UINT32_C(0x04),
        "exact-image-writecopy-canonicalized");
    check(san9_p1_m2b_canonical_easy_page_protection(
            UINT32_C(0x0061C001), UINT32_C(0x0061C000), UINT32_C(0x2F000),
            UINT32_C(0x00400000), UINT32_C(0x80), UINT32_C(0x1000),
            UINT32_C(0x01000000), UINT32_C(0x08)) == UINT32_C(0x08),
        "wrong-address-not-canonicalized");
    check(san9_p1_m2b_canonical_easy_page_protection(
            UINT32_C(0x0061C000), UINT32_C(0x0061C000), UINT32_C(0x2F000),
            UINT32_C(0x00500000), UINT32_C(0x80), UINT32_C(0x1000),
            UINT32_C(0x01000000), UINT32_C(0x08)) == UINT32_C(0x08),
        "wrong-allocation-not-canonicalized");
    check(san9_p1_m2b_canonical_easy_page_protection(
            UINT32_C(0x0061C000), UINT32_C(0x0061C000), UINT32_C(0x2F000),
            UINT32_C(0x00400000), UINT32_C(0x40), UINT32_C(0x1000),
            UINT32_C(0x01000000), UINT32_C(0x08)) == UINT32_C(0x08),
        "wrong-allocation-protection-not-canonicalized");
    check(san9_p1_m2b_canonical_easy_page_protection(
            UINT32_C(0x0061C000), UINT32_C(0x0061C000), UINT32_C(0x2F000),
            UINT32_C(0x00400000), UINT32_C(0x80), UINT32_C(0x1000),
            UINT32_C(0x00020000), UINT32_C(0x08)) == UINT32_C(0x08),
        "private-page-not-canonicalized");
    check(san9_p1_m2b_canonical_easy_page_protection(
            UINT32_C(0x0061C000), UINT32_C(0x0061C000), UINT32_C(0x800),
            UINT32_C(0x00400000), UINT32_C(0x80), UINT32_C(0x1000),
            UINT32_C(0x01000000), UINT32_C(0x08)) == UINT32_C(0x08),
        "short-region-not-canonicalized");
    check(SAN9_P1_M2B_COMMERCE_HANDLER == UINT32_C(0x004C61F0)
        && SAN9_P1_M2B_COMMERCE_CAN_EXECUTE == UINT32_C(0x004C6400)
        && SAN9_P1_M2B_COPY_FIRST_N == UINT32_C(0x0046EF80)
        && SAN9_P1_M2B_COMMAND_ATTACH == UINT32_C(0x0047E6F0)
        && SAN9_P1_M2B_COMMAND_ALLOCATION_SIZE == 0x40u
        && SAN9_P1_M2B_SELECTION_LIMIT == 5u,
        "commerce-placeholder-candidates");
    check(SAN9_P1_M2B_COMMERCE_NATIVE_ID == 1u
        && SAN9_P1_M2B_COMMERCE_EVENT_ID == UINT32_C(0x2711)
        && SAN9_P1_M2B_HANDLER_DRIVER == UINT32_C(0x0050EAB0)
        && SAN9_P1_M2B_COMMERCE_NATIVE_APPLY == UINT32_C(0x0048B5D0)
        && SAN9_P1_M2B_ROOT_HANDLER_OFFSET == 0x10u
        && SAN9_P1_M2B_HANDLER_SELECTION_OFFSET == 0x40u
        && SAN9_P1_M2B_COMMERCE_VTABLE_SLOT_COUNT == 12u
        && SAN9_P1_M2B_COMMERCE_VTABLE_SIZE == 0x30u
        && SAN9_P1_M2B_COMMERCE_EXECUTE_SLOT_OFFSET == 0x28u
        && SAN9_P1_M2B_COMMAND_VTABLE_SLOT_COUNT == 22u
        && SAN9_P1_M2B_COMMAND_VTABLE_SIZE == 0x58u
        && SAN9_P1_M2B_COMMAND_APPLY_SLOT_OFFSET == 0x30u
        && SAN9_P1_M2B_PERSON_LIST_SIZE == 0x20u
        && SAN9_P1_M2B_COMMERCE_COMMAND_CONSTRUCTION_READY == 0u,
        "commerce-shadow-contract");
    {
        static const uint32_t handler_vtable[12] = {
            UINT32_C(0x004C63E0), UINT32_C(0x0059A850),
            UINT32_C(0x0047E4C0), UINT32_C(0x004C5280),
            UINT32_C(0x0050E9D0), UINT32_C(0x005B6930),
            UINT32_C(0x00401290), UINT32_C(0x005B6930),
            UINT32_C(0x004C6400), UINT32_C(0x004C62D0),
            UINT32_C(0x004C6310), UINT32_C(0x0050E9E0)
        };
        static const uint32_t command_vtable[22] = {
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
        };
        check(sizeof(handler_vtable) == SAN9_P1_M2B_COMMERCE_VTABLE_SIZE
            && handler_vtable[10] == UINT32_C(0x004C6310)
            && sizeof(command_vtable) == SAN9_P1_M2B_COMMAND_VTABLE_SIZE
            && command_vtable[SAN9_P1_M2B_COMMAND_APPLY_SLOT_OFFSET / 4u]
                == SAN9_P1_M2B_COMMERCE_NATIVE_APPLY,
            "commerce-vtable-independent-oracle");
    }
    check(san9_p1_m2_mailbox_initialize(&mailbox, key, sizeof(key))
        && san9_p1_m2_mailbox_validate(&mailbox), "mailbox-init");
    check(san9_p1_m2_runtime_initialize(&runtime, &mailbox)
        && san9_p1_m2_lifecycle_load(&runtime) == SAN9_P1_M2_LIFECYCLE_EMPTY,
        "runtime-init");
    printf("P1_M2B_OFFLINE_SELFTEST passed=%u failed=%u live_runs=0\n",
        passed, failed);
    return failed == 0u ? 0 : 1;
}
