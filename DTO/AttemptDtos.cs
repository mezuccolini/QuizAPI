namespace QuizAPI.DTO
{
    public class QuizAttemptSubmitDto
    {
        public List<QuestionAttemptDto> Answers { get; set; } = new();
    }

    public class QuestionAttemptDto
    {
        public Guid QuestionId { get; set; }
        public List<Guid> SelectedAnswerIds { get; set; } = new();
    }

    public class QuizAttemptResultDto
    {
        public Guid QuizId { get; set; }
        public int TotalQuestions { get; set; }
        public int CorrectCount { get; set; }
        public double ScorePercent { get; set; }
        public List<QuestionResultDto> Questions { get; set; } = new();
    }

    public class QuestionResultDto
    {
        public Guid QuestionId { get; set; }
        public bool IsCorrect { get; set; }
        public List<Guid> SelectedAnswerIds { get; set; } = new();
    }
}
