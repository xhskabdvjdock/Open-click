using OpenClick.Core.Tester;

namespace OpenClick.Core;

/// <summary>Aggregated test-session summary (copyable / exportable).</summary>
public sealed class TestSessionSummary
{
    public TimeSpan Duration { get; init; }
    public Dictionary<MouseButton, long> ClicksPerButton { get; init; } = new();
    public long TotalClicks { get; init; }
    public double AverageIntervalMs { get; init; }
    public long FastestIntervalMs { get; init; }
    public long SlowestIntervalMs { get; init; }
    public int DoubleClickIntervalsMeasured { get; init; }
    public long SuspiciousRapidClicks { get; init; }
    public long ScrollEvents { get; init; }

    public string ToClipboardText()
    {
        var lines = new List<string>
        {
            "Open Click - Mouse Test Session Summary",
            $"Duration: {Duration}",
            $"Total completed clicks: {TotalClicks}",
        };
        foreach (var kv in ClicksPerButton)
            lines.Add($"{kv.Key}: {kv.Value}");
        lines.Add($"Double-click intervals measured: {DoubleClickIntervalsMeasured}");
        lines.Add($"Average interval: {AverageIntervalMs:F1} ms");
        lines.Add($"Fastest: {FastestIntervalMs} ms, Slowest: {SlowestIntervalMs} ms");
        lines.Add($"Suspicious rapid clicks (<threshold): {SuspiciousRapidClicks}");
        lines.Add($"Scroll events: {ScrollEvents}");
        return string.Join(Environment.NewLine, lines);
    }

    public string ToCsv()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("metric,value");
        sb.AppendLine($"duration_seconds,{Duration.TotalSeconds:F1}");
        sb.AppendLine($"total_clicks,{TotalClicks}");
        foreach (var kv in ClicksPerButton)
            sb.AppendLine($"clicks_{kv.Key.ToString().ToLowerInvariant()},{kv.Value}");
        sb.AppendLine($"intervals_measured,{DoubleClickIntervalsMeasured}");
        sb.AppendLine($"avg_interval_ms,{AverageIntervalMs:F1}");
        sb.AppendLine($"fastest_ms,{FastestIntervalMs}");
        sb.AppendLine($"slowest_ms,{SlowestIntervalMs}");
        sb.AppendLine($"suspicious,{SuspiciousRapidClicks}");
        sb.AppendLine($"scrolls,{ScrollEvents}");
        return sb.ToString();
    }
}
