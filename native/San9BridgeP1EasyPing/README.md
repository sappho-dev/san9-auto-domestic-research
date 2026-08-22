# San9BridgeP1EasyPing — P1 M1 only

This directory is the isolated **P1 M1 offline native Easy compatibility gate**.
It is not a bridge DLL, hook, injector, IPC endpoint, ping transport, or business
command implementation. It cannot authorize execution.

The build performs five fail-closed steps:

1. authenticate `docs/easy-compatibility-manifest.json` by the frozen SHA-256;
2. authenticate the exact `San9PK.exe` named by that manifest and generate a
   private `san9_p1_easy_manifest.gen.h` in the ignored build staging directory;
3. compile the x86 C11 `-Werror` self-test in two independent artifact roots and
   require byte-identical output matching the frozen whole-image SHA-256;
4. before any execution, statically audit that PE for zero process access, zero
   target writes, zero IPC, and zero live code;
5. run the buffer/callback, actual-base rel32, full 32+6 ownership,
   page/HWND/idle-anchor, stable A/B, and record-first tests, then require an
   unchanged hash and repeat the PE audit.

The self-test executable contains exactly two callbacks supplied by the pinned
MinGW CRT startup. The PE audit freezes their table RVA, ordered callback RVAs,
non-writable `.text` ownership, exact function boundaries through `ret 0x0c`,
and complete-function machine-code SHA-256 identities. It also requires the PE
entrypoint to remain in a non-writable executable section.
It rejects duplicate import descriptors before building the exact import map.
Five malicious PE fixtures prove rejection of a duplicate descriptor, TLS
entrypoint substitution, first-byte and tail callback mutations, and an
entrypoint redirected into `.data`. These callbacks are
offline test-runtime startup, not bridge or target-process code.

The generated header is never committed and is never a second hand-maintained
truth table. Generated artifacts remain under the repository's ignored
`tools/artifacts/` tree by default.

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\native\San9BridgeP1EasyPing\build.ps1
```

M1 accepts only two stable cleanup/runtime classes:

- original: all 32 redirects original; training exactly `32/32`; the other
  fixed auxiliaries, Easy HWND slot, and page are original; idle anchors exact;
- installed: all 32 redirects resolve to the declared handler RVA in the one
  actual Easy image; training is `32/32` or `00/00`; fixed auxiliaries, bound
  HWND, committed read/write page, and all idle anchors are exact.

Partial, mixed, unknown, unstable, overflowed, or occupied-anchor states fail
closed. Only `installed` is marked compatible for a future bridge; M1 itself
contains no installation or execution path.
