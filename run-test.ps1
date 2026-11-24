# Runs harness.exe against one WindowTabs build.
#   -Zip     the build to test (a zip with one folder holding WindowTabs.exe)
#   -Label   name used for results-LABEL.txt and the screenshots
#   -Harness path of the built harness.exe
#   -Out     folder for results, logs and screenshots
param([string]$Zip, [string]$Label, [string]$Harness, [string]$Out)
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$dir = Join-Path $env:RUNNER_TEMP $Label
Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
Expand-Archive -Path $Zip -DestinationPath $dir
$exe = Get-ChildItem -Path $dir -Recurse -Filter WindowTabs.exe | Select-Object -First 1
$wtDir = $exe.DirectoryName
# The same settings as on the user's machine, plus: put every harness.exe window into one group.
$settings = [ordered]@{
  AutoGroupingPaths = @($Harness)
  ReserveTopBand = $true
  SnapTabHeightMargin = $true
  SnapTabMarginMode = 'all'
  HideInactiveTabs = $true
  EnableTabbingByDefault = $true
  TabEdges = @('Top')
}
$settings | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $wtDir 'WindowTabsSettings.json')
Remove-Item -Recurse -Force (Join-Path $env:APPDATA 'WindowTabs') -ErrorAction SilentlyContinue
Get-Process WindowTabs -ErrorAction SilentlyContinue | Stop-Process -Force
$wt = Start-Process -FilePath $exe.FullName -WorkingDirectory $wtDir -PassThru
Start-Sleep -Seconds 20
"WindowTabs running: $(-not $wt.HasExited)" | Tee-Object -FilePath (Join-Path $Out "results-$Label-start.txt")
$p = Start-Process -FilePath $Harness -ArgumentList @('run', $Out, $Label) -PassThru -Wait
Get-Process WindowTabs -ErrorAction SilentlyContinue | Stop-Process -Force
foreach ($log in @((Join-Path $wtDir 'taskbar_trap.log'), (Join-Path $env:APPDATA 'WindowTabs\taskbar_trap.log'))) {
  if (Test-Path $log) { Copy-Item $log (Join-Path $Out "taskbar_trap-$Label.log") }
}
Get-ChildItem (Join-Path $env:APPDATA 'WindowTabs') -Filter 'crash*' -ErrorAction SilentlyContinue | ForEach-Object { Copy-Item $_.FullName (Join-Path $Out "$Label-$($_.Name)") }
Get-Content (Join-Path $Out "results-$Label.txt") -ErrorAction SilentlyContinue
