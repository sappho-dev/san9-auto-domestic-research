#include "san9_p1_m2.h"
#include "san9_p1_easy_manifest.gen.h"

#include <stdio.h>
#include <string.h>

#define TEST_OWNER_TOKEN UINT64_C(0x8877665544332211)

static unsigned int checks;
static unsigned int failures;

static void check(int condition, const char *name)
{
    ++checks;
    if (!condition) {
        ++failures;
        fprintf(stderr, "FAIL %s\n", name);
    }
}

static void check_indexed(int condition, const char *prefix, unsigned int index)
{
    char name[128];
    (void)snprintf(name, sizeof(name), "%s%u", prefix, index);
    check(condition, name);
}

static int all_zero(const void *value, size_t size)
{
    const uint8_t *bytes = (const uint8_t *)value;
    size_t index;
    for (index = 0u; index < size; ++index) {
        if (bytes[index] != 0u) {
            return 0;
        }
    }
    return 1;
}

static void fill_bytes(uint8_t *output, size_t size, uint8_t seed)
{
    size_t index;
    for (index = 0u; index < size; ++index) {
        output[index] = (uint8_t)(seed + (uint8_t)index);
        if (output[index] == 0u) {
            output[index] = 0xA5u;
        }
    }
}

static void write_u32_le(uint8_t output[4], uint32_t value)
{
    output[0] = (uint8_t)value;
    output[1] = (uint8_t)(value >> 8);
    output[2] = (uint8_t)(value >> 16);
    output[3] = (uint8_t)(value >> 24);
}

static San9P1EasyBinding make_easy_binding(void)
{
    San9P1EasyBinding binding;
    binding.game_image_base = SAN9_P1_EASY_GAME_IMAGE_BASE;
    binding.game_image_size = SAN9_P1_EASY_GAME_IMAGE_SIZE;
    binding.easy_image_base = SAN9_P1_EASY_PREFERRED_IMAGE_BASE;
    binding.easy_image_size = SAN9_P1_EASY_IMAGE_SIZE;
    binding.game_hwnd = UINT32_C(0x1234ABCD);
    return binding;
}

static void make_installed_redirect(
    const San9P1EasyBinding *binding,
    uint32_t index,
    uint8_t output[SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY])
{
    const San9P1EasyGeneratedRedirect *definition =
        &san9_p1_easy_generated_redirects[index];
    uint32_t source = binding->game_image_base + definition->game_rva;
    uint32_t target = binding->easy_image_base + definition->easy_target_rva;
    uint32_t displacement = target - (source + UINT32_C(5));
    memset(output, 0, SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY);
    output[0] = definition->opcode;
    write_u32_le(output + 1, displacement);
    if (definition->trailing_length != 0u) {
        output[5] = definition->trailing[0];
    }
}

static San9P1EasySnapshot make_easy_snapshot(
    const San9P1EasyBinding *binding,
    int installed)
{
    San9P1EasySnapshot snapshot;
    uint32_t index;
    memset(&snapshot, 0, sizeof(snapshot));
    snapshot.schema = SAN9_P1_EASY_SNAPSHOT_SCHEMA;
    snapshot.structure_size = (uint32_t)sizeof(snapshot);
    snapshot.captured_points = SAN9_P1_EASY_ALL_POINTS_MASK;
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_REDIRECT_COUNT; ++index) {
        if (installed) {
            make_installed_redirect(binding, index, snapshot.redirect_bytes[index]);
        } else {
            memcpy(snapshot.redirect_bytes[index],
                san9_p1_easy_generated_redirects[index].original,
                SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY);
        }
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_AUXILIARY_COUNT; ++index) {
        const San9P1EasyGeneratedAuxiliary *definition =
            &san9_p1_easy_generated_auxiliaries[index];
        if (!installed) {
            memcpy(snapshot.auxiliary_bytes[index], definition->original,
                SAN9_P1_EASY_AUXILIARY_BYTE_CAPACITY);
        } else if (definition->kind == 2u) {
            write_u32_le(snapshot.auxiliary_bytes[index], binding->game_hwnd);
        } else {
            memcpy(snapshot.auxiliary_bytes[index], definition->installed_a,
                SAN9_P1_EASY_AUXILIARY_BYTE_CAPACITY);
        }
    }
    snapshot.protected_page.base_address =
        binding->game_image_base + SAN9_P1_EASY_PAGE_RVA;
    snapshot.protected_page.region_size = SAN9_P1_EASY_PAGE_LENGTH;
    snapshot.protected_page.state = SAN9_P1_EASY_PAGE_COMMITTED_STATE;
    snapshot.protected_page.protection = installed
        ? SAN9_P1_EASY_PAGE_INSTALLED_PROTECTION
        : SAN9_P1_EASY_PAGE_ORIGINAL_PROTECTION;
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_IDLE_ANCHOR_COUNT; ++index) {
        memcpy(snapshot.idle_anchor_bytes[index],
            san9_p1_easy_generated_idle_anchors[index].expected,
            SAN9_P1_EASY_IDLE_BYTE_CAPACITY);
    }
    return snapshot;
}

