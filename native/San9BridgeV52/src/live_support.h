#ifndef SAN9_V52_LIVE_SUPPORT_H
#define SAN9_V52_LIVE_SUPPORT_H

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "san9_bridge_v52.h"

typedef struct San9V52TargetProbe {
    uint32_t pid;
    uint32_t thread_id;
    uint32_t hwnd_value;
    uint64_t creation_time;
    wchar_t image_path[1024];
    uint8_t exe_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t context_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t module_inventory_digest[SAN9_PING_DIGEST_SIZE];
    uint8_t module_without_bridge_digest[SAN9_PING_DIGEST_SIZE];
    uint32_t module_count;
    uint32_t module_without_bridge_count;
    uint32_t bridge_module_count;
    uint32_t bridge_module_base;
    uint32_t bridge_module_size;
    wchar_t bridge_module_path[1024];
} San9V52TargetProbe;

const wchar_t *san9_v52_exact_target_path(void);
const uint8_t *san9_v52_exact_target_digest(void);

int san9_v52_hash_file(
    const wchar_t *path,
    uint8_t output[SAN9_PING_DIGEST_SIZE]);
int san9_v52_hash_wide_string(
    const wchar_t *value,
    uint8_t output[SAN9_PING_DIGEST_SIZE]);
int san9_v52_hash_context(
    uint32_t pid,
    uint32_t thread_id,
    uint32_t hwnd_value,
    uint64_t creation_time,
    const uint8_t exe_digest[SAN9_PING_DIGEST_SIZE],
    uint8_t output[SAN9_PING_DIGEST_SIZE]);
int san9_v52_get_process_creation_time(uint32_t pid, uint64_t *creation_time);
int san9_v52_probe_target(
    uint32_t pid,
    uint32_t expected_thread_id,
    uint32_t expected_hwnd,
    uint64_t expected_creation_time,
    San9V52TargetProbe *probe);
int san9_v52_loaded_modules_conflict_free(uint32_t pid);
int san9_v52_local_proxy_files_absent(const wchar_t *image_path);

#endif
