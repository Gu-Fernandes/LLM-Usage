using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace UsageLLMs
{
    internal sealed class Quota
    {
        public double Used { get; private set; }
        public DateTimeOffset? Reset { get; private set; }

        public Quota(double used, DateTimeOffset? reset)
        {
            Used = Math.Min(100, Math.Max(0, used));
            Reset = reset;
        }
    }

    internal sealed class Usage
    {
        public Quota FiveHours;
        public Quota Weekly;
        public readonly DateTimeOffset Observed = DateTimeOffset.Now;
        private static readonly DateTimeOffset Epoch =
            new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static Usage Read(Dictionary<string, object> result)
        {
            var bucket = ObjectAt(ObjectAt(result, "rateLimitsByLimitId"), "codex");
            if (bucket == null)
            {
                var legacy = ObjectAt(result, "rateLimits");
                var id = Find(legacy, "limitId") as string;
                if (String.IsNullOrEmpty(id) || id == "codex")
                    bucket = legacy;
            }

            var usage = new Usage();
            foreach (var key in new[] { "primary", "secondary" })
            {
                var window = ObjectAt(bucket, key);
                var mins = Number(Find(window, "windowDurationMins"));
                var used = Number(Find(window, "usedPercent"));
                if (!used.HasValue || (mins != 300 && mins != 10080))
                    continue;

                var seconds = Number(Find(window, "resetsAt"));
                DateTimeOffset? reset = seconds > 0 && seconds < 253402300799
                    ? Epoch.AddSeconds(seconds.Value)
                    : (DateTimeOffset?)null;
                var quota = new Quota(used.Value, reset);
                if (mins == 300)
                    usage.FiveHours = quota;
                else
                    usage.Weekly = quota;
            }
            return usage;
        }

        public static object Find(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) ? value : null;
        }

        public static Dictionary<string, object> ObjectAt(Dictionary<string, object> obj, string key)
        {
            return Find(obj, key) as Dictionary<string, object>;
        }

        private static double? Number(object value)
        {
            if (!(value is int || value is long || value is decimal || value is double))
                return null;
            double number = Convert.ToDouble(value);
            return Double.IsNaN(number) || Double.IsInfinity(number) ? (double?)null : number;
        }
    }

    internal sealed class UsageException : Exception
    {
        public readonly bool RequiresLogin;

        public UsageException(string message, bool requiresLogin = false) : base(message)
        {
            RequiresLogin = requiresLogin;
        }
    }

    internal sealed class CodexConnection : IDisposable
    {
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly object gate = new object();
        private Process process;
        private bool disposed;

        public Task<Usage> Fetch()
        {
            return Task.Run((Func<Task<Usage>>)FetchCore);
        }

        private async Task<Usage> FetchCore()
        {
            string executable = FindExecutable();
            if (executable == null)
                throw new UsageException("Codex CLI não encontrado. Instale-o ou configure CODEX_USAGE_CLI.");

            var start = new ProcessStartInfo(executable, "app-server -c analytics.enabled=false")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.SystemDirectory
            };
            if (String.IsNullOrEmpty(start.EnvironmentVariables["CODEX_HOME"]) &&
                String.IsNullOrEmpty(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) &&
                Directory.Exists(Environment.GetEnvironmentVariable("USERPROFILE")))
            {
                start.EnvironmentVariables["CODEX_HOME"] =
                    Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), ".codex");
            }

            using (var child = new Process { StartInfo = start })
            {
                lock (gate)
                {
                    if (disposed)
                        throw new ObjectDisposedException("CodexConnection");
                    try { child.Start(); }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        throw new UsageException("Não foi possível abrir o Codex CLI.");
                    }
                    process = child;
                }
                try
                {
                    // Drain stderr so the CLI never blocks on a full pipe.
                    child.ErrorDataReceived += (sender, args) => { };
                    child.BeginErrorReadLine();
                    child.StandardInput.AutoFlush = true;
                    await Request(child, 1, "initialize",
                        ",\"params\":{\"clientInfo\":{\"name\":\"codex_usage_widget\",\"version\":\"1.0.0\"}}");
                    child.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                    return Usage.Read(await Request(child, 2, "account/rateLimits/read", null));
                }
                finally
                {
                    try
                    {
                        child.StandardInput.Close();
                        if (!child.WaitForExit(350))
                            Stop(child);
                    }
                    catch (IOException) { Stop(child); }
                    catch (InvalidOperationException) { Stop(child); }
                    lock (gate)
                        process = null;
                }
            }
        }

        private async Task<Dictionary<string, object>> Request(Process child, int id, string method, string parameters)
        {
            child.StandardInput.WriteLine("{\"id\":" + id + ",\"method\":\"" + method + "\"" + parameters + "}");

            var output = child.StandardOutput;
            var deadline = Task.Delay(15000);
            while (true)
            {
                var line = output.ReadLineAsync();
                if (await Task.WhenAny(line, deadline) != line)
                    break;

                string raw = await line;
                if (raw == null)
                    throw new UsageException("O Codex encerrou a conexão. Confira o CLI e a sessão local.");

                var message = json.DeserializeObject(raw) as Dictionary<string, object>;
                if (!Object.Equals(Usage.Find(message, "id"), id) || Usage.Find(message, "method") != null)
                    continue;

                var error = Usage.ObjectAt(message, "error");
                if (error != null)
                    throw ReadError(error);

                var result = Usage.ObjectAt(message, "result");
                if (result != null)
                    return result;
                break;
            }
            throw new UsageException("O Codex não respondeu à consulta.");
        }

        private static UsageException ReadError(Dictionary<string, object> error)
        {
            var message = Usage.Find(error, "message") as string ?? "";
            if (message.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.Contains("401") || message.Contains("403"))
                return new UsageException("Faça login no Codex para ver o consumo.", true);

            return new UsageException("O Codex não conseguiu consultar as cotas. Confira a conexão.");
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                if (process != null)
                    Stop(process);
            }
        }

        private static void Stop(Process child)
        {
            try
            {
                if (!child.HasExited)
                    child.Kill();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static string FindExecutable()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_USAGE_CLI");
            if (!String.IsNullOrWhiteSpace(configured))
            {
                if (Path.IsPathRooted(configured) &&
                    String.Equals(Path.GetFileName(configured), "codex.exe", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(configured))
                    return configured;
                throw new UsageException("CODEX_USAGE_CLI deve apontar para um codex.exe existente.");
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string entry in path.Split(Path.PathSeparator))
            {
                string folder = entry.Trim().Trim('"');
                if (Path.IsPathRooted(folder))
                {
                    string exe = Path.Combine(folder, "codex.exe");
                    if (File.Exists(exe))
                        return exe;
                }
            }

            string profile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (String.IsNullOrEmpty(profile))
                return null;

            foreach (string home in new[]
            {
                Environment.GetEnvironmentVariable("CODEX_HOME"),
                Path.Combine(profile, ".codex"), Path.Combine(profile, ".local")
            })
            {
                if (String.IsNullOrEmpty(home) || !Path.IsPathRooted(home))
                    continue;
                string exe = Path.Combine(home, "bin", "codex.exe");
                if (File.Exists(exe))
                    return exe;
            }

            string architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
            string platform = String.Equals(architecture, "ARM64", StringComparison.OrdinalIgnoreCase)
                ? "aarch64" : "x86_64";
            foreach (string editor in new[] { ".vscode", ".vscode-insiders", ".cursor" })
            {
                string folder = Path.Combine(profile, editor, "extensions");
                if (!Directory.Exists(folder))
                    continue;
                foreach (var extension in new DirectoryInfo(folder).GetDirectories("openai.chatgpt-*")
                    .OrderByDescending(dir => dir.LastWriteTimeUtc))
                {
                    string exe = Path.Combine(extension.FullName, "bin", "windows-" + platform, "codex.exe");
                    if (File.Exists(exe))
                        return exe;
                }
            }

            string appData = Environment.GetEnvironmentVariable("APPDATA");
            if (!String.IsNullOrEmpty(appData))
            {
                string modules = Path.Combine(appData, "npm", "node_modules", "@openai");
                string package = platform == "aarch64" ? "codex-win32-arm64" : "codex-win32-x64";
                foreach (string basePath in new[] { Path.Combine(modules, package),
                    Path.Combine(modules, "codex", "node_modules", "@openai", package),
                    Path.Combine(modules, "codex") })
                {
                    string exe = Path.Combine(basePath, "vendor",
                        platform + "-pc-windows-msvc", "codex", "codex.exe");
                    if (File.Exists(exe))
                        return exe;
                }
            }
            return null;
        }
    }
}
