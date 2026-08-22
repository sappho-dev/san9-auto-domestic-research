#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "live_support.h"
#include "sha256.h"

#include <string.h>
#include <wchar.h>

#if SAN9_V52_LIVE_ENABLED == 1
#include "san9_v52_safety_contract.h"
#include <tlhelp32.h>

enum {
    EXACT_TARGET_FILE_SIZE = 2636800,
    EXACT_TARGET_IMAGE_BASE = 0x00400000,
    EXACT_TARGET_IMAGE_SIZE = 0x01759000,
    MAX_INVENTORY_MODULES = 256
};

static const wchar_t EXACT_TARGET_PATH[] =
    L"D:\\" L"\x4E09\x56FD\x5FD7" L"9\\10101749\\San9PK.exe";
static const uint8_t EXACT_TARGET_DIGEST[SAN9_PING_DIGEST_SIZE] = {
    0xd2, 0x07, 0x94, 0xae, 0xff, 0x67, 0x30, 0x1e,
    0xc2, 0xbf, 0x8c, 0x3b, 0xec, 0xb1, 0xe9, 0x94,
    0x4c, 0x68, 0xc6, 0xc0, 0x58, 0xfb, 0xfd, 0x4b,
    0xf0, 0x4e, 0x85, 0x97, 0xf0, 0xe5, 0x02, 0x8d
};

typedef struct WindowProbeContext {
    uint32_t pid;
    uint32_t count;
    uint32_t hwnd_value;
    uint32_t thread_id;
    uint32_t class_window_count;
    uint32_t class_query_failed;
} WindowProbeContext;

typedef struct ModuleInventoryEntry {
    wchar_t name[256];
    wchar_t path[MAX_PATH];
    uint32_t base;
    uint32_t size;
} ModuleInventoryEntry;

typedef struct ModuleInventoryCapture {
    uint8_t digest[SAN9_PING_DIGEST_SIZE];
    uint8_t without_bridge_digest[SAN9_PING_DIGEST_SIZE];
    uint32_t count;
    uint32_t without_bridge_count;
    uint32_t bridge_count;
    uint32_t bridge_base;
    uint32_t bridge_size;
    wchar_t bridge_path[MAX_PATH];
} ModuleInventoryCapture;

static void write_u32(uint8_t *output, uint32_t value)
{
    output[0] = (uint8_t)value;
    output[1] = (uint8_t)(value >> 8);
    output[2] = (uint8_t)(value >> 16);
    output[3] = (uint8_t)(value >> 24);
}

static void write_u64(uint8_t *output, uint64_t value)
{
    unsigned index;
    for (index = 0; index < 8; ++index) {
        output[index] = (uint8_t)(value >> (index * 8U));
    }
}

static BOOL CALLBACK enumerate_target_windows(HWND window, LPARAM parameter)
{
    WindowProbeContext *context = (WindowProbeContext *)parameter;
    wchar_t class_name[256];
    DWORD pid = 0;
    DWORD thread_id;
    int class_length;
    class_length = GetClassNameW(
        window,
        class_name,
        (int)(sizeof(class_name) / sizeof(class_name[0])));
    if (class_length <= 0) {
        context->class_query_failed = 1U;
        return TRUE;
    }
    if (wcscmp(class_name, SAN9_V52_EXACT_WINDOW_CLASS) != 0) {
        return TRUE;
    }
    ++context->class_window_count;
    thread_id = GetWindowThreadProcessId(window, &pid);
    if (pid == context->pid && thread_id != 0U) {
        ++context->count;
        context->hwnd_value = (uint32_t)(uintptr_t)window;
        context->thread_id = thread_id;
    }
    return TRUE;
}

static uint32_t read_u32(const uint8_t *value)
{
    return (uint32_t)value[0]
        | ((uint32_t)value[1] << 8)
        | ((uint32_t)value[2] << 16)
        | ((uint32_t)value[3] << 24);
}

