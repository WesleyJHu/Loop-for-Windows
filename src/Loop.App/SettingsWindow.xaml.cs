using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Loop.Core.Actions;
using Loop.Core.Animation;
using Loop.Core.Input;
using Loop.Core.Settings;
using ModifierKeys = Loop.Core.Input.ModifierKeys;

namespace Loop.App;

/// <summary>Edits <see cref="LoopSettings"/>. Raises <see cref="Saved"/> with validated settings.</summary>
public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<BindingRow> _rows = new();
    private BindingRow? _recording;

    /// <summary>Raised with validated settings and the launch-at-startup choice when the user saves.</summary>
    public event Action<LoopSettings, bool>? Saved;

    public SettingsWindow(LoopSettings settings, bool launchAtStartup, bool elevated)
    {
        InitializeComponent();
        BindingList.ItemsSource = _rows;
        PreviewKeyDown += OnPreviewKeyDown;
        Load(settings);
        LaunchAtStartup.IsChecked = launchAtStartup;
        ElevationState.Text = elevated ? "Loop is running as administrator now." : "Loop is running without administrator rights now.";
    }

    private void Load(LoopSettings s)
    {
        TriggerCtrl.IsChecked = s.Trigger.HasFlag(ModifierKeys.Ctrl);
        TriggerWin.IsChecked = s.Trigger.HasFlag(ModifierKeys.Win);
        TriggerAlt.IsChecked = s.Trigger.HasFlag(ModifierKeys.Alt);
        TriggerShift.IsChecked = s.Trigger.HasFlag(ModifierKeys.Shift);

        ModeOnRelease.IsChecked = s.ApplyMode == ApplyMode.OnRelease;
        ModeInstant.IsChecked = s.ApplyMode == ApplyMode.Instant;
        ShowPreview.IsChecked = s.ShowPreview;
        CycleSizes.Text = string.Join(", ", s.CycleSizes);
        Gap.Text = s.Gap.ToString(CultureInfo.InvariantCulture);
        ChordGrace.Text = s.ChordGraceMs.ToString(CultureInfo.InvariantCulture);
        PreviewColor.Text = s.PreviewColor;

        AnimateWindows.IsChecked = s.AnimateWindows;
        AnimatePreview.IsChecked = s.AnimatePreview;
        ShowIndicator.IsChecked = s.ShowIndicator;
        MouseTrigger.ItemsSource = MouseTriggerChoices;
        MouseTrigger.DisplayMemberPath = nameof(MouseTriggerChoice.Label);
        MouseTrigger.SelectedItem = MouseTriggerChoices.FirstOrDefault(c => c.Trigger == s.MouseTrigger) ?? MouseTriggerChoices[0];
        MouseDoubleClickSpeed.Text = s.MouseDoubleClickMs.ToString(CultureInfo.InvariantCulture);
        RunAsAdministrator.IsChecked = s.RunAsAdministrator;
        AnimationDuration.Text = s.AnimationDurationMs.ToString(CultureInfo.InvariantCulture);
        AnimationEasing.ItemsSource = EasingChoices;
        AnimationEasing.DisplayMemberPath = nameof(EasingChoice.Label);
        AnimationEasing.SelectedItem = EasingChoices.FirstOrDefault(c => c.Kind == s.AnimationEasing) ?? EasingChoices[0];

        StopRecording();
        _rows.Clear();
        foreach (var direction in new[] { Direction.Left, Direction.Right, Direction.Up, Direction.Down })
            _rows.Add(BindingRow.ForDirection(direction, s.DirectionKeys.GetValueOrDefault(direction, "")));
        foreach (var action in Enum.GetValues<WindowAction>())
            _rows.Add(BindingRow.ForAction(action, s.ActionKeys.GetValueOrDefault(action, "")));

        ExcludedList.Items.Clear();
        foreach (var app in s.ExcludedApps) ExcludedList.Items.Add(app);
        ShowErrors(Array.Empty<string>());
    }

    /// <summary>Reads the form into settings; returns null and lists problems if it can't.</summary>
    private LoopSettings? Read(List<string> errors)
    {
        var s = LoopSettings.CreateDefault();

        s.Trigger = ModifierKeys.None;
        if (TriggerCtrl.IsChecked == true) s.Trigger |= ModifierKeys.Ctrl;
        if (TriggerWin.IsChecked == true) s.Trigger |= ModifierKeys.Win;
        if (TriggerAlt.IsChecked == true) s.Trigger |= ModifierKeys.Alt;
        if (TriggerShift.IsChecked == true) s.Trigger |= ModifierKeys.Shift;

        s.ApplyMode = ModeInstant.IsChecked == true ? ApplyMode.Instant : ApplyMode.OnRelease;
        s.ShowPreview = ShowPreview.IsChecked == true;
        s.CycleSizes = CycleSizes.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        s.PreviewColor = PreviewColor.Text.Trim();

        if (int.TryParse(Gap.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gap)) s.Gap = gap;
        else errors.Add("Gap must be a whole number of pixels.");
        if (int.TryParse(ChordGrace.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var grace)) s.ChordGraceMs = grace;
        else errors.Add("Chord window must be a whole number of milliseconds.");

        s.AnimateWindows = AnimateWindows.IsChecked == true;
        s.AnimatePreview = AnimatePreview.IsChecked == true;
        s.ShowIndicator = ShowIndicator.IsChecked == true;
        s.MouseTrigger = (MouseTrigger.SelectedItem as MouseTriggerChoice)?.Trigger ?? Loop.Core.Input.MouseTrigger.Off;
        if (int.TryParse(MouseDoubleClickSpeed.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var doubleClick))
            s.MouseDoubleClickMs = doubleClick;
        else errors.Add("Double-click speed must be a whole number of milliseconds.");
        s.RunAsAdministrator = RunAsAdministrator.IsChecked == true;
        s.AnimationEasing = (AnimationEasing.SelectedItem as EasingChoice)?.Kind ?? EasingKind.EaseOut;
        if (int.TryParse(AnimationDuration.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration))
            s.AnimationDurationMs = duration;
        else errors.Add("Animation duration must be a whole number of milliseconds.");

        s.DirectionKeys = _rows.Where(r => r.Direction is not null).ToDictionary(r => r.Direction!.Value, r => r.KeyName);
        s.ActionKeys = _rows.Where(r => r.Action is not null && r.KeyName.Length > 0).ToDictionary(r => r.Action!.Value, r => r.KeyName);
        s.ExcludedApps = ExcludedList.Items.Cast<string>().ToList();

        errors.AddRange(s.Validate());
        return errors.Count == 0 ? s : null;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        StopRecording();
        var errors = new List<string>();
        if (Read(errors) is not { } settings)
        {
            ShowErrors(errors);
            return;
        }
        try
        {
            Saved?.Invoke(settings, LaunchAtStartup.IsChecked == true);
            Close();
        }
        catch (Exception ex)
        {
            ShowErrors(new[] { $"Could not save: {ex.Message}" });
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    /// <summary>The double-click speed only matters for the middle-button trigger.</summary>
    private void OnMouseTriggerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MouseDoubleClickSpeed is null || MouseTriggerNote is null) return;
        var middle = (MouseTrigger.SelectedItem as MouseTriggerChoice)?.Trigger == Loop.Core.Input.MouseTrigger.MiddleDoubleClick;
        MouseDoubleClickSpeed.IsEnabled = middle;
        MouseTriggerNote.Visibility = middle ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnReset(object sender, RoutedEventArgs e) => Load(LoopSettings.CreateDefault());

    private void ShowErrors(IReadOnlyCollection<string> errors)
    {
        ErrorText.Text = string.Join(Environment.NewLine, errors);
        ErrorText.Visibility = errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnColorChanged(object sender, TextChangedEventArgs e)
    {
        if (ColorSwatch is null) return;
        if (LoopSettings.TryParseColor(PreviewColor.Text.Trim(), out var c))
        {
            ColorSwatch.Background = new SolidColorBrush(Color.FromArgb(0x40, c.R, c.G, c.B));
            ColorSwatch.BorderBrush = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        }
        else
        {
            ColorSwatch.Background = Brushes.Transparent;
            ColorSwatch.BorderBrush = Brushes.LightGray;
        }
    }

    // ---- Key recording ----------------------------------------------------------------------------

    private void OnRecord(object sender, RoutedEventArgs e)
    {
        var row = (BindingRow)((FrameworkElement)sender).Tag;
        var wasThis = _recording == row;
        StopRecording();
        if (wasThis) return;
        _recording = row;
        row.IsRecording = true;
    }

    private void OnClearBinding(object sender, RoutedEventArgs e)
    {
        StopRecording();
        ((BindingRow)((FrameworkElement)sender).Tag).KeyName = "";
    }

    private void StopRecording()
    {
        if (_recording is not null) _recording.IsRecording = false;
        _recording = null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recording is null) return;
        e.Handled = true; // while recording, keys don't move focus or press buttons

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
            return; // modifiers can't be bound; wait for a real key

        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk != 0) _recording.KeyName = KeyNames.GetName(vk);
        StopRecording();
    }

    // ---- Exclusions -------------------------------------------------------------------------------

    private void OnAddExcluded(object sender, RoutedEventArgs e)
    {
        var name = ExcludedInput.Text.Trim();
        if (name.Length == 0) return;
        if (!ExcludedList.Items.Cast<string>().Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
            ExcludedList.Items.Add(name);
        ExcludedInput.Clear();
    }

    private void OnRemoveExcluded(object sender, RoutedEventArgs e)
    {
        if (ExcludedList.SelectedItem is { } item) ExcludedList.Items.Remove(item);
    }

    private void OnExcludedInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OnAddExcluded(sender, e);
        e.Handled = true;
    }

    public sealed record MouseTriggerChoice(MouseTrigger Trigger, string Label);

    private static readonly MouseTriggerChoice[] MouseTriggerChoices =
    {
        new(Loop.Core.Input.MouseTrigger.Off, "Off"),
        new(Loop.Core.Input.MouseTrigger.MiddleDoubleClick, "Double-click the middle button (wheel)"),
        new(Loop.Core.Input.MouseTrigger.BackButton, "Press the back side button"),
        new(Loop.Core.Input.MouseTrigger.ForwardButton, "Press the forward side button"),
    };

    public sealed record EasingChoice(EasingKind Kind, string Label);

    private static readonly EasingChoice[] EasingChoices =
    {
        new(EasingKind.EaseOut, "Ease out (snappy)"),
        new(EasingKind.EaseInOut, "Ease in and out (smooth)"),
        new(EasingKind.Linear, "Linear"),
    };

    /// <summary>One row of the key list: a direction or an action, and the key bound to it.</summary>
    public sealed class BindingRow : INotifyPropertyChanged
    {
        private string _keyName = "";
        private bool _isRecording;

        public required string Label { get; init; }
        public Direction? Direction { get; init; }
        public WindowAction? Action { get; init; }

        public string KeyName
        {
            get => _keyName;
            set { _keyName = value; Changed(nameof(ButtonText)); }
        }

        public bool IsRecording
        {
            get => _isRecording;
            set { _isRecording = value; Changed(nameof(ButtonText)); }
        }

        public string ButtonText => IsRecording ? "Press a key…" : KeyName.Length == 0 ? "Not set" : KeyName;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public static BindingRow ForDirection(Direction d, string key) =>
            new() { Label = $"Direction: {d}", Direction = d, KeyName = key };

        public static BindingRow ForAction(WindowAction a, string key) =>
            new() { Label = ActionLabels.Describe(a), Action = a, KeyName = key };
    }
}

internal static class ActionLabels
{
    public static string Describe(WindowAction action) => action switch
    {
        WindowAction.Maximize => "Maximize",
        WindowAction.LeftHalf => "Left half",
        WindowAction.RightHalf => "Right half",
        WindowAction.TopHalf => "Top half",
        WindowAction.BottomHalf => "Bottom half",
        WindowAction.TopLeft => "Top-left corner",
        WindowAction.TopRight => "Top-right corner",
        WindowAction.BottomLeft => "Bottom-left corner",
        WindowAction.BottomRight => "Bottom-right corner",
        WindowAction.Center => "Center (keep size)",
        WindowAction.CenterQuarter => "Center (corner size)",
        WindowAction.Minimize => "Minimize",
        WindowAction.RestorePrevious => "Previous size and position (undo)",
        WindowAction.NextDisplay => "Next display",
        WindowAction.PreviousDisplay => "Previous display",
        _ when action.DisplayIndex() is { } n => $"Display {n + 1}",
        _ => action.ToString(),
    };
}
