using Lms.Infrastructure.People.Students;

namespace Lms.UnitTests.People;

public sealed class PasswordGeneratorTests
{
    private const string Ambiguous = "0O1lI|`'\" ";

    [Fact]
    public void Generated_password_meets_policy_and_avoids_ambiguous_chars()
    {
        for (var i = 0; i < 500; i++)
        {
            var password = PasswordGenerator.Generate();

            Assert.Equal(12, password.Length);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.DoesNotContain(password, c => Ambiguous.Contains(c));
            Assert.All(password, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
        }
    }

    [Fact]
    public void Generated_passwords_differ_between_calls()
    {
        var all = Enumerable.Range(0, 200).Select(_ => PasswordGenerator.Generate()).ToHashSet();

        Assert.Equal(200, all.Count);
    }
}
