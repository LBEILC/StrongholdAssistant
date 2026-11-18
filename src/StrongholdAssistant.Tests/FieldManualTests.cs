using Moq;
﻿using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class FieldManualTests
{
    [Fact]
    public void DisabledObservationKeepsSnapshotWithoutMatchingOrRecordingPurchase()
    {
        var tracker = new FieldTracker();
        int matches = 0;
        tracker.ConfigureIdentity([new() { Name = "甲", Tier = 1 }, new() { Name = "乙", Tier = 1 }],
            (_, _, names) => { matches++; return new(names.Select((n, i) => new SpriteCandidate(n, .9 - i * .2)).ToArray(), .2); });
        var cell = new FieldCell("bench", 1, 0);
        var at = DateTimeOffset.UtcNow;
        var empty = new FieldObservation(at, true, "m01", 1, new Dictionary<FieldCell, int> { [cell] = 0 }, new Dictionary<int,string> { [1] = "甲", [2] = "乙" }, 10);
        tracker.Observe(empty);
        var snapshot = tracker.State;
        tracker.Observe(empty with { At = at.AddSeconds(1) }, recognize: false);
        using var image = new OpenCvSharp.Mat(204, 180, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(40, 80, 120));
        var patch = FieldReader.CropSprite(image, 90, 180, true)!;
        var purchase = empty with { At = at.AddSeconds(2), Money = 9, Cells = new Dictionary<FieldCell,int> { [cell] = 1 },
            Sprites = new Dictionary<FieldCell,FieldSpritePatch> { [cell] = patch } };
        tracker.Observe(purchase, recognize: false);
        Assert.Same(snapshot, tracker.State);
        Assert.Equal(0, matches);
        tracker.Observe(purchase with { At = at.AddSeconds(3) }, force: true);
        Assert.Empty(tracker.State!.Roster);
        Assert.DoesNotContain(tracker.State.Log, e => e.Kind == "buy");
        var previous = tracker.State.UpdatedAt;
        tracker.Observe(purchase with { At = at.AddSeconds(4) }, force: true);
        Assert.NotEqual(previous, tracker.State.UpdatedAt);
        tracker.Observe(purchase with { At = at.AddSeconds(5), Money = 8 }, recognize: false);
        tracker.Observe(purchase with { At = at.AddSeconds(6), Money = 8 }, force: true);
        Assert.Equal("甲", Assert.Single(tracker.State.Units).Operator);
        Assert.NotEqual("unknown", Assert.Single(tracker.State.Units).Status);
    }

    [Fact]
    public async Task ManualRecognitionPublishesFieldWithoutWaitingForPlacement()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var pending = new TaskCompletionSource();
        try
        {
            string data = Path.Combine(AppContext.BaseDirectory, "data");
            var api = await HostApi.CreateAsync(data, directory);
            var ocr = new Moq.Mock<IOcrEngine>();
            ocr.Setup(o => o.RecognizeRegionAsync(Moq.It.IsAny<byte[]>(), Moq.It.IsAny<int>(), Moq.It.IsAny<int>(), Moq.It.IsAny<int>(), Moq.It.IsAny<int>()))
                .ReturnsAsync(Array.Empty<OcrTextResult>());
            api.CreateShopReader(ocr.Object);
            api.InitializeField(data, ocr.Object);
            typeof(HostApi).GetField("_placementWork", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(api, pending.Task);
            bool published = false;
            api.Changed += (kind, _) => { if (kind == "field") published = true; };
            using var bitmap = new System.Drawing.Bitmap(1920, 1080);
            using var bytes = new MemoryStream();
            bitmap.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
            await api.RecognizeFieldFrameAsync(bytes.ToArray(), DateTimeOffset.UtcNow, new(1, null, null, null, true), true, false, [])
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(published);
            Assert.False(pending.Task.IsCompleted);
        }
        finally { pending.SetResult(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void LegacySettingsDefaultAndRoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{}");
            var store = new HostSettingsStore(path);
            var old = store.LoadSettings();
            Assert.False(old.FieldAuto); Assert.Equal("Alt+F", old.FieldHotkey);
            // 自动识别暂时固定关闭：存进去的 true 也不认
            store.SaveSettings(old with { FieldAuto = true, FieldHotkey = "" });
            Assert.False(store.LoadSettings().FieldAuto); Assert.Equal("", store.LoadSettings().FieldHotkey);
        }
        finally { Directory.Delete(directory, true); }
    }
}