static int same_file_identity(
    const BY_HANDLE_FILE_INFORMATION *left,
    const BY_HANDLE_FILE_INFORMATION *right)
{
    return left->dwVolumeSerialNumber == right->dwVolumeSerialNumber
        && left->nFileIndexHigh == right->nFileIndexHigh
        && left->nFileIndexLow == right->nFileIndexLow
        && left->nFileSizeHigh == right->nFileSizeHigh
        && left->nFileSizeLow == right->nFileSizeLow
        && left->ftLastWriteTime.dwHighDateTime
            == right->ftLastWriteTime.dwHighDateTime
        && left->ftLastWriteTime.dwLowDateTime
            == right->ftLastWriteTime.dwLowDateTime;
}

static int inspect_exact_target_file(
    const wchar_t *path,
    uint8_t output[SAN9_PING_DIGEST_SIZE])
{
    HANDLE file;
    uint8_t dos[64];
    uint8_t pe_header[128];
    uint8_t buffer[4096];
    DWORD read_count;
    uint32_t pe_offset;
    LARGE_INTEGER offset;
    LARGE_INTEGER file_size;
    BY_HANDLE_FILE_INFORMATION identity_before;
    BY_HANDLE_FILE_INFORMATION identity_after;
    San9Sha256Context hash;
    int result = 0;

    file = CreateFileW(
        path,
        GENERIC_READ,
        FILE_SHARE_READ,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN,
        NULL);
    if (file == INVALID_HANDLE_VALUE) {
        return 0;
    }
    memset(output, 0, SAN9_PING_DIGEST_SIZE);
    memset(&identity_before, 0, sizeof(identity_before));
    memset(&identity_after, 0, sizeof(identity_after));
    if (GetFileInformationByHandle(file, &identity_before)
        && GetFileSizeEx(file, &file_size)
        && file_size.QuadPart == EXACT_TARGET_FILE_SIZE
        && ReadFile(file, dos, sizeof(dos), &read_count, NULL)
        && read_count == sizeof(dos) && dos[0] == 'M' && dos[1] == 'Z') {
        pe_offset = read_u32(dos + 0x3c);
        offset.QuadPart = pe_offset;
        if (pe_offset >= sizeof(dos)
            && pe_offset <= 0x10000000U
            && SetFilePointerEx(file, offset, NULL, FILE_BEGIN)
            && ReadFile(file, pe_header, sizeof(pe_header), &read_count, NULL)
            && read_count == sizeof(pe_header)
            && pe_header[0] == 'P' && pe_header[1] == 'E'
            && pe_header[2] == 0 && pe_header[3] == 0
            && pe_header[4] == 0x4c && pe_header[5] == 0x01
            && pe_header[24] == 0x0b && pe_header[25] == 0x01
            && read_u32(pe_header + 52) == EXACT_TARGET_IMAGE_BASE
            && read_u32(pe_header + 80) == EXACT_TARGET_IMAGE_SIZE) {
            offset.QuadPart = 0;
            if (SetFilePointerEx(file, offset, NULL, FILE_BEGIN)) {
                san9_sha256_initialize(&hash);
                for (;;) {
                    if (!ReadFile(file, buffer, sizeof(buffer), &read_count, NULL)) {
                        break;
                    }
                    if (read_count == 0U) {
                        if (GetFileInformationByHandle(file, &identity_after)
                            && same_file_identity(
                                &identity_before,
                                &identity_after)) {
                            san9_sha256_finish(&hash, output);
                            result = san9_constant_time_equal(
                                output,
                                EXACT_TARGET_DIGEST,
                                SAN9_PING_DIGEST_SIZE);
                        }
                        break;
                    }
                    san9_sha256_update(&hash, buffer, read_count);
                }
                if (!result) {
                    san9_secure_zero(&hash, sizeof(hash));
                }
            }
        }
    }
    san9_secure_zero(buffer, sizeof(buffer));
    CloseHandle(file);
    return result;
}

