# External San9 reverse-engineering repositories

This file catalogs external repositories worth cloning when researching San9PK internals, file formats, executable patching, runtime injection, or compatibility behavior.

## Tier 1 — directly useful for San9PK reverse engineering

### 1. cdd1037/san9-toolkit

- URL: https://github.com/cdd1037/san9-toolkit
- Clone: `git clone https://github.com/cdd1037/san9-toolkit.git`
- Target: Traditional Chinese San9PK 1.0.1.0 (`San9PK.exe`, 2,636,800 bytes).
- Why useful:
  - Modern x86 runtime injection into San9PK.
  - Documents concrete game addresses and hooks.
  - Handles message-pump interception, imported API hooks, registry virtualization, timing acceleration, rendering interception, mouse coordinate translation, and launcher/runtime handshake.
  - Good reference for how to safely inject and hook this exact executable without directly overwriting the original EXE.
- Priority paths:
  - `README.md`
  - runtime / launcher source directories
  - any address/signature tables
  - `San9Toolkit.Runtime.vcxproj`
  - `San9Toolkit.ini.example`

### 2. jslhcl/jslhcl.github.io — `files/` San9PK multi-officer mod

- URL: https://github.com/jslhcl/jslhcl.github.io
- Clone: `git clone https://github.com/jslhcl/jslhcl.github.io.git`
- Relevant subtree: `files/`
- Why useful:
  - Direct San9PK.exe reverse engineering and binary patching.
  - Uses `pefile`, Capstone and Keystone.
  - Adds a new PE section (`.cop`), creates code caves, redirects instructions/vtable slots, and patches game behavior.
  - The reverse-engineering notes record concrete addresses, RVA/file-offset relationships, vtable locations and original functions.
  - This is currently the best external reference for extending gameplay behavior rather than only compatibility/rendering.
- Priority files:
  - `files/plan.md` — full RE notes and addresses.
  - `files/patch3.py` — patch builder.
  - `files/an.py`
  - `files/an2.py`
  - `files/an3.py`
  - `files/README.txt`
- Important: do not copy or redistribute the bundled original/patched game executable. Only use the scripts and notes as research references.

### 3. tzengyuxio/kaodata

- URL: https://github.com/tzengyuxio/kaodata
- Clone: `git clone https://github.com/tzengyuxio/kaodata.git`
- Why useful:
  - Early KOEI game data-format research covering SAN9 among many titles.
  - Provides working examples of parsing `.s9` resource files and handling KOEI data layouts.
  - Useful starting point for identifying record layout, headers, offsets, image resources and common binary conventions before extending analysis to scenario and battle-data files.
- Priority file:
  - `dekoei/sango.py`, especially the `san9` section.

## Tier 2 — executable loading / compatibility techniques

### 4. leejeonghun/san9pk-win10

- URL: https://github.com/leejeonghun/san9pk-win10
- Clone: `git clone https://github.com/leejeonghun/san9pk-win10.git`
- Target: Korean San9PK 1.0.1.0.
- Why useful:
  - Demonstrates DLL-hijacking / compatibility patching around San9PK on Windows 10/11.
  - Useful for understanding loader behavior, version-specific executable fingerprints and a low-impact drop-in patch architecture.
  - Less useful for battle formulas than the Tier 1 repos, but useful when building a stable injection/launcher layer.

### 5. ChihChiu29/chihchiu29.github.io — San9 PK notes

- URL: https://github.com/ChihChiu29/chihchiu29.github.io
- Clone: `git clone https://github.com/ChihChiu29/chihchiu29.github.io.git`
- Relevant path: `doc/games.md`
- Why useful:
  - Small but concrete notes on San9/San9PK registry keys, install-path registration and fullscreen configuration.
  - Not a reverse-engineering codebase, so treat as supporting documentation only.

## Research order for battle-system work

For questions such as formation parameters, tactic power, tactic activation probability, cooldown, proficiency resistance, status effects, treatment/illusion triggers, or hidden combat modifiers:

1. Start with `jslhcl/jslhcl.github.io/files/` to reuse its proven San9PK EXE disassembly/patch workflow and known address conventions.
2. Use `cdd1037/san9-toolkit` to reuse robust runtime injection, process/version validation, and hook infrastructure.
3. Use `tzengyuxio/kaodata` for `.s9` binary-resource parsing conventions and file-layout clues.
4. Compare Korean executable-loading work in `leejeonghun/san9pk-win10` only where loader/version differences matter.
5. Treat forum formulas and old guides as hypotheses until matched to executable/data evidence.

## Local layout

Run `tools/fetch-external-repos.ps1` from the repository root. It clones these repositories into `external/`, which is intentionally git-ignored so third-party source is not silently vendored into this repository.
