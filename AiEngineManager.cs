using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

namespace DKImageSimpleUpscaler;

internal enum AiModel
{
    General,
    Anime
}

internal static class AiEngineManager
{
    private const string EngineVersion = "v0.2.5.0";
    private const string EngineArchiveUrl = "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip";

    internal static string EngineRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DKImageSimpleUpscaler", "AI", "RealESRGAN", EngineVersion);

    internal static string? FindExecutable()
    {
        if (!Directory.Exists(EngineRoot)) return null;
        return Directory.EnumerateFiles(EngineRoot, "realesrgan-ncnn-vulkan.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    internal static bool IsInstalled => FindExecutable() is not null;

    internal static async Task InstallAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(EngineRoot);
        string archivePath = Path.Combine(Path.GetTempPath(), $"DKImageSimpleUpscaler-{Guid.NewGuid():N}.zip");
        string extractPath = Path.Combine(Path.GetTempPath(), $"DKImageSimpleUpscaler-{Guid.NewGuid():N}");

        try
        {
            progress?.Report("Real-ESRGAN AI 엔진 다운로드 중…");
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DKImageSimpleUpscaler/0.2");
            using var response = await client.GetAsync(EngineArchiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true))
            {
                var buffer = new byte[1024 * 128];
                long readTotal = 0;
                while (true)
                {
                    int read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    readTotal += read;
                    if (total is > 0)
                        progress?.Report($"AI 엔진 다운로드 중… {readTotal * 100 / total.Value}%");
                }
            }

            progress?.Report("AI 엔진 압축 해제 중…");
            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(archivePath, extractPath, overwriteFiles: true);

            string? exe = Directory.EnumerateFiles(extractPath, "realesrgan-ncnn-vulkan.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe is null) throw new InvalidDataException("다운로드한 패키지에서 Real-ESRGAN 실행 파일을 찾지 못했습니다.");

            string packageRoot = Path.GetDirectoryName(exe)!;
            if (Directory.Exists(EngineRoot)) Directory.Delete(EngineRoot, recursive: true);
            CopyDirectory(packageRoot, EngineRoot);

            if (FindExecutable() is null)
                throw new InvalidDataException("AI 엔진 설치를 확인할 수 없습니다.");

            progress?.Report("AI 엔진 설치 완료");
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(extractPath);
        }
    }

    internal static async Task<Bitmap> UpscaleAsync(
        Bitmap source,
        AiModel model,
        int tileSize,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string exe = FindExecutable() ?? throw new FileNotFoundException("AI 엔진이 설치되어 있지 않습니다.");
        string workingDirectory = Path.GetDirectoryName(exe)!;
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"DKImageSimpleUpscaler-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        string inputPath = Path.Combine(tempDirectory, "input.png");
        string outputPath = Path.Combine(tempDirectory, "output.png");
        source.Save(inputPath, System.Drawing.Imaging.ImageFormat.Png);

        try
        {
            string modelName = model == AiModel.Anime ? "realesrgan-x4plus-anime" : "realesrgan-x4plus";
            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(outputPath);
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add(modelName);
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add("4");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("png");
            if (tileSize > 0)
            {
                startInfo.ArgumentList.Add("-t");
                startInfo.ArgumentList.Add(tileSize.ToString());
            }

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress?.Report(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress?.Report(e.Data); };

            progress?.Report(model == AiModel.Anime ? "Anime AI 처리 중…" : "General AI 처리 중…");
            if (!process.Start()) throw new InvalidOperationException("AI 엔진을 시작하지 못했습니다.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Real-ESRGAN이 오류 코드 {process.ExitCode}로 종료되었습니다.");
            if (!File.Exists(outputPath))
                throw new FileNotFoundException("AI 결과 이미지가 생성되지 않았습니다.", outputPath);

            using var temp = new Bitmap(outputPath);
            return new Bitmap(temp);
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
