# San9AutoDomestic.Input.Win32

This project contains the exact-build Win32 input boundary for
`D:\三国志9\10101749\San9PK.exe` 1.0.1.0.

## Current execution boundary

`AllDirectCitiesDomesticPlanExecutor` is connected to the persistent UI for one explicitly authorized all-direct-cities batch. It freezes the exact direct-city set, supports at most four calibrated visible CITY rows, and discovers row identity from the freshly opened city instead of assuming city-ID order. After a row click it accepts either the exact city menu opened directly by an already-selected row, or the verified strategic-map candidate followed by the calibrated center-city click; every other layer aborts uncertain. It rejects duplicate/out-of-set rows and requires exact set equality at completion. It delegates each verified city to `SingleCityDomesticPlanExecutor` under one shared process-wide lease. Every step binds to the exact PID, process creation FILETIME, HWND and executable path; uses only foreground Win32 input; captures a screenshot before and after every attempted action; freshly reads the UI and command availability; selects exactly five; validates the exact postcondition; and never retries a commit.

Known grey, delegated, under-five and underfunded tasks skip before command input. Any technical mismatch after an attempted input produces `AbortUncertain` and terminates the batch. The executor does not modify files, write process memory, inject/Hook code, call internal game functions, or advance the turn.

The older `San9ManualInputTraceRecorder` remains available as an engineering evidence tool:

- it never sends input;
- it does not capture the screen, install a hook, inject code, or write game memory;
- it binds a stable start observation to PID, process creation FILETIME and HWND;
- after the exact game window remains foreground and input is released for 500 ms, it polls cursor/button/modifier state;
- it accepts exactly one manual left-button down/up pair and rejects extra input or foreground changes;
- it requires the final stable UI to be `DomesticCommandMenu` for the requested city;
- it writes a create-new JSON evidence file whose event payload has a SHA-256 digest;
- only an accepted trace sets `ReplayAuthorized=true`. This flag makes the trace eligible for later review; this recorder itself has no replay capability.

The visible-row route uses only the four empirically calibrated coordinates. More than four direct cities, an unexpected row identity, a changed direct-city set, a duplicate, binding drift, or any ambiguous transition stops the batch. Scrolling is not authorized.

## Build and offline test

```powershell
& 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe' `
  'tools\San9AutoDomestic.Input.SelfTest\San9AutoDomestic.Input.SelfTest.csproj' `
  /t:Rebuild /p:Configuration=Release /p:Platform=x86
& 'tools\San9AutoDomestic.Input.SelfTest\bin\Release\San9AutoDomestic.Input.SelfTest.exe'
```

The trace runner is `tools\record-san9-manual-input.ps1`. It must be launched by 32-bit Windows PowerShell after the facilities list is already open. The runner waits for the game to become foreground; it does not focus the game itself.

The root transactional build also compiles this project and runs the same synthetic suite before publication.
