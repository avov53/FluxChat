using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluxChat.Shared;

namespace FluxChat.Client;

internal static class MessageE2eCrypto
{
    public const string MessageIntent = "chat-e2e-message";
    public const string ControlIntent = "chat-e2e-control";
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public static ChatPacket Encrypt(
        ChatPacket packet,
        UserProfile profile,
        string recipientPublicKey,
        bool persistentMessage)
    {
        if (string.IsNullOrWhiteSpace(recipientPublicKey) ||
            IdentityCrypto.CreateUserId(recipientPublicKey) != packet.ToUserId)
        {
            throw new CryptographicException("The recipient identity key is missing or invalid.");
        }

        var envelope = new PlainEnvelope(packet.Intent ?? "", packet.Body);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        var key = DeriveSharedKey(profile, recipientPublicKey, packet.MessageId, packet.FromUserId, packet.ToUserId);
        using (var aes = new AesGcm(key, TagLength))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, BuildAad(packet));
        }

        var protectedEnvelope = new ProtectedEnvelope(
            1,
            profile.PublicKey,
            recipientPublicKey,
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(ciphertext),
            Convert.ToBase64String(tag));
        return packet with
        {
            Intent = persistentMessage ? MessageIntent : ControlIntent,
            Body = JsonSerializer.Serialize(protectedEnvelope)
        };
    }

    public static ChatPacket Decrypt(ChatPacket packet, UserProfile profile)
    {
        if (packet.Intent is not (MessageIntent or ControlIntent))
        {
            return packet;
        }

        var envelope = JsonSerializer.Deserialize<ProtectedEnvelope>(packet.Body)
            ?? throw new CryptographicException("Encrypted message envelope is invalid.");
        if (envelope.Version != 1 ||
            IdentityCrypto.CreateUserId(envelope.SenderPublicKey) != packet.FromUserId ||
            IdentityCrypto.CreateUserId(envelope.RecipientPublicKey) != packet.ToUserId)
        {
            throw new CryptographicException("Encrypted message identity does not match its route.");
        }

        var isSender = string.Equals(profile.PublicKey, envelope.SenderPublicKey, StringComparison.Ordinal);
        var isRecipient = string.Equals(profile.PublicKey, envelope.RecipientPublicKey, StringComparison.Ordinal);
        if (!isSender && !isRecipient)
        {
            throw new CryptographicException("This device does not own a key for the encrypted message.");
        }

        var peerPublicKey = isSender ? envelope.RecipientPublicKey : envelope.SenderPublicKey;
        var key = DeriveSharedKey(profile, peerPublicKey, packet.MessageId, packet.FromUserId, packet.ToUserId);
        var nonce = Convert.FromBase64String(envelope.Nonce);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var tag = Convert.FromBase64String(envelope.Tag);
        if (nonce.Length != NonceLength || tag.Length != TagLength)
        {
            throw new CryptographicException("Encrypted message parameters are invalid.");
        }

        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(key, TagLength))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, BuildAad(packet));
        }

        var plain = JsonSerializer.Deserialize<PlainEnvelope>(plaintext)
            ?? throw new CryptographicException("Encrypted message payload is invalid.");
        return packet with { Intent = plain.Intent, Body = plain.Body };
    }

    private static byte[] DeriveSharedKey(
        UserProfile profile,
        string peerPublicKey,
        Guid messageId,
        string fromUserId,
        string toUserId)
    {
        var protectedBytes = Convert.FromBase64String(profile.ProtectedPrivateKey);
        var privateBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        try
        {
            using var signingKey = ECDsa.Create();
            signingKey.ImportPkcs8PrivateKey(privateBytes, out _);
            using var own = ECDiffieHellman.Create(signingKey.ExportParameters(true));

            using var peerSigningKey = ECDsa.Create();
            peerSigningKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(peerPublicKey), out _);
            using var peer = ECDiffieHellman.Create(peerSigningKey.ExportParameters(false));
            var shared = own.DeriveKeyMaterial(peer.PublicKey);
            try
            {
                var context = Encoding.UTF8.GetBytes($"FluxChat-E2E-v1\n{messageId:N}\n{fromUserId}\n{toUserId}");
                return HMACSHA256.HashData(shared, context);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(shared);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateBytes);
        }
    }

    private static byte[] BuildAad(ChatPacket packet)
        => Encoding.UTF8.GetBytes($"{packet.Type}\n{packet.MessageId:N}\n{packet.FromUserId}\n{packet.ToUserId}\n{packet.SentAtUtc.UtcDateTime:O}");

    private sealed record PlainEnvelope(string Intent, string Body);

    private sealed record ProtectedEnvelope(
        int Version,
        string SenderPublicKey,
        string RecipientPublicKey,
        string Nonce,
        string Ciphertext,
        string Tag);
}
