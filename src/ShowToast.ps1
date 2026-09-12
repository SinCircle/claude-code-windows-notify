param([Parameter(Mandatory=$true)][string]$Request)
$ErrorActionPreference = 'Stop'
try {
    $data = Get-Content -LiteralPath $Request -Raw -Encoding UTF8 | ConvertFrom-Json
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'ClaudeNotify.exe'))
    if ([Native]::GetForegroundWindow().ToInt64() -eq $data.window) { exit 3 }
    $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
    $null = [Windows.UI.Notifications.NotificationSetting, Windows.UI.Notifications, ContentType = WindowsRuntime]
    $null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType = WindowsRuntime]
    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
    $title = [Security.SecurityElement]::Escape($data.title)
    $body = [Security.SecurityElement]::Escape($data.body)
    $logo = [Security.SecurityElement]::Escape(([Uri](Join-Path $PSScriptRoot 'claude-large.png')).AbsoluteUri)
    $xml.LoadXml("<toast launch='$($data.token)' duration='short'><visual><binding template='ToastGeneric'><image placement='appLogoOverride' src='$logo' alternateText='Claude'/><text hint-maxLines='1'>$title</text><text hint-wrap='true' hint-maxLines='4'>$body</text></binding></visual><audio src='ms-winsoundevent:Notification.Default'/></toast>")
    $toast = New-Object Windows.UI.Notifications.ToastNotification($xml)
    $toast.Tag = $data.tag
    $toast.Group = 'claude-code'
    $toast.ExpirationTime = [DateTimeOffset]::Now.AddHours(4)
    $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Local.ClaudeCode')
    # The first Show registers the sender with the notification platform.
    # Querying Setting before its first notification can throw 0x80070490.
    $notifier.Show($toast)
    $setting = $notifier.get_Setting()
    if ($setting.ToString() -ne 'Enabled') { throw "Windows notification setting: $setting" }
    exit 0
} catch {
    Add-Content -LiteralPath (Join-Path $PSScriptRoot 'toast-errors.log') -Encoding UTF8 -Value ((Get-Date -Format o) + ' ' + $_.Exception.Message + ' ' + $_.InvocationInfo.PositionMessage)
    exit 1
} finally {
    if (Test-Path -LiteralPath $Request) { Remove-Item -LiteralPath $Request -Force }
}