typedef struct EasyFixture {
    const San9P1EasyBinding *binding;
    const San9P1EasySnapshot *snapshot;
    uint32_t capture_index;
    uint32_t calls;
} EasyFixture;

static int easy_read(
    void *context,
    uint32_t address,
    uint8_t *output,
    size_t output_size)
{
    EasyFixture *fixture = (EasyFixture *)context;
    uint32_t index;
    ++fixture->calls;
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_REDIRECT_COUNT; ++index) {
        const San9P1EasyGeneratedRedirect *definition =
            &san9_p1_easy_generated_redirects[index];
        if (address == fixture->binding->game_image_base + definition->game_rva) {
            if (output_size != definition->length) {
                return 0;
            }
            memcpy(output, fixture->snapshot->redirect_bytes[index], output_size);
            return 1;
        }
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_AUXILIARY_COUNT; ++index) {
        const San9P1EasyGeneratedAuxiliary *definition =
            &san9_p1_easy_generated_auxiliaries[index];
        uint32_t base = definition->module_index == 0u
            ? fixture->binding->game_image_base : fixture->binding->easy_image_base;
        if (address == base + definition->rva) {
            if (output_size != definition->length) {
                return 0;
            }
            memcpy(output, fixture->snapshot->auxiliary_bytes[index], output_size);
            return 1;
        }
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_IDLE_ANCHOR_COUNT; ++index) {
        const San9P1EasyGeneratedIdleAnchor *definition =
            &san9_p1_easy_generated_idle_anchors[index];
        if (address == fixture->binding->game_image_base + definition->game_rva) {
            if (output_size != definition->length) {
                return 0;
            }
            memcpy(output, fixture->snapshot->idle_anchor_bytes[index], output_size);
            if (index == SAN9_P1_EASY_IDLE_CALLER_INDEX) {
                ++fixture->capture_index;
            }
            return 1;
        }
    }
    return 0;
}

static int easy_query(
    void *context,
    uint32_t address,
    San9P1EasyMemoryRegion *output)
{
    EasyFixture *fixture = (EasyFixture *)context;
    ++fixture->calls;
    if (address != fixture->binding->game_image_base + SAN9_P1_EASY_PAGE_RVA) {
        return 0;
    }
    *output = fixture->snapshot->protected_page;
    return 1;
}

static San9P1Frame make_binding_frame(void)
{
    San9P1Frame frame;
    memset(&frame, 0, sizeof(frame));
    frame.kind = SAN9_P1_PING_REQUEST;
    frame.state = SAN9_P1_PENDING;
    frame.sequence = 1u;
    frame.issued_at_ms = 1000u;
    frame.expires_at_ms = 2000u;
    frame.game_pid = 11u;
    frame.main_tid = 22u;
    frame.game_hwnd = 33u;
    frame.helper_pid = 44u;
    frame.easy_loader_pid = 55u;
    frame.game_generation = 66u;
    frame.helper_generation = 77u;
    frame.easy_loader_generation = 88u;
    fill_bytes(frame.session_nonce, sizeof(frame.session_nonce), 0x10u);
    fill_bytes(frame.request_id, sizeof(frame.request_id), 0x20u);
    fill_bytes(frame.easy_epoch_nonce, sizeof(frame.easy_epoch_nonce), 0x30u);
    fill_bytes(frame.build_digest, sizeof(frame.build_digest), 0x40u);
    fill_bytes(frame.profile_digest, sizeof(frame.profile_digest), 0x50u);
    fill_bytes(frame.manifest_digest, sizeof(frame.manifest_digest), 0x60u);
    fill_bytes(frame.easy_epoch_digest, sizeof(frame.easy_epoch_digest), 0x70u);
    fill_bytes(frame.easy_ticket_digest, sizeof(frame.easy_ticket_digest), 0x80u);
    fill_bytes(frame.context_digest, sizeof(frame.context_digest), 0x90u);
    fill_bytes(frame.bridge_digest, sizeof(frame.bridge_digest), 0xA0u);
    fill_bytes(frame.mapping_digest, sizeof(frame.mapping_digest), 0xB0u);
    fill_bytes(frame.challenge_digest, sizeof(frame.challenge_digest), 0xC0u);
    return frame;
}

static San9P1Frame make_request(
    const San9P1Frame *binding,
    uint64_t sequence,
    uint64_t issued_at_ms,
    uint8_t identity)
{
    San9P1Frame request = *binding;
    request.sequence = sequence;
    request.issued_at_ms = issued_at_ms;
    request.expires_at_ms = issued_at_ms + 500u;
    fill_bytes(request.request_id, sizeof(request.request_id), identity);
    fill_bytes(request.challenge_digest, sizeof(request.challenge_digest),
        (uint8_t)(identity + 0x40u));
    memset(request.result_digest, 0, sizeof(request.result_digest));
    return request;
}

struct TestSystem;

