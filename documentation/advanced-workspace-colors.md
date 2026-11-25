# Advanced workspace colors

Feature branch from dev after PR143. Not merged or released.

## Ownership
- `WorkspaceColors` owns color normalization, unique history insertion, and the nine base colors.
- `MainViewModel.WorkspaceColors` owns draft selection and permission to apply it. The create draft is shared with capture-new. Card design has its own existing draft.
- `WorkspaceColorPicker` is reused in all three entry points, with shared swatch/button styles. It lists nine base colors first, then extra saved/history colors without duplicates, and shows the current draft color.
- `AdvancedColorWindow` owns a local HSV/RGB/hex edit. Cancel/close returns no value. Confirm sends a validated color to the appropriate draft.
- `JsonStore` owns atomic history persistence and schema upgrades. Schema6 adds `advancedColorHistory`; schema1-5 still load. Existing backup logic keeps the byte-exact original before first schema6 write. Old builds reject newer schemas.

## Product decisions
- 25 November: unify all pickers and the filter base list on nine muted colors, retaining the six styled creation colors. Added muted teal, peach and brick.
- Existing card colors are never rewritten. Colors outside the base palette remain available as extra colors and filter choices.
- 25 November: history is written only with a successful outer workspace/design save, in the same transaction. Cancel/discard and failed saves write neither. Replacing an advanced choice with another choice remembers only the final saved color. Failed saves retain the pending selection for retry.

## Review map
- Draft state, cancel, validation and read-only gates.
- Create, capture-new, card design and existing unsaved-exit/save failure behavior.
- Dynamic color filter list and additional picker colors.
- File-format validation, case-insensitive uniqueness, first-selection order, old-schema read/backup, and refusal to overwrite corrupt history.
- Shared controls/resources, RTL layout and narrow-width wrapping.

Targeted tests and actual Windows pixel captures are in the beta-advanced-colors workflow. The evidence fixture is synthetic and does not cover all DPI, all screen readers, real owner data, or full-system release qualification.

## Review corrections, 25 November
- Source-integrity and registered-command contracts now assert the shared picker and its new advanced command, rather than removed inline controls.
- Active capture/cycle1/tab/design consumers use current preset hex names and a shared advanced-dialog UIA helper. A Windows smoke test executes that helper against the app. Full long-running capture/cycle1 browser/native flows are not claimed by this feature workflow.
- Updated palette geometry to nine28px swatches, preserving the scroll-to-reveal and full-size checks.
- Added explicit failure/retry tests for design and capture, not only create.
- Fixed schema documentation, mixed-direction error message, and caret retention during hex/RGB normalization.
- Run the entire affected Core, Presentation and Storage test projects once on the corrected head; do not rerun a whole suite merely to hide a single assertion failure.
