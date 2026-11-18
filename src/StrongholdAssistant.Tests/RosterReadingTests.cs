using System.Text.Json;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using Xunit.Abstractions;
namespace StrongholdAssistant.Tests;
[Collection("Native OCR")]
public sealed class RosterReadingTests(EnemyRecognitionFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task WideFrameKeepsCardsUndistortedAndSortAnchoredRight()
    {
        var reader = new RosterReader(fixture.Engine, Path.Combine(AppContext.BaseDirectory, "data"));
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "roster-left-end-1920x1080.jpg");
        var original = await File.ReadAllBytesAsync(source);
        using var image = new System.Drawing.Bitmap(source);
        using var canvas = new System.Drawing.Bitmap(2045, 1080);
        using (var g = System.Drawing.Graphics.FromImage(canvas))
        {
            g.Clear(System.Drawing.Color.Black);
            // 只验证已有完整列不变形；右侧残卡缺少完整像素，不能伪造新列。
            g.DrawImage(image, new System.Drawing.Rectangle(0, 0, 1750, 1080), 0, 0, 1750, 1080, System.Drawing.GraphicsUnit.Pixel);
            g.FillRectangle(System.Drawing.Brushes.Black, 1250, 0, 795, 130);
            g.DrawImage(image, new System.Drawing.Rectangle(1375, 0, 670, 130), 1250, 0, 670, 130, System.Drawing.GraphicsUnit.Pixel);
        }
        using var wide = new System.Drawing.Bitmap(canvas, 2558, 1351);
        using var png = new MemoryStream();
        wide.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        var bytes = png.ToArray();
        var before = await reader.ReadAsync(original);
        var after = await reader.ReadAsync(bytes);
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before.Cards.Select(c => c.Operator.Name), after.Cards.Select(c => c.Operator.Name));
        Assert.Equal(before.Cards.Select(c => c.Operator.Skill), after.Cards.Select(c => c.Operator.Skill));
        var (width, spacing) = RosterReader.MeasureScroll(bytes);
        Assert.Equal(2045, width);
        Assert.InRange(spacing, 308, 312);
        var oldSort = await reader.ReadSortAsync(original);
        var sort = await reader.ReadSortAsync(bytes);
        Assert.NotNull(oldSort);
        Assert.NotNull(sort);
        Assert.Equal(oldSort.Mode, sort.Mode);
        Assert.InRange(sort.X - oldSort.X, 123, 127);
        Assert.InRange(sort.Y - oldSort.Y, -2, 2);
        output.WriteLine($"2558×1351：完整卡片 {after.Cards.Length}，归一宽度 {width}，列距 {spacing}，排序 ({sort.X},{sort.Y})。");
    }

    [Fact]
    public void VectorAveragesFineStripesWithoutDarkEdges()
    {
        using var image = new System.Drawing.Bitmap(120, 120);
        // 每个目标像素覆盖五列，细条纹缩小后应接近均匀灰色，而不是采到黑白交替。
        for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++)
            image.SetPixel(x, y, x % 2 == 0 ? System.Drawing.Color.White : System.Drawing.Color.Black);
        var vector = RosterReader.Vector(image, new(0, 0, 120, 120));
        Assert.All(vector.Where((_, i) => i % 24 is > 0 and < 23), value => Assert.InRange(value, 120, 135));
        using var graphics = System.Drawing.Graphics.FromImage(image);
        graphics.Clear(System.Drawing.Color.White);
        Assert.All(RosterReader.Vector(image, new(0, 0, 120, 120)), value => Assert.InRange(value, 254, 255.01));
    }

    [Fact]
    public async Task OfflineSortAndTierCards()
    {
        var samples = Environment.GetEnvironmentVariable("ROSTER_SAMPLES");
        if (samples is null) return;
        var reader = new RosterReader(fixture.Engine, Path.Combine(AppContext.BaseDirectory, "data"));
        for (int i = 0; i < 37; i++)
        {
            var sort = await reader.ReadSortAsync(await File.ReadAllBytesAsync(Path.Combine(samples, $"scan{i:00}.png")));
            Assert.NotNull(sort);
            Assert.Equal("covenant", sort.Mode);
            Assert.InRange(sort.X, 1707, 1727);
            Assert.InRange(sort.Y, 50, 65);
        }
        var frame = await File.ReadAllBytesAsync(Path.Combine(samples, "user-tier-sort.png"));
        var tierSort = await reader.ReadSortAsync(frame);
        Assert.NotNull(tierSort);
        Assert.Equal("tier", tierSort.Mode);
        Assert.InRange(tierSort.X, 1615, 1635);
        Assert.InRange(tierSort.Y, 50, 65);
        var reading = await reader.ReadAsync(frame);
        Assert.NotNull(reading);
        Assert.Equal("折桠,艾丝黛尔,幽灵鲨,野鬃,慕斯,角峰,灰喉,深靛,卡达,摆渡人,夏栎,杰克".Split(','),
            reading.Cards.Select(c => c.Operator.Name));
        using var extra = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "codex_extra.json")));
        int[] selected = [2, 1, 1, 2, 1, 2, 1, 2, 2, 2, 2, 1];
        for (int i = 0; i < selected.Length; i++)
        {
            var card = reading.Cards[i];
            Assert.Equal(extra.RootElement.GetProperty("operators").GetProperty(card.Operator.Name).GetProperty("skills")[selected[i] - 1].GetProperty("id").GetString(), card.Operator.Skill);
        }
        output.WriteLine($"排序识别：按盟约 37/37，按等阶 1/1；等阶样图完整卡片 {reading.Cards.Length}/12，选中技能 12/12。");
        foreach (var card in reading.Cards) output.WriteLine(JsonSerializer.Serialize(card.Operator));
    }

    [Fact]
    public async Task MumuSortLocationAndOpenFilterAreRecognized()
    {
        var samples = Environment.GetEnvironmentVariable("ROSTER_SAMPLES");
        if (samples is null) return;
        var reader = new RosterReader(fixture.Engine, Path.Combine(AppContext.BaseDirectory, "data"));
        var frame = await File.ReadAllBytesAsync(Path.Combine(samples, "mumu-filter-open.png"));
        var sort = await reader.ReadSortAsync(frame);
        Assert.NotNull(sort);
        Assert.Equal("covenant", sort.Mode);
        Assert.InRange(sort.X, 1590, 1610);
        Assert.InRange(sort.Y, 55, 70);
        output.WriteLine($"MuMu 等阶图标：({sort.X},{sort.Y})");
        var error = await Assert.ThrowsAsync<IOException>(() => reader.ReadAsync(frame));
        Assert.Equal("先关掉右侧的盟约筛选", error.Message);
        using var blank = new System.Drawing.Bitmap(1920, 1080);
        using var stream = new MemoryStream();
        blank.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        Assert.Null(await reader.ReadSortAsync(stream.ToArray()));
    }

    [Fact]
    public void FullPotentialAtPlayerUiScale()
    {
        using var icon = new System.Drawing.Bitmap(Path.Combine(AppContext.BaseDirectory, "TestData", "potential-6-ui100.png"));
        Assert.Equal(6, RosterReader.ReadPotential(icon, 0, 0));
    }

    [Fact]
    public void PotentialTwoAndObscuredIcon()
    {
        using var icon = new System.Drawing.Bitmap(30, 30);
        using var graphics = System.Drawing.Graphics.FromImage(icon);
        graphics.Clear(System.Drawing.Color.FromArgb(35, 35, 35));
        Assert.Equal(1, RosterReader.ReadPotential(icon, 0, 0));
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(30, 170, 230), 3);
        graphics.DrawLine(pen, 3, 21, 28, 21);
        Assert.Equal(2, RosterReader.ReadPotential(icon, 0, 0));
        graphics.FillRectangle(System.Drawing.Brushes.Black, 19, 18, 6, 6);
        Assert.Null(RosterReader.ReadPotential(icon, 0, 0));
        graphics.Clear(System.Drawing.Color.White);
        Assert.Null(RosterReader.ReadPotential(icon, 0, 0));
        Assert.Null(RosterReader.ReadPotential(icon, -1, 0));
    }

    [Fact]
    public async Task OfflineCards()
    {
        var reader = new RosterReader(fixture.Engine, Path.Combine(AppContext.BaseDirectory, "data"));
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "roster-*.jpg").Where(f => !Path.GetFileName(f).StartsWith("roster-mumu-")).ToArray();
        var extra = Environment.GetEnvironmentVariable("ROSTER_SAMPLES");
        if (extra is not null) files = files.Concat(Enumerable.Range(0, 37).Select(i => Path.Combine(extra, $"scan{i:00}.png")))
            .Append(Path.Combine(extra, "user-tier-sort.png")).ToArray();
        var results = new List<object>(); var names = new HashSet<string>(); var custom = new HashSet<string>();
        foreach (var file in files)
        {
            var result = await reader.ReadAsync(await File.ReadAllBytesAsync(file));
            Assert.NotNull(result);
            Assert.Equal(result.Cards.Length, result.Cards.Select(c => c.Operator.Name).Distinct().Count());
            Assert.All(result.Cards.Where(c => Path.GetFileName(file) != "user-tier-sort.png" || c.Operator.Name != "杰克"),
                c => Assert.InRange(c.Operator.Potential ?? 0, 1, 6));
            foreach (var card in result.Cards)
            {
                if (new[] { "左乐", "年", "结城理", "珊比", "信仰搅拌机" }.Contains(card.Operator.Name))
                    Assert.Equal("", card.Operator.Module);
                // 设计已核对的低分皮肤，同时覆盖普通卡和带自选角标的卡。
                if (card.Operator.Name == "老鲤") Assert.Equal("char_322_lmlee#2", card.Operator.Skin);
                if (card.Operator.Name == "年") Assert.Equal("char_2014_nian@nian#4", card.Operator.Skin);
                if (card.Operator.Name == "摆渡人") Assert.Equal("char_4166_varkis#2", card.Operator.Skin);
                if (card.Operator.Name is "斩业星熊" or "摆渡人")
                {
                    Assert.True(card.SkinScore >= .85, $"{card.Operator.Name} 抗锯齿缩图后的皮肤分数：{card.SkinScore:F3}");
                    Assert.True(card.SkinMargin >= .10);
                }
            }
            var potentials = new Dictionary<string, int>
            {
                ["余"] = 3, ["老鲤"] = 3, ["霍尔海雅"] = 3, ["异客"] = 3, ["逻各斯"] = 3, ["夕"] = 3,
                ["棘刺"] = 4, ["维伊"] = 5, ["拜松"] = 6, ["溯光星源"] = 6, ["仇白"] = 1
            };
            foreach (var card in result.Cards)
                if (potentials.TryGetValue(card.Operator.Name, out int value)) Assert.Equal(value, card.Operator.Potential);
            if (Path.GetExtension(file) == ".jpg")
            {
                var expected = Path.GetFileName(file) switch
                {
                    "roster-left-end-1920x1080.jpg" => ("拜松,矩,风絮,乌有,余,仇白,黍,老鲤,赤刃明霄陈,斩业星熊", "2,2,2,2,3,3,3,3,3,2"),
                    "roster-mid-1-1920x1080.jpg" => ("角峰,雪猎,哈洛德,极光,耶拉,初雪,灵知,圣聆初雪", "2,2,2,2,2,2,2,3"),
                    "roster-mid-2-1920x1080.jpg" => ("W,焰尾,冻河,引星棘刺,烛煌,妮芙,霜叶,云迹,承曦格雷伊,水灯心", "2,3,3,2,3,2,1,2,2,1"),
                    _ => ("维伊,霍尔海雅,异客,棘刺,圣聆初雪,薇薇安娜,溯光星源,逻各斯,塔露拉,夕", "3,1,1,3,3,3,3,3,3,2")
                };
                Assert.Equal(expected.Item1.Split(','), result.Cards.Select(c => c.Operator.Name));
                using var extraData = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "codex_extra.json")));
                var selected = expected.Item2.Split(',').Select(int.Parse).ToArray();
                for (int i = 0; i < result.Cards.Length; i++)
                {
                    var card = result.Cards[i];
                    Assert.Equal(extraData.RootElement.GetProperty("operators").GetProperty(card.Operator.Name).GetProperty("skills")[selected[i] - 1].GetProperty("id").GetString(), card.Operator.Skill);
                }
                if (file.Contains("mid-2")) Assert.Equal("char_193_frostl@boc#4", result.Cards.Single(c => c.Operator.Name == "霜叶").Operator.Skin);
            }
            var orderErrors = CheckSkillOrder(file, result);
            Assert.Empty(orderErrors);
            results.Add(new { file = Path.GetFileName(file), result, orderErrors });
            output.WriteLine("技能图标顺序异常：" + string.Join(",", orderErrors));
            output.WriteLine("未匹配：" + string.Join(",", result.UnknownNames));
            foreach (var card in result.Cards) { names.Add(card.Operator.Name); if (card.Custom) custom.Add(card.Operator.Name); }
            output.WriteLine($"{Path.GetFileName(file)}: {result.Cards.Length} / {result.Milliseconds:F0}ms: {string.Join(",",result.Cards.Select(c => c.Operator.Name))}");
        }
        var report = Environment.GetEnvironmentVariable("ROSTER_REPORT");
        if (report is not null) File.WriteAllText(report, JsonSerializer.Serialize(new { results, names, custom }, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"去重 {names.Count}，自选 {custom.Count}: {string.Join(",",custom)}");
        Assert.NotEmpty(names);

    }

    [Fact]
    public async Task MumuCards()
    {
        var reader = new RosterReader(fixture.Engine, Path.Combine(AppContext.BaseDirectory, "data"));
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "roster-mumu-*.jpg");
        var samples = Environment.GetEnvironmentVariable("ROSTER_SAMPLES");
        if (samples is not null) files = files.Concat(Enumerable.Range(0, 22).Select(i => Path.Combine(samples, "mumu", $"m{i:00}.png"))).ToArray();
        var results = new List<object>();
        foreach (var file in files)
        {
            var result = await reader.ReadAsync(await File.ReadAllBytesAsync(file));
            Assert.NotNull(result);
            Assert.NotEmpty(result.Cards);
            if (Path.GetExtension(file) == ".jpg") Assert.Empty(result.UnknownNames);
            Assert.Equal(result.Cards.Length, result.Cards.Select(c => c.Operator.Name).Distinct().Count());
            Assert.All(result.Cards, card =>
            {
                Assert.NotNull(card.Operator.Skin);
                Assert.NotNull(card.Operator.Skill);
                Assert.NotNull(card.Operator.Module);
                Assert.InRange(card.Operator.Potential ?? 0, 1, 6);
            });
            if (Path.GetExtension(file) == ".jpg")
            {
                bool rightEnd = Path.GetFileName(file) == "roster-mumu-m00.jpg";
                var names = (rightEnd ? "缪尔赛思,帕拉斯,风笛,银灰,左乐,年,珊比,信仰搅拌机"
                    : "折桠,艾丝黛尔,幽灵鲨,野鬃,慕斯,角峰,灰喉,深靛,卡达,摆渡人").Split(',');
                var skills = rightEnd ? new[] { 3, 3, 2, 3, 3, 3, 1, 1 } : [2, 1, 1, 2, 1, 2, 1, 2, 2, 2];
                Assert.Equal(names, result.Cards.Select(c => c.Operator.Name));
                Assert.Equal(rightEnd ? 6 : 0, result.Cards.Count(c => c.Custom));
                using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "codex_extra.json")));
                for (int i = 0; i < names.Length; i++)
                {
                    var op = data.RootElement.GetProperty("operators").GetProperty(names[i]);
                    Assert.Equal(op.GetProperty("skills")[skills[i] - 1].GetProperty("id").GetString(), result.Cards[i].Operator.Skill);
                    Assert.Equal(rightEnd ? new[] { 1, 3, 6, 4, 1, 3, 1, 6 }[i] : 6, result.Cards[i].Operator.Potential);
                }
                if (rightEnd)
                {
                    Assert.Equal("char_2014_nian@nian#4", result.Cards.Single(c => c.Operator.Name == "年").Operator.Skin);
                    foreach (var name in new[] { "左乐", "年", "珊比", "信仰搅拌机" })
                        Assert.Equal("", result.Cards.Single(c => c.Operator.Name == name).Operator.Module);
                }
                else Assert.Equal("char_4166_varkis#2", result.Cards.Single(c => c.Operator.Name == "摆渡人").Operator.Skin);
            }
            results.Add(new { file = Path.GetFileName(file), result });
            output.WriteLine($"{Path.GetFileName(file)}: {result.Cards.Length}: {string.Join(",",result.Cards.Select(c => c.Operator.Name))}; 未匹配：{string.Join(",",result.UnknownNames)}");
        }
        var report = Environment.GetEnvironmentVariable("ROSTER_REPORT");
        if (report is not null) File.WriteAllText(report + ".mumu.json", JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string[] CheckSkillOrder(string file, RosterReading reading)
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        using var extra = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "codex_extra.json")));
        var templates = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double[]>>>(File.ReadAllText(Path.Combine(data, "roster_templates.json")))!["skills"];
        static double[] Norm(double[] v) { double avg = v.Average(); var n = v.Select(x => x - avg).ToArray(); double len = Math.Sqrt(n.Sum(x => x * x)); return n.Select(x => x / len).ToArray(); }
        using var image = new System.Drawing.Bitmap(file);
        var errors = new List<string>();
        foreach (var card in reading.Cards)
        {
            var ids = extra.RootElement.GetProperty("operators").GetProperty(card.Operator.Name).GetProperty("skills").EnumerateArray().Select(o => o.GetProperty("id").GetString()!).ToArray();
            for (int i = 0; i < ids.Length; i++)
            {
                var sample = Norm(RosterReader.Vector(image, new(card.X + 130, card.Y + 24 + i * 97, 72, 72)));
                var scores = ids.Select(id => Norm(templates[id]).Zip(sample, (a, b) => a * b).Sum()).ToArray();
                if (scores[i] + .03 < scores.Max()) errors.Add($"{card.Operator.Name} 技能{i + 1}: {scores[i]:F2}/{scores.Max():F2}");
            }
        }
        return errors.ToArray();
    }
}