typedef struct FakeContext {
    struct TestSystem *system;
    San9P1M2FakeActionKind fail_action;
    San9P1M2FakeActionKind cancel_action;
    San9P1M2FakeActionKind nested_action;
    San9P1M2FakeActionKind cancel_loses_action;
    San9P1M2Status nested_status;
    San9P1M2Status cancel_status;
    uint32_t calls[5];
    uint32_t caller;
    uint8_t easy_digest[SAN9_P1_DIGEST_SIZE];
} FakeContext;

typedef struct TestSystem {
    San9P1M2Mailbox mailbox;
    San9P1M2RuntimeStorage runtime;
    San9P1EasyBinding easy_binding;
    San9P1EasySnapshot easy_snapshot;
    EasyFixture easy_fixture;
    San9P1EasyCallbacks easy_callbacks;
    San9P1Frame binding_frame;
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    FakeContext fake;
    San9P1M2FakeActions actions;
} TestSystem;

static int fake_action(
    void *context,
    San9P1M2FakeActionKind action,
    const San9P1Frame *authenticated_request,
    San9P1M2ResultEvidence *result_evidence)
{
    FakeContext *fake = (FakeContext *)context;
    TestSystem *system = fake->system;
    if ((unsigned int)action < sizeof(fake->calls) / sizeof(fake->calls[0])) {
        ++fake->calls[action];
    }
    if (action != SAN9_P1_M2_FAKE_PING && authenticated_request == NULL) {
        return 0;
    }
    if (action == fake->nested_action) {
        fake->nested_action = 0;
        fake->nested_status = san9_p1_m2_lifecycle_validate_and_commit(
            &system->runtime, &system->easy_binding, &system->easy_callbacks,
            &system->actions, TEST_OWNER_TOKEN);
    }
    if (action == fake->cancel_action) {
        fake->cancel_action = 0;
        fake->cancel_status = san9_p1_m2_lifecycle_cancel_precommit(
            &system->runtime);
    }
    if (action == fake->cancel_loses_action) {
        fake->cancel_loses_action = 0;
        fake->cancel_status = san9_p1_m2_lifecycle_cancel_precommit(
            &system->runtime);
    }
    if (action == SAN9_P1_M2_FAKE_PING) {
        if (authenticated_request == NULL || result_evidence == NULL) {
            return 0;
        }
        result_evidence->caller = fake->caller;
        memcpy(result_evidence->easy_snapshot_digest, fake->easy_digest,
            sizeof(result_evidence->easy_snapshot_digest));
    }
    return action != fake->fail_action;
}

static int initialize_system(TestSystem *system, int installed)
{
    uint8_t binding_bytes[SAN9_P1_FRAME_SIZE];
    San9P1DecodeStatus decode_status;
    memset(system, 0, sizeof(*system));
    fill_bytes(system->key, sizeof(system->key), 1u);
    system->easy_binding = make_easy_binding();
    system->easy_snapshot = make_easy_snapshot(&system->easy_binding, installed);
    system->easy_fixture.binding = &system->easy_binding;
    system->easy_fixture.snapshot = &system->easy_snapshot;
    system->easy_callbacks.read = easy_read;
    system->easy_callbacks.query = easy_query;
    system->easy_callbacks.context = &system->easy_fixture;
    system->binding_frame = make_binding_frame();
    system->fake.system = system;
    system->fake.caller = UINT32_C(0x10203040);
    fill_bytes(system->fake.easy_digest, sizeof(system->fake.easy_digest), 0xD0u);
    system->actions.invoke = fake_action;
    system->actions.context = &system->fake;
    if (!san9_p1_m2_mailbox_initialize(&system->mailbox,
            system->key, sizeof(system->key))
        || !san9_p1_m2_runtime_initialize(&system->runtime, &system->mailbox)
        || san9_p1_m2_lifecycle_begin_write(&system->runtime) != SAN9_P1_M2_OK
        || !san9_p1_encode(&system->binding_frame, system->key,
            sizeof(system->key), binding_bytes, sizeof(binding_bytes))
        || san9_p1_m2_session_bind_authenticated(&system->runtime,
            binding_bytes, sizeof(binding_bytes), &decode_status) != SAN9_P1_M2_OK
        || decode_status != SAN9_P1_DECODE_ACCEPTED
        || san9_p1_m2_lifecycle_seal(&system->runtime) != SAN9_P1_M2_OK) {
        return 0;
    }
    return 1;
}

static int commit_system(TestSystem *system)
{
    system->easy_fixture.capture_index = 0u;
    return san9_p1_m2_lifecycle_validate_and_commit(&system->runtime,
        &system->easy_binding, &system->easy_callbacks, &system->actions,
        TEST_OWNER_TOKEN) == SAN9_P1_M2_OK;
}

static int encode_request(
    const TestSystem *system,
    const San9P1Frame *request,
    uint8_t output[SAN9_P1_FRAME_SIZE])
{
    return san9_p1_encode(request, system->key, sizeof(system->key),
        output, SAN9_P1_FRAME_SIZE);
}

