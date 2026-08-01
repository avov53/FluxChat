using System.Text.Json.Serialization;

namespace FluxChat.Shared;

public static class FluxChatPorts
{
    public const int Discovery = 42731;
    public const int Messages = 42732;
    public const int Relay = 42800;
}

public sealed record DiscoveryPacket(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("localIp")] string LocalIp,
    [property: JsonPropertyName("messagePort")] int MessagePort,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset SentAtUtc)
{
    public static DiscoveryPacket Create(string userId, string displayName, string localIp)
        => new("fluxchat.discovery.v1", userId, displayName, localIp, FluxChatPorts.Messages, DateTimeOffset.UtcNow);
}

public sealed record ChatPacket(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonPropertyName("fromUserId")] string FromUserId,
    [property: JsonPropertyName("fromDisplayName")] string FromDisplayName,
    [property: JsonPropertyName("toUserId")] string ToUserId,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset SentAtUtc,
    [property: JsonPropertyName("networkId")] string? NetworkId = null,
    [property: JsonPropertyName("fromRelayServer")] string? FromRelayServer = null,
    [property: JsonPropertyName("toRelayServer")] string? ToRelayServer = null,
    [property: JsonPropertyName("intent")] string? Intent = null,
    [property: JsonPropertyName("fromStatus")] string? FromStatus = null,
    [property: JsonPropertyName("fromAvatarKind")] string? FromAvatarKind = null,
    [property: JsonPropertyName("fromAvatarMediaBase64")] string? FromAvatarMediaBase64 = null,
    [property: JsonPropertyName("fromAvatarExtension")] string? FromAvatarExtension = null,
    [property: JsonPropertyName("fromAvatarScale")] double FromAvatarScale = 1,
    [property: JsonPropertyName("fromAvatarOffsetX")] double FromAvatarOffsetX = 0,
    [property: JsonPropertyName("fromAvatarOffsetY")] double FromAvatarOffsetY = 0,
    [property: JsonPropertyName("fromAvatarVideoStartSeconds")] double FromAvatarVideoStartSeconds = 0,
    [property: JsonPropertyName("fromAvatarVideoDurationSeconds")] double FromAvatarVideoDurationSeconds = 10,
    [property: JsonPropertyName("fromPublicKey")] string? FromPublicKey = null,
    [property: JsonPropertyName("identityNonce")] string? IdentityNonce = null,
    [property: JsonPropertyName("identitySignature")] string? IdentitySignature = null,
    [property: JsonPropertyName("fromCustomStatus")] string? FromCustomStatus = null)
{
    public static ChatPacket Create(
        string fromUserId,
        string fromDisplayName,
        string toUserId,
        string body,
        string? networkId = null,
        string? fromRelayServer = null,
        string? toRelayServer = null,
        string? intent = null,
        string? fromStatus = null,
        string? fromAvatarKind = null,
        string? fromAvatarMediaBase64 = null,
        string? fromAvatarExtension = null,
        double fromAvatarScale = 1,
        double fromAvatarOffsetX = 0,
        double fromAvatarOffsetY = 0,
        double fromAvatarVideoStartSeconds = 0,
        double fromAvatarVideoDurationSeconds = 10,
        string? fromCustomStatus = null)
        => new(
            "fluxchat.message.v1",
            Guid.NewGuid(),
            fromUserId,
            fromDisplayName,
            toUserId,
            body,
            DateTimeOffset.UtcNow,
            networkId,
            fromRelayServer,
            toRelayServer,
            intent,
            fromStatus,
            fromAvatarKind,
            fromAvatarMediaBase64,
            fromAvatarExtension,
            fromAvatarScale,
            fromAvatarOffsetX,
            fromAvatarOffsetY,
            fromAvatarVideoStartSeconds,
            fromAvatarVideoDurationSeconds,
            null,
            null,
            null,
            fromCustomStatus);
}

