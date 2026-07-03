# Waits for the desktop to go idle, then runs both injection tests back to back.
# Every keystroke sent is focus-gated, so a returning user aborts the test rather
# than receiving stray input.
$results = "C:\Users\Daker\Murmur\test\results.txt"
"STARTED $(Get-Date -Format o)" | Set-Content $results

$idle = & "$PSScriptRoot\wait-idle.ps1" -IdleSeconds 45 -TimeoutSeconds 420
if ($idle -ne 'IDLE') {
    "NEVER_IDLE: desktop stayed in use; tests not run" | Add-Content $results
    exit 3
}

"--- NOTEPAD TEST ---" | Add-Content $results
& "$PSScriptRoot\test-notepad2.ps1" 2>&1 | Add-Content $results

# Re-check idle between tests; bail if the user came back
$idle2 = & "$PSScriptRoot\wait-idle.ps1" -IdleSeconds 10 -TimeoutSeconds 60
if ($idle2 -ne 'IDLE') {
    "SKIPPED VSCODE: user became active" | Add-Content $results
    exit 3
}

"--- VSCODE TEST ---" | Add-Content $results
& "$PSScriptRoot\test-vscode.ps1" 2>&1 | Add-Content $results
"DONE $(Get-Date -Format o)" | Add-Content $results