static void test_abi_initialize_alias(void)
{
    San9P1M2Mailbox mailbox;
    San9P1M2Mailbox before;
    San9P1M2RuntimeStorage storage;
    San9P1M2RuntimeStorage storage_before;
    uint8_t key[SAN9_P1_HMAC_KEY_SIZE];
    fill_bytes(key, sizeof(key), 1u);
    check(sizeof(mailbox) == 4096u && sizeof(storage) == 4096u,
        "abi-fixed-4096");
    check(offsetof(San9P1M2Mailbox, request_frame) == 512u
        && offsetof(San9P1M2Mailbox, response_frame) == 1024u,
        "abi-frame-offsets");
    check(SAN9_P1_M2_LIVE_AUTHORIZATION == 0
        && SAN9_P1_LIVE_AUTHORIZATION == 0,
        "abi-live-zero");

    memset(&mailbox, 0xA5, sizeof(mailbox));
    before = mailbox;
    check(!san9_p1_m2_mailbox_initialize(&mailbox, key, sizeof(key) - 1u)
        && memcmp(&mailbox, &before, sizeof(mailbox)) == 0,
        "init-reject-before-clear-size");
    memset(&mailbox, 0xA5, sizeof(mailbox));
    before = mailbox;
    check(!san9_p1_m2_mailbox_initialize(&mailbox,
            ((const uint8_t *)&mailbox) + SAN9_P1_M2_HMAC_KEY_OFFSET,
            SAN9_P1_HMAC_KEY_SIZE)
        && memcmp(&mailbox, &before, sizeof(mailbox)) == 0,
        "init-reject-before-clear-alias");
    check(san9_p1_m2_mailbox_initialize(&mailbox, key, sizeof(key))
        && san9_p1_m2_mailbox_validate(&mailbox)
        && atomic_is_lock_free(&mailbox.lifecycle_state),
        "init-valid-lock-free");
    mailbox.reserved_tail[0] = 1u;
    check(!san9_p1_m2_mailbox_validate(&mailbox), "reserved-tail-reject");
    mailbox.reserved_tail[0] = 0u;

    memset(&storage, 0xA5, sizeof(storage));
    storage_before = storage;
    mailbox.magic ^= 1u;
    check(!san9_p1_m2_runtime_initialize(&storage, &mailbox)
        && memcmp(&storage, &storage_before, sizeof(storage)) == 0,
        "runtime-invalid-reject-before-clear");
    mailbox.magic ^= 1u;
    before = mailbox;
    check(!san9_p1_m2_runtime_initialize(
            (San9P1M2RuntimeStorage *)(void *)&mailbox, &mailbox)
        && memcmp(&mailbox, &before, sizeof(mailbox)) == 0,
        "runtime-alias-reject-before-clear");
}

static void test_result_digest_golden_and_zeroing(void)
{
    static const uint8_t expected[SAN9_P1_DIGEST_SIZE] = {
        0x1Au, 0x93u, 0x7Au, 0x70u, 0x65u, 0x10u, 0x1Fu, 0xE6u,
        0x88u, 0x38u, 0x7Au, 0x0Bu, 0x88u, 0xB5u, 0xA1u, 0x8Du,
        0xD6u, 0x61u, 0x3Bu, 0xE6u, 0x99u, 0xA7u, 0xFAu, 0xB3u,
        0x3Fu, 0x82u, 0xCEu, 0x0Eu, 0xE4u, 0xD3u, 0x44u, 0x83u
    };
    San9P1Frame request;
    uint8_t easy_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t output[SAN9_P1_DIGEST_SIZE];
    size_t index;
    memset(&request, 0, sizeof(request));
    request.kind = SAN9_P1_PING_REQUEST;
    request.state = SAN9_P1_PENDING;
    request.sequence = UINT64_C(0x0102030405060708);
    request.main_tid = UINT32_C(0x11223344);
    for (index = 0u; index < SAN9_P1_NONCE_SIZE; ++index) {
        request.request_id[index] = (uint8_t)(index + 1u);
    }
    for (index = 0u; index < SAN9_P1_DIGEST_SIZE; ++index) {
        request.challenge_digest[index] = (uint8_t)(0x80u + index);
        easy_digest[index] = (uint8_t)(0x20u + index);
    }
    check(san9_p1_m2_result_digest(&request, UINT32_C(0x55667788),
            easy_digest, 37u, output)
        && memcmp(output, expected, sizeof(expected)) == 0,
        "result-digest-independent-golden");
    memset(output, 0xA5, sizeof(output));
    check(!san9_p1_m2_result_digest(&request, 0u, easy_digest, 37u, output)
        && all_zero(output, sizeof(output)), "result-failure-zeroes-output");
    check(!san9_p1_m2_result_digest(&request, UINT32_C(0x55667788),
            easy_digest, 37u, easy_digest)
        && easy_digest[0] == 0x20u && easy_digest[31] == 0x3Fu,
        "result-alias-rejected-before-clear");
}

