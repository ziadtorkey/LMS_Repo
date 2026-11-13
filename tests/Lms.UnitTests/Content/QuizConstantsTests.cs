using Lms.Domain.Quizzes;
using Lms.Shared.Quizzes;

namespace Lms.UnitTests.Content;

public sealed class QuizConstantsTests
{
    [Fact]
    public void Attempt_statuses_match_the_contract_catalog()
    {
        Assert.Equal(AttemptStatuses.InProgress, QuizAttempt.InProgress);
        Assert.Equal(AttemptStatuses.Submitted, QuizAttempt.Submitted);
        Assert.Equal(AttemptStatuses.AutoSubmitted, QuizAttempt.AutoSubmitted);
        Assert.Equal(AttemptStatuses.Graded, QuizAttempt.Graded);
    }
}
