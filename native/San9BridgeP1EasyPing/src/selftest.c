#include "san9_p1_easy_gate.h"
#include "san9_p1_easy_manifest.gen.h"

#include <stdio.h>
#include <string.h>

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

static void check_indexed(int condition, const char *prefix, size_t index)
{
    char name[128];
    (void)snprintf(name, sizeof(name), "%s%zu", prefix, index);
    check(condition, name);
}

static int snapshot_is_all_zero(const San9P1EasySnapshot *snapshot)
{
    const uint8_t *bytes = (const uint8_t *)snapshot;
    size_t index;
    for (index = 0u; index < sizeof(*snapshot); ++index) {
        if (bytes[index] != 0u) {
            return 0;
        }
    }
    return 1;
}

static void write_u32_le(uint8_t output[4], uint32_t value)
{
    output[0] = (uint8_t)value;
    output[1] = (uint8_t)(value >> 8);
    output[2] = (uint8_t)(value >> 16);
    output[3] = (uint8_t)(value >> 24);
}

static San9P1EasyBinding create_binding(uint32_t easy_base)
{
    San9P1EasyBinding result;
    result.game_image_base = SAN9_P1_EASY_GAME_IMAGE_BASE;
    result.game_image_size = SAN9_P1_EASY_GAME_IMAGE_SIZE;
    result.easy_image_base = easy_base;
    result.easy_image_size = SAN9_P1_EASY_IMAGE_SIZE;
    result.game_hwnd = UINT32_C(0x1234ABCD);
    return result;
}

static void make_installed_redirect(
    const San9P1EasyBinding *binding,
    size_t index,
    uint8_t output[SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY])
{
    const San9P1EasyGeneratedRedirect *definition = &san9_p1_easy_generated_redirects[index];
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

static San9P1EasySnapshot create_snapshot(
    const San9P1EasyBinding *binding,
    int installed,
    int training_zero)
{
    San9P1EasySnapshot result;
    uint32_t index;
    memset(&result, 0, sizeof(result));
    result.schema = SAN9_P1_EASY_SNAPSHOT_SCHEMA;
    result.structure_size = (uint32_t)sizeof(result);
    result.captured_points = SAN9_P1_EASY_ALL_POINTS_MASK;
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_REDIRECT_COUNT; ++index) {
        if (installed) {
            make_installed_redirect(binding, index, result.redirect_bytes[index]);
        } else {
            memcpy(result.redirect_bytes[index], san9_p1_easy_generated_redirects[index].original,
                SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY);
        }
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_AUXILIARY_COUNT; ++index) {
        const San9P1EasyGeneratedAuxiliary *definition = &san9_p1_easy_generated_auxiliaries[index];
        if (!installed) {
            memcpy(result.auxiliary_bytes[index], definition->original,
                SAN9_P1_EASY_AUXILIARY_BYTE_CAPACITY);
        } else if (definition->kind == 2u) {
            write_u32_le(result.auxiliary_bytes[index], binding->game_hwnd);
        } else if (definition->kind == 1u && training_zero) {
            memcpy(result.auxiliary_bytes[index], definition->installed_b,
                SAN9_P1_EASY_AUXILIARY_BYTE_CAPACITY);
        } else {
            memcpy(result.auxiliary_bytes[index], definition->installed_a,
                SAN9_P1_EASY_AUXILIARY_BYTE_CAPACITY);
        }
    }
    result.protected_page.base_address = binding->game_image_base + SAN9_P1_EASY_PAGE_RVA;
    result.protected_page.region_size = SAN9_P1_EASY_PAGE_LENGTH;
    result.protected_page.state = SAN9_P1_EASY_PAGE_COMMITTED_STATE;
    result.protected_page.protection = installed
        ? SAN9_P1_EASY_PAGE_INSTALLED_PROTECTION
        : SAN9_P1_EASY_PAGE_ORIGINAL_PROTECTION;
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_IDLE_ANCHOR_COUNT; ++index) {
        memcpy(result.idle_anchor_bytes[index], san9_p1_easy_generated_idle_anchors[index].expected,
            SAN9_P1_EASY_IDLE_BYTE_CAPACITY);
    }
    return result;
}

static San9P1EasyState verify_same(
    const San9P1EasyBinding *binding,
    const San9P1EasySnapshot *snapshot,
    San9P1EasyReport *report)
{
    return san9_p1_easy_verify_buffers(binding, snapshot, snapshot, report);
}

