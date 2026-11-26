<#
.SYNOPSIS
  End-to-end smoke test: launches Loop and a throwaway WinForms window, injects trigger chords
  with SendInput, and checks where the window (and the preview overlay) land.

.NOTES
  - Steals keyboard focus for about a minute; don't type while it runs.
  - Loop runs with its own temporary settings file (LOOP_SETTINGS); your settings are untouched.
    Quit any running Loop first (only one instance can run).
  - The target is a DPI-unaware WinForms window: the hardest case for mixed-DPI setups.
  - Display checks run only when more than one monitor is connected.
  - Run:  .\tests\smoke\Invoke-SmokeTest.ps1
#>
param(
    [string]$AppExe = (Join-Path $PSScriptRoot '..\..\src\Loop.App\bin\Debug\net8.0-windows\LoopForWindows.exe')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $AppExe)) { throw "Build first: $AppExe not found (dotnet build)" }
if (Get-Process LoopForWindows -ErrorAction SilentlyContinue) { throw "Quit the running Loop first (tray icon > Quit)." }

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class LoopSmoke {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cb; public RECT mon; public RECT work; public uint flags; }
  [StructLayout(LayoutKind.Sequential)] public struct KI { public ushort vk; public ushort scan; public uint flags; public uint time; public UIntPtr extra; }
  [StructLayout(LayoutKind.Explicit, Size=40)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KI ki; }
  delegate bool MonEnum(IntPtr m, IntPtr dc, ref RECT r, IntPtr d);
  [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int cb);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
  [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
  [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonEnum cb, IntPtr d);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int cb);
  [StructLayout(LayoutKind.Sequential)] public struct MI { public int dx; public int dy; public uint data; public uint flags; public uint time; public UIntPtr extra; }
  [StructLayout(LayoutKind.Explicit, Size=40)] public struct MINPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MI mi; }
  [DllImport("user32.dll", EntryPoint="SendInput")] static extern uint SendMouseInput(uint n, MINPUT[] i, int cb);
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
  public static void Mouse(uint flags, int dx, int dy, uint data) {
    var i = new MINPUT[1]; i[0].type = 0; i[0].mi.flags = flags; i[0].mi.dx = dx; i[0].mi.dy = dy; i[0].mi.data = data;
    SendMouseInput(1, i, Marshal.SizeOf(typeof(MINPUT)));
  }
  // Moves the cursor to (x, y) in physical pixels, as real mouse input (no pointer acceleration).
  public static void MoveTo(int x, int y) {
    int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
    Mouse(0x0001 | 0x8000 | 0x4000, (int)((x - vx) * 65535L / (vw - 1)), (int)((y - vy) * 65535L / (vh - 1)), 0);
  }
  public static void Key(int vk, bool up) {
    var i = new INPUT[1]; i[0].type = 1; i[0].ki.vk = (ushort)vk; i[0].ki.flags = up ? 2u : 0u;
    SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
  }
  static int[] R(RECT r) { return new[] { r.L, r.T, r.R - r.L, r.B - r.T }; }
  // Rects are [left, top, width, height] in physical pixels.
  public static int[] Frame(IntPtr h) { RECT r; DwmGetWindowAttribute(h, 9, out r, 16); return R(r); }
  public static int[] Outer(IntPtr h) { RECT r; GetWindowRect(h, out r); return R(r); }
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr h, string name);
  // The rectangle the preview overlay is drawing (it publishes it as window properties).
  public static int[] Preview(IntPtr h) {
    return new[] { (int)GetProp(h, "Loop.Preview.Left"), (int)GetProp(h, "Loop.Preview.Top"),
                   (int)GetProp(h, "Loop.Preview.Width"), (int)GetProp(h, "Loop.Preview.Height") };
  }
  public static int[] Work(IntPtr h) {
    var mi = new MONITORINFO(); mi.cb = Marshal.SizeOf(typeof(MONITORINFO));
    GetMonitorInfo(MonitorFromWindow(h, 2), ref mi); return R(mi.work);
  }
  public static List<int[]> WorkAreas() {
    var list = new List<int[]>();
    EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr dc, ref RECT r, IntPtr d) => {
      var mi = new MONITORINFO(); mi.cb = Marshal.SizeOf(typeof(MONITORINFO)); GetMonitorInfo(m, ref mi);
      list.Add(R(mi.work)); return true; }, IntPtr.Zero);
    return list;
  }
}
'@
[LoopSmoke]::SetProcessDpiAwarenessContext([IntPtr]-4) | Out-Null  # per-monitor v2: physical pixels

