using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;
using ShapeRectangle = System.Windows.Shapes.Rectangle;
using IOPath = System.IO.Path;

namespace FluxChat.Client;

public sealed record RingtoneTrimSelection(TimeSpan Start, TimeSpan Duration);

public partial class RingtoneTrimWindow : Window
{
    private const int PreviewSampleRate = 8_000;
    private static readonly TimeSpan MaxSelection = TimeSpan.FromSeconds(20);

    private readonly string _sourcePath;
    private readonly string _ffmpegPath;
    private readonly double[] _peaks;
    private readonly TimeSpan _duration;
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _playbackTimer;

    private bool _updatingSliders;
    private bool _playing;

    public RingtoneTrimSelection? Selection { get; private set; }

    private RingtoneTrimWindow(string sourcePath, string ffmpegPath, double[] peaks, TimeSpan duration)
    {
        InitializeComponent();
        _sourcePath = sourcePath;
        _ffmpegPath = ffmpegPath;
        _peaks = peaks;
        _duration = duration;
        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _playbackTimer.Tick += PlaybackTimer_OnTick;
    }

    public static async Task<RingtoneTrimSelection?> ShowEditorAsync(Window owner, string sourcePath, string ffmpegPath)
    {
        var analysis = await AnalyzeAsync(ffmpegPath, sourcePath);
        if (analysis.Duration <= MaxSelection)
        {
            return new RingtoneTrimSelection(TimeSpan.Zero, analysis.Duration);
        }

        var window = new RingtoneTrimWindow(sourcePath, ffmpegPath, analysis.Peaks, analysis.Duration)
        {
            Owner = owner
        };

        return window.ShowDialog() == true ? window.Selection : null;
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        StartSlider.Minimum = 0;
        EndSlider.Minimum = 0;
        StartSlider.Maximum = Math.Max(0, _duration.TotalSeconds - 0.1);
        EndSlider.Maximum = _duration.TotalSeconds;
        StartSlider.Value = 0;
        EndSlider.Value = Math.Min(MaxSelection.TotalSeconds, _duration.TotalSeconds);
        StatusText.Text = IOPath.GetFileName(_sourcePath);
        DrawWaveform();
        UpdateSelectionUi();

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fade);
        if (DialogCard.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 }
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 }
            });
        }
    }

    private static async Task<(double[] Peaks, TimeSpan Duration)> AnalyzeAsync(string ffmpegPath, string sourcePath)
    {
        var tempPath = IOPath.Combine(IOPath.GetTempPath(), $"fluxchat-wave-{Guid.NewGuid():N}.pcm");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(sourcePath);
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0:a:0");
            startInfo.ArgumentList.Add("-vn");
            startInfo.ArgumentList.Add("-ac");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-ar");
            startInfo.ArgumentList.Add(PreviewSampleRate.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("s16le");
            startInfo.ArgumentList.Add(tempPath);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("ffmpeg.exe did not start.");
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stderr = await stderrTask;
            if (process.ExitCode != 0 || !File.Exists(tempPath))
            {
                throw new InvalidOperationException($"ffmpeg.exe could not read the ringtone. {stderr}");
            }

            var bytes = await File.ReadAllBytesAsync(tempPath);
            var sampleCount = bytes.Length / 2;
            if (sampleCount == 0)
            {
                throw new InvalidOperationException("The selected file does not contain readable audio.");
            }

            var duration = TimeSpan.FromSeconds(sampleCount / (double)PreviewSampleRate);
            var targetPeaks = Math.Clamp((int)Math.Ceiling(duration.TotalSeconds * 48), 120, 4_000);
            var samplesPerPeak = Math.Max(1, sampleCount / targetPeaks);
            var peaks = new double[(sampleCount + samplesPerPeak - 1) / samplesPerPeak];
            var peakIndex = 0;
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex += samplesPerPeak)
            {
                var end = Math.Min(sampleCount, sampleIndex + samplesPerPeak);
                var max = 0;
                for (var i = sampleIndex; i < end; i++)
                {
                    var sample = BitConverter.ToInt16(bytes, i * 2);
                    max = Math.Max(max, Math.Abs((int)sample));
                }

                peaks[peakIndex++] = Math.Min(1, max / 32768d);
            }

            return (peaks, duration);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private void DrawWaveform()
    {
        WaveformCanvas.Children.Clear();
        var width = Math.Max(1, WaveformCanvas.ActualWidth);
        var height = Math.Max(1, WaveformCanvas.ActualHeight);
        var barWidth = Math.Max(2, width / Math.Max(1, _peaks.Length));
        var center = height / 2;

        for (var i = 0; i < _peaks.Length; i++)
        {
            var peak = Math.Max(0.04, _peaks[i]);
            var barHeight = Math.Max(3, peak * (height - 18));
            var rect = new ShapeRectangle
            {
                Width = Math.Max(1, barWidth - 1),
                Height = barHeight,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(MediaColor.FromRgb(94, 234, 212)),
                Opacity = 0.76
            };
            Canvas.SetLeft(rect, i * barWidth);
            Canvas.SetTop(rect, center - barHeight / 2);
            WaveformCanvas.Children.Add(rect);
        }
    }

    private void StartSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSliders)
        {
            return;
        }

        _updatingSliders = true;
        if (StartSlider.Value > EndSlider.Value - 0.1)
        {
            EndSlider.Value = Math.Min(_duration.TotalSeconds, StartSlider.Value + 0.1);
        }

        if (EndSlider.Value - StartSlider.Value > MaxSelection.TotalSeconds)
        {
            EndSlider.Value = Math.Min(_duration.TotalSeconds, StartSlider.Value + MaxSelection.TotalSeconds);
            if (EndSlider.Value - StartSlider.Value > MaxSelection.TotalSeconds)
            {
                StartSlider.Value = Math.Max(0, EndSlider.Value - MaxSelection.TotalSeconds);
            }
        }

        _updatingSliders = false;
        StopPreview();
        UpdateSelectionUi();
    }

    private void EndSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSliders)
        {
            return;
        }

        _updatingSliders = true;
        if (EndSlider.Value < StartSlider.Value + 0.1)
        {
            StartSlider.Value = Math.Max(0, EndSlider.Value - 0.1);
        }

        if (EndSlider.Value - StartSlider.Value > MaxSelection.TotalSeconds)
        {
            StartSlider.Value = Math.Max(0, EndSlider.Value - MaxSelection.TotalSeconds);
        }

        _updatingSliders = false;
        StopPreview();
        UpdateSelectionUi();
    }

    private void UpdateSelectionUi()
    {
        var start = TimeSpan.FromSeconds(StartSlider.Value);
        var end = TimeSpan.FromSeconds(EndSlider.Value);
        var length = end - start;
        StartText.Text = FormatTime(start);
        EndText.Text = FormatTime(end);
        SelectionText.Text = $"Selected {FormatTime(length)} of {FormatTime(_duration)}";

        var width = Math.Max(1, WaveformCanvas.ActualWidth);
        var durationSeconds = Math.Max(0.1, _duration.TotalSeconds);
        var left = width * start.TotalSeconds / durationSeconds;
        var right = width * end.TotalSeconds / durationSeconds;
        SelectionOverlay.Width = Math.Max(2, right - left);
        Canvas.SetLeft(SelectionOverlay, left);
        Canvas.SetLeft(PlayheadLine, left);
    }

    private void WaveformCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawWaveform();
        UpdateSelectionUi();
    }

    private void WaveformCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var x = Math.Clamp(e.GetPosition(WaveformCanvas).X, 0, Math.Max(1, WaveformCanvas.ActualWidth));
        var startSeconds = x / Math.Max(1, WaveformCanvas.ActualWidth) * _duration.TotalSeconds;
        startSeconds = Math.Clamp(startSeconds, 0, Math.Max(0, _duration.TotalSeconds - 0.1));
        var length = Math.Min(MaxSelection.TotalSeconds, EndSlider.Value - StartSlider.Value);
        _updatingSliders = true;
        StartSlider.Value = startSeconds;
        EndSlider.Value = Math.Min(_duration.TotalSeconds, startSeconds + length);
        _updatingSliders = false;
        StopPreview();
        UpdateSelectionUi();
    }

    private void PlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_playing)
        {
            StopPreview();
            return;
        }

        _player.Open(new Uri(_sourcePath));
        _player.Volume = 1;
        _player.Position = TimeSpan.FromSeconds(StartSlider.Value);
        _player.Play();
        _playing = true;
        PlayButton.Content = "Stop";
        PlayheadLine.Visibility = Visibility.Visible;
        _playbackTimer.Start();
    }

    private void PlaybackTimer_OnTick(object? sender, EventArgs e)
    {
        var end = TimeSpan.FromSeconds(EndSlider.Value);
        if (_player.Position >= end)
        {
            StopPreview();
            return;
        }

        var width = Math.Max(1, WaveformCanvas.ActualWidth);
        var durationSeconds = Math.Max(0.1, _duration.TotalSeconds);
        Canvas.SetLeft(PlayheadLine, width * _player.Position.TotalSeconds / durationSeconds);
    }

    private void StopPreview()
    {
        _playbackTimer.Stop();
        _player.Stop();
        _playing = false;
        PlayButton.Content = "Play selected";
        PlayheadLine.Visibility = Visibility.Collapsed;
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        StopPreview();
        var start = TimeSpan.FromSeconds(StartSlider.Value);
        var duration = TimeSpan.FromSeconds(Math.Max(0.1, EndSlider.Value - StartSlider.Value));
        Selection = new RingtoneTrimSelection(start, duration);
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        StopPreview();
        DialogResult = false;
    }

    private void Window_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            StopPreview();
            DialogResult = false;
        }
    }

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPreview();
        _player.Close();
        base.OnClosed(e);
    }

    private static string FormatTime(TimeSpan value)
        => value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
