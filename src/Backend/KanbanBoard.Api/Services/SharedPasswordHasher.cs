using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KanbanBoard.Api.Services;

/// <summary>
/// PBKDF2-SHA256 hashing for the shared access password.
/// </summary>
/// <remarks>
/// Format: <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;</c>.
/// The same format is produced by deploy-iis.ps1 (Windows PowerShell /
/// .NET Framework) and by the E2E config (Node crypto), so all three must
/// stay in sync - tests cross-check them.
/// </remarks>
public static class SharedPasswordHasher
{
    public const string Prefix = "pbkdf2-sha256";

    /// <summary>OWASP 2023 recommendation for PBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>Configured hashes weaker than this are rejected as malformed.</summary>
    public const int MinimumIterations = 10_000;

    public const int MinimumPasswordLength = 8;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Creates a new salted hash for <paramref name="password"/>.</summary>
    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (iterations < MinimumIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Join('$', Prefix, iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>True when <paramref name="stored"/> is a well-formed hash this class can verify.</summary>
    public static bool IsWellFormed(string? stored) => TryParse(stored, out _, out _, out _);

    /// <summary>
    /// Constant-time check of <paramref name="password"/> against a stored hash.
    /// Malformed hashes never verify (fail closed).
    /// </summary>
    public static bool Verify(string? password, string? stored)
    {
        if (string.IsNullOrEmpty(password) || !TryParse(stored, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Short, non-reversible identifier of the configured hash, stored in each
    /// session cookie. Changing the password changes it, which signs every
    /// browser out.
    /// </summary>
    public static string Fingerprint(string? stored) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stored ?? string.Empty)))[..16];

    private static bool TryParse(string? stored, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = Array.Empty<byte>();
        hash = Array.Empty<byte>();
        var parts = stored?.Trim().Split('$');
        if (parts is not { Length: 4 } || parts[0] != Prefix ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations) ||
            iterations < MinimumIterations)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length >= 8 && hash.Length >= 16;
    }
}
