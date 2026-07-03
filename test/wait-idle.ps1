param(
    [int]$IdleSeconds = 8,
    [int]$TimeoutSeconds = 120
)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class IdleCheck {
    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    public static uint IdleMs() {
        var lii = new LASTINPUTINFO { cbSize = 8 };
        GetLastInputInfo(ref lii);
        return (uint)Environment.TickCount - lii.dwTime;
    }
}
"@
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if ([IdleCheck]::IdleMs() -ge ($IdleSeconds * 1000)) { Write-Output "IDLE"; exit 0 }
    Start-Sleep -Milliseconds 1000
}
Write-Output "BUSY"
exit 1
