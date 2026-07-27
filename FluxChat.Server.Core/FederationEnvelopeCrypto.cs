using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluxChat.Shared;

namespace FluxChat.Server.Core;

public static class FederationEnvelopeCrypto
{
    private const string PacketType = "fluxchat.relay-federation.v2";
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public static RelayFederationPacket Seal(ChatPacket message, DateTimeOffset sentAtUtc, string sharedKey)
    {
        EnsureKey(sharedKey);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(message);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        var encryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(sharedKey));
        using (var aes = new AesGcm(encryptionKey, TagLength))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, BuildAad(sentAtUtc));
        }

        var nonceBase64 = Convert.ToBase64String(nonce);
        var ciphertextBase64 = Convert.ToBase64String(ciphertext);
        var tagBase64 = Convert.ToBase64String(tag);
        var signature = Sign(sentAtUtc, nonceBase64, ciphertextBase64, tagBase64, sharedKey);
        return new RelayFederationPacket(
            PacketType,
            null,
            sentAtUtc,
            signature,
            nonceBase64,
            ciphertextBase64,
            tagBase64);
    }

    public static bool TryOpen(RelayFederationPacket envelope, string sharedKey, out ChatPacket? message)
    {
        message = null;
        if (envelope.Type != PacketType ||
            string.IsNullOrWhiteSpace(envelope.Nonce) ||
            string.IsNullOrWhiteSpace(envelope.Ciphertext) ||
            string.IsNullOrWhiteSpace(envelope.Tag) ||
            string.IsNullOrWhiteSpace(envelope.Signature))
        {
            return false;
        }

        try
        {
            EnsureKey(sharedKey);
            var expected = Sign(
                envelope.SentAtUtc,
                envelope.Nonce,
                envelope.Ciphertext,
                envelope.Tag,
                sharedKey);
            if (!FixedEquals(expected, envelope.Signature)) return false;

            var nonce = Convert.FromBase64String(envelope.Nonce);
            var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            var tag = Convert.FromBase64String(envelope.Tag);
            if (nonce.Length != NonceLength || tag.Length != TagLength) return false;

            var plaintext = new byte[ciphertext.Length];
            var encryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(sharedKey));
            using (var aes = new AesGcm(encryptionKey, TagLength))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext, BuildAad(envelope.SentAtUtc));
            }

            message = JsonSerializer.Deserialize<ChatPacket>(plaintext);
            return message is not null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static string Sign(
        DateTimeOffset sentAtUtc,
        string nonce,
        string ciphertext,
        string tag,
        string sharedKey)
    {
        var payload = $"{sentAtUtc.UtcDateTime:O}\n{nonce}\n{tag}\n{ciphertext}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static byte[] BuildAad(DateTimeOffset sentAtUtc)
        => Encoding.UTF8.GetBytes($"{PacketType}\n{sentAtUtc.UtcDateTime:O}");

    private static bool FixedEquals(string leftHex, string rightHex)
    {
        var left = Encoding.UTF8.GetBytes(leftHex);
        var right = Encoding.UTF8.GetBytes(rightHex);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static void EnsureKey(string sharedKey)
    {
        if (string.IsNullOrWhiteSpace(sharedKey) || Encoding.UTF8.GetByteCount(sharedKey) < 32)
        {
            throw new InvalidOperationException("FLUXCHAT_FEDERATION_KEY must contain at least 32 bytes.");
        }
    }
}