static void test_lifecycle_matrix(void)
{
    TestSystem system;
    uint8_t owner_bytes[8] = {0x11u, 0x22u, 0x33u, 0x44u,
        0x55u, 0x66u, 0x77u, 0x88u};
    size_t index;
    int owner_visible = 0;

    check(initialize_system(&system, 1) && commit_system(&system)
        && san9_p1_m2_lifecycle_load(&system.runtime)
            == SAN9_P1_M2_LIFECYCLE_READY,
        "lifecycle-happy-ready");
    check(system.fake.calls[SAN9_P1_M2_FAKE_PRECOMMIT_VALIDATE] == 1u
        && system.fake.calls[SAN9_P1_M2_FAKE_BEFORE_COMMIT_CAS] == 1u
        && system.fake.calls[SAN9_P1_M2_FAKE_COMMIT_SIDE_EFFECT] == 1u,
        "lifecycle-actions-once");
    for (index = 0u; index + sizeof(owner_bytes) <= sizeof(system.runtime.bytes); ++index) {
        if (memcmp(system.runtime.bytes + index, owner_bytes, sizeof(owner_bytes)) == 0) {
            owner_visible = 1;
        }
    }
    check(!owner_visible, "owner-token-not-recoverable-from-runtime");

    check(initialize_system(&system, 1), "cancel-precommit-setup");
    system.fake.cancel_action = SAN9_P1_M2_FAKE_PRECOMMIT_VALIDATE;
    check(san9_p1_m2_lifecycle_validate_and_commit(&system.runtime,
            &system.easy_binding, &system.easy_callbacks, &system.actions,
            TEST_OWNER_TOKEN) == SAN9_P1_M2_CANCELLED_PRECOMMIT
        && system.fake.cancel_status == SAN9_P1_M2_CANCELLED_PRECOMMIT
        && san9_p1_m2_lifecycle_load(&system.runtime)
            == SAN9_P1_M2_LIFECYCLE_STOPPED_PRECOMMIT
        && system.easy_fixture.calls == 0u,
        "cancel-wins-validating-before-m1");

    check(initialize_system(&system, 1), "cancel-before-cas-setup");
    system.fake.cancel_action = SAN9_P1_M2_FAKE_BEFORE_COMMIT_CAS;
    check(san9_p1_m2_lifecycle_validate_and_commit(&system.runtime,
            &system.easy_binding, &system.easy_callbacks, &system.actions,
            TEST_OWNER_TOKEN) == SAN9_P1_M2_CANCELLED_PRECOMMIT
        && system.fake.cancel_status == SAN9_P1_M2_CANCELLED_PRECOMMIT
        && system.fake.calls[SAN9_P1_M2_FAKE_COMMIT_SIDE_EFFECT] == 0u,
        "cancel-wins-before-committing-cas");

    check(initialize_system(&system, 1), "double-claim-setup");
    system.fake.nested_action = SAN9_P1_M2_FAKE_PRECOMMIT_VALIDATE;
    check(commit_system(&system)
        && system.fake.nested_status == SAN9_P1_M2_CAS_LOST
        && san9_p1_m2_lifecycle_load(&system.runtime)
            == SAN9_P1_M2_LIFECYCLE_READY,
        "double-claim-one-committer");

    check(initialize_system(&system, 1), "cancel-loses-setup");
    system.fake.cancel_loses_action = SAN9_P1_M2_FAKE_COMMIT_SIDE_EFFECT;
    check(commit_system(&system)
        && system.fake.cancel_status == SAN9_P1_M2_STATE_REJECTED
        && san9_p1_m2_lifecycle_load(&system.runtime)
            == SAN9_P1_M2_LIFECYCLE_READY,
        "cancel-loses-after-committing");

    check(initialize_system(&system, 1), "postcommit-fail-setup");
    system.fake.fail_action = SAN9_P1_M2_FAKE_COMMIT_SIDE_EFFECT;
    check(san9_p1_m2_lifecycle_validate_and_commit(&system.runtime,
            &system.easy_binding, &system.easy_callbacks, &system.actions,
            TEST_OWNER_TOKEN) == SAN9_P1_M2_POISONED
        && san9_p1_m2_lifecycle_load(&system.runtime)
            == SAN9_P1_M2_LIFECYCLE_POISONED_RESTART
        && atomic_load_explicit(&system.mailbox.poison_code, memory_order_acquire)
            == SAN9_P1_M2_POISON_POSTCOMMIT_ACTION,
        "postcommit-failure-poisons");

    check(initialize_system(&system, 1) && commit_system(&system),
        "poison-first-setup");
    check(san9_p1_m2_lifecycle_poison(&system.runtime,
            SAN9_P1_M2_POISON_RESULT_DIGEST, TEST_OWNER_TOKEN + 1u)
            == SAN9_P1_M2_POISONED
        && san9_p1_m2_lifecycle_load(&system.runtime)
            == SAN9_P1_M2_LIFECYCLE_POISONED_RESTART
        && atomic_load_explicit(&system.mailbox.poison_code, memory_order_acquire)
            == SAN9_P1_M2_POISON_OWNER_MISMATCH,
        "owner-mismatch-fail-closed-poison");
    check(san9_p1_m2_lifecycle_poison(&system.runtime,
            SAN9_P1_M2_POISON_RESPONSE_AUTH, TEST_OWNER_TOKEN)
            == SAN9_P1_M2_POISONED
        && atomic_load_explicit(&system.mailbox.poison_code, memory_order_acquire)
            == SAN9_P1_M2_POISON_OWNER_MISMATCH,
        "first-poison-code-wins");

    check(initialize_system(&system, 0), "m1-reject-setup");
    check(san9_p1_m2_lifecycle_validate_and_commit(&system.runtime,
            &system.easy_binding, &system.easy_callbacks, &system.actions,
            TEST_OWNER_TOKEN) == SAN9_P1_M2_REJECTED_PRECOMMIT
        && san9_p1_m2_lifecycle_load(&system.runtime)
            == SAN9_P1_M2_LIFECYCLE_REJECTED_PRECOMMIT
        && system.fake.calls[SAN9_P1_M2_FAKE_BEFORE_COMMIT_CAS] == 1u
        && system.fake.calls[SAN9_P1_M2_FAKE_COMMIT_SIDE_EFFECT] == 0u,
        "m1-failure-before-commit");
}

