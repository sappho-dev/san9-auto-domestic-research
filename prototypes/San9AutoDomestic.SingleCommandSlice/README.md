# San9AutoDomestic.SingleCommandSlice

This is a **permanent offline shadow-only prototype** for one narrow vertical
slice:

`Core configuration profile -> stable city/task cursor -> one-use ticket ->`
`fresh synthetic command observation -> ValidatedSingleCommand -> V8 request`

It is deliberately not included by the repository root build.  It does not
reference the game adapter, UI, bridge, V5.2, process APIs, IPC, or native
callbacks.  Every public artifact reports `ShadowOnly == true` and
`LiveAuthorized == false`; a V8 `SingleCommandRequest` also reports
`AuthorizesLiveMutation == false`.

Only an `IShadowObservationProvider` supplies observations.  The self-test
implements the sole provider used here as an in-memory fake.  No Core internal
trusted-observation capability is reflected, forged, or exposed.

## Build and test

From this directory:

```powershell
.\build-and-test.ps1
```

The script first runs the authoritative V8 offline suite, pins the exact Core and
V8 production DLL hashes, performs a source-level forbidden API/reference scan,
Release builds the isolated library and self-test with warnings as errors, runs
all synthetic tests, and prints SHA-256 hashes.  It never starts or connects to
San9PK.

## Deliberate boundary

“Commit” means only committing projected resource use to the in-memory shadow
ledger.  It removes exactly five officers and the frozen command cost only after
a validated command wins the one pending slot.  `ShadowCommitReceipt` explicitly
states that no native submission occurred.

This prototype cannot be promoted to live execution merely by supplying a new
provider.  A future production integration would require a separately reviewed
authority boundary, OS-authenticated generation identity, live re-read, native
adapter, cross-process single-flight, and fault persistence; none exists here.
