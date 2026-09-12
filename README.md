# Claude Code Windows Notify

为 Windows Terminal 中的 Claude Code CLI 提供原生 Windows 通知。

## 功能

- 回复完成、需要权限确认、需要回答问题时提醒。
- 原终端窗口处于前台时不提醒；已忽略的事件不延迟补发。
- 同一轮完成或同一次等待只提醒一次。
- 完成时以会话标题为标题；需要操作时加 `需要操作-` 前缀。
- 正文显示回复或问题摘要，最多 360 字符，请求 Windows 最多显示四行。
- 点击通知还原并聚焦原 Windows Terminal 窗口，不切换标签页或分屏。
- 256 像素黑白 Claude 大图标，标题栏图标使用透明资源，无回复框。

## 安装

适用于 x64 Windows、Windows Terminal、Claude Code CLI；需要系统自带的 Windows PowerShell 5.1 和 .NET Framework C# 编译器。

在仓库目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

脚本编译源码，将程序安装到 `%USERPROFILE%\.claude\notifications`，注册当前用户的通知身份和点击接收器，并将 hooks 合并到 `%USERPROFILE%\.claude\settings.json`。现有设置会先备份，其他 hooks 会保留。重复安装不会重复添加相同命令。不需要管理员权限。安装后重启 Claude Code 会话。

仅编译可运行 `build.ps1`。安装和编译不会发送测试通知。本次仓库整理未运行功能测试。

## 实现

`src/Notifier.cs` 处理 hook 输入、去重、会话标题、窗口识别和 COM 点击回调。`src/ShowToast.ps1` 通过 WinRT 发送原生通知。通知发出前启动一个共享点击接收进程，空闲四小时后退出；启动时不继承 hook 管道，避免 Claude 等待管道关闭。

会话标题优先使用 transcript 中的自定义标题，其次自动标题、首条用户输入、目录名。回复正文来自 Stop 事件的 `last_assistant_message`。显示效果受 Windows 通知设置、勿扰模式和图标缓存影响。

通知身份为 `Local.ClaudeCode`；COM CLSID 为 `{B0BCB73E-A94E-4817-BEAE-038195F6C9CD}`。安装会创建开始菜单 `Claude Code Notifications/Claude Code.lnk`。

## 本地数据与排错

安装目录的 `events.jsonl` 和 `toast-errors.log` 记录事件与错误；`state/` 保存会话状态和点击目标。状态可能包含会话标题或首条输入，Windows 通知中心也会保留正文。它们都是本地运行数据，不应提交到 Git。

点击问题可查看 `toast_clicked`、`activation`；发送问题可查看 `toast_failed`、`toast_timeout`、`com_start_timeout`。旧通知可能仍显示缓存图标，请以新通知为准。

## 停用

从 `%USERPROFILE%\.claude\settings.json` 的 hooks 中移除调用 `notifications/ClaudeNotify.exe` 的条目，保留其他 hook，然后重启 Claude Code。共享点击接收进程会在空闲后退出。

## 图标

`assets/claude-large.png` 是从本机 Claude 图标文件中提取的 256 像素黑白图标；`claude.png` 与 `claude.ico` 是透明的标题栏资源。Claude 名称及图标归各自权利人所有，本项目不是官方产品。
