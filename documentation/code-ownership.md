# Code ownership guide

This guide describes the staged refactor branch, not a released build. The draft PR stack is not merged into dev or main. The dedicated integration PR now gathers the stages. Its merge still needs the owner's separate decision.

## Start with an action

| Change you want | Start here | Keep out of this class |
| --- | --- | --- |
| New workspace input, dirty state or preview | `src/StartApp.Presentation/Workspaces/WorkspaceCreateDraft.cs` | Saving, navigation and notifications |
| Manual-add draft, web-target interpretation and duplicate state | `Items/ManualAddSession.cs` | Selected workspace, notification order and catalog mutation |
| Existing workspace details draft | `Workspaces/WorkspaceDetailsDraft.cs` | Picking another workspace or writing storage |
| Item edit draft and captured identity | `Items/ItemEditSession.cs` | Retargeting to current selection during save |
| Saved/staged address edit state | `Items/AddressEditSession.cs` | Store writes and staged-row writes |
| Saved/staged open-with state | `Items/OpenWithSession.cs` | Launching and setting Windows defaults |
| Card design originals and draft | `Workspaces/CardDesignSession.cs` | Saving and unsaved-exit decisions |
| Category editor draft/warning state | `Workspaces/CategoryEditSession.cs` | Catalog mutations |
| Resource picker type/search/open state | `Items/ResourcePickerSession.cs` | Binding collections and writes |
| Resource picker sorting/filtering/usage | `Items/ResourcePickerQuery.cs` | Session state |
| Capture single/group duplicate state | `Capture/CaptureDuplicateSession.cs` | Duplicate identity policy and stage/save decisions |
| Staged-row collection and same-group movement | `Capture/StagedItemCollection.cs` | Row writes and capture persistence |
| Scan cancellation and stale results | `Capture/CaptureScanLifetime.cs` | Scan adapter and UI publication |
| Opening target, checked plan and UI phase flags | `Opening/OpeningSessionState.cs` | Launch/check orchestration and notification order |
| Atomic worker result and fault handoff | `Opening/CheckResultPublication.cs` | Deadline, cancellation, worker budget and error display |
| Opening cancellation and generations | `Opening/OpenOperationLifetime.cs` | Launch and check policy |
| Blocking preflight worker ceiling | `Opening/OpenCheckWorkerBudget.cs` | Resetting capacity when a worker times out |
| Toast state and stale auto-hide identity | `Shared/ToastSession.cs` | Timer and dispatcher |
| Inline validation routing | `Shared/FieldErrorSession.cs` | Validation rules and translated messages |
| Unsaved dialog and deferred continuation | `Shared/UnsavedExitSession.cs` | Save/discard ordering |
| Command registration/invalidation order | `Shared/CommandRegistry.cs` | Execute/can-execute policy |
| Window subscription lifetime | `src/StartApp.App/WindowSessionSubscriptions.cs` | View rendering |
| Optional update window and approved-close state | `WindowUpdateSession.cs` in the App folder | WPF construction/events, apply launch and actual closing |
| Default dialog focus transition | `DialogFocusSession.cs` in the App folder | WPF focus eligibility |
| Row context-menu geometry | `WindowPopupPlacement.cs` in the App folder | Popup opening and dispatcher ordering |

## The host is still a host

`MainViewModel` remains the public binding surface and workflow coordinator. Its partial files are one class, not independent objects. Files named `Items/MainViewModel.ItemEditor.cs`, `Items/MainViewModel.AddressEditor.cs`, `Workspaces/MainViewModel.CardDesign.cs`, `Workspaces/MainViewModel.CategoryManager.cs` and `Items/MainViewModel.ResourcePicker.cs` adapt the independent owners to the existing public properties and commands.

A state owner has no repository, window or MainViewModel reference. The host keeps persistence, validation, notifications and cross-form decisions. Moving fields into an owner is a boundary, not a claim that all workflow orchestration has already been separated.

