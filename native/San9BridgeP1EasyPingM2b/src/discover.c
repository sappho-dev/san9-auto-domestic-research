#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>

#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <wchar.h>

#include "discover.h"
#include "easy_page_normalization.h"
#include "sha256.h"
#include "san9_p1_easy_manifest.gen.h"

#define GAME_FILE_SIZE UINT64_C(2636800)
#define LOADER_FILE_SIZE UINT64_C(24576)
#define EASY_FILE_SIZE UINT64_C(32768)

static const wchar_t EXPECTED_GAME_PATH[] =
    L"D:\\\x4E09\x56FD\x5FD7" L"9\\10101749\\San9PK.exe";
static const wchar_t EXPECTED_LOADER_PATH[] =
    L"D:\\\x4E09\x56FD\x5FD7" L"9\\10101749\\San9PKEasy.exe";
static const wchar_t EXPECTED_EASY_PATH[] =
    L"D:\\\x4E09\x56FD\x5FD7" L"9\\10101749\\Easy.dll";

static const char GAME_SHA[] =
    "D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028";
static const char LOADER_SHA[] =
    "CDACA1477EDB5A3BD79BDA8540E19837FC3F9170965D972E8E48FB24CAE21C07";
static const char EASY_SHA[] =
    "E8BA3A603F6B0E7AF8A246DA0FE5CBDBA86AD9B77C5A507DD86159EF6C74A3F0";

typedef struct ProcessCandidate {
    uint32_t pid;
    uint32_t matching_name_count;
    uint64_t generation;
    HANDLE handle;
    wchar_t path[SAN9_P1_M2B_DISCOVERY_PATH_CAPACITY];
    uint8_t digest[SAN9_P1_DIGEST_SIZE];
} ProcessCandidate;

typedef struct WindowSearch {
    uint32_t pid;
    uint32_t count;
    HWND hwnd;
    uint32_t tid;
} WindowSearch;

typedef struct RemoteContext {
    HANDLE process;
} RemoteContext;

static int hex_value(char value)
{
    if (value >= '0' && value <= '9') {
        return value - '0';
    }
    if (value >= 'A' && value <= 'F') {
        return value - 'A' + 10;
    }
    if (value >= 'a' && value <= 'f') {
        return value - 'a' + 10;
    }
    return -1;
}

static int parse_digest(const char text[65], uint8_t output[SAN9_P1_DIGEST_SIZE])
{
    size_t index;
    if (text == NULL || output == NULL || text[64] != '\0') {
        return 0;
    }
    for (index = 0u; index < SAN9_P1_DIGEST_SIZE; ++index) {
        int high = hex_value(text[index * 2u]);
        int low = hex_value(text[index * 2u + 1u]);
        if (high < 0 || low < 0) {
            return 0;
        }
        output[index] = (uint8_t)((high << 4) | low);
    }
    return 1;
}

static int path_equal(const wchar_t *left, const wchar_t *right)
{
    return left != NULL && right != NULL
        && CompareStringOrdinal(left, -1, right, -1, TRUE) == CSTR_EQUAL;
}

static uint64_t filetime_value(const FILETIME *value)
{
    return ((uint64_t)value->dwHighDateTime << 32u) | value->dwLowDateTime;
}

static int process_generation(HANDLE process, uint64_t *output)
{
    FILETIME created;
    FILETIME exited;
    FILETIME kernel;
    FILETIME user;
    if (process == NULL || output == NULL
        || !GetProcessTimes(process, &created, &exited, &kernel, &user)) {
        return 0;
    }
    *output = filetime_value(&created);
    return *output != 0u;
}

int san9_p1_m2b_hash_file(
    const wchar_t *path,
    uint8_t output[SAN9_P1_DIGEST_SIZE])
{
    HANDLE file = INVALID_HANDLE_VALUE;
    uint8_t buffer[4096];
    San9P1Sha256Context sha;
    DWORD read = 0u;
    int ok = 0;
    if (path == NULL || output == NULL) {
        return 0;
    }
    file = CreateFileW(path, GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) {
        goto cleanup;
    }
    san9_p1_sha256_initialize(&sha);
    for (;;) {
        if (!ReadFile(file, buffer, sizeof(buffer), &read, NULL)) {
            goto cleanup;
        }
        if (read == 0u) {
            break;
        }
        san9_p1_sha256_update(&sha, buffer, read);
    }
    san9_p1_sha256_finish(&sha, output);
    ok = 1;
cleanup:
    SecureZeroMemory(buffer, sizeof(buffer));
    if (!ok) {
        SecureZeroMemory(output, SAN9_P1_DIGEST_SIZE);
    }
    if (file != INVALID_HANDLE_VALUE) {
        (void)CloseHandle(file);
    }
    return ok;
}

