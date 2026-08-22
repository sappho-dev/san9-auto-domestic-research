# San9Bridge P1 Easy Ping / S3 Read-only Observer

M2b is an isolated x86 Windows bridge for the ping-only milestone.  Its
DLL/controller contain a real
current-user-only ACL mapping, `WH_GETMESSAGE` bootstrap, the frozen Easy
32+6/page/HWND/idle gate, original-to-wrapper slot CAS, main-thread idle safe
point, the frozen P1Wire/M2a 100-ping path, and the S3 read-only observer. The idle wrapper calls the
original `0x00434100` exactly once before its post helper.

The production artifacts are compiled with `S3_READONLY_BUILD=1`. The observer
has 17 compiled `(symbol, offset, length)` read entries, zero write entries,
and a 256-byte per-read ceiling. Requests cannot carry an address. A 10-record
SPSC ring captures only structural changes in the scheduler/controller,
Commerce handler/list, modal task, selection and command candidates. Nested
modal idle calls may record snapshots but cannot process authenticated writes.

The build does **not** load or execute either live artifact.  It statically
audits them, then runs only a separate offline self-test with no live Windows
bridge code.  `LIVE_EXECUTION_DEFAULT=0`, `UI_CONNECTED=0`, and no production
UI references this directory.

The exact-profile live gates observed one successful probe0 and one successful
S2 batch: 100/100 authenticated request/response rounds, sequence 1..100,
one stable Easy snapshot digest, zero nested calls, zero identity rejects, and
zero business Apply.  An independent read-only V2 business summary was
byte-identical before and after that batch.  These observations authorize only
the completed ping-only evidence; every new resident run still needs explicit
user authorization and the DLL remains pinned until game restart.

One separately authorized S3 run observed a user-performed Commerce command for
40,172 ms. It produced six structural records with zero drops and zero capture
failures: controller idle -> Commerce handler (`vptr 0x00609F38`) -> command
(`vptr 0x00607D98`) -> handler -> controller idle. The handler's embedded list
at `+0x40` had `vptr 0x00606C8C`, a live count of 34, four observed tail words
through `+0x5C`, and an independently matching city target at `+0x60`; this
dynamically fixes the embedded `PersonList` footprint at `0x20` bytes. The
global selected count was exactly five. Original/outer counts were both 1115;
nested calls, identity rejects, observer business Apply, dropped records and
restart-required were all zero. The run recorded layout and lifecycle only;
the game itself performed the command after manual user input.

Commerce remains dynamic **NO-GO**. Its static shadow-vtable ABI marker remains
frozen (`COMMERCE_ABI_READY=1`) but live authorization is zero. The S3 build
physically excludes the shadow-vtable CAS skeleton and all command construction.
Command construction remains closed. S3 authenticated the embedded list's
`0x20`-byte footprint and its live handler placement, but did not authorize or
prove autonomous list construction, ownership transfer, native invocation, or
Apply.
The known handler/list/copy/constructor/validator/attach candidate addresses
are inert constants; no code calls them.
There are no five-command fields, all-city loop, slot restore,
or hot-unload path; after commit the DLL is pinned and failure requires restart.

Build from PowerShell:

```powershell
.\native\San9BridgeP1EasyPingM2b\build.ps1
```

The controller exposes four explicit modes: zero-write `--inspect`, authorized
resident `--probe0`, authorized resident `--ping`, and separately authorized
resident `--observe`. Neither the
root build nor the UI invokes the resident modes.

The S5 `--s5-no-apply` timing probe uses a two-stage live sequence. It must be
started from the strategic map so the newly installed idle wrapper is observed
for the exact Probe0 ticks. The same controller then prints
`WAITING_FOR_USER_MENU_SIGNAL` and blocks without publishing a request while the
user opens the already-selected current city's domestic menu. An exact
controller-side `OPEN_CURRENT_CITY_MENU` signal starts a 60-second read-only
capture window. A valid context is frozen without publishing a request and the
controller prints `CONTEXT_FROZEN_CLOSE_MENU`. The user then closes the menu.
Only after the exact outer idle counter resumes and a fresh A/B snapshot remains
byte-semantically equal to the frozen context does the controller sign and
publish the five-second NO_APPLY request. Starting the bridge after the menu is
already open is intentionally rejected: that modal loop does not invoke the
outer idle callback. No controller retry is performed, and the DLL remains
pinned until the game process is restarted.

Generated artifacts are placed under the ignored `tools/artifacts` tree.
