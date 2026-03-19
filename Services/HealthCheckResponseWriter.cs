using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace QuizAPI.Services;

public static class HealthCheckResponseWriter
{
    public static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    duration = entry.Value.Duration.TotalMilliseconds,
                    error = entry.Value.Exception?.Message
                }),
            traceId = context.TraceIdentifier
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
