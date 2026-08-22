#ifndef SAN9_P1_M2B_DISCOVER_H
#define SAN9_P1_M2B_DISCOVER_H

#include <stdint.h>
#include <wchar.h>

#include "san9_p1_easy_gate.h"
#include "san9_p1_wire.h"

#ifdef __cplusplus
extern "C" {
#endif

#define SAN9_P1_M2B_DISCOVERY_PATH_CAPACITY 260u

typedef enum San9P1M2bDiscoveryStatus {
    SAN9_P1_M2B_DISCOVERY_OK = 0,
    SAN9_P1_M2B_DISCOVERY_INVALID_ARGUMENT = 1,
    SAN9_P1_M2B_DISCOVERY_GAME_MISSING = 2,
    SAN9_P1_M2B_DISCOVERY_GAME_AMBIGUOUS = 3,
    SAN9_P1_M2B_DISCOVERY_GAME_IDENTITY = 4,
    SAN9_P1_M2B_DISCOVERY_LOADER_MISSING = 5,
    SAN9_P1_M2B_DISCOVERY_LOADER_AMBIGUOUS = 6,
    SAN9_P1_M2B_DISCOVERY_LOADER_IDENTITY = 7,
    SAN9_P1_M2B_DISCOVERY_WINDOW_MISSING = 8,
    SAN9_P1_M2B_DISCOVERY_WINDOW_AMBIGUOUS = 9,
    SAN9_P1_M2B_DISCOVERY_MODULE_ENUMERATION = 10,
    SAN9_P1_M2B_DISCOVERY_GAME_MODULE_IDENTITY = 11,
    SAN9_P1_M2B_DISCOVERY_EASY_MODULE_MISSING = 12,
    SAN9_P1_M2B_DISCOVERY_EASY_MODULE_AMBIGUOUS = 13,
    SAN9_P1_M2B_DISCOVERY_EASY_MODULE_IDENTITY = 14,
    SAN9_P1_M2B_DISCOVERY_EASY_CAPTURE = 15,
    SAN9_P1_M2B_DISCOVERY_EASY_NOT_INSTALLED = 16,
    SAN9_P1_M2B_DISCOVERY_GENERATION_CHANGED = 17
} San9P1M2bDiscoveryStatus;

typedef struct San9P1M2bDiscovery {
    uint32_t structure_size;
    San9P1M2bDiscoveryStatus status;
    uint32_t game_pid;
    uint32_t main_thread_id;
    uint32_t game_hwnd;
    uint32_t easy_loader_pid;
    uint32_t helper_pid;
    uint64_t game_generation;
    uint64_t easy_loader_generation;
    uint64_t helper_generation;
    San9P1EasyBinding easy_binding;
    San9P1EasyReport easy_report;
    San9P1EasyMemoryRegion easy_page;
    uint32_t easy_page_allocation_base;
    uint32_t easy_page_allocation_protection;
    uint32_t easy_page_type;
    uint8_t game_file_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t easy_loader_file_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t easy_module_file_digest[SAN9_P1_DIGEST_SIZE];
    uint8_t easy_snapshot_digest[SAN9_P1_DIGEST_SIZE];
    wchar_t game_path[SAN9_P1_M2B_DISCOVERY_PATH_CAPACITY];
    wchar_t easy_loader_path[SAN9_P1_M2B_DISCOVERY_PATH_CAPACITY];
    wchar_t easy_module_path[SAN9_P1_M2B_DISCOVERY_PATH_CAPACITY];
} San9P1M2bDiscovery;

San9P1M2bDiscoveryStatus san9_p1_m2b_discover(
    San9P1M2bDiscovery *output);

const char *san9_p1_m2b_discovery_status_name(
    San9P1M2bDiscoveryStatus status);

int san9_p1_m2b_hash_file(
    const wchar_t *path,
    uint8_t output[SAN9_P1_DIGEST_SIZE]);

#ifdef __cplusplus
}
#endif

#endif