static void test_contract_and_vectors(void)
{
    San9P1EasyBinding binding = create_binding(SAN9_P1_EASY_PREFERRED_IMAGE_BASE);
    San9P1EasyBinding high_binding = create_binding(UINT32_C(0x90000000));
    San9P1EasySnapshot installed = create_snapshot(&binding, 1, 0);
    San9P1EasySnapshot high = create_snapshot(&high_binding, 1, 0);
    San9P1EasyReport report;
    static const uint8_t preferred_30[6] = {0xE8u, 0x5Eu, 0x3Cu, 0xBCu, 0x0Fu, 0x90u};
    static const uint8_t preferred_31[6] = {0xE8u, 0x2Au, 0x39u, 0xBAu, 0x0Fu, 0x90u};
    static const uint8_t high_30[6] = {0xE8u, 0x5Eu, 0x3Cu, 0xBCu, 0x8Fu, 0x90u};
    static const uint8_t high_31[6] = {0xE8u, 0x2Au, 0x39u, 0xBAu, 0x8Fu, 0x90u};
    check(strcmp(san9_p1_easy_manifest_sha256(),
        "72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE") == 0,
        "manifest-sha");
    check(san9_p1_easy_owned_write_count() == 38u, "owned-32-plus-6");
    check(san9_p1_easy_total_point_count() == 42u, "total-42-points");
    check(san9_p1_easy_binding_is_exact(&binding), "preferred-binding");
    check(san9_p1_easy_binding_is_exact(&high_binding), "high-binding");
    check(memcmp(installed.redirect_bytes[30], preferred_30, sizeof(preferred_30)) == 0,
        "call-nop-30-next-plus-five");
    check(memcmp(installed.redirect_bytes[31], preferred_31, sizeof(preferred_31)) == 0,
        "call-nop-31-next-plus-five");
    check(memcmp(high.redirect_bytes[30], high_30, sizeof(high_30)) == 0,
        "high-base-call-nop-30");
    check(memcmp(high.redirect_bytes[31], high_31, sizeof(high_31)) == 0,
        "high-base-call-nop-31");
    check(verify_same(&binding, &installed, &report) == SAN9_P1_EASY_INSTALLED
        && report.stable_snapshot == 1u
        && report.compatible_for_future_bridge == 1u
        && report.installed_redirect_count == 32u, "preferred-installed");
    check(verify_same(&high_binding, &high, &report) == SAN9_P1_EASY_INSTALLED,
        "high-base-installed");
    check(verify_same(&binding, &high, &report) == SAN9_P1_EASY_UNKNOWN
        && report.unknown_redirect_count == 32u, "wrong-easy-base-rejected");
}

static void test_states(void)
{
    San9P1EasyBinding binding = create_binding(SAN9_P1_EASY_PREFERRED_IMAGE_BASE);
    San9P1EasySnapshot original = create_snapshot(&binding, 0, 0);
    San9P1EasySnapshot installed = create_snapshot(&binding, 1, 0);
    San9P1EasySnapshot changed;
    San9P1EasyReport report;
    check(verify_same(&binding, &original, &report) == SAN9_P1_EASY_ORIGINAL
        && report.compatible_for_future_bridge == 0u
        && report.original_redirect_count == 32u, "state-original");
    changed = create_snapshot(&binding, 1, 1);
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_INSTALLED,
        "state-installed-training-zero");
    changed = installed;
    memcpy(changed.redirect_bytes[0], original.redirect_bytes[0],
        SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY);
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_MIXED,
        "state-mixed-redirect");
    changed = installed;
    changed.redirect_bytes[0][0] ^= 1u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_UNKNOWN,
        "state-unknown-byte");
    changed = installed;
    changed.captured_points &= ~(UINT64_C(1) << 7);
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_PARTIAL,
        "state-partial-point");
    changed = installed;
    memcpy(changed.auxiliary_bytes[SAN9_P1_EASY_AUX_CHILD_TRAINING_A_INDEX],
        san9_p1_easy_generated_auxiliaries[SAN9_P1_EASY_AUX_CHILD_TRAINING_A_INDEX].installed_b, 4u);
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_UNKNOWN
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_TRAINING_PAIR) != 0u,
        "state-split-training");
    changed = original;
    memcpy(changed.auxiliary_bytes[SAN9_P1_EASY_AUX_CHILD_TRAINING_A_INDEX],
        san9_p1_easy_generated_auxiliaries[SAN9_P1_EASY_AUX_CHILD_TRAINING_A_INDEX].installed_b, 4u);
    memcpy(changed.auxiliary_bytes[SAN9_P1_EASY_AUX_CHILD_TRAINING_B_INDEX],
        san9_p1_easy_generated_auxiliaries[SAN9_P1_EASY_AUX_CHILD_TRAINING_B_INDEX].installed_b, 4u);
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_MIXED,
        "original-training-zero-not-original");
    changed = installed;
    changed.idle_anchor_bytes[SAN9_P1_EASY_IDLE_SLOT_INDEX][0] ^= 1u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_IDLE_ANCHOR_CONFLICT,
        "state-idle-conflict");
    changed = installed;
    changed.redirect_bytes[0][5] = 1u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_UNKNOWN
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_NONCANONICAL_PADDING) != 0u,
        "state-noncanonical-padding");
    check(san9_p1_easy_verify_buffers(&binding, &installed, &original, &report)
        == SAN9_P1_EASY_SNAPSHOT_UNSTABLE, "state-a-b-unstable");
}

