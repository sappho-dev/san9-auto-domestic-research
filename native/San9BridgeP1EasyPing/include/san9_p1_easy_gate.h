#ifndef SAN9_P1_EASY_GATE_H
#define SAN9_P1_EASY_GATE_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define SAN9_P1_EASY_SNAPSHOT_SCHEMA 1u
#define SAN9_P1_EASY_REDIRECT_CAPACITY 32u
#define SAN9_P1_EASY_AUXILIARY_CAPACITY 6u
#define SAN9_P1_EASY_IDLE_ANCHOR_CAPACITY 3u
#define SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY 6u
#define SAN9_P1_EASY_AUXILIARY_BYTE_CAPACITY 4u
#define SAN9_P1_EASY_IDLE_BYTE_CAPACITY 12u
#define SAN9_P1_EASY_TOTAL_POINT_CAPACITY 42u
#define SAN9_P1_EASY_ALL_POINTS_MASK ((UINT64_C(1) << SAN9_P1_EASY_TOTAL_POINT_CAPACITY) - UINT64_C(1))
#define SAN9_P1_EASY_NO_FAILURE_POINT UINT32_MAX

typedef enum San9P1EasyState {
    SAN9_P1_EASY_NOT_INSPECTED = 0,
    SAN9_P1_EASY_INVALID_BINDING = 1,
    SAN9_P1_EASY_CAPTURE_FAILED = 2,
    SAN9_P1_EASY_SNAPSHOT_UNSTABLE = 3,
    SAN9_P1_EASY_ORIGINAL = 4,
    SAN9_P1_EASY_PARTIAL = 5,
    SAN9_P1_EASY_MIXED = 6,
    SAN9_P1_EASY_UNKNOWN = 7,
    SAN9_P1_EASY_IDLE_ANCHOR_CONFLICT = 8,
    SAN9_P1_EASY_INSTALLED = 9
} San9P1EasyState;

enum San9P1EasyDetailFlags {
    SAN9_P1_EASY_DETAIL_NONE = 0u,
    SAN9_P1_EASY_DETAIL_BINDING = 1u << 0,
    SAN9_P1_EASY_DETAIL_CAPTURE = 1u << 1,
    SAN9_P1_EASY_DETAIL_UNSTABLE = 1u << 2,
    SAN9_P1_EASY_DETAIL_SNAPSHOT_SHAPE = 1u << 3,
    SAN9_P1_EASY_DETAIL_REDIRECT = 1u << 4,
    SAN9_P1_EASY_DETAIL_TRAINING_PAIR = 1u << 5,
    SAN9_P1_EASY_DETAIL_FIXED_AUXILIARY = 1u << 6,
    SAN9_P1_EASY_DETAIL_HWND = 1u << 7,
    SAN9_P1_EASY_DETAIL_PAGE = 1u << 8,
    SAN9_P1_EASY_DETAIL_IDLE_SLOT = 1u << 9,
    SAN9_P1_EASY_DETAIL_IDLE_FUNCTION = 1u << 10,
    SAN9_P1_EASY_DETAIL_IDLE_CALLER = 1u << 11,
    SAN9_P1_EASY_DETAIL_NONCANONICAL_PADDING = 1u << 12
};

typedef struct San9P1EasyBinding {
    uint32_t game_image_base;
    uint32_t game_image_size;
    uint32_t easy_image_base;
    uint32_t easy_image_size;
    uint32_t game_hwnd;
} San9P1EasyBinding;

typedef struct San9P1EasyMemoryRegion {
    uint32_t base_address;
    uint32_t region_size;
    uint32_t state;
    uint32_t protection;
} San9P1EasyMemoryRegion;

typedef int (*San9P1EasyReadCallback)(
    void *context,
    uint32_t address,
    uint8_t *output,
    size_t output_size);

typedef int (*San9P1EasyQueryCallback)(
    void *context,
    uint32_t address,
    San9P1EasyMemoryRegion *output);

typedef struct San9P1EasyCallbacks {
    San9P1EasyReadCallback read;
    San9P1EasyQueryCallback query;
    void *context;
} San9P1EasyCallbacks;

typedef struct San9P1EasySnapshot {
    uint32_t schema;
    uint32_t structure_size;
    uint64_t captured_points;
    uint8_t redirect_bytes[SAN9_P1_EASY_REDIRECT_CAPACITY]
        [SAN9_P1_EASY_REDIRECT_BYTE_CAPACITY];
    uint8_t auxiliary_bytes[SAN9_P1_EASY_AUXILIARY_CAPACITY]
        [SAN9_P1_EASY_AUXILIARY_BYTE_CAPACITY];
    San9P1EasyMemoryRegion protected_page;
    uint8_t idle_anchor_bytes[SAN9_P1_EASY_IDLE_ANCHOR_CAPACITY]
        [SAN9_P1_EASY_IDLE_BYTE_CAPACITY];
} San9P1EasySnapshot;

typedef struct San9P1EasyReport {
    San9P1EasyState state;
    uint32_t stable_snapshot;
    uint32_t compatible_for_future_bridge;
    uint32_t installed_redirect_count;
    uint32_t original_redirect_count;
    uint32_t unknown_redirect_count;
    uint32_t detail_flags;
    uint32_t first_failure_point;
} San9P1EasyReport;

int san9_p1_easy_binding_is_exact(const San9P1EasyBinding *binding);

int san9_p1_easy_capture(
    const San9P1EasyBinding *binding,
    const San9P1EasyCallbacks *callbacks,
    San9P1EasySnapshot *output);

San9P1EasyState san9_p1_easy_verify_buffers(
    const San9P1EasyBinding *binding,
    const San9P1EasySnapshot *snapshot_a,
    const San9P1EasySnapshot *snapshot_b,
    San9P1EasyReport *report);

San9P1EasyState san9_p1_easy_verify_callbacks(
    const San9P1EasyBinding *binding,
    const San9P1EasyCallbacks *callbacks,
    San9P1EasyReport *report);

const char *san9_p1_easy_manifest_sha256(void);
uint32_t san9_p1_easy_owned_write_count(void);
uint32_t san9_p1_easy_total_point_count(void);

#ifdef __cplusplus
}
#endif

#endif
