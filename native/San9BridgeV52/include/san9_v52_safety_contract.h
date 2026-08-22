#ifndef SAN9_V52_SAFETY_CONTRACT_H
#define SAN9_V52_SAFETY_CONTRACT_H

#include <stddef.h>

#define SAN9_V52_EXACT_WINDOW_CLASS L"KOEI_SAN9WINDOW"

enum {
    SAN9_V52_CONFLICT_PROCESS_COUNT = 3,
    SAN9_V52_CONFLICT_MODULE_COUNT = 3,
    SAN9_V52_LOCAL_PROXY_COUNT = 5
};

/* Frozen from the existing V0/V6 fail-closed conflict contract. */
static const wchar_t *const SAN9_V52_CONFLICT_PROCESSES[
    SAN9_V52_CONFLICT_PROCESS_COUNT] = {
    L"San9PKEasy.exe",
    L"San9PKHard.exe",
    L"SanIXPKCheat.exe"
};

static const wchar_t *const SAN9_V52_CONFLICT_MODULES[
    SAN9_V52_CONFLICT_MODULE_COUNT] = {
    L"Easy.dll",
    L"SanIXSpy.dll",
    L"San9Common.dll"
};

static const wchar_t *const SAN9_V52_LOCAL_PROXIES[
    SAN9_V52_LOCAL_PROXY_COUNT] = {
    L"version.dll",
    L"dinput.dll",
    L"dinput8.dll",
    L"winmm.dll",
    L"dsound.dll"
};

#endif