static void check_stable_mutation_rejected(
    const San9P1EasyBinding *binding,
    const San9P1EasySnapshot *mutated,
    const char *prefix,
    size_t index)
{
    San9P1EasyReport report;
    check_indexed(verify_same(binding, mutated, &report) != SAN9_P1_EASY_INSTALLED,
        prefix, index);
}

static void check_unstable_mutation_rejected(
    const San9P1EasyBinding *binding,
    const San9P1EasySnapshot *original,
    const San9P1EasySnapshot *mutated,
    const char *prefix,
    size_t index)
{
    San9P1EasyReport report;
    check_indexed(san9_p1_easy_verify_buffers(binding, original, mutated, &report)
        == SAN9_P1_EASY_SNAPSHOT_UNSTABLE, prefix, index);
}

static void test_every_owned_byte_and_a_b(void)
{
    San9P1EasyBinding binding = create_binding(SAN9_P1_EASY_PREFERRED_IMAGE_BASE);
    San9P1EasySnapshot installed = create_snapshot(&binding, 1, 0);
    uint32_t point;
    size_t ordinal = 0u;
    for (point = 0u; point < SAN9_P1_EASY_GENERATED_REDIRECT_COUNT; ++point) {
        uint32_t byte_index;
        for (byte_index = 0u; byte_index < san9_p1_easy_generated_redirects[point].length; ++byte_index) {
            San9P1EasySnapshot changed = installed;
            changed.redirect_bytes[point][byte_index] ^= 1u;
            check_stable_mutation_rejected(&binding, &changed, "redirect-stable-byte-", ordinal);
            check_unstable_mutation_rejected(&binding, &installed, &changed,
                "redirect-a-b-byte-", ordinal);
            ++ordinal;
        }
    }
    check(ordinal == 162u, "redirect-byte-coverage-162");
    ordinal = 0u;
    for (point = 0u; point < SAN9_P1_EASY_GENERATED_AUXILIARY_COUNT; ++point) {
        uint32_t byte_index;
        for (byte_index = 0u; byte_index < san9_p1_easy_generated_auxiliaries[point].length; ++byte_index) {
            San9P1EasySnapshot changed = installed;
            changed.auxiliary_bytes[point][byte_index] ^= 1u;
            check_stable_mutation_rejected(&binding, &changed, "aux-stable-byte-", ordinal);
            check_unstable_mutation_rejected(&binding, &installed, &changed,
                "aux-a-b-byte-", ordinal);
            ++ordinal;
        }
    }
    check(ordinal == 15u, "aux-byte-coverage-15");
    ordinal = 0u;
    for (point = 0u; point < SAN9_P1_EASY_GENERATED_IDLE_ANCHOR_COUNT; ++point) {
        uint32_t byte_index;
        for (byte_index = 0u; byte_index < san9_p1_easy_generated_idle_anchors[point].length; ++byte_index) {
            San9P1EasySnapshot changed = installed;
            changed.idle_anchor_bytes[point][byte_index] ^= 1u;
            check_stable_mutation_rejected(&binding, &changed, "idle-stable-byte-", ordinal);
            check_unstable_mutation_rejected(&binding, &installed, &changed,
                "idle-a-b-byte-", ordinal);
            ++ordinal;
        }
    }
    check(ordinal == 22u, "idle-byte-coverage-22");
    for (point = 0u; point < SAN9_P1_EASY_TOTAL_POINT_CAPACITY; ++point) {
        San9P1EasySnapshot changed = installed;
        changed.captured_points &= ~(UINT64_C(1) << point);
        check_stable_mutation_rejected(&binding, &changed, "missing-point-", point);
        check_unstable_mutation_rejected(&binding, &installed, &changed,
            "missing-point-a-b-", point);
    }
}

