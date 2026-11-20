using System.Diagnostics;

namespace ASPAssistant.Core.Services;

public enum CustomPickCardState { Unknown, Unselected, Banned, Selected }
public sealed record CustomPickCard(int Tier, int Slot, string? Name, CustomPickCardState State);
public sealed record CustomPickReading(CustomPickCard[] Cards, bool IsFinal, double CardsMs)
{
    internal async Task<string> CountAsync(IOcrEngine ocr, byte[] frame, double s, int tier)
    {
        int offset = (tier - 5) * 400;
        var lines = Zoom == 1
            ? await ocr.RecognizeLineAsync(frame, (int)(1832 * s), (int)((300 + offset) * s), (int)(60 * s), (int)(38 * s))
            : await ocr.RecognizeRegionAsync(frame, (int)(1770 * s), (int)(Y(282 + offset, tier) * s),
                (int)(150 * s), (int)(70 * s));
        var count = string.Concat(lines.Select(t => t.Text.Replace(" ", "")));
        return count == "O/2" ? "0/2" : count;
    }
    internal double Zoom { get; init; } = 1;
    internal double X(double x) => 1920 - (1920 - x) * Zoom;
    internal double Y(double y, int tier) => y * Zoom - (tier - 5) * (Zoom - 1) * 540;
}

/// <summary>读取自选编队页；只将有明确最终提示且名单完整的状态交给宿主。</summary>
public sealed class CustomPickReader(IOcrEngine ocr, IReadOnlyList<string> names)
{
    public async Task<CustomPickReading?> ReadAsync(byte[] frame, int width, int height)
    {
        double s = Math.Min(width / 1920d, height / 1080d);
        Task<IReadOnlyList<OcrTextResult>> Read(int x, int y, int w, int h) =>
            ocr.RecognizeRegionAsync(frame, (int)(x * s), (int)(y * s), (int)(w * s), (int)(h * s));
        static string Text(IEnumerable<OcrTextResult> lines) => string.Concat(lines.Select(l => l.Text.Replace(" ", "")));

        var title = await Read(660, 15, 590, 100);
        if (!Text(title).Contains("确定自选编队")) return null;
        // 标题随界面缩放，右端在 UI90 约 1040、UI100 约 1160。自选卡组锚定右侧。
        var layout = new CustomPickReading([], false, 0)
        {
            Zoom = title.Any(l => l.Text.Contains("编队") && l.Box.X + l.Box.W > 1100 * s) ? 100d / 90 : 1
        };
        string status = Text(await Read(950, 575, 350, 70));
        if (status.Contains("未选择自选编队"))
        {
            bool empty5 = Text(await Read(1580, 265, 180, 60)).Contains("NONE", StringComparison.OrdinalIgnoreCase);
            bool empty6 = Text(await Read(1580, 665, 180, 60)).Contains("NONE", StringComparison.OrdinalIgnoreCase);
            return new([], empty5 && empty6, 0);
        }

        var timer = Stopwatch.StartNew();
        var cards = new List<CustomPickCard>();
        if (status.Contains("已选择自选编队"))
        {
            bool complete = true;
            // 单人和四人样图的最终结果区均在右侧，两排相隔 400 像素。
            for (int tier = 5; tier <= 6; tier++)
            {
                int offset = (tier - 5) * 400;
                var labels = (await Read(1500, 375 + offset, 365, 40)).OrderBy(l => l.Box.X).ToArray();
                if (labels.Length == 0)
                {
                    complete &= Text(await Read(1580, 265 + offset, 180, 60)).Contains("NONE", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                complete &= labels.Length <= 2;
                foreach (var label in labels)
                {
                    string text = label.Text.Replace(" ", "");
                    string? name = names.FirstOrDefault(n => n == text);
                    complete &= name is not null;
                    cards.Add(new(tier, (int)((label.Box.X / s - 1500) / 177), name, CustomPickCardState.Selected));
                }
            }
            return new(cards.ToArray(), complete && cards.Count > 0, timer.Elapsed.TotalMilliseconds);
        }
        for (int tier = 5; tier <= 6; tier++)
        {
            int offset = (tier - 5) * 400;
            var lines = await Read((int)layout.X(735), (int)layout.Y(360 + offset, tier),
                (int)(1060 * layout.Zoom), (int)(95 * layout.Zoom));
            bool noneSelected = await layout.CountAsync(ocr, frame, s, tier) == "0/2";
            for (int slot = 0; slot < 6; slot++)
            {
                double left = layout.X(735 + 177 * slot) * s;
                var label = lines.Where(l => l.Box.X >= left && l.Box.X < left + 177 * s * layout.Zoom).ToArray();
                bool banned = Text(label).Contains("本局禁用");
                var fragments = label.Where(l => l.Box.Y >= layout.Y(375 + offset, tier) * s && l.Box.Y < layout.Y(407 + offset, tier) * s)
                    .Select(l => l.Text.Replace(" ", "")).ToArray();
                // 禁用角标会遮住名字前半段，只有剩余文字唯一对应候选时才补全。
                var candidates = names.Where(n => fragments.Any(f => banned ? n.EndsWith(f, StringComparison.Ordinal) : n == f)).ToArray();
                string? name = candidates.Length == 1 ? candidates[0] : null;
                if (name is not null || banned)
                    cards.Add(new(tier, slot, name, banned ? CustomPickCardState.Banned
                        : noneSelected ? CustomPickCardState.Unselected : CustomPickCardState.Unknown));
            }
        }
        // TODO(design): 禁用图标遮挡名字且剩余文字有歧义（如星熊）时，需要头像取景验证。
        return layout with { Cards = cards.ToArray(), CardsMs = timer.Elapsed.TotalMilliseconds };
    }
}