static int path_directory(const wchar_t *path, wchar_t *output, size_t capacity)
{
    const wchar_t *separator;
    size_t length;
    if (path == NULL || output == NULL || capacity == 0U) {
        return 0;
    }
    separator = wcsrchr(path, L'\\');
    if (separator == NULL) {
        return 0;
    }
    length = (size_t)(separator - path);
    if (length == 0U || length + 1U > capacity) {
        return 0;
    }
    memcpy(output, path, length * sizeof(wchar_t));
    output[length] = L'\0';
    return 1;
}

static int path_is_under_directory(
    const wchar_t *path,
    const wchar_t *directory)
{
    size_t path_length;
    size_t directory_length;
    const wchar_t *child;
    if (path == NULL || directory == NULL) {
        return 0;
    }
    path_length = wcslen(path);
    directory_length = wcslen(directory);
    /* Check both indexed positions before reading path[directory_length]. */
    if (directory_length == 0U || path_length <= directory_length + 1U) {
        return 0;
    }
    if (_wcsnicmp(path, directory, directory_length) != 0
        || path[directory_length] != L'\\') {
        return 0;
    }
    child = path + directory_length + 1U;
    return *child != L'\0' && wcschr(child, L'\\') == NULL;
}

const wchar_t *san9_v52_exact_target_path(void)
{
    return EXACT_TARGET_PATH;
}

const uint8_t *san9_v52_exact_target_digest(void)
{
    return EXACT_TARGET_DIGEST;
}

int san9_v52_hash_file(
    const wchar_t *path,
    uint8_t output[SAN9_PING_DIGEST_SIZE])
{
    HANDLE file;
    San9Sha256Context hash;
    uint8_t buffer[4096];
    DWORD read_count;
    int result = 0;
    if (path == NULL || output == NULL) {
        return 0;
    }
    file = CreateFileW(
        path,
        GENERIC_READ,
        FILE_SHARE_READ,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN,
        NULL);
    if (file == INVALID_HANDLE_VALUE) {
        return 0;
    }
    san9_sha256_initialize(&hash);
    for (;;) {
        if (!ReadFile(file, buffer, sizeof(buffer), &read_count, NULL)) {
            break;
        }
        if (read_count == 0U) {
            san9_sha256_finish(&hash, output);
            result = 1;
            break;
        }
        san9_sha256_update(&hash, buffer, read_count);
    }
    san9_secure_zero(buffer, sizeof(buffer));
    if (!result) {
        san9_secure_zero(&hash, sizeof(hash));
    }
    CloseHandle(file);
    return result;
}

int san9_v52_hash_wide_string(
    const wchar_t *value,
    uint8_t output[SAN9_PING_DIGEST_SIZE])
{
    San9Sha256Context hash;
    size_t length;
    if (value == NULL || output == NULL) {
        return 0;
    }
    length = wcslen(value);
    if (length == 0U || length >= SAN9_V52_MAPPING_NAME_MAX) {
        return 0;
    }
    san9_sha256_initialize(&hash);
    san9_sha256_update(
        &hash,
        (const uint8_t *)value,
        length * sizeof(wchar_t));
    san9_sha256_finish(&hash, output);
    return 1;
}

int san9_v52_hash_context(
    uint32_t pid,
    uint32_t thread_id,
    uint32_t hwnd_value,
    uint64_t creation_time,
    const uint8_t exe_digest[SAN9_PING_DIGEST_SIZE],
    uint8_t output[SAN9_PING_DIGEST_SIZE])
{
    uint8_t material[52];
    San9Sha256Context hash;
    if (pid == 0U || thread_id == 0U || hwnd_value == 0U
        || creation_time == 0U || exe_digest == NULL || output == NULL) {
        return 0;
    }
    write_u32(material, pid);
    write_u32(material + 4, thread_id);
    write_u32(material + 8, hwnd_value);
    write_u64(material + 12, creation_time);
    memcpy(material + 20, exe_digest, SAN9_PING_DIGEST_SIZE);
    san9_sha256_initialize(&hash);
    san9_sha256_update(&hash, material, sizeof(material));
    san9_sha256_finish(&hash, output);
    san9_secure_zero(material, sizeof(material));
    return 1;
}

