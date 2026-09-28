using System.Collections.Concurrent;
using ResidentialAmenities.Api.Modules.Payments.Application;

namespace ResidentialAmenities.Api.Tests.Infrastructure;

/// <summary>
/// In-memory stand-in for Mercado Pago (no network, per issue #24). It
/// behaves like the real Orders API where it matters for these tests:
/// creating an order twice with the same idempotency key returns the same
/// order instead of creating another, and a "lost response" can be
/// simulated (the order IS created remotely but the caller sees a failure).
/// </summary>
public sealed class FakeMercadoPagoClient : IMercadoPagoClient
{
    private readonly ConcurrentDictionary<string, MercadoPagoOrder> _ordersById = new();
    private readonly ConcurrentDictionary<string, string> _orderIdByIdempotencyKey = new();

    public ConcurrentQueue<(string IdempotencyKey, CreateMercadoPagoOrderRequest Request)>
        CreateCalls { get; } = new();

    public int GetCalls;

    /// <summary>Simulate: provider creates the order, then our connection drops before we read the response.</summary>
    public bool LoseNextCreateResponse { get; set; }

    public bool FailGets { get; set; }

    public int DistinctOrdersCreated => _ordersById.Count;

    public Task<MercadoPagoOrder> CreateOrderAsync(
        CreateMercadoPagoOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        CreateCalls.Enqueue((idempotencyKey, request));

        var orderId = _orderIdByIdempotencyKey.GetOrAdd(
            idempotencyKey,
            // Globally unique: the database outlives each test's fake, and
            // ProviderOrderId is unique there.
            _ => $"ORDFAKE{Guid.NewGuid():N}");

        var order = _ordersById.GetOrAdd(
            orderId,
            id => new MercadoPagoOrder(
                id,
                "created",
                "created",
                request.ExternalReference,
                request.TotalAmount,
                "ARS",
                $"https://fake.mercadopago.test/checkout?order_id={id}"));

        if (LoseNextCreateResponse)
        {
            LoseNextCreateResponse = false;
            throw new MercadoPagoUnavailableException("simulated connection loss");
        }

        return Task.FromResult(order);
    }

    public Task<MercadoPagoOrder?> GetOrderAsync(
        string orderId,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref GetCalls);

        if (FailGets)
        {
            throw new MercadoPagoUnavailableException("simulated outage");
        }

        return Task.FromResult(_ordersById.TryGetValue(orderId, out var order) ? order : null);
    }

    public void SetStatus(string orderId, string status, string statusDetail) =>
        _ordersById[orderId] = _ordersById[orderId] with
        {
            Status = status,
            StatusDetail = statusDetail
        };

    public void Tamper(
        string orderId,
        decimal? totalAmount = null,
        string? currency = null,
        string? externalReference = null) =>
        _ordersById[orderId] = _ordersById[orderId] with
        {
            TotalAmount = totalAmount ?? _ordersById[orderId].TotalAmount,
            Currency = currency ?? _ordersById[orderId].Currency,
            ExternalReference = externalReference ?? _ordersById[orderId].ExternalReference
        };
}
