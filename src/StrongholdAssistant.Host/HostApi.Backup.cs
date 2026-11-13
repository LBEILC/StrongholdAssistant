using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ASPAssistant.Core.Models;
using Microsoft.Win32;

namespace StrongholdAssistant.Host;

public sealed record BackupSummary(string AppVersion, DateTime ExportedAt, int Lineups, int FocusCovenants, int FocusOperators);
public sealed record BackupSelection(string Token, BackupSummary Summary);

public sealed partial class HostApi
{
    private const string BackupFormat = "stronghold-assistant-backup";
    private const string InvalidBackup = "这不是有效的备份文件，或者来自不兼容的版本。";
    private readonly Dictionary<string, BackupDocument> _backups = [];
    private sealed record BackupDocument(string Format, int Version, string AppVersion, DateTime ExportedAt,
        SettingsDto Settings, LineupDto[] Lineups)
    {
        public MatchRecord[] History { get; init; } = [];
        public string[] DeletedSklandIds { get; init; } = [];
    }

    public async Task ExportBackupAsync(string path)
    {
        var version = typeof(HostApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        var backup = new BackupDocument(BackupFormat, 1, version, DateTime.UtcNow, Settings with
        { MirrorChyanCdk = "", UpdateProxy = "", CloudSync = new(false, null, null, null, []) }, await LineupsAsync())
        { History = History(), DeletedSklandIds = DeletedSklandIds() };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(backup, JsonOptions));
    }

    public async Task<BackupSelection> ReadBackupAsync(string path)
    {
        BackupDocument backup;
        try
        {
            backup = JsonSerializer.Deserialize<BackupDocument>(await File.ReadAllTextAsync(path), JsonOptions)!;
            if (backup is null || backup.Format != BackupFormat || backup.Version != 1
                || backup.AppVersion is null || backup.ExportedAt == default
                || backup.Settings is null || backup.Lineups is null)
                throw new ArgumentException(InvalidBackup);
            ValidateSettings(backup.Settings);
            if (backup.DeletedSklandIds is null || backup.DeletedSklandIds.Any(string.IsNullOrEmpty)
                || backup.History is null || backup.History.Any(r => r is null || string.IsNullOrEmpty(r.Id)
                || !DateTimeOffset.TryParse(r.Time, out _) || (r.Local is null && r.Skland is null)
                || r.Local is { } local && (local.Rounds is null || local.Covenants is null || local.BannedOperators is null
                    || !DateTimeOffset.TryParse(local.LastSeenAt, out _))))
                throw new ArgumentException(InvalidBackup);
            foreach (var lineup in backup.Lineups)
            {
                if (lineup is null) throw new ArgumentException(InvalidBackup);
                ValidateLineup(lineup);
                ValidateId(lineup.Id);
            }
            if (backup.Lineups.Select(l => l.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != backup.Lineups.Length)
                throw new ArgumentException(InvalidBackup);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new ArgumentException(InvalidBackup);
        }
        var token = Guid.NewGuid().ToString("N");
        _backups.Add(token, backup);
        return new(token, new(backup.AppVersion, backup.ExportedAt, backup.Lineups.Length,
            backup.Settings.FocusCovenants.Length, backup.Settings.FocusOperators.Length));
    }

    public async Task ImportDataAsync(string token, string mode)
    {
        if (!_backups.TryGetValue(token, out var backup)) throw new ArgumentException("请重新选择备份文件。");
        if (mode is not ("merge" or "replace")) throw new ArgumentException("导入方式无效。"); // TODO(design): 确认非法导入方式的错误文案。
        var directory = Path.Combine(DataDirectory, "backups");
        Directory.CreateDirectory(directory);
        await ExportBackupAsync(Path.Combine(directory, $"导入前-{DateTime.Now:yyyyMMdd-HHmmss}.json"));
        if (mode == "replace")
            foreach (var lineup in await _lineups.LoadAllAsync()) await _lineups.DeleteAsync(lineup.Id);
        foreach (var input in backup.Lineups)
            await _lineups.SaveAsync(ApplyLineup(input, new Lineup { Id = input.Id }));
        MergeHistory(backup.History, backup.DeletedSklandIds);
        var session = await SessionAsync();
        if (session.TargetLineupId is not null && !(await LineupsAsync()).Any(l => l.Id == session.TargetLineupId))
            await SaveSessionAsync(session with { TargetLineupId = null });
        else
            await RefreshTrackingAsync();
        SaveSettings(mode == "replace" ? backup.Settings with { MirrorChyanCdk = Settings.MirrorChyanCdk, UpdateProxy = Settings.UpdateProxy, CloudSync = Settings.CloudSync } : Settings with
        {
            FocusCovenants = Settings.FocusCovenants.Union(backup.Settings.FocusCovenants).ToArray(),
            FocusOperators = Settings.FocusOperators.Union(backup.Settings.FocusOperators).ToArray()
        });
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(DataDirectory);
        Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true });
    }

    private async Task<string?> ExportDataAsync()
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"卫戍协议助手-备份-{DateTime.Now:yyyy-MM-dd}.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Filter = "JSON (*.json)|*.json", DefaultExt = ".json"
        };
        if (dialog.ShowDialog(DialogOwner) != true) return null;
        await ExportBackupAsync(dialog.FileName);
        return Path.GetFullPath(dialog.FileName);
    }

    private async Task<BackupSelection?> PickBackupAsync()
    {
        var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json", DefaultExt = ".json" };
        return dialog.ShowDialog(DialogOwner) == true ? await ReadBackupAsync(dialog.FileName) : null;
    }
}
