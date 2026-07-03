$ErrorActionPreference = 'Stop'
$exe = "C:\Users\Daker\Murmur\bin\Release\net8.0-windows\win-x64\Murmur.exe"
$text = 'Hello from Murmur - the quick brown fox! Unicode check: Héllo — ünïcödé ✓'
$testFile = "C:\Users\Daker\Murmur\test\wk-inject-test.txt"
$titleGuard = "wk-inject-test"

$idle = & "$PSScriptRoot\wait-idle.ps1" -IdleSeconds 10 -TimeoutSeconds 300
if ($idle -ne 'IDLE') { Write-Output 'SKIPPED: desktop stayed busy'; exit 3 }

# Open Notepad on a dedicated empty file so the focused tab is OURS,
# not a restored-session tab with the user's content.
Set-Content -Path $testFile -Value $null -NoNewline
$savedClip = $null
try { $savedClip = Get-Clipboard -Raw } catch {}

Start-Process notepad -ArgumentList $testFile
Start-Sleep -Seconds 2

$pf = Start-Process $exe -ArgumentList "--focus", $titleGuard -Wait -WindowStyle Hidden -PassThru
if ($pf.ExitCode -ne 0) { Write-Output "ABORTED: could not focus test window (exit $($pf.ExitCode))"; exit 3 }

$p1 = Start-Process $exe -ArgumentList "--inject", "`"$text`"", "--delay", "300", "--require-title", $titleGuard -Wait -WindowStyle Hidden -PassThru
if ($p1.ExitCode -eq 3) { Write-Output 'ABORTED: focus gate tripped during inject'; exit 3 }
Start-Sleep -Milliseconds 400

Start-Process $exe -ArgumentList "--focus", $titleGuard -Wait -WindowStyle Hidden | Out-Null
$p2 = Start-Process $exe -ArgumentList "--select-copy", "--delay", "300", "--require-title", $titleGuard -Wait -WindowStyle Hidden -PassThru
if ($p2.ExitCode -eq 3) { Write-Output 'ABORTED: focus gate tripped during readback'; exit 3 }
Start-Sleep -Milliseconds 500

$got = Get-Clipboard -Raw

# Close only OUR notepad window (title match), never the user's
Get-Process Notepad -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowTitle -like "*$titleGuard*" } |
    Stop-Process -Force -Confirm:$false

try { if ($null -ne $savedClip) { Set-Clipboard -Value $savedClip } else { Set-Clipboard -Value '' } } catch {}

Write-Output "EXPECTED: $text"
Write-Output "GOT     : $got"
if ($got -eq $text) { Write-Output "RESULT  : MATCH" } else { Write-Output "RESULT  : MISMATCH" }
