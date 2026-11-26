using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Runs launch requests off the caller thread and records exact outcomes. Manual runs use one sequential queue;
/// sign-in uses independent sequential local/site queues so network readiness or a blocked local start cannot hold sites.
/// Cancel only stops later requests. No store, UI state, timestamp, toast or exception display access.</summary>
public static class OpeningLaunchRunner
{
    public static async Task<(List<OpeningResult> Results, Exception? Fault)> RunAsync(
        List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)> plan,
        IItemLauncher launcher, CancellationToken token, HashSet<int>? declined = null, Func<Item, CancellationToken, Task>? beforeLaunch = null)
    {
        var results = new OpeningResult?[plan.Count]; Exception? fault = null;
        async Task RunIndices(IEnumerable<int> indices)
        {
            foreach (var i in indices)
            {
                var p = plan[i];
                try
                {
                    if (token.IsCancellationRequested) { results[i] = new(p.Item, OpeningOutcome.Cancelled, "לא נשלחה בקשת פתיחה כי הפעולה בוטלה."); continue; }
                    if (declined?.Contains(i) == true) { results[i] = new(p.Item, OpeningOutcome.DeclinedReopen, "כבר פתוח. לא נשלחה בקשת פתיחה נוספת."); continue; }
                    if (NeverLaunched(p.Finding.State)) { results[i] = new(p.Item, OpeningOutcome.Skipped, SkipExplanation(p.Finding), p.Finding.Detail); continue; }
                    if (beforeLaunch is not null) await beforeLaunch(p.Item, token);
                    var r = await Task.Run(() => launcher.Launch(p.Item, p.Launch));
                    results[i] = r.Status == LaunchStatus.Started
                        ? new(p.Item, OpeningOutcome.RequestSent, "בקשת הפתיחה נשלחה. אין בכך אישור שהפריט נטען.")
                        : new(p.Item, OpeningOutcome.Failed, "בקשת הפתיחה נכשלה. בדקו את הכתובת, ההרשאות והתוכנה שנבחרה ונסו שוב.", r.Detail ?? r.Status.ToString());
                }
                catch (OperationCanceledException) when (beforeLaunch is not null || token.IsCancellationRequested)
                { results[i] = new(p.Item, OpeningOutcome.Cancelled, "לא נשלחה בקשת פתיחה כי הפעולה בוטלה."); }
                catch (Exception e) { fault ??= e; break; }
            }
        }
        if (beforeLaunch is null) await RunIndices(Enumerable.Range(0, plan.Count));
        else await Task.WhenAll(RunIndices(Enumerable.Range(0, plan.Count).Where(i=>plan[i].Item.Type!=ItemType.Link)), RunIndices(Enumerable.Range(0, plan.Count).Where(i=>plan[i].Item.Type==ItemType.Link)));
        for (var i=0;i<results.Length;i++) results[i] ??= new(plan[i].Item, OpeningOutcome.Failed, "לא נפתח: הפעולה נעצרה בגלל תקלה.", fault?.GetType().Name);
        return (results.Select(x=>x!).ToList(), fault);
    }

    internal static bool NeverLaunched(PreflightState s) => s is PreflightState.Missing or PreflightState.Unavailable or PreflightState.Unsupported or PreflightState.NeedsDecision;

    internal static string SkipExplanation(PreflightFinding f) => f.State switch
    {
        PreflightState.Missing => "לא נמצא בכתובת השמורה. בדקו את הכתובת ונסו שוב.",
        PreflightState.Unavailable => "לא ניתן היה לבדוק את הפריט בגלל גישה או זמן המתנה. לא נפתח; בדקו הרשאות ונסו שוב.",
        PreflightState.NeedsDecision => "הפריט דורש בדיקה נוספת לפני פתיחה. בדקו את הכתובת והתוכנה שנבחרה.",
        _ => "הפריט או צורת הפתיחה אינם נתמכים. עדכנו את הכתובת או את התוכנה שנבחרה."
    };

}
