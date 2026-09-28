using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResidentialAmenities.Api.Modules.Payments.Application;
using ResidentialAmenities.Api.Modules.Payments.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Payments;

/// <summary>
/// Verifies the typed client against a fake HttpMessageHandler — the exact
/// request shape required by the Orders API, response mapping, and that
/// secrets never leak. No network, no real credentials.
/// </summary>
public sealed class MercadoPagoHttpClientTests
{
    private const string Token = "TEST-super-secret-access-token";

    private const string OrderJson = """
        {
          "id": "ORDTST01KS5AJ6HTK2HRQ3XJ3C2JCKP9",
          "type": "online",
          "processing_mode": "manual",
          "status": "created",
          "status_detail": "created",
          "external_reference": "abc123",
          "total_amount": "5000.00",
          "checkout_url": "https://www.mercadopago.com.ar/checkout/v1/redirect?order_id=ORDTST01KS5AJ6HTK2HRQ3XJ3C2JCKP9",
          "currency": "ARS"
        }
        """;

    [Fact]
    public async Task CreateOrder_SendsDocumentedRequest_AndMapsResponse()
    {
        var handler = new CapturingHandler(HttpStatusCode.Created, OrderJson);
        var client = CreateClient(handler);

        var order = await client.CreateOrderAsync(
            new CreateMercadoPagoOrderRequest("abc123", 5000m, "Amenity reservation", "PT29M59S"),
            "idem-key-1",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("/v1/orders", handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal(Token, handler.Request.Headers.Authorization.Parameter);
        Assert.Equal("idem-key-1", handler.Request.Headers.GetValues("X-Idempotency-Key").Single());

        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal("online", root.GetProperty("type").GetString());
        Assert.Equal("manual", root.GetProperty("processing_mode").GetString());
        Assert.Equal("5000.00", root.GetProperty("total_amount").GetString());
        Assert.Equal("abc123", root.GetProperty("external_reference").GetString());
        Assert.Equal("PT29M59S", root.GetProperty("expiration_time").GetString());
        Assert.Equal(
            "https://app.example.test/success",
            root.GetProperty("config").GetProperty("back_urls").GetProperty("success").GetString());

        var item = root.GetProperty("items")[0];
        Assert.Equal("5000.00", item.GetProperty("unit_price").GetString());
        Assert.Equal("5000.00", item.GetProperty("total_amount").GetString());
        Assert.Equal(1, item.GetProperty("quantity").GetInt32());

        Assert.Equal("ORDTST01KS5AJ6HTK2HRQ3XJ3C2JCKP9", order.Id);
        Assert.Equal("created", order.Status);
        Assert.Equal(5000m, order.TotalAmount);
        Assert.Equal("ARS", order.Currency);
        Assert.Equal("abc123", order.ExternalReference);
        Assert.StartsWith("https://www.mercadopago.com.ar/checkout", order.CheckoutUrl);
    }

    [Fact]
    public async Task CreateOrder_Retry_SendsSameIdempotencyKeyAndByteIdenticalBody()
    {
        var first = new CapturingHandler(HttpStatusCode.Created, OrderJson);
        var second = new CapturingHandler(HttpStatusCode.Created, OrderJson);
        var request = new CreateMercadoPagoOrderRequest("abc123", 5000m, "d", "PT10M");

        await CreateClient(first).CreateOrderAsync(
            request, "same-key", TestContext.Current.CancellationToken);
        await CreateClient(second).CreateOrderAsync(
            request, "same-key", TestContext.Current.CancellationToken);

        Assert.Equal(first.Request!.Headers.GetValues("X-Idempotency-Key").Single(),
            second.Request!.Headers.GetValues("X-Idempotency-Key").Single());
        Assert.Equal(first.Body, second.Body);
    }

    [Fact]
    public async Task GetOrder_UsesBearerToken_AndOrderPath()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, OrderJson);

        var order = await CreateClient(handler).GetOrderAsync(
            "ORDTST01KS5AJ6HTK2HRQ3XJ3C2JCKP9",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal(
            "/v1/orders/ORDTST01KS5AJ6HTK2HRQ3XJ3C2JCKP9",
            handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal(Token, handler.Request.Headers.Authorization!.Parameter);
        Assert.NotNull(order);
    }

    [Fact]
    public async Task GetOrder_NotFound_ReturnsNull()
    {
        var order = await CreateClient(new CapturingHandler(HttpStatusCode.NotFound, "{}"))
            .GetOrderAsync("X", TestContext.Current.CancellationToken);

        Assert.Null(order);
    }

    [Fact]
    public async Task ProviderError_ThrowsUnavailable_WithoutLeakingTokenOrBody()
    {
        var logger = new CapturingLogger();
        var client = CreateClient(
            new CapturingHandler(HttpStatusCode.InternalServerError, "secret-body-content"),
            logger);

        var error = await Assert.ThrowsAsync<MercadoPagoUnavailableException>(() =>
            client.GetOrderAsync("X", TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Token, error.Message);
        Assert.DoesNotContain("secret-body-content", error.Message);
        Assert.All(logger.Messages, message =>
        {
            Assert.DoesNotContain(Token, message);
            Assert.DoesNotContain("secret-body-content", message);
        });
    }

    [Fact]
    public async Task MissingAccessToken_ThrowsUnavailable_AndSendsNothing()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, OrderJson);
        var client = CreateClient(handler, accessToken: "");

        await Assert.ThrowsAsync<MercadoPagoUnavailableException>(() =>
            client.GetOrderAsync("X", TestContext.Current.CancellationToken));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task UnreadableResponse_ThrowsUnavailable()
    {
        var client = CreateClient(new CapturingHandler(HttpStatusCode.OK, "not json"));

        await Assert.ThrowsAsync<MercadoPagoUnavailableException>(() =>
            client.GetOrderAsync("X", TestContext.Current.CancellationToken));
    }

    private static MercadoPagoHttpClient CreateClient(
        HttpMessageHandler handler,
        ILogger<MercadoPagoHttpClient>? logger = null,
        string accessToken = Token) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadopago.test") },
            Options.Create(new MercadoPagoOptions
            {
                AccessToken = accessToken,
                SuccessUrl = "https://app.example.test/success",
                PendingUrl = "https://app.example.test/pending",
                FailureUrl = "https://app.example.test/failure"
            }),
            logger ?? NullLogger<MercadoPagoHttpClient>.Instance);

    private sealed class CapturingHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class CapturingLogger : ILogger<MercadoPagoHttpClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