static int hash_exact_file(
    const wchar_t *path,
    uint64_t expected_size,
    const char expected_sha[65],
    uint8_t output[SAN9_P1_DIGEST_SIZE])
{
    HANDLE file = INVALID_HANDLE_VALUE;
    LARGE_INTEGER size;
    uint8_t expected[SAN9_P1_DIGEST_SIZE];
    int ok = 0;
    if (path == NULL || output == NULL || !parse_digest(expected_sha, expected)) {
        return 0;
    }
    file = CreateFileW(path, FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE || !GetFileSizeEx(file, &size)
        || size.QuadPart < 0 || (uint64_t)size.QuadPart != expected_size) {
        goto cleanup;
    }
    ok = san9_p1_m2b_hash_file(path, output)
        && san9_p1_constant_time_equal(output, expected, sizeof(expected));
cleanup:
    SecureZeroMemory(expected, sizeof(expected));
    if (!ok) {
        SecureZeroMemory(output, SAN9_P1_DIGEST_SIZE);
    }
    if (file != INVALID_HANDLE_VALUE) {
        (void)CloseHandle(file);
    }
    return ok;
}

static int query_process_path(HANDLE process, wchar_t output[260])
{
    DWORD length = SAN9_P1_M2B_DISCOVERY_PATH_CAPACITY;
    if (process == NULL || output == NULL
        || !QueryFullProcessImageNameW(process, 0u, output, &length)
        || length == 0u || length >= SAN9_P1_M2B_DISCOVERY_PATH_CAPACITY) {
        return 0;
    }
    output[length] = L'\0';
    return 1;
}

static San9P1M2bDiscoveryStatus find_exact_process(
    const wchar_t *name,
    const wchar_t *expected_path,
    uint64_t expected_size,
    const char expected_sha[65],
    San9P1M2bDiscoveryStatus missing,
    San9P1M2bDiscoveryStatus ambiguous,
    San9P1M2bDiscoveryStatus identity,
    ProcessCandidate *output)
{
    HANDLE snapshot = INVALID_HANDLE_VALUE;
    PROCESSENTRY32W entry;
    ProcessCandidate candidate;
    memset(&candidate, 0, sizeof(candidate));
    candidate.handle = NULL;
    snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0u);
    if (snapshot == INVALID_HANDLE_VALUE) {
        return identity;
    }
    memset(&entry, 0, sizeof(entry));
    entry.dwSize = sizeof(entry);
    if (Process32FirstW(snapshot, &entry)) {
        do {
            if (CompareStringOrdinal(entry.szExeFile, -1, name, -1, TRUE)
                    == CSTR_EQUAL) {
                HANDLE process;
                ++candidate.matching_name_count;
                if (candidate.matching_name_count != 1u) {
                    continue;
                }
                process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ,
                    FALSE, entry.th32ProcessID);
                if (process == NULL
                    || !query_process_path(process, candidate.path)
                    || !path_equal(candidate.path, expected_path)
                    || !process_generation(process, &candidate.generation)
                    || !hash_exact_file(candidate.path, expected_size,
                        expected_sha, candidate.digest)) {
                    if (process != NULL) {
                        (void)CloseHandle(process);
                    }
                    candidate.handle = NULL;
                } else {
                    candidate.handle = process;
                    candidate.pid = entry.th32ProcessID;
                }
            }
        } while (Process32NextW(snapshot, &entry));
    }
    (void)CloseHandle(snapshot);
    if (candidate.matching_name_count == 0u) {
        return missing;
    }
    if (candidate.matching_name_count != 1u) {
        if (candidate.handle != NULL) {
            (void)CloseHandle(candidate.handle);
        }
        return ambiguous;
    }
    if (candidate.handle == NULL) {
        return identity;
    }
    *output = candidate;
    return SAN9_P1_M2B_DISCOVERY_OK;
}

