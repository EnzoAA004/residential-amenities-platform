using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Identity;

[Collection(DevelopmentSeedCollection.Name)]
public sealed class DemoAccessTests
{
    [Fact]
    public async Task DemoSession_WhenDisabled_Returns404()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                    builder.UseSetting("environment", "Development"));

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                HandleCookies = true
            });

        var status =
            await client.GetAsync(
                "/api/demo/status",
                cancellationToken);

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);

        using var statusJson =
            JsonDocument.Parse(
                await status.Content.ReadAsStringAsync(
                    cancellationToken));

        Assert.False(
            statusJson.RootElement
                .GetProperty("enabled")
                .GetBoolean());

        var session =
            await client.PostAsync(
                "/api/demo/session",
                content: null,
                cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, session.StatusCode);
    }

    [Fact]
    public async Task DemoSession_WhenEnabled_SignsInResidentWithoutAdminAccess()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("environment", "Development");
                    builder.UseSetting("Demo:Enabled", "true");
                });

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                HandleCookies = true
            });

        var session =
            await client.PostAsync(
                "/api/demo/session",
                content: null,
                cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, session.StatusCode);

        var me =
            await client.GetAsync(
                "/api/auth/me",
                cancellationToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var body =
            await me.Content.ReadAsStringAsync(
                cancellationToken);

        Assert.Contains(
            DemoDataSeeder.DemoResidentEmail,
            body);

        Assert.Contains(
            ApplicationRoles.Resident,
            body);

        Assert.Contains("1A", body);

        var resident =
            await client.GetAsync(
                "/api/auth/check/resident",
                cancellationToken);

        var admin =
            await client.GetAsync(
                "/api/auth/check/admin",
                cancellationToken);

        Assert.Equal(HttpStatusCode.OK, resident.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }
}
