using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace PesaControl.Core.Security;

public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private const int MemorySizeKiB = 19 * 1024;
    private const int Iterations = 2;
    private const int Parallelism = 1;
    private const int SaltLength = 16;
    private const int HashLength = 32;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Derive(password, salt, MemorySizeKiB, Iterations, Parallelism);
        return $"$argon2id$v=19$m={MemorySizeKiB},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        if (!TryParse(encodedHash, out var memory, out var iterations, out var parallelism, out var salt, out var expected)) return false;
        var actual = Derive(password, salt, memory, iterations, parallelism);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string encodedHash) =>
        !TryParse(encodedHash, out var memory, out var iterations, out var parallelism, out _, out _) ||
        memory != MemorySizeKiB || iterations != Iterations || parallelism != Parallelism;

    private static byte[] Derive(string password, byte[] salt, int memory, int iterations, int parallelism)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };
        return argon.GetBytes(HashLength);
    }

    private static bool TryParse(string value, out int memory, out int iterations, out int parallelism, out byte[] salt, out byte[] hash)
    {
        memory = iterations = parallelism = 0; salt = hash = [];
        var parts = value.Split('$', StringSplitOptions.None);
        if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19") return false;
        try
        {
            var parameters = parts[3].Split(',').ToDictionary(x => x[..x.IndexOf('=')], x => x[(x.IndexOf('=') + 1)..]);
            memory = int.Parse(parameters["m"]); iterations = int.Parse(parameters["t"]); parallelism = int.Parse(parameters["p"]);
            salt = Convert.FromBase64String(parts[4]); hash = Convert.FromBase64String(parts[5]);
            return memory > 0 && iterations > 0 && parallelism > 0 && salt.Length >= SaltLength && hash.Length == HashLength;
        }
        catch { return false; }
    }
}