static BOOL CALLBACK find_window_callback(HWND hwnd, LPARAM parameter)
{
    WindowSearch *search = (WindowSearch *)(uintptr_t)parameter;
    wchar_t class_name[64];
    DWORD pid = 0u;
    DWORD tid;
    if (search == NULL || GetClassNameW(hwnd, class_name,
            (int)(sizeof(class_name) / sizeof(class_name[0]))) <= 0
        || CompareStringOrdinal(class_name, -1, L"KOEI_SAN9WINDOW", -1, FALSE)
            != CSTR_EQUAL) {
        return TRUE;
    }
    tid = GetWindowThreadProcessId(hwnd, &pid);
    if (pid == search->pid && tid != 0u) {
        ++search->count;
        search->hwnd = hwnd;
        search->tid = tid;
    }
    return TRUE;
}

static San9P1M2bDiscoveryStatus find_game_window(
    uint32_t game_pid,
    uint32_t *hwnd,
    uint32_t *tid)
{
    WindowSearch search;
    memset(&search, 0, sizeof(search));
    search.pid = game_pid;
    if (!EnumWindows(find_window_callback, (LPARAM)(uintptr_t)&search)) {
        return SAN9_P1_M2B_DISCOVERY_WINDOW_MISSING;
    }
    if (search.count == 0u) {
        return SAN9_P1_M2B_DISCOVERY_WINDOW_MISSING;
    }
    if (search.count != 1u || (uintptr_t)search.hwnd > UINT32_MAX) {
        return SAN9_P1_M2B_DISCOVERY_WINDOW_AMBIGUOUS;
    }
    *hwnd = (uint32_t)(uintptr_t)search.hwnd;
    *tid = search.tid;
    return SAN9_P1_M2B_DISCOVERY_OK;
}

static San9P1M2bDiscoveryStatus find_modules(
    uint32_t game_pid,
    San9P1M2bDiscovery *output)
{
    HANDLE snapshot = INVALID_HANDLE_VALUE;
    MODULEENTRY32W entry;
    uint32_t game_count = 0u;
    uint32_t easy_count = 0u;
    int attempts;
    for (attempts = 0; attempts < 3; ++attempts) {
        snapshot = CreateToolhelp32Snapshot(
            TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, game_pid);
        if (snapshot != INVALID_HANDLE_VALUE || GetLastError() != ERROR_BAD_LENGTH) {
            break;
        }
    }
    if (snapshot == INVALID_HANDLE_VALUE) {
        return SAN9_P1_M2B_DISCOVERY_MODULE_ENUMERATION;
    }
    memset(&entry, 0, sizeof(entry));
    entry.dwSize = sizeof(entry);
    if (!Module32FirstW(snapshot, &entry)) {
        (void)CloseHandle(snapshot);
        return SAN9_P1_M2B_DISCOVERY_MODULE_ENUMERATION;
    }
    do {
        if (CompareStringOrdinal(entry.szModule, -1, L"San9PK.exe", -1, TRUE)
                == CSTR_EQUAL) {
            ++game_count;
            if (!path_equal(entry.szExePath, EXPECTED_GAME_PATH)
                || (uintptr_t)entry.modBaseAddr != SAN9_P1_EASY_GAME_IMAGE_BASE
                || entry.modBaseSize != SAN9_P1_EASY_GAME_IMAGE_SIZE) {
                (void)CloseHandle(snapshot);
                return SAN9_P1_M2B_DISCOVERY_GAME_MODULE_IDENTITY;
            }
        } else if (CompareStringOrdinal(entry.szModule, -1, L"Easy.dll", -1, TRUE)
                == CSTR_EQUAL) {
            ++easy_count;
            if (!path_equal(entry.szExePath, EXPECTED_EASY_PATH)
                || (uintptr_t)entry.modBaseAddr > UINT32_MAX
                || entry.modBaseSize != SAN9_P1_EASY_IMAGE_SIZE
                || !hash_exact_file(entry.szExePath, EASY_FILE_SIZE, EASY_SHA,
                    output->easy_module_file_digest)) {
                (void)CloseHandle(snapshot);
                return SAN9_P1_M2B_DISCOVERY_EASY_MODULE_IDENTITY;
            }
            output->easy_binding.easy_image_base =
                (uint32_t)(uintptr_t)entry.modBaseAddr;
            output->easy_binding.easy_image_size = entry.modBaseSize;
            (void)wcsncpy(output->easy_module_path, entry.szExePath,
                SAN9_P1_M2B_DISCOVERY_PATH_CAPACITY - 1u);
        }
    } while (Module32NextW(snapshot, &entry));
    (void)CloseHandle(snapshot);
    if (game_count != 1u) {
        return SAN9_P1_M2B_DISCOVERY_GAME_MODULE_IDENTITY;
    }
    if (easy_count == 0u) {
        return SAN9_P1_M2B_DISCOVERY_EASY_MODULE_MISSING;
    }
    if (easy_count != 1u) {
        return SAN9_P1_M2B_DISCOVERY_EASY_MODULE_AMBIGUOUS;
    }
    return SAN9_P1_M2B_DISCOVERY_OK;
}