public sealed record PeerInfoRequest(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("requestId")] Guid RequestId,
    [property: JsonPropertyName("requesterUserId")] string? RequesterUserId = null,
    [property: JsonPropertyName("requesterDisplayName")] string? RequesterDisplayName = null,
    [property: JsonPropertyName("requesterMessagePort")] int RequesterMessagePort = FluxChatPorts.Messages,
    [property: JsonPropertyName("requesterStatus")] string? RequesterStatus = null,
    [property: JsonPropertyName("networkId")] string? NetworkId = null)
{
    public static PeerInfoRequest Create(
        string? requesterUserId = null,
        string? requesterDisplayName = null,
        string? requesterStatus = null,
        string? networkId = null)
        => new(
            "fluxchat.peer-info-request.v1",
            Guid.NewGuid(),
            requesterUserId,
            requesterDisplayName,
            FluxChatPorts.Messages,
            requesterStatus,
            networkId);
}

public sealed record PeerInfoResponse(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("requestId")] Guid RequestId,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("messagePort")] int MessagePort,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("queuedMessages")] IReadOnlyList<ChatPacket>? QueuedMessages = null,
    [property: JsonPropertyName("networkId")] string? NetworkId = null)
{
    public static PeerInfoResponse Create(
        Guid requestId,
        string userId,
        string displayName,
        string status,
        IReadOnlyList<ChatPacket>? queuedMessages = null,
        string? networkId = null)
        => new("fluxchat.peer-info-response.v1", requestId, userId, displayName, FluxChatPorts.Messages, status, queuedMessages, networkId);
}

public sealed record RelayRegisterPacket(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("credential")] string Credential,
    [property: JsonPropertyName("publicKey")] string? PublicKey = null,
    [property: JsonPropertyName("identityNonce")] string? IdentityNonce = null,
    [property: JsonPropertyName("identityTimestampUtc")] DateTimeOffset? IdentityTimestampUtc = null,
    [property: JsonPropertyName("identitySignature")] string? IdentitySignature = null)
{
    public static RelayRegisterPacket Create(string userId, string displayName, string credential)
        => new("fluxchat.relay-register.v1", userId, displayName, credential);
}

public sealed record RelayAudioRegisterPacket(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("credential")] string Credential)
{
    public static RelayAudioRegisterPacket Create(string userId, string displayName, string credential)
        => new("fluxchat.audio-register.v1", userId, displayName, credential);
}

public sealed record RelayScreenRegisterPacket(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("credential")] string Credential)
{
    public static RelayScreenRegisterPacket Create(string userId, string displayName, string credential)
        => new("fluxchat.screen-register.v1", userId, displayName, credential);
}

public sealed record RelayIceServer(
    [property: JsonPropertyName("urls")] IReadOnlyList<string> Urls,
    [property: JsonPropertyName("username")] string? Username = null,
    [property: JsonPropertyName("credential")] string? Credential = null,
    [property: JsonPropertyName("credentialType")] string CredentialType = "password");

public sealed record RelayIceConfig(
    [property: JsonPropertyName("iceServers")] IReadOnlyList<RelayIceServer> IceServers,
    [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc);

public sealed record RelayAckPacket(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("clientToken")] string? ClientToken = null,
    [property: JsonPropertyName("iceConfig")] RelayIceConfig? IceConfig = null,
    [property: JsonPropertyName("accountApiUrl")] string? AccountApiUrl = null)
{
    public static RelayAckPacket AcceptedResult(
        string message,
        string? clientToken = null,
        RelayIceConfig? iceConfig = null,
        string? accountApiUrl = null)
        => new("fluxchat.relay-ack.v1", true, message, clientToken, iceConfig, accountApiUrl);

    public static RelayAckPacket Denied(string message, string? accountApiUrl = null)
        => new("fluxchat.relay-ack.v1", false, message, null, null, accountApiUrl);
}

public sealed record AccountRegisterRequest(
    string UserId,
    string DisplayName,
    string Login,
    string Email,
    string Password,
    string PublicKey);

