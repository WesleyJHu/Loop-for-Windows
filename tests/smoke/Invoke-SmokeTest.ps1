<#
.SYNOPSIS
  End-to-end smoke test: launches Loop and a throwaway WinForms window, injects
  Ctrl+Win+Alt chords with SendInput, and checks where the window's visible frame lands.

.NOTES
  - Steals keyboard focus for ~15 seconds; don't type while it runs.
  - Other window managers (AquaSnap, FancyZones, DisplayFusion...) that react to window moves
    will make geometry checks fail. Pause them first.
  - Run from Windows PowerShell 5.1 or PowerShell 7:  .\tests\smoke\Invoke-SmokeTest.ps1
#>
param(
    [string]$AppExe = (Join-Path $PSScriptRoot '..\..\src\Loop.App\bin\Debug\net8.0-windows\LoopForWindows.exe')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $AppExe)) { throw "Build first: $AppExe not found (dotnet build)" }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LoopSmoke {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cb; public RECT mon; public RECT work; public uint flags; }
  [StructLayout(LayoutKind.Sequential)] public struct KI { public ushort vk; public ushort scan; public uint flags; public uint time; public UIntPtr extra; }
  [StructLayout(LayoutKind.Explicit, Size=40)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KI ki; }
  [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int cb);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
  [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int cb);
  public static void Key(int vk, bool up) {
    var i = new INPUT[1]; i[0].type = 1; i[0].ki.vk = (ushort)vk; i[0].ki.flags = up ? 2u : 0u;
    SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
  }
  // Returns [left, top, width, height].
  public static int[] Frame(IntPtr h) { RECT r; DwmGetWindowAttribute(h, 9, out r, 16); return new[] { r.L, r.T, r.R - r.L, r.B - r.T }; }
  public static int[] Work(IntPtr h) {
    var mi = new MONITORINFO(); mi.cb = Marshal.SizeOf(typeof(MONITORINFO));
    GetMonitorInfo(MonitorFromWindow(h, 2), ref mi);
    return new[] { mi.work.L, mi.work.T, mi.work.R - mi.work.L, mi.work.B - mi.work.T };
  }
}
'@
[LoopSmoke]::SetProcessDpiAwarenessContext([IntPtr]-4) | Out-Null  # per-monitor v2: physical pixels

$VK = @{ Left = 0x25; Up = 0x26; Right = 0x27; Down = 0x28; Enter = 0x0D; C = 0x43; M = 0x4D }
$title = "LoopSmokeTarget-$PID"
$formScript = "Add-Type -AssemblyName System.Windows.Forms; `$f = New-Object Windows.Forms.Form; `$f.Text = '$title'; " +
              "`$f.StartPosition = 'Manual'; `$f.Left = 300; `$f.Top = 200; `$f.Width = 800; `$f.Height = 600; [Windows.Forms.Application]::Run(`$f)"
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($formScript))

$target = Start-Process powershell -ArgumentList @('-NoProfile', '-WindowStyle', 'Hidden', '-EncodedCommand', $encoded) -PassThru
$app = Start-Process $AppExe -PassThru
$failures = 0

function Format-Rect([int[]]$r) { "({0},{1} {2}x{3})" -f $r[0], $r[1], $r[2], $r[3] }

