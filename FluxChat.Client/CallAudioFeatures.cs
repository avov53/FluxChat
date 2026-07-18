using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FluxChat.Client;

internal sealed class SoundboardClipViewModel : INotifyPropertyChanged
{
    private bool _isPlaying;

    public required string Id { get; init; }
    public required string DisplayName { get; set; }
    public required string SourcePath { get; init; }
    public required string PcmPath { get; init; }
    public double DurationSeconds { get; init; }
    public DateTimeOffset AddedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string DurationText => TimeSpan.FromSeconds(Math.Max(0, DurationSeconds)).ToString(DurationSeconds >= 60 ? @"m\:ss" : @"s\:ff");

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class SoundboardLibrary
{
    public double Volume { get; set; } = 0.8;
    public List<SoundboardClipData> Clips { get; set; } = [];
}

internal sealed record SoundboardClipData(
    string Id,
    string DisplayName,
    string SourcePath,
    string PcmPath,
    double DurationSeconds,
    DateTimeOffset AddedAtUtc);

internal static class SoundboardLibraryStore
{
    public static async Task<SoundboardLibrary> LoadAsync()
    {
        try
        {
            if (!File.Exists(AppPaths.SoundboardLibraryPath))
            {
                return new SoundboardLibrary();
            }

            var json = await File.ReadAllTextAsync(AppPaths.SoundboardLibraryPath);
            return JsonSerializer.Deserialize<SoundboardLibrary>(json) ?? new SoundboardLibrary();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            AppLog.Write(ex, "Soundboard library could not be loaded");
            return new SoundboardLibrary();
        }
    }

    public static async Task SaveAsync(double volume, IEnumerable<SoundboardClipViewModel> clips)
    {
        AppPaths.EnsureSoundboardDirectoriesCreated();
        var library = new SoundboardLibrary
        {
            Volume = Math.Clamp(volume, 0, 1),
            Clips = clips.Select(x => new SoundboardClipData(
                x.Id,
                x.DisplayName,
                x.SourcePath,
                x.PcmPath,
                x.DurationSeconds,
                x.AddedAtUtc)).ToList()
        };
        await File.WriteAllTextAsync(
            AppPaths.SoundboardLibraryPath,
            JsonSerializer.Serialize(library, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class ParticipantAudioPreference
{
    public double Volume { get; set; } = 1;
    public bool IsMuted { get; set; }
    public bool IsSoundboardMuted { get; set; }
    public double StreamVolume { get; set; } = 1;
    public bool IsStreamMuted { get; set; }
}

internal sealed class CallAudioPreferences
{
    public ConcurrentDictionary<string, ParticipantAudioPreference> Participants { get; set; } = new(StringComparer.Ordinal);

    public ParticipantAudioPreference Get(string userId)
    {
        var preference = Participants.GetOrAdd(userId, _ => new ParticipantAudioPreference());

        preference.Volume = Math.Clamp(preference.Volume, 0, 5);
        preference.StreamVolume = Math.Clamp(preference.StreamVolume, 0, 1);
        return preference;
    }
}

internal static class CallAudioPreferencesStore
{
    private static readonly SemaphoreSlim SaveGate = new(1, 1);

    public static async Task<CallAudioPreferences> LoadAsync()
    {
        try
        {
            if (!File.Exists(AppPaths.CallAudioPreferencesPath))
            {
                return new CallAudioPreferences();
            }

            var json = await File.ReadAllTextAsync(AppPaths.CallAudioPreferencesPath);
            return JsonSerializer.Deserialize<CallAudioPreferences>(json) ?? new CallAudioPreferences();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            AppLog.Write(ex, "Call audio preferences could not be loaded");
            return new CallAudioPreferences();
        }
    }

    public static async Task SaveAsync(CallAudioPreferences preferences)
    {
        await SaveGate.WaitAsync();
        try
        {
            AppPaths.EnsureCreated();
            await File.WriteAllTextAsync(
                AppPaths.CallAudioPreferencesPath,
                JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            SaveGate.Release();
        }
    }
}
