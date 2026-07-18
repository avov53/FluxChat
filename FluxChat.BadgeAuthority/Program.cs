using System.Collections.Concurrent;
using System.Security.Cryptography;
using FluxChat.BadgeAuthority;
using FluxChat.Shared;

var dataDirectory = Environment.GetEnvironmentVariable("FLUXCHAT_BADGE_DATA")
    ?? (OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FluxChat", "BadgeAuthority")
        : "/var/lib/fluxchat-badge-authority");
var keyPath = Environment.GetEnvironmentVariable("FLUXCHAT_BADGE_KEY_PATH") ?? Path.Combine(dataDirectory, "authority-key.pem");
var databasePath = Environment.GetEnvironmentVariable("FLUXCHAT_BADGE_DATABASE") ?? Path.Combine(dataDirectory, "badges.db");

if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    RunSecuritySelfTest();
    return;
}

if (args.Contains("--init", StringComparer.OrdinalIgnoreCase))
{
    var ownerUserId = Required("FLUXCHAT_BADGE_OWNER_USER_ID");
    var ownerPublicKey = Required("FLUXCHAT_BADGE_OWNER_PUBLIC_KEY");
    Directory.CreateDirectory(dataDirectory);
    if (File.Exists(keyPath)) throw new InvalidOperationException($"Authority key already exists: {keyPath}");
    using var generated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(keyPath, generated.ExportECPrivateKeyPem());
    TryRestrictKeyPermissions(keyPath);
    var store = new AuthorityStore(databasePath, generated);
    store.Initialize();
    store.BootstrapOwner(ownerUserId, ownerPublicKey);
    Console.WriteLine("Badge Authority initialized.");
    Console.WriteLine($"Public key: {Convert.ToBase64String(generated.ExportSubjectPublicKeyInfo())}");
    Console.WriteLine("Keep the private key outside Git, builds, logs and backups of the repository.");
    return;
}

if (!File.Exists(keyPath))
{
    throw new InvalidOperationException($"Badge Authority key is missing at {keyPath}. Run once with --init and owner environment variables.");
}

using var authorityKey = ECDsa.Create();
authorityKey.ImportFromPem(File.ReadAllText(keyPath));
var authorityPublicKey = Convert.ToBase64String(authorityKey.ExportSubjectPublicKeyInfo());
var authorityStore = new AuthorityStore(databasePath, authorityKey);
authorityStore.Initialize();
var sessions = new ConcurrentDictionary<string, Session>();

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
var app = builder.Build();

app.MapGet("/api/v1/public", () => Results.Ok(new
{
    issuer = BadgeCrypto.OfficialIssuer,
    publicKey = authorityPublicKey,
    revocations = authorityStore.GetState("__public__").Revocations
}));

app.MapPost("/api/v1/challenge", (BadgeChallengeRequest request) => Try(() => authorityStore.CreateChallenge(request)));
app.MapPost("/api/v1/authenticate", (BadgeAuthenticateRequest request) => Try(() =>
{
    var subject = authorityStore.Authenticate(request);
    var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    var expires = DateTimeOffset.UtcNow.AddMinutes(10);
    sessions[token] = new Session(subject.UserId, subject.IsAdmin, expires);
    return new BadgeAuthenticateResponse(token, expires, authorityStore.GetState(subject.UserId));
}));

app.MapGet("/api/v1/me", (HttpRequest request) => WithSession(request, false, s => authorityStore.GetState(s.UserId)));
app.MapPost("/api/v1/select", (HttpRequest request, BadgeSelectRequest body) => WithSession(request, false, s => authorityStore.Select(s.UserId, body.BadgeId)));
app.MapGet("/api/v1/admin/users/{userId}", (HttpRequest request, string userId) => WithSession(request, true, _ => authorityStore.Lookup(userId)));
app.MapPost("/api/v1/admin/grant-tester", (HttpRequest request, BadgeAdminMutationRequest body) => WithSession(request, true, s => authorityStore.GrantTester(s.UserId, body.UserId)));
app.MapPost("/api/v1/admin/revoke-tester", (HttpRequest request, BadgeAdminMutationRequest body) => WithSession(request, true, s => authorityStore.RevokeTester(s.UserId, body.UserId)));

app.Run();

IResult WithSession<T>(HttpRequest request, bool requireAdmin, Func<Session, T> action)
{
    var token = request.Headers.Authorization.ToString();
    if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..];
    if (!sessions.TryGetValue(token, out var session) || session.ExpiresAtUtc <= DateTimeOffset.UtcNow || (requireAdmin && !session.IsAdmin))
        return Results.Unauthorized();
    return Try(() => action(session));
}

