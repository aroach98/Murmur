$ErrorActionPreference = 'Stop'
$exe = "C:\Users\Daker\Murmur\bin\Release\net8.0-windows\win-x64\Murmur.exe"
$text = 'Hello from Murmur - the quick brown fox! Unicode check: Héllo — ünïcödé ✓'

# Only run while the desktop is idle so we don't fight a human for focus.
$idle = & "$PSScriptRoot\wait-idle.ps1" -IdleSeconds 8 -TimeoutSeconds 120
if ($idle -ne 'IDLE') { Write-Output 'SKIPPED: desktop stayed busy'; exit 3 }

$savedClip = $null
try { $savedClip = Get-Clipboard -Raw } catch {}

$np = Start-Process notepad -PassThru
Start-Sleep -Seconds 2

# Force notepad to the foreground right before injecting
Add-Type -AssemblyName Microsoft.VisualBasic
try { [Microsoft.VisualBasic.Interaction]::AppActivate($np.Id) } catch {}
Start-Sleep -Milliseconds 300

Start-Process $exe -ArgumentList "--inject", "`"$text`"", "--delay", "300" -Wait -WindowStyle Hidden
Start-Sleep -Milliseconds 400

try { [Microsoft.VisualBasic.Interaction]::AppActivate($np.Id) } catch {}
Start-Process $exe -ArgumentList "--select-copy", "--delay", "300" -Wait -WindowStyle Hidden
Start-Sleep -Milliseconds 500

$got = Get-Clipboard -Raw

try { Stop-Process -Id $np.Id -Force -Confirm:$false } catch { Get-Process Notepad -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false }
try { if ($null -ne $savedClip) { Set-Clipboard -Value $savedClip } else { Set-Clipboard -Value '' } } catch {}

Write-Output "EXPECTED: $text"
Write-Output "GOT     : $got"
if ($got -eq $text) { Write-Output "RESULT  : MATCH" } else { Write-Output "RESULT  : MISMATCH" }
