using System.Security.Cryptography;

namespace Rotadesk.Api.Staff;

/// <summary>A random first password that meets Identity's rules. Look-alike characters are left out.</summary>
internal static class TemporaryPassword
{
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*?";

    public static string Create()
    {
        var chars = new List<char> { Pick(Lower), Pick(Upper), Pick(Digits), Pick(Symbols) };
        while (chars.Count < 16)
        {
            chars.Add(Pick(Lower + Upper + Digits + Symbols));
        }

        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string([.. chars]);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
