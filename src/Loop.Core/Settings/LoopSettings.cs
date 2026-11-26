using System.Globalization;
using Loop.Core.Actions;
using Loop.Core.Animation;
using Loop.Core.Input;

namespace Loop.Core.Settings;

public enum ApplyMode
{
    /// <summary>Show a preview while the trigger is held; move the window when it is released (Esc cancels).</summary>
    OnRelease,
    /// <summary>Move the window as soon as each key is pressed.</summary>
    Instant,
}

/// <summary>User settings, persisted as JSON. Properties are plain data; call <see cref="Validate"/> before use.</summary>
public sealed class LoopSettings
{
    public const int MaxGap = 100;
    public const int MaxChordGraceMs = 500;
    public const int MaxCycleSizes = 10;

    public ModifierKeys Trigger { get; set; } = ModifierKeys.Ctrl | ModifierKeys.Win | ModifierKeys.Alt;
    public ApplyMode ApplyMode { get; set; } = ApplyMode.OnRelease;
    public bool ShowPreview { get; set; } = true;

    /// <summary>Preview color as #RRGGBB.</summary>
    public string PreviewColor { get; set; } = "#3B82F6";

    /// <summary>How long to wait for more arrows before applying a chord.</summary>
    public int ChordGraceMs { get; set; } = 60;

    /// <summary>Sizes a repeated layout cycles through, as fractions like "1/2" or decimals like "0.4".</summary>
    public List<string> CycleSizes { get; set; } = new() { "1/2", "1/3", "2/3" };

    /// <summary>Pixels between tiled windows and between windows and screen edges.</summary>
    public int Gap { get; set; }

    /// <summary>Key names for the four direction keys (empty = unbound).</summary>
    public Dictionary<Direction, string> DirectionKeys { get; set; } = new()
    {
        [Direction.Left] = "Left",
        [Direction.Right] = "Right",
        [Direction.Up] = "Up",
        [Direction.Down] = "Down",
    };

    /// <summary>Key names for actions (empty or missing = unbound).</summary>
    public Dictionary<WindowAction, string> ActionKeys { get; set; } = DefaultActionKeys();

    /// <summary>Process names (e.g. "notepad" or "notepad.exe") whose windows Loop never moves.</summary>
    public List<string> ExcludedApps { get; set; } = new();

    /// <summary>Fade the preview in and out and glide it between targets.</summary>
    public bool AnimatePreview { get; set; } = true;

    /// <summary>Glide windows into place instead of jumping (skipped when it would stutter).</summary>
    public bool AnimateWindows { get; set; } = true;

    /// <summary>How long a glide takes. 0 turns all animation off.</summary>
    public int AnimationDurationMs { get; set; } = 180;

    public EasingKind AnimationEasing { get; set; } = EasingKind.EaseOut;

    /// <summary>Show a radial direction indicator near the cursor while the trigger is held.</summary>
    public bool ShowIndicator { get; set; }

    public const int MaxAnimationDurationMs = 1000;

    /// <summary>The animation settings as options (call on validated settings).</summary>
    public AnimationOptions ToAnimationOptions() =>
        new(AnimatePreview, AnimateWindows, TimeSpan.FromMilliseconds(AnimationDurationMs), AnimationEasing);

    public static LoopSettings CreateDefault() => new();

    private static Dictionary<WindowAction, string> DefaultActionKeys()
    {
        var d = new Dictionary<WindowAction, string>
        {
            [WindowAction.Maximize] = "Enter",
            [WindowAction.Center] = "C",
            [WindowAction.Minimize] = "M",
            [WindowAction.NextDisplay] = "]",
            [WindowAction.PreviousDisplay] = "[",
        };
        for (var i = 0; i < 9; i++) d[WindowAction.Display1 + i] = (i + 1).ToString(CultureInfo.InvariantCulture);
        return d;
    }

    public LoopSettings Clone() => new()
    {
        Trigger = Trigger,
        ApplyMode = ApplyMode,
        ShowPreview = ShowPreview,
        PreviewColor = PreviewColor,
        ChordGraceMs = ChordGraceMs,
        CycleSizes = new List<string>(CycleSizes),
        Gap = Gap,
        DirectionKeys = new Dictionary<Direction, string>(DirectionKeys),
        ActionKeys = new Dictionary<WindowAction, string>(ActionKeys),
        ExcludedApps = new List<string>(ExcludedApps),
        AnimatePreview = AnimatePreview,
        AnimateWindows = AnimateWindows,
        AnimationDurationMs = AnimationDurationMs,
        AnimationEasing = AnimationEasing,
        ShowIndicator = ShowIndicator,
    };

