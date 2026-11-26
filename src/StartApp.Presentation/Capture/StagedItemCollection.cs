using System.Collections.ObjectModel;

namespace StartApp.Presentation;

/// <summary>Owns staged row order and same-display-group movement. The host still creates, edits and saves rows.
/// Group selection is supplied by the host, so this owner has no nested row or MainViewModel dependency.</summary>
public sealed class StagedItemCollection<TRow>(Func<TRow, string> groupOf)
{
    public ObservableCollection<TRow> Rows { get; } = new();

    /// <summary>Move one position in its group, skipping interleaved groups. Magnitude is ignored, matching row commands.</summary>
    public bool MoveWithinGroup(TRow row, int delta)
    {
        var index = Rows.IndexOf(row);
        if (index < 0 || delta == 0) return false;
        var group = groupOf(row);
        var step = Math.Sign(delta);
        var next = index + step;
        while (next >= 0 && next < Rows.Count && groupOf(Rows[next]) != group) next += step;
        if (next < 0 || next >= Rows.Count) return false;
        Rows.Move(index, next);
        return true;
    }
}
