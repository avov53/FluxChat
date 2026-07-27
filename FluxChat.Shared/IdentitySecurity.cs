using System.Security.Cryptography;
using System.Text;

namespace FluxChat.Shared;

public static class IdentityCrypto
{
    public static string CreateUserId(string publicKeyBase64)
    {
        var hash = SHA256.HashData(Convert.FromBase64String(publicKeyBase64));
        return Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    public static string PublicKeySha256(string publicKeyBase64)
        => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKeyBase64))).ToLowerInvariant();

    public static byte[] BuildRegisterIdentityPayload(RelayRegisterPacket packet)
        => Build(writer =>
        {
            Write(writer, packet.Type);
            Write(writer, packet.UserId);
            Write(writer, packet.DisplayName);
            Write(writer, packet.IdentityNonce ?? "");
            writer.Write(packet.IdentityTimestampUtc?.ToUnixTimeMilliseconds() ?? 0);
        });

    public static byte[] BuildPresenceIdentityPayload(RelayPresencePacket packet)
        => Build(writer =>
        {
            Write(writer, packet.Type);
            Write(writer, packet.UserId);
            Write(writer, packet.DisplayName);
            Write(writer, packet.Status);
            writer.Write(packet.SentAtUtc.ToUnixTimeMilliseconds());
            Write(writer, packet.IdentityNonce ?? "");
        });

    public static byte[] BuildChatIdentityPayload(ChatPacket packet)
        => Build(writer =>
        {
            Write(writer, packet.Type);
            Write(writer, packet.MessageId.ToString("N"));
            Write(writer, packet.FromUserId);
            Write(writer, packet.ToUserId);
            Write(writer, packet.FromDisplayName);
            Write(writer, packet.Body);
            writer.Write(packet.SentAtUtc.ToUnixTimeMilliseconds());
            Write(writer, packet.Intent ?? "");
            Write(writer, packet.IdentityNonce ?? "");
        });

    public static string Sign(byte[] payload, ECDsa privateKey)
        => Convert.ToBase64String(privateKey.SignData(payload, HashAlgorithmName.SHA256));

    public static bool Verify(byte[] payload, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return key.VerifyData(payload, Convert.FromBase64String(signatureBase64), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static byte[] Build(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        write(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static void Write(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
