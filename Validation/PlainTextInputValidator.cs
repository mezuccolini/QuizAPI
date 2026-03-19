using System.Text.RegularExpressions;

namespace QuizAPI.Validation
{
    public static class PlainTextInputValidator
    {
        private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex PersonNameRegex = new(@"^[A-Za-z]+(?: [A-Za-z]+)*$", RegexOptions.Compiled);

        public static bool TryNormalizePersonName(
            string? value,
            string fieldName,
            bool required,
            out string normalized,
            out string? error)
        {
            normalized = NormalizeWhitespace(value);
            error = null;

            if (string.IsNullOrWhiteSpace(normalized))
            {
                if (required)
                {
                    error = $"{fieldName} is required.";
                    return false;
                }

                normalized = string.Empty;
                return true;
            }

            if (normalized.Length > 50)
            {
                error = $"{fieldName} must be 50 characters or fewer.";
                return false;
            }

            if (!PersonNameRegex.IsMatch(normalized))
            {
                error = $"{fieldName} can only contain letters and spaces.";
                return false;
            }

            return true;
        }

        private static string NormalizeWhitespace(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return MultiWhitespaceRegex.Replace(value.Trim(), " ");
        }
    }
}
