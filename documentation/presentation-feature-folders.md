# Presentation feature folders

Source snapshot: `daadb2d1eb89861423324d7c695ca7377db3abf2`.

Before: 458 tracked files, 56 folders. Presentation root held 57 files and its test root 62. App root still holds 53 files; workflows hold 53. Four folders held 225 files (49%).

This stage moves 56 presentation source files and 61 test files, all byte-identical. Namespaces remain stable. Root project/Properties files stay. The source root drops from 57 direct files to 1; test root from 62 to 1. The largest new presentation folder holds 15 files, the largest test feature folder 12.

| Feature | Source files | Test files |
| --- | ---: | ---: |
| Capture | 8 | 8 |
| Opening | 8 | 10 |
| Items | 15 | 12 |
| Workspaces | 10 | 12 |
| Updates | 7 | 8 |
| Shared | 6 | 5 |
| Shell | 2 | 6 |

Exact old/new paths are in `presentation-folder-moves.json`. `Items/ResourcePickerTests.cs` also contains legacy capture tests; moving a file does not separate those classes. Existing MainViewModel partial files remain one class, now grouped by feature. Folder structure does not remove orchestration coupling.

Explicit workflow path filters and the traversal source guard follow the new paths. The traversal guard was already checking pre-extraction fields; it now verifies generation/cancellation at the current production owners without loosening the required behavior.

Next structural stage: App views/dialogs, menus, converters/icons, updates, diagnostics and windowing. Keep App.xaml/MainWindow entry points initially; audit relative font/pack URIs and paired XAML paths before moving. Workflows must stay directly under .github/workflows for discovery; their 34 refactor-stage definitions need separate consolidation, not nested folders. No workflow consolidation in this stage.
