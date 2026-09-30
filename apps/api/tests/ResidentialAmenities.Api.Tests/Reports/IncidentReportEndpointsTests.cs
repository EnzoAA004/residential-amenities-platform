using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reports.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Reports;

/// <summary>
/// Issue #91: incident/damage reports. Covers the acceptance criteria
/// directly — text-only, with image, with video, resident submission,
/// admin access/review, and that no financial side effect ever occurs
/// (reports are evidence only, never an automatic penalty).
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class IncidentReportEndpointsTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly byte[] ValidJpegBytes =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9];

    // A minimal, structurally valid WEBM header (EBML signature).
    private static readonly byte[] ValidWebmBytes = [0x1A, 0x45, 0xDF, 0xA3, 0x00, 0x00, 0x00, 0x00];

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));

    private string _reporterEmail = string.Empty;
    private string _otherResidentEmail = string.Empty;
    private string _adminEmail = string.Empty;
    private Guid _buildingId;
    private Guid _otherBuildingId;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _reporterEmail = $"report-reporter-{Guid.NewGuid():N}@example.test";
        _otherResidentEmail = $"report-other-{Guid.NewGuid():N}@example.test";
        _adminEmail = $"report-admin-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var building = new Building(Guid.NewGuid(), "Report Building", "America/Argentina/Buenos_Aires");
        dbContext.Buildings.Add(building);
        _buildingId = building.Id;

        var otherBuilding = new Building(Guid.NewGuid(), "Other Report Building", "America/Argentina/Buenos_Aires");
        dbContext.Buildings.Add(otherBuilding);
        _otherBuildingId = otherBuilding.Id;

        var unit = new Unit(Guid.NewGuid(), building.Id, floor: 1, door: "1", label: "R1");
        dbContext.Units.Add(unit);
        var otherUnit = new Unit(Guid.NewGuid(), building.Id, floor: 1, door: "2", label: "R2");
        dbContext.Units.Add(otherUnit);

        var reporter = new UserAccount(Guid.NewGuid(), _reporterEmail, "Reporter Resident");
        Assert.True((await userManager.CreateAsync(reporter, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(reporter, ApplicationRoles.Resident)).Succeeded);
        dbContext.ResidentMemberships.Add(new ResidentMembership(
            Guid.NewGuid(), building.Id, unit.Id, reporter.Id, DateTimeOffset.UtcNow));

        var other = new UserAccount(Guid.NewGuid(), _otherResidentEmail, "Other Resident");
        Assert.True((await userManager.CreateAsync(other, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(other, ApplicationRoles.Resident)).Succeeded);
        dbContext.ResidentMemberships.Add(new ResidentMembership(
            Guid.NewGuid(), building.Id, otherUnit.Id, other.Id, DateTimeOffset.UtcNow));

        var admin = new UserAccount(Guid.NewGuid(), _adminEmail, "Report Admin");
        Assert.True((await userManager.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(admin, ApplicationRoles.Administrator)).Succeeded);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Create_TextOnlyReport_Succeeds_WithOpenStatus()
    {
        using var client = await LoginAsync(_reporterEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new { buildingId = _buildingId, content = "Someone left the pool gate open again." },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.Equal("Open", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("mediaAttachmentIds").EnumerateArray());
    }

    [Fact]
    public async Task Create_ReportWithImageAttachment_Succeeds()
    {
        using var client = await LoginAsync(_reporterEmail);
        var mediaId = await UploadMediaAsync(client, ValidJpegBytes, "image/jpeg");

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new
            {
                buildingId = _buildingId,
                content = "Damage to the gym equipment.",
                mediaAttachmentIds = new[] { mediaId }
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        var attachmentIds = body.GetProperty("mediaAttachmentIds").EnumerateArray()
            .Select(e => e.GetGuid()).ToList();
        Assert.Equal([mediaId], attachmentIds);
    }

    [Fact]
    public async Task Create_ReportWithVideoAttachment_Succeeds()
    {
        using var client = await LoginAsync(_reporterEmail);
        var mediaId = await UploadMediaAsync(client, ValidWebmBytes, "video/webm");

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new
            {
                buildingId = _buildingId,
                content = "Elevator making a strange noise.",
                mediaAttachmentIds = new[] { mediaId }
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new { buildingId = _buildingId, content = "x" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutContent_Returns400()
    {
        using var client = await LoginAsync(_reporterEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new { buildingId = _buildingId, content = "   " },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_TooManyAttachments_Returns400()
    {
        using var client = await LoginAsync(_reporterEmail);
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            ids.Add(await UploadMediaAsync(client, ValidJpegBytes, "image/jpeg"));
        }

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new { buildingId = _buildingId, content = "too much evidence", mediaAttachmentIds = ids },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithSomeoneElsesAttachment_Returns400()
    {
        using var otherClient = await LoginAsync(_otherResidentEmail);
        var mediaId = await UploadMediaAsync(otherClient, ValidJpegBytes, "image/jpeg");

        using var reporterClient = await LoginAsync(_reporterEmail);
        var response = await reporterClient.PostAsJsonAsync(
            "/api/reports",
            new
            {
                buildingId = _buildingId,
                content = "trying to attach someone else's file",
                mediaAttachmentIds = new[] { mediaId }
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithReservationFromAnotherBuilding_Returns400()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        var foreignReservation = new Reservation(
            Guid.NewGuid(), _otherBuildingId, Guid.NewGuid(), ReservationUseType.SharedLeisure,
            nowUtc.AddDays(1), nowUtc.AddDays(1).AddHours(1), nowUtc, nowUtc.AddMinutes(30));
        dbContext.Reservations.Add(foreignReservation);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var client = await LoginAsync(_reporterEmail);
        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new { buildingId = _buildingId, reservationId = foreignReservation.Id, content = "mismatched building" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Resident_CanListAndGetOwnReport_ButNotSomeoneElses()
    {
        using var reporterClient = await LoginAsync(_reporterEmail);
        var created = await CreateReportAsync(reporterClient, "my own report");

        var list = await reporterClient.GetFromJsonAsync<JsonElement>(
            $"/api/reports?buildingId={_buildingId}", JsonOptions, TestContext.Current.CancellationToken);
        Assert.Single(list.EnumerateArray());

        var getOwn = await reporterClient.GetAsync(
            $"/api/reports/{created}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getOwn.StatusCode);

        using var otherClient = await LoginAsync(_otherResidentEmail);
        var otherList = await otherClient.GetFromJsonAsync<JsonElement>(
            $"/api/reports?buildingId={_buildingId}", JsonOptions, TestContext.Current.CancellationToken);
        Assert.Empty(otherList.EnumerateArray());

        var getOthers = await otherClient.GetAsync(
            $"/api/reports/{created}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, getOthers.StatusCode);
    }

    [Fact]
    public async Task Administrator_CanListAndAccessAllReportsInBuilding()
    {
        using var reporterClient = await LoginAsync(_reporterEmail);
        var id = await CreateReportAsync(reporterClient, "admin should see this");

        using var adminClient = await LoginAsync(_adminEmail);
        var list = await adminClient.GetFromJsonAsync<JsonElement>(
            $"/api/admin/reports?buildingId={_buildingId}", JsonOptions, TestContext.Current.CancellationToken);
        Assert.Single(list.EnumerateArray());

        var detail = await adminClient.GetAsync(
            $"/api/admin/reports/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task Resident_CannotAccessAdminReportEndpoints()
    {
        using var client = await LoginAsync(_reporterEmail);

        var response = await client.GetAsync(
            $"/api/admin/reports?buildingId={_buildingId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_CanUpdateStatus_AndItIsAudited()
    {
        using var reporterClient = await LoginAsync(_reporterEmail);
        var id = await CreateReportAsync(reporterClient, "please review");

        using var adminClient = await LoginAsync(_adminEmail);
        var response = await adminClient.PostAsJsonAsync(
            $"/api/admin/reports/{id}/status",
            new { status = "Reviewed" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.Equal("Reviewed", body.GetProperty("status").GetString());

        var report = await WithDbAsync(db => db.IncidentReports
            .AsNoTracking().SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken));
        Assert.Equal(IncidentReportStatus.Reviewed, report.Status);
    }

    [Fact]
    public async Task NoFinancialSideEffect_ExistsAnywhereInTheFlow()
    {
        using var reporterClient = await LoginAsync(_reporterEmail);
        await CreateReportAsync(reporterClient, "no money should ever move for this");

        using var adminClient = await LoginAsync(_adminEmail);
        var reports = await adminClient.GetFromJsonAsync<JsonElement>(
            $"/api/admin/reports?buildingId={_buildingId}", JsonOptions, TestContext.Current.CancellationToken);
        var id = reports.EnumerateArray().Single().GetProperty("id").GetGuid();
        await adminClient.PostAsJsonAsync(
            $"/api/admin/reports/{id}/status", new { status = "Resolved" }, TestContext.Current.CancellationToken);

        var payments = await WithDbAsync(db => db.Payments
            .Where(p => p.BuildingId == _buildingId)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(payments);
    }

    private async Task<Guid> CreateReportAsync(HttpClient client, string content)
    {
        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new { buildingId = _buildingId, content },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> UploadMediaAsync(HttpClient client, byte[] bytes, string contentType)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", "evidence");

        var response = await client.PostAsync("/api/media", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
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
