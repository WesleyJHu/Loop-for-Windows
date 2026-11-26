using Loop.Core.Actions;
using Loop.Core.Input;
using Loop.Core.Settings;

namespace Loop.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void Defaults_AreValid() => Assert.Empty(LoopSettings.CreateDefault().Validate());

    [Fact]
    public void Defaults_MatchBuiltInBindings()
    {
        var fromSettings = LoopSettings.CreateDefault().ToKeyBindings();
        Assert.Equal(KeyBindings.Default.DirectionKeys.OrderBy(k => k.Key), fromSettings.DirectionKeys.OrderBy(k => k.Key));
        Assert.Equal(KeyBindings.Default.ActionKeys.OrderBy(k => k.Key), fromSettings.ActionKeys.OrderBy(k => k.Key));
    }

    [Fact]
    public void Defaults_CycleSizes()
    {
        var sizes = LoopSettings.CreateDefault().ParseCycleSizes();
        Assert.Equal(CycleTracker.DefaultFractions, sizes);
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var s = LoopSettings.CreateDefault();
        s.Trigger = ModifierKeys.Ctrl | ModifierKeys.Shift;
        s.ApplyMode = ApplyMode.Instant;
        s.ShowPreview = false;
        s.PreviewColor = "#FF8800";
        s.ChordGraceMs = 90;
        s.Gap = 12;
        s.CycleSizes = new() { "1/2", "0.25" };
        s.DirectionKeys[Direction.Left] = "H";
        s.ActionKeys[WindowAction.TopLeft] = "U";
        s.ActionKeys.Remove(WindowAction.Minimize);
        s.ExcludedApps = new() { "notepad.exe" };

        var back = SettingsStore.Deserialize(SettingsStore.Serialize(s));

        Assert.Equal(SettingsStore.Serialize(s), SettingsStore.Serialize(back));
        Assert.Equal(ModifierKeys.Ctrl | ModifierKeys.Shift, back.Trigger);
        Assert.Equal("U", back.ActionKeys[WindowAction.TopLeft]);
        Assert.False(back.ActionKeys.ContainsKey(WindowAction.Minimize));
    }

    [Fact]
    public void Json_UsesReadableNames()
    {
        var json = SettingsStore.Serialize(LoopSettings.CreateDefault());
        Assert.Contains("\"Trigger\": \"Ctrl, Alt, Win\"", json);
        Assert.Contains("\"ApplyMode\": \"OnRelease\"", json);
        Assert.Contains("\"NextDisplay\": \"]\"", json);
        Assert.Contains("\"1/3\"", json);
    }

    [Fact]
    public void PartialFile_FillsInDefaults()
    {
        var s = SettingsStore.Deserialize("""
            { "Gap": 8, // comments and trailing commas are allowed
              "ExcludedApps": null, }
            """);
        Assert.Equal(8, s.Gap);
        Assert.Empty(s.ExcludedApps);
        Assert.Equal(LoopSettings.CreateDefault().Trigger, s.Trigger);
        Assert.Empty(s.Validate());
    }

    [Theory]
    [InlineData(ModifierKeys.None)]
    [InlineData(ModifierKeys.Ctrl | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Win)]
    public void InvalidTrigger(ModifierKeys trigger)
    {
        var s = LoopSettings.CreateDefault();
        s.Trigger = trigger;
        Assert.Single(s.Validate());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidGap(int gap)
    {
        var s = LoopSettings.CreateDefault();
        s.Gap = gap;
        Assert.Contains(s.Validate(), e => e.Contains("Gap"));
    }

    [Fact]
    public void InvalidGrace()
    {
        var s = LoopSettings.CreateDefault();
        s.ChordGraceMs = 1000;
        Assert.Contains(s.Validate(), e => e.Contains("grace"));
    }

    [Theory]
    [InlineData("blue")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void InvalidColor(string color)
    {
        var s = LoopSettings.CreateDefault();
        s.PreviewColor = color;
        Assert.Contains(s.Validate(), e => e.Contains("color"));
    }

    [Fact]
    public void DuplicateKey_IsReported()
    {
        var s = LoopSettings.CreateDefault();
        s.ActionKeys[WindowAction.TopLeft] = "Left";
        Assert.Contains(s.Validate(), e => e.Contains("both use Left"));
    }

    [Theory]
    [InlineData("LeftShift", "modifier")]
    [InlineData("Escape", "reserved")]
    [InlineData("Banana", "unknown key")]
    public void BadKey_IsReported(string key, string message)
    {
        var s = LoopSettings.CreateDefault();
        s.ActionKeys[WindowAction.Center] = key;
        Assert.Contains(s.Validate(), e => e.Contains(message));
    }

    [Fact]
    public void UnboundKeys_AreAllowed()
    {
        var s = LoopSettings.CreateDefault();
        s.DirectionKeys[Direction.Up] = "";
        s.ActionKeys.Clear();
        Assert.Empty(s.Validate());
        Assert.Equal(3, s.ToKeyBindings().DirectionKeys.Count);
        Assert.Empty(s.ToKeyBindings().ActionKeys);
    }

    [Theory]
    [InlineData("1/2", 0.5)]
    [InlineData(" 2/3 ", 2.0 / 3)]
    [InlineData("0.4", 0.4)]
    [InlineData("1", 1.0)]
    public void Fractions_Parse(string text, double expected)
    {
        Assert.True(LoopSettings.TryParseFraction(text, out var f));
        Assert.Equal(expected, f, 10);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3/2")]
    [InlineData("1/0")]
    [InlineData("half")]
    [InlineData("")]
    [InlineData("-1/3")]
    public void Fractions_Reject(string text) => Assert.False(LoopSettings.TryParseFraction(text, out _));

    [Fact]
    public void CycleSizes_Validated()
    {
        var s = LoopSettings.CreateDefault();
        s.CycleSizes = new() { "1/2", "nope" };
        Assert.Contains(s.Validate(), e => e.Contains("nope"));
        s.CycleSizes = new();
        Assert.NotEmpty(s.Validate());
    }

    [Fact]
    public void Color_Parses() =>
        Assert.True(LoopSettings.TryParseColor("#3B82F6", out var c) && c == (0x3B, 0x82, 0xF6));

    [Fact]
    public void Clone_IsDeep()
    {
        var s = LoopSettings.CreateDefault();
        var c = s.Clone();
        c.ExcludedApps.Add("x");
        c.ActionKeys[WindowAction.Center] = "Z";
        Assert.Empty(s.ExcludedApps);
        Assert.Equal("C", s.ActionKeys[WindowAction.Center]);
    }
}

public class SettingsStoreFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LoopTests-" + Guid.NewGuid().ToString("N"));
    private string PathIn(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        var (s, problem) = SettingsStore.Load(PathIn("none.json"));
        Assert.Null(problem);
        Assert.Equal(SettingsStore.Serialize(LoopSettings.CreateDefault()), SettingsStore.Serialize(s));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_AndCreatesFolder()
    {
        var path = PathIn(Path.Combine("nested", "settings.json"));
        var s = LoopSettings.CreateDefault();
        s.Gap = 6;
        SettingsStore.Save(path, s);

        var (loaded, problem) = SettingsStore.Load(path);
        Assert.Null(problem);
        Assert.Equal(6, loaded.Gap);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void CorruptFile_GivesDefaultsAndExplains()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn("bad.json"), "{ not json");
        var (s, problem) = SettingsStore.Load(PathIn("bad.json"));
        Assert.NotNull(problem);
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void InvalidValues_GiveDefaultsAndExplain()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn("invalid.json"), """{ "Gap": 500 }""");
        var (s, problem) = SettingsStore.Load(PathIn("invalid.json"));
        Assert.Contains("Gap", problem);
        Assert.Equal(0, s.Gap);
    }
}

