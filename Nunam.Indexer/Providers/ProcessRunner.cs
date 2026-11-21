using System.Diagnostics;
using System.Text;

namespace Nunam.Indexer.Providers
{
    /// <summary>Runs a program with optional input on stdin and returns its stdout as text.</summary>
    public static class ProcessRunner
    {
        private const int MaxErrorChars = 2000;

        /// <summary>
        /// Stops reading after <paramref name="maxChars"/> characters and kills the program, so a runaway
        /// one can't fill memory. Throws when it exits with a non-zero code or runs past <paramref name="timeout"/>.
        /// </summary>
        public static async Task<string> RunAsync(
            string executable, IEnumerable<string> arguments, byte[]? input, TimeSpan timeout, int maxChars,
            IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken)
        {
            var start = new ProcessStartInfo(executable)
            {
                RedirectStandardInput = input is not null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            foreach (var (name, value) in environment ?? new Dictionary<string, string>()) start.Environment[name] = value;

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);

            using var process = Process.Start(start) ?? throw new InvalidOperationException($"{executable} did not start");
            try
            {
                var output = ReadLimitedAsync(process.StandardOutput, maxChars, deadline.Token);
                var errors = ReadLimitedAsync(process.StandardError, MaxErrorChars, deadline.Token);
                if (input is not null)
                {
                    await process.StandardInput.BaseStream.WriteAsync(input, deadline.Token);
                    process.StandardInput.Close();
                }

                var text = await output;
                if (text.Length >= maxChars) return text;

                await process.WaitForExitAsync(deadline.Token);
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(executable)} exited with {process.ExitCode}: {(await errors).Trim()}");
                }
                return text;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"{Path.GetFileName(executable)} ran longer than {timeout.TotalSeconds:0} s");
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }

        private static async Task<string> ReadLimitedAsync(StreamReader reader, int maxChars, CancellationToken cancellationToken)
        {
            var text = new StringBuilder();
            var buffer = new char[8192];
            int read;
            while (text.Length < maxChars && (read = await reader.ReadAsync(buffer, cancellationToken)) > 0)
            {
                text.Append(buffer, 0, Math.Min(read, maxChars - text.Length));
            }
            return text.ToString();
        }
    }
}
