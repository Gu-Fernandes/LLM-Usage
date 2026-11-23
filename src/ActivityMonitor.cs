using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace UsageLLMs
{
    internal sealed class ActivityMonitor : IDisposable
    {
        private const string Rollouts = "rollout-*.jsonl";
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10);
        private static readonly Regex Lifecycle = new Regex(
            @"^\{""timestamp"":""[^""\r\n]+"",(?:""ordinal"":\d+,)?""type"":""event_msg"",""payload"":\{""type"":""(?<event>task_started|turn_started|task_complete|turn_complete|turn_aborted)""",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private readonly object gate = new object();
        private readonly HashSet<string> pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer scan;
        private readonly Action<bool> onChange;
        private FileSystemWatcher watcher;
        private byte[] buffer;
        private bool active;
        private bool disposed;

        private sealed class Session
        {
            public long Offset;
            public bool Active;
            public DateTime Expires;
        }

        public ActivityMonitor(Action<bool> onChange) : this(SessionDirectory(), onChange) { }

        internal ActivityMonitor(string directory, Action<bool> onChange)
        {
            this.onChange = onChange;
            scan = new Timer(Scan);
            if (!Directory.Exists(directory))
                return;

            try
            {
                watcher = new FileSystemWatcher(directory, Rollouts)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                watcher.Created += OnFile;
                watcher.Changed += OnFile;
                watcher.Deleted += OnFile;
                watcher.Renamed += (sender, args) => { Queue(args.OldFullPath); Queue(args.FullPath); };
                watcher.EnableRaisingEvents = true;
                ThreadPool.QueueUserWorkItem(SeedRecent, directory);
            }
            catch (IOException) { StopWatcher(); }
            catch (UnauthorizedAccessException) { StopWatcher(); }
            catch (SecurityException) { StopWatcher(); }
        }

        private static string SessionDirectory()
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (String.IsNullOrWhiteSpace(home))
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (String.IsNullOrEmpty(profile))
                    profile = Environment.GetEnvironmentVariable("USERPROFILE");
                if (String.IsNullOrEmpty(profile))
                    return null;
                home = Path.Combine(profile, ".codex");
            }
            return Path.Combine(home, "sessions");
        }

        private void SeedRecent(object value)
        {
            try
            {
                var today = DateTime.Today;
                var cutoff = DateTime.UtcNow - SessionLifetime;
                for (int day = 0; day < 2; day++)
                {
                    string folder = Path.Combine((string)value, today.AddDays(-day)
                        .ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture));
                    if (!Directory.Exists(folder))
                        continue;
                    foreach (var entry in new DirectoryInfo(folder).EnumerateFiles(Rollouts)
                        .Where(file => file.LastWriteTimeUtc > cutoff)
                        .OrderByDescending(file => file.LastWriteTimeUtc).Take(8))
                        Queue(entry.FullName);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }

        private void OnFile(object sender, FileSystemEventArgs args) { Queue(args.FullPath); }

        private void Queue(string path)
        {
            lock (gate)
            {
                if (!disposed && pending.Add(path) && pending.Count == 1)
                    scan.Change(180, Timeout.Infinite);
            }
        }

        private void Scan(object unused)
        {
            bool next;
            lock (gate)
            {
                if (disposed)
                    return;

                var now = DateTime.UtcNow;
                foreach (var path in pending)
                    ReadMarkers(path, now);
                pending.Clear();

                foreach (var entry in sessions.Where(pair => pair.Value.Expires <= now).ToArray())
                    sessions.Remove(entry.Key);
                if (sessions.Count != 0)
                {
                    DateTime expires = sessions.Values.Min(session => session.Expires);
                    scan.Change(Math.Max(1, (int)Math.Ceiling((expires - now).TotalMilliseconds)), Timeout.Infinite);
                }
                else
                    buffer = null;

                next = sessions.Values.Any(session => session.Active);
                if (next == active)
                    return;
                active = next;
            }
            onChange(next);
        }

        private void ReadMarkers(string path, DateTime now)
        {
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    Session session;
                    if (!sessions.TryGetValue(path, out session) || file.Length < session.Offset)
                        sessions[path] = session = new Session();
                    session.Expires = now + SessionLifetime;
                    if (file.Length == session.Offset)
                        return;

                    // Only inspect appended data, capped to the most recent 256 KiB on first observation or large writes.
                    if (buffer == null)
                        buffer = new byte[256 * 1024];
                    file.Position = Math.Max(session.Offset, file.Length - buffer.Length);
                    long start = file.Position;
                    int count = file.Read(buffer, 0, buffer.Length);
                    if (count == 0)
                        return;
                    int last = Array.LastIndexOf(buffer, (byte)'\n', count - 1, count);
                    if (last < 0)
                        return;
                    session.Offset = start + last + 1;

                    string lines = Encoding.UTF8.GetString(buffer, 0, last + 1);
                    for (var match = Lifecycle.Match(lines); match.Success; match = match.NextMatch())
                        session.Active = match.Groups["event"].Value.EndsWith("_started", StringComparison.Ordinal);
                }
            }
            catch (FileNotFoundException) { sessions.Remove(path); }
            catch (DirectoryNotFoundException) { sessions.Remove(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }

        private void StopWatcher()
        {
            if (watcher != null)
                watcher.Dispose();
            watcher = null;
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                scan.Dispose();
            }
            StopWatcher();
        }
    }
}