static int remote_read(
    void *context,
    uint32_t address,
    uint8_t *output,
    size_t output_size)
{
    RemoteContext *remote = (RemoteContext *)context;
    SIZE_T read = 0u;
    return remote != NULL && remote->process != NULL && output != NULL
        && output_size != 0u
        && ReadProcessMemory(remote->process, (const void *)(uintptr_t)address,
            output, output_size, &read)
        && read == output_size;
}

static int remote_query(
    void *context,
    uint32_t address,
    San9P1EasyMemoryRegion *output)
{
    RemoteContext *remote = (RemoteContext *)context;
    MEMORY_BASIC_INFORMATION info;
    uintptr_t base;
    if (remote == NULL || remote->process == NULL || output == NULL
        || VirtualQueryEx(remote->process, (const void *)(uintptr_t)address,
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

static int capture_page_metadata(
    HANDLE game_process,
    uint32_t address,
    San9P1M2bDiscovery *output)
{
    MEMORY_BASIC_INFORMATION info;
    uintptr_t allocation_base;
    if (game_process == NULL || output == NULL
        || VirtualQueryEx(game_process, (const void *)(uintptr_t)address,
            &info, sizeof(info)) != sizeof(info)) {
        return 0;
    }
    allocation_base = (uintptr_t)info.AllocationBase;
    if (allocation_base > UINT32_MAX) {
        return 0;
    }
    output->easy_page_allocation_base = (uint32_t)allocation_base;
    output->easy_page_allocation_protection = info.AllocationProtect;
    output->easy_page_type = info.Type;
    return 1;
}

static San9P1M2bDiscoveryStatus capture_easy(
    HANDLE game_process,
    San9P1M2bDiscovery *output)
{
    RemoteContext remote;
    San9P1EasyCallbacks callbacks;
    San9P1EasySnapshot first;
    San9P1EasySnapshot second;
    San9P1Sha256Context sha;
    San9P1EasyState state;
    memset(&remote, 0, sizeof(remote));
    memset(&callbacks, 0, sizeof(callbacks));
    memset(&first, 0, sizeof(first));
    memset(&second, 0, sizeof(second));
    remote.process = game_process;
    callbacks.read = remote_read;
    callbacks.query = remote_query;
    callbacks.context = &remote;
    if (!san9_p1_easy_capture(&output->easy_binding, &callbacks, &first)
        || !san9_p1_easy_capture(&output->easy_binding, &callbacks, &second)) {
        return SAN9_P1_M2B_DISCOVERY_EASY_CAPTURE;
    }
    state = san9_p1_easy_verify_buffers(&output->easy_binding,
        &first, &second, &output->easy_report);
    output->easy_page = second.protected_page;
    if (!capture_page_metadata(game_process,
            second.protected_page.base_address, output)) {
        return SAN9_P1_M2B_DISCOVERY_EASY_CAPTURE;
    }
    if (state != SAN9_P1_EASY_INSTALLED
        || output->easy_report.compatible_for_future_bridge != 1u
        || output->easy_report.stable_snapshot != 1u
        || output->easy_report.installed_redirect_count != 32u
        || output->easy_report.unknown_redirect_count != 0u) {
        return SAN9_P1_M2B_DISCOVERY_EASY_NOT_INSTALLED;
    }
    san9_p1_sha256_initialize(&sha);
    san9_p1_sha256_update(&sha, (const uint8_t *)&second, sizeof(second));
    san9_p1_sha256_finish(&sha, output->easy_snapshot_digest);
    san9_p1_secure_zero(&first, sizeof(first));
    san9_p1_secure_zero(&second, sizeof(second));
    return SAN9_P1_M2B_DISCOVERY_OK;
}

San9P1M2bDiscoveryStatus san9_p1_m2b_discover(San9P1M2bDiscovery *output)
{
    ProcessCandidate game;
    ProcessCandidate loader;
    HANDLE helper = NULL;
    uint64_t game_generation_after = 0u;
    uint64_t loader_generation_after = 0u;
    San9P1M2bDiscoveryStatus status;
    if (output == NULL) {
        return SAN9_P1_M2B_DISCOVERY_INVALID_ARGUMENT;
    }
    memset(output, 0, sizeof(*output));
    memset(&game, 0, sizeof(game));
    memset(&loader, 0, sizeof(loader));
    output->structure_size = sizeof(*output);
    output->status = SAN9_P1_M2B_DISCOVERY_INVALID_ARGUMENT;
    status = find_exact_process(L"San9PK.exe", EXPECTED_GAME_PATH,
        GAME_FILE_SIZE, GAME_SHA, SAN9_P1_M2B_DISCOVERY_GAME_MISSING,
        SAN9_P1_M2B_DISCOVERY_GAME_AMBIGUOUS,
        SAN9_P1_M2B_DISCOVERY_GAME_IDENTITY, &game);
    if (status != SAN9_P1_M2B_DISCOVERY_OK) {
        goto cleanup;
    }
    status = find_exact_process(L"San9PKEasy.exe", EXPECTED_LOADER_PATH,
        LOADER_FILE_SIZE, LOADER_SHA, SAN9_P1_M2B_DISCOVERY_LOADER_MISSING,
        SAN9_P1_M2B_DISCOVERY_LOADER_AMBIGUOUS,
        SAN9_P1_M2B_DISCOVERY_LOADER_IDENTITY, &loader);
    if (status != SAN9_P1_M2B_DISCOVERY_OK) {
        goto cleanup;
    }
    output->game_pid = game.pid;
    output->easy_loader_pid = loader.pid;
    output->helper_pid = GetCurrentProcessId();
    output->game_generation = game.generation;
    output->easy_loader_generation = loader.generation;
    memcpy(output->game_file_digest, game.digest, sizeof(game.digest));
    memcpy(output->easy_loader_file_digest, loader.digest, sizeof(loader.digest));
    (void)wcsncpy(output->game_path, game.path,
        SAN9_P1_M2B_DISCOVERY_PATH_CAPACITY - 1u);
    (void)wcsncpy(output->easy_loader_path, loader.path,
        SAN9_P1_M2B_DISCOVERY_PATH_CAPACITY - 1u);
    status = find_game_window(game.pid, &output->game_hwnd,
        &output->main_thread_id);
    if (status != SAN9_P1_M2B_DISCOVERY_OK) {
        goto cleanup;
    }
    output->easy_binding.game_image_base = SAN9_P1_EASY_GAME_IMAGE_BASE;
    output->easy_binding.game_image_size = SAN9_P1_EASY_GAME_IMAGE_SIZE;
    output->easy_binding.game_hwnd = output->game_hwnd;
    status = find_modules(game.pid, output);
    if (status != SAN9_P1_M2B_DISCOVERY_OK) {
        goto cleanup;
    }
    status = capture_easy(game.handle, output);
    if (status != SAN9_P1_M2B_DISCOVERY_OK) {
        goto cleanup;
    }
    helper = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE,
        GetCurrentProcessId());
    if (helper == NULL || !process_generation(helper, &output->helper_generation)
        || !process_generation(game.handle, &game_generation_after)
        || !process_generation(loader.handle, &loader_generation_after)
        || game_generation_after != output->game_generation
        || loader_generation_after != output->easy_loader_generation) {
        status = SAN9_P1_M2B_DISCOVERY_GENERATION_CHANGED;
        goto cleanup;
    }
    status = SAN9_P1_M2B_DISCOVERY_OK;
cleanup:
    output->status = status;
    if (helper != NULL) {
        (void)CloseHandle(helper);
    }
    if (game.handle != NULL) {
        (void)CloseHandle(game.handle);
    }
    if (loader.handle != NULL) {
        (void)CloseHandle(loader.handle);
    }
    return status;
}

const char *san9_p1_m2b_discovery_status_name(San9P1M2bDiscoveryStatus status)
{
    static const char *const names[] = {
        "OK", "INVALID_ARGUMENT", "GAME_MISSING", "GAME_AMBIGUOUS",
        "GAME_IDENTITY", "LOADER_MISSING", "LOADER_AMBIGUOUS",
        "LOADER_IDENTITY", "WINDOW_MISSING", "WINDOW_AMBIGUOUS",
        "MODULE_ENUMERATION", "GAME_MODULE_IDENTITY", "EASY_MODULE_MISSING",
        "EASY_MODULE_AMBIGUOUS", "EASY_MODULE_IDENTITY", "EASY_CAPTURE",
        "EASY_NOT_INSTALLED", "GENERATION_CHANGED"
    };
    if ((unsigned int)status >= sizeof(names) / sizeof(names[0])) {
        return "UNKNOWN";
    }
    return names[(unsigned int)status];
}
