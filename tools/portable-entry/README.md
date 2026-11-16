# Portable entry candidate

Status: preparation only, not a released layout. No signing or security-policy change.

Extracted candidate:

```
Start-app.exe
App/                 # whole original self-contained payload, unchanged
  StartApp.App.exe
  ... runtime, app DLLs, notices ...
START-HERE.txt
```

The new native Windows x64 GUI launcher resolves `App/StartApp.App.exe` from its
own absolute location, launches it directly with that App folder as working
directory, waits, and returns its exit code. The validation harness, not the product, contains its synthetic test descendants in kill-on-close jobs. No shell command, PATH lookup,
elevation, arbitrary target, arguments, updater, download, or registry change.
Existing app data stays under its existing local-app-data path. Moving the whole
extracted folder is supported in design; moving only the launcher is not.
The launcher is statically linked against the MSVC runtime, not a second .NET
runtime bundle. It must be reviewed and verified on Windows before any release.
Both launcher and actual app would need consideration in any future signing plan.

Build on a Windows x64 machine with Visual Studio C++ tools using
`tools\portable-entry\build.cmd`. Stage using `package.ps1` with explicit payload,
launcher, and a NEW output directory. The existing release pipeline is untouched. Payload equality means this candidate staging copy equals the freshly built input; it does not mean equivalence to the published release ZIP.
Candidate validation does not upload a distributable ZIP or alter website links.

Acceptance: paths with spaces/Hebrew, relocation, unrelated working directory,
fixed target rather than a CWD decoy, owned-child exit code, missing-folder UX,
actual WPF app startup/clean exit and inspectable app pixels. Hosted-runner checks
are not a physical consumer-Windows or SmartScreen approval claim.

## Candidate pipeline integration preparation

The follow-up feature routes the normal **validation** candidate ZIP through this
layout, records `layout: portable-v1` in its manifest, verifies fixed archive
structure and launches the **extracted archive's top-level entry**, not the loose
payload directly. It uploads small synthetic smoke pixels for human inspection.
The original release verifier's legacy mode remains the default for the existing
published package and unchanged release workflow. Layout choice must be explicit.

Release workflow wiring is NOT done by this slice. Before a future distribution,
reconcile the live main release workflow's active approval gates against dev's
older dormant version. Apply only reviewed packaging steps; never overwrite the
owner's current activation/approval policy. Build and verify a new candidate,
review its real artifact/layout/checksums and request owner main/release approval.
The current site download and SmartScreen status are unchanged.

Validation's `src` and `trusted` directories currently use the same pushed SHA.
The names do not establish independent checker trust. ZIP schema checks establish
layout consistency, not signing, safety, or publisher trust. Reproducibility is a
report-only same-run comparison with one launcher build shared by both copies;
it does not independently reproduce the native launcher. The release workflow
and downstream selector schema remain outside this slice.
