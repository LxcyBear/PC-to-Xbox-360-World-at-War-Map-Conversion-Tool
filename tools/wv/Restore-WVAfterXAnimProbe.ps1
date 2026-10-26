param(
    [string]$WVRoot = "C:\Users\jorda\OneDrive\Desktop\My Xbox\X360DebuggerWV-master\X360DebuggerWV"
)

$ErrorActionPreference = "Stop"

$debugger = Join-Path $WVRoot "Debugger.cs"
$probeBackup = $debugger + ".before-null-xanim-precall-probe.bak"
$repoScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$firstExceptionPatch = Join-Path $repoScriptRoot "Patch-WVFirstExceptionCapture.ps1"

if (-not (Test-Path -LiteralPath $debugger -PathType Leaf)) {
    throw "Debugger.cs not found: $debugger"
}
if (-not (Test-Path -LiteralPath $probeBackup -PathType Leaf)) {
    throw "XAnim pre-call backup not found: $probeBackup"
}
if (-not (Test-Path -LiteralPath $firstExceptionPatch -PathType Leaf)) {
    throw "First-exception patch script not found: $firstExceptionPatch"
}

# Preserve the current probe-instrumented source before restoring the exact file
# that existed immediately before Patch-WVNullXAnimPreCallProbe.ps1 was installed.
$instrumentedBackup = $debugger + ".with-null-xanim-precall-probe.bak"
Copy-Item -LiteralPath $debugger -Destination $instrumentedBackup -Force

Copy-Item -LiteralPath $probeBackup -Destination $debugger -Force
Write-Host "[WV:CLEAN] Restored Debugger.cs from the pre-XAnim-probe backup."

# Re-apply/verify the first-exception capture explicitly. This keeps the next
# hardware run useful even if the historical probe backup predates that patch.
& powershell -ExecutionPolicy Bypass -File $firstExceptionPatch -WVRoot $WVRoot
if ($LASTEXITCODE -ne 0) {
    throw "First-exception capture verification/repatch failed with exit code $LASTEXITCODE."
}

$restored = Get-Content -LiteralPath $debugger -Raw
if ($restored.Contains("NullXAnimPre") -or
    $restored.Contains("HandleNullXAnimBreak") -or
    $restored.Contains("ArmNullXAnimProbe")) {
    throw "XAnim probe markers are still present after restore; refusing the hardware test."
}
if (-not $restored.Contains("CaptureFirstExceptionContext")) {
    throw "First-exception capture marker is missing after cleanup."
}

Write-Host ""
Write-Host "[WV:CLEAN] PASS"
Write-Host "     XAnim pre-call breakpoint probe: REMOVED"
Write-Host "     First-exception capture: PRESENT"
Write-Host "     Fastfile: DO NOT REBUILD; reuse the current 4363a79 native-footprint FF."
Write-Host "     Rebuild X360DebuggerWV, launch the same map, and send the first real exception context."
