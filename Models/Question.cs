using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuizAPI.Models
{
    public class Question
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();

        [ForeignKey(nameof(Quiz))]
        public Guid QuizId { get; set; }

        [Required]
        public string Text { get; set; } = string.Empty;

        public int OrderIndex { get; set; }
        public bool AllowMultiple { get; set; }

        public Quiz? Quiz { get; set; }

        public List<Answer> Answers { get; set; } = new();
        public List<Image> Images { get; set; } = new();
    }
}
