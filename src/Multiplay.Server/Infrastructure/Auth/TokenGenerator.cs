using System.Security.Cryptography;

namespace Multiplay.Server.Infrastructure.Auth;

public static class TokenGenerator
{
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
               .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