static int publish_request(
    TestSystem *system,
    const San9P1Frame *request,
    uint64_t *round_token)
{
    uint8_t bytes[SAN9_P1_FRAME_SIZE];
    if (!encode_request(system, request, bytes)) {
        return 0;
    }
    return san9_p1_m2_controller_publish_request(&system->runtime,
        bytes, sizeof(bytes), round_token) == SAN9_P1_M2_OK;
}

static void test_slot_old_token_and_late_response(void)
{
    TestSystem system;
    San9P1Frame request;
    San9P1Frame verified;
    San9P1DecodeStatus decode_status;
    San9P1GateStatus gate_status;
    uint64_t token1;
    uint64_t token2;
    uint64_t ignored_token = UINT64_MAX;
    check(initialize_system(&system, 1) && commit_system(&system),
        "slot-setup");
    request = make_request(&system.binding_frame, 1u, 3000u, 1u);
    check(publish_request(&system, &request, &token1) && token1 == 1u,
        "slot-round1-publish");
    check(!publish_request(&system, &request, &ignored_token)
        && ignored_token == 0u, "slot-no-early-reuse");
    check(san9_p1_m2_target_process_request(&system.runtime, token1, 3001u,
            &system.actions, &decode_status, &gate_status) == SAN9_P1_M2_OK
        && decode_status == SAN9_P1_DECODE_ACCEPTED
        && gate_status == SAN9_P1_GATE_ACCEPTED
        && atomic_load_explicit(&system.mailbox.controller_ack,
            memory_order_acquire) == 0u,
        "slot-round1-target");
    check(san9_p1_m2_target_process_request(&system.runtime, token1, 3001u,
            &system.actions, &decode_status, &gate_status) == SAN9_P1_M2_CAS_LOST,
        "slot-double-claim-rejected");
    system.mailbox.request_frame[0] ^= 0xFFu;
    check(san9_p1_m2_controller_consume_response(&system.runtime, token1,
            &verified, &decode_status) == SAN9_P1_M2_OK
        && verified.sequence == 1u
        && atomic_load_explicit(&system.mailbox.controller_ack,
            memory_order_acquire) == 1u,
        "slot-private-request-immutable");

    request = make_request(&system.binding_frame, 2u, 3010u, 2u);
    check(publish_request(&system, &request, &token2) && token2 == 2u,
        "slot-round2-publish");
    check(san9_p1_m2_controller_discard_failed_request(&system.runtime, token1)
            == SAN9_P1_M2_LATE_RESPONSE
        && atomic_load_explicit(&system.mailbox.request_slot_state,
            memory_order_acquire) == SAN9_P1_M2_SLOT_READY
        && atomic_load_explicit(&system.mailbox.controller_ack,
            memory_order_acquire) == 1u,
        "old-discard-cannot-touch-next-round");
    check(san9_p1_m2_target_process_request(&system.runtime, token1, 3011u,
            &system.actions, &decode_status, &gate_status)
            == SAN9_P1_M2_LATE_RESPONSE
        && atomic_load_explicit(&system.mailbox.request_slot_state,
            memory_order_acquire) == SAN9_P1_M2_SLOT_READY,
        "old-target-token-cannot-touch-next-round");
    memset(&verified, 0xA5, sizeof(verified));
    check(san9_p1_m2_controller_consume_response(&system.runtime, token1,
            &verified, &decode_status) == SAN9_P1_M2_LATE_RESPONSE
        && all_zero(&verified, sizeof(verified))
        && atomic_load_explicit(&system.mailbox.request_slot_state,
            memory_order_acquire) == SAN9_P1_M2_SLOT_READY,
        "late-consume-zero-and-no-touch");
    check(san9_p1_m2_target_process_request(&system.runtime, token2, 3011u,
            &system.actions, &decode_status, &gate_status) == SAN9_P1_M2_OK
        && san9_p1_m2_controller_consume_response(&system.runtime, token2,
            &verified, &decode_status) == SAN9_P1_M2_OK
        && verified.sequence == 2u,
        "slot-round2-survives-stale-calls");
}

