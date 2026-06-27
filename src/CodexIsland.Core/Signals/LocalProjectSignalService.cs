using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexIsland.Core.Models;

namespace CodexIsland.Core.Signals;

public sealed class LocalProjectSignalService : IProjectSignalService
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(2);

    public static string ResolveStatusFile()
    {
        var env = Environment.GetEnvironmentVariable("CODEX_ISLAND_STATUS_FILE");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexIsland",
            "status.json");
    }

    public Task<ProjectStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var statusFile = Environment.GetEnvironmentVariable("CODEX_ISLAND_STATUS_FILE");
        if (!string.IsNullOrWhiteSpace(statusFile) && File.Exists(statusFile))
        {
            var fromStatusFile = TryReadStatusFile(statusFile);
            if (fromStatusFile is not null)
            {
                return Task.FromResult(fromStatusFile);
            }
        }

        var recent = GetRecentProjects(1);
        return Task.FromResult(recent.FirstOrDefault() ?? ProjectStatusSnapshot.Ready());
    }

    public IReadOnlyList<ProjectStatusSnapshot> GetRecentProjects(int maxCount = 6)
    {
        var workspaceState = TryReadWorkspaceState();
        var items = new List<ProjectStatusSnapshot>();
        var statusFile = Environment.GetEnvironmentVariable("CODEX_ISLAND_STATUS_FILE");
        if (!string.IsNullOrWhiteSpace(statusFile) && File.Exists(statusFile))
        {
            var status = TryReadStatusFile(statusFile);
            if (status is not null)
            {
                items.Add(status);
            }
        }

        items.AddRange(ReadThreadsFromStateDb(Math.Max(maxCount * 10, 32), workspaceState));

        if (items.Count == 0)
        {
            items.AddRange(ReadThreadsFromSessionLogs(Math.Max(maxCount * 12, 48), workspaceState));
        }

        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.WorkingDirectory))
            .GroupBy(item => item.ProjectId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .OrderBy(item => ProjectSortKey(item, workspaceState))
            .ThenByDescending(item => item.UpdatedAt)
            .Take(maxCount)
            .ToList();
    }

    private static IEnumerable<ProjectStatusSnapshot> ReadThreadsFromStateDb(int limit, WorkspaceState workspaceState)
    {
        var stateDb = FindLatestStateDatabase();
        if (stateDb is null)
        {
            yield break;
        }

        foreach (var row in QueryThreads(stateDb, limit))
        {
            if (string.Equals(row.ThreadSource, "subagent", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var workingDirectory = NormalizePath(row.Cwd);
            var projectRoot = FindProjectRoot(workingDirectory, workspaceState);
            if (string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(projectRoot))
            {
                continue;
            }

            var projectName = ShortName(projectRoot);
            var title = CleanTitle(row.Title) ?? projectName;
            var rolloutPath = NormalizePath(row.RolloutPath);
            var eventInfo = TryReadRolloutSignal(rolloutPath);
            var updatedAt = eventInfo.UpdatedAt
                ?? FromUnixTimestamp(row.UpdatedAtUnix)
                ?? (File.Exists(rolloutPath) ? File.GetLastWriteTime(rolloutPath) : DateTimeOffset.Now);

            yield return CreateSnapshot(
                row.Id,
                title,
                eventInfo.EventName,
                updatedAt,
                workingDirectory,
                projectRoot,
                projectName);
        }
    }

    private static IEnumerable<ProjectStatusSnapshot> ReadThreadsFromSessionLogs(int maxFiles, WorkspaceState workspaceState)
    {
        var sessionRoot = Path.Combine(ResolveUserHome(), ".codex", "sessions");
        if (!Directory.Exists(sessionRoot))
        {
            yield break;
        }

        foreach (var file in Directory
                     .EnumerateFiles(sessionRoot, "*.jsonl", SearchOption.AllDirectories)
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Take(maxFiles))
        {
            var summary = TryReadCodexSession(file.FullName, workspaceState);
            if (summary is null)
            {
                continue;
            }

            yield return CreateSnapshot(
                summary.ThreadId,
                summary.Title,
                summary.EventName,
                summary.UpdatedAt,
                summary.WorkingDirectory,
                summary.ProjectRoot,
                summary.ProjectName);
        }
    }

    private static ProjectStatusSnapshot CreateSnapshot(
        string projectId,
        string displayName,
        string? eventName,
        DateTimeOffset updatedAt,
        string workingDirectory,
        string projectRoot,
        string projectName)
    {
        var isFresh = DateTimeOffset.Now - updatedAt <= FreshnessWindow;
        var signal = ProjectSignalMapper.FromEvent(eventName);

        if (!isFresh && signal is not ProjectSignal.Completed)
        {
            signal = ProjectSignal.Stale;
        }
        else if (DateTimeOffset.Now - updatedAt <= ActiveWindow &&
                 signal is ProjectSignal.Ready or ProjectSignal.Completed)
        {
            signal = ProjectSignal.Working;
        }

        return new ProjectStatusSnapshot(
            projectId,
            displayName,
            signal,
            eventName,
            updatedAt,
            isFresh,
            workingDirectory,
            projectRoot,
            projectName);
    }

    private static int ProjectSortKey(ProjectStatusSnapshot item, WorkspaceState state)
    {
        if (IsLiveSignal(item.Signal) || DateTimeOffset.Now - item.UpdatedAt <= ActiveWindow)
        {
            return -2000;
        }

        if (!string.IsNullOrWhiteSpace(item.ProjectRoot) &&
            state.ActiveRoots.Contains(item.ProjectRoot))
        {
            return -1000;
        }

        if (!string.IsNullOrWhiteSpace(item.ProjectRoot) &&
            state.OrderIndex.TryGetValue(item.ProjectRoot, out var exact))
        {
            return exact;
        }

        return 10_000;
    }

    private static bool IsLiveSignal(ProjectSignal signal)
    {
        return signal is ProjectSignal.Thinking
            or ProjectSignal.Working
            or ProjectSignal.ToolDone
            or ProjectSignal.Permission
            or ProjectSignal.Attention
            or ProjectSignal.Blocked;
    }

    private static WorkspaceState TryReadWorkspaceState()
    {
        try
        {
            var globalStatePath = Path.Combine(ResolveUserHome(), ".codex", ".codex-global-state.json");
            if (!File.Exists(globalStatePath))
            {
                return WorkspaceState.Empty;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(globalStatePath));
            var root = doc.RootElement;
            var projectOrder = ReadStringArray(root, "project-order")
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToList();
            var activeRoots = ReadStringArray(root, "active-workspace-roots")
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return new WorkspaceState(
                projectOrder.Select((path, index) => new { path, index })
                    .ToDictionary(item => item.path, item => item.index, StringComparer.OrdinalIgnoreCase),
                activeRoots);
        }
        catch
        {
            return WorkspaceState.Empty;
        }
    }

    private static IEnumerable<string> ReadStringArray(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(item.GetString()))
            {
                yield return item.GetString()!;
            }
        }
    }

    private static string? FindLatestStateDatabase()
    {
        try
        {
            var codexRoot = Path.Combine(ResolveUserHome(), ".codex");
            return Directory.EnumerateFiles(codexRoot, "state_*.sqlite", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<ThreadRow> QueryThreads(string stateDbPath, int limit)
    {
        const string pythonScript = """
import json, sqlite3, sys

db_path = sys.argv[1]
limit = int(sys.argv[2])
conn = sqlite3.connect(db_path)
conn.row_factory = sqlite3.Row
rows = conn.execute(
    "select id, cwd, title, rollout_path, thread_source, updated_at "
    "from threads "
    "where archived = 0 and coalesce(cwd, '') <> '' "
    "order by updated_at desc "
    "limit ?",
    (limit,)
).fetchall()
payload = []
for row in rows:
    payload.append({
        "id": row["id"],
        "cwd": row["cwd"],
        "title": row["title"],
        "rollout_path": row["rollout_path"],
        "thread_source": row["thread_source"],
        "updated_at": row["updated_at"],
    })
sys.stdout.write(json.dumps(payload))
""";

        var output = RunPythonJson(pythonScript, stateDbPath, limit.ToString());
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<ThreadRow>();
        }

        try
        {
            using var doc = JsonDocument.Parse(output);
            return doc.RootElement.ValueKind != JsonValueKind.Array
                ? Array.Empty<ThreadRow>()
                : doc.RootElement.EnumerateArray()
                    .Select(element => new ThreadRow(
                        TryGetString(element, "id") ?? "",
                        TryGetString(element, "cwd"),
                        TryGetString(element, "title"),
                        TryGetString(element, "rollout_path"),
                        TryGetString(element, "thread_source"),
                        TryGetLong(element, "updated_at")))
                    .Where(row => !string.IsNullOrWhiteSpace(row.Id))
                    .ToList();
        }
        catch
        {
            return Array.Empty<ThreadRow>();
        }
    }

    private static string? RunPythonJson(string script, params string[] arguments)
    {
        var candidates = new[]
        {
            ResolveOnPath("python.exe"),
            ResolveOnPath("py.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WindowsApps",
                "python.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WindowsApps",
                "py.exe")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var command in candidates)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (Path.GetFileName(command!).StartsWith("py", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add("-3");
            }

            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(script);
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    continue;
                }

                if (!process.WaitForExit((int)TimeSpan.FromSeconds(6).TotalMilliseconds))
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch
                    {
                    }

                    continue;
                }

                if (process.ExitCode == 0)
                {
                    return process.StandardOutput.ReadToEnd();
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string? ResolveOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static SessionSignal TryReadRolloutSignal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return SessionSignal.Empty;
        }

        try
        {
            var tail = new Queue<string>();
            foreach (var line in File.ReadLines(path))
            {
                if (tail.Count == 160)
                {
                    tail.Dequeue();
                }

                tail.Enqueue(line);
            }

            var events = new List<SessionEvent>(tail.Count);
            foreach (var line in tail)
            {
                var sessionEvent = TryReadSessionEvent(line);
                if (sessionEvent is not null)
                {
                    events.Add(sessionEvent);
                }
            }

            var eventName = ResolveRecentEventName(events);
            var updatedAt = events.LastOrDefault(evt => evt.Timestamp is not null)?.Timestamp;
            return new SessionSignal(eventName, updatedAt);
        }
        catch
        {
            return SessionSignal.Empty;
        }
    }

    private static SessionSummary? TryReadCodexSession(string path, WorkspaceState workspaceState)
    {
        string? threadId = null;
        string? cwd = null;
        string? title = null;
        bool isSubagent = false;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                ApplySessionMetadata(line, ref threadId, ref cwd, ref title, ref isSubagent);
            }

            if (isSubagent)
            {
                return null;
            }

            var workingDirectory = NormalizePath(cwd);
            var projectRoot = FindProjectRoot(workingDirectory, workspaceState);
            if (string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(projectRoot))
            {
                return null;
            }

            var projectName = ShortName(projectRoot);
            var displayName = CleanTitle(title) ?? projectName;
            var eventInfo = TryReadRolloutSignal(path);
            var updatedAt = eventInfo.UpdatedAt ?? File.GetLastWriteTime(path);
            return new SessionSummary(
                threadId ?? Path.GetFileNameWithoutExtension(path),
                displayName,
                eventInfo.EventName,
                updatedAt,
                workingDirectory,
                projectRoot,
                projectName);
        }
        catch
        {
            return null;
        }
    }

    private static void ApplySessionMetadata(
        string line,
        ref string? threadId,
        ref string? cwd,
        ref string? title,
        ref bool isSubagent)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var payload = TryGetObject(root, "payload");

            threadId ??= TryGetString(payload, "id") ??
                         TryGetString(root, "session_id") ??
                         TryGetString(payload, "session_id");
            cwd ??= TryGetString(payload, "cwd") ??
                    TryGetString(payload, "working_directory") ??
                    TryGetString(root, "cwd");

            isSubagent |= string.Equals(TryGetString(payload, "thread_source"), "subagent", StringComparison.OrdinalIgnoreCase) ||
                          !string.IsNullOrWhiteSpace(TryGetString(payload, "parent_thread_id")) ||
                          IsSubagentSource(TryGetPropertyValue(payload, "source"));

            title ??= TryReadTitle(root, payload);
        }
        catch
        {
        }
    }

    private static bool IsSubagentSource(JsonElement? source)
    {
        return source is JsonElement value &&
               value.ValueKind == JsonValueKind.Object &&
               value.TryGetProperty("subagent", out _);
    }

    private static string? TryReadTitle(JsonElement root, JsonElement payload)
    {
        var payloadType = TryGetString(payload, "type");
        if (string.Equals(payloadType, "user_message", StringComparison.OrdinalIgnoreCase))
        {
            return TryGetString(payload, "message") ??
                   TryGetString(payload, "text") ??
                   TryGetContentText(payload);
        }

        if (!string.Equals(TryGetString(payload, "role"), "user", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(payloadType, "message", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return TryGetString(payload, "text") ??
               TryGetString(payload, "message") ??
               TryGetNestedString(payload, "message", "content") ??
               TryGetContentText(payload);
    }

    private static string? TryGetContentText(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var part in content.EnumerateArray())
        {
            var partType = TryGetString(part, "type");
            if (!string.Equals(partType, "input_text", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(partType, "output_text", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = TryGetString(part, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static SessionEvent? TryReadSessionEvent(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var payload = TryGetObject(root, "payload");
            var eventName = new[]
            {
                TryGetString(payload, "type"),
                TryGetString(root, "event"),
                TryGetString(root, "type"),
                TryGetNestedString(root, "msg", "type")
            }.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && IsMeaningfulEvent(candidate));

            var timestamp = TryGetDate(root, "timestamp") ??
                            TryGetDate(payload, "timestamp") ??
                            TryGetDate(payload, "started_at");
            var turnId = TryGetString(payload, "turn_id") ??
                         TryGetString(root, "turn_id") ??
                         TryGetNestedString(payload, "metadata", "turn_id") ??
                         TryGetNestedString(root, "metadata", "turn_id");

            return eventName is null && timestamp is null && turnId is null
                ? null
                : new SessionEvent(eventName, timestamp, turnId);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveRecentEventName(IReadOnlyList<SessionEvent> events)
    {
        if (events.Count == 0)
        {
            return null;
        }

        var latestTurnId = events.LastOrDefault(evt => !string.IsNullOrWhiteSpace(evt.TurnId))?.TurnId;
        if (!string.IsNullOrWhiteSpace(latestTurnId))
        {
            var latestTurnEvents = events
                .Where(evt => string.Equals(evt.TurnId, latestTurnId, StringComparison.Ordinal))
                .ToList();
            var turnEventName = ResolveTurnEventName(latestTurnEvents);
            if (!string.IsNullOrWhiteSpace(turnEventName))
            {
                return turnEventName;
            }
        }

        return events.LastOrDefault(evt => !string.IsNullOrWhiteSpace(evt.EventName))?.EventName;
    }

    private static string? ResolveTurnEventName(IReadOnlyList<SessionEvent> turnEvents)
    {
        if (turnEvents.Count == 0)
        {
            return null;
        }

        var lastCompletionIndex = FindLastIndex(turnEvents, evt => IsCompletionSignal(evt.EventName));
        var lastActiveIndex = FindLastIndex(turnEvents, evt => IsActiveSignal(evt.EventName));

        if (lastActiveIndex > lastCompletionIndex)
        {
            return turnEvents[lastActiveIndex].EventName;
        }

        if (lastCompletionIndex >= 0)
        {
            return turnEvents[lastCompletionIndex].EventName;
        }

        if (lastActiveIndex >= 0)
        {
            return turnEvents[lastActiveIndex].EventName;
        }

        return turnEvents.LastOrDefault(evt => !string.IsNullOrWhiteSpace(evt.EventName))?.EventName;
    }

    private static int FindLastIndex(IReadOnlyList<SessionEvent> events, Func<SessionEvent, bool> predicate)
    {
        for (var index = events.Count - 1; index >= 0; index--)
        {
            if (predicate(events[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsCompletionSignal(string? eventName)
        => ProjectSignalMapper.FromEvent(eventName) == ProjectSignal.Completed;

    private static bool IsActiveSignal(string? eventName)
    {
        var signal = ProjectSignalMapper.FromEvent(eventName);
        return signal is ProjectSignal.Thinking
            or ProjectSignal.Working
            or ProjectSignal.ToolDone
            or ProjectSignal.Permission
            or ProjectSignal.Attention
            or ProjectSignal.Blocked;
    }

    private static bool IsMeaningfulEvent(string eventName)
    {
        var normalized = new string(eventName
            .Trim()
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '_')
            .ToArray());

        return normalized is not ""
            and not "token_count"
            and not "rate_limits"
            and not "context_compacted"
            and not "message"
            and not "event_msg"
            and not "response_item"
            and not "turn_context"
            and not "agent_message";
    }

    private static ProjectStatusSnapshot? TryReadStatusFile(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var eventName = TryGetString(root, "last_event") ??
                            TryGetString(root, "event") ??
                            TryGetString(root, "aggregate") ??
                            TryGetString(root, "signal");
            var signal = TryGetString(root, "signal") is string rawSignal
                ? ProjectSignalMapper.FromEvent(rawSignal)
                : ProjectSignalMapper.FromEvent(eventName);
            var updatedAt = TryGetDate(root, "updated_at") ?? File.GetLastWriteTime(path);
            var isFresh = DateTimeOffset.Now - updatedAt <= FreshnessWindow;

            if (!isFresh && signal is not ProjectSignal.Completed)
            {
                signal = ProjectSignal.Stale;
            }

            return new ProjectStatusSnapshot(
                TryGetString(root, "project_id") ?? "codex",
                TryGetString(root, "display_name") ?? "Codex",
                signal,
                eventName,
                updatedAt,
                isFresh,
                TryGetString(root, "working_directory"),
                TryGetString(root, "project_root"),
                TryGetString(root, "project_name") ?? "Codex");
        }
        catch
        {
            return ProjectStatusSnapshot.Stale("status file unreadable");
        }
    }

    private static long? TryGetLong(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt64(out var number)
            ? number
            : null;
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static JsonElement TryGetObject(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Object
            ? value
            : default;
    }

    private static JsonElement? TryGetPropertyValue(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value)
            ? value
            : null;
    }

    private static string? TryGetNestedString(JsonElement element, string parent, string child)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(parent, out var parentElement)
            ? TryGetString(parentElement, child)
            : null;
    }

    private static DateTimeOffset? TryGetDate(JsonElement element, string name)
    {
        var raw = TryGetString(element, name);
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? FromUnixTimestamp(long? raw)
    {
        if (!raw.HasValue || raw <= 0)
        {
            return null;
        }

        try
        {
            return raw.Value >= 1_000_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(raw.Value).ToLocalTime()
                : DateTimeOffset.FromUnixTimeSeconds(raw.Value).ToLocalTime();
        }
        catch
        {
            return null;
        }
    }

    private static string ShortName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Codex project";
        }

        var cleaned = NormalizePath(value)?.Replace("\\", "/").Trim('/');
        var last = cleaned?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrWhiteSpace(last) ? "Codex project" : last;
    }

    private static string? FindProjectRoot(string? workingDirectory, WorkspaceState state)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return null;
        }

        return state.OrderIndex.Keys
            .Where(root => workingDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(root => root.Length)
            .FirstOrDefault();
    }

    private static string? NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith(@"\\?\"))
        {
            normalized = normalized[4..];
        }

        return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string? CleanTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var firstLine = value
            .Replace("\r", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return null;
        }

        if (firstLine.StartsWith("Automation:", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("<environment_context>", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("<permissions instructions>", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("<app-context>", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("<collaboration_mode>", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("<skills_instructions>", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("<plugins_instructions>", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("# AGENTS.md instructions", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return firstLine.Length <= 96 ? firstLine : firstLine[..96] + "...";
    }

    private static string ResolveUserHome()
    {
        var env = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private sealed record ThreadRow(
        string Id,
        string? Cwd,
        string? Title,
        string? RolloutPath,
        string? ThreadSource,
        long? UpdatedAtUnix);

    private sealed record SessionSummary(
        string ThreadId,
        string Title,
        string? EventName,
        DateTimeOffset UpdatedAt,
        string WorkingDirectory,
        string ProjectRoot,
        string ProjectName);

    private sealed record SessionSignal(string? EventName, DateTimeOffset? UpdatedAt)
    {
        public static SessionSignal Empty { get; } = new(null, null);
    }

    private sealed record SessionEvent(string? EventName, DateTimeOffset? Timestamp, string? TurnId);

    private sealed record WorkspaceState(
        IReadOnlyDictionary<string, int> OrderIndex,
        IReadOnlySet<string> ActiveRoots)
    {
        public static WorkspaceState Empty { get; } = new(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }
}
