using System.Reflection;
using System.Text.Json;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Audit;

public sealed class AuditDomainTests
{
    private static readonly DateTimeOffset Now = new(2027, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static AuditLog Create(
        AuditActorType actorType = AuditActorType.System,
        Guid? actorUserId = null,
        AuditAction action = AuditAction.ReservationExpired,
        AuditTargetType targetType = AuditTargetType.Reservation,
        string? metadata = null) =>
        new(
            Guid.NewGuid(),
            Now,
            Guid.NewGuid(),
            actorType,
            actorUserId,
            action,
            targetType,
            Guid.NewGuid(),
            "trace-1",
            metadata);

    [Fact]
    public void AuditLog_RequiresAKnownAction() =>
        Assert.Throws<ArgumentException>(() => Create(action: (AuditAction)9999));

    [Fact]
    public void AuditLog_RequiresAKnownTargetType() =>
        Assert.Throws<ArgumentException>(() => Create(targetType: (AuditTargetType)9999));

    [Fact]
    public void UserActor_RequiresTheUserId()
    {
        Assert.Throws<ArgumentException>(() => Create(AuditActorType.User, actorUserId: null));
        Assert.Throws<ArgumentException>(() => Create(AuditActorType.User, actorUserId: Guid.Empty));

        var log = Create(AuditActorType.User, Guid.NewGuid());
        Assert.NotNull(log.ActorUserId);
    }

    [Theory]
    [InlineData(AuditActorType.System)]
    [InlineData(AuditActorType.ExternalProvider)]
    public void NonUserActors_HaveNoUserId(AuditActorType actorType)
    {
        Assert.Null(Create(actorType).ActorUserId);
        Assert.Throws<ArgumentException>(() => Create(actorType, Guid.NewGuid()));
    }

    [Fact]
    public void AuditLog_IsImmutableFromItsPublicModel()
    {
        var mutableProperties = typeof(AuditLog)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true });

        Assert.Empty(mutableProperties);

        var publicMutators = typeof(AuditLog)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName);

        Assert.Empty(publicMutators);
    }

    [Fact]
    public void Metadata_SerializesToSmallCamelCaseJson()
    {
        var reservationId = Guid.NewGuid();

        var metadata = AuditMetadata.PaymentSettled(
            "Cash", reservationId, 5_000m, "ARS", "ReservationConfirmed");

        using var document = JsonDocument.Parse(metadata.Json);
        var root = document.RootElement;

        Assert.Equal("Cash", root.GetProperty("method").GetString());
        Assert.Equal(reservationId, root.GetProperty("reservationId").GetGuid());
        Assert.Equal(5_000m, root.GetProperty("amount").GetDecimal());
        Assert.Equal("ARS", root.GetProperty("currency").GetString());
        Assert.Equal("ReservationConfirmed", root.GetProperty("reservationOutcome").GetString());
        Assert.True(metadata.Json.Length < 300);
    }

    [Fact]
    public void Metadata_TypedHelpersOnlyEverEmitAllowlistedKeys_AndNothingSensitive()
    {
        var reservationId = Guid.NewGuid();
        var all = new[]
        {
            AuditMetadata.ReservationCreated("Event", Now, Now.AddHours(1), 3, "Pending"),
            AuditMetadata.ReservationConfirmed(),
            AuditMetadata.ReservationExpired(Now),
            AuditMetadata.ReservationCancelled("moved by admin"),
            AuditMetadata.PaymentInitiated(reservationId, 1m, "ARS", "MercadoPago"),
            AuditMetadata.PaymentSettled("MercadoPago", reservationId, 1m, "ARS", "ApprovedAfterExpiry"),
            AuditMetadata.PaymentTransition("MercadoPago", reservationId),
            AuditMetadata.ManualReview("ApprovedAfterExpiry", reservationId),
            AuditMetadata.WebhookProcessed("Reconciled"),
            AuditMetadata.AuthenticationFailure("InvalidCredentials")
        };

        var forbidden = new[]
        {
            "password", "token", "cookie", "secret", "authorization", "signature",
            "connection", "checkout", "idempotency", "email", "card", "body"
        };

        foreach (var metadata in all)
        {
            using var document = JsonDocument.Parse(metadata.Json);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                Assert.Contains(property.Name, AuditMetadata.AllowedKeys);
            }

            foreach (var word in forbidden)
            {
                Assert.DoesNotContain(word, metadata.Json, StringComparison.OrdinalIgnoreCase);
            }
        }

        foreach (var key in AuditMetadata.AllowedKeys)
        {
            foreach (var word in forbidden)
            {
                Assert.DoesNotContain(word, key, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Metadata_CannotBeBuiltFromAnArbitraryObjectOrDictionary()
    {
        Assert.Empty(typeof(AuditMetadata).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var factories = typeof(AuditMetadata)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(AuditMetadata));

        foreach (var factory in factories)
        {
            foreach (var parameter in factory.GetParameters())
            {
                Assert.NotEqual(typeof(object), parameter.ParameterType);
                Assert.False(
                    parameter.ParameterType.IsGenericType &&
                    parameter.ParameterType.GetGenericTypeDefinition() == typeof(Dictionary<,>));
                Assert.NotEqual(typeof(JsonElement), parameter.ParameterType);
            }
        }
    }

    [Fact]
    public void CancellationReason_IsTruncated()
    {
        var metadata = AuditMetadata.ReservationCancelled(new string('x', 5_000));

        using var document = JsonDocument.Parse(metadata.Json);
        Assert.Equal(
            AuditMetadata.MaxReasonLength,
            document.RootElement.GetProperty("reason").GetString()!.Length);
    }

    [Fact]
    public void ActorFactories_StateTheActorInvariantAtTheCallSite()
    {
        var user = AuditRecord.ByUser(
            Guid.NewGuid(), AuditAction.Logout, AuditTargetType.User, null, null);
        var system = AuditRecord.BySystem(
            AuditAction.ReservationExpired, AuditTargetType.Reservation, Guid.NewGuid(), null);
        var provider = AuditRecord.ByExternalProvider(
            AuditAction.PaymentApproved, AuditTargetType.Payment, Guid.NewGuid(), null);

        Assert.Equal(AuditActorType.User, user.ActorType);
        Assert.NotNull(user.ActorUserId);
        Assert.Equal(AuditActorType.System, system.ActorType);
        Assert.Null(system.ActorUserId);
        Assert.Equal(AuditActorType.ExternalProvider, provider.ActorType);
        Assert.Null(provider.ActorUserId);
    }
}
