Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class FG {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    public static string Title() { var sb = new StringBuilder(512); GetWindowText(GetForegroundWindow(), sb, 512); return sb.ToString(); }
}
"@
Write-Output ("Before: [" + [FG]::Title() + "]")
Start-Process notepad -ArgumentList "C:\Users\Daker\Murmur\test\wk-inject-test.txt"
Start-Sleep -Seconds 2
Write-Output ("After launch: [" + [FG]::Title() + "]")
Add-Type -AssemblyName Microsoft.VisualBasic
try { [Microsoft.VisualBasic.Interaction]::AppActivate("wk-inject-test") } catch { Write-Output ("AppActivate failed: " + $_.Exception.Message) }
Start-Sleep -Milliseconds 500
Write-Output ("After activate: [" + [FG]::Title() + "]")
Get-Process Notepad -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ("Notepad window: [" + $_.MainWindowTitle + "]") }
Get-Process Notepad -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*wk-inject-test*" } | Stop-Process -Force -Confirm:$false