static void test_binding_matrix(void)
{
    San9P1EasyBinding exact = create_binding(SAN9_P1_EASY_PREFERRED_IMAGE_BASE);
    San9P1EasyBinding changed;
    San9P1EasySnapshot installed = create_snapshot(&exact, 1, 0);
    San9P1EasyReport report;
    changed = exact; changed.game_image_base += UINT32_C(0x10000);
    check(!san9_p1_easy_binding_is_exact(&changed), "binding-wrong-game-base");
    changed = exact; --changed.game_image_size;
    check(!san9_p1_easy_binding_is_exact(&changed), "binding-wrong-game-size");
    changed = exact; changed.easy_image_base = 0u;
    check(!san9_p1_easy_binding_is_exact(&changed), "binding-zero-easy-base");
    changed = exact; changed.easy_image_base = UINT32_C(0xFFFFA000);
    check(!san9_p1_easy_binding_is_exact(&changed), "binding-easy-image-overflow");
    changed = exact; --changed.easy_image_size;
    check(!san9_p1_easy_binding_is_exact(&changed), "binding-wrong-easy-size");
    changed = exact; changed.game_hwnd = 0u;
    check(!san9_p1_easy_binding_is_exact(&changed), "binding-zero-hwnd");
    check(san9_p1_easy_verify_buffers(NULL, &installed, &installed, &report)
        == SAN9_P1_EASY_INVALID_BINDING, "binding-null");
    check(san9_p1_easy_verify_buffers(&exact, NULL, &installed, &report)
        == SAN9_P1_EASY_UNKNOWN, "snapshot-a-null");
    check(san9_p1_easy_verify_buffers(&exact, &installed, NULL, &report)
        == SAN9_P1_EASY_UNKNOWN, "snapshot-b-null");
}

static void test_hwnd_page_slot_anchor_matrix(void)
{
    San9P1EasyBinding binding = create_binding(SAN9_P1_EASY_PREFERRED_IMAGE_BASE);
    San9P1EasySnapshot installed = create_snapshot(&binding, 1, 0);
    San9P1EasySnapshot changed;
    San9P1EasyReport report;
    changed = installed;
    changed.auxiliary_bytes[SAN9_P1_EASY_AUX_HWND_INDEX][0] ^= 1u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_UNKNOWN
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_HWND) != 0u, "wrong-hwnd");
    changed = installed; changed.protected_page.base_address += 1u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_UNKNOWN,
        "page-base-too-late");
    changed = installed; --changed.protected_page.region_size;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_UNKNOWN,
        "page-short-region");
    changed = installed; changed.protected_page.state = 0u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_UNKNOWN,
        "page-uncommitted");
    changed = installed; changed.protected_page.protection = UINT32_C(0x20);
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_UNKNOWN,
        "page-wrong-protection");
    changed = installed;
    changed.protected_page.base_address -= UINT32_C(0x100);
    changed.protected_page.region_size += UINT32_C(0x100);
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_INSTALLED,
        "page-covering-region");
    changed = installed; changed.protected_page.base_address = UINT32_C(0xFFFFFF00);
    changed.protected_page.region_size = UINT32_C(0x200);
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_UNKNOWN,
        "page-region-overflow");
    {
        size_t byte_index;
        for (byte_index = 0u; byte_index < sizeof(installed.protected_page); ++byte_index) {
            changed = installed;
            ((uint8_t *)&changed.protected_page)[byte_index] ^= 1u;
            check_unstable_mutation_rejected(&binding, &installed, &changed,
                "page-a-b-byte-", byte_index);
        }
    }
    changed = installed; changed.idle_anchor_bytes[SAN9_P1_EASY_IDLE_SLOT_INDEX][3] ^= 1u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_IDLE_ANCHOR_CONFLICT
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_IDLE_SLOT) != 0u, "wrong-idle-slot");
    changed = installed; changed.idle_anchor_bytes[SAN9_P1_EASY_IDLE_FUNCTION_INDEX][2] ^= 1u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_IDLE_ANCHOR_CONFLICT
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_IDLE_FUNCTION) != 0u, "wrong-idle-function");
    changed = installed; changed.idle_anchor_bytes[SAN9_P1_EASY_IDLE_CALLER_INDEX][7] ^= 1u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_IDLE_ANCHOR_CONFLICT
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_IDLE_CALLER) != 0u, "wrong-idle-caller");
}

