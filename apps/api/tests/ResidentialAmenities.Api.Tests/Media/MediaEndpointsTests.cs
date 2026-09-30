using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Media;

/// <summary>
/// Issue #92: safe media upload, the prerequisite for #91's incident
/// reports. Covers the acceptance criteria directly: an allowed type is
/// accepted, a disallowed type is rejected, an oversized file is rejected,
/// and only the uploader or an Administrator can retrieve an attachment.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class MediaEndpointsTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // A minimal, structurally valid 1x1 JPEG: real magic bytes (FF D8 FF)
    // followed by an EOI marker, so AllowedMediaContentTypes actually
    // detects it as image/jpeg rather than merely satisfying the first
    // three bytes.
    private static readonly byte[] ValidJpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9];

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));

    private string _uploaderEmail = string.Empty;
    private string _otherResidentEmail = string.Empty;
    private string _adminEmail = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _uploaderEmail = $"media-uploader-{Guid.NewGuid():N}@example.test";
        _otherResidentEmail = $"media-other-{Guid.NewGuid():N}@example.test";
        _adminEmail = $"media-admin-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();

        var uploader = new UserAccount(Guid.NewGuid(), _uploaderEmail, "Media Uploader");
        Assert.True((await userManager.CreateAsync(uploader, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(uploader, ApplicationRoles.Resident)).Succeeded);

        var other = new UserAccount(Guid.NewGuid(), _otherResidentEmail, "Media Other Resident");
        Assert.True((await userManager.CreateAsync(other, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(other, ApplicationRoles.Resident)).Succeeded);

        var admin = new UserAccount(Guid.NewGuid(), _adminEmail, "Media Admin");
        Assert.True((await userManager.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(admin, ApplicationRoles.Administrator)).Succeeded);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Upload_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/media", BuildMultipart(ValidJpegBytes, "image/jpeg"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_AllowedImageType_IsAccepted()
    {
        using var client = await LoginAsync(_uploaderEmail);

        var response = await client.PostAsync(
            "/api/media", BuildMultipart(ValidJpegBytes, "image/jpeg"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.Equal("image/jpeg", body.GetProperty("contentType").GetString());
        Assert.Equal(ValidJpegBytes.Length, body.GetProperty("sizeBytes").GetInt64());
    }

    [Fact]
    public async Task Upload_DisallowedType_IsRejected()
    {
        using var client = await LoginAsync(_uploaderEmail);

        // Plain text bytes — matches none of the allowed magic-byte
        // signatures, regardless of what Content-Type the client claims.
        var textBytes = "MZ this is not really an executable but isn't an allowed type either"u8.ToArray();

        var response = await client.PostAsync(
            "/api/media", BuildMultipart(textBytes, "image/jpeg"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ClaimedContentTypeIsIgnored_OnlyActualBytesMatter()
    {
        using var client = await LoginAsync(_uploaderEmail);

        // The multipart part claims video/mp4, but the actual bytes are a
        // real JPEG signature — detection must go by content, not the
        // client-supplied header.
        var response = await client.PostAsync(
            "/api/media", BuildMultipart(ValidJpegBytes, "video/mp4"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.Equal("image/jpeg", body.GetProperty("contentType").GetString());
    }

    [Fact]
    public async Task Upload_OversizedFile_IsRejected()
    {
        Environment.SetEnvironmentVariable("MediaStorage__MaxImageSizeBytes", "5");
        try
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));

            await using var scope = factory.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
            var email = $"media-oversize-{Guid.NewGuid():N}@example.test";
            var user = new UserAccount(Guid.NewGuid(), email, "Oversize Uploader");
            Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(user, ApplicationRoles.Resident)).Succeeded);

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            var login = await client.PostAsJsonAsync(
                "/api/auth/login?useCookies=true",
                new { email, password = Password },
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            // ValidJpegBytes (13 bytes) exceeds the 5-byte limit configured
            // above.
            var response = await client.PostAsync(
                "/api/media", BuildMultipart(ValidJpegBytes, "image/jpeg"), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MediaStorage__MaxImageSizeBytes", null);
        }
    }

    [Fact]
    public async Task Download_ByOwner_Succeeds()
    {
        using var client = await LoginAsync(_uploaderEmail);
        var id = await UploadAsync(client);

        var response = await client.GetAsync($"/api/media/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ValidJpegBytes, bytes);
    }

    [Fact]
    public async Task Download_ByAdministrator_Succeeds()
    {
        using var uploaderClient = await LoginAsync(_uploaderEmail);
        var id = await UploadAsync(uploaderClient);

        using var adminClient = await LoginAsync(_adminEmail);
        var response = await adminClient.GetAsync($"/api/media/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Download_ByUnrelatedResident_Returns403()
    {
        using var uploaderClient = await LoginAsync(_uploaderEmail);
        var id = await UploadAsync(uploaderClient);

        using var otherClient = await LoginAsync(_otherResidentEmail);
        var response = await otherClient.GetAsync($"/api/media/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_Unauthenticated_Returns401()
    {
        using var uploaderClient = await LoginAsync(_uploaderEmail);
        var id = await UploadAsync(uploaderClient);

        using var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/media/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Download_UnknownId_Returns404()
    {
        using var client = await LoginAsync(_uploaderEmail);

        var response = await client.GetAsync($"/api/media/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> UploadAsync(HttpClient client)
    {
        var response = await client.PostAsync(
            "/api/media", BuildMultipart(ValidJpegBytes, "image/jpeg"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent BuildMultipart(byte[] bytes, string claimedContentType)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(claimedContentType);
        // A deliberately suspicious client-supplied filename: it must never
        // be trusted/stored/used to build a path.
        content.Add(fileContent, "file", "../../evil.exe");
        return content;
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true",
            new { email, password = Password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return client;
    }
}
