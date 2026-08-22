#include "san9_p1_easy_gate.h"
#include "san9_p1_easy_manifest.gen.h"

#include <string.h>

_Static_assert(SAN9_P1_EASY_GENERATED_REDIRECT_COUNT == SAN9_P1_EASY_REDIRECT_CAPACITY,
    "generated redirect count changed");
_Static_assert(SAN9_P1_EASY_GENERATED_AUXILIARY_COUNT == SAN9_P1_EASY_AUXILIARY_CAPACITY,
    "generated auxiliary count changed");
_Static_assert(SAN9_P1_EASY_GENERATED_IDLE_ANCHOR_COUNT == SAN9_P1_EASY_IDLE_ANCHOR_CAPACITY,
    "generated idle count changed");
_Static_assert(SAN9_P1_EASY_GENERATED_TOTAL_POINT_COUNT == SAN9_P1_EASY_TOTAL_POINT_CAPACITY,
    "generated total point count changed");
_Static_assert(sizeof(uint32_t) == 4u && sizeof(uint64_t) == 8u,
    "fixed-width integer contract changed");

enum {
    SAN9_P1_EASY_MODULE_GAME = 0,
    SAN9_P1_EASY_MODULE_EASY = 1,
    SAN9_P1_EASY_AUX_KIND_FIXED = 0,
    SAN9_P1_EASY_AUX_KIND_TRAINING = 1,
    SAN9_P1_EASY_AUX_KIND_HWND = 2,
    SAN9_P1_EASY_PAGE_POINT = SAN9_P1_EASY_REDIRECT_CAPACITY
        + SAN9_P1_EASY_AUXILIARY_CAPACITY,
    SAN9_P1_EASY_IDLE_POINT_BASE = SAN9_P1_EASY_PAGE_POINT + 1
};

static void clear_report(San9P1EasyReport *report)
{
    if (report != NULL) {
        memset(report, 0, sizeof(*report));
        report->state = SAN9_P1_EASY_NOT_INSPECTED;
        report->first_failure_point = SAN9_P1_EASY_NO_FAILURE_POINT;
    }
}

static void record_first_failure(San9P1EasyReport *report, uint32_t point)
{
    if (report != NULL
        && report->first_failure_point == SAN9_P1_EASY_NO_FAILURE_POINT) {
        report->first_failure_point = point;
    }
}

static int checked_image_end(uint32_t base, uint32_t size, uint64_t *end)
{
    uint64_t candidate;
    if (base == 0u || size == 0u || end == NULL) {
        return 0;
    }
    candidate = (uint64_t)base + (uint64_t)size;
    if (candidate > UINT64_C(0x100000000)) {
        return 0;
    }
    *end = candidate;
    return 1;
}

static int checked_point_address(
    uint32_t image_base,
    uint32_t image_size,
    uint32_t rva,
    uint32_t length,
    uint32_t *address)
{
    uint64_t image_end;
    uint64_t point_end;
    uint64_t start;
    if (address == NULL || length == 0u
        || !checked_image_end(image_base, image_size, &image_end)) {
        return 0;
    }
    start = (uint64_t)image_base + (uint64_t)rva;
    point_end = start + (uint64_t)length;
    if (start > UINT32_MAX || point_end > image_end
        || point_end > UINT64_C(0x100000000)) {
        return 0;
    }
    *address = (uint32_t)start;
    return 1;
}