typedef struct CallbackFixture {
    const San9P1EasyBinding *binding;
    const San9P1EasySnapshot *snapshot_a;
    const San9P1EasySnapshot *snapshot_b;
    uint32_t capture_index;
    uint32_t fail_point;
    uint32_t fail_capture;
    uint32_t unexpected_calls;
} CallbackFixture;

static const San9P1EasySnapshot *callback_snapshot(const CallbackFixture *fixture)
{
    return fixture->capture_index == 0u ? fixture->snapshot_a : fixture->snapshot_b;
}

static int fixture_read(
    void *context,
    uint32_t address,
    uint8_t *output,
    size_t output_size)
{
    CallbackFixture *fixture = (CallbackFixture *)context;
    const San9P1EasySnapshot *snapshot = callback_snapshot(fixture);
    uint32_t index;
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_REDIRECT_COUNT; ++index) {
        const San9P1EasyGeneratedRedirect *definition = &san9_p1_easy_generated_redirects[index];
        if (address == fixture->binding->game_image_base + definition->game_rva) {
            if (output_size != definition->length
                || (fixture->fail_point == index && fixture->fail_capture == fixture->capture_index)) {
                return 0;
            }
            memcpy(output, snapshot->redirect_bytes[index], output_size);
            return 1;
        }
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_AUXILIARY_COUNT; ++index) {
        const San9P1EasyGeneratedAuxiliary *definition = &san9_p1_easy_generated_auxiliaries[index];
        uint32_t base = definition->module_index == 0u
            ? fixture->binding->game_image_base : fixture->binding->easy_image_base;
        uint32_t point = SAN9_P1_EASY_REDIRECT_CAPACITY + index;
        if (address == base + definition->rva) {
            if (output_size != definition->length
                || (fixture->fail_point == point && fixture->fail_capture == fixture->capture_index)) {
                return 0;
            }
            memcpy(output, snapshot->auxiliary_bytes[index], output_size);
            return 1;
        }
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_IDLE_ANCHOR_COUNT; ++index) {
        const San9P1EasyGeneratedIdleAnchor *definition = &san9_p1_easy_generated_idle_anchors[index];
        uint32_t point = SAN9_P1_EASY_REDIRECT_CAPACITY
            + SAN9_P1_EASY_AUXILIARY_CAPACITY + 1u + index;
        if (address == fixture->binding->game_image_base + definition->game_rva) {
            if (output_size != definition->length
                || (fixture->fail_point == point && fixture->fail_capture == fixture->capture_index)) {
                return 0;
            }
            memcpy(output, snapshot->idle_anchor_bytes[index], output_size);
            if (index == SAN9_P1_EASY_IDLE_CALLER_INDEX) {
                ++fixture->capture_index;
            }
            return 1;
        }
    }
    ++fixture->unexpected_calls;
    return 0;
}

static int fixture_query(
    void *context,
    uint32_t address,
    San9P1EasyMemoryRegion *output)
{
    CallbackFixture *fixture = (CallbackFixture *)context;
    uint32_t point = SAN9_P1_EASY_REDIRECT_CAPACITY + SAN9_P1_EASY_AUXILIARY_CAPACITY;
    if (address != fixture->binding->game_image_base + SAN9_P1_EASY_PAGE_RVA) {
        ++fixture->unexpected_calls;
        return 0;
    }
    if (fixture->fail_point == point && fixture->fail_capture == fixture->capture_index) {
        return 0;
    }
    *output = callback_snapshot(fixture)->protected_page;
    return 1;
}

static CallbackFixture create_callback_fixture(
    const San9P1EasyBinding *binding,
    const San9P1EasySnapshot *snapshot_a,
    const San9P1EasySnapshot *snapshot_b)
{
    CallbackFixture result;
    memset(&result, 0, sizeof(result));
    result.binding = binding;
    result.snapshot_a = snapshot_a;
    result.snapshot_b = snapshot_b;
    result.fail_point = UINT32_MAX;
    result.fail_capture = UINT32_MAX;
    return result;
}

