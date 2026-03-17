using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using System.Globalization;
using System.Text.Json;
using System.Text;
using System.IO.Compression;

namespace QuizAPI.Services
{
    public class QuizImportService
    {
        private readonly IWebHostEnvironment _env;
        private readonly QuizDbContext _db;

        public QuizImportService(IWebHostEnvironment env, QuizDbContext db)
        {
            _env = env;
            _db = db;
        }

        private sealed class ImportRunRecord
        {
            public DateTime ImportedUtc { get; set; }
            public string FileName { get; set; } = "";
            public int Rows { get; set; }
            public int Quizzes { get; set; }
            public int Questions { get; set; }
            public int Answers { get; set; }
            public string Status { get; set; } = "";
            public string Message { get; set; } = "";
        }

        private string GetImportHistoryPath()
        {
            var root = Path.Combine(_env.ContentRootPath, "App_Data");
            Directory.CreateDirectory(root);
            return Path.Combine(root, "import_history.jsonl");
        }

        private void AppendImportHistory(ImportRunRecord run)
        {
            var line = JsonSerializer.Serialize(run);
            File.AppendAllText(GetImportHistoryPath(), line + Environment.NewLine, Encoding.UTF8);
        }

        public IEnumerable<object> ReadImportHistory(int take = 50)
        {
            var path = GetImportHistoryPath();
            if (!File.Exists(path))
                return Array.Empty<object>();

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            var parsed = new List<ImportRunRecord>();
            foreach (var l in lines)
            {
                if (string.IsNullOrWhiteSpace(l))
                    continue;

                try
                {
                    var run = JsonSerializer.Deserialize<ImportRunRecord>(l);
                    if (run != null)
                        parsed.Add(run);
                }
                catch
                {
                }
            }

            return parsed
                .OrderByDescending(r => r.ImportedUtc)
                .Take(take)
                .Select(r => new
                {
                    r.ImportedUtc,
                    r.FileName,
                    r.Status,
                    r.Message,
                    r.Rows,
                    r.Quizzes,
                    r.Questions,
                    r.Answers
                })
                .ToList();
        }

        public async Task<string> SaveUploadAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("No file received.");

            var uploadsRoot = GetPrivateUploadsRoot();
            Directory.CreateDirectory(uploadsRoot);

            var safeName = Path.GetFileName(file.FileName);
            var outPath = Path.Combine(uploadsRoot, $"{Guid.NewGuid()}_{safeName}");

            using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                await file.CopyToAsync(fs);

            return $"Saved import file: {Path.GetFileName(outPath)}";
        }

        public async Task<object> SaveUploadPackageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("No file received.");

            var safeName = Path.GetFileName(file.FileName);
            if (!safeName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Package upload must be a .zip file.");

            var privateUploadsRoot = GetPrivateUploadsRoot();
            Directory.CreateDirectory(privateUploadsRoot);

            var packageId = Guid.NewGuid().ToString("N");

            var zipPath = Path.Combine(privateUploadsRoot, $"{packageId}_{safeName}");
            using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                await file.CopyToAsync(fs);

            var publicUploadsRoot = GetPublicUploadsRoot();
            var imagesDir = Path.Combine(publicUploadsRoot, "images", packageId);
            Directory.CreateDirectory(imagesDir);

            string? csvEntryName = null;
            string? csvSavedFileName = null;
            int imagesSaved = 0;

            using (var zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false))
            {
                var csvEntries = archive.Entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.Name) && e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (csvEntries.Count != 1)
                    throw new InvalidOperationException("Package must contain exactly one .csv file.");

                var csvEntry = csvEntries[0];
                csvEntryName = csvEntry.Name;
                csvSavedFileName = $"{packageId}_{Path.GetFileName(csvEntry.Name)}";
                var csvOutPath = Path.Combine(privateUploadsRoot, csvSavedFileName);

