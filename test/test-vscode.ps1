$ErrorActionPreference = 'Stop'
$exe = "C:\Users\Daker\Murmur\bin\Release\net8.0-windows\win-x64\Murmur.exe"
$text = 'Electron injection test from Murmur. Unicode: Héllo — ünïcödé ✓'
$testFile = "C:\Users\Daker\Murmur\test\wk-electron-test.txt"
$titleGuard = "wk-electron-test"

# Dedicated empty file -> new VS Code window whose title contains our guard string
Set-Content -Path $testFile -Value $null -NoNewline
$savedClip = $null
try { $savedClip = Get-Clipboard -Raw } catch {}

& "$env:LOCALAPPDATA\Programs\Microsoft VS Code\bin\code.cmd" -n $testFile
Start-Sleep -Seconds 8   # Electron window + workbench load

$pf = Start-Process $exe -ArgumentList "--focus", $titleGuard -Wait -WindowStyle Hidden -PassThru
if ($pf.ExitCode -ne 0) { Write-Output "ABORTED: could not focus test window (exit $($pf.ExitCode))"; exit 3 }

$p1 = Start-Process $exe -ArgumentList "--inject", "`"$text`"", "--delay", "300", "--require-title", $titleGuard -Wait -WindowStyle Hidden -PassThru
if ($p1.ExitCode -eq 3) { Write-Output 'ABORTED: focus gate tripped during inject'; exit 3 }
Start-Sleep -Milliseconds 600

$p2 = Start-Process $exe -ArgumentList "--select-copy", "--delay", "300", "--require-title", $titleGuard -Wait -WindowStyle Hidden -PassThru
if ($p2.ExitCode -eq 3) { Write-Output 'ABORTED: focus gate tripped during readback'; exit 3 }
Start-Sleep -Milliseconds 600

$got = Get-Clipboard -Raw

# Clean up: save (so no unsaved-changes prompt), then close just this window
Start-Process $exe -ArgumentList "--combo", "ctrl+s", "--delay", "200", "--require-title", $titleGuard -Wait -WindowStyle Hidden
Start-Sleep -Milliseconds 800
Start-Process $exe -ArgumentList "--combo", "ctrl+shift+w", "--delay", "200", "--require-title", $titleGuard -Wait -WindowStyle Hidden

try { if ($null -ne $savedClip) { Set-Clipboard -Value $savedClip } else { Set-Clipboard -Value '' } } catch {}

Write-Output "EXPECTED: $text"
Write-Output "GOT     : $got"
if ($got -eq $text) { Write-Output "RESULT  : MATCH" } else { Write-Output "RESULT  : MISMATCH" }
