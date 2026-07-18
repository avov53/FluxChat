using System.Security.Cryptography;
using FluxChat.Shared;

namespace FluxChat.Client;

internal sealed class IdentitySigner(UserProfile profile)
{
    public BadgeCertificate? ActiveBadgeCertificate { get; set; }

    public RelayRegisterPacket Sign(RelayRegisterPacket packet)
    {
        var unsigned = packet with
        {
            PublicKey = profile.PublicKey,
            IdentityNonce = CreateNonce(),
            IdentityTimestampUtc = DateTimeOffset.UtcNow,
            IdentitySignature = null
        };
        return unsigned with { IdentitySignature = Sign(BadgeCrypto.BuildRegisterIdentityPayload(unsigned)) };
    }

    public RelayPresencePacket Sign(RelayPresencePacket packet)
    {
        var unsigned = packet with
        {
            PublicKey = profile.PublicKey,
            IdentityNonce = CreateNonce(),
            IdentitySignature = null,
            BadgeCertificate = ActiveBadgeCertificate
        };
        return unsigned with { IdentitySignature = Sign(BadgeCrypto.BuildPresenceIdentityPayload(unsigned)) };
    }

    public ChatPacket Sign(ChatPacket packet)
    {
        var unsigned = packet with
        {
            FromPublicKey = profile.PublicKey,
            IdentityNonce = CreateNonce(),
            IdentitySignature = null,
            BadgeCertificate = ActiveBadgeCertificate
        };
        return unsigned with { IdentitySignature = Sign(BadgeCrypto.BuildChatIdentityPayload(unsigned)) };
    }

    public string SignChallenge(string challengeBase64)
        => Sign(Convert.FromBase64String(challengeBase64));

    private string Sign(byte[] payload)
    {
        var protectedBytes = Convert.FromBase64String(profile.ProtectedPrivateKey);
        var privateBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        try
        {
            using var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(privateBytes, out _);
            return BadgeCrypto.Sign(payload, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateBytes);
        }
    }

    private static string CreateNonce()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