try {
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 50 -and $h -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 200
        $h = [LoopSmoke]::FindWindow([NullString]::Value, $title)
    }
    if ($h -eq [IntPtr]::Zero) { throw "Target window never appeared." }
    Start-Sleep -Milliseconds 500

    function Focus-Target {
        # Synthesized input makes this process the last input owner, lifting the foreground lock.
        [LoopSmoke]::Key(0xE8, $false); [LoopSmoke]::Key(0xE8, $true)
        [LoopSmoke]::SetForegroundWindow($h) | Out-Null
        Start-Sleep -Milliseconds 200
        if ([LoopSmoke]::GetForegroundWindow() -ne $h) { throw "Could not focus the target window." }
    }

    function Send-Chord([int[]]$keys) {
        Focus-Target
        foreach ($m in 0xA2, 0x5B, 0xA4) { [LoopSmoke]::Key($m, $false) }   # LCtrl, LWin, LAlt
        foreach ($k in $keys) { [LoopSmoke]::Key($k, $false); Start-Sleep -Milliseconds 30 }
        foreach ($k in $keys) { [LoopSmoke]::Key($k, $true) }
        foreach ($m in 0xA4, 0x5B, 0xA2) { [LoopSmoke]::Key($m, $true) }
        Start-Sleep -Milliseconds 400
    }

    function Check([string]$name, [int[]]$expected) {
        $actual = [LoopSmoke]::Frame($h)
        if (($actual -join ',') -eq ($expected -join ',')) {
            Write-Host ("PASS  {0,-22} {1}" -f $name, (Format-Rect $actual)) -ForegroundColor Green
        } else {
            Write-Host ("FAIL  {0,-22} expected {1}, got {2}" -f $name, (Format-Rect $expected), (Format-Rect $actual)) -ForegroundColor Red
            $script:failures++
        }
    }

    $work = [LoopSmoke]::Work($h)
    $ax, $ay, $aw, $ah = $work   # names differ from $h: PowerShell variables are case-insensitive
    $hw = [int][Math]::Round($aw / 2, [MidpointRounding]::AwayFromZero)  # leading half (matches LayoutEngine)
    $hh = [int][Math]::Round($ah / 2, [MidpointRounding]::AwayFromZero)
    $rw = $aw - [int][Math]::Round($aw / 2, [MidpointRounding]::AwayFromZero)  # trailing half
    $rh = $ah - [int][Math]::Round($ah / 2, [MidpointRounding]::AwayFromZero)
    Write-Host "Work area $(Format-Rect $work)"

    $cases = @(
        @{ Name = 'Left';        Keys = @($VK.Left);            Expect = @($ax, $ay, $hw, $ah) },
        @{ Name = 'Right';       Keys = @($VK.Right);           Expect = @(($ax + $aw - $rw), $ay, $rw, $ah) },
        @{ Name = 'Up';          Keys = @($VK.Up);              Expect = @($ax, $ay, $aw, $hh) },
        @{ Name = 'Down';        Keys = @($VK.Down);            Expect = @($ax, ($ay + $ah - $rh), $aw, $rh) },
        @{ Name = 'Up+Left';     Keys = @($VK.Up, $VK.Left);    Expect = @($ax, $ay, $hw, $hh) },
        @{ Name = 'Up+Right';    Keys = @($VK.Up, $VK.Right);   Expect = @(($ax + $aw - $rw), $ay, $rw, $hh) },
        @{ Name = 'Down+Left';   Keys = @($VK.Down, $VK.Left);  Expect = @($ax, ($ay + $ah - $rh), $hw, $rh) },
        @{ Name = 'Down+Right';  Keys = @($VK.Down, $VK.Right); Expect = @(($ax + $aw - $rw), ($ay + $ah - $rh), $rw, $rh) },
        @{ Name = 'Enter (maximize)'; Keys = @($VK.Enter);      Expect = @($ax, $ay, $aw, $ah) }
    )
    foreach ($c in $cases) { Send-Chord $c.Keys; Check $c.Name $c.Expect }

    # Center keeps size: from the top-left quarter, centering keeps the quarter's size.
    Send-Chord @($VK.Up, $VK.Left)
    $q = [LoopSmoke]::Frame($h)
    Send-Chord @($VK.C)
    Check 'C (center)' @(($ax + [int][Math]::Floor(($aw - $q[2]) / 2)), ($ay + [int][Math]::Floor(($ah - $q[3]) / 2)), $q[2], $q[3])

    # Full width / full height keep the other axis.
    $before = [LoopSmoke]::Frame($h)
    Send-Chord @($VK.Left, $VK.Right)
    Check 'Left+Right (width)' @($ax, $before[1], $aw, $before[3])
    Send-Chord @($VK.C)
    $before = [LoopSmoke]::Frame($h)
    Send-Chord @($VK.Up, $VK.Down)
    Check 'Up+Down (height)' @($before[0], $ay, $before[2], $ah)

    # Without the trigger, arrows reach the app and Loop does nothing.
    Focus-Target
    $before = [LoopSmoke]::Frame($h)
    [LoopSmoke]::Key($VK.Left, $false); [LoopSmoke]::Key($VK.Left, $true)
    Start-Sleep -Milliseconds 300
    Check 'bare Left (ignored)' $before

    Send-Chord @($VK.M)
    if ([LoopSmoke]::IsIconic($h)) { Write-Host 'PASS  M (minimize)' -ForegroundColor Green }
    else { Write-Host 'FAIL  M (minimize): window is not minimized' -ForegroundColor Red; $failures++ }
}
finally {
    Stop-Process -Id $app.Id -ErrorAction SilentlyContinue
    Stop-Process -Id $target.Id -ErrorAction SilentlyContinue
}

if ($failures -gt 0) { Write-Host "$failures check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All smoke checks passed.' -ForegroundColor Green
