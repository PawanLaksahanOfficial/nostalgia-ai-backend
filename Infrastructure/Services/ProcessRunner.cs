using System.Diagnostics;
using System.Text;

namespace Infrastructure.Services
{
    internal sealed class ProcessResult
    {
        public int ExitCode { get; init; }
        public string StandardOutput { get; init; } = string.Empty;
        public string StandardError { get; init; } = string.Empty;
        public bool TimedOut { get; init; }
        public bool Succeeded => !TimedOut && ExitCode == 0;
    }

    internal static class ProcessRunner
    {
        private const int MaxRetainedLines = 200;

        public static async Task<ProcessResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string? workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var stdout = new BoundedLineBuffer(MaxRetainedLines);
            var stderr = new BoundedLineBuffer(MaxRetainedLines);
            process.OutputDataReceived += (_, e) => stdout.Add(e.Data);
            process.ErrorDataReceived += (_, e) => stderr.Add(e.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            TrySetBelowNormalPriority(process);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);

                return new ProcessResult
                {
                    ExitCode = -1,
                    TimedOut = true,
                    StandardOutput = stdout.ToString(),
                    StandardError = stderr.ToString()
                };
            }
            process.WaitForExit();
            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString()
            };
        }

        private static void TrySetBelowNormalPriority(Process process)
        {
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch
            {

            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {

            }
        }

        private sealed class BoundedLineBuffer
        {
            private readonly Queue<string> _lines = new();
            private readonly int _capacity;
            private readonly object _gate = new();

            public BoundedLineBuffer(int capacity) => _capacity = capacity;

            public void Add(string? line)
            {
                if (line == null)
                {
                    return;
                }

                lock (_gate)
                {
                    _lines.Enqueue(line);
                    while (_lines.Count > _capacity)
                    {
                        _lines.Dequeue();
                    }
                }
            }

            public override string ToString()
            {
                lock (_gate)
                {
                    return string.Join(Environment.NewLine, _lines);
                }
            }
        }
    }
}
