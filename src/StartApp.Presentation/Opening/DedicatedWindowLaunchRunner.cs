using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Groups only eligible websites, in plan order, per resolved browser. Other item types retain the normal
/// launcher. A batch is atomic at submission: cancellation stops later batches, never pretends submitted tabs were cancelled.</summary>
public static class DedicatedWindowLaunchRunner
{
    public static async Task<(List<OpeningResult> Results, Exception? Fault)> RunAsync(
        List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)> plan,
        IItemLauncher launcher, CancellationToken token, HashSet<int>? declined = null)
    {
        var rows = new OpeningResult?[plan.Count];
        Exception? fault = null;
        await Task.Run(() =>
        {
            var targets = new Dictionary<int, BrowserWindowTarget>();
            try
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    var p = plan[i];
                    if (token.IsCancellationRequested) { rows[i] = Cancelled(p.Item); continue; }
                    if (declined?.Contains(i) == true) { rows[i] = new(p.Item, OpeningOutcome.DeclinedReopen, "כבר פתוח. בחרת שלא לפתוח שוב."); continue; }
                    if (OpeningLaunchRunner.NeverLaunched(p.Finding.State)) { rows[i] = new(p.Item, OpeningOutcome.Skipped, OpeningLaunchRunner.SkipExplanation(p.Finding), p.Finding.Detail); continue; }
                    if (p.Item.Type == ItemType.Link)
                    {
                        var target = launcher is IBrowserWindowLauncher browser ? browser.ResolveBrowser(p.Item, p.Launch) : new BrowserWindowTarget(null, "Dedicated browser windows are unavailable");
                        if (target.Executable is null) rows[i] = new(p.Item, OpeningOutcome.Skipped, "לא נפתח בחלון ייעודי. אפשרות זו זמינה לכרום ול-Edge בלבד, ללא החלפת הדפדפן שנבחר.", target.Detail);
                        else targets.Add(i, target);
                    }
                }
                for (int i = 0; i < plan.Count; i++)
                {
                    if (rows[i] is not null) continue;
                    if (token.IsCancellationRequested) { rows[i] = Cancelled(plan[i].Item); continue; }
                    if (targets.TryGetValue(i, out var target))
                    {
                        var indices = targets.Where(x => rows[x.Key] is null && string.Equals(x.Value.Executable, target.Executable, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).Order().ToArray();
                        var result = ((IBrowserWindowLauncher)launcher).LaunchWindow(target.Executable!, indices.Select(index => plan[index].Item).ToArray());
                        foreach (var index in indices) rows[index] = Describe(plan[index].Item, result, true);
                    }
                    else rows[i] = Describe(plan[i].Item, launcher.Launch(plan[i].Item, plan[i].Launch), false);
                }
            }
            catch (Exception e) { fault = e; }
        }).ConfigureAwait(true);
        return (plan.Select((p, i) => rows[i] ?? new OpeningResult(p.Item, OpeningOutcome.Failed, "לא נפתח: הפעולה נעצרה בגלל תקלה.", fault?.GetType().Name)).ToList(), fault);
    }

    private static OpeningResult Cancelled(Item item) => new(item, OpeningOutcome.Cancelled, "לא נשלחה בקשת פתיחה כי הפעולה בוטלה.");
    private static OpeningResult Describe(Item item, LaunchResult result, bool dedicated) => result.Status == LaunchStatus.Started
        ? new(item, OpeningOutcome.RequestSent, dedicated ? "נשלחה בקשת פתיחה בחלון ייעודי, לפי סדר האתרים בסביבה. אין בכך אישור שהאתר נטען." : "בקשת הפתיחה נשלחה. אין בכך אישור שהפריט נטען.")
        : new(item, OpeningOutcome.Failed, "בקשת הפתיחה נכשלה. אפשר לעיין בפרטים ולנסות שוב.", result.Detail ?? result.Status.ToString());
}