public sealed record AccountLoginRequest(string LoginOrEmail, string Password, string DeviceName);

public sealed record AccountCodeRequest(string LoginOrEmail, string Purpose);

public sealed record AccountCodeLoginRequest(string LoginOrEmail, string Code, string DeviceName);

public sealed record AccountResetPasswordRequest(string LoginOrEmail, string Code, string NewPassword);

public sealed record AccountVerifyEmailRequest(string LoginOrEmail, string Code);

public sealed record AccountSessionResponse(
    bool Accepted,
    string Message,
    string? UserId = null,
    string? DisplayName = null,
    string? Login = null,
    string? RelayToken = null,
    DateTimeOffset? ExpiresAtUtc = null,
    string? SessionId = null,
    string? DeviceName = null);

public sealed record AccountResult(bool Accepted, string Message);

public sealed record AccountPreferences(
    string ActivityVisibility,
    IReadOnlyList<string> ActivitySelectedFriendIds);

public sealed record AccountPreferencesResponse(
    bool Accepted,
    string Message,
    AccountPreferences? Preferences = null);

public sealed record AccountPreferencesUpdateRequest(
    string ActivityVisibility,
    IReadOnlyList<string> ActivitySelectedFriendIds);

public sealed record AccountReadState(
    string ScopeType,
    string ScopeId,
    string ChannelId,
    DateTimeOffset LastReadAtUtc,
    Guid? LastReadMessageId);

public sealed record AccountReadStatesResponse(
    bool Accepted,
    string Message,
    IReadOnlyList<AccountReadState>? ReadStates = null);

public sealed record AccountMarkReadRequest(
    string ScopeType,
    string ScopeId,
    string ChannelId,
    DateTimeOffset LastReadAtUtc,
    Guid? LastReadMessageId);

public sealed record AccountDeviceSession(
    string SessionId,
    string DeviceName,
    string Location,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool IsCurrent,
    bool IsActive = true);

public sealed record AccountDeviceSessionsResponse(
    bool Accepted,
    string Message,
    IReadOnlyList<AccountDeviceSession>? Sessions = null);

public sealed record AccountSyncedContact(
    string UserId,
    string DisplayName,
    string RelayServer,
    string AvatarKind,
    string AvatarPath,
    double AvatarScale,
    double AvatarOffsetX,
    double AvatarOffsetY,
    double AvatarVideoStartSeconds,
    double AvatarVideoDurationSeconds,
    bool IsGroup,
    string GroupMemberIds,
    string GroupOwnerUserId,
    long GroupVersion,
    bool GroupIsDeleted,
    string GroupMembersJson,
    string IdentityPublicKey,
    DateTimeOffset UpdatedAtUtc,
    string CustomStatus = "",
    string ServerTemplate = "",
    string ServerChannelsJson = "",
    string ServerRolesJson = "",
    string SelectedServerChannelId = "",
    string ServerModerationJson = "",
    string ServerBackgroundPath = "");

public sealed record AccountContactsResponse(
    bool Accepted,
    string Message,
    IReadOnlyList<AccountSyncedContact>? Contacts = null);

public sealed record AccountContactUpsertRequest(AccountSyncedContact Contact);

public sealed record AccountContactDeleteRequest(string UserId);

public sealed record AccountHistoryDeleteRequest(string PeerUserId);

public sealed record AccountMediaDeleteRequest(string MediaId);

public sealed record AccountSessionRevokeRequest(string SessionId);

public sealed record AccountSessionRevokeAllRequest(bool IncludeCurrent = true);

public sealed record AccountChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record AccountDeleteRequest(string CurrentPassword, string Confirmation);

public sealed record AccountMediaUploadResponse(
    bool Accepted,
    string Message,
    string? MediaId = null,
    string? ThumbnailMediaId = null,
    string? MimeType = null,
    long ByteLength = 0,
    int Width = 0,
    int Height = 0,
    int DurationMs = 0);

