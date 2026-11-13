// 2026-11-11：StrongholdAssistant 移除商店标记测试，仅保留窗口几何测试。
// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class WindowGeometryTests
{
    [Theory]
    [InlineData(1.0, 1.25, 0, 1920, 1000, 475)]
    [InlineData(1.25, 1.25, 1920, 3920, 1250, 475)]
    public void MixedDpiFollowAndOverlayUsePhysicalGameAndPanelDpi(
        double gameDpi, double panelDpi, int gameLeft, int panelLeft, int height, int width)
    {
        var game = new RECT { Left = gameLeft, Top = 0, Right = gameLeft + (int)(1600 * gameDpi), Bottom = height };
        var panelWork = new RECT { Left = panelLeft, Top = 0, Right = panelLeft + 1080, Bottom = 1920 };
        var offset = new ASPAssistant.Core.Models.SidePanelOffset((panelLeft - game.Right) / panelDpi, 0);
        var scale = WindowGeometry.UiScale(null, height, panelDpi);
        var panel = WindowGeometry.FollowBounds(game, panelWork, panelDpi, 380 * scale, null, false, offset);
        Assert.Equal((panelLeft, 0, width, height), (panel.Left, panel.Top, panel.Width, panel.Height));
        var strip = WindowGeometry.CollapseBounds(panel, (int)(48 * panelDpi), null, false);
        Assert.Equal((panelLeft, 60, height), (strip.Left, strip.Width, strip.Height));
        var overlay = WindowGeometry.OverlayBounds(game, gameDpi, 380, null);
        Assert.Equal((gameLeft, (int)Math.Round(height * .15), (int)(380 * gameDpi), (int)Math.Round(height * .97)),
            (overlay.Left, overlay.Top, overlay.Width, overlay.Bottom));
    }

    [Theory]
    [InlineData("auto", 500, 1500, false, "right")]
    [InlineData("auto", 500, 1800, false, "left")]
    [InlineData("auto", 100, 1800, true, "left")]
    [InlineData("left", 500, 1500, false, "left")]
    [InlineData("left", 100, 1500, true, "left")]
    [InlineData("right", 500, 1800, true, "left")]
    public void ChoosesOnlyAllowedDockSides(string preference, int left, int right, bool overlay, string side)
    {
        var game = new RECT { Left = left, Top = 50, Right = right, Bottom = 950 };
        var work = new RECT { Right = 1920, Bottom = 1040 };
        Assert.Equal((overlay, side), WindowGeometry.SelectSide(game, work, 1, 380, false, preference, "left"));
        if (!overlay)
        {
            var bounds = WindowGeometry.SidePanelBounds(game, work, 1, 380, null, false, side);
            Assert.Equal(side == "left" ? game.Left : game.Right, side == "left" ? bounds.Right : bounds.Left);
            Assert.Equal(game.Top, bounds.Top);
        }
    }

    [Fact]
    public void RightOverlayUsesClientRightAndVerticalRatios()
    {
        var client = new RECT { Left = 100, Top = 50, Right = 1700, Bottom = 1050 };
        var bounds = WindowGeometry.OverlayBounds(client, 1.5, 380, null, "right");
        Assert.Equal((1700, 200, 1020, 570), (bounds.Right, bounds.Top, bounds.Bottom, bounds.Width));
    }

    [Theory]
    [InlineData(false, "left", true)]
    [InlineData(true, "right", true)]
    [InlineData(false, "right", false)]
    [InlineData(true, "left", false)]
    public void CollapsedStripKeepsGameFacingEdge(bool overlay, string side, bool anchorRight)
    {
        var expanded = new RECT { Left = 400, Top = 100, Right = 780, Bottom = 900 };
        var strip = WindowGeometry.CollapseBounds(expanded, 48, null, WindowGeometry.AnchorsRight(overlay, side));
        Assert.Equal(anchorRight ? expanded.Right : expanded.Left, anchorRight ? strip.Right : strip.Left);
        Assert.Equal(48, strip.Width);
        Assert.Equal((100, 900), (strip.Top, strip.Bottom));
    }

    [Theory]
    [InlineData(1600, 1024, true)]
    [InlineData(1620, 1080, true)]
    [InlineData(1920, 1080, true)]
    [InlineData(2560, 1080, false)]
    [InlineData(1440, 1080, false)]
    public void AcceptsGameAspectRatiosFromThreeTwoToSixteenNine(int width, int height, bool expected)
        => Assert.Equal(expected, WindowGeometry.IsSupportedGameAspectRatio(width, height));

    [Fact]
    public void TallMumuImageUsesItsActualAspectRatioInsideViewport()
    {
        var actual = WindowGeometry.FitAspectRatio(new RECT { Right = 1800, Bottom = 1024 }, 1600, 1024);
        Assert.Equal((100, 0, 1600, 1024), (actual.Left, actual.Top, actual.Width, actual.Height));
    }

    [Theory]
    [InlineData(2000, 900, 200, 0, 1600, 900)]
    [InlineData(1600, 1200, 0, 150, 1600, 900)]
    [InlineData(1600, 900, 0, 0, 1600, 900)]
    public void FitsGameInsideLetterboxedViewport(int width, int height, int x, int y, int w, int h)
    {
        var actual = WindowGeometry.FitAspectRatio(new RECT { Left = 100, Top = 50, Right = 100 + width, Bottom = 50 + height }, 16, 9);
        Assert.Equal((100 + x, 50 + y, w, h), (actual.Left, actual.Top, actual.Width, actual.Height));
    }

    [Fact]
    public void CollapseModesPreserveExpandedDimensionsAndRespectScale()
    {
        WindowGeometry.CollapsedSize(500, 900, 1.25, true, null).Should().Be((60d, 900d));
        WindowGeometry.CollapsedSize(500, 900, 1.25, true, 180).Should().Be((500d, 225d));
        WindowGeometry.CollapsedSize(500, 900, 1.25, true, 240).Should().Be((500d, 300d));
        WindowGeometry.CollapsedSize(500, 900, 1.25, false, null).Should().Be((500d, 900d));
        var game = new RECT { Left = 0, Top = 60, Right = 1200, Bottom = 960 };
        var work = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        var strip = WindowGeometry.SidePanelBounds(game, work, 1.25, 60, null, false);
        strip.Left.Should().Be(game.Right);
        strip.Top.Should().Be(game.Top);
        WindowGeometry.CenterFreePosition(400, 600, work, 1.25).Should().Be((568d, 116d));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void SidePanel_AttachesUsingItsScaledPhysicalWidth(double scale)
    {
        var game = new RECT { Left = 100, Top = 60, Right = 1600, Bottom = 960 };
        var work = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        var result = WindowGeometry.SidePanelBounds(game, work, scale, 310, null, false);
        result.Width.Should().Be((int)Math.Round(310 * scale));
        result.Top.Should().Be(60);
        result.Height.Should().Be(900);
        if (scale == 1)
            result.Left.Should().Be(game.Right);
        else
            result.Right.Should().Be(game.Right);
        result.Left.Should().BeGreaterThanOrEqualTo(work.Left);
        result.Right.Should().BeLessThanOrEqualTo(work.Right);
        result.Bottom.Should().BeLessThanOrEqualTo(work.Bottom);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Fullscreen_PreservesPhysicalHeightRatiosAndFitsWorkArea(double scale)
    {
        var game = new RECT { Left = 1920, Top = 0, Right = 4480, Bottom = 1440 };
        var work = new RECT { Left = 1920, Top = 0, Right = 4480, Bottom = 1400 };
        var result = WindowGeometry.SidePanelBounds(game, work, scale, 310, null, true);
        result.Right.Should().Be(4480);
        result.Top.Should().Be((int)Math.Round(1440 * 0.22));
        result.Height.Should().Be((int)Math.Round(1440 * 0.46));
        result.Width.Should().Be((int)Math.Round(310 * scale));
    }

    [Fact]
    public void MixedDpiMonitors_KeepDesktopOriginsAndUseIndependentPanelScales()
    {
        var firstWork = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        var secondWork = new RECT { Left = 1920, Top = -200, Right = 4480, Bottom = 1200 };
        var first = WindowGeometry.SidePanelBounds(
            new RECT { Left = 100, Top = 60, Right = 1800, Bottom = 960 }, firstWork, 1.25, 310, null, false);
        var second = WindowGeometry.SidePanelBounds(
            new RECT { Left = 2020, Top = -100, Right = 4360, Bottom = 1100 }, secondWork, 1.75, 310, null, false);
        first.Right.Should().Be(1800);
        first.Width.Should().Be(388);
        second.Right.Should().Be(4360);
        second.Top.Should().Be(-100);
        second.Width.Should().Be(542);
        second.Left.Should().BeGreaterThanOrEqualTo(secondWork.Left);
    }

    [Fact]
    public void NegativeMonitorOriginAndManualHeight_AreNotScaledAsDesktopCoordinates()
    {
        var work = new RECT { Left = -2560, Top = -200, Right = 0, Bottom = 1200 };
        var game = new RECT { Left = -2400, Top = -100, Right = -100, Bottom = 1100 };
        var result = WindowGeometry.SidePanelBounds(game, work, 1.75, 310, 400, false);
        result.Right.Should().Be(-100);
        result.Top.Should().Be(-100);
        result.Height.Should().Be(700);
        result.Width.Should().Be(542);
        var moved = WindowGeometry.ClampBounds(new RECT { Left = 7000, Top = -800, Right = 7542, Bottom = -100 }, work);
        moved.Right.Should().Be(work.Right);
        moved.Top.Should().Be(work.Top);
    }

    [Fact]
    public void WorkArea_ClampsOversizedOrOffscreenPanel()
    {
        var work = new RECT { Left = 0, Top = 40, Right = 1280, Bottom = 680 };
        var game = new RECT { Left = -100, Top = -100, Right = 1400, Bottom = 1000 };
        var result = WindowGeometry.SidePanelBounds(game, work, 2, 400, 900, false);
        result.Left.Should().BeGreaterThanOrEqualTo(0);
        result.Right.Should().BeLessThanOrEqualTo(1280);
        result.Top.Should().Be(40);
        result.Bottom.Should().Be(680);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void RememberedOffset_FollowsGameTranslationAtEachDpi(double scale)
    {
        var game = new RECT { Left = 100, Top = 100, Right = 1000, Bottom = 900 };
        var placement = new ASPAssistant.Core.Models.SidePanelPlacement(300, 200, 310, 300);
        var offset = WindowGeometry.CaptureFollowOffset(game, placement, scale);
        var work = new RECT { Left = -2560, Top = -1440, Right = 4480, Bottom = 2160 };
        var result = WindowGeometry.FollowBounds(
            new RECT { Left = 130, Top = 120, Right = 1030, Bottom = 920 }, work, scale, 310, 300, false, offset);
        result.Left.Should().Be(330);
        result.Top.Should().Be(220);
        result.Width.Should().Be((int)Math.Round(310 * scale));
        result.Height.Should().Be((int)Math.Round(300 * scale));
    }

    [Fact]
    public void RememberedOffset_ChangesScreenDpiWithoutScalingDesktopOrigin()
    {
        var game = new RECT { Left = 100, Top = 100, Right = 1000, Bottom = 900 };
        var offset = WindowGeometry.CaptureFollowOffset(game,
            new ASPAssistant.Core.Models.SidePanelPlacement(1025, 150, 310, 300), 1.25);
        var moved = WindowGeometry.FollowBounds(
            new RECT { Left = 2020, Top = -100, Right = 2920, Bottom = 700 },
            new RECT { Left = 1920, Top = -200, Right = 4480, Bottom = 1200 }, 1.75, 310, 300, true, offset);
        moved.Left.Should().Be(2955);
        moved.Top.Should().Be(-30);
    }

    [Fact]
    public void RestoreFreePlacement_ClampsRemovedMonitorAndOversizedHeight()
    {
        var result = WindowGeometry.RestoreBounds(
            new ASPAssistant.Core.Models.SidePanelPlacement(-5000, 6000, 310, 1e300),
            new RECT { Left = 0, Top = 40, Right = 1920, Bottom = 1040 }, 1.75);
        result.Left.Should().Be(0);
        result.Top.Should().Be(40);
        result.Bottom.Should().Be(1040);
        result.Width.Should().Be(542);
    }
}
