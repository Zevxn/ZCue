using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZCue.Services;

public sealed class UpdateInstallerService
{
    private const long MaximumPackageSize = 256L * 1024 * 1024;
    private static readonly HttpClient DownloadClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    // SECTION 下载与更新交接

    public async Task PrepareAndStartAsync(
        UpdateInfo update,
        IReadOnlyList<string> protectedPaths,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var download = update.Download ?? throw new InvalidOperationException("此版本未提供可直接安装的更新包。");
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64
            || !string.Equals(Path.GetFileName(Environment.ProcessPath), "ZCue.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("请从 Windows x64 发布版的 ZCue.exe 运行程序后更新。");
        }

        var installDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        // 在退出前确认原目录可写，不自动提权或改变程序位置。
        var probe = Path.Combine(installDirectory, $".zcue-write-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(probe, string.Empty, cancellationToken);
        File.Delete(probe);

        var workspace = Path.Combine(Path.GetTempPath(), $"ZCue-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var handedOff = false;
        try
        {
            var archivePath = Path.Combine(workspace, "update.zip");
            await DownloadAsync(download, archivePath, progress, cancellationToken);
            progress.Report("校验更新包…");
            var stageDirectory = Path.Combine(workspace, "stage");
            var files = await Task.Run(() => ExtractPackage(archivePath, stageDirectory, update.TagName), cancellationToken);
            foreach (var relativePath in files)
            {
                var destination = Path.GetFullPath(Path.Combine(installDirectory, relativePath));
                if (protectedPaths.Any(path => string.Equals(destination, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException("更新包与用户数据位置冲突，已停止更新。");
                }
            }

            var scriptPath = Path.Combine(workspace, "install.ps1");
            using (var resource = typeof(UpdateInstallerService).Assembly.GetManifestResourceStream("ZCue.InstallUpdate.ps1")
                ?? throw new InvalidOperationException("缺少更新程序。"))
            using (var reader = new StreamReader(resource))
            {
                await File.WriteAllTextAsync(scriptPath, await reader.ReadToEndAsync(cancellationToken),
                    new UTF8Encoding(true), cancellationToken);
            }

            using var currentProcess = Process.GetCurrentProcess();
            var plan = new
            {
                InstallDirectory = installDirectory,
                Files = files,
                ProcessId = Environment.ProcessId,
                ProcessStartTicks = currentProcess.StartTime.ToUniversalTime().Ticks.ToString()
            };
            await File.WriteAllTextAsync(Path.Combine(workspace, "plan.json"), JsonSerializer.Serialize(plan),
                new UTF8Encoding(true), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath()
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath })
            {
                startInfo.ArgumentList.Add(argument);
            }

            progress.Report("准备重启更新…");
            using var updater = Process.Start(startInfo) ?? throw new IOException("无法启动更新程序。");
            try
            {
                // 辅助进程成功读取更新计划后才退出主程序，避免启动失败导致程序消失。
                for (var attempt = 0; attempt < 200; attempt++)
                {
                    if (File.Exists(Path.Combine(workspace, "ready")))
                    {
                        handedOff = true;
                        return;
                    }

                    if (updater.HasExited)
                    {
                        throw new IOException("更新程序启动失败，当前版本未被替换。");
                    }

                    await Task.Delay(100, cancellationToken);
                }

                throw new TimeoutException("更新程序未能及时启动，当前版本未被替换。");
            }
            finally
            {
                if (!handedOff && !updater.HasExited)
                {
                    updater.Kill();
                    await updater.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            if (!handedOff)
            {
                try { Directory.Delete(workspace, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static async Task DownloadAsync(
        UpdateDownload download, string path, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (download.Size <= 0 || download.Size > MaximumPackageSize)
        {
            throw new InvalidDataException("更新包大小无效或超过限制。");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var response = await DownloadClient.GetAsync(download.Uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using (var output = File.Create(path))
        {
            var buffer = new byte[81920];
            long received = 0;
            int lastPercent = -1;
            int count;
            while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                received += count;
                if (received > download.Size)
                {
                    throw new InvalidDataException("更新包大小与发布信息不一致。");
                }

                await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                var percent = (int)(received * 100 / download.Size);
                if (percent != lastPercent)
                {
                    progress.Report($"下载中 {percent}%");
                    lastPercent = percent;
                }
            }

            if (received != download.Size)
            {
                throw new InvalidDataException("更新包下载不完整，请重试。");
            }
        }

        if (!string.IsNullOrWhiteSpace(download.Digest))
        {
            await using var file = File.OpenRead(path);
            var actualDigest = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(file, timeout.Token));
            if (!string.Equals(actualDigest, download.Digest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新包校验失败，请重试。");
            }
        }
    }

    // !SECTION 下载与更新交接

    // SECTION 发布包校验与解压

    private static string[] ExtractPackage(string archivePath, string stageDirectory, string tagName)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 2000 || archive.Entries.Sum(entry => entry.Length) > MaximumPackageSize * 4)
        {
            throw new InvalidDataException("更新包解压大小超过限制。");
        }

        var executables = archive.Entries.Where(entry =>
            string.Equals(entry.FullName.Replace('\\', '/').Split('/').Last(), "ZCue.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (executables.Length != 1)
        {
            throw new InvalidDataException("更新包必须包含唯一的 ZCue.exe。");
        }

        var executableName = executables[0].FullName.Replace('\\', '/');
        var prefix = executableName[..^"ZCue.exe".Length];
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Split('/').Any(part => part is ".." or "." || part.Contains(':'))
                || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            {
                throw new InvalidDataException("更新包包含不安全的文件路径。");
            }

            if (name.EndsWith('/'))
            {
                continue;
            }

            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新包包含程序目录以外的文件。");
            }

            var relative = name[prefix.Length..];
            var isProgramFile = !relative.Contains('/') && (relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || relative is "ZCue.exe" or "ZCue.pdb" or "ZCue.deps.json" or "ZCue.runtimeconfig.json");
            var isResource = relative.Equals("assets/logo.ico", StringComparison.OrdinalIgnoreCase)
                || (relative.StartsWith("docs/licenses/", StringComparison.OrdinalIgnoreCase)
                    && relative.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
            if ((!isProgramFile && !isResource) || !files.Add(relative))
            {
                throw new InvalidDataException($"更新包包含不支持或重复的文件：{relative}");
            }

            var destination = Path.GetFullPath(Path.Combine(stageDirectory, relative));
            if (!destination.StartsWith(Path.GetFullPath(stageDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新包路径越界。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination);
        }

        foreach (var required in new[] { "ZCue.exe", "ZCue.dll", "ZCue.deps.json", "ZCue.runtimeconfig.json",
            "PinYinConverterCore.dll", "Wpf.Ui.dll", "Wpf.Ui.Abstractions.dll", "assets/logo.ico" })
        {
            if (!files.Contains(required))
            {
                throw new InvalidDataException($"更新包不完整，缺少 {required}。");
            }
        }

        var packageVersion = AssemblyName.GetAssemblyName(Path.Combine(stageDirectory, "ZCue.dll")).Version;
        if (packageVersion is null || $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}" != tagName.TrimStart('v', 'V'))
        {
            throw new InvalidDataException("更新包版本与发布版本不一致。");
        }

        return files.ToArray();
    }

    // !SECTION 发布包校验与解压
}
