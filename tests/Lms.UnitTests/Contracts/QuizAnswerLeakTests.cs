using System.Reflection;
using Lms.Shared.Quizzes;

namespace Lms.UnitTests.Contracts;

/// <summary>
/// Plan Review Focus 4: a student-facing quiz DTO must have no member that can carry the key. Walks the nested types of every
/// student-facing root and rejects any member named like Correct, Answer or Solution, except the student own saved answers.
/// The policy-gated result (<see cref="AttemptResultDto"/>, whose <c>IsCorrect</c> is null unless the policy and the attempt count allow it)
/// and the builder (staff only) are not roots.
/// </summary>
public sealed class QuizAnswerLeakTests
{
    private static readonly Type[] StudentRoots =
    [
        typeof(AttemptDto), typeof(AttemptQuestionDto), typeof(AttemptOptionDto), typeof(AttemptAnswerDto), typeof(SaveAnswerRequest),
        typeof(SaveAnswerResultDto), typeof(QuizSummaryDto), typeof(AttemptSummaryDto), typeof(QuizListItemDto), typeof(QuizPreviewDto),
    ];

    /// <summary>The student own saved answer (what they picked, never the key).</summary>
    private static readonly HashSet<string> OwnAnswers = ["AttemptDto.Answers", "AttemptAnswerDto.SelectedOptionIds"];

    private static IEnumerable<Type> Walk(Type root, HashSet<Type> seen)
    {
        if (!seen.Add(root))
        {
            yield break;
        }

        yield return root;
        foreach (var property in root.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var type in Walk(Element(property.PropertyType), seen))
            {
                yield return type;
            }
        }
    }

    private static Type Element(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsGenericType ? Element(type.GetGenericArguments()[0]) : type;
    }

    private static bool Suspicious(string name) =>
        name.Contains("Correct", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Solution", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Answer", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void No_student_facing_quiz_dto_has_a_member_that_can_carry_the_key()
    {
        var seen = new HashSet<Type>();
        var leaks = new List<string>();
        foreach (var type in StudentRoots.SelectMany(r => Walk(r, seen)).Where(t => t.Namespace?.StartsWith("Lms.Shared") == true))
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var qualified = $"{type.Name}.{property.Name}";
                if (Suspicious(property.Name) && !OwnAnswers.Contains(qualified))
                {
                    leaks.Add(qualified);
                }
            }
        }

        Assert.Empty(leaks);
        Assert.Contains(typeof(AttemptOptionDto), seen); // the walk really reached the nested option type
    }

    [Fact]
    public void The_walk_finds_a_leaking_member_when_there_is_one()
    {
        // The guard must bite: the builder option carries IsCorrect, so walking from it is reported.
        var types = Walk(typeof(QuizDto), []).SelectMany(t => t.GetProperties()).Select(p => p.Name).ToList();

        Assert.Contains(types, Suspicious);
    }

    [Fact]
    public void The_only_policy_gated_member_is_the_nullable_is_correct_of_the_result_option()
    {
        var property = typeof(AttemptResultOptionDto).GetProperty(nameof(AttemptResultOptionDto.IsCorrect))!;

        Assert.Equal(typeof(bool?), property.PropertyType); // null unless the policy and the attempt count allow it
    }
}