static void test_hundred_and_101_budget(void)
{
    TestSystem system;
    San9P1Frame request;
    San9P1Frame verified;
    San9P1DecodeStatus decode_status;
    San9P1GateStatus gate_status;
    uint64_t token;
    uint32_t ordinal;
    check(initialize_system(&system, 1) && commit_system(&system),
        "hundred-setup");
    for (ordinal = 1u; ordinal <= SAN9_P1_MAXIMUM_SESSION_PINGS; ++ordinal) {
        request = make_request(&system.binding_frame, ordinal,
            UINT64_C(10000) + (uint64_t)ordinal * 10u, (uint8_t)ordinal);
        check_indexed(publish_request(&system, &request, &token)
            && token == ordinal, "hundred-publish-", ordinal);
        check_indexed(san9_p1_m2_target_process_request(&system.runtime, token,
                request.issued_at_ms + 1u, &system.actions, &decode_status,
                &gate_status) == SAN9_P1_M2_OK
            && gate_status == SAN9_P1_GATE_ACCEPTED,
            "hundred-target-", ordinal);
        check_indexed(san9_p1_m2_controller_consume_response(&system.runtime,
                token, &verified, &decode_status) == SAN9_P1_M2_OK
            && verified.sequence == ordinal,
            "hundred-consume-", ordinal);
    }
    check(atomic_load_explicit(&system.mailbox.accepted_ping_count,
            memory_order_acquire) == 100u
        && atomic_load_explicit(&system.mailbox.controller_ack,
            memory_order_acquire) == 100u,
        "hundred-counts-exact");
    request = make_request(&system.binding_frame, 101u, 12000u, 101u);
    check(publish_request(&system, &request, &token) && token == 101u,
        "budget-101-published-for-gate");
    check(san9_p1_m2_target_process_request(&system.runtime, token, 12001u,
            &system.actions, &decode_status, &gate_status)
            == SAN9_P1_M2_BUDGET_EXHAUSTED
        && gate_status == SAN9_P1_GATE_BUDGET_EXHAUSTED
        && san9_p1_m2_lifecycle_load(&system.runtime)
            == SAN9_P1_M2_LIFECYCLE_QUIESCENT_PINNED,
        "budget-101-quiescent");
    check(san9_p1_m2_controller_discard_failed_request(&system.runtime, token)
            == SAN9_P1_M2_OK
        && atomic_load_explicit(&system.mailbox.controller_ack,
            memory_order_acquire) == 101u,
        "budget-101-controller-ack");
    token = UINT64_MAX;
    check(!publish_request(&system, &request, &token) && token == 0u,
        "budget-no-round-102");
}

static void test_gate_clock_and_failure_zeroing(void)
{
    TestSystem system;
    San9P1Frame request;
    San9P1Frame verified;
    San9P1DecodeStatus decode_status;
    San9P1GateStatus gate_status;
    uint8_t bytes[SAN9_P1_FRAME_SIZE];
    uint64_t token;

    check(initialize_system(&system, 1) && commit_system(&system),
        "auth-failure-setup");
    request = make_request(&system.binding_frame, 1u, 4000u, 1u);
    check(encode_request(&system, &request, bytes), "auth-failure-encode");
    bytes[200] ^= 1u;
    check(san9_p1_m2_controller_publish_request(&system.runtime, bytes,
            sizeof(bytes), &token) == SAN9_P1_M2_OK
        && san9_p1_m2_target_process_request(&system.runtime, token, 4001u,
            &system.actions, &decode_status, &gate_status)
            == SAN9_P1_M2_AUTH_REJECTED
        && gate_status == SAN9_P1_GATE_AUTHENTICATED_FRAME_REJECTED,
        "auth-failure-through-frozen-decode-accept");
    check(san9_p1_m2_controller_discard_failed_request(&system.runtime, token)
        == SAN9_P1_M2_OK, "auth-failure-discard");

    check(initialize_system(&system, 1) && commit_system(&system),
        "clock-setup");
    request = make_request(&system.binding_frame, 1u, 5000u, 1u);
    check(publish_request(&system, &request, &token)
        && san9_p1_m2_target_process_request(&system.runtime, token, 5100u,
            &system.actions, &decode_status, &gate_status) == SAN9_P1_M2_OK
        && san9_p1_m2_controller_consume_response(&system.runtime, token,
            &verified, &decode_status) == SAN9_P1_M2_OK,
        "clock-first-round");
    request = make_request(&system.binding_frame, 2u, 5000u, 2u);
    check(publish_request(&system, &request, &token)
        && san9_p1_m2_target_process_request(&system.runtime, token, 5099u,
            &system.actions, &decode_status, &gate_status) == SAN9_P1_M2_POISONED
        && gate_status == SAN9_P1_GATE_CLOCK_ROLLBACK
        && san9_p1_m2_lifecycle_load(&system.runtime)
            == SAN9_P1_M2_LIFECYCLE_POISONED_RESTART
        && atomic_load_explicit(&system.mailbox.poison_code,
            memory_order_acquire) == SAN9_P1_M2_POISON_CLOCK_FAULT,
        "clock-rollback-sticky-poison");

    check(initialize_system(&system, 1) && commit_system(&system),
        "ping-failure-setup");
    system.fake.fail_action = SAN9_P1_M2_FAKE_PING;
    request = make_request(&system.binding_frame, 1u, 6000u, 1u);
    check(publish_request(&system, &request, &token)
        && san9_p1_m2_target_process_request(&system.runtime, token, 6001u,
            &system.actions, &decode_status, &gate_status) == SAN9_P1_M2_POISONED
        && all_zero(system.mailbox.response_frame,
            sizeof(system.mailbox.response_frame)),
        "postcommit-ping-failure-poison-zero-response");

    check(initialize_system(&system, 1) && commit_system(&system),
        "consume-empty-setup");
    memset(&verified, 0xA5, sizeof(verified));
    check(san9_p1_m2_controller_consume_response(&system.runtime, 1u,
            &verified, &decode_status) == SAN9_P1_M2_LATE_RESPONSE
        && all_zero(&verified, sizeof(verified)),
        "consume-failure-zeroes-output");
}

