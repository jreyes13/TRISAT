using System.Diagnostics;

namespace Trisat.Core.Services;

/// <summary>
/// Manages external TRISAT Python processes (TRISAT-SDR receiver & Septentrio SBF Replay + OSNMAlib)
/// natively from the Windows desktop cockpit, streaming stdout/stderr in real time.
/// </summary>
public sealed class ProcessBridgeService : IDisposable
{
    private Process? _process;
    private readonly object _lock = new();

    public bool IsRunning
    {
        get
        {
            lock (_lock)
                return _process is { HasExited: false };
        }
    }

    public event Action<string>? OutputReceived;
    public event Action<bool, string>? StateChanged;

    /// <summary>
    /// Walks upward from the current executable directory to locate the TRISAT repository root
    /// containing <c>trisat_sdr/main.py</c>.
    /// </summary>
    public static string? DiscoverRepositoryRoot(string? startingDirectory = null)
    {
        var dir = new DirectoryInfo(startingDirectory ?? AppContext.BaseDirectory);
        for (int depth = 0; depth < 10 && dir is not null; depth++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "trisat_sdr", "main.py")))
                return dir.FullName;
            dir = dir.Parent;
        }

        var cwd = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int depth = 0; depth < 6 && cwd is not null; depth++)
        {
            if (File.Exists(Path.Combine(cwd.FullName, "trisat_sdr", "main.py")))
                return cwd.FullName;
            cwd = cwd.Parent;
        }

        return null;
    }

    public bool StartTrisatSdr(
        string repoRoot,
        string pythonExecutable = "python",
        string band1 = "GAL_E1",
        string band2 = "GAL_E6",
        bool singleSdr = false,
        int udpPort = 5005,
        int tcpPort = 5006,
        int webPort = 8085,
        bool emitLegacySimulation = false)
    {
        var scriptPath = Path.Combine(repoRoot, "trisat_sdr", "main.py");
        if (!File.Exists(scriptPath))
        {
            OutputReceived?.Invoke($"[ERROR] trisat_sdr/main.py not found under: {repoRoot}");
            return false;
        }

        var args = new List<string>
        {
            "-u",
            $"\"{scriptPath}\"",
            "--band1", band1,
            "--band2", band2,
            "--udp-port", udpPort.ToString(),
            "--tcp-port", tcpPort.ToString(),
            "--web-port", webPort.ToString()
        };
        if (singleSdr) args.Add("--single-sdr");
        if (emitLegacySimulation) args.Add("--emit-legacy-simulation");

        return StartProcess(pythonExecutable, string.Join(' ', args), repoRoot, null, "TRISAT-SDR");
    }

    public bool StartSbfReplay(
        string repoRoot,
        string sbfFilePath,
        int port = 8090,
        string? merkleTreeXmlPath = null,
        string? publicKeyXmlPath = null,
        string? osnmaLibPath = null,
        string pythonExecutable = "python")
    {
        if (!File.Exists(sbfFilePath))
        {
            OutputReceived?.Invoke($"[ERROR] SBF recording not found: {sbfFilePath}");
            return false;
        }

        var args = new List<string>
        {
            "-u",
            "-m", "trisat_sdr.replay_sbf",
            $"\"{sbfFilePath}\"",
            "--port", port.ToString()
        };

        if (!string.IsNullOrWhiteSpace(merkleTreeXmlPath) && !string.IsNullOrWhiteSpace(publicKeyXmlPath))
        {
            args.Add("--merkle-tree");
            args.Add($"\"{merkleTreeXmlPath}\"");
            args.Add("--public-key");
            args.Add($"\"{publicKeyXmlPath}\"");
        }

        Dictionary<string, string>? env = null;
        if (!string.IsNullOrWhiteSpace(osnmaLibPath))
            env = new Dictionary<string, string> { ["PYTHONPATH"] = osnmaLibPath };

        return StartProcess(pythonExecutable, string.Join(' ', args), repoRoot, env, "SBF-Replay");
    }

    public bool StartProcess(
        string fileName,
        string arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? extraEnvironment,
        string label)
    {
        Stop();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (extraEnvironment is not null)
            {
                foreach (var kv in extraEnvironment)
                    psi.Environment[kv.Key] = kv.Value;
            }

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    OutputReceived?.Invoke($"[{label}] {e.Data}");
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    OutputReceived?.Invoke($"[{label}:ERR] {e.Data}");
            };
            proc.Exited += (_, _) =>
            {
                StateChanged?.Invoke(false, $"{label} stopped (exit code {proc.ExitCode})");
            };

            if (!proc.Start())
            {
                OutputReceived?.Invoke($"[ERROR] Failed to launch {fileName} {arguments}");
                return false;
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            lock (_lock)
                _process = proc;

            StateChanged?.Invoke(true, $"{label} running (PID {proc.Id})");
            return true;
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke($"[ERROR] Unable to launch {label}: {ex.Message}");
            StateChanged?.Invoke(false, $"Launch failed: {ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        Process? proc;
        lock (_lock)
        {
            proc = _process;
            _process = null;
        }

        if (proc is null) return;

        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process may have exited concurrently
        }
        finally
        {
            proc.Dispose();
            StateChanged?.Invoke(false, "Stopped");
        }
    }

    public void Dispose() => Stop();
}
