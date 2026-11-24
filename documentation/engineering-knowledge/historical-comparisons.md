# Historical code comparisons, 24 November 2026

Ten additional project comparisons, excluding the earlier Caliburn.Micro and ILSpy review. The snapshots below are from 2018–2020. Their dates are established by repository commit metadata, not by a claim of exclusive human authorship. We cannot prove how every contributor produced every line. We inspected source mechanisms, not popularity or formatting. No historical dependency is recommended for installation.

Start-app anchor: integration snapshot `1da69a0`, not a released version. This documentation branch makes no product changes and does not approve a merge.

## Working rules

Trace ownership and state transitions before moving code. Preserve notification, publication and cleanup order. Reuse existing mechanisms only where their failure policy fits. Require executable evidence for edge cases and visual evidence against the actual destination branch. A mature project is a comparison, not an authority to copy blindly.

## 1. PrismLibrary/Prism
- Snapshot: `4e7f7ddad3f1ff6f64165e157820dcf986e0f74f`, 2020-12-28. [Source](https://github.com/PrismLibrary/Prism/blob/4e7f7ddad3f1ff6f64165e157820dcf986e0f74f/src/Prism.Core/Commands/DelegateCommandBase.cs) · [Commit](https://github.com/PrismLibrary/Prism/commit/4e7f7ddad3f1ff6f64165e157820dcf986e0f74f).
- Observed: DelegateCommandBase captures the construction SynchronizationContext, posts CanExecuteChanged when the caller is on another context, and rejects duplicate observed-property expressions.
- Start-app: `Shared/CommandRegistry.cs and Shared/Mvvm.cs`. Start-app explicitly invalidates a registry in registration order. Its RelayCommand does not itself marshal notifications. The registry is an owner of ordering, not evidence that every caller is on the UI thread.
- Apply: For each command dependency, identify which state change invalidates it and which thread delivers the event. Test both registration order and async completion paths.
- Limit: Do not add context capture to every command automatically. First establish the UI-thread contract and preserve synchronous order where callers depend on it.

## 2. reactiveui/ReactiveUI
- Snapshot: `963666ac751d83561e8b549a184cc2d1f95b0c9d`, 2020-12-18. [Source](https://github.com/reactiveui/ReactiveUI/blob/963666ac751d83561e8b549a184cc2d1f95b0c9d/src/ReactiveUI/ReactiveCommand/ReactiveCommand.cs) · [Commit](https://github.com/reactiveui/ReactiveUI/commit/963666ac751d83561e8b549a184cc2d1f95b0c9d).
- Observed: ReactiveCommand tracks Begin/Result/End execution records, derives IsExecuting from an in-flight count, combines it with CanExecute, and exposes exceptions on a separate observable.
- Start-app: `Opening/MainViewModel.OpeningExecution.cs, Opening/OpeningSessionState.cs and Opening/CheckResultPublication.cs`. Start-app has explicit checking/launching flags and a first-fault publication handoff. Both need execution lifetime and errors to remain distinct from the returned item results. ReactiveUI is not proof that one boolean or one enum fits all Start-app phases.
- Apply: Trace start, result, fault, cancellation and completion separately. A cancelled request is not proof that blocking work completed. Test a late fault while another worker remains blocked.
- Limit: No Rx migration is proposed. The historical command implementation is a mechanism to study, not a dependency recommendation.

## 3. lbugnion/mvvmlight
- Snapshot: `4cbf77ca6fbe91f83cece8fbf288b166c54202c5`, 2018-12-16. [Source](https://github.com/lbugnion/mvvmlight/blob/4cbf77ca6fbe91f83cece8fbf288b166c54202c5/GalaSoft.MvvmLight/GalaSoft.MvvmLight%20%28PCL%29/ObservableObject.cs) · [Commit](https://github.com/lbugnion/mvvmlight/commit/4cbf77ca6fbe91f83cece8fbf288b166c54202c5).
- Observed: ObservableObject.Set compares old and new values, assigns the field, then raises the named property notification and returns whether a change occurred.
- Start-app: `Shared/Mvvm.cs and Items/MainViewModel.ItemEditor.cs`. Start-app already uses equality-aware Set for simple properties, while adapter setters additionally raise dirty-state and command notifications. Reducing repeated setters must preserve these dependent notifications and their order.
- Apply: When moving a property, enumerate direct and derived notifications. Test same-value assignment, changed-value assignment and command enablement, not only the final field value.
- Limit: Do not infer that every repeated Raise is redundant. Some host notifications are deliberate side effects beyond the backing-field change.

## 4. icsharpcode/AvalonEdit
- Snapshot: `a046e2dcb90e867982f7dc3ac2589b91d6326a42`, 2020-12-21. [Source](https://github.com/icsharpcode/AvalonEdit/blob/a046e2dcb90e867982f7dc3ac2589b91d6326a42/ICSharpCode.AvalonEdit/Document/UndoStack.cs) · [Commit](https://github.com/icsharpcode/AvalonEdit/commit/a046e2dcb90e867982f7dc3ac2589b91d6326a42).
- Observed: UndoStack has explicit group depth, rejects ending a nonexistent group, and restores playback state and document update boundaries in finally during undo/redo.
- Start-app: `Shared/UnsavedExitSession.cs and Capture/StagedItemCollection.cs`. Start-app has staged edits and deferred save/discard continuations, not a text undo stack. The useful comparison is transition ownership: the owner should reject impossible transitions and restore its own invariants even when callbacks fail.
- Apply: Give grouped operations explicit begin/end semantics, define nesting or reject it, and test callback exceptions and reentry. Consume a one-shot continuation before invoking it.
- Limit: Do not add undo or copy AvalonEdit's depth counter into unsaved navigation. Five host flags may deserve an enum, but that remains separate old-debt work.

## 5. MahApps/MahApps.Metro
- Snapshot: `bbab412af829c9b565fdfdbfd7aeacd9a30805f3`, 2020-12-18. [Source](https://github.com/MahApps/MahApps.Metro/blob/bbab412af829c9b565fdfdbfd7aeacd9a30805f3/src/MahApps.Metro/Controls/Dialogs/DialogManager.cs) · [Commit](https://github.com/MahApps/MahApps.Metro/commit/bbab412af829c9b565fdfdbfd7aeacd9a30805f3).
- Observed: DialogManager verifies dispatcher access, opens a dialog owned by a window, installs a SizeChanged handler, waits for load/button/close phases, then removes the handler and dialog before hiding the overlay.
- Start-app: `src/StartApp.App/WindowSessionSubscriptions.cs, src/StartApp.App/WindowUpdateSession.cs and src/StartApp.App/DialogFocusSession.cs`. Start-app extracts subscription and focus lifetimes from WPF windows. The boundary is useful only if replacement and close detach the exact prior source and if focus is tested on the actual dialog.
- Apply: Review the whole dialog lifecycle, including owner, dispatcher, load, close, handler removal and focus restoration. Test replacement, repeated close and exceptional exit.
- Limit: The historical source uses nested continuations. Copy its lifecycle checks, not its syntax or styling. No Metro dependency is proposed.

## 6. ButchersBoy/Dragablz
- Snapshot: `da7a4e58e1986b59fade3805f3777e6acb3e8fc5`, 2020-10-27. [Source](https://github.com/ButchersBoy/Dragablz/blob/da7a4e58e1986b59fade3805f3777e6acb3e8fc5/Dragablz/InterTabController.cs) · [Commit](https://github.com/ButchersBoy/Dragablz/commit/da7a4e58e1986b59fade3805f3777e6acb3e8fc5).
- Observed: InterTabController exposes WPF dependency properties for tab policy and an IInterTabClient collaborator. IInterTabClient asks the host to create a new window and decide what happens when a tab empties.
- Start-app: `src/StartApp.App/Components/ActionMenu.cs and src/StartApp.App/Components/DialogFrame.cs`. Start-app's shared menu owns placement/building while callers retain feature actions; the shared frame adds no wrapper layer. This is a smaller version of separating reusable visual mechanics from application/window policy.
- Apply: Before extracting a UI component, state which mechanics it owns and which decisions remain at the call site. Exercise two callers with meaningfully different policies.
- Limit: Do not introduce a pluggable interface for every control. Start-app does not need detachable tabs, and its current menu helper may remain concrete.

Additional inspected source: [IInterTabClient.cs](https://github.com/ButchersBoy/Dragablz/blob/da7a4e58e1986b59fade3805f3777e6acb3e8fc5/Dragablz/IInterTabClient.cs).

## 7. Fody/PropertyChanged
- Snapshot: `dd75f34f31e0b232a2665a21b8313f6c3c12f2b7`, 2020-12-29. [Source](https://github.com/Fody/PropertyChanged/blob/dd75f34f31e0b232a2665a21b8313f6c3c12f2b7/PropertyChanged.Fody/DependsOnDataAttributeReader.cs) · [Commit](https://github.com/Fody/PropertyChanged/commit/dd75f34f31e0b232a2665a21b8313f6c3c12f2b7).
- Observed: DependsOnDataAttributeReader builds explicit source-to-dependent property edges. Its integration test checks that FullName depends on both GivenNames and FamilyName; a missing dependency name is logged rather than silently invented.
- Start-app: `Items/MainViewModel.ItemEditor.cs, Shared/CommandRegistry.cs and tests/StartApp.Presentation.Tests/Shared/CommandRegistryTests.cs`. Start-app manually publishes derived state such as dirty flags and command availability. A move that preserves stored values but drops one edge can leave the UI stale.
- Apply: Write down dependency edges before consolidating setters. Check the notification edge itself in a focused test, including a derived property used by a different screen.
- Limit: No IL weaver or automatic dependency inference is recommended. The historical missing-name test even carries a TODO, so this source is not a model of complete validation.

Additional inspected test: [DependsOnDataAttributeReaderTests.cs](https://github.com/Fody/PropertyChanged/blob/dd75f34f31e0b232a2665a21b8313f6c3c12f2b7/Tests/AttributeReaders/DependsOnDataAttributeReaderTests.cs).

## 8. App-vNext/Polly
- Snapshot: `52968c25f2e13e2708898174c15aafc9cb915020`, 2020-05-02. [Source](https://github.com/App-vNext/Polly/blob/52968c25f2e13e2708898174c15aafc9cb915020/src/Polly/Bulkhead/AsyncBulkheadEngine.cs) · [Commit](https://github.com/App-vNext/Polly/commit/52968c25f2e13e2708898174c15aafc9cb915020).
- Observed: AsyncBulkheadEngine acquires a queue slot, then a parallel-execution slot, awaits the actual action, and releases each successfully acquired slot in its corresponding finally block.
- Start-app: `Opening/OpenCheckWorkerBudget.cs and Opening/OpeningCheckRunner.cs`. Start-app retains worker capacity until the blocking worker actually returns, even if the caller's deadline already published a snapshot. This matches the ownership principle, but Start-app has dedicated blocking threads rather than Polly's async action.
- Apply: Pair every successful acquisition with one release by the owner of actual completion. Test timeouts, cancellation before admission and a permanently blocked worker; never free capacity merely because the UI stopped waiting.
- Limit: Do not replace bounded blocking workers with an async semaphore without checking thread-pool starvation and OS-call cancellation behavior.

## 9. serilog/serilog
- Snapshot: `e890a3dc5a77c1ae782f7104ee51f5874d55bf45`, 2020-12-10. [Source](https://github.com/serilog/serilog/blob/e890a3dc5a77c1ae782f7104ee51f5874d55bf45/src/Serilog/Core/Sinks/SafeAggregateSink.cs) · [Commit](https://github.com/serilog/serilog/commit/e890a3dc5a77c1ae782f7104ee51f5874d55bf45).
- Observed: SafeAggregateSink snapshots sinks to an array, invokes each sink independently, catches a sink failure and writes it to SelfLog so later sinks can still receive the event.
- Start-app: `Opening/CheckResultPublication.cs, Opening/MainViewModel.OpeningExecution.cs and Shared/CommandRegistry.cs`. Start-app intentionally surfaces unexpected opening failures and preserves notification ordering. Serilog's broad catch is valid for a diagnostic fan-out boundary, not a universal rule for business operations or command invalidation.
- Apply: Every catch must name the boundary's failure policy: convert an expected environmental problem, report an unexpected fault, or isolate an optional observer. Never import a broad catch without importing and justifying that policy.
- Limit: Do not swallow arbitrary Start-app command or store failures to keep the UI moving. A failed write must remain failed and visible.

## 10. JamesNK/Newtonsoft.Json
- Snapshot: `0cf47a4687f50c1e0fb46c0f8048531d0faaf978`, 2020-12-14. [Source](https://github.com/JamesNK/Newtonsoft.Json/blob/0cf47a4687f50c1e0fb46c0f8048531d0faaf978/Src/Newtonsoft.Json/Serialization/JsonSerializerInternalReader.cs) · [Commit](https://github.com/JamesNK/Newtonsoft.Json/commit/0cf47a4687f50c1e0fb46c0f8048531d0faaf978).
- Observed: JsonSerializerInternalReader's required-property error path calls IsErrorHandled and either performs explicit recovery through HandleError or rethrows. Error recovery is a selected policy, not the default meaning of valid data.
- Start-app: `src/StartApp.Storage.Json/JsonStore.cs and Items/MainViewModel.SavedItemTransfers.cs`. Start-app deliberately refuses invalid/newer stores before mutation and preserves the original bytes, optionally making a diagnostic copy. Permissive member recovery would be the wrong default for this whole-store editor.
- Apply: At persistence boundaries, distinguish parse/shape/version validation, recovery policy and commit. Corrupt or too-new data must not become an empty valid store that is then saved. Test unchanged bytes and repeat attempts through every write entry point.
- Limit: Do not switch serializers or enable handled-error continuation merely because a mature library supports it. Atomic replacement is not a power-loss durability guarantee.

## Apply without widening this change

- Already consistent: retained worker capacity until real completion, one-shot continuation consumption, explicit error handoff, subscription replacement, and shared UI mechanics with feature-owned actions.
- Follow-up candidates, not implemented: unsaved-host transition modeling, measured picker-query indexing and cancellation-source completion ownership. These are existing debt, not proof of regressions.
- Next UI work: map existing field/selector variants first; preserve meaningful differences and compare the actual dev base, not a prior refactor snapshot.
- Before a future change: choose the relevant rule above, name the invariant, add or identify the regression test, then update this knowledge only when new evidence changes it.
- Efficiency: use one affected-path map and an explicit invariant matrix rather than rereading the whole codebase for every small change. Full-suite and destination-baseline qualification remain appropriate at integration boundaries.
