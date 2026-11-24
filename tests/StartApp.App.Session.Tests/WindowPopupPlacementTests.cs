using StartApp.App;

public class WindowPopupPlacementTests
{
    [Theory]
    [InlineData(100, 40, 650, 132)] [InlineData(100, 20, 650, 120)]
    [InlineData(100, 0, 650, 132)] [InlineData(100, -1, 650, 132)]
    [InlineData(-100, 20, 650, 8)] [InlineData(700, 32, 650, 642)]
    [InlineData(0, 32, 10, 8)]
    public void Anchor_uses_row_cap_virtualization_fallback_and_owner_bounds(double rowY, double rowHeight, double windowHeight, double expected) =>
        Assert.Equal(expected, WindowPopupPlacement.AnchorY(rowY, rowHeight, windowHeight));

    [Theory]
    [InlineData(100, 100, 300, 200, 100, 132, 960, 650, 100, 32)]
    [InlineData(800, 100, 300, 200, 100, 132, 960, 650, -48, 32)]
    [InlineData(-40, 100, 10, 200, 100, 132, 960, 650, 48, 32)]
    [InlineData(100, 500, 300, 200, 150, 532, 960, 650, 100, -136)]
    [InlineData(100, 100, 300, 200, 1000, 132, 960, 650, 100, -92)]
    [InlineData(0, 0, 10, 1000, 1000, 8, 20, 20, 8, 8)]
    public void Offset_right_aligns_flips_and_clamps_relative_to_row(double x, double y, double targetWidth, double popupWidth,
        double popupHeight, double anchor, double width, double height, double expectedX, double expectedY)
    {
        var actual = WindowPopupPlacement.Offset(x, y, targetWidth, popupWidth, popupHeight, anchor, width, height);
        Assert.Equal((expectedX, expectedY), actual);
    }
    [Fact] public void Exact_bottom_fit_stays_below_and_one_more_pixel_flips_upward()
    {
        var fits = WindowPopupPlacement.Offset(10, 100, 300, 200, 114, 200, 960, 340);
        var overflow = WindowPopupPlacement.Offset(10, 100, 300, 200, 115, 200, 960, 340);
        Assert.Equal(100, fits.Y); Assert.Equal(-33, overflow.Y);
    }
}
