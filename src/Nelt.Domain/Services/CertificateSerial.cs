using System.Security.Cryptography;

namespace Nelt.Domain.Services;

public static class CertificateSerial
{
    // Crockford base32 without ambiguous characters (I, L, O, U).
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string New(DateTime issuedAt)
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"NELT-{issuedAt:yyyy}-{new string(chars[..4])}-{new string(chars[4..])}";
    }
}