int san9_v52_get_process_creation_time(uint32_t pid, uint64_t *creation_time)
{
    HANDLE process;
    FILETIME created;
    FILETIME exited;
    FILETIME kernel;
    FILETIME user;
    ULARGE_INTEGER combined;
    if (pid == 0U || creation_time == NULL) {
        return 0;
    }
    process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (process == NULL) {
        return 0;
    }
    if (!GetProcessTimes(process, &created, &exited, &kernel, &user)) {
        CloseHandle(process);
        return 0;
    }
    CloseHandle(process);
    combined.LowPart = created.dwLowDateTime;
    combined.HighPart = created.dwHighDateTime;
    *creation_time = combined.QuadPart;
    return *creation_time != 0U;
}

static int known_conflict_processes_absent(void)
{
    HANDLE snapshot;
    PROCESSENTRY32W process;
    size_t index;
    int result = 1;
    snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) {
        return 0;
    }
    memset(&process, 0, sizeof(process));
    process.dwSize = sizeof(process);
    if (!Process32FirstW(snapshot, &process)) {
        CloseHandle(snapshot);
        return 0;
    }
    do {
        for (index = 0; index < SAN9_V52_CONFLICT_PROCESS_COUNT; ++index) {
            if (_wcsicmp(
                    process.szExeFile,
                    SAN9_V52_CONFLICT_PROCESSES[index]) == 0) {
                result = 0;
            }
        }
        if (!result) {
            break;
        }
        process.dwSize = sizeof(process);
    } while (Process32NextW(snapshot, &process));
    if (result && GetLastError() != ERROR_NO_MORE_FILES) {
        result = 0;
    }
    CloseHandle(snapshot);
    return result;
}

static int inventory_compare(
    const ModuleInventoryEntry *left,
    const ModuleInventoryEntry *right)
{
    int compared = _wcsicmp(left->path, right->path);
    if (compared != 0) {
        return compared;
    }
    compared = _wcsicmp(left->name, right->name);
    if (compared != 0) {
        return compared;
    }
    if (left->base != right->base) {
        return left->base < right->base ? -1 : 1;
    }
    if (left->size != right->size) {
        return left->size < right->size ? -1 : 1;
    }
    return 0;
}

static void sort_inventory(ModuleInventoryEntry *entries, uint32_t count)
{
    uint32_t index;
    for (index = 1U; index < count; ++index) {
        ModuleInventoryEntry value = entries[index];
        uint32_t cursor = index;
        while (cursor != 0U
            && inventory_compare(&value, &entries[cursor - 1U]) < 0) {
            entries[cursor] = entries[cursor - 1U];
            --cursor;
        }
        entries[cursor] = value;
    }
}

