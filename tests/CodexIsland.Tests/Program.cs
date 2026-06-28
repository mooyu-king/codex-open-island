using CodexIsland.Core.Models;
using CodexIsland.Core.Quota;
using CodexIsland.Core.Signals;
using System.Text.Json;

if (args.Contains("--live-quota"))
{
    var snapshot = await new CodexQuotaService().GetSnapshotAsync();
    Console.WriteLine($"quota health={snapshot.Health} remaining={snapshot.RemainingPercent?.ToString() ?? "null"} error={snapshot.ErrorCode ?? ""} {snapshot.ErrorMessage ?? ""}");
    return snapshot.Health == QuotaHealth.Error ? 2 : 0;
}

if (args.Contains("--list-projects"))
{
    var projects = new LocalProjectSignalService().GetRecentProjects();
    Console.WriteLine($"projects={projects.Count}");
    foreach (var project in projects)
    {
        Console.WriteLine($"{project.Signal} | {project.DisplayName} | {project.LastEvent ?? "-"} | {project.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
    }

    return projects.Count == 0 ? 2 : 0;
}

if (args.Contains("--env-paths"))
{
    var envUserProfile = Environment.GetEnvironmentVariable("USERPROFILE") ?? "<null>";
    var specialUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var sessionRoot = Path.Combine(envUserProfile, ".codex", "sessions");
    Console.WriteLine($"env_userprofile={envUserProfile}");
    Console.WriteLine($"special_userprofile={specialUserProfile}");
    Console.WriteLine($"session_root={sessionRoot}");
    Console.WriteLine($"session_root_exists={Directory.Exists(sessionRoot)}");
    return 0;
}

var tests = new (string Name, Action Body)[]
{
    ("quota maps 10 percent to green", () => AssertEqual(QuotaHealth.Green, QuotaHealthMapper.FromRemainingPercent(10))),
    ("quota maps 9 percent to yellow", () => AssertEqual(QuotaHealth.Yellow, QuotaHealthMapper.FromRemainingPercent(9))),
    ("quota maps 0 percent to red", () => AssertEqual(QuotaHealth.Red, QuotaHealthMapper.FromRemainingPercent(0))),
    ("codex pre tool maps to working", () => AssertEqual(ProjectSignal.Working, ProjectSignalMapper.FromEvent("PreToolUse"))),
    ("status file working maps to working", () => AssertEqual(ProjectSignal.Working, ProjectSignalMapper.FromEvent("working"))),
    ("codex stop maps to completed", () => AssertEqual(ProjectSignal.Completed, ProjectSignalMapper.FromEvent("Stop"))),
    ("codex generic message is not completed", () => AssertEqual(ProjectSignal.Ready, ProjectSignalMapper.FromEvent("message"))),
    ("codex function output maps to tool done", () => AssertEqual(ProjectSignal.ToolDone, ProjectSignalMapper.FromEvent("custom_tool_call_output"))),
    ("codex task started maps to thinking", () => AssertEqual(ProjectSignal.Thinking, ProjectSignalMapper.FromEvent("task_started"))),
    ("active codex turn stays working", AssertActiveTurnStaysWorking),
    ("completed and permission trigger persistent bounce", AssertPersistentBounce)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failures == 0 ? 0 : 1;

static void AssertPersistentBounce()
{
    var detector = new CompletionTransitionDetector();
    AssertFalse(detector.ShouldStartPersistentBounce(ProjectSignal.Completed), "startup completed should not bounce");
    AssertFalse(detector.ShouldStartPersistentBounce(ProjectSignal.Completed), "same completed should not bounce again");
    AssertFalse(detector.ShouldStartPersistentBounce(ProjectSignal.Working), "working should not bounce");
    AssertTrue(detector.ShouldStartPersistentBounce(ProjectSignal.Completed), "transition into completed should bounce");
    AssertFalse(detector.ShouldStartPersistentBounce(ProjectSignal.Ready), "ready should not bounce");
    AssertTrue(detector.ShouldStartPersistentBounce(ProjectSignal.Completed), "new transition after ack should bounce");

    detector = new CompletionTransitionDetector();
    AssertFalse(detector.ShouldStartPersistentBounce(ProjectSignal.Permission), "startup permission should not bounce");
    AssertFalse(detector.ShouldStartPersistentBounce(ProjectSignal.Permission), "same permission should not bounce again");
    AssertFalse(detector.ShouldStartPersistentBounce(ProjectSignal.Working), "working should not bounce");
    AssertTrue(detector.ShouldStartPersistentBounce(ProjectSignal.Permission), "transition into permission should bounce");
    detector.Acknowledge(ProjectSignal.Working);
    AssertTrue(detector.ShouldStartPersistentBounce(ProjectSignal.Permission), "permission after ack should bounce");
}

static void AssertActiveTurnStaysWorking()
{
    var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
    var tempRoot = Path.Combine(Path.GetTempPath(), "codex-island-tests", Guid.NewGuid().ToString("N"));
    var sessionDirectory = Path.Combine(tempRoot, ".codex", "sessions", "2026", "06", "21");
    var codexDirectory = Path.Combine(tempRoot, ".codex");
    Directory.CreateDirectory(sessionDirectory);
    Directory.CreateDirectory(codexDirectory);

    try
    {
        var now = DateTimeOffset.UtcNow;
        var sessionFile = Path.Combine(sessionDirectory, "rollout-test.jsonl");
        File.WriteAllText(
            Path.Combine(codexDirectory, ".codex-global-state.json"),
            """
            {
              "project-order": ["D:/demo/workspace"],
              "active-workspace-roots": ["D:/demo/workspace"]
            }
            """);
        File.WriteAllLines(sessionFile, new[]
        {
            JsonSerializer.Serialize(new
            {
                timestamp = now.AddSeconds(-6).ToString("O"),
                type = "session_meta",
                payload = new { id = "session-1", cwd = "D:/demo/workspace" }
            }),
            JsonSerializer.Serialize(new
            {
                timestamp = now.AddSeconds(-5).ToString("O"),
                type = "event_msg",
                payload = new { type = "task_complete", turn_id = "turn-old" }
            }),
            JsonSerializer.Serialize(new
            {
                timestamp = now.AddSeconds(-4).ToString("O"),
                type = "event_msg",
                payload = new
                {
                    type = "task_started",
                    turn_id = "turn-new",
                    started_at = now.AddSeconds(-4).ToUnixTimeSeconds()
                }
            }),
            JsonSerializer.Serialize(new
            {
                timestamp = now.AddSeconds(-3).ToString("O"),
                type = "response_item",
                payload = new { type = "reasoning", metadata = new { turn_id = "turn-new" } }
            }),
            JsonSerializer.Serialize(new
            {
                timestamp = now.AddSeconds(-2).ToString("O"),
                type = "response_item",
                payload = new { type = "function_call", metadata = new { turn_id = "turn-new" } }
            }),
            JsonSerializer.Serialize(new
            {
                timestamp = now.AddSeconds(-1).ToString("O"),
                type = "response_item",
                payload = new
                {
                    type = "message",
                    role = "assistant",
                    metadata = new { turn_id = "turn-new" },
                    content = new[] { new { type = "output_text", text = "still working" } }
                }
            })
        });

        Environment.SetEnvironmentVariable("USERPROFILE", tempRoot);
        var project = new LocalProjectSignalService().GetRecentProjects(1).Single();
        AssertEqual(ProjectSignal.Working, project.Signal);
    }
    finally
    {
        Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected {expected}, got {actual}");
    }
}

static void AssertTrue(bool value, string message)
{
    if (!value)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertFalse(bool value, string message)
{
    if (value)
    {
        throw new InvalidOperationException(message);
    }
}
