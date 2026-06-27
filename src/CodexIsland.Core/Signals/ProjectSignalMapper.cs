using CodexIsland.Core.Models;

namespace CodexIsland.Core.Signals;

public static class ProjectSignalMapper
{
    public static ProjectSignal FromEvent(string? eventName, bool hasStructuredFailure = false)
    {
        if (hasStructuredFailure)
        {
            return ProjectSignal.Blocked;
        }

        var normalized = Normalize(eventName);
        return normalized switch
        {
            "ready" or "idle" or "sessionstart" or "session_start" or "session_meta" => ProjectSignal.Ready,
            "thinking" or "userpromptsubmit" or "user_prompt_submit" or "reasoning" or "task_started" or "user_message" => ProjectSignal.Thinking,
            "working" or "active" or "pretooluse" or "pre_tool_use" or "function_call" or "custom_tool_call" => ProjectSignal.Working,
            "tooldone" or "tool_done" or "posttooluse" or "post_tool_use" or "function_call_output" or "custom_tool_call_output" or "patch_apply_end" => ProjectSignal.ToolDone,
            "permissionrequest" or "permission_request" or "approvalrequired" or "approval_requested" or "exec_approval_request" => ProjectSignal.Permission,
            "attention" or "notification" or "needs_review" => ProjectSignal.Attention,
            "blocked" or "failure" or "error" or "turn_aborted" => ProjectSignal.Blocked,
            "stop" or "done" or "task_complete" or "final_answer" or "completed" or "turn_completed" => ProjectSignal.Completed,
            "pause" or "paused" or "off" => ProjectSignal.Paused,
            _ => ProjectSignal.Ready
        };
    }

    public static string DisplayName(ProjectSignal signal)
    {
        return signal switch
        {
            ProjectSignal.Ready => "Ready",
            ProjectSignal.Thinking => "Thinking",
            ProjectSignal.Working => "Working",
            ProjectSignal.ToolDone => "Tool done",
            ProjectSignal.Permission => "Needs permission",
            ProjectSignal.Attention => "Needs attention",
            ProjectSignal.Blocked => "Blocked",
            ProjectSignal.Completed => "Completed",
            ProjectSignal.Stale => "Status stale",
            ProjectSignal.Paused => "Paused",
            _ => "Unknown"
        };
    }

    private static string Normalize(string? eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return "";
        }

        return new string(eventName
            .Trim()
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '_')
            .ToArray());
    }
}
