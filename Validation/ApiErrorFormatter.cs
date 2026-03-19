using Microsoft.AspNetCore.Identity;

namespace QuizAPI.Validation
{
    public static class ApiErrorFormatter
    {
        public static string SummarizeIdentityErrors(IEnumerable<IdentityError> errors, string fallbackMessage)
        {
            var descriptions = (errors ?? Enumerable.Empty<IdentityError>())
                .Select(e => e.Description?.Trim())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return descriptions.Count > 0
                ? string.Join("; ", descriptions)
                : fallbackMessage;
        }
    }
}