static int capture_module_inventory(
    uint32_t pid,
    ModuleInventoryCapture *capture)
{
    HANDLE snapshot = INVALID_HANDLE_VALUE;
    MODULEENTRY32W module;
    ModuleInventoryEntry *entries = NULL;
    wchar_t game_directory[1024];
    San9Sha256Context hash;
    unsigned retry;
    uint32_t count = 0U;
    uint32_t main_count = 0U;
    uint32_t without_bridge_count = 0U;
    size_t index;
    int result = 1;

    if (pid == 0U || capture == NULL
        || !path_directory(EXACT_TARGET_PATH, game_directory,
            sizeof(game_directory) / sizeof(game_directory[0]))) {
        return 0;
    }
    memset(capture, 0, sizeof(*capture));
    entries = (ModuleInventoryEntry *)HeapAlloc(
        GetProcessHeap(), HEAP_ZERO_MEMORY,
        sizeof(ModuleInventoryEntry) * MAX_INVENTORY_MODULES);
    if (entries == NULL) {
        return 0;
    }
    for (retry = 0; retry < 4U; ++retry) {
        snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
        if (snapshot != INVALID_HANDLE_VALUE || GetLastError() != ERROR_BAD_LENGTH) {
            break;
        }
    }
    if (snapshot == INVALID_HANDLE_VALUE) {
        HeapFree(GetProcessHeap(), 0, entries);
        return 0;
    }
    memset(&module, 0, sizeof(module));
    module.dwSize = sizeof(module);
    if (!Module32FirstW(snapshot, &module)) {
        CloseHandle(snapshot);
        HeapFree(GetProcessHeap(), 0, entries);
        return 0;
    }
    do {
        if (count >= MAX_INVENTORY_MODULES
            || module.szModule[0] == L'\0' || module.szExePath[0] == L'\0') {
            result = 0;
            break;
        }
        wcsncpy(entries[count].name, module.szModule,
            (sizeof(entries[count].name) / sizeof(entries[count].name[0])) - 1U);
        wcsncpy(entries[count].path, module.szExePath,
            (sizeof(entries[count].path) / sizeof(entries[count].path[0])) - 1U);
        entries[count].base = (uint32_t)(uintptr_t)module.modBaseAddr;
        entries[count].size = module.modBaseSize;
        ++count;

        if (_wcsicmp(module.szModule, L"San9BridgeV52Live.dll") == 0) {
            ++capture->bridge_count;
            if (capture->bridge_count != 1U) {
                result = 0;
            } else {
                wcsncpy(
                    capture->bridge_path,
                    module.szExePath,
                    (sizeof(capture->bridge_path)
                        / sizeof(capture->bridge_path[0])) - 1U);
                capture->bridge_base =
                    (uint32_t)(uintptr_t)module.modBaseAddr;
                capture->bridge_size = module.modBaseSize;
            }
        }

        if (_wcsicmp(module.szModule, L"San9PK.exe") == 0
            && _wcsicmp(module.szExePath, EXACT_TARGET_PATH) == 0
            && (uint32_t)(uintptr_t)module.modBaseAddr == EXACT_TARGET_IMAGE_BASE
            && module.modBaseSize == EXACT_TARGET_IMAGE_SIZE) {
            ++main_count;
        } else if (_wcsicmp(module.szModule, L"San9PK.exe") == 0
            || _wcsicmp(module.szExePath, EXACT_TARGET_PATH) == 0
            || (uint32_t)(uintptr_t)module.modBaseAddr == EXACT_TARGET_IMAGE_BASE) {
            result = 0;
        }
        for (index = 0; index < SAN9_V52_CONFLICT_MODULE_COUNT; ++index) {
            if (_wcsicmp(
                    module.szModule,
                    SAN9_V52_CONFLICT_MODULES[index]) == 0) {
                result = 0;
            }
        }
        for (index = 0; index < SAN9_V52_LOCAL_PROXY_COUNT; ++index) {
            if (_wcsicmp(module.szModule, SAN9_V52_LOCAL_PROXIES[index]) == 0
                && path_is_under_directory(module.szExePath, game_directory)) {
                result = 0;
            }
        }
        if (!result) {
            break;
        }
        module.dwSize = sizeof(module);
    } while (Module32NextW(snapshot, &module));
    if (GetLastError() != ERROR_NO_MORE_FILES) {
        result = 0;
    }
    CloseHandle(snapshot);
    if (main_count != 1U || count == 0U) {
        result = 0;
    }
    if (result) {
        sort_inventory(entries, count);
        san9_sha256_initialize(&hash);
        san9_sha256_update(
            &hash,
            (const uint8_t *)entries,
            sizeof(ModuleInventoryEntry) * count);
        san9_sha256_finish(&hash, capture->digest);
        san9_sha256_initialize(&hash);
        for (index = 0; index < count; ++index) {
            if (_wcsicmp(
                    entries[index].name,
                    L"San9BridgeV52Live.dll") != 0) {
                san9_sha256_update(
                    &hash,
                    (const uint8_t *)&entries[index],
                    sizeof(entries[index]));
                ++without_bridge_count;
            }
        }
        san9_sha256_finish(&hash, capture->without_bridge_digest);
        capture->count = count;
        capture->without_bridge_count = without_bridge_count;
    }
    san9_secure_zero(entries, sizeof(ModuleInventoryEntry) * MAX_INVENTORY_MODULES);
    HeapFree(GetProcessHeap(), 0, entries);
    return result;
}

