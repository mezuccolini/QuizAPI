namespace QuizAPI.DTO
{
    public class RandomizedQuizDto
    {
        public Guid QuizId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = "Uncategorized";
        public List<QuestionDto> Questions { get; set; } = new();
    }

    public class QuestionDto
    {
        public Guid QuestionId { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool AllowMultiple { get; set; }
        public List<AnswerDto> Answers { get; set; } = new();
        public List<ImageDto> Images { get; set; } = new();
    }

    public class AnswerDto
    {
        public Guid AnswerId { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public class ImageDto
    {
        public Guid ImageId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
    }
}