static IResult Try<T>(Func<T> action)
{
    try { return Results.Ok(action()); }
    catch (UnauthorizedAccessException ex) { return Results.Json(new { error = ex.Message }, statusCode: 401); }
    catch (KeyNotFoundException ex) { return Results.Json(new { error = ex.Message }, statusCode: 404); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
}

static string Required(string name) => Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Environment variable {name} is required.");

static void TryRestrictKeyPermissions(string path)
{
    if (OperatingSystem.IsWindows()) return;
    try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    catch (Exception ex) { Console.Error.WriteLine($"Warning: could not chmod private key: {ex.Message}"); }
}

static void RunSecuritySelfTest()
{
    using var authority = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    using var subject = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    using var impostor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var authorityPublic = Convert.ToBase64String(authority.ExportSubjectPublicKeyInfo());
    var subjectPublic = Convert.ToBase64String(subject.ExportSubjectPublicKeyInfo());
    var impostorPublic = Convert.ToBase64String(impostor.ExportSubjectPublicKeyInfo());
    var unsigned = new BadgeCertificate(1, Guid.NewGuid().ToString("N"), BadgeIds.Owner,
        BadgeCrypto.CreateUserId(subjectPublic), BadgeCrypto.PublicKeySha256(subjectPublic),
        DateTimeOffset.UtcNow, BadgeCrypto.OfficialIssuer, "");
    var certificate = unsigned with { Signature = BadgeCrypto.Sign(BadgeCrypto.BuildCertificatePayload(unsigned), authority) };
    Must(BadgeCrypto.VerifyCertificate(certificate, subjectPublic, authorityPublic), "valid certificate");
    Must(!BadgeCrypto.VerifyCertificate(certificate with { BadgeId = BadgeIds.Tester }, subjectPublic, authorityPublic), "tampered badge id");
    Must(!BadgeCrypto.VerifyCertificate(certificate, impostorPublic, authorityPublic), "copied certificate");
    Must(!BadgeCrypto.VerifyCertificate(certificate, subjectPublic, authorityPublic, new HashSet<string> { certificate.Serial }), "revoked certificate");

    var unsignedRevocations = new BadgeRevocationSnapshot(1, DateTimeOffset.UtcNow, [certificate.Serial], "");
    var revocations = unsignedRevocations with { Signature = BadgeCrypto.Sign(BadgeCrypto.BuildRevocationPayload(unsignedRevocations), authority) };
    Must(BadgeCrypto.VerifyRevocations(revocations, authorityPublic), "signed revocations");
    Must(!BadgeCrypto.VerifyRevocations(revocations with { RevokedSerials = ["changed"] }, authorityPublic), "tampered revocations");

    var databasePath = Path.Combine(Path.GetTempPath(), $"fluxchat-badge-self-test-{Guid.NewGuid():N}.db");
    try
    {
        Console.WriteLine("Self-test: authority database");
        var store = new AuthorityStore(databasePath, authority);
        store.Initialize();
        var ownerUserId = BadgeCrypto.CreateUserId(subjectPublic);
        store.BootstrapOwner(ownerUserId, subjectPublic);
        var challenge = store.CreateChallenge(new BadgeChallengeRequest(ownerUserId, subjectPublic, "Owner test"));
        var challengeSignature = BadgeCrypto.Sign(Convert.FromBase64String(challenge.Challenge), subject);
        var authenticated = store.Authenticate(new BadgeAuthenticateRequest(challenge.ChallengeId, challengeSignature));
        Console.WriteLine("Self-test: challenge authentication");
        Must(authenticated.IsAdmin, "owner challenge authentication");
        var replayRejected = false;
        try { store.Authenticate(new BadgeAuthenticateRequest(challenge.ChallengeId, challengeSignature)); }
        catch (UnauthorizedAccessException) { replayRejected = true; }
        Must(replayRejected, "challenge replay rejection");

        var testerUserId = BadgeCrypto.CreateUserId(impostorPublic);
        store.CreateChallenge(new BadgeChallengeRequest(testerUserId, impostorPublic, "Tester test"));
        var granted = store.GrantTester(ownerUserId, testerUserId);
        Console.WriteLine("Self-test: tester grant");
        Must(granted.Certificates.Any(x => x.BadgeId == BadgeIds.Tester), "tester grant");
        var testerSerial = granted.Certificates.Single(x => x.BadgeId == BadgeIds.Tester).Serial;
        var revoked = store.RevokeTester(ownerUserId, testerUserId);
        Console.WriteLine("Self-test: tester revoke");
        Must(revoked.Certificates.All(x => x.BadgeId != BadgeIds.Tester), "tester revoke");
        var testerState = store.GetState(testerUserId);
        Must(testerState.Revocations.RevokedSerials.Contains(testerSerial), "revocation publication");
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
    }
    Console.WriteLine("Badge security self-test passed.");

    static void Must(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"Security self-test failed: {name}");
    }
}

internal sealed record Session(string UserId, bool IsAdmin, DateTimeOffset ExpiresAtUtc);
