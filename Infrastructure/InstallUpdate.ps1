$ErrorActionPreference = 'Stop'

# SECTION 更新计划与路径检查

$workspace = [IO.Path]::GetFullPath($PSScriptRoot)
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
if (-not $workspace.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($workspace) -notmatch '^ZCue-update-[a-f0-9]{32}$') {
    throw '更新工作目录无效。'
}
$plan = Get-Content -LiteralPath (Join-Path $workspace 'plan.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$installRoot = [IO.Path]::GetFullPath($plan.InstallDirectory).TrimEnd('\') + '\'
$stageRoot = Join-Path $workspace 'stage'
$backupRoot = Join-Path $workspace 'backup'
$changed = [Collections.Generic.List[object]]::new()
$parentExited = $false
$installed = $false

function Get-InstallPath([string] $relative) {
    $path = [IO.Path]::GetFullPath((Join-Path $installRoot $relative))
    if (-not $path.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw '更新目标路径越界。'
    }
    # 不沿目录联接或符号链接写入其他位置。
    $ancestor = $path
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw '程序目录含链接，无法安全更新。'
            }
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $path
}

function Copy-WithRetry([string] $source, [string] $destination) {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            [IO.File]::Copy($source, $destination, $true)
            return
        } catch [IO.IOException] {
            if ($attempt -eq 19) { throw }
            Start-Sleep -Milliseconds 250
        }
    }
}

# !SECTION 更新计划与路径检查

# SECTION 等待退出与替换回滚

try {
    foreach ($relative in $plan.Files) {
        $null = Get-InstallPath $relative
    }
    $parent = Get-Process -Id $plan.ProcessId -ErrorAction SilentlyContinue
    if ($parent -and $parent.StartTime.ToUniversalTime().Ticks.ToString() -ne $plan.ProcessStartTicks) {
        throw '运行中的程序已变化，已停止更新。'
    }
    [IO.File]::WriteAllText((Join-Path $workspace 'ready'), '')
    if ($parent -and -not $parent.WaitForExit(60000)) {
        throw 'ZCue 未能退出，当前版本未被替换。'
    }
    $parentExited = $true

    # 所有旧文件备份成功后才开始替换；只处理清单中的程序文件。
    foreach ($relative in $plan.Files) {
        $destination = Get-InstallPath $relative
        if (Test-Path -LiteralPath $destination) {
            $backup = Join-Path $backupRoot $relative
            $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup))
            Copy-WithRetry $destination $backup
        }
    }
    foreach ($relative in $plan.Files) {
        $destination = Get-InstallPath $relative
        $changed.Add([pscustomobject]@{ Path = $destination; Backup = (Join-Path $backupRoot $relative) })
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        Copy-WithRetry (Join-Path $stageRoot $relative) $destination
    }
    Start-Process -FilePath (Join-Path $installRoot 'ZCue.exe') -WorkingDirectory $installRoot -WindowStyle Hidden
    $installed = $true
} catch {
    $failure = $_.Exception.Message
    $rollbackErrors = [Collections.Generic.List[string]]::new()
    for ($index = $changed.Count - 1; $index -ge 0; $index--) {
        $file = $changed[$index]
        try {
            if (Test-Path -LiteralPath $file.Backup) {
                Copy-WithRetry $file.Backup $file.Path
            } elseif (Test-Path -LiteralPath $file.Path) {
                Remove-Item -LiteralPath $file.Path -Force
            }
        } catch {
            $rollbackErrors.Add($_.Exception.Message)
        }
    }
    if ($parentExited -and $rollbackErrors.Count -eq 0) {
        try {
            Start-Process -FilePath (Join-Path $installRoot 'ZCue.exe') -WorkingDirectory $installRoot -WindowStyle Hidden
        } catch {
            $rollbackErrors.Add($_.Exception.Message)
        }
    }
    $message = "更新失败：$failure"
    if ($rollbackErrors.Count -gt 0) {
        $message += "`n恢复旧版时遇到问题，请从备份目录恢复：$backupRoot`n" + ($rollbackErrors -join "`n")
    } elseif ($parentExited) {
        $message += "`n已保留或恢复原程序文件。"
    }
    [IO.File]::WriteAllText((Join-Path $workspace 'error.log'), $message)
    Add-Type -AssemblyName PresentationFramework
    $null = [System.Windows.MessageBox]::Show($message, 'ZCue 更新失败')
} finally {
    if ($installed) {
        # workspace 已在入口确认属于临时目录，不删除安装目录或用户数据。
        Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# !SECTION 等待退出与替换回滚
