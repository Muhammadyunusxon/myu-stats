namespace MYUStats.Tests;

public class EdgeLayoutTests
{
    /// <summary>A 1000 × 800 DIP work area.</summary>
    private static readonly SizeD Area = new(1000, 800);

    private static EdgeLayout Layout(ScreenEdge edge, int cells = 7) => new(edge, cells, Area);

    [Fact]
    public void ThePillIsCentredOnItsEdge()
    {
        var layout = Layout(ScreenEdge.Right);
        Assert.Equal(7 * 72 + 2 * (34 + 5), layout.PillLength);
        var pill = layout.PillRect(0);
        Assert.Equal(new RectD(1000 - 54, (800 - layout.PillLength) / 2, 54, layout.PillLength), pill);
    }

    [Theory]
    [InlineData(ScreenEdge.Left)]
    [InlineData(ScreenEdge.Right)]
    [InlineData(ScreenEdge.Top)]
    [InlineData(ScreenEdge.Bottom)]
    public void ThePillTouchesItsEdge(ScreenEdge edge)
    {
        var pill = Layout(edge).PillRect(0);
        switch (edge)
        {
            case ScreenEdge.Left: Assert.Equal(0, pill.X); break;
            case ScreenEdge.Right: Assert.Equal(Area.Width, pill.Right); break;
            case ScreenEdge.Top: Assert.Equal(0, pill.Y); break;
            case ScreenEdge.Bottom: Assert.Equal(Area.Height, pill.Bottom); break;
        }
    }

    [Fact]
    public void AnOffsetNudgesThePillButNeverOffScreen()
    {
        var layout = Layout(ScreenEdge.Left);
        Assert.Equal(layout.PillRect(0).Y + 50, layout.PillRect(50).Y);
        Assert.Equal(0, layout.PillRect(-5_000).Y);
        Assert.Equal(Area.Height, layout.PillRect(5_000).Bottom);
    }

    [Fact]
    public void ALongPillOnASmallScreenStartsAtTheTop()
    {
        var layout = new EdgeLayout(ScreenEdge.Right, 7, new SizeD(1000, 400));
        Assert.Equal(0, layout.PillRect(100).Y);
    }

    [Fact]
    public void OffsetOfUndoesTheClamp()
    {
        var layout = Layout(ScreenEdge.Bottom);
        Assert.Equal(120, layout.OffsetOf(layout.PillRect(120)), 6);
        var clamped = layout.PillRect(9_999);
        Assert.Equal(clamped, layout.PillRect(layout.OffsetOf(clamped)));
    }

    [Theory]
    [InlineData(ScreenEdge.Right)]
    [InlineData(ScreenEdge.Top)]
    public void CellsSitBetweenTheTwoButtonsWithoutGaps(ScreenEdge edge)
    {
        var layout = Layout(edge, 3);
        var move = layout.ButtonRect(0, PillButton.Move);
        var settings = layout.ButtonRect(0, PillButton.Settings);
        var cells = Enumerable.Range(0, 3).Select(index => layout.CellRect(0, index)).ToList();
        if (layout.IsVertical)
        {
            Assert.Equal(move.Bottom, cells[0].Y);
            Assert.Equal(cells[0].Bottom, cells[1].Y);
            Assert.Equal(cells[2].Bottom, settings.Y);
            Assert.Equal(layout.PillRect(0).Bottom, settings.Bottom + EdgeLayout.BodyPadding, 6);
        }
        else
        {
            Assert.Equal(move.Right, cells[0].X);
            Assert.Equal(cells[0].Right, cells[1].X);
            Assert.Equal(cells[2].Right, settings.X);
        }
    }

    [Fact]
    public void HitTestingFindsCellsAndButtons()
    {
        var layout = Layout(ScreenEdge.Right, 3);
        var cell = layout.CellRect(0, 1);
        Assert.Equal(1, layout.CellAt(0, new PointD(cell.X + 10, cell.Y + 10)));
        Assert.Null(layout.CellAt(0, new PointD(10, 10)));
        var gear = layout.ButtonRect(0, PillButton.Settings);
        Assert.Equal(PillButton.Settings, layout.ButtonAt(0, new PointD(gear.X + 5, gear.Y + 5)));
        Assert.Null(layout.ButtonAt(0, new PointD(cell.X + 5, cell.Y + 5)));
    }