static San9P1EasyCallbacks callbacks_for(CallbackFixture *fixture)
{
    San9P1EasyCallbacks result;
    result.read = fixture_read;
    result.query = fixture_query;
    result.context = fixture;
    return result;
}

static void test_callback_capture_and_a_b(void)
{
    San9P1EasyBinding binding = create_binding(SAN9_P1_EASY_PREFERRED_IMAGE_BASE);
    San9P1EasySnapshot installed = create_snapshot(&binding, 1, 0);
    San9P1EasySnapshot changed = installed;
    San9P1EasySnapshot captured;
    San9P1EasyReport report;
    CallbackFixture fixture = create_callback_fixture(&binding, &installed, &installed);
    San9P1EasyCallbacks callbacks = callbacks_for(&fixture);
    check(san9_p1_easy_capture(&binding, &callbacks, &captured)
        && memcmp(&captured, &installed, sizeof(captured)) == 0
        && fixture.capture_index == 1u && fixture.unexpected_calls == 0u,
        "callback-buffer-capture");
    fixture = create_callback_fixture(&binding, &installed, &installed);
    callbacks = callbacks_for(&fixture);
    check(san9_p1_easy_verify_callbacks(&binding, &callbacks, &report)
        == SAN9_P1_EASY_INSTALLED
        && fixture.capture_index == 2u && fixture.unexpected_calls == 0u,
        "callback-stable-a-b");
    changed.redirect_bytes[4][2] ^= 1u;
    fixture = create_callback_fixture(&binding, &installed, &changed);
    callbacks = callbacks_for(&fixture);
    check(san9_p1_easy_verify_callbacks(&binding, &callbacks, &report)
        == SAN9_P1_EASY_SNAPSHOT_UNSTABLE, "callback-unstable-a-b");
    {
        uint32_t point;
        for (point = 0u; point < SAN9_P1_EASY_TOTAL_POINT_CAPACITY; ++point) {
            fixture = create_callback_fixture(&binding, &installed, &installed);
            fixture.fail_point = point;
            fixture.fail_capture = 0u;
            callbacks = callbacks_for(&fixture);
            check_indexed(san9_p1_easy_verify_callbacks(&binding, &callbacks, &report)
                == SAN9_P1_EASY_CAPTURE_FAILED, "callback-a-fail-point-", point);
            fixture = create_callback_fixture(&binding, &installed, &installed);
            fixture.fail_point = point;
            fixture.fail_capture = 1u;
            callbacks = callbacks_for(&fixture);
            check_indexed(san9_p1_easy_verify_callbacks(&binding, &callbacks, &report)
                == SAN9_P1_EASY_CAPTURE_FAILED, "callback-b-fail-point-", point);

            memset(&captured, 0xA5, sizeof(captured));
            fixture = create_callback_fixture(&binding, &installed, &installed);
            fixture.fail_point = point;
            fixture.fail_capture = 0u;
            callbacks = callbacks_for(&fixture);
            check_indexed(!san9_p1_easy_capture(&binding, &callbacks, &captured)
                && snapshot_is_all_zero(&captured), "capture-a-fail-clears-point-", point);

            memset(&captured, 0xA5, sizeof(captured));
            fixture = create_callback_fixture(&binding, &installed, &installed);
            fixture.capture_index = 1u;
            fixture.fail_point = point;
            fixture.fail_capture = 1u;
            callbacks = callbacks_for(&fixture);
            check_indexed(!san9_p1_easy_capture(&binding, &callbacks, &captured)
                && snapshot_is_all_zero(&captured), "capture-b-fail-clears-point-", point);
        }
    }
    memset(&captured, 0xA5, sizeof(captured));
    check(!san9_p1_easy_capture(&binding, NULL, &captured)
        && memcmp(&captured, &(San9P1EasySnapshot){0}, sizeof(captured)) == 0,
        "callback-null-clears-output");
}

static int report_has_counts(
    const San9P1EasyReport *report,
    uint32_t installed,
    uint32_t original,
    uint32_t unknown)
{
    return report->installed_redirect_count == installed
        && report->original_redirect_count == original
        && report->unknown_redirect_count == unknown;
}

