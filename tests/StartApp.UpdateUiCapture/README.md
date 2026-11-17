# Local-only synthetic WPF state capture

Not in the solution, not wired into any workflow, not run yet. Review before running.

On an approved isolated Windows runner with .NET 10, build/run this project with one argument naming a NEW output directory. It does not invoke App.OnStartup. It instantiates the real MainWindow XAML with a fresh JsonStore under the supplied output and injects synthetic update states into the actual update row. The product has no test flag, alternate network source or browser-launch switch.

Produces eight actual WPF-content PNG renders at window widths 900 and 720: inactive, available, failure and busy, plus button state metadata. Browser handoff is a recording delegate, never Process.Start. No network client is constructed. The product's inactive Updates instance is retained unchanged. No preexisting store or LOCALAPPDATA path is read or deleted. Output stays for inspection; there is no recursive cleanup.

RenderTargetBitmap captures real WPF pixels, not an HTML mockup, but excludes the native titlebar/taskbar. It does not prove native browser launch, physical DPI, consumer Windows 11, all screen layouts or peer interactive QA. Inspect actual PNGs before deciding visual readiness. Separate normal full-regression UI automation remains necessary. A successful cross-build alone proves no pixels.