int san9_p1_easy_binding_is_exact(const San9P1EasyBinding *binding)
{
    uint64_t ignored_end;
    uint32_t index;
    if (binding == NULL || binding->game_hwnd == 0u
        || binding->game_image_base != SAN9_P1_EASY_GAME_IMAGE_BASE
        || binding->game_image_size != SAN9_P1_EASY_GAME_IMAGE_SIZE
        || binding->easy_image_size != SAN9_P1_EASY_IMAGE_SIZE
        || !checked_image_end(binding->game_image_base, binding->game_image_size, &ignored_end)
        || !checked_image_end(binding->easy_image_base, binding->easy_image_size, &ignored_end)) {
        return 0;
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_REDIRECT_COUNT; ++index) {
        const San9P1EasyGeneratedRedirect *definition = &san9_p1_easy_generated_redirects[index];
        uint32_t ignored_address;
        if ((definition->length != 5u && definition->length != 6u)
            || definition->trailing_length != definition->length - 5u
            || !checked_point_address(binding->game_image_base, binding->game_image_size,
                definition->game_rva, definition->length, &ignored_address)
            || !checked_point_address(binding->easy_image_base, binding->easy_image_size,
                definition->easy_target_rva, 1u, &ignored_address)) {
            return 0;
        }
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_AUXILIARY_COUNT; ++index) {
        const San9P1EasyGeneratedAuxiliary *definition = &san9_p1_easy_generated_auxiliaries[index];
        uint32_t ignored_address;
        uint32_t base = definition->module_index == SAN9_P1_EASY_MODULE_GAME
            ? binding->game_image_base : binding->easy_image_base;
        uint32_t size = definition->module_index == SAN9_P1_EASY_MODULE_GAME
            ? binding->game_image_size : binding->easy_image_size;
        if (definition->module_index > SAN9_P1_EASY_MODULE_EASY
            || definition->length == 0u
            || definition->length > SAN9_P1_EASY_AUXILIARY_BYTE_CAPACITY
            || !checked_point_address(base, size, definition->rva,
                definition->length, &ignored_address)) {
            return 0;
        }
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_IDLE_ANCHOR_COUNT; ++index) {
        const San9P1EasyGeneratedIdleAnchor *definition = &san9_p1_easy_generated_idle_anchors[index];
        uint32_t ignored_address;
        if (definition->length == 0u
            || definition->length > SAN9_P1_EASY_IDLE_BYTE_CAPACITY
            || !checked_point_address(binding->game_image_base, binding->game_image_size,
                definition->game_rva, definition->length, &ignored_address)) {
            return 0;
        }
    }
    {
        uint32_t ignored_address;
        if (!checked_point_address(binding->game_image_base, binding->game_image_size,
            SAN9_P1_EASY_PAGE_RVA, SAN9_P1_EASY_PAGE_LENGTH, &ignored_address)) {
            return 0;
        }
    }
    return 1;
}

static int capture_read(
    const San9P1EasyCallbacks *callbacks,
    uint32_t address,
    uint8_t *output,
    size_t output_size)
{
    return callbacks->read(callbacks->context, address, output, output_size) != 0;
}

int san9_p1_easy_capture(
    const San9P1EasyBinding *binding,
    const San9P1EasyCallbacks *callbacks,
    San9P1EasySnapshot *output)
{
    uint32_t index;
    uint32_t address;
    if (output == NULL) {
        return 0;
    }
    memset(output, 0, sizeof(*output));
    if (!san9_p1_easy_binding_is_exact(binding) || callbacks == NULL
        || callbacks->read == NULL || callbacks->query == NULL) {
        return 0;
    }
    output->schema = SAN9_P1_EASY_SNAPSHOT_SCHEMA;
    output->structure_size = (uint32_t)sizeof(*output);
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_REDIRECT_COUNT; ++index) {
        const San9P1EasyGeneratedRedirect *definition = &san9_p1_easy_generated_redirects[index];
        if (!checked_point_address(binding->game_image_base, binding->game_image_size,
                definition->game_rva, definition->length, &address)
            || !capture_read(callbacks, address, output->redirect_bytes[index], definition->length)) {
            memset(output, 0, sizeof(*output));
            return 0;
        }
        output->captured_points |= UINT64_C(1) << index;
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_AUXILIARY_COUNT; ++index) {
        const San9P1EasyGeneratedAuxiliary *definition = &san9_p1_easy_generated_auxiliaries[index];
        uint32_t base = definition->module_index == SAN9_P1_EASY_MODULE_GAME
            ? binding->game_image_base : binding->easy_image_base;
        uint32_t size = definition->module_index == SAN9_P1_EASY_MODULE_GAME
            ? binding->game_image_size : binding->easy_image_size;
        if (!checked_point_address(base, size, definition->rva, definition->length, &address)
            || !capture_read(callbacks, address, output->auxiliary_bytes[index], definition->length)) {
            memset(output, 0, sizeof(*output));
            return 0;
        }
        output->captured_points |= UINT64_C(1)
            << (SAN9_P1_EASY_REDIRECT_CAPACITY + index);
    }
    if (!checked_point_address(binding->game_image_base, binding->game_image_size,
            SAN9_P1_EASY_PAGE_RVA, SAN9_P1_EASY_PAGE_LENGTH, &address)
        || callbacks->query(callbacks->context, address, &output->protected_page) == 0) {
        memset(output, 0, sizeof(*output));
        return 0;
    }
    output->captured_points |= UINT64_C(1) << SAN9_P1_EASY_PAGE_POINT;
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_IDLE_ANCHOR_COUNT; ++index) {
        const San9P1EasyGeneratedIdleAnchor *definition = &san9_p1_easy_generated_idle_anchors[index];
        if (!checked_point_address(binding->game_image_base, binding->game_image_size,
                definition->game_rva, definition->length, &address)
            || !capture_read(callbacks, address, output->idle_anchor_bytes[index], definition->length)) {
            memset(output, 0, sizeof(*output));
            return 0;
        }
        output->captured_points |= UINT64_C(1) << (SAN9_P1_EASY_IDLE_POINT_BASE + index);
    }
    return output->captured_points == SAN9_P1_EASY_ALL_POINTS_MASK;
}

