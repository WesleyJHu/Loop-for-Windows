using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Loop.Core.Settings;
using Loop.Platform;

namespace Loop.App;

public partial class App : Application
{
    private const string MutexName = @"Local\LoopForWindows.SingleInstance";

    private Mutex? _singleInstance;
    private LoopService? _loop;
    private PreviewOverlay? _overlay;
    private DirectionIndicator? _indicator;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private string _settingsPath = SettingsStore.DefaultPath;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Loop is already running. Right-click its tray icon for settings.", "Loop",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // LOOP_SETTINGS points at another settings file (used by the smoke test).
        _settingsPath = Environment.GetEnvironmentVariable("LOOP_SETTINGS") is { Length: > 0 } p ? p : SettingsStore.DefaultPath;
        var (settings, problem) = SettingsStore.Load(_settingsPath);

        try
        {
            _overlay = new PreviewOverlay(settings.PreviewColor);
            _indicator = new DirectionIndicator(settings.PreviewColor);
            _overlay.Prewarm();
            _indicator.Prewarm();
            ApplyVisualSettings(settings);
            _loop = new LoopService(settings, _overlay, _indicator);
            _loop.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Loop could not start:\n{ex.Message}", "Loop", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _tray = new TrayIcon(settings.Trigger,
            onEnabledChanged: enabled => _loop.Enabled = enabled,
            onSettings: OpenSettings,
            onQuit: () => Shutdown());

        if (problem is not null) _tray.ShowWarning(problem);
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) OpenSettings();
    }

    private void OpenSettings()
    {
        if (_loop is null) return;
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_loop.Settings, StartupRegistration.IsEnabled());
        _settingsWindow.Saved += OnSettingsSaved;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsSaved(LoopSettings settings, bool launchAtStartup)
    {
        // Throws on failure; the settings window shows the message and stays open.
        SettingsStore.Save(_settingsPath, settings);
        if (launchAtStartup != StartupRegistration.IsEnabled() && Environment.ProcessPath is { } exe)
            StartupRegistration.SetEnabled(launchAtStartup, exe);

        _loop!.ApplySettings(settings);
        ApplyVisualSettings(settings);
        _tray!.SetTrigger(settings.Trigger);
    }

    private void ApplyVisualSettings(LoopSettings settings)
    {
        var animation = settings.ToAnimationOptions();
        _overlay!.SetColor(settings.PreviewColor);
        _overlay.SetAnimation(animation);
        _indicator!.SetColor(settings.PreviewColor);
        _indicator.SetAnimation(animation);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _loop?.Dispose();
        _overlay?.Close();
        _indicator?.Close();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
