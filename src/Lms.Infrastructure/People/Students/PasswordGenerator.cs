using System.Security.Cryptography;

namespace Lms.Infrastructure.People.Students;

/// <summary>
/// Default passwords for student accounts (D24): 12 characters from a cryptographic generator, at least one upper-case letter,
/// one lower-case letter and one digit (the Identity password policy), and no character that is easy to misread on a printed
/// sheet (<c>0 O 1 l I</c>).
/// </summary>
public static class PasswordGenerator
{
    public const int Length = 12;

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string All = Upper + Lower + Digits;

    public static string Generate()
    {
        var chars = new char[Length];
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        for (var i = 3; i < Length; i++)
        {
            chars[i] = Pick(All);
        }

        RandomNumberGenerator.Shuffle(chars.AsSpan()); // the guaranteed characters do not always come first
        return new string(chars);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
