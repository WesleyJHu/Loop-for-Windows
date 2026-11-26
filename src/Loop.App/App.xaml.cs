using System;
using System.Threading;
using System.Windows;
using Loop.Core.Input;
using Loop.Platform;

namespace Loop.App;

public partial class App : Application
{
    private const string MutexName = @"Local\LoopForWindows.SingleInstance";

    /// <summary>Phase 1 default; becomes user-configurable in Phase 5.</summary>
    private const ModifierKeys DefaultTrigger = ModifierKeys.Ctrl | ModifierKeys.Win | ModifierKeys.Alt;

    private Mutex? _singleInstance;
    private LoopService? _loop;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Loop is already running.", "Loop", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _loop = new LoopService(new TriggerController(DefaultTrigger, KeyBindings.Default));
            _loop.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Loop could not start:\n{ex.Message}", "Loop", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _tray = new TrayIcon(_loop, DefaultTrigger, onQuit: () => Shutdown());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _loop?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
