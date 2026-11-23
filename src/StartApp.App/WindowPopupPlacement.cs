namespace StartApp.App;

/// <summary>Row action popup geometry in owner-window coordinates, returned relative to the source row.
/// No WPF, dispatcher, controls or view-model dependency. Values preserve the existing measured placement policy.</summary>
internal static class WindowPopupPlacement
{
    public static double AnchorY(double rowY, double rowHeight, double windowHeight) =>
        Math.Clamp(rowY + Math.Min(32, rowHeight > 0 ? rowHeight : 32), 8, Math.Max(8, windowHeight - 8));

    public static (double X, double Y) Offset(double rowX, double rowY, double targetWidth,
        double popupWidth, double popupHeight, double anchorY, double windowWidth, double windowHeight)
    {
        // WPF's callback can under-measure final height. Keep the original 18-DIP safety allowance.
        var safeHeight = popupHeight + 18;
        var x = Math.Clamp(rowX + targetWidth - popupWidth, 8, Math.Max(8, windowWidth - popupWidth - 8));
        var y = anchorY + safeHeight <= windowHeight - 8 ? anchorY : Math.Max(8, anchorY - safeHeight);
        var maxY = Math.Max(8, windowHeight - 8 - safeHeight);
        if (y > maxY) y = maxY;
        return (x - rowX, y - rowY);
    }
}
