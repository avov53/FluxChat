using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxChat.Shared;

public static class BadgeIds
{
    public const string Owner = "owner";
    public const string Tester = "tester";
    public const string Special = "special";

    public static bool IsKnown(string? badgeId)
        => badgeId is Owner or Tester or Special;
}

public sealed record BadgeCertificate(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("serial")] string Serial,
    [property: JsonPropertyName("badgeId")] string BadgeId,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("subjectPublicKeySha256")] string SubjectPublicKeySha256,
    [property: JsonPropertyName("issuedAtUtc")] DateTimeOffset IssuedAtUtc,
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("signature")] string Signature);

public sealed record BadgeRevocationSnapshot(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("generatedAtUtc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("revokedSerials")] IReadOnlyList<string> RevokedSerials,
    [property: JsonPropertyName("signature")] string Signature);

public sealed record BadgeStateResponse(
    [property: JsonPropertyName("certificates")] IReadOnlyList<BadgeCertificate> Certificates,
    [property: JsonPropertyName("selectedBadgeId")] string? SelectedBadgeId,
    [property: JsonPropertyName("canManageBadges")] bool CanManageBadges,
    [property: JsonPropertyName("revocations")] BadgeRevocationSnapshot Revocations);

public sealed record BadgeChallengeRequest(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("publicKey")] string PublicKey,
    [property: JsonPropertyName("displayName")] string DisplayName = "");

public sealed record BadgeChallengeResponse(
    [property: JsonPropertyName("challengeId")] string ChallengeId,
    [property: JsonPropertyName("challenge")] string Challenge,
    [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc);

public sealed record BadgeAuthenticateRequest(
    [property: JsonPropertyName("challengeId")] string ChallengeId,
    [property: JsonPropertyName("signature")] string Signature);

public sealed record BadgeAuthenticateResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc,
    [property: JsonPropertyName("state")] BadgeStateResponse State);

public sealed record BadgeSelectRequest([property: JsonPropertyName("badgeId")] string? BadgeId);

public sealed record BadgeAdminUserResponse(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("certificates")] IReadOnlyList<BadgeCertificate> Certificates);

public sealed record BadgeAdminMutationRequest([property: JsonPropertyName("userId")] string UserId);

public static class BadgeCrypto
{
    public const int CertificateVersion = 1;
    public const string OfficialIssuer = "FluxChat Official Badge Authority";

    public static string CreateUserId(string publicKeyBase64)
    {
        var hash = SHA256.HashData(Convert.FromBase64String(publicKeyBase64));
        return Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    public static string PublicKeySha256(string publicKeyBase64)
        => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKeyBase64))).ToLowerInvariant();

    public static byte[] BuildCertificatePayload(BadgeCertificate certificate)
        => Build(writer =>
        {
            writer.Write(certificate.Version);
            Write(writer, certificate.Serial);
            Write(writer, certificate.BadgeId);
            Write(writer, certificate.UserId);
            Write(writer, certificate.SubjectPublicKeySha256);
            writer.Write(certificate.IssuedAtUtc.ToUnixTimeMilliseconds());
            Write(writer, certificate.Issuer);
        });

    public static byte[] BuildRevocationPayload(BadgeRevocationSnapshot snapshot)
        => Build(writer =>
        {
            writer.Write(snapshot.Version);
            writer.Write(snapshot.GeneratedAtUtc.ToUnixTimeMilliseconds());
            writer.Write(snapshot.RevokedSerials.Count);
            foreach (var serial in snapshot.RevokedSerials.Order(StringComparer.Ordinal))
            {
                Write(writer, serial);
            }
        });

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
            Write(writer, CertificateDigest(packet.BadgeCertificate));
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
            Write(writer, CertificateDigest(packet.BadgeCertificate));
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

    public static bool VerifyCertificate(
        BadgeCertificate certificate,
        string subjectPublicKeyBase64,
        string authorityPublicKeyBase64,
        IReadOnlySet<string>? revokedSerials = null)
    {
        if (certificate.Version != CertificateVersion ||
            !BadgeIds.IsKnown(certificate.BadgeId) ||
            certificate.Issuer != OfficialIssuer ||
            certificate.UserId != CreateUserId(subjectPublicKeyBase64) ||
            !certificate.SubjectPublicKeySha256.Equals(PublicKeySha256(subjectPublicKeyBase64), StringComparison.OrdinalIgnoreCase) ||
            revokedSerials?.Contains(certificate.Serial) == true)
        {
            return false;
        }

        return Verify(BuildCertificatePayload(certificate), certificate.Signature, authorityPublicKeyBase64);
    }

    public static bool VerifyRevocations(BadgeRevocationSnapshot snapshot, string authorityPublicKeyBase64)
        => Verify(BuildRevocationPayload(snapshot), snapshot.Signature, authorityPublicKeyBase64);

    public static string? SerializeCertificate(BadgeCertificate? certificate)
        => certificate is null ? null : JsonSerializer.Serialize(certificate);

    public static BadgeCertificate? DeserializeCertificate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BadgeCertificate>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CertificateDigest(BadgeCertificate? certificate)
        => certificate is null
            ? ""
            : Convert.ToHexString(SHA256.HashData(BuildCertificatePayload(certificate))).ToLowerInvariant();

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
