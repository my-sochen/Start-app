# Real drive-root acceptance

This executable is test-only. It runs only with the explicit disposable GitHub desktop opt-in in `validation-feature-e2e.yml`, and only on `fix/folder-root-paths` through that workflow. It is not added to the solution, shipped, or called by the app.

## Scope

- Real system-drive root, actual `WindowsComposition` probe, launcher and `ProcessStarter`.
- New visible Explorer root window, bound to HWND, actual explorer.exe PID, process start time, native class and exact path.
- Real `WindowsOpenItemCapturer.Scan`, requiring the exact root string.
- Only that captured root is saved with `JsonStore` and then loaded from disk after disposing the first repository.
- Production `MainViewModel.OpenItemCommand` opens the saved item using the actual production adapters, then a new Explorer window and a second real capture must agree.
- Foreground HWND and window identity must match before each screenshot. A reviewer must inspect the actual images, not infer visual success from file existence.

This is production adapter/VM integration, not a WPF control-click test. It does not prove consumer Windows 11 behavior, general DPI, physical display framing, complete user-flow coverage, all drive letters, or all path spellings.

UNC is explicitly NOT exercised. No controlled network share is supplied. The harness never creates shares, changes SMB/firewall policy, tries admin shares, searches remote servers, or converts a missing UNC test into a pass. Existing UNC unit/fake-adapter tests remain the only UNC evidence.

## Lifetime and cleanup

The baseline Explorer HWND set is read before opening anything. An existing root window causes refusal. A candidate must be the sole new matching root window after the single request and must not be in the baseline. On this dedicated runner this is scoped temporal evidence, not a general-purpose ownership detector. There is no claim that Explorer's process ID alone establishes ownership.

Only registered root HWNDs can receive WM_CLOSE, and only after the complete recorded identity and location are rechecked. Existing windows and whole Explorer processes are never closed or killed. If identity changes, cleanup fails visibly instead of guessing. Closure must be observed before reopening. JSON evidence records any registered windows left at process exit.

Storage uses a newly generated path beneath RUNNER_TEMP, not app LOCALAPPDATA. Only that newly created tree is deleted. Assertions record cleanup outcomes. The outer three-minute timeout kills only the child harness if it hangs. In that forced-exit case finally-based cleanup is not guaranteed; the job explicitly fails and marks Explorer cleanup unverified, then the disposable hosted runner is discarded.

## Evidence gate

The harness fails on any exception or failed assertion. The workflow separately checks a matching SHA FLOW-COMPLETE row, absence of failed assertions, both screenshots, empty registered-window residue and removal of the scratch directory. The ordinary UI gate checks are unchanged, and a root-acceptance failure fails the ui-gate job. Artifacts are uploaded even on failure, when available. No release, merge, feed activation, tag, upload destination or repository write permission is added.

Linux cross-compilation is a compile check only. Real Windows execution and image inspection remain pending until this exact reviewed source is pushed and its run is examined.