$VK = @{ Left = 0x25; Up = 0x26; Right = 0x27; Down = 0x28; Enter = 0x0D; C = 0x43; M = 0x4D; Next = 0xDD; Prev = 0xDB; Esc = 0x1B; Back = 0x08 }
$title = "LoopSmokeTarget-$PID"
$formScript = "Add-Type -AssemblyName System.Windows.Forms; `$f = New-Object Windows.Forms.Form; `$f.Text = '$title'; " +
              "`$f.StartPosition = 'Manual'; `$f.Left = 300; `$f.Top = 200; `$f.Width = 800; `$f.Height = 600; [Windows.Forms.Application]::Run(`$f)"
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($formScript))
$settingsFile = Join-Path ([IO.Path]::GetTempPath()) "loop-smoke-settings-$PID.json"
$script:failures = 0
$script:app = $null

function Format-Rect([int[]]$r) { "({0},{1} {2}x{3})" -f $r[0], $r[1], $r[2], $r[3] }
function Round-Half([double]$v) { [int][Math]::Round($v, [MidpointRounding]::AwayFromZero) }

# Mirrors LayoutEngine: leading segment = round(len*f); trailing starts at round(len*(1-f)).
function Lead([int]$start, [int]$len, [double]$f) { @($start, (Round-Half ($len * $f))) }
function Trail([int]$start, [int]$len, [double]$f) { $o = Round-Half ($len * (1 - $f)); @(($start + $o), ($len - $o)) }

function Expect-Layout([string]$action, [int[]]$work, [double]$f = 0.5, [int[]]$current = $null) {
    $x, $y, $w, $hgt = $work
    $l = Lead $x $w $f; $r = Trail $x $w $f; $t = Lead $y $hgt $f; $b = Trail $y $hgt $f
    $ct = Lead $y $hgt 0.5; $cb = Trail $y $hgt 0.5
    switch ($action) {
        'Maximize'      { return @($x, $y, $w, $hgt) }
        'LeftHalf'      { return @($l[0], $y, $l[1], $hgt) }
        'RightHalf'     { return @($r[0], $y, $r[1], $hgt) }
        'TopHalf'       { return @($x, $t[0], $w, $t[1]) }
        'BottomHalf'    { return @($x, $b[0], $w, $b[1]) }
        'TopLeft'       { return @($l[0], $ct[0], $l[1], $ct[1]) }
        'TopRight'      { return @($r[0], $ct[0], $r[1], $ct[1]) }
        'BottomLeft'    { return @($l[0], $cb[0], $l[1], $cb[1]) }
        'BottomRight'   { return @($r[0], $cb[0], $r[1], $cb[1]) }
        'Center'        { $cw = [Math]::Min($current[2], $w); $ch = [Math]::Min($current[3], $hgt)
                          return @(($x + [Math]::Floor(($w - $cw) / 2)), ($y + [Math]::Floor(($hgt - $ch) / 2)), $cw, $ch) }
        'CenterQuarter' { return @(($x + [Math]::Floor(($w - $l[1]) / 2)), ($y + [Math]::Floor(($hgt - $ct[1]) / 2)), $l[1], $ct[1]) }
    }
}

# Mirrors LayoutEngine gaps: edges on the work-area border move in by the gap, inner edges by half.
function With-Gap([int[]]$r, [int[]]$work, [int]$gap) {
    $half = [Math]::Floor($gap / 2); $rest = $gap - $half
    $left = if ($r[0] -eq $work[0]) { $r[0] + $gap } else { $r[0] + $rest }
    $top = if ($r[1] -eq $work[1]) { $r[1] + $gap } else { $r[1] + $rest }
    $right = if ($r[0] + $r[2] -eq $work[0] + $work[2]) { $r[0] + $r[2] - $gap } else { $r[0] + $r[2] - $half }
    $bottom = if ($r[1] + $r[3] -eq $work[1] + $work[3]) { $r[1] + $r[3] - $gap } else { $r[1] + $r[3] - $half }
    @($left, $top, ($right - $left), ($bottom - $top))
}

function Start-Loop([hashtable]$settings) {
    Stop-Loop
    ($settings | ConvertTo-Json -Depth 4) | Set-Content -Path $settingsFile -Encoding UTF8
    $env:LOOP_SETTINGS = $settingsFile
    $script:app = Start-Process $AppExe -PassThru
    Remove-Item Env:LOOP_SETTINGS
    Start-Sleep -Milliseconds 1500
}

