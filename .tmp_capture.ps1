param([int]$ProcId, [string]$OutPath)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Cap {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
"@
$p = Get-Process -Id $ProcId -ErrorAction Stop
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Output "NO_WINDOW"; exit 1 }
[Win32Cap]::ShowWindow($h, 9) | Out-Null
[Win32Cap]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 700
$c = New-Object Win32Cap+RECT
[Win32Cap]::GetClientRect($h, [ref]$c) | Out-Null
$pt = New-Object Win32Cap+POINT
[Win32Cap]::ClientToScreen($h, [ref]$pt) | Out-Null
$w = $c.Right - $c.Left
$hgt = $c.Bottom - $c.Top
Write-Output ("client {0}x{1} at {2},{3}" -f $w, $hgt, $pt.X, $pt.Y)
if ($w -le 0 -or $hgt -le 0) { Write-Output "BAD_RECT"; exit 1 }
$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($pt.X, $pt.Y, 0, 0, $bmp.Size)
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "SAVED $OutPath"
