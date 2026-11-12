using System.Text.Json;
using Lms.Shared.Quizzes;

namespace Lms.Client.Pages.Quizzes;

public sealed class OptionDraft
{
    public Guid Key { get; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}

/// <summary>A question being edited. <c>Id</c> is null until the first save.</summary>
public sealed class QuestionDraft
{
    public Guid Key { get; } = Guid.NewGuid();
    public Guid? Id { get; set; }
    public string Type { get; set; } = QuestionTypes.SingleChoice;
    public string Text { get; set; } = "";
    public decimal Points { get; set; } = 1m;
    public List<OptionDraft> Options { get; } = [];

    public static QuestionDraft New(string type = QuestionTypes.SingleChoice)
    {
        var q = new QuestionDraft { Type = type };
        q.ResetOptions();
        return q;
    }

    public static QuestionDraft From(QuizQuestionDto dto)
    {
        var q = new QuestionDraft { Id = dto.Id, Type = dto.Type, Text = dto.Text, Points = dto.Points };
        q.Options.AddRange(dto.Options.OrderBy(o => o.Order).Select(o => new OptionDraft { Text = o.Text, IsCorrect = o.IsCorrect }));
        return q;
    }

    /// <summary>The option set a type starts with: two blanks for choices, none for an essay, True/False labels filled by the caller.</summary>
    public void ResetOptions(string? trueLabel = null, string? falseLabel = null)
    {
        Options.Clear();
        switch (Type)
        {
            case QuestionTypes.Essay: break;
            case QuestionTypes.TrueFalse:
                Options.Add(new OptionDraft { Text = trueLabel ?? "", IsCorrect = true });
                Options.Add(new OptionDraft { Text = falseLabel ?? "" });
                break;
            default:
                Options.Add(new OptionDraft());
                Options.Add(new OptionDraft());
                break;
        }
    }

    public QuizQuestionInput ToInput() => new(Type, Text.Trim(), Points, Options.Select(o => new QuizOptionInput(o.Text.Trim(), o.IsCorrect)).ToList());

    public string Signature() => JsonSerializer.Serialize(new { Id, Type, Text, Points, Options = Options.Select(o => new { o.Text, o.IsCorrect }) });
}

/// <summary>The question rules of the server (FR-QZ-01), so the teacher sees what is wrong before saving. Returns a resource key or null.</summary>
public static class QuestionRules
{
    public const int MaxOptions = 10;

    public static string? Validate(QuestionDraft q)
    {
        if (string.IsNullOrWhiteSpace(q.Text)) return "Quiz_Err_Text";
        if (q.Text.Length > 4000) return "Quiz_Err_TextLong";
        if (q.Points is < 0.01m or > 1000m) return "Quiz_Err_Points";
        var correct = q.Options.Count(o => o.IsCorrect);
        switch (q.Type)
        {
            case QuestionTypes.Essay:
                return q.Options.Count == 0 ? null : "Quiz_Err_Options";
            case QuestionTypes.TrueFalse:
                return q.Options.Count == 2 && correct == 1 ? null : "Quiz_Err_OneCorrect";
            case QuestionTypes.SingleChoice:
            case QuestionTypes.MultipleChoice:
                if (q.Options.Count < 2) return "Quiz_Err_TwoOptions";
                if (q.Options.Count > MaxOptions) return "Quiz_Err_TooManyOptions";
                if (q.Options.Any(o => string.IsNullOrWhiteSpace(o.Text))) return "Quiz_Err_OptionText";
                if (q.Type == QuestionTypes.SingleChoice) return correct == 1 ? null : "Quiz_Err_OneCorrect";
                return correct >= 1 ? null : "Quiz_Err_AtLeastOneCorrect";
            default:
                return "Quiz_Err_Options";
        }
    }
}
