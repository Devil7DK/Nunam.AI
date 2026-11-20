using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Nunam.API.Auth
{
    public static class SessionTokens
    {
        public const string CookieName = "nunam_session";

        public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