public sealed record FederationUsernameClaim(
    string LoginNormalized,
    string UserId,
    string ServerId,
    DateTimeOffset ClaimedAtUtc);

public sealed record RelayPresencePacket(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset SentAtUtc,
    [property: JsonPropertyName("avatarKind")] string? AvatarKind = null,
    [property: JsonPropertyName("avatarMediaBase64")] string? AvatarMediaBase64 = null,
    [property: JsonPropertyName("avatarExtension")] string? AvatarExtension = null,
    [property: JsonPropertyName("avatarScale")] double AvatarScale = 1,
    [property: JsonPropertyName("avatarOffsetX")] double AvatarOffsetX = 0,
    [property: JsonPropertyName("avatarOffsetY")] double AvatarOffsetY = 0,
    [property: JsonPropertyName("avatarVideoStartSeconds")] double AvatarVideoStartSeconds = 0,
    [property: JsonPropertyName("avatarVideoDurationSeconds")] double AvatarVideoDurationSeconds = 10,
    [property: JsonPropertyName("publicKey")] string? PublicKey = null,
    [property: JsonPropertyName("identityNonce")] string? IdentityNonce = null,
    [property: JsonPropertyName("identitySignature")] string? IdentitySignature = null,
    [property: JsonPropertyName("customStatus")] string? CustomStatus = null,
    [property: JsonPropertyName("activityVisibility")] string ActivityVisibility = "Friends",
    [property: JsonPropertyName("activitySelectedFriendIds")] IReadOnlyList<string>? ActivitySelectedFriendIds = null)
{
    public static RelayPresencePacket Create(
        string userId,
        string displayName,
        string status,
        string? avatarKind = null,
        string? avatarMediaBase64 = null,
        string? avatarExtension = null,
        double avatarScale = 1,
        double avatarOffsetX = 0,
        double avatarOffsetY = 0,
        double avatarVideoStartSeconds = 0,
        double avatarVideoDurationSeconds = 10,
        string? customStatus = null,
        string activityVisibility = "Friends",
        IReadOnlyList<string>? activitySelectedFriendIds = null)
        => new(
            "fluxchat.relay-presence.v1",
            userId,
            displayName,
            status,
            DateTimeOffset.UtcNow,
            avatarKind,
            avatarMediaBase64,
            avatarExtension,
            avatarScale,
            avatarOffsetX,
            avatarOffsetY,
            avatarVideoStartSeconds,
            avatarVideoDurationSeconds,
            null,
            null,
            null,
            customStatus,
            activityVisibility,
            activitySelectedFriendIds);
}

public sealed record RelayAudioPacket(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonPropertyName("fromUserId")] string FromUserId,
    [property: JsonPropertyName("toUserId")] string ToUserId,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset SentAtUtc,
    [property: JsonPropertyName("sequence")] long Sequence = 0)
{
    public static RelayAudioPacket Create(string fromUserId, string toUserId, string body, long sequence = 0)
        => new("fluxchat.call-audio.v1", Guid.NewGuid(), fromUserId, toUserId, body, DateTimeOffset.UtcNow, sequence);
}

public sealed record RelayScreenFramePacket(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonPropertyName("fromUserId")] string FromUserId,
    [property: JsonPropertyName("toUserId")] string ToUserId,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset SentAtUtc)
{
    public static RelayScreenFramePacket Create(string fromUserId, string toUserId, string body)
        => new("fluxchat.screen-frame.v1", Guid.NewGuid(), fromUserId, toUserId, body, DateTimeOffset.UtcNow);
}

public sealed record RelayFederationPacket(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("message")] ChatPacket? Message,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset SentAtUtc,
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("nonce")] string? Nonce = null,
    [property: JsonPropertyName("ciphertext")] string? Ciphertext = null,
    [property: JsonPropertyName("tag")] string? Tag = null)
{
    public static RelayFederationPacket Create(ChatPacket message, DateTimeOffset sentAtUtc, string signature)
        => new("fluxchat.relay-federation.v1", message, sentAtUtc, signature);
}