static void test_authenticated_response_and_result_tamper(void)
{
    TestSystem system;
    San9P1Frame request;
    San9P1Frame response;
    San9P1Frame verified;
    San9P1DecodeStatus decode_status;
    San9P1GateStatus gate_status;
    uint8_t encoded[SAN9_P1_FRAME_SIZE];
    uint64_t token;

    check(initialize_system(&system, 1) && commit_system(&system),
        "response-auth-tamper-setup");
    request = make_request(&system.binding_frame, 1u, 7000u, 1u);
    check(publish_request(&system, &request, &token)
        && san9_p1_m2_target_process_request(&system.runtime, token, 7001u,
            &system.actions, &decode_status, &gate_status) == SAN9_P1_M2_OK,
        "response-auth-tamper-produce");
    system.mailbox.response_frame[200] ^= 1u;
    memset(&verified, 0xA5, sizeof(verified));
    check(san9_p1_m2_controller_consume_response(&system.runtime, token,
            &verified, &decode_status) == SAN9_P1_M2_POISONED
        && decode_status == SAN9_P1_DECODE_CRC_MISMATCH
        && all_zero(&verified, sizeof(verified))
        && atomic_load_explicit(&system.mailbox.poison_code,
            memory_order_acquire) == SAN9_P1_M2_POISON_RESPONSE_AUTH,
        "response-must-pass-frozen-auth-verify");

    check(initialize_system(&system, 1) && commit_system(&system),
        "result-tamper-setup");
    request = make_request(&system.binding_frame, 1u, 8000u, 1u);
    check(publish_request(&system, &request, &token)
        && san9_p1_m2_target_process_request(&system.runtime, token, 8001u,
            &system.actions, &decode_status, &gate_status) == SAN9_P1_M2_OK,
        "result-tamper-produce");
    check(san9_p1_decode(system.mailbox.response_frame,
            sizeof(system.mailbox.response_frame), system.key,
            sizeof(system.key), &response) == SAN9_P1_DECODE_ACCEPTED,
        "result-tamper-decode-test-fixture");
    response.result_digest[0] ^= 1u;
    check(san9_p1_encode(&response, system.key, sizeof(system.key),
            encoded, sizeof(encoded)), "result-tamper-reauth-fixture");
    memcpy(system.mailbox.response_frame, encoded, sizeof(encoded));
    memset(&verified, 0xA5, sizeof(verified));
    check(san9_p1_m2_controller_consume_response(&system.runtime, token,
            &verified, &decode_status) == SAN9_P1_M2_POISONED
        && decode_status == SAN9_P1_DECODE_ACCEPTED
        && all_zero(&verified, sizeof(verified))
        && atomic_load_explicit(&system.mailbox.poison_code,
            memory_order_acquire) == SAN9_P1_M2_POISON_RESULT_DIGEST,
        "authenticated-wrong-result-digest-poisons");
}

int main(void)
{
    test_abi_initialize_alias();
    test_result_digest_golden_and_zeroing();
    test_lifecycle_matrix();
    test_slot_old_token_and_late_response();
    test_hundred_and_101_budget();
    test_gate_clock_and_failure_zeroing();
    test_authenticated_response_and_result_tamper();
    if (failures != 0u) {
        fprintf(stderr, "P1_M2A_OFFLINE_SELFTEST FAIL failures=%u checks=%u\n",
            failures, checks);
        return 1;
    }
    printf(
        "P1_M2A_OFFLINE_SELFTEST PASS checks=%u mailbox=4096 pings=100 "
        "process_access=0 target_writes=0 ipc=0 live_code=0\n",
        checks);
    return 0;
}
