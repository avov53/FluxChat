using System.Security.Cryptography;

namespace FluxChat.Server.Core;

public sealed class ServerDataProtector
{
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private readonly byte[] _key;

    public ServerDataProtector(string base64Key)
    {
        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32) throw new InvalidOperationException("FLUXCHAT_DATA_KEY must be a base64-encoded 32-byte key.");
    }

    public static ServerDataProtector FromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("FLUXCHAT_DATA_KEY");
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("FLUXCHAT_DATA_KEY is required when PostgreSQL account storage is enabled.");
        }
        return new ServerDataProtector(value);
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using var aes = new AesGcm(_key, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. nonce, .. tag, .. ciphertext];
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedValue)
    {
        if (protectedValue.Length < NonceLength + TagLength) throw new CryptographicException("Protected payload is invalid.");
        var nonce = protectedValue[..NonceLength];
        var tag = protectedValue.Slice(NonceLength, TagLength);
        var ciphertext = protectedValue[(NonceLength + TagLength)..];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, TagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
