using System.Formats.Cbor;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fido2NetLib;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Identity;

/// <summary>
/// Issue #94 (ADR-012): mobile biometric sign-in via WebAuthn/passkeys.
/// There is no Capacitor native project in this repo yet, so an actual
/// fingerprint/face sensor cannot be exercised here — but the ceremony
/// this module runs against Fido2NetLib's real cryptographic verification
/// can be, using a software authenticator (an ES256 keypair plus
/// hand-built CBOR attestation/assertion objects, the same shapes a real
/// platform authenticator sends) so the security-critical path is
/// genuinely tested end to end, not stubbed out.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class WebAuthnEndpointsTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private const string Origin = "http://localhost:4200";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));

    private string _email = string.Empty;
    private Guid _userId;

    public async ValueTask InitializeAsync()
    {
        _email = $"webauthn-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<
            Microsoft.AspNetCore.Identity.UserManager<UserAccount>>();

        var user = new UserAccount(Guid.NewGuid(), _email, "Biometric Resident");
        Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, ApplicationRoles.Resident)).Succeeded);
        _userId = user.Id;
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task RegisterOptions_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient().PostAsync(
            "/api/auth/webauthn/register/options", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterOptions_WhenAuthenticated_ReturnsChallengeScopedToTheCurrentUser()
    {
        using var client = await LoginAsync();

        var response = await client.PostAsync(
            "/api/auth/webauthn/register/options", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        var userIdBase64Url = body.GetProperty("user").GetProperty("id").GetString();
        Assert.Equal(_userId, new Guid(Base64UrlDecode(userIdBase64Url!)));
        Assert.True(body.GetProperty("challenge").GetString()!.Length > 0);
    }

    [Fact]
    public async Task LoginOptions_UnknownEmail_ReturnsUnavailable_NotAnError()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/webauthn/login/options",
            new { email = "nobody@example.test" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.False(body.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task LoginOptions_KnownEmailWithNoRegisteredCredential_ReturnsUnavailable_NotAnError()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/webauthn/login/options",
            new { email = _email },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.False(body.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Login_WithUnknownSession_Returns400()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/webauthn/login",
            new
            {
                sessionId = Guid.NewGuid(),
                assertionResponse = new
                {
                    id = "x",
                    rawId = Convert.ToBase64String([1, 2, 3]),
                    type = "public-key",
                    response = new
                    {
                        authenticatorData = Convert.ToBase64String([1]),
                        signature = Convert.ToBase64String([1]),
                        clientDataJson = Convert.ToBase64String([1]),
                    }
                }
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterThenLogin_WithASoftwareAuthenticator_SucceedsAgainstRealFido2Verification_AndIsAudited()
    {
        using var client = await LoginAsync();
        var authenticator = new SoftwareAuthenticator();

        // 1. Registration ceremony.
        var optionsResponse = await client.PostAsync(
            "/api/auth/webauthn/register/options", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        var createOptionsJson = await optionsResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var createOptions = CredentialCreateOptions.FromJson(createOptionsJson);

        var attestation = authenticator.CreateAttestation(createOptions.Challenge, createOptions.Rp.Id!, Origin);

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/webauthn/register", attestation, TestContext.Current.CancellationToken);
        var registerBody = await registerResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, registerResponse.StatusCode);

        Assert.Single(await AuditAsync(_userId, AuditAction.BiometricCredentialRegistered));

        // 2. Login options.
        var loginOptionsResponse = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/webauthn/login/options", new { email = _email }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, loginOptionsResponse.StatusCode);
        var loginOptionsBody = await loginOptionsResponse.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.True(loginOptionsBody.GetProperty("available").GetBoolean());
        var sessionId = loginOptionsBody.GetProperty("sessionId").GetGuid();
        var assertionOptions = AssertionOptions.FromJson(loginOptionsBody.GetProperty("optionsJson").GetString()!);

        var assertion = authenticator.CreateAssertion(assertionOptions.Challenge, createOptions.Rp.Id!, Origin);

        // 3. Login ceremony: a fresh, unauthenticated client signs in purely
        // via the biometric assertion.
        using var anonymousClient = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginResponse = await anonymousClient.PostAsJsonAsync(
            "/api/auth/webauthn/login",
            new { sessionId, assertionResponse = assertion },
            TestContext.Current.CancellationToken);
        var loginBody = await loginResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(
            loginResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent,
            $"Expected success, got {loginResponse.StatusCode}: {loginBody}");

        Assert.Single(await AuditAsync(_userId, AuditAction.BiometricSignInSucceeded));

        // The client is now signed in via the cookie the login ceremony set.
        var me = await anonymousClient.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var credential = await WithDbAsync(db => db.WebAuthnCredentials.AsNoTracking()
            .SingleAsync(c => c.UserId == _userId, TestContext.Current.CancellationToken));
        Assert.Equal(1u, credential.SignCount);
        Assert.NotNull(credential.LastUsedAtUtc);
    }

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<List<AuditLog>> AuditAsync(Guid targetId, AuditAction action) =>
        WithDbAsync(db => db.AuditLogs
            .AsNoTracking()
            .Where(log => log.TargetId == targetId && log.Action == action)
            .ToListAsync(TestContext.Current.CancellationToken));

    private async Task<HttpClient> LoginAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true",
            new { email = _email, password = Password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return client;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>
    /// A minimal software WebAuthn authenticator: generates its own ES256
    /// keypair and hand-builds the same CBOR/JSON shapes a real platform
    /// authenticator (Face ID, fingerprint, Windows Hello, ...) would send,
    /// using the "none" attestation format. This lets the registration and
    /// assertion ceremonies run through Fido2NetLib's actual signature and
    /// attestation verification rather than mocking it away.
    /// </summary>
    private sealed class SoftwareAuthenticator
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly byte[] _credentialId = RandomNumberGenerator.GetBytes(32);
        private uint _signCount;

        public object CreateAttestation(byte[] challenge, string rpId, string origin)
        {
            var authenticatorData = BuildAuthenticatorData(rpId, includeAttestedCredentialData: true);
            var clientDataJson = BuildClientDataJson("webauthn.create", challenge, origin);

            var attestationObject = BuildCborMap(writer =>
            {
                writer.WriteTextString("fmt");
                writer.WriteTextString("none");
                writer.WriteTextString("attStmt");
                writer.WriteStartMap(0);
                writer.WriteEndMap();
                writer.WriteTextString("authData");
                writer.WriteByteString(authenticatorData);
            }, mapEntryCount: 3);

            return new
            {
                id = Base64UrlEncode(_credentialId),
                rawId = Base64UrlEncode(_credentialId),
                type = "public-key",
                response = new
                {
                    attestationObject = Base64UrlEncode(attestationObject),
                    clientDataJson = Base64UrlEncode(clientDataJson),
                }
            };
        }

        public object CreateAssertion(byte[] challenge, string rpId, string origin)
        {
            _signCount++;
            var authenticatorData = BuildAuthenticatorData(rpId, includeAttestedCredentialData: false);
            var clientDataJson = BuildClientDataJson("webauthn.get", challenge, origin);

            var clientDataHash = SHA256.HashData(clientDataJson);
            var signatureBase = authenticatorData.Concat(clientDataHash).ToArray();
            var signature = _key.SignData(signatureBase, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

            return new
            {
                id = Base64UrlEncode(_credentialId),
                rawId = Base64UrlEncode(_credentialId),
                type = "public-key",
                response = new
                {
                    authenticatorData = Base64UrlEncode(authenticatorData),
                    signature = Base64UrlEncode(signature),
                    clientDataJson = Base64UrlEncode(clientDataJson),
                }
            };
        }

        private byte[] BuildAuthenticatorData(string rpId, bool includeAttestedCredentialData)
        {
            var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(rpId));

            const byte userPresent = 0x01;
            const byte userVerified = 0x04;
            const byte attestedCredentialDataIncluded = 0x40;

            var flags = (byte)(userPresent | userVerified | (includeAttestedCredentialData ? attestedCredentialDataIncluded : 0));

            var signCountBytes = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(signCountBytes, _signCount);

            using var stream = new MemoryStream();
            stream.Write(rpIdHash);
            stream.WriteByte(flags);
            stream.Write(signCountBytes);

            if (includeAttestedCredentialData)
            {
                stream.Write(new byte[16]); // AAGUID: zeroed — this software authenticator has none.

                var credentialIdLength = new byte[2];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(credentialIdLength, (ushort)_credentialId.Length);
                stream.Write(credentialIdLength);
                stream.Write(_credentialId);

                var publicKey = _key.ExportParameters(includePrivateParameters: false);
                var coseKey = BuildCborMap(writer =>
                {
                    writer.WriteInt32(1);
                    writer.WriteInt32(2); // kty: EC2
                    writer.WriteInt32(3);
                    writer.WriteInt32(-7); // alg: ES256
                    writer.WriteInt32(-1);
                    writer.WriteInt32(1); // crv: P-256
                    writer.WriteInt32(-2);
                    writer.WriteByteString(publicKey.Q.X!);
                    writer.WriteInt32(-3);
                    writer.WriteByteString(publicKey.Q.Y!);
                }, mapEntryCount: 5);
                stream.Write(coseKey);
            }

            return stream.ToArray();
        }

        private static byte[] BuildClientDataJson(string type, byte[] challenge, string origin)
        {
            var json = JsonSerializer.Serialize(new
            {
                type,
                challenge = Convert.ToBase64String(challenge).Replace('+', '-').Replace('/', '_').TrimEnd('='),
                origin
            });
            return Encoding.UTF8.GetBytes(json);
        }

        private static byte[] BuildCborMap(Action<CborWriter> writeEntries, int mapEntryCount)
        {
            var writer = new CborWriter(CborConformanceMode.Lax);
            writer.WriteStartMap(mapEntryCount);
            writeEntries(writer);
            writer.WriteEndMap();
            return writer.Encode();
        }
    }
}
