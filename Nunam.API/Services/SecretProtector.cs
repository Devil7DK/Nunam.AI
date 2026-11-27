using System.Security.Cryptography;

namespace Nunam.API.Services
{
    /// <summary>
    /// Encrypts secrets kept in the database, such as describe providers' API keys, with AES-GCM under
    /// <c>Secrets:Key</c> (32 random bytes, base64). The key lives in the server's environment, so a
    /// copy of the database alone doesn't give the secrets away.
    /// </summary>
    public sealed class SecretProtector
    {
        public const string KeySetting = "Secrets:Key";
        private const string Version = "v1.";
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private readonly byte[]? _key;

        public SecretProtector(IConfiguration configuration)
        {
            var value = configuration[KeySetting];
            if (string.IsNullOrWhiteSpace(value)) return;

            byte[] key;
            try
            {
                key = Convert.FromBase64String(value.Trim());
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"{KeySetting} is not base64. Make one with: openssl rand -base64 32");
            }
            if (key.Length != 32)
            {
                throw new InvalidOperationException($"{KeySetting} must be 32 bytes. Make one with: openssl rand -base64 32");
            }
            _key = key;
        }

        public bool IsConfigured => _key is not null;

        public string Protect(string secret)
        {
            var key = _key ?? throw new InvalidOperationException($"{KeySetting} is not set.");
            var plain = System.Text.Encoding.UTF8.GetBytes(secret);
            var sealedBytes = new byte[NonceSize + TagSize + plain.Length];
            var nonce = sealedBytes.AsSpan(0, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plain, sealedBytes.AsSpan(NonceSize + TagSize), sealedBytes.AsSpan(NonceSize, TagSize));
            return Version + Convert.ToBase64String(sealedBytes);
        }

        /// <summary><c>null</c> when the key is missing or isn't the one the value was sealed with.</summary>
        public string? Unprotect(string value)
        {
            if (_key is null || !value.StartsWith(Version, StringComparison.Ordinal)) return null;
            try
            {
                var sealedBytes = Convert.FromBase64String(value[Version.Length..]);
                if (sealedBytes.Length < NonceSize + TagSize) return null;
                var plain = new byte[sealedBytes.Length - NonceSize - TagSize];
                using var aes = new AesGcm(_key, TagSize);
                aes.Decrypt(sealedBytes.AsSpan(0, NonceSize), sealedBytes.AsSpan(NonceSize + TagSize),
                    sealedBytes.AsSpan(NonceSize, TagSize), plain);
                return System.Text.Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                return null;
            }
        }
    }
}
