Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class WinEnum {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
}
"@
[WinEnum]::EnumWindows({ param($h, $l)
    if ([WinEnum]::IsWindowVisible($h)) {
        $t = New-Object System.Text.StringBuilder 256
        [void][WinEnum]::GetWindowText($h, $t, 256)
        if ($t.ToString() -like '*Notepad*') {
            $c = New-Object System.Text.StringBuilder 256
            [void][WinEnum]::GetClassName($h, $c, 256)
            [Console]::WriteLine(("title=[{0}] class=[{1}]" -f $t, $c))
        }
    }
    $true }, [IntPtr]::Zero) | Out-Null
[Console]::WriteLine("done")