                using (var inStream = csvEntry.Open())
                using (var outStream = new FileStream(csvOutPath, FileMode.Create, FileAccess.Write))
                    await inStream.CopyToAsync(outStream);

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name))
                        continue;

                    var ext = Path.GetExtension(entry.Name);
                    if (string.IsNullOrWhiteSpace(ext))
                        continue;

                    if (!IsSupportedImageExtension(ext))
                        continue;

                    var fileNameOnly = Path.GetFileName(entry.Name);
                    if (string.IsNullOrWhiteSpace(fileNameOnly))
                        continue;

                    var outFileName = EnsureUniqueFileName(imagesDir, fileNameOnly);
                    var outPath = Path.Combine(imagesDir, outFileName);

                    using (var inStream = entry.Open())
                    using (var outStream = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                        await inStream.CopyToAsync(outStream);

                    imagesSaved++;
                }
            }

            return new
            {
                PackageId = packageId,
                ZipFileName = Path.GetFileName(zipPath),
                CsvFileName = csvSavedFileName ?? "",
                ImageBaseUrl = $"/uploads/images/{packageId}/",
                ImagesSaved = imagesSaved,
                CsvEntry = csvEntryName ?? ""
            };
        }

        private static bool IsSupportedImageExtension(string ext)
        {
            return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".svg", StringComparison.OrdinalIgnoreCase);
        }

        private static string EnsureUniqueFileName(string directory, string desiredFileName)
        {
            var safe = Path.GetFileName(desiredFileName);
            var baseName = Path.GetFileNameWithoutExtension(safe);
            var ext = Path.GetExtension(safe);

            var candidate = safe;
            int i = 1;
            while (File.Exists(Path.Combine(directory, candidate)))
            {
                candidate = $"{baseName}_{i}{ext}";
                i++;
            }

            return candidate;
        }

        private static IEnumerable<string[]> ReadCsvRecords(string filePath)
        {
            using var sr = new StreamReader(filePath);
            var record = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            while (true)
            {
                int chInt = sr.Read();
                if (chInt == -1)
                {
                    if (inQuotes)
                        throw new InvalidOperationException("CSV ended while inside a quoted field.");

                    if (field.Length > 0 || record.Count > 0)
                    {
                        record.Add(field.ToString());
                        yield return record.ToArray();
                    }
                    yield break;
                }

                char ch = (char)chInt;

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        int peek = sr.Peek();
                        if (peek == '"')
                        {
                            sr.Read();
                            field.Append('"');
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(ch);
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = true;
                    continue;
                }

                if (ch == ',')
                {
                    record.Add(field.ToString());
                    field.Clear();
                    continue;
                }

                if (ch == '\r')
                {
                    if (sr.Peek() == '\n')
                        sr.Read();

                    record.Add(field.ToString());
                    field.Clear();
                    yield return record.ToArray();
                    record.Clear();
                    continue;
                }

                if (ch == '\n')
                {
                    record.Add(field.ToString());
                    field.Clear();
                    yield return record.ToArray();
                    record.Clear();
                    continue;
                }

                field.Append(ch);
            }
        }

        private static bool ParseBoolLoose(string value)
        {
            var v = (value ?? string.Empty).Trim();
            return v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("t", StringComparison.OrdinalIgnoreCase)
                || v.Equals("1", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || v.Equals("y", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string> ProcessCsvAsync(string fileName)
        {
            var run = new ImportRunRecord
            {
                ImportedUtc = DateTime.UtcNow,
                FileName = fileName
            };

            try
            {
                var uploadsRoot = GetPrivateUploadsRoot();
                var filePath = GetSafeChildPath(uploadsRoot, fileName);

                if (filePath == null)
                    throw new InvalidOperationException("Invalid file name.");

                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {fileName}");

                var records = ReadCsvRecords(filePath).ToList();
                if (records.Count <= 1)
                    throw new InvalidOperationException("CSV appears empty or has no data rows.");

                var header = records[0].Select(h => (h ?? string.Empty).Trim()).ToArray();
                int quizIdx = Array.IndexOf(header, "QuizTitle");
                int categoryIdx = Array.IndexOf(header, "Category");
                int questionImgKeyIdx = Array.IndexOf(header, "QuestionImgKey");
                int questionIdx = Array.IndexOf(header, "QuestionText");
                int answerIdx = Array.IndexOf(header, "AnswerText");
                int correctIdx = Array.IndexOf(header, "IsCorrect");

                if (quizIdx < 0 || questionIdx < 0 || answerIdx < 0 || correctIdx < 0)
                    throw new InvalidOperationException("CSV missing required columns (QuizTitle, QuestionText, AnswerText, IsCorrect). Optional columns include Category and QuestionImgKey.");

                var rows = records.Skip(1)
                    .Where(c => c.Length > Math.Max(Math.Max(quizIdx, questionIdx), Math.Max(answerIdx, correctIdx)))
                    .Select(c => new
                    {
                        QuizTitle = (c[quizIdx] ?? string.Empty).Trim(),
                        Category = categoryIdx >= 0 && categoryIdx < c.Length ? (c[categoryIdx] ?? string.Empty).Trim() : "",
                        QuestionImgKey = questionImgKeyIdx >= 0 && questionImgKeyIdx < c.Length ? (c[questionImgKeyIdx] ?? string.Empty).Trim() : "",
                        QuestionText = (c[questionIdx] ?? string.Empty).Trim(),
                        AnswerText = (c[answerIdx] ?? string.Empty).Trim(),
                        IsCorrect = ParseBoolLoose(correctIdx >= 0 && correctIdx < c.Length ? c[correctIdx] : "")
                    })
                    .ToList();

                var grouped = rows.GroupBy(r => new { Title = r.QuizTitle, Category = NormalizeCategory(r.Category) });
                var packageId = TryGetPackageIdFromImportedFile(fileName);
                var packageImagesDir = GetPackageImagesDirectory(packageId);

                int quizCount = 0, questionCount = 0, answerCount = 0;

                foreach (var quizGroup in grouped)
                {
                    var quizTitle = quizGroup.Key.Title;
                    var quizCategory = quizGroup.Key.Category;

                    var existing = _db.Quizzes
                        .Include(q => q.Questions)
                            .ThenInclude(q => q.Answers)
                        .Include(q => q.Questions)
                            .ThenInclude(q => q.Images)
                        .FirstOrDefault(q => q.Title == quizTitle && (q.Category ?? "Uncategorized") == quizCategory);

                    if (existing != null)
                    {
                        _db.Quizzes.Remove(existing);
                        await _db.SaveChangesAsync();
                    }

                    var quiz = new Models.Quiz { Title = quizTitle, Category = quizCategory == "Uncategorized" ? null : quizCategory };
                    _db.Quizzes.Add(quiz);
                    quizCount++;

                    var questions = quizGroup.GroupBy(r => r.QuestionText);

                    int orderIndex = 0;
                    foreach (var qGroup in questions)
                        {
                            var question = new Models.Question
                            {
                                Quiz = quiz,
                                Text = qGroup.Key,
                            OrderIndex = orderIndex++,
                            AllowMultiple = qGroup.Count(r => r.IsCorrect) > 1
                        };
                        _db.Questions.Add(question);
                        questionCount++;

                        int ansOrder = 0;
                        foreach (var row in qGroup)
                        {
                            var answer = new Models.Answer
                            {
                                Question = question,
                                Text = row.AnswerText,
                                IsCorrect = row.IsCorrect,
                                OrderIndex = ansOrder++
                            };
                            _db.Answers.Add(answer);
                            answerCount++;
                        }

                        var questionImageKeys = qGroup
                            .Select(r => r.QuestionImgKey)
                            .Where(v => !string.IsNullOrWhiteSpace(v))
                            .SelectMany(SplitImageKeys)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        foreach (var imageKey in questionImageKeys)
                        {
                            foreach (var imageFile in ResolveImageFiles(packageImagesDir, imageKey))
                            {
                                _db.Images.Add(new Models.Image
                                {
                                    QuestionId = question.Id,
                                    FileName = imageFile,
                                    ContentType = GetContentType(imageFile),
                                    Url = BuildImageUrl(packageId, imageFile)
                                });
                            }
                        }
                    }
                }

                await _db.SaveChangesAsync();

                run.Rows = rows.Count;
                run.Quizzes = quizCount;
                run.Questions = questionCount;
                run.Answers = answerCount;
                run.Status = "Success";
                run.Message = $"Imported {quizCount} quiz(es), {questionCount} question(s), {answerCount} answer(s) from {fileName}";

                AppendImportHistory(run);

                return run.Message;
            }
            catch (Exception ex)
            {
                run.Status = "Failed";
                run.Message = ex.Message;
                try { AppendImportHistory(run); } catch { }
                throw;
            }
        }

        private static string NormalizeCategory(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return "Uncategorized";

            var collapsed = System.Text.RegularExpressions.Regex.Replace(category.Trim(), @"\s+", " ");
            var lower = collapsed.ToLowerInvariant();
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
        }

        private string GetPrivateUploadsRoot()
        {
            return Path.Combine(_env.ContentRootPath, "App_Data", "uploads");
        }

        private string GetPublicUploadsRoot()
        {
            return Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads");
        }

        private string? GetPackageImagesDirectory(string? packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return null;

            var path = Path.Combine(GetPublicUploadsRoot(), "images", packageId);
            return Directory.Exists(path) ? path : null;
        }

        private static string? TryGetPackageIdFromImportedFile(string fileName)
        {
            var normalized = Path.GetFileName(fileName);
            var underscore = normalized.IndexOf('_');
            if (underscore <= 0)
                return null;

            return normalized[..underscore];
        }

        private static IEnumerable<string> SplitImageKeys(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v));
        }

        private static IEnumerable<string> ResolveImageFiles(string? packageImagesDir, string imageKey)
        {
            if (string.IsNullOrWhiteSpace(packageImagesDir) || string.IsNullOrWhiteSpace(imageKey))
                return Array.Empty<string>();

            var allFiles = Directory.GetFiles(packageImagesDir)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToList();

            var exactMatches = allFiles
                .Where(name => string.Equals(name, imageKey, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (exactMatches.Count > 0)
                return exactMatches;

            var requestedStem = Path.GetFileNameWithoutExtension(imageKey);
            return allFiles
                .Where(name => string.Equals(Path.GetFileNameWithoutExtension(name), requestedStem, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildImageUrl(string? packageId, string fileName)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return $"/uploads/{fileName}";

            return $"/uploads/images/{packageId}/{Uri.EscapeDataString(fileName)}";
        }

        private static string GetContentType(string fileName)
        {
            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream"
            };
        }

        private static string? GetSafeChildPath(string root, string fileName)
        {
            var normalizedName = Path.GetFileName(fileName);
            if (!string.Equals(fileName, normalizedName, StringComparison.Ordinal))
                return null;

            var rootPath = Path.GetFullPath(root);
            var candidate = Path.GetFullPath(Path.Combine(rootPath, normalizedName));

            return candidate.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? candidate
                : null;
        }
    }
}