    [Fact]
    public void TheTabSitsInTheMiddleOfTheStrip()
    {
        var layout = Layout(ScreenEdge.Right);
        var strip = layout.StripRect(0);
        var tab = layout.TabRect(0);
        Assert.Equal(layout.PillRect(0).Y, strip.Y);
        Assert.Equal(layout.PillLength, strip.Height);
        Assert.Equal(EdgeLayout.TabDepth, strip.Width);
        Assert.Equal(strip.Y + (strip.Height - EdgeLayout.TabLength) / 2, tab.Y, 6);
        Assert.Equal(Area.Width, tab.Right);
    }

    [Fact]
    public void TheCardSitsBesideItsCellAndStaysOnScreen()
    {
        var layout = Layout(ScreenEdge.Right);
        var card = layout.CardRect(0, 3, 300);
        Assert.Equal(Area.Width - 54 - EdgeLayout.CardGap - EdgeLayout.CardWidth, card.X);
        Assert.Equal(EdgeLayout.CardWidth, card.Width);
        var cell = layout.CellRect(0, 3);
        Assert.Equal(cell.Y + cell.Height / 2, card.Y + card.Height / 2, 6);

        var tall = layout.CardRect(0, 0, 790);
        Assert.Equal(EdgeLayout.CardMargin, tall.Y);
        var top = Layout(ScreenEdge.Top).CardRect(0, 0, 200);
        Assert.Equal(EdgeLayout.HorizontalDepth + EdgeLayout.CardGap, top.Y);
        Assert.Equal(200, top.Height);
    }

    [Fact]
    public void TheCardOnTheBottomEdgeOpensUpwards()
    {
        var layout = Layout(ScreenEdge.Bottom);
        var card = layout.CardRect(0, 2, 180);
        Assert.Equal(Area.Height - layout.Depth - EdgeLayout.CardGap, card.Bottom);
    }

    [Theory]
    [InlineData(20, 400, ScreenEdge.Left)]
    [InlineData(980, 400, ScreenEdge.Right)]
    [InlineData(500, 10, ScreenEdge.Top)]
    [InlineData(500, 790, ScreenEdge.Bottom)]
    public void DraggingTowardsAnEdgeArmsIt(double x, double y, ScreenEdge expected) =>
        Assert.Equal(expected, EdgeLayout.DropTarget(new PointD(x, y), Area, ScreenEdge.Right == expected ? ScreenEdge.Left : ScreenEdge.Right));

    [Fact]
    public void TheMiddleOfTheScreenAndTheCurrentEdgeArmNothing()
    {
        Assert.Null(EdgeLayout.DropTarget(new PointD(500, 400), Area, ScreenEdge.Right));
        Assert.Null(EdgeLayout.DropTarget(new PointD(990, 400), Area, ScreenEdge.Right));
    }

    [Fact]
    public void DropZonesFollowTheCursorAndStayOnScreen()
    {
        var right = EdgeLayout.DropZoneRect(ScreenEdge.Right, new PointD(500, 790), Area);
        Assert.Equal(new RectD(1000 - 64, 800 - 180, 64, 180), right);
        var top = EdgeLayout.DropZoneRect(ScreenEdge.Top, new PointD(10, 300), Area);
        Assert.Equal(new RectD(0, 0, 180, 64), top);
    }

    [Fact]
    public void LandingCentresThePillOnTheCursor()
    {
        var offset = EdgeLayout.LandingOffset(ScreenEdge.Left, new PointD(20, 300), Area);
        var pill = Layout(ScreenEdge.Left).PillRect(offset);
        Assert.Equal(300, pill.Y + pill.Height / 2, 6);
    }
}