public class KeyNamesTests
{
    [Theory]
    [InlineData(0x25, "Left")]
    [InlineData(0x43, "C")]
    [InlineData(0x31, "1")]
    [InlineData(0xDD, "]")]
    [InlineData(0x0D, "Enter")]
    [InlineData(0x70, "F1")]
    [InlineData(0x87, "F24")]
    [InlineData(0x65, "Numpad5")]
    [InlineData(0xE8, "VK 0xE8")]
    public void NamesRoundTrip(int vk, string name)
    {
        Assert.Equal(name, KeyNames.GetName(vk));
        Assert.True(KeyNames.TryParse(name, out var parsed));
        Assert.Equal(vk, parsed);
    }

    [Fact]
    public void Parse_IsCaseInsensitive() => Assert.True(KeyNames.TryParse("enter", out var vk) && vk == 0x0D);

    [Theory]
    [InlineData("")]
    [InlineData("NotAKey")]
    [InlineData("VK 0x00")]
    [InlineData("VK 0xZZ")]
    public void Parse_Rejects(string text) => Assert.False(KeyNames.TryParse(text, out _));

    [Fact]
    public void FormatTrigger() =>
        Assert.Equal("Ctrl+Win+Alt", KeyNames.Format(ModifierKeys.Alt | ModifierKeys.Ctrl | ModifierKeys.Win));
}
