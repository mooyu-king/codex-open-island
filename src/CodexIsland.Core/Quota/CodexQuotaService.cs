using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexIsland.Core.Models;

namespace CodexIsland.Core.Quota;

public sealed class CodexQuotaService : IQuotaService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);

    public async Task<QuotaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            using var client = new StdioJsonRpcClient(ResolveCodexPath());
            await client.StartAsync(timeout.Token).ConfigureAwait(false);
            await client.SendAsync("initialize", new
            {
                clientInfo = new
                {
                    name = "codex-island",
                    title = "Codex Island",
                    version = "0.1.0"
                },
                capabilities = (object?)null
            }, timeout.Token).ConfigureAwait(false);

            using var result = await client.SendAsync("account/rateLimits/read", null, timeout.Token)
                .ConfigureAwait(false);

            return Normalize(result.RootElement);
        }
        catch (OperationCanceledException)
        {
            return QuotaSnapshot.Error("Codex quota request timed out.", "timeout");
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("authentication required", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("not logged in", StringComparison.OrdinalIgnoreCase))
            {
                return QuotaSnapshot.Error("Codex login required. Run codex login.", "auth_required");
            }

            return QuotaSnapshot.Error(ex.Message, ex.GetType().Name);
        }
    }

    private static string ResolveCodexPath()
    {
        var env = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
        {
            return env;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var npmCmd = Path.Combine(appData, "npm", "codex.cmd");
        if (File.Exists(npmCmd))
        {
            return npmCmd;
        }

        var npmPs1 = Path.Combine(appData, "npm", "codex.ps1");
        if (File.Exists(npmPs1))
        {
            return npmPs1;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidate = Path.Combine(localAppData, "OpenAI", "Codex", "bin", "codex.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var onPath = ResolveOnPath("codex.cmd")
                     ?? ResolveOnPath("codex.bat")
                     ?? ResolveOnPath("codex.exe");
        if (onPath is not null)
        {
            return onPath;
        }

        var windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            "codex.exe");
        if (File.Exists(windowsApps))
        {
            return windowsApps;
        }

        return "codex.exe";
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
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static QuotaSnapshot Normalize(JsonElement root)
    {
        var snapshot = TryGetCodexSnapshot(root);
        if (snapshot is null)
        {
            return QuotaSnapshot.Error("Codex did not return a rate-limit snapshot.", "no_snapshot");
        }

        var primary = TryGetProperty(snapshot.Value, "primary");
        var secondary = TryGetProperty(snapshot.Value, "secondary");
        var activeWindow = primary ?? secondary;
        int? usedPercent = null;
        int? remainingPercent = null;
        DateTimeOffset? resetsAt = null;
        int? weeklyUsedPercent = null;
        int? weeklyRemainingPercent = null;
        DateTimeOffset? weeklyResetsAt = null;

        if (activeWindow is JsonElement window)
        {
            usedPercent = TryGetDouble(window, "usedPercent") is double used
                ? QuotaHealthMapper.ClampPercent(used)
                : 0;
            remainingPercent = Math.Clamp(100 - usedPercent.Value, 0, 100);
            resetsAt = TryGetUnixSeconds(window, "resetsAt");
        }

        if (secondary is JsonElement weeklyWindow)
        {
            weeklyUsedPercent = TryGetDouble(weeklyWindow, "usedPercent") is double weeklyUsed
                ? QuotaHealthMapper.ClampPercent(weeklyUsed)
                : 0;
            weeklyRemainingPercent = Math.Clamp(100 - weeklyUsedPercent.Value, 0, 100);
            weeklyResetsAt = TryGetUnixSeconds(weeklyWindow, "resetsAt");
        }

        var limitId = TryGetString(snapshot.Value, "limitId") ?? "codex";
        var limitName = TryGetString(snapshot.Value, "limitName") ?? "Codex";
        var planType = TryGetString(snapshot.Value, "planType") ?? "unknown";
        var health = QuotaHealthMapper.FromRemainingPercent(remainingPercent);

        return new QuotaSnapshot(
            limitId,
            limitName,
            planType,
            remainingPercent,
            usedPercent,
            resetsAt,
            weeklyRemainingPercent,
            weeklyUsedPercent,
            weeklyResetsAt,
            DateTimeOffset.Now,
            health);
    }

    private static JsonElement? TryGetCodexSnapshot(JsonElement root)
    {
        if (TryGetProperty(root, "rateLimitsByLimitId") is JsonElement byId)
        {
            if (TryGetProperty(byId, "codex") is JsonElement codex)
            {
                return codex;
            }

            foreach (var item in byId.EnumerateObject())
            {
                return item.Value;
            }
        }

        return TryGetProperty(root, "rateLimits");
    }

    private static JsonElement? TryGetProperty(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : null;
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        return TryGetProperty(element, name) is JsonElement value && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static double? TryGetDouble(JsonElement element, string name)
    {
        if (TryGetProperty(element, name) is not JsonElement value)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;
    }

    private static DateTimeOffset? TryGetUnixSeconds(JsonElement element, string name)
    {
        if (TryGetProperty(element, name) is JsonElement value &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
        }

        return null;
    }

    private sealed class StdioJsonRpcClient : IDisposable
    {
        private readonly Process _process;
        private int _nextId;

        public StdioJsonRpcClient(string executable)
        {
            var startInfo = CreateStartInfo(executable);
            _process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
        }

        private static ProcessStartInfo CreateStartInfo(string executable)
        {
            var extension = Path.GetExtension(executable).ToLowerInvariant();
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (extension == ".ps1")
            {
                startInfo.FileName = "powershell.exe";
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(executable);
                startInfo.ArgumentList.Add("app-server");
                startInfo.ArgumentList.Add("--listen");
                startInfo.ArgumentList.Add("stdio://");
                return startInfo;
            }

            startInfo.FileName = executable;
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--listen");
            startInfo.ArgumentList.Add("stdio://");
            return startInfo;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _process.Start();
            return Task.CompletedTask;
        }

        public async Task<JsonDocument> SendAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextId);
            var payload = parameters is null
                ? JsonSerializer.Serialize(new { id, method })
                : JsonSerializer.Serialize(new { id, method, @params = parameters });

            await _process.StandardInput.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    var error = await ReadErrorAsync().ConfigureAwait(false);
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? "Codex app-server exited before responding."
                        : error);
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var errorElement))
                {
                    throw new InvalidOperationException(ExtractError(errorElement));
                }

                if (root.TryGetProperty("result", out var result))
                {
                    return JsonDocument.Parse(result.GetRawText());
                }

                throw new InvalidOperationException("Codex returned a response without result.");
            }

            throw new OperationCanceledException(cancellationToken);
        }

        private async Task<string> ReadErrorAsync()
        {
            try
            {
                var builder = new StringBuilder();
                while (!_process.StandardError.EndOfStream)
                {
                    builder.AppendLine(await _process.StandardError.ReadLineAsync().ConfigureAwait(false));
                }

                return builder.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string ExtractError(JsonElement error)
        {
            return error.ValueKind == JsonValueKind.Object &&
                   error.TryGetProperty("message", out var message) &&
                   message.ValueKind == JsonValueKind.String
                ? message.GetString() ?? "Codex returned an error."
                : error.GetRawText();
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }

            _process.Dispose();
        }
    }
}