function Stop-Loop {
    if ($script:app) { Stop-Process -Id $script:app.Id -ErrorAction SilentlyContinue; $script:app.WaitForExit(); $script:app = $null }
}

$target = Start-Process powershell -ArgumentList @('-NoProfile', '-WindowStyle', 'Hidden', '-EncodedCommand', $encoded) -PassThru

try {
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 50 -and $h -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 200
        $h = [LoopSmoke]::FindWindow([NullString]::Value, $title)
    }
    if ($h -eq [IntPtr]::Zero) { throw "Target window never appeared." }

    function Focus-Target {
        # Synthesized input makes this process the last input owner, lifting the foreground lock.
        for ($attempt = 0; $attempt -lt 5; $attempt++) {
            [LoopSmoke]::Key(0xE8, $false); [LoopSmoke]::Key(0xE8, $true)
            [LoopSmoke]::SetForegroundWindow($h) | Out-Null
            Start-Sleep -Milliseconds 200
            if ([LoopSmoke]::GetForegroundWindow() -eq $h) { return }
        }
        throw "Could not focus the target window."
    }

    function Hold-Trigger { foreach ($m in 0xA2, 0x5B, 0xA4) { [LoopSmoke]::Key($m, $false) } }   # LCtrl, LWin, LAlt
    function Release-Modifiers { foreach ($m in 0xA4, 0x5B, 0xA2) { [LoopSmoke]::Key($m, $true) } }
    # Waits long enough for the (default 180 ms) glide and the final placement.
    function Release-Trigger { Release-Modifiers; Start-Sleep -Milliseconds 500 }

    # Samples a rect every ~10 ms for a while (e.g. to watch a glide).
    function Sample([scriptblock]$read, [int]$ms) {
        $samples = New-Object System.Collections.Generic.List[object]
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.ElapsedMilliseconds -lt $ms) { $samples.Add((& $read)); Start-Sleep -Milliseconds 10 }
        , $samples
    }
    function Tap([int[]]$keys) {
        foreach ($k in $keys) { [LoopSmoke]::Key($k, $false); Start-Sleep -Milliseconds 15 }
        Start-Sleep -Milliseconds 150   # longer than Loop's chord grace period
        foreach ($k in $keys) { [LoopSmoke]::Key($k, $true) }
        Start-Sleep -Milliseconds 100
    }

    # One full activation: hold the trigger, press the keys together, release everything.
    function Send-Chord([int[]]$keys) { Focus-Target; Hold-Trigger; Tap $keys; Release-Trigger }

    function Check([string]$name, [int[]]$expected, [int[]]$actual = $null, [int]$tolerance = 0) {
        if ($null -eq $actual) { $actual = [LoopSmoke]::Frame($h) }
        $ok = $true
        for ($i = 0; $i -lt 4; $i++) { if ([Math]::Abs($actual[$i] - $expected[$i]) -gt $tolerance) { $ok = $false } }
        if ($ok) {
            Write-Host ("PASS  {0,-38} {1}" -f $name, (Format-Rect $actual)) -ForegroundColor Green
        } else {
            Write-Host ("FAIL  {0,-38} expected {1}, got {2}" -f $name, (Format-Rect $expected), (Format-Rect $actual)) -ForegroundColor Red
            $script:failures++
        }
    }

    function Check-True([string]$name, [bool]$ok, [string]$detail = '') {
        if ($ok) { Write-Host ("PASS  {0,-38} {1}" -f $name, $detail) -ForegroundColor Green }
        else { Write-Host ("FAIL  {0,-38} {1}" -f $name, $detail) -ForegroundColor Red; $script:failures++ }
    }

    function Get-Overlay { [LoopSmoke]::FindWindow([NullString]::Value, 'Loop preview') }
    function Overlay-Visible { $o = Get-Overlay; ($o -ne [IntPtr]::Zero) -and [LoopSmoke]::IsWindowVisible($o) }
    function Inset([int[]]$r) { @(($r[0] + 4), ($r[1] + 4), ($r[2] - 8), ($r[3] - 8)) }  # PreviewOverlay.Inset

    # ============================ Preview mode (the default) =====================================
    Start-Loop @{}
    $work = [LoopSmoke]::Work($h)
    Write-Host "Work area $(Format-Rect $work)   [default settings: preview, apply on release]"

    $cases = @(
        @{ Name = 'Left';        Keys = @($VK.Left);            Action = 'LeftHalf' },
        @{ Name = 'Right';       Keys = @($VK.Right);           Action = 'RightHalf' },
        @{ Name = 'Up';          Keys = @($VK.Up);              Action = 'TopHalf' },
        @{ Name = 'Down';        Keys = @($VK.Down);            Action = 'BottomHalf' },
        @{ Name = 'Up+Left';     Keys = @($VK.Up, $VK.Left);    Action = 'TopLeft' },
        @{ Name = 'Up+Right';    Keys = @($VK.Up, $VK.Right);   Action = 'TopRight' },
        @{ Name = 'Down+Left';   Keys = @($VK.Down, $VK.Left);  Action = 'BottomLeft' },
        @{ Name = 'Down+Right';  Keys = @($VK.Down, $VK.Right); Action = 'BottomRight' }
    )
    foreach ($c in $cases) { Send-Chord $c.Keys; Check $c.Name (Expect-Layout $c.Action $work) }

    # ---- Enter = the window's own maximize/restore button ---------------------------------------
    $before = [LoopSmoke]::Frame($h)
    Send-Chord @($VK.Enter)
    Check-True 'Enter: really maximized' ([LoopSmoke]::IsZoomed($h)) "frame $(Format-Rect ([LoopSmoke]::Frame($h)))"
    Check-True 'Enter: on the same display' ((([LoopSmoke]::Work($h)) -join ',') -eq ($work -join ','))
    Send-Chord @($VK.Enter)
    Check-True 'Enter again: restored' (-not [LoopSmoke]::IsZoomed($h))
    Check 'Enter again: back to previous size' $before
    Send-Chord @($VK.Enter)
    Send-Chord @($VK.Left, $VK.Right)
    Check-True 'Left+Right on maximized: restored' (-not [LoopSmoke]::IsZoomed($h))
    Check 'Left+Right on maximized: centered at old size' (Expect-Layout 'Center' $work 0.5 $before)

    # ---- The preview itself --------------------------------------------------------------------
    Send-Chord @($VK.Up, $VK.Left)
    $before = [LoopSmoke]::Frame($h)
    Focus-Target; Hold-Trigger; Tap @($VK.Right)
    Start-Sleep -Milliseconds 150
    Check 'holding: window stays put' $before
    Check-True 'holding: preview visible' (Overlay-Visible)
    if (Overlay-Visible) { Check 'holding: preview over target' (Inset (Expect-Layout 'RightHalf' $work)) ([LoopSmoke]::Preview((Get-Overlay))) }
    Release-Trigger
    Check 'release: window moves' (Expect-Layout 'RightHalf' $work)
    Check-True 'release: preview hidden' (-not (Overlay-Visible))

    Focus-Target; Hold-Trigger; Tap @($VK.Left); Tap @($VK.Esc); Release-Trigger
    Check 'Esc cancels' (Expect-Layout 'RightHalf' $work)
    Check-True 'Esc hides preview' (-not (Overlay-Visible))

    Focus-Target; Hold-Trigger; Tap @($VK.Left); Tap @($VK.Left); Tap @($VK.Left); Release-Trigger
    Check 'Left x3 in one hold -> 2/3' (Expect-Layout 'LeftHalf' $work (2.0 / 3))
    Focus-Target; Hold-Trigger; Tap @($VK.Left); Tap @($VK.Esc); Release-Trigger
    Send-Chord @($VK.Left)
    Check 'cycle continues after Esc -> 1/2' (Expect-Layout 'LeftHalf' $work)

    # ---- Centering chords ----------------------------------------------------------------------
    Send-Chord @($VK.Up, $VK.Left)
    $before = [LoopSmoke]::Frame($h)
    Send-Chord @($VK.Left, $VK.Right)
    Check 'Left+Right (center)' (Expect-Layout 'Center' $work 0.5 $before)
    Send-Chord @($VK.Down, $VK.Left)
    $before = [LoopSmoke]::Frame($h)
    Send-Chord @($VK.Up, $VK.Down)
    Check 'Up+Down (center)' (Expect-Layout 'Center' $work 0.5 $before)
    Send-Chord @($VK.Left, $VK.Right, $VK.Up, $VK.Down)
    Check 'All four (corner size, centered)' (Expect-Layout 'CenterQuarter' $work)
    Send-Chord @($VK.C)
    Check 'C keeps size' (Expect-Layout 'CenterQuarter' $work)

    # ---- Cycling across activations -------------------------------------------------------------
    foreach ($seq in @(@{ Keys = @($VK.Left); Action = 'LeftHalf'; Name = 'Left' },
                       @{ Keys = @($VK.Down); Action = 'BottomHalf'; Name = 'Down' },
                       @{ Keys = @($VK.Up, $VK.Right); Action = 'TopRight'; Name = 'Up+Right' },
                       @{ Keys = @($VK.Left, $VK.Right, $VK.Up, $VK.Down); Action = 'CenterQuarter'; Name = 'All four' })) {
        Send-Chord @($VK.Enter)
        $i = 0
        foreach ($f in @(0.5, (1.0 / 3), (2.0 / 3), 0.5)) {
            Send-Chord $seq.Keys
            $i++
            Check ("{0} press {1} ({2:0.##})" -f $seq.Name, $i, $f) (Expect-Layout $seq.Action $work $f)
        }
    }

    Send-Chord @($VK.Left); Send-Chord @($VK.Left)          # now at 1/3
    [LoopSmoke]::SetWindowPos($h, [IntPtr]::Zero, 400, 300, 700, 500, 0x14) | Out-Null; Start-Sleep -Milliseconds 200
    Send-Chord @($VK.Left)
    Check 'Left after manual move -> 1/2' (Expect-Layout 'LeftHalf' $work)

    Focus-Target
    $before = [LoopSmoke]::Frame($h)
    [LoopSmoke]::Key($VK.Left, $false); [LoopSmoke]::Key($VK.Left, $true)
    Start-Sleep -Milliseconds 300
    Check 'bare Left (ignored)' $before

    # ---- Restore previous (undo) ------------------------------------------------------------------
    [LoopSmoke]::SetWindowPos($h, [IntPtr]::Zero, 300, 200, 800, 600, 0x14) | Out-Null; Start-Sleep -Milliseconds 200
    $free = [LoopSmoke]::Frame($h)
    Send-Chord @($VK.Left); Send-Chord @($VK.Up, $VK.Right); Send-Chord @($VK.Left)
    Send-Chord @($VK.Back)
    Check 'Backspace: back to where it was' $free
    Send-Chord @($VK.Back)
    Check 'Backspace again: nothing to undo' $free
    [LoopSmoke]::ShowWindow($h, 3) | Out-Null; Start-Sleep -Milliseconds 400   # maximized by the "user"
    Send-Chord @($VK.Down)
    Send-Chord @($VK.Back)
    Check-True 'undo: maximized again' ([LoopSmoke]::IsZoomed($h))
    [LoopSmoke]::ShowWindow($h, 9) | Out-Null; Start-Sleep -Milliseconds 400
    Check 'undo: restoring then gives the old size' $free

    # ---- Displays --------------------------------------------------------------------------------
    $areas = [LoopSmoke]::WorkAreas()
    if ($areas.Count -lt 2) {
        Write-Host "SKIP  display checks (only one monitor)" -ForegroundColor Yellow
    } else {
        $startWork = [LoopSmoke]::Work($h)

        Send-Chord @($VK.Enter)
        Send-Chord @($VK.Left)
        Send-Chord @($VK.Next)
        $destWork = [LoopSmoke]::Work($h)
        Check-True ']' (($destWork -join ',') -ne ($startWork -join ',')) "moved to $(Format-Rect $destWork)"
        Check '] keeps left half' (Expect-Layout 'LeftHalf' $destWork)
        Send-Chord @($VK.Left)
        Check 'Left cycles on new display -> 1/3' (Expect-Layout 'LeftHalf' $destWork (1.0 / 3))
        Send-Chord @($VK.Prev)
        Check '[ keeps 1/3 on the way back' (Expect-Layout 'LeftHalf' $startWork (1.0 / 3))

        Focus-Target; Hold-Trigger; Tap @($VK.Next); Tap @($VK.Down, $VK.Right); Release-Trigger
        Check '] then Down+Right in one hold' (Expect-Layout 'BottomRight' $destWork)
        Send-Chord @($VK.Next)
        Check '] wraps back, keeps corner' (Expect-Layout 'BottomRight' $startWork)

        # A free-floating window keeps its relative position and size. DPI-unaware windows on a
        # fractionally scaled display can be off by 1px (their size is stored in scaled units).
        [LoopSmoke]::SetWindowPos($h, [IntPtr]::Zero, $startWork[0] + 300, $startWork[1] + 200, 800, 600, 0x14) | Out-Null
        Start-Sleep -Milliseconds 200
        $free = [LoopSmoke]::Frame($h)
        Send-Chord @($VK.Next)
        $sx = $destWork[2] / $startWork[2]; $sy = $destWork[3] / $startWork[3]
        Check 'free window ] (proportional, +-1px)' @(
            ($destWork[0] + (Round-Half (($free[0] - $startWork[0]) * $sx))),
            ($destWork[1] + (Round-Half (($free[1] - $startWork[1]) * $sy))),
            (Round-Half ($free[2] * $sx)), (Round-Half ($free[3] * $sy))) -tolerance 1
        Send-Chord @($VK.Prev)
        Check 'free window [ (round trip, +-1px)' $free -tolerance 1

        [LoopSmoke]::ShowWindow($h, 3) | Out-Null; Start-Sleep -Milliseconds 300
        Send-Chord @($VK.Next)
        Check-True 'maximized window ] stays maximized' ([LoopSmoke]::IsZoomed($h))
        Check-True 'maximized window ] on next display' ((([LoopSmoke]::Work($h)) -join ',') -eq ($destWork -join ',')) "on $(Format-Rect ([LoopSmoke]::Work($h)))"
        Send-Chord @($VK.Enter)
        Check-True 'restore after move: stays on new display' ((-not [LoopSmoke]::IsZoomed($h)) -and
            ((([LoopSmoke]::Work($h)) -join ',') -eq ($destWork -join ','))) "frame $(Format-Rect ([LoopSmoke]::Frame($h)))"
        Send-Chord @($VK.Enter)

        $sorted = $areas | Sort-Object { $_[0] }, { $_[1] }
        for ($n = 1; $n -le $sorted.Count; $n++) {
            Send-Chord @(0x30 + $n)
            $now = [LoopSmoke]::Work($h)
            Check-True "display key $n" (($now -join ',') -eq ($sorted[$n - 1] -join ',')) "on $(Format-Rect $now)"
        }
        Send-Chord @(0x30 + $sorted.Count + 1)
        Check-True "display key $($sorted.Count + 1) (none)" ((([LoopSmoke]::Work($h)) -join ',') -eq ($sorted[-1] -join ',')) 'stayed'
        Send-Chord @(0x30 + 1 + [array]::IndexOf(@($sorted | ForEach-Object { $_ -join ',' }), ($work -join ',')))  # back to start
    }

    Send-Chord @($VK.M)
    Check-True 'M (minimize)' ([LoopSmoke]::IsIconic($h))
    [LoopSmoke]::ShowWindow($h, 9) | Out-Null; Start-Sleep -Milliseconds 300

    # ============================ Animation ======================================================
    Start-Loop @{}
    Write-Host "[default settings: animations on]"
    Send-Chord @($VK.Left)
    Focus-Target; Hold-Trigger; Tap @($VK.Right); Release-Modifiers
    $frames = Sample { [LoopSmoke]::Frame($h) } 450
    $between = @($frames | Where-Object { $_[0] -gt 0 -and $_[0] -lt 960 })
    Check-True 'glide: passes through in-between positions' ($between.Count -ge 2) "$($between.Count) of $($frames.Count) samples in between"
    Check 'glide: lands exactly' (Expect-Layout 'RightHalf' $work)
    $lefts = @($frames | ForEach-Object { $_[0] })
    $monotonic = $true
    for ($i = 1; $i -lt $lefts.Count; $i++) { if ($lefts[$i] -lt $lefts[$i - 1]) { $monotonic = $false } }
    Check-True 'glide: moves one way, no jumps back' $monotonic

    # Preview glides between targets while the trigger is held: Left, then Up with Left still held.
    Focus-Target; Hold-Trigger
    [LoopSmoke]::Key($VK.Left, $false)
    Start-Sleep -Milliseconds 300
    [LoopSmoke]::Key($VK.Up, $false)
    $overlays = Sample { [LoopSmoke]::Preview((Get-Overlay)) } 350
    [LoopSmoke]::Key($VK.Up, $true); [LoopSmoke]::Key($VK.Left, $true)
    $endOverlay = Inset (Expect-Layout 'TopLeft' $work)
    $startOverlay = Inset (Expect-Layout 'LeftHalf' $work)
    $mid = @($overlays | Where-Object { $_[3] -lt $startOverlay[3] -and $_[3] -gt $endOverlay[3] })
    Check-True 'preview glides (Left -> Up+Left)' ($mid.Count -ge 2) "$($mid.Count) in-between heights"
    Check 'preview ends over the corner' $endOverlay ([LoopSmoke]::Preview((Get-Overlay)))
    Release-Trigger
    Check 'after preview glide: window at corner' (Expect-Layout 'TopLeft' $work)

    # A new key press during a glide jumps it to its end, then continues from there.
    Start-Loop @{ ApplyMode = 'Instant' }
    Write-Host "[settings: instant, animations on]"
    Send-Chord @($VK.Right)
    Focus-Target; Hold-Trigger
    [LoopSmoke]::Key($VK.Left, $false); [LoopSmoke]::Key($VK.Left, $true)
    Start-Sleep -Milliseconds 140    # chord grace (60) + part of the glide
    [LoopSmoke]::Key($VK.Down, $false); [LoopSmoke]::Key($VK.Down, $true)
    Release-Trigger
    Check 'interrupted glide: lands on the newer target' (Expect-Layout 'BottomHalf' $work)
    Send-Chord @($VK.Left)
    Check 'after interruption: layouts still exact' (Expect-Layout 'LeftHalf' $work)

    # Direction indicator.
    Start-Loop @{ ShowIndicator = $true }
    Write-Host "[settings: direction indicator on]"
    Focus-Target; Hold-Trigger; Tap @($VK.Up, $VK.Right)
    $ind = [LoopSmoke]::FindWindow([NullString]::Value, 'Loop indicator')
    Check-True 'indicator: visible while holding' (($ind -ne [IntPtr]::Zero) -and [LoopSmoke]::IsWindowVisible($ind))
    Release-Trigger
    Check-True 'indicator: hidden after release' (-not [LoopSmoke]::IsWindowVisible($ind))
    Check 'indicator: window still moved' (Expect-Layout 'TopRight' $work)

    # ============================ Instant mode + gaps (settings file) =============================
    # Window animation off here: checks the off switch, and lets us check positions mid-hold.
    Start-Loop @{ ApplyMode = 'Instant'; Gap = 10; AnimateWindows = $false }
    Write-Host "[settings: instant, gap 10, no window animation]"
    Focus-Target; Hold-Trigger; Tap @($VK.Left)
    Check 'instant: moves while holding, with gap' (With-Gap (Expect-Layout 'LeftHalf' $work) $work 10)
    Check-True 'instant: no preview' (-not (Overlay-Visible))
    Release-Trigger
    Send-Chord @($VK.Down, $VK.Right)
    Check 'instant: corner with gap' (With-Gap (Expect-Layout 'BottomRight' $work) $work 10)

    # ============================ Rebound key + excluded app ======================================
    Start-Loop @{ ActionKeys = @{ Maximize = 'Space' } }
    Write-Host "[settings: Maximize on Space]"
    Send-Chord @($VK.Left)
    Send-Chord @(0x20)
    Check-True 'rebound: Space maximizes' ([LoopSmoke]::IsZoomed($h))
    $before = [LoopSmoke]::Frame($h)
    Send-Chord @($VK.Enter)
    Check 'rebound: Enter no longer bound' $before

    Start-Loop @{ ExcludedApps = @('PowerShell.exe') }
    Write-Host "[settings: PowerShell excluded]"
    $before = [LoopSmoke]::Frame($h)
    Send-Chord @($VK.Left)
    Check 'excluded app is not moved' $before

    # ============================ Mouse radial menu ===============================================
    Start-Loop @{ MouseTrigger = 'MiddleDoubleClick' }
    Write-Host "[settings: mouse menu on middle double-click]"
    $MB = @{ LeftDown = 0x2; LeftUp = 0x4; RightDown = 0x8; RightUp = 0x10; MiddleDown = 0x20; MiddleUp = 0x40; Wheel = 0x800 }
    function Button([int]$flags) { [LoopSmoke]::Mouse($flags, 0, 0, 0); Start-Sleep -Milliseconds 40 }
    function Mouse-To([int]$x, [int]$y) { [LoopSmoke]::MoveTo($x, $y); Start-Sleep -Milliseconds 40 }
    function Get-Indicator { [LoopSmoke]::FindWindow([NullString]::Value, 'Loop indicator') }
    function Indicator-Visible { $i = Get-Indicator; ($i -ne [IntPtr]::Zero) -and [LoopSmoke]::IsWindowVisible($i) }
    # Brings the target to the front, puts the cursor in its middle, and double-clicks the middle
    # button, keeping the second press held. Returns the menu's center.
    function Open-MouseMenu {
        Focus-Target
        $f = [LoopSmoke]::Frame($h); $c = @(($f[0] + [int]($f[2] / 2)), ($f[1] + [int]($f[3] / 2)))
        Mouse-To $c[0] $c[1]
        Button $MB.MiddleDown; Button $MB.MiddleUp; Button $MB.MiddleDown
        Start-Sleep -Milliseconds 150
        , $c
    }
    # Moves from the menu's center in a direction, in two steps (like a hand would).
    function Drag-Toward([int[]]$c, [int]$dx, [int]$dy) {
        Mouse-To ($c[0] + [int]($dx / 2)) ($c[1] + [int]($dy / 2)); Mouse-To ($c[0] + $dx) ($c[1] + $dy)
        Start-Sleep -Milliseconds 150
    }

    $c = Open-MouseMenu
    Check-True 'mouse: double-click opens the menu' (Indicator-Visible)
    Drag-Toward $c -150 0
    Check-True 'mouse: dragging shows the preview' (Overlay-Visible)
    Button $MB.MiddleUp; Start-Sleep -Milliseconds 500
    Check-True 'mouse: menu closes on release' (-not (Indicator-Visible))
    Check 'mouse: drag left, release: left half' (Expect-Layout 'LeftHalf' $work)

    $c = Open-MouseMenu
    Drag-Toward $c 120 -120
    Button $MB.MiddleUp; Start-Sleep -Milliseconds 500
    Check 'mouse: drag up-right: top-right corner' (Expect-Layout 'TopRight' $work)

    $c = Open-MouseMenu
    Button $MB.MiddleUp; Start-Sleep -Milliseconds 150
    Check-True 'mouse: quick release keeps it open' (Indicator-Visible)
    Drag-Toward $c -120 120
    Button $MB.LeftDown; Button $MB.LeftUp; Start-Sleep -Milliseconds 500
    Check-True 'mouse: click closes the menu' (-not (Indicator-Visible))
    Check 'mouse: click mode, down-left: bottom-left' (Expect-Layout 'BottomLeft' $work)

    $before = [LoopSmoke]::Frame($h)
    $c = Open-MouseMenu
    Drag-Toward $c 150 0
    Button $MB.RightDown; Button $MB.RightUp; Button $MB.MiddleUp; Start-Sleep -Milliseconds 500
    Check-True 'mouse: right-click cancels' ((-not (Indicator-Visible)) -and (-not (Overlay-Visible)))
    Check 'mouse: cancelled: window unchanged' $before

    $c = Open-MouseMenu
    Drag-Toward $c 0 150
    [LoopSmoke]::Mouse($MB.Wheel, 0, 0, 120); Start-Sleep -Milliseconds 40  # one notch steps the cycle: 1/2 -> 1/3
    Button $MB.MiddleUp; Start-Sleep -Milliseconds 500
    Check 'mouse: wheel cycles: bottom third' (Expect-Layout 'BottomHalf' $work (1.0 / 3))

    $c = Open-MouseMenu
    Button $MB.MiddleUp; Start-Sleep -Milliseconds 100
    Button $MB.LeftDown; Button $MB.LeftUp; Start-Sleep -Milliseconds 700
    Check-True 'mouse: clicking the center maximizes' ([LoopSmoke]::IsZoomed($h))
    $c = Open-MouseMenu
    Button $MB.MiddleUp; Start-Sleep -Milliseconds 100
    Button $MB.LeftDown; Button $MB.LeftUp; Start-Sleep -Milliseconds 700
    Check-True 'mouse: clicking it again restores' (-not [LoopSmoke]::IsZoomed($h))

    Focus-Target
    $before = [LoopSmoke]::Frame($h)
    Button $MB.MiddleDown; Button $MB.MiddleUp; Start-Sleep -Milliseconds 600
    Check-True 'mouse: a single middle click opens nothing' (-not (Indicator-Visible))
    Check 'mouse: single click: window unchanged' $before
}
finally {
    Stop-Loop
    Stop-Process -Id $target.Id -ErrorAction SilentlyContinue
    Remove-Item $settingsFile -ErrorAction SilentlyContinue
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All smoke checks passed.' -ForegroundColor Green