static int module_captures_equal(
    const ModuleInventoryCapture *left,
    const ModuleInventoryCapture *right)
{
    return left->count == right->count
        && left->without_bridge_count == right->without_bridge_count
        && left->bridge_count == right->bridge_count
        && left->bridge_base == right->bridge_base
        && left->bridge_size == right->bridge_size
        && _wcsicmp(left->bridge_path, right->bridge_path) == 0
        && san9_constant_time_equal(
            left->digest,
            right->digest,
            SAN9_PING_DIGEST_SIZE)
        && san9_constant_time_equal(
            left->without_bridge_digest,
            right->without_bridge_digest,
            SAN9_PING_DIGEST_SIZE);
}

int san9_v52_loaded_modules_conflict_free(uint32_t pid)
{
    ModuleInventoryCapture first;
    ModuleInventoryCapture second;
    return capture_module_inventory(pid, &first)
        && capture_module_inventory(pid, &second)
        && module_captures_equal(&first, &second);
}

int san9_v52_local_proxy_files_absent(const wchar_t *image_path)
{
    wchar_t directory[1024];
    wchar_t candidate[1200];
    size_t directory_length;
    size_t name_length;
    size_t index;
    DWORD attributes;
    DWORD error;
    if (!path_directory(image_path, directory, sizeof(directory) / sizeof(directory[0]))) {
        return 0;
    }
    directory_length = wcslen(directory);
    for (index = 0; index < SAN9_V52_LOCAL_PROXY_COUNT; ++index) {
        name_length = wcslen(SAN9_V52_LOCAL_PROXIES[index]);
        if (directory_length + 1U + name_length + 1U
            > sizeof(candidate) / sizeof(candidate[0])) {
            return 0;
        }
        memcpy(candidate, directory, directory_length * sizeof(wchar_t));
        candidate[directory_length] = L'\\';
        memcpy(
            candidate + directory_length + 1U,
            SAN9_V52_LOCAL_PROXIES[index],
            (name_length + 1U) * sizeof(wchar_t));
        SetLastError(ERROR_SUCCESS);
        attributes = GetFileAttributesW(candidate);
        if (attributes != INVALID_FILE_ATTRIBUTES) {
            return 0;
        }
        error = GetLastError();
        if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND) {
            return 0;
        }
    }
    return 1;
}

