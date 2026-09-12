$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$claudeDirectory = Join-Path $env:USERPROFILE '.claude'
$destination = Join-Path $claudeDirectory 'notifications'
$settingsPath = Join-Path $claudeDirectory 'settings.json'
$settings = if (Test-Path -LiteralPath $settingsPath) {
    Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else { [pscustomobject]@{} }
if (-not $settings) { throw 'Settings must be a JSON object.' }
if (-not $settings.PSObject.Properties['hooks']) {
    $settings | Add-Member -NotePropertyName hooks -NotePropertyValue ([pscustomobject]@{})
}
$exe = Join-Path $destination 'ClaudeNotify.exe'
$command = '"' + $exe.Replace('\','/') + '"'
$events = [ordered]@{
    SessionStart = ''; UserPromptSubmit = ''; Stop = ''; PermissionRequest = ''
    PreToolUse = 'AskUserQuestion'
    Notification = 'permission_prompt|elicitation_dialog|elicitation_url_dialog|agent_needs_input'
    PostToolUse = ''; PostToolUseFailure = ''
}
foreach ($eventName in $events.Keys) {
    $existing = @($settings.hooks.$eventName | Where-Object { $null -ne $_ })
    $alreadyInstalled = @($existing | ForEach-Object { $_.hooks } | Where-Object {
        $_.type -eq 'command' -and $_.command -eq $command
    }).Count -gt 0
    if (-not $alreadyInstalled) {
        $entry = [ordered]@{ hooks = @(@{type='command'; command=$command; timeout=15}) }
        if ($events[$eventName]) { $entry.matcher = $events[$eventName] }
        $settings.hooks | Add-Member -NotePropertyName $eventName -NotePropertyValue @($existing + [pscustomobject]$entry) -Force
    }
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
if (Test-Path -LiteralPath $settingsPath) {
    Copy-Item -LiteralPath $settingsPath -Destination ($settingsPath + '.notify-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
# Replace only this helper's receiver, never Claude or another application's process.
Get-CimInstance Win32_Process -Filter "Name = 'ClaudeNotify.exe'" | Where-Object {
    $_.ExecutablePath -eq $exe -and $_.CommandLine -match '\s--com-server(?:\s|$)'
} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'build') -File | Copy-Item -Destination $destination -Force
$registration = Start-Process -FilePath $exe -ArgumentList '--install' -WindowStyle Hidden -Wait -PassThru
if ($registration.ExitCode -ne 0) { throw 'Notification registration failed.' }
[IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
Write-Output 'Installed. Restart Claude Code sessions to load hooks. No test notification was sent.'
