using System.Diagnostics;
using System.Text;

namespace DysinIDE.Services;

public sealed class TerminalSession : IDisposable
{
    private sealed class Job
    {
        public required Process Process { get; init; }
        public volatile bool Stopped;
    }

    private Job? _job;

    public event Action<string>? Output;
    public event Action<int, bool>? Finished;

    public bool Verbose { get; set; }

    public string WorkingDirectory { get; private set; } = Environment.CurrentDirectory;

    public bool IsBusy
    {
        get
        {
            var job = _job;
            if (job is null)
                return false;
            try
            {
                return !job.Process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public void Start(string workingDirectory)
    {
        Stop();
        SetWorkingDirectory(workingDirectory);
    }

    public void SetWorkingDirectory(string directory)
    {
        if (Directory.Exists(directory))
            WorkingDirectory = directory;
    }

    public void Run(string command)
    {
        Stop();

        var process = new Process { StartInfo = BuildStartInfo(command), EnableRaisingEvents = true };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            Output?.Invoke($"[error] {ex.Message}\n");
            process.Dispose();
            return;
        }

        var job = new Job { Process = process };
        _job = job;

        if (Verbose)
            Output?.Invoke($"[run] pid {process.Id}: {process.StartInfo.FileName} {process.StartInfo.Arguments}{string.Join(' ', process.StartInfo.ArgumentList)}\n");

        var pumpOut = Pump(process.StandardOutput);
        var pumpErr = Pump(process.StandardError);

        _ = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync();
                await Task.WhenAny(Task.WhenAll(pumpOut, pumpErr), Task.Delay(2000));
            }
            catch (Exception)
            {
            }

            var code = 0;
            try
            {
                code = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
            }

            if (ReferenceEquals(_job, job))
                _job = null;
            process.Dispose();
            Finished?.Invoke(code, job.Stopped);
        });
    }

    public void SendInput(string line)
    {
        var job = _job;
        if (job is null)
            return;
        try
        {
            job.Process.StandardInput.WriteLine(line);
            job.Process.StandardInput.Flush();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    public void CloseInput()
    {
        var job = _job;
        if (job is null)
            return;
        try
        {
            job.Process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    public void Stop()
    {
        var job = _job;
        if (job is null)
            return;
        job.Stopped = true;
        try
        {
            if (!job.Process.HasExited)
                job.Process.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    public void Dispose() => Stop();

    private ProcessStartInfo BuildStartInfo(string command)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/d /s /c \"chcp 65001>nul & {command}\"";
        }
        else
        {
            psi.FileName = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        return psi;
    }

    private Task Pump(StreamReader reader) => Task.Run(async () =>
    {
        var buffer = new char[4096];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                Output?.Invoke(new string(buffer, 0, read));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    });
}