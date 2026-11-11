// CI-only Windows evidence harness (synthetic temp tree, nothing here ships). It exercises the REAL FileSystemProbe / FileSystemPreflight / ProcessStarter.
// The only process ever really started is this same exe in "helper mode" (env STARTAPP_HELPER_MARKER set): it writes a marker file and exits.
// Script / cmd samples are created but never executed; every other launch goes to a recording starter that starts nothing.
using System.Diagnostics;
using System.Text.Json;
using StartApp.Core;
using StartApp.Platform.Windows;
using StartApp.Presentation;
using StartApp.Storage.Json;

var marker = Environment.GetEnvironmentVariable("STARTAPP_HELPER_MARKER");
if (!string.IsNullOrEmpty(marker)) { File.WriteAllText(marker, $"helper-started args={Environment.GetCommandLineArgs().Length - 1}"); return 0; }

var root = args[0]; var results = new List<object>(); int failed = 0;
void Assert(string name, bool ok, string detail) { if (!ok) failed++; results.Add(new { name, ok, detail }); Console.WriteLine($"ASSERT {(ok ? "PASS" : "FAIL")} {name} :: {detail}"); }
void Info(string name, string detail) { results.Add(new { name, info = true, detail }); Console.WriteLine($"INFO {name} :: {detail}"); }
string P(string rel) => Path.Combine(root, rel);
Item It(ItemType t, string target) => new("i", t, target, target);
var probe = new FileSystemProbe(); var pre = new FileSystemPreflight(probe);
PreflightFinding Classify(Item it, LaunchConfig? l = null) => PreflightRules.CheckForm(it, l) ?? pre.Check(it, l);
string Show(PreflightFinding f) => f.State + (f.Detail is null ? "" : " (" + f.Detail + ")");
var self = Environment.ProcessPath!;
Info("environment", $"{Environment.OSVersion} {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} helper={self}");

// S1 probe kinds and reparse flags (final component)
foreach (var (rel, kind, rp) in new[] { ("ok.txt", ProbeKind.File, false), ("dir", ProbeKind.Directory, false), ("nothing.txt", ProbeKind.NotFound, false), ("link-file.txt", ProbeKind.File, true), ("link-dir", ProbeKind.Directory, true), ("junc-dir", ProbeKind.Directory, true) })
{ var r = probe.Probe(P(rel)); Assert($"probe {rel}", r.Kind == kind && r.Reparse == rp, $"kind={r.Kind} reparse={r.Reparse} reason={r.Reason}"); }

// S2 parent-directory reparse: only the final component is checked (documented limit). Observed values, not an assertion of safety.
foreach (var rel in new[] { @"junc-dir\inner.txt", @"link-dir\inner.txt" })
{ var r = probe.Probe(P(rel)); var f = Classify(It(ItemType.File, P(rel))); Info($"parent-reparse {rel}", $"probe kind={r.Kind} reparse={r.Reparse}; preflight={Show(f)} (a Ready here means a parent junction/symlink is NOT detected)"); }