    /// <summary>Every problem with these settings, as user-facing messages. Empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Trigger == ModifierKeys.None)
            errors.Add("Choose at least one trigger modifier.");
        else if (Trigger == (ModifierKeys.Ctrl | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Win))
            errors.Add("Ctrl+Win+Alt+Shift is reserved by Windows (the Office key). Choose a different trigger.");

        if (!Enum.IsDefined(ApplyMode)) errors.Add("Unknown apply mode.");
        if (!TryParseColor(PreviewColor, out _)) errors.Add($"Preview color \"{PreviewColor}\" is not a #RRGGBB color.");
        if (ChordGraceMs is < 0 or > MaxChordGraceMs) errors.Add($"Chord grace period must be 0–{MaxChordGraceMs} ms.");
        if (Gap is < 0 or > MaxGap) errors.Add($"Gap must be 0–{MaxGap} px.");
        if (AnimationDurationMs is < 0 or > MaxAnimationDurationMs)
            errors.Add($"Animation duration must be 0–{MaxAnimationDurationMs} ms.");
        if (!Enum.IsDefined(AnimationEasing)) errors.Add("Unknown animation easing.");

        if (CycleSizes is null || CycleSizes.Count is 0 or > MaxCycleSizes)
            errors.Add($"Enter 1–{MaxCycleSizes} cycle sizes.");
        else
            foreach (var s in CycleSizes)
                if (!TryParseFraction(s, out _)) errors.Add($"Cycle size \"{s}\" must be a fraction like 1/2 or a number between 0 and 1.");

        var seen = new Dictionary<int, string>();
        void CheckKey(string label, string? keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return;
            if (!KeyNames.TryParse(keyName, out var vk)) { errors.Add($"{label}: unknown key \"{keyName}\"."); return; }
            if (VirtualKeys.IsModifier(vk)) { errors.Add($"{label}: modifier keys can't be bound."); return; }
            if (vk == VirtualKeys.Escape) { errors.Add($"{label}: Escape is reserved for cancelling a preview."); return; }
            if (seen.TryGetValue(vk, out var other)) { errors.Add($"{label} and {other} both use {KeyNames.GetName(vk)}."); return; }
            seen[vk] = label;
        }

        foreach (var (direction, key) in DirectionKeys ?? new()) CheckKey($"Direction {direction}", key);
        foreach (var (action, key) in ActionKeys ?? new()) CheckKey(action.ToString(), key);

        return errors;
    }

    /// <summary>Builds the key bindings. Call only on settings that passed <see cref="Validate"/>.</summary>
    public KeyBindings ToKeyBindings()
    {
        var directions = new Dictionary<int, Direction>();
        foreach (var (direction, key) in DirectionKeys)
            if (KeyNames.TryParse(key, out var vk)) directions[vk] = direction;

        var actions = new Dictionary<int, WindowAction>();
        foreach (var (action, key) in ActionKeys)
            if (KeyNames.TryParse(key, out var vk)) actions[vk] = action;

        return new KeyBindings(directions, actions);
    }

    /// <summary>The cycle sizes as fractions. Call only on settings that passed <see cref="Validate"/>.</summary>
    public IReadOnlyList<double> ParseCycleSizes() =>
        CycleSizes.Select(s => TryParseFraction(s, out var f) ? f : 0.5).ToList();

    /// <summary>Parses "1/3", "2/3", "0.4" (must be in (0, 1]).</summary>
    public static bool TryParseFraction(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        var slash = s.IndexOf('/');
        if (slash >= 0)
        {
            if (!double.TryParse(s[..slash], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ||
                !double.TryParse(s[(slash + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ||
                d == 0)
                return false;
            value = n / d;
        }
        else if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }
        return value > 0 && value <= 1;
    }

    /// <summary>Parses #RRGGBB into its components.</summary>
    public static bool TryParseColor(string? text, out (byte R, byte G, byte B) color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#' ||
            !int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return false;
        color = ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}