static void test_report_failure_normalization(void)
{
    San9P1EasyBinding binding = create_binding(SAN9_P1_EASY_PREFERRED_IMAGE_BASE);
    San9P1EasyBinding invalid_binding = binding;
    San9P1EasySnapshot installed = create_snapshot(&binding, 1, 0);
    San9P1EasySnapshot original = create_snapshot(&binding, 0, 0);
    San9P1EasySnapshot changed;
    San9P1EasyReport report;
    CallbackFixture fixture;
    San9P1EasyCallbacks callbacks;

    invalid_binding.easy_image_base = UINT32_C(0xFFFFA000);
    memset(&report, 0xA5, sizeof(report));
    check(san9_p1_easy_verify_buffers(&invalid_binding, &installed, &installed, &report)
            == SAN9_P1_EASY_INVALID_BINDING
        && report.compatible_for_future_bridge == 0u
        && report.stable_snapshot == 0u
        && report_has_counts(&report, 0u, 0u, 0u)
        && report.detail_flags == SAN9_P1_EASY_DETAIL_BINDING
        && report.first_failure_point == SAN9_P1_EASY_NO_FAILURE_POINT,
        "report-normalized-invalid-binding");

    fixture = create_callback_fixture(&binding, &installed, &installed);
    fixture.fail_point = 0u;
    fixture.fail_capture = 0u;
    callbacks = callbacks_for(&fixture);
    memset(&report, 0xA5, sizeof(report));
    check(san9_p1_easy_verify_callbacks(&binding, &callbacks, &report)
            == SAN9_P1_EASY_CAPTURE_FAILED
        && report.compatible_for_future_bridge == 0u
        && report.stable_snapshot == 0u
        && report_has_counts(&report, 0u, 0u, 0u)
        && report.detail_flags == SAN9_P1_EASY_DETAIL_CAPTURE
        && report.first_failure_point == SAN9_P1_EASY_NO_FAILURE_POINT,
        "report-normalized-capture-failed");

    changed = installed;
    changed.redirect_bytes[4][2] ^= 1u;
    memset(&report, 0xA5, sizeof(report));
    check(san9_p1_easy_verify_buffers(&binding, &installed, &changed, &report)
            == SAN9_P1_EASY_SNAPSHOT_UNSTABLE
        && report.compatible_for_future_bridge == 0u
        && report.stable_snapshot == 0u
        && report_has_counts(&report, 0u, 0u, 0u)
        && report.detail_flags == SAN9_P1_EASY_DETAIL_UNSTABLE
        && report.first_failure_point == 4u,
        "report-normalized-snapshot-unstable");

    memset(&report, 0xA5, sizeof(report));
    check(san9_p1_easy_verify_buffers(&binding, NULL, &installed, &report)
            == SAN9_P1_EASY_UNKNOWN
        && report.compatible_for_future_bridge == 0u
        && report.stable_snapshot == 0u
        && report_has_counts(&report, 0u, 0u, 0u)
        && report.detail_flags == SAN9_P1_EASY_DETAIL_SNAPSHOT_SHAPE
        && report.first_failure_point == SAN9_P1_EASY_NO_FAILURE_POINT,
        "report-normalized-null-shape");

    changed = installed;
    changed.captured_points &= ~(UINT64_C(1) << 7);
    memset(&report, 0xA5, sizeof(report));
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_PARTIAL
        && report.compatible_for_future_bridge == 0u
        && report.stable_snapshot == 1u
        && report_has_counts(&report, 0u, 0u, 0u)
        && report.detail_flags == SAN9_P1_EASY_DETAIL_SNAPSHOT_SHAPE
        && report.first_failure_point == SAN9_P1_EASY_NO_FAILURE_POINT,
        "report-normalized-partial");

    changed = installed;
    memcpy(changed.redirect_bytes[0], original.redirect_bytes[0],
        SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY);
    memset(&report, 0xA5, sizeof(report));
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_MIXED
        && report.compatible_for_future_bridge == 0u
        && report.stable_snapshot == 1u
        && report_has_counts(&report, 31u, 1u, 0u)
        && report.detail_flags == SAN9_P1_EASY_DETAIL_NONE
        && report.first_failure_point == SAN9_P1_EASY_NO_FAILURE_POINT,
        "report-normalized-mixed");

    changed = installed;
    changed.redirect_bytes[0][0] ^= 1u;
    memset(&report, 0xA5, sizeof(report));
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_UNKNOWN
        && report.compatible_for_future_bridge == 0u
        && report.stable_snapshot == 1u
        && report_has_counts(&report, 31u, 0u, 1u)
        && report.detail_flags == SAN9_P1_EASY_DETAIL_REDIRECT
        && report.first_failure_point == 0u,
        "report-normalized-unknown");

    changed = installed;
    changed.idle_anchor_bytes[SAN9_P1_EASY_IDLE_FUNCTION_INDEX][0] ^= 1u;
    memset(&report, 0xA5, sizeof(report));
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_IDLE_ANCHOR_CONFLICT
        && report.compatible_for_future_bridge == 0u
        && report.stable_snapshot == 1u
        && report_has_counts(&report, 32u, 0u, 0u)
        && report.detail_flags == SAN9_P1_EASY_DETAIL_IDLE_FUNCTION
        && report.first_failure_point == 40u,
        "report-normalized-idle-conflict");

    memset(&report, 0xA5, sizeof(report));
    check(verify_same(&binding, &original, &report) == SAN9_P1_EASY_ORIGINAL
        && report.compatible_for_future_bridge == 0u
        && report.stable_snapshot == 1u
        && report_has_counts(&report, 0u, 32u, 0u)
        && report.detail_flags == SAN9_P1_EASY_DETAIL_NONE
        && report.first_failure_point == SAN9_P1_EASY_NO_FAILURE_POINT,
        "report-normalized-original-cleanup");
}