// S3 classes
void Expect(string name, Item it, PreflightState s, LaunchConfig? l = null) { var f = Classify(it, l); Assert(name, f.State == s, Show(f)); }
Expect("File ok.txt", It(ItemType.File, P("ok.txt")), PreflightState.Ready);
Expect("File missing", It(ItemType.File, P("nothing.txt")), PreflightState.Missing);
Expect("Folder dir", It(ItemType.Folder, P("dir")), PreflightState.Ready);
Expect("File given a folder path", It(ItemType.File, P("dir")), PreflightState.Unsupported);
Expect("File link-file.txt (symlink)", It(ItemType.File, P("link-file.txt")), PreflightState.NeedsDecision);
Expect("Folder link-dir (symlink)", It(ItemType.Folder, P("link-dir")), PreflightState.NeedsDecision);
Expect("Folder junc-dir (junction)", It(ItemType.Folder, P("junc-dir")), PreflightState.NeedsDecision);
Expect("File run.ps1", It(ItemType.File, P("run.ps1")), PreflightState.NeedsDecision);
Expect("File run.cmd", It(ItemType.File, P("run.cmd")), PreflightState.NeedsDecision);
Expect("File run.ps1 WITH explicit OpenWith (no exemption)", It(ItemType.File, P("run.ps1")), PreflightState.NeedsDecision, new LaunchConfig(self));
Expect("File ok.txt with OpenWith = symlinked program", It(ItemType.File, P("ok.txt")), PreflightState.NeedsDecision, new LaunchConfig(P("link-helper.exe")));
Expect("File ok.txt with OpenWith = cmd.exe", It(ItemType.File, P("ok.txt")), PreflightState.NeedsDecision, new LaunchConfig(Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\cmd.exe"));
Expect("Program = symlinked helper", It(ItemType.Program, P("link-helper.exe")), PreflightState.NeedsDecision);
Expect("Link https ok", It(ItemType.Link, "https://example.com/a"), PreflightState.Ready);
Expect("Link userinfo", It(ItemType.Link, "https://user@evil.example/"), PreflightState.NeedsDecision);
Expect("Link file: scheme", It(ItemType.Link, "file:///C:/Windows/System32/calc.exe"), PreflightState.Unsupported);

// S4 real launch of the helper only (Program, exact path, no args, no shell)
var m4 = P("helper-marker.txt"); Environment.SetEnvironmentVariable("STARTAPP_HELPER_MARKER", m4);
var real = new WindowsLauncher(new ProcessStarter());
var lr = real.Launch(It(ItemType.Program, self), null);
Environment.SetEnvironmentVariable("STARTAPP_HELPER_MARKER", null);
var sw = Stopwatch.StartNew(); while (!File.Exists(m4) && sw.Elapsed.TotalSeconds < 15) Thread.Sleep(100);
Assert("real ProcessStarter started the helper", lr.Status == LaunchStatus.Started && lr.Ownership == Ownership.Unknown && File.Exists(m4), $"status={lr.Status} ownership={lr.Ownership} marker={(File.Exists(m4) ? File.ReadAllText(m4) : "absent")} after {sw.ElapsedMilliseconds} ms");
var bad = real.Launch(It(ItemType.Program, @"C:\definitely\not\here.exe"), null);
Assert("real ProcessStarter: missing program -> Failed (exception mapped, not thrown)", bad.Status == LaunchStatus.Failed, $"{bad.Status} {bad.Detail}");

// S5 launcher guard: recording starter, nothing started
var rec = new RecStarter(); var guarded = new WindowsLauncher(rec);
foreach (var (n, it, l) in new (string, Item, LaunchConfig?)[] {
    ("run.ps1", It(ItemType.File, P("run.ps1")), null), ("run.cmd", It(ItemType.File, P("run.cmd")), null),
    ("run.ps1 + OpenWith helper", It(ItemType.File, P("run.ps1")), new LaunchConfig(self)),
    ("link userinfo", It(ItemType.Link, "https://user@evil.example/"), null), ("link file:", It(ItemType.Link, "file:///C:/x.exe"), null) })
{ var before = rec.Calls.Count; var r = guarded.Launch(it, l); Assert($"launcher refuses {n}", r.Status != LaunchStatus.Started && rec.Calls.Count == before, $"{r.Status} {r.Detail}"); }
guarded.Launch(It(ItemType.Link, "https://example.com/a?b=c"), null); guarded.Launch(It(ItemType.Link, "https://example.com/"), new LaunchConfig(null, self)); guarded.Launch(It(ItemType.File, P("ok.txt")), new LaunchConfig(self));
Info("recorded requests (NOT started)", string.Join(" || ", rec.Calls.Select(c => $"{c.FileName} args=[{string.Join(",", c.Arguments)}] shell={c.UseShell}")));
Assert("recorded request shapes", rec.Calls.Count == 3 && rec.Calls[0].UseShell && rec.Calls[0].Arguments.Count == 0 && !rec.Calls[1].UseShell && rec.Calls[1].FileName == self && rec.Calls[1].Arguments.SequenceEqual(["https://example.com/"]) && !rec.Calls[2].UseShell && rec.Calls[2].Arguments.SequenceEqual([P("ok.txt")]), $"count={rec.Calls.Count}");

// S6 view model on Windows: deadline and cancellation with real preflight + synthetic delay (NOT OS-level blocking)
JsonStore Repo(IEnumerable<string> targets) { var r = new JsonStore(Path.Combine(root, "store-" + Guid.NewGuid().ToString("N"), "store.json")); r.Mutate(c => { var w = c.CreateWorkspace("w"); foreach (var t in targets) c.AddToWorkspace(w.Id, c.GetOrAddItem(ItemType.File, t, t).Id); }); return r; }
var many = Enumerable.Range(0, 12).Select(i => { var f = P($"m{i}.txt"); File.WriteAllText(f, "x"); return f; }).ToList();
using (var repo = Repo(many))
{
    var vm = new MainViewModel(repo, new WindowsLauncher(new RecStarter()), new SlowPre(pre, 1500));
    vm.OpenCheckTimeout = TimeSpan.FromMilliseconds(400);
    var t0 = Stopwatch.StartNew(); vm.OpenWorkspaceCommand.Execute(null); var callerMs = t0.ElapsedMilliseconds; vm.OpenTask.Wait(); var doneMs = t0.ElapsedMilliseconds;
    Assert("12 slow checks, 400 ms test deadline (NOT the 10 s production value)", callerMs < 300 && vm.OpenDecisionPending && vm.OpenProblems.Count == 12, $"Execute returned in {callerMs} ms; task done in {doneMs} ms; problems={vm.OpenProblems.Count}; sample='{vm.OpenProblems.FirstOrDefault()}'");
    Assert("unfinished items are 'could not be checked', never Ready", vm.OpenProblems.Count(p => p.Contains("check did not finish in time")) >= 8, $"{vm.OpenProblems.Count(p => p.Contains("check did not finish in time"))} unfinished");
}
using (var repo = Repo(many.Take(6)))
{
    var rs = new RecStarter { DelayMs = 400 }; var vm = new MainViewModel(repo, new WindowsLauncher(rs));
    vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait(); Assert("Unknown duplicate state is warned (no detector)", vm.OpenDecisionPending, vm.Status);
    vm.ConfirmOpenAnywayCommand.Execute(null); Thread.Sleep(700); var launching = vm.IsLaunching; vm.CancelOpenCommand.Execute(null); vm.OpenTask.Wait();
    var sent = vm.OpenReport.Count(l => l.Contains("open request sent")); var notTried = vm.OpenReport.Count(l => l.Contains("not attempted (cancelled"));
    Assert("cancel during launch: sent + not attempted = 6, nothing hidden", launching && sent >= 1 && notTried >= 1 && sent + notTried == 6 && rs.Calls.Count == sent, $"launching={launching} sent={sent} notAttempted={notTried} recorded={rs.Calls.Count} status='{vm.Status}'");
}

// S7 probe bound: 8 in flight, 9th refused, slots come back
{
    var p2 = new FileSystemProbe(TimeSpan.FromMilliseconds(200)); var gate = new ManualResetEventSlim();
    var rs = Enumerable.Range(0, 9).Select(_ => p2.Probe("x", _ => { gate.Wait(5000); return new(ProbeKind.File); })).ToList();
    Assert("probe timeout + in-flight bound", rs.Count(r => r.Reason == "check timed out") == 8 && rs[8].Reason == "too many unfinished checks", string.Join(",", rs.Select(r => r.Reason)));
    gate.Set(); Thread.Sleep(500); var again = p2.Probe("x", _ => new(ProbeKind.File)); Assert("slots released after the blocked calls return", again.Kind == ProbeKind.File, again.Kind.ToString());
}

// S8 nothing dangerous ran
Assert("no script/cmd sample was executed", !File.Exists(P("EXECUTED-ps1.txt")) && !File.Exists(P("EXECUTED-cmd.txt")), "marker files absent");
Directory.CreateDirectory("diag"); File.WriteAllText("diag/harness.json", JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"HARNESS DONE failed={failed}"); return failed == 0 ? 0 : 1;

sealed class RecStarter : IProcessStarter { public List<StartRequest> Calls = new(); public int DelayMs; public void Start(StartRequest r) { if (DelayMs > 0) Thread.Sleep(DelayMs); lock (Calls) Calls.Add(r); } }
sealed class SlowPre(IItemPreflight inner, int ms) : IItemPreflight { public PreflightFinding Check(Item i, LaunchConfig? l) { Thread.Sleep(ms); return inner.Check(i, l); } }
