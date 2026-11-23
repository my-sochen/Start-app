using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Runs launch requests sequentially off the caller thread and records exact outcomes.
/// Cancel only stops later requests. No store, UI state, timestamp, toast or exception display access.</summary>
public static class OpeningLaunchRunner
{
    public static async Task<(List<OpeningResult> Results, Exception? Fault)> RunAsync(
        List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)> plan,
        IItemLauncher launcher, CancellationToken token, HashSet<int>? declined = null)
    {
        var results = new List<OpeningResult>(); Exception? fault = null;
        await Task.Run(() =>
        {
            try { for (int i = 0; i < plan.Count; i++)
            {
                var p = plan[i];
                if (token.IsCancellationRequested) { results.Add(new(p.Item, OpeningOutcome.Cancelled, "לא נשלחה בקשת פתיחה כי הפעולה בוטלה.")); continue; }
                if (declined?.Contains(i) == true) { results.Add(new(p.Item, OpeningOutcome.DeclinedReopen, "כבר פתוח. בחרת שלא לפתוח שוב.")); continue; }
                if (NeverLaunched(p.Finding.State)) { results.Add(new(p.Item, OpeningOutcome.Skipped, SkipExplanation(p.Finding), p.Finding.Detail)); continue; }
                var r = launcher.Launch(p.Item, p.Launch);
                results.Add(r.Status == LaunchStatus.Started
                    ? new(p.Item, OpeningOutcome.RequestSent, "בקשת הפתיחה נשלחה. אין בכך אישור שהפריט נטען.")
                    : new(p.Item, OpeningOutcome.Failed, "בקשת הפתיחה נכשלה. בדקו את הכתובת, ההרשאות והתוכנה שנבחרה ונסו שוב.", r.Detail ?? r.Status.ToString()));
            } }
            catch (Exception e) { fault = e; }
        }).ConfigureAwait(true);
        if (fault != null) foreach (var p in plan.Skip(results.Count)) results.Add(new(p.Item, OpeningOutcome.Failed, "לא נפתח: הפעולה נעצרה בגלל תקלה.", fault.GetType().Name));
        return (results, fault);
    }

    private static bool NeverLaunched(PreflightState s) => s is PreflightState.Missing or PreflightState.Unavailable or PreflightState.Unsupported or PreflightState.NeedsDecision;

    private static string SkipExplanation(PreflightFinding f) => f.State switch
    {
        PreflightState.Missing => "לא נמצא בכתובת השמורה. בדקו את הכתובת ונסו שוב.",
        PreflightState.Unavailable => "לא ניתן היה לבדוק את הפריט בגלל גישה או זמן המתנה. לא נפתח; בדקו הרשאות ונסו שוב.",
        PreflightState.NeedsDecision => "הפריט דורש בדיקה נוספת לפני פתיחה. בדקו את הכתובת והתוכנה שנבחרה.",
        _ => "הפריט או צורת הפתיחה אינם נתמכים. עדכנו את הכתובת או את התוכנה שנבחרה."
    };

}
