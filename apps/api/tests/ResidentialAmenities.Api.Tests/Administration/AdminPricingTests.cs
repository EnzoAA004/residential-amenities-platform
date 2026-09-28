using System.Net;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Modules.Amenities.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Application;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Administration;

[Collection(DevelopmentSeedCollection.Name)]
public sealed class AdminPricingTests : AdminTestBase
{
    private Task<HttpResponseMessage> CreateRuleAsync(
        HttpClient admin,
        Guid? buildingId = null,
        Guid? amenityId = null,
        decimal amount = 6_000m,
        string currency = "ARS",
        string componentType = "Base",
        string useType = "SharedLeisure",
        DateTimeOffset? from = null,
        DateTimeOffset? to = null) =>
        PostJsonAsync(admin, "/api/admin/pricing/rules", new
        {
            buildingId = buildingId ?? BuildingId,
            amenityId = amenityId ?? SumId,
            componentType,
            useType,
            currency,
            amount,
            effectiveFromUtc = from,
            effectiveToUtc = to
        });

    private async Task<JsonElement> RulesAsync(HttpClient admin, string extra = "") =>
        await GetOkAsync(admin, $"/api/admin/pricing/rules?buildingId={BuildingId}&pageSize=100{extra}");

    [Fact]
    public async Task CreateRule_Valid_SupersedesTheOpenRule_AndTheNewOneQuotes()
    {
        using var admin = await LoginAdminAsync();
        using var resident = await LoginResidentAsync();

        var response = await CreateRuleAsync(admin, amount: 6_500m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(6_500m, body.GetProperty("created").GetProperty("amount").GetDecimal());
        var superseded = Assert.Single(body.GetProperty("superseded").EnumerateArray());
        Assert.Equal(5_000m, superseded.GetProperty("amount").GetDecimal());
        Assert.NotEqual(JsonValueKind.Null, superseded.GetProperty("effectiveToUtc").ValueKind);

        // The resident-facing quote now uses the new price.
        var quote = await GetOkAsync(
            resident,
            $"/api/pricing/quote?buildingId={BuildingId}&amenityId={SumId}&useType=SharedLeisure");
        Assert.Equal(6_500m, quote.GetProperty("totalAmount").GetDecimal());

        // Exactly one rule is active for the combination.
        var active = await RulesAsync(
            admin,
            $"&amenityId={SumId}&activeAtUtc={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddSeconds(5).ToString("O"))}");
        var activeShared = active.GetProperty("items").EnumerateArray()
            .Where(rule => rule.GetProperty("useType").GetString() == "SharedLeisure")
            .ToList();
        Assert.Single(activeShared);
        Assert.Equal(6_500m, activeShared[0].GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task CreateRule_RecordsCreatedAndSupersededAudit_WithSafeMetadata()
    {
        using var admin = await LoginAdminAsync();

        var body = await ReadAsync(await CreateRuleAsync(admin, amount: 7_000m));
        var createdId = body.GetProperty("created").GetProperty("id").GetGuid();
        var supersededId = body.GetProperty("superseded")[0].GetProperty("id").GetGuid();

        var created = Assert.Single(await AuditAsync(createdId, AuditAction.PriceRuleCreated));
        Assert.Equal(AdminId, created.ActorUserId);
        Assert.Equal(BuildingId, created.BuildingId);
        using var metadata = JsonDocument.Parse(created.MetadataJson!);
        Assert.Equal(SumId, metadata.RootElement.GetProperty("amenityId").GetGuid());
        Assert.Equal("SharedLeisure", metadata.RootElement.GetProperty("useType").GetString());
        Assert.Equal("Base", metadata.RootElement.GetProperty("componentType").GetString());
        Assert.Equal("ARS", metadata.RootElement.GetProperty("currency").GetString());
        Assert.Equal(7_000m, metadata.RootElement.GetProperty("amount").GetDecimal());

        var superseded = Assert.Single(await AuditAsync(supersededId, AuditAction.PriceRuleSuperseded));
        using var supersededMetadata = JsonDocument.Parse(superseded.MetadataJson!);
        Assert.NotEqual(JsonValueKind.Null, supersededMetadata.RootElement.GetProperty("effectiveToUtc").ValueKind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task CreateRule_InvalidAmount_IsRejected_AndNothingChanges(decimal amount)
    {
        using var admin = await LoginAdminAsync();
        var before = (await RulesAsync(admin)).GetProperty("totalCount").GetInt32();

        var response = await CreateRuleAsync(admin, amount: amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, (await RulesAsync(admin)).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task CreateRule_InvalidCurrencyOrType_IsRejected()
    {
        using var admin = await LoginAdminAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRuleAsync(admin, currency: "PESOS")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRuleAsync(admin, useType: "Nope")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRuleAsync(admin, componentType: "Nope")).StatusCode);
    }

    [Fact]
    public async Task CreateRule_ForAnAmenityOfAnotherBuilding_IsRejected()
    {
        using var admin = await LoginAdminAsync();

        // The seeded pilot SUM belongs to a different building.
        var response = await CreateRuleAsync(
            admin, amenityId: DevelopmentDataSeeder.PilotSumId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateRule_Backdated_IsRejected()
    {
        using var admin = await LoginAdminAsync();

        var response = await CreateRuleAsync(admin, from: DateTimeOffset.UtcNow.AddDays(-30));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateRule_ThatWouldOverlapALaterRule_IsRejectedAsAmbiguous()
    {
        using var admin = await LoginAdminAsync();

        var future = DateTimeOffset.UtcNow.AddDays(30);
        Assert.Equal(HttpStatusCode.Created, (await CreateRuleAsync(admin, amount: 9_000m, from: future)).StatusCode);

        // Open-ended from now would overlap the future rule.
        var ambiguous = await CreateRuleAsync(admin, amount: 6_000m);
        Assert.Equal(HttpStatusCode.Conflict, ambiguous.StatusCode);

        // Ending exactly where the future rule starts is unambiguous.
        var bounded = await CreateRuleAsync(admin, amount: 6_000m, to: future);
        Assert.Equal(HttpStatusCode.Created, bounded.StatusCode);
    }

    [Fact]
    public async Task ExistingReservationSnapshots_AreNotChangedByANewRule()
    {
        using var admin = await LoginAdminAsync();
        using var resident = await LoginResidentAsync();
        var id = await CreateReservationAsync(resident, new DateOnly(2028, 3, 1));
        var before = await LoadReservationAsync(id);

        await CreateRuleAsync(admin, amount: 12_000m);

        var after = await LoadReservationAsync(id);
        Assert.Equal(5_000m, after.PriceLines.Sum(line => line.Amount));
        Assert.Equal(
            before.PriceLines.Select(line => (line.PriceRuleId, line.Amount)),
            after.PriceLines.Select(line => (line.PriceRuleId, line.Amount)));

        // A reservation made after the change gets the new price.
        var later = await CreateReservationAsync(resident, new DateOnly(2028, 3, 2));
        Assert.Equal(12_000m, (await LoadReservationAsync(later)).PriceLines.Sum(line => line.Amount));
    }

    [Fact]
    public async Task CreateRule_WhenTheAuditWriteFails_TheRuleChangeRollsBackToo()
    {
        var before = await WithDbAsync(db => db.PriceRules.AsNoTracking()
            .Where(rule => rule.AmenityId == SumId && rule.UseType == ReservationUseType.SharedLeisure)
            .ToListAsync(TestContext.Current.CancellationToken));

        await WithDbAsync(async db =>
        {
            var clock = TimeProvider.System;
            var service = new PricingAdminService(
                db,
                clock,
                new PoisonedAuditRecorder(db, clock),
                new AmenityAdminService(db, new PoisonedAuditRecorder(db, clock)));

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => service.CreateRuleAsync(
                new CreatePriceRuleCommand(
                    BuildingId, SumId, PriceComponentType.Base, ReservationUseType.SharedLeisure,
                    "ARS", 99m, null, null),
                AdminId,
                TestContext.Current.CancellationToken));
            return 0;
        });

        var after = await WithDbAsync(db => db.PriceRules.AsNoTracking()
            .Where(rule => rule.AmenityId == SumId && rule.UseType == ReservationUseType.SharedLeisure)
            .ToListAsync(TestContext.Current.CancellationToken));

        // Still one rule, still open: neither the new row nor the supersede persisted.
        Assert.Equal(before.Count, after.Count);
        Assert.All(after, rule => Assert.Null(rule.EffectiveToUtc));
    }

    [Fact]
    public async Task ListRules_IsScopedToTheBuilding_AndPaginated()
    {
        using var admin = await LoginAdminAsync();

        var page = await GetOkAsync(
            admin, $"/api/admin/pricing/rules?buildingId={BuildingId}&pageSize=2&page=1");

        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal(5, page.GetProperty("totalCount").GetInt32());
        Assert.All(
            page.GetProperty("items").EnumerateArray(),
            rule => Assert.Equal(BuildingId, rule.GetProperty("buildingId").GetGuid()));
    }
}