`Opening/MainViewModel.OpeningExecution.cs` still coordinates asynchronous checks and launches. `Capture/MainViewModel.Capture.cs` still coordinates scan publication, staging, duplicate decisions and capture saves. `Shell/MainViewModel.cs` still coordinates navigation, storage reload and save/discard ordering. These are remaining larger boundaries.

## Follow one saved item edit

1. The public command calls `BeginItemEdit` in `Items/MainViewModel.ItemEditor.cs`.
2. The host refuses replacing an open dirty editor, then captures workspace and instance identity into `ItemEditSession`.
3. Draft setters publish the same public binding notifications and command invalidation as before.
4. Save validates the captured identity against the current snapshot, then checks it again inside the storage mutation.
5. Only a successful save closes the form. A missing record keeps the draft instead of silently targeting another item.

For staged addresses and open-with defaults, the target is a staged row. Those writes remain staged until the capture save. The state owners treat these target references as opaque.

## Cancellation is not an undo

- An opening generation rejects stale publication. Cancelling a launch stops later requests, not requests already sent.
- A blocked preflight worker keeps its capacity slot until its `finally` block finishes.
- A timed-out capture worker stays outstanding until it returns. A new generation does not free that slot.
- Opening and capture lifetimes deliberately have different source-disposal rules. Do not replace one with the other without reviewing late-worker behavior.
- The unsaved continuation is consumed before invocation, so reentry or an exception cannot execute the same continuation twice.

## Follow a check deadline

1. `Opening/MainViewModel.OpeningExecution.cs` creates one `CheckResultPublication` per check run.
2. Workers record by original item index. Expected environmental failures become findings; unexpected faults enter the first-fault handoff.
3. At the deadline, `Publish` takes a detached snapshot under the same lock used by workers. A later worker cannot change it.
4. A fault already present belongs to `Guard`. The first fault arriving after handoff is returned to the late-error callback once, without waiting for another blocked worker.
5. `OpenCheckWorkerBudget` still holds each worker's slot until its `finally` runs. Publication and cancellation do not free capacity.

## Follow an optional update window

`MainWindow.ShowStartupUpdate` delegates loaded/approved-close/single-window checks to `WindowUpdateSession`. Creation assigns the reference before the close event is subscribed, and subscription happens before showing. Dismissal clears the reference; it does not approve closing or install anything. A successfully launched apply helper or explicit application-close approval marks the session approved before `Close` is called. WPF events and apply-launch error handling remain in the window.

## Where to test

- Each presentation owner has a matching `*Tests.cs` in `tests/StartApp.Presentation.Tests/`.
- `tests/StartApp.App.Session.Tests/` compiles the exact production subscription, focus, update-window lifetime and placement source without WPF, then tests state/order/geometry directly.
- Existing integration tests exercise public MainViewModel commands, saved/staged paths and error handling. Run the contracts for the affected workflow, not an unrelated full suite.
- `tests/StartApp.Platform.Windows.Tests/MenuBindingSourceTests.cs` checks XAML popup bindings and the call into the extracted geometry. A source contract is not rendered visual proof.
- `tests/StartApp.CaptureRefactorCapture/` runs actual production MainWindow/XAML against a synthetic store.

## Visual evidence has a boundary

The capture fixture covers nine capture states. Its additional same-process comparison requires zero differing pixels between the original Grid and extracted view inside one window/session. The independent baseline-repeat and baseline/candidate gates also require exact equality, without masks or tolerance.

The integration run45350576134 compared against actual dev4e3ae7bf:25capture/frame/prompt states,10menus and24button variants matched exactly, including independent baseline repeats. Earlier glyph-cache/focus fixture diagnostics are historical, not an unresolved gate in that run. This finite evidence does not prove every dialog/menu/focus path.

Feature partials use MainViewModel.Feature.cs names because they belong to the same binding host, not separate view-model objects. OpeningCheckResult names the internal preflight publication payload; the launch coordinator explicitly maps it into its unchanged launch plan.