static void test_first_failure_is_recorded_once(void)
{
    San9P1EasyBinding binding = create_binding(SAN9_P1_EASY_PREFERRED_IMAGE_BASE);
    San9P1EasySnapshot installed = create_snapshot(&binding, 1, 0);
    San9P1EasySnapshot changed;
    San9P1EasyReport report;

    changed = installed;
    changed.redirect_bytes[2][0] ^= 1u;
    changed.auxiliary_bytes[SAN9_P1_EASY_AUX_MAX_CORPS_FOOD_A_INDEX][0] ^= 1u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_UNKNOWN
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_REDIRECT) != 0u
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_FIXED_AUXILIARY) != 0u
        && report.first_failure_point == 2u,
        "first-failure-redirect-before-later-aux");

    changed = installed;
    changed.redirect_bytes[2][0] ^= 1u;
    changed.protected_page.state = 0u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_UNKNOWN
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_REDIRECT) != 0u
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_PAGE) != 0u
        && report.first_failure_point == 2u,
        "first-failure-redirect-before-later-page");

    changed = installed;
    changed.redirect_bytes[2][0] ^= 1u;
    changed.idle_anchor_bytes[SAN9_P1_EASY_IDLE_FUNCTION_INDEX][0] ^= 1u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_IDLE_ANCHOR_CONFLICT
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_REDIRECT) != 0u
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_IDLE_FUNCTION) != 0u
        && report.first_failure_point == 2u,
        "first-failure-redirect-before-later-idle");

    changed = installed;
    changed.redirect_bytes[2][0] ^= 1u;
    changed.auxiliary_bytes[SAN9_P1_EASY_AUX_MAX_CORPS_FOOD_A_INDEX][0] ^= 1u;
    changed.protected_page.state = 0u;
    changed.idle_anchor_bytes[SAN9_P1_EASY_IDLE_CALLER_INDEX][0] ^= 1u;
    check(verify_same(&binding, &changed, &report) == SAN9_P1_EASY_IDLE_ANCHOR_CONFLICT
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_REDIRECT) != 0u
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_FIXED_AUXILIARY) != 0u
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_PAGE) != 0u
        && (report.detail_flags & SAN9_P1_EASY_DETAIL_IDLE_CALLER) != 0u
        && report.first_failure_point == 2u,
        "first-failure-redirect-survives-all-later-failures");
}

int main(void)
{
    test_contract_and_vectors();
    test_states();
    test_every_owned_byte_and_a_b();
    test_binding_matrix();
    test_hwnd_page_slot_anchor_matrix();
    test_callback_capture_and_a_b();
    test_report_failure_normalization();
    test_first_failure_is_recorded_once();
    if (failures != 0u) {
        fprintf(stderr, "P1_NATIVE_EASY_SELFTEST FAIL failures=%u checks=%u\n",
            failures, checks);
        return 1;
    }
    printf(
        "P1_NATIVE_EASY_SELFTEST PASS checks=%u redirects=32 auxiliaries=6 "
        "process_access=0 target_writes=0 live_code=0\n",
        checks);
    return 0;
}
