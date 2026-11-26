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
    private const string OpenSettingsEventName = @"Local\LoopForWindows.OpenSettings";

    /// <summary>Marks a start that replaces the running Loop (to gain or drop administrator rights).</summary>
    private const string RestartedArg = "--restarted";

    private EventWaitHandle? _openSettingsSignal;
    private RegisteredWaitHandle? _openSettingsWait;

    /// <summary>
    /// How long a new instance waits for a running one to exit. Covers Loop restarting itself (to gain
    /// or drop administrator rights): the new process starts just before the old one quits.
    /// </summary>
    private static readonly TimeSpan HandOverWait = TimeSpan.FromSeconds(3);

    private Mutex? _singleInstance;
    private LoopService? _loop;
    private PreviewOverlay? _overlay;
    private DirectionIndicator? _indicator;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private string _settingsPath = SettingsStore.DefaultPath;

    private static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = AcquireSingleInstance(TimeSpan.Zero);
        if (_singleInstance is null)
        {
            // Started again (Start menu, the settings shortcut) while Loop runs: show its settings.
            // A restart (--restarted) instead waits for the old instance to quit and takes over.
            var restarted = e.Args.Contains(RestartedArg, StringComparer.OrdinalIgnoreCase);
            if (!restarted && SignalRunningInstance())
            {
                Shutdown();
                return;
            }

            _singleInstance = AcquireSingleInstance(HandOverWait);
            if (_singleInstance is null)
            {
                MessageBox.Show("Loop is already running. Right-click its tray icon for settings.", "Loop",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }
        ListenForOpenSettings();

        // LOOP_SETTINGS points at another settings file (used by the smoke test).
        _settingsPath = Environment.GetEnvironmentVariable("LOOP_SETTINGS") is { Length: > 0 } p ? p : SettingsStore.DefaultPath;
        var (settings, problem) = SettingsStore.Load(_settingsPath);

        // Set to run as administrator but started normally (Start menu, double-click): ask Windows
        // for administrator rights and hand over to the elevated instance.
        var elevationDeclined = false;
        if (settings.RunAsAdministrator && !Elevation.IsElevated)
        {
            if (Elevation.StartElevated(ExePath, RestartedArg))
            {
                Shutdown();
                return;
            }
            elevationDeclined = true;
        }

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

        _tray = new TrayIcon(settings.Trigger, Elevation.IsElevated,
            onEnabledChanged: enabled => _loop.Enabled = enabled,
            onSettings: OpenSettings,
            onQuit: () => Shutdown());

        if (problem is not null) _tray.ShowWarning(problem);
        else if (elevationDeclined)
            _tray.ShowWarning("Loop is running without administrator rights, so it can't move windows of apps that run as administrator.");
        else ReconcileStartup(settings);

        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) OpenSettings();
    }

    /// <summary>Makes the sign-in registration match the settings (if the user ever chose in Loop).</summary>
    private void ReconcileStartup(LoopSettings settings)
    {
        if (settings.LaunchAtStartup is not { } launch) return;
        if (StartupRegistration.Apply(launch, settings.RunAsAdministrator, ExePath) is { } startupProblem)
            _tray?.ShowWarning(startupProblem);
    }

    private void OpenSettings()
    {
        if (_loop is null) return;
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var settings = _loop.Settings;
        _settingsWindow = new SettingsWindow(settings, settings.LaunchAtStartup ?? StartupRegistration.IsEnabled(), Elevation.IsElevated);
        _settingsWindow.Saved += OnSettingsSaved;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsSaved(LoopSettings settings, bool launchAtStartup)
    {
        settings.LaunchAtStartup = launchAtStartup;

        // Throws on failure; the settings window shows the message and stays open.
        SettingsStore.Save(_settingsPath, settings);

        if (settings.RunAsAdministrator && !Elevation.IsElevated)
        {
            // The elevated instance sets up sign-in registration when it starts.
            if (Elevation.StartElevated(ExePath, RestartedArg))
            {
                Dispatcher.BeginInvoke(() => Shutdown());
                return;
            }
            _tray!.ShowWarning("Windows didn't give Loop administrator rights, so it keeps running without them. " +
                               "It will ask again the next time Loop starts.");
        }
        else if (!settings.RunAsAdministrator && Elevation.IsElevated)
        {
            ReconcileStartup(settings); // remove the elevated startup task while we still have the rights to
            Elevation.StartUnelevated(ExePath);
            Dispatcher.BeginInvoke(() => Shutdown());
            return;
        }
        else
        {
            ReconcileStartup(settings);
        }

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

    /// <summary>Lets a second launch of Loop ask this one to show its settings.</summary>
    private void ListenForOpenSettings()
    {
        try
        {
            _openSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsEventName);
            _openSettingsWait = ThreadPool.RegisterWaitForSingleObject(_openSettingsSignal,
                (_, _) => Dispatcher.BeginInvoke(OpenSettings), null, Timeout.Infinite, executeOnlyOnce: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // Another (elevated) Loop's signal we can't use; second launches just get the usual message.
        }
    }

    /// <summary>Asks the running Loop to open its settings. False if it can't be reached.</summary>
    private static bool SignalRunningInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(OpenSettingsEventName);
            return signal.Set();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            return false; // not running yet/anymore, or running as administrator (out of reach)
        }
    }

    /// <summary>
    /// Takes the single-instance lock, waiting up to <paramref name="wait"/> for another Loop to exit.
    /// Returns null if another Loop keeps running.
    /// </summary>
    private static Mutex? AcquireSingleInstance(TimeSpan wait)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            try
            {
                var mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
                if (created) return mutex;
                try
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (mutex.WaitOne(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero)) return mutex;
                }
                catch (AbandonedMutexException)
                {
                    return mutex; // the other Loop exited without releasing: ours now
                }
                mutex.Dispose();
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                // Held by a Loop running as administrator, which this one can't open. It may be
                // restarting us without administrator rights: wait for it to go away.
                if (DateTime.UtcNow >= deadline) return null;
                Thread.Sleep(200);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _openSettingsWait?.Unregister(null);
        _openSettingsSignal?.Dispose();
        _tray?.Dispose();
        _loop?.Dispose();
        _overlay?.Close();
        _indicator?.Close();
        if (_singleInstance is not null)
        {
            try { _singleInstance.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }
}