int san9_v52_probe_target(
    uint32_t pid,
    uint32_t expected_thread_id,
    uint32_t expected_hwnd,
    uint64_t expected_creation_time,
    San9V52TargetProbe *probe)
{
    HANDLE process;
    DWORD path_length;
    DWORD current_session;
    DWORD target_session;
    WindowProbeContext windows;
    uint64_t creation_time;
    ModuleInventoryCapture first_inventory;
    ModuleInventoryCapture second_inventory;

    if (pid == 0U || expected_thread_id == 0U || expected_hwnd == 0U
        || expected_creation_time == 0U || probe == NULL) {
        return SAN9_V52_INVALID;
    }
    memset(probe, 0, sizeof(*probe));
    process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (process == NULL) {
        return SAN9_V52_TARGET_FAILED;
    }
    path_length = (DWORD)(sizeof(probe->image_path) / sizeof(probe->image_path[0]));
    if (!QueryFullProcessImageNameW(process, 0, probe->image_path, &path_length)
        || path_length == 0U || path_length >= sizeof(probe->image_path) / sizeof(wchar_t)) {
        CloseHandle(process);
        return SAN9_V52_TARGET_FAILED;
    }
    probe->image_path[path_length] = L'\0';
    CloseHandle(process);

    if (_wcsicmp(probe->image_path, EXACT_TARGET_PATH) != 0
        || !inspect_exact_target_file(probe->image_path, probe->exe_digest)) {
        return SAN9_V52_TARGET_FAILED;
    }
    if (!san9_v52_get_process_creation_time(pid, &creation_time)
        || creation_time != expected_creation_time) {
        return SAN9_V52_GENERATION_FAILED;
    }
    if (!ProcessIdToSessionId(GetCurrentProcessId(), &current_session)
        || !ProcessIdToSessionId(pid, &target_session)
        || current_session != target_session) {
        return SAN9_V52_TARGET_FAILED;
    }

    memset(&windows, 0, sizeof(windows));
    windows.pid = pid;
    if (!EnumWindows(enumerate_target_windows, (LPARAM)&windows)
        || windows.class_query_failed != 0U
        || windows.class_window_count != 1U
        || windows.count != 1U
        || windows.hwnd_value != expected_hwnd
        || windows.thread_id != expected_thread_id) {
        return SAN9_V52_THREAD_FAILED;
    }
    if (!known_conflict_processes_absent()
        || !capture_module_inventory(pid, &first_inventory)
        || !capture_module_inventory(pid, &second_inventory)
        || !module_captures_equal(&first_inventory, &second_inventory)
        || !san9_v52_local_proxy_files_absent(probe->image_path)) {
        return SAN9_V52_CONFLICT_FAILED;
    }
    if (!san9_v52_hash_context(
            pid,
            expected_thread_id,
            expected_hwnd,
            creation_time,
            probe->exe_digest,
            probe->context_digest)) {
        return SAN9_V52_INTERNAL_FAILED;
    }
    probe->pid = pid;
    probe->thread_id = expected_thread_id;
    probe->hwnd_value = expected_hwnd;
    probe->creation_time = creation_time;
    memcpy(
        probe->module_inventory_digest,
        first_inventory.digest,
        SAN9_PING_DIGEST_SIZE);
    memcpy(
        probe->module_without_bridge_digest,
        first_inventory.without_bridge_digest,
        SAN9_PING_DIGEST_SIZE);
    probe->module_count = first_inventory.count;
    probe->module_without_bridge_count = first_inventory.without_bridge_count;
    probe->bridge_module_count = first_inventory.bridge_count;
    probe->bridge_module_base = first_inventory.bridge_base;
    probe->bridge_module_size = first_inventory.bridge_size;
    wcsncpy(
        probe->bridge_module_path,
        first_inventory.bridge_path,
        (sizeof(probe->bridge_module_path)
            / sizeof(probe->bridge_module_path[0])) - 1U);
    return SAN9_V52_OK;
}

#else

static const wchar_t OFFLINE_PATH[] = L"";
static const uint8_t OFFLINE_DIGEST[SAN9_PING_DIGEST_SIZE] = {0};

const wchar_t *san9_v52_exact_target_path(void) { return OFFLINE_PATH; }
const uint8_t *san9_v52_exact_target_digest(void) { return OFFLINE_DIGEST; }
int san9_v52_hash_file(const wchar_t *path, uint8_t output[SAN9_PING_DIGEST_SIZE])
{ (void)path; (void)output; return 0; }
int san9_v52_hash_wide_string(const wchar_t *value, uint8_t output[SAN9_PING_DIGEST_SIZE])
{ (void)value; (void)output; return 0; }
int san9_v52_hash_context(uint32_t pid, uint32_t thread_id, uint32_t hwnd_value,
    uint64_t creation_time, const uint8_t exe_digest[SAN9_PING_DIGEST_SIZE],
    uint8_t output[SAN9_PING_DIGEST_SIZE])
{ (void)pid; (void)thread_id; (void)hwnd_value; (void)creation_time;
  (void)exe_digest; (void)output; return 0; }
int san9_v52_get_process_creation_time(uint32_t pid, uint64_t *creation_time)
{ (void)pid; (void)creation_time; return 0; }
int san9_v52_probe_target(uint32_t pid, uint32_t thread_id, uint32_t hwnd_value,
    uint64_t creation_time, San9V52TargetProbe *probe)
{ (void)pid; (void)thread_id; (void)hwnd_value; (void)creation_time;
  (void)probe; return SAN9_V52_OFFLINE_ONLY; }
int san9_v52_loaded_modules_conflict_free(uint32_t pid) { (void)pid; return 0; }
int san9_v52_local_proxy_files_absent(const wchar_t *path) { (void)path; return 0; }

#endif
