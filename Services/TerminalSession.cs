using System.Diagnostics;
using System.Text;

namespace DysinIDE.Services;

public sealed class TerminalSession : IDisposable
{
    private Process? _process;

    public event Action<string>? Output;
    public event Action? Exited;

    public bool IsRunning => _process is { HasExited: false };

    public void Start(string workingDirectory)
    {
        Stop();
        var windows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = windows ? "cmd.exe" : (File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh"),
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (!windows)
            psi.ArgumentList.Add("-i");

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                Output?.Invoke(e.Data + "\n");
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                Output?.Invoke(e.Data + "\n");
        };
        _process.Exited += (_, _) => Exited?.Invoke();
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void Send(string line)
    {
        if (!IsRunning)
            return;
        _process!.StandardInput.WriteLine(line);
        _process.StandardInput.Flush();
    }

    public void Stop()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(true);
        }
        catch (InvalidOperationException)
        {
        }
        _process?.Dispose();
        _process = null;
    }

    public void Dispose() => Stop();
}
