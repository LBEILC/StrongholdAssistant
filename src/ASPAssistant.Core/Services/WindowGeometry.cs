// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
// 2026-11-12：支持 3:2 到 16:9，保留上边界原有的两像素舍入容差。
// 2026-11-12：移植上游 PR #5 的 FitAspectRatio 和横屏比例判断。
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public static class WindowGeometry
{
    public static bool IsSupportedGameAspectRatio(int width, int height)
        => width > 0 && height > 0 && width >= height * 1.5 && width <= height * 16.0 / 9 + 2;

    /// <summary>按画面比例居中裁去显示区域的黑边，坐标为物理像素。</summary>
    public static RECT FitAspectRatio(RECT bounds, int imageWidth, int imageHeight)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        var scale = Math.Min((double)bounds.Width / imageWidth, (double)bounds.Height / imageHeight);
        var width = (int)Math.Round(imageWidth * scale);
        var height = (int)Math.Round(imageHeight * scale);
        var left = bounds.Left + (bounds.Width - width) / 2;
        var top = bounds.Top + (bounds.Height - height) / 2;
        return new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    public static (double Width, double Height) CollapsedSize(double width, double height,
        double uiScale, bool collapsed, double? upHeight)
        => !collapsed ? (width, height) : upHeight is { } h ? (width, h * uiScale) : (48 * uiScale, height);

    public static (double Left, double Top) CenterFreePosition(double widthDip, double heightDip, RECT workArea, double dpiScale)
        => ((workArea.Left + (workArea.Width - widthDip * dpiScale) / 2) / dpiScale,
            (workArea.Top + (workArea.Height - heightDip * dpiScale) / 2) / dpiScale);

    public static bool ShouldHideForCapture(RECT sidebar, RECT client, bool collapsed)
        => !collapsed && sidebar.Left < client.Right && sidebar.Right > client.Left
            && sidebar.Top < client.Bottom && sidebar.Bottom > client.Top;

    public static double UiScale(double? manualScale, int? clientHeight, double dpiScale)
        => manualScale ?? (clientHeight is { } height ? Math.Clamp(height / dpiScale / 1080, 1, 2) : 1);

    public static double PanelWidth(double widthDip, double uiScale, bool collapsed)
        => (collapsed ? 48 : widthDip) * uiScale;

    // 使用展开宽度选边，避免收起后改变贴靠模式。
    public static (bool Overlay, string Side) SelectSide(RECT game, RECT workArea, double dpiScale,
        double expandedWidthDip, bool fullscreen, string dockSide, string overlaySide)
    {
        var width = (int)Math.Round(expandedWidthDip * dpiScale);
        if (!fullscreen)
        {
            if (dockSide != "left" && workArea.Right - game.Right >= width) return (false, "right");
            if (dockSide != "right" && game.Left - workArea.Left >= width) return (false, "left");
        }
        return (true, overlaySide);
    }

    public static bool AnchorsRight(bool overlay, string side) => overlay ? side == "right" : side == "left";

    public static RECT CollapseBounds(RECT expanded, int width, int? upHeight, bool anchorRight)
    {
        if (upHeight is { } height) expanded.Bottom = expanded.Top + height;
        else if (anchorRight) expanded.Left = expanded.Right - width;
        else expanded.Right = expanded.Left + width;
        return expanded;
    }

    public static bool NeedsOverlay(RECT game, RECT workArea, double dpiScale, double expandedWidthDip, bool fullscreen)
        => fullscreen || workArea.Right - game.Right < (int)Math.Round(expandedWidthDip * dpiScale);

    public static RECT OverlayBounds(RECT client, double dpiScale, double widthDip, SidePanelOffset? offset, string side = "left")
    {
        var origin = side == "right" ? client.Right - (int)Math.Round(widthDip * dpiScale) : client.Left;
        var left = origin + (int)Math.Round((offset?.X ?? 0) * dpiScale);
        var top = offset is null ? (int)Math.Round(client.Top + client.Height * .15)
            : (int)Math.Round(client.Top + offset.Y * dpiScale);
        var height = (int)Math.Round(client.Height * .97) - (int)Math.Round(client.Height * .15);
        return ClampBounds(new RECT { Left = left, Top = top,
            Right = left + (int)Math.Round(widthDip * dpiScale), Bottom = top + height }, client);
    }

    /// <summary>游戏和工作区使用物理像素；侧栏宽度、手动高度使用 WPF DIP。</summary>
    public static RECT SidePanelBounds(RECT game, RECT workArea, double dpiScale,
        double widthDip, double? manualHeightDip, bool fullscreen, string side = "right")
    {
        var width = Math.Min((int)Math.Round(widthDip * dpiScale), workArea.Width);
        var height = (int)Math.Round(Math.Min(manualHeightDip is { } heightDip
            ? heightDip * dpiScale : game.Height * (fullscreen ? 0.46 : 1), workArea.Height));
        var left = side == "left" ? game.Left - width : !fullscreen && workArea.Right - game.Right >= width
            ? game.Right : game.Right - width;
        var top = (int)Math.Round(game.Top + (fullscreen ? game.Height * 0.22 : 0));
        return ClampBounds(new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height }, workArea);
    }

    public static RECT ClampBounds(RECT bounds, RECT workArea)
    {
        var width = Math.Min(bounds.Width, workArea.Width);
        var height = Math.Min(bounds.Height, workArea.Height);
        var left = Math.Clamp(bounds.Left, workArea.Left, workArea.Right - width);
        var top = Math.Clamp(bounds.Top, workArea.Top, workArea.Bottom - height);
        return new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    public static SidePanelOffset CaptureFollowOffset(RECT game, SidePanelPlacement placement, double dpiScale, string side = "right")
        => new((placement.Left - (side == "left" ? game.Left - placement.WidthDip * dpiScale : game.Right)) / dpiScale,
            (placement.Top - game.Top) / dpiScale);

    // 横纵分别按 DIP 吸附；两轴都回到默认位置时不保留手动偏移。
    public static SidePanelOffset? SnapOffset(SidePanelOffset offset, SidePanelOffset? defaultOffset = null)
    {
        var target = defaultOffset ?? new SidePanelOffset(0, 0);
        var x = Math.Abs(offset.X - target.X) <= 40 ? target.X : offset.X;
        var y = Math.Abs(offset.Y - target.Y) <= 40 ? target.Y : offset.Y;
        return x == target.X && y == target.Y ? null : new SidePanelOffset(x, y);
    }

    public static SidePanelOffset? SnapOverlayOffset(SidePanelOffset offset, RECT client,
        double dpiScale, double widthDip, string side = "left")
    {
        var bounds = OverlayBounds(client, dpiScale, widthDip, null, side);
        var origin = side == "right" ? client.Right - (int)Math.Round(widthDip * dpiScale) : client.Left;
        return SnapOffset(offset, new SidePanelOffset((bounds.Left - origin) / dpiScale,
            (bounds.Top - client.Top) / dpiScale));
    }

    public static RECT FollowBounds(RECT game, RECT workArea, double dpiScale, double widthDip,
        double? heightDip, bool fullscreen, SidePanelOffset offset, string side = "right")
    {
        var size = SidePanelBounds(game, workArea, dpiScale, widthDip, heightDip, fullscreen, side);
        var origin = side == "left" ? game.Left - size.Width : game.Right;
        var left = (int)Math.Round(Math.Clamp(origin + offset.X * dpiScale, workArea.Left, workArea.Right - size.Width));
        var top = (int)Math.Round(Math.Clamp(game.Top + offset.Y * dpiScale, workArea.Top, workArea.Bottom - size.Height));
        return new RECT { Left = left, Top = top, Right = left + size.Width, Bottom = top + size.Height };
    }

    public static RECT RestoreBounds(SidePanelPlacement placement, RECT workArea, double dpiScale)
    {
        var width = (int)Math.Round(Math.Min(placement.WidthDip * dpiScale, workArea.Width));
        var height = (int)Math.Round(Math.Min(placement.HeightDip * dpiScale, workArea.Height));
        var left = (int)Math.Round(Math.Clamp(placement.Left, workArea.Left, workArea.Right - width));
        var top = (int)Math.Round(Math.Clamp(placement.Top, workArea.Top, workArea.Bottom - height));
        return new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

}