static int bytes_equal(const uint8_t *left, const uint8_t *right, size_t length)
{
    return memcmp(left, right, length) == 0;
}

static int bytes_zero(const uint8_t *value, size_t length)
{
    size_t index;
    for (index = 0u; index < length; ++index) {
        if (value[index] != 0u) {
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

static int expected_installed_redirect(
    const San9P1EasyBinding *binding,
    const San9P1EasyGeneratedRedirect *definition,
    uint8_t output[SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY])
{
    uint32_t source;
    uint32_t target;
    uint32_t displacement;
    memset(output, 0, SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY);
    if (!checked_point_address(binding->game_image_base, binding->game_image_size,
            definition->game_rva, definition->length, &source)
        || !checked_point_address(binding->easy_image_base, binding->easy_image_size,
            definition->easy_target_rva, 1u, &target)) {
        return 0;
    }
    /* The rel32 belongs to the five-byte CALL/JMP.  A trailing NOP is not EIP. */
    displacement = target - (source + UINT32_C(5));
    output[0] = definition->opcode;
    write_u32_le(output + 1, displacement);
    if (definition->trailing_length != 0u) {
        output[5] = definition->trailing[0];
    }
    return 1;
}

static int snapshot_equal(const San9P1EasySnapshot *left, const San9P1EasySnapshot *right)
{
    return left->schema == right->schema
        && left->structure_size == right->structure_size
        && left->captured_points == right->captured_points
        && bytes_equal(&left->redirect_bytes[0][0], &right->redirect_bytes[0][0],
            sizeof(left->redirect_bytes))
        && bytes_equal(&left->auxiliary_bytes[0][0], &right->auxiliary_bytes[0][0],
            sizeof(left->auxiliary_bytes))
        && left->protected_page.base_address == right->protected_page.base_address
        && left->protected_page.region_size == right->protected_page.region_size
        && left->protected_page.state == right->protected_page.state
        && left->protected_page.protection == right->protected_page.protection
        && bytes_equal(&left->idle_anchor_bytes[0][0], &right->idle_anchor_bytes[0][0],
            sizeof(left->idle_anchor_bytes));
}

static uint32_t first_unstable_point(
    const San9P1EasySnapshot *left,
    const San9P1EasySnapshot *right)
{
    uint32_t index;
    for (index = 0u; index < SAN9_P1_EASY_REDIRECT_CAPACITY; ++index) {
        if (!bytes_equal(left->redirect_bytes[index], right->redirect_bytes[index],
            SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY)) {
            return index;
        }
    }
    for (index = 0u; index < SAN9_P1_EASY_AUXILIARY_CAPACITY; ++index) {
        if (!bytes_equal(left->auxiliary_bytes[index], right->auxiliary_bytes[index],
            SAN9_P1_EASY_AUXILIARY_BYTE_CAPACITY)) {
            return SAN9_P1_EASY_REDIRECT_CAPACITY + index;
        }
    }
    if (left->protected_page.base_address != right->protected_page.base_address
        || left->protected_page.region_size != right->protected_page.region_size
        || left->protected_page.state != right->protected_page.state
        || left->protected_page.protection != right->protected_page.protection) {
        return SAN9_P1_EASY_PAGE_POINT;
    }
    for (index = 0u; index < SAN9_P1_EASY_IDLE_ANCHOR_CAPACITY; ++index) {
        if (!bytes_equal(left->idle_anchor_bytes[index], right->idle_anchor_bytes[index],
            SAN9_P1_EASY_IDLE_BYTE_CAPACITY)) {
            return SAN9_P1_EASY_IDLE_POINT_BASE + index;
        }
    }
    return SAN9_P1_EASY_NO_FAILURE_POINT;
}

static int canonical_padding(const San9P1EasySnapshot *snapshot, San9P1EasyReport *report)
{
    uint32_t index;
    int exact = 1;
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_REDIRECT_COUNT; ++index) {
        const San9P1EasyGeneratedRedirect *definition = &san9_p1_easy_generated_redirects[index];
        if (!bytes_zero(snapshot->redirect_bytes[index] + definition->length,
            SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY - definition->length)) {
            record_first_failure(report, index);
            exact = 0;
        }
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_AUXILIARY_COUNT; ++index) {
        const San9P1EasyGeneratedAuxiliary *definition = &san9_p1_easy_generated_auxiliaries[index];
        if (!bytes_zero(snapshot->auxiliary_bytes[index] + definition->length,
            SAN9_P1_EASY_AUXILIARY_BYTE_CAPACITY - definition->length)) {
            record_first_failure(report, SAN9_P1_EASY_REDIRECT_CAPACITY + index);
            exact = 0;
        }
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_IDLE_ANCHOR_COUNT; ++index) {
        const San9P1EasyGeneratedIdleAnchor *definition = &san9_p1_easy_generated_idle_anchors[index];
        if (!bytes_zero(snapshot->idle_anchor_bytes[index] + definition->length,
            SAN9_P1_EASY_IDLE_BYTE_CAPACITY - definition->length)) {
            record_first_failure(report, SAN9_P1_EASY_IDLE_POINT_BASE + index);
            exact = 0;
        }
    }
    if (!exact) {
        report->detail_flags |= SAN9_P1_EASY_DETAIL_NONCANONICAL_PADDING;
    }
    return exact;
}

static int region_covers_page(
    const San9P1EasyBinding *binding,
    const San9P1EasyMemoryRegion *region,
    uint32_t expected_protection)
{
    uint32_t page_address;
    uint64_t region_end;
    uint64_t page_end;
    if (!checked_point_address(binding->game_image_base, binding->game_image_size,
            SAN9_P1_EASY_PAGE_RVA, SAN9_P1_EASY_PAGE_LENGTH, &page_address)
        || region->region_size == 0u) {
        return 0;
    }
    region_end = (uint64_t)region->base_address + (uint64_t)region->region_size;
    page_end = (uint64_t)page_address + (uint64_t)SAN9_P1_EASY_PAGE_LENGTH;
    return region_end <= UINT64_C(0x100000000)
        && region->base_address <= page_address
        && region_end >= page_end
        && region->state == SAN9_P1_EASY_PAGE_COMMITTED_STATE
        && region->protection == expected_protection;
}

San9P1EasyState san9_p1_easy_verify_buffers(
    const San9P1EasyBinding *binding,
    const San9P1EasySnapshot *snapshot_a,
    const San9P1EasySnapshot *snapshot_b,
    San9P1EasyReport *report)
{
    uint32_t index;
    int training_original;
    int training_installed;
    int fixed_original = 1;
    int fixed_installed = 1;
    int hwnd_original;
    int hwnd_installed;
    int page_original;
    int page_installed;
    int idle_exact = 1;
    int padding_exact;
    int unknown_auxiliary = 0;
    uint8_t expected_hwnd[4];
    if (report == NULL) {
        return SAN9_P1_EASY_INVALID_BINDING;
    }
    clear_report(report);
    if (!san9_p1_easy_binding_is_exact(binding)) {
        report->state = SAN9_P1_EASY_INVALID_BINDING;
        report->detail_flags = SAN9_P1_EASY_DETAIL_BINDING;
        return report->state;
    }
    if (snapshot_a == NULL || snapshot_b == NULL) {
        report->state = SAN9_P1_EASY_UNKNOWN;
        report->detail_flags = SAN9_P1_EASY_DETAIL_SNAPSHOT_SHAPE;
        return report->state;
    }
    if (!snapshot_equal(snapshot_a, snapshot_b)) {
        report->state = SAN9_P1_EASY_SNAPSHOT_UNSTABLE;
        report->detail_flags = SAN9_P1_EASY_DETAIL_UNSTABLE;
        record_first_failure(report, first_unstable_point(snapshot_a, snapshot_b));
        return report->state;
    }
    report->stable_snapshot = 1u;
    if (snapshot_b->schema != SAN9_P1_EASY_SNAPSHOT_SCHEMA
        || snapshot_b->structure_size != (uint32_t)sizeof(*snapshot_b)) {
        report->state = SAN9_P1_EASY_UNKNOWN;
        report->detail_flags = SAN9_P1_EASY_DETAIL_SNAPSHOT_SHAPE;
        return report->state;
    }
    if (snapshot_b->captured_points != SAN9_P1_EASY_ALL_POINTS_MASK) {
        report->state = SAN9_P1_EASY_PARTIAL;
        report->detail_flags = SAN9_P1_EASY_DETAIL_SNAPSHOT_SHAPE;
        return report->state;
    }
    padding_exact = canonical_padding(snapshot_b, report);
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_REDIRECT_COUNT; ++index) {
        const San9P1EasyGeneratedRedirect *definition = &san9_p1_easy_generated_redirects[index];
        uint8_t installed[SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY];
        if (!expected_installed_redirect(binding, definition, installed)) {
            report->state = SAN9_P1_EASY_INVALID_BINDING;
            report->detail_flags |= SAN9_P1_EASY_DETAIL_BINDING;
            return report->state;
        }
        if (bytes_equal(snapshot_b->redirect_bytes[index], installed,
            SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY)) {
            ++report->installed_redirect_count;
        } else if (bytes_equal(snapshot_b->redirect_bytes[index], definition->original,
            SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY)) {
            ++report->original_redirect_count;
        } else {
            ++report->unknown_redirect_count;
            report->detail_flags |= SAN9_P1_EASY_DETAIL_REDIRECT;
            record_first_failure(report, index);
        }
    }

    training_original = bytes_equal(
            snapshot_b->auxiliary_bytes[SAN9_P1_EASY_AUX_CHILD_TRAINING_A_INDEX],
            san9_p1_easy_generated_auxiliaries[SAN9_P1_EASY_AUX_CHILD_TRAINING_A_INDEX].original, 4u)
        && bytes_equal(
            snapshot_b->auxiliary_bytes[SAN9_P1_EASY_AUX_CHILD_TRAINING_B_INDEX],
            san9_p1_easy_generated_auxiliaries[SAN9_P1_EASY_AUX_CHILD_TRAINING_B_INDEX].original, 4u);
    training_installed = training_original
        || (bytes_equal(
                snapshot_b->auxiliary_bytes[SAN9_P1_EASY_AUX_CHILD_TRAINING_A_INDEX],
                san9_p1_easy_generated_auxiliaries[SAN9_P1_EASY_AUX_CHILD_TRAINING_A_INDEX].installed_b, 4u)
            && bytes_equal(
                snapshot_b->auxiliary_bytes[SAN9_P1_EASY_AUX_CHILD_TRAINING_B_INDEX],
                san9_p1_easy_generated_auxiliaries[SAN9_P1_EASY_AUX_CHILD_TRAINING_B_INDEX].installed_b, 4u));
    if (!training_installed) {
        unknown_auxiliary = 1;
        report->detail_flags |= SAN9_P1_EASY_DETAIL_TRAINING_PAIR;
        record_first_failure(report, SAN9_P1_EASY_REDIRECT_CAPACITY
            + SAN9_P1_EASY_AUX_CHILD_TRAINING_A_INDEX);
    }

    for (index = SAN9_P1_EASY_AUX_MAX_CORPS_FOOD_A_INDEX;
         index <= SAN9_P1_EASY_AUX_AI_COUNTER_INDEX; ++index) {
        const San9P1EasyGeneratedAuxiliary *definition = &san9_p1_easy_generated_auxiliaries[index];
        int is_original = bytes_equal(snapshot_b->auxiliary_bytes[index], definition->original, 4u);
        int is_installed = bytes_equal(snapshot_b->auxiliary_bytes[index], definition->installed_a, 4u);
        fixed_original = fixed_original && is_original;
        fixed_installed = fixed_installed && is_installed;
        if (!is_original && !is_installed) {
            unknown_auxiliary = 1;
            report->detail_flags |= SAN9_P1_EASY_DETAIL_FIXED_AUXILIARY;
            record_first_failure(report, SAN9_P1_EASY_REDIRECT_CAPACITY + index);
        }
    }
    write_u32_le(expected_hwnd, binding->game_hwnd);
    hwnd_original = bytes_zero(
        snapshot_b->auxiliary_bytes[SAN9_P1_EASY_AUX_HWND_INDEX], 4u);
    hwnd_installed = bytes_equal(
        snapshot_b->auxiliary_bytes[SAN9_P1_EASY_AUX_HWND_INDEX], expected_hwnd, 4u);
    if (!hwnd_original && !hwnd_installed) {
        unknown_auxiliary = 1;
        report->detail_flags |= SAN9_P1_EASY_DETAIL_HWND;
        record_first_failure(report, SAN9_P1_EASY_REDIRECT_CAPACITY
            + SAN9_P1_EASY_AUX_HWND_INDEX);
    }
    page_original = region_covers_page(binding, &snapshot_b->protected_page,
        SAN9_P1_EASY_PAGE_ORIGINAL_PROTECTION);
    page_installed = region_covers_page(binding, &snapshot_b->protected_page,
        SAN9_P1_EASY_PAGE_INSTALLED_PROTECTION);
    if (!page_original && !page_installed) {
        unknown_auxiliary = 1;
        report->detail_flags |= SAN9_P1_EASY_DETAIL_PAGE;
        record_first_failure(report, SAN9_P1_EASY_PAGE_POINT);
    }
    for (index = 0u; index < SAN9_P1_EASY_GENERATED_IDLE_ANCHOR_COUNT; ++index) {
        const San9P1EasyGeneratedIdleAnchor *definition = &san9_p1_easy_generated_idle_anchors[index];
        if (!bytes_equal(snapshot_b->idle_anchor_bytes[index], definition->expected,
            SAN9_P1_EASY_IDLE_BYTE_CAPACITY)) {
            idle_exact = 0;
            report->detail_flags |= index == SAN9_P1_EASY_IDLE_SLOT_INDEX
                ? SAN9_P1_EASY_DETAIL_IDLE_SLOT
                : (index == SAN9_P1_EASY_IDLE_FUNCTION_INDEX
                    ? SAN9_P1_EASY_DETAIL_IDLE_FUNCTION
                    : SAN9_P1_EASY_DETAIL_IDLE_CALLER);
            record_first_failure(report, SAN9_P1_EASY_IDLE_POINT_BASE + index);
        }
    }
    if (!idle_exact) {
        report->state = SAN9_P1_EASY_IDLE_ANCHOR_CONFLICT;
        return report->state;
    }
    if (!padding_exact || report->unknown_redirect_count != 0u || unknown_auxiliary) {
        report->state = SAN9_P1_EASY_UNKNOWN;
        return report->state;
    }
    if (report->original_redirect_count == SAN9_P1_EASY_GENERATED_REDIRECT_COUNT
        && training_original && fixed_original && hwnd_original && page_original) {
        report->state = SAN9_P1_EASY_ORIGINAL;
        return report->state;
    }
    if (report->installed_redirect_count == SAN9_P1_EASY_GENERATED_REDIRECT_COUNT
        && training_installed && fixed_installed && hwnd_installed && page_installed) {
        report->state = SAN9_P1_EASY_INSTALLED;
        report->compatible_for_future_bridge = 1u;
        return report->state;
    }
    report->state = SAN9_P1_EASY_MIXED;
    return report->state;
}

San9P1EasyState san9_p1_easy_verify_callbacks(
    const San9P1EasyBinding *binding,
    const San9P1EasyCallbacks *callbacks,
    San9P1EasyReport *report)
{
    San9P1EasySnapshot snapshot_a;
    San9P1EasySnapshot snapshot_b;
    if (report == NULL) {
        return SAN9_P1_EASY_INVALID_BINDING;
    }
    clear_report(report);
    if (!san9_p1_easy_binding_is_exact(binding)) {
        report->state = SAN9_P1_EASY_INVALID_BINDING;
        report->detail_flags = SAN9_P1_EASY_DETAIL_BINDING;
        return report->state;
    }
    if (!san9_p1_easy_capture(binding, callbacks, &snapshot_a)
        || !san9_p1_easy_capture(binding, callbacks, &snapshot_b)) {
        report->state = SAN9_P1_EASY_CAPTURE_FAILED;
        report->detail_flags = SAN9_P1_EASY_DETAIL_CAPTURE;
        return report->state;
    }
    return san9_p1_easy_verify_buffers(binding, &snapshot_a, &snapshot_b, report);
}

const char *san9_p1_easy_manifest_sha256(void)
{
    return SAN9_P1_EASY_MANIFEST_SHA256;
}

uint32_t san9_p1_easy_owned_write_count(void)
{
    return SAN9_P1_EASY_GENERATED_REDIRECT_COUNT
        + SAN9_P1_EASY_GENERATED_AUXILIARY_COUNT;
}

uint32_t san9_p1_easy_total_point_count(void)
{
    return SAN9_P1_EASY_GENERATED_TOTAL_POINT_COUNT;
}
