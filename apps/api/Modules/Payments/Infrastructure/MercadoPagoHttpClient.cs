using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResidentialAmenities.Api.Modules.Payments.Application;

namespace ResidentialAmenities.Api.Modules.Payments.Infrastructure;

/// <summary>
/// Small typed HTTP client for the two Orders API calls Checkout Pro needs.
///
/// SDK decision: the official <c>mercadopago-sdk</c> NuGet package (3.x,
/// .NET 8+) does expose an Orders client, but this integration needs exact
/// control of the persisted <c>X-Idempotency-Key</c>, the persisted
/// <c>expiration_time</c>, <c>config.back_urls</c>, and the
/// <c>checkout_url</c> response field, and those specifics could not be
/// confirmed from the SDK's published surface. The surface we need is two
/// endpoints and a small HMAC check, so a typed client keeps the request
/// body fully explicit, keeps tests offline (a fake HttpMessageHandler),
/// and avoids a large dependency of unverified coverage.
///
/// Security: the access token is read from configuration per request, sent
/// only in the Authorization header, and never logged; error bodies are
/// never logged (only status codes).
/// </summary>
public sealed class MercadoPagoHttpClient(
    HttpClient httpClient,
    IOptions<MercadoPagoOptions> options,
    ILogger<MercadoPagoHttpClient> logger) : IMercadoPagoClient
{
    public async Task<MercadoPagoOrder> CreateOrderAsync(
        CreateMercadoPagoOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var amount = FormatAmount(request.TotalAmount);
        var settings = options.Value;

        var body = new Dictionary<string, object?>
        {
            ["type"] = "online",
            ["processing_mode"] = "manual",
            ["total_amount"] = amount,
            ["external_reference"] = request.ExternalReference,
            ["description"] = request.Description,
            ["expiration_time"] = request.ExpirationTime,
            ["items"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["title"] = request.Description,
                    ["unit_price"] = amount,
                    ["quantity"] = 1,
                    ["unit_measure"] = "unit",
                    ["total_amount"] = amount
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.SuccessUrl) &&
            !string.IsNullOrWhiteSpace(settings.PendingUrl) &&
            !string.IsNullOrWhiteSpace(settings.FailureUrl))
        {
            body["config"] = new Dictionary<string, object?>
            {
                ["back_urls"] = new Dictionary<string, object?>
                {
                    ["success"] = settings.SuccessUrl,
                    ["failure"] = settings.FailureUrl,
                    ["pending"] = settings.PendingUrl
                }
            };
        }

        using var message = BuildRequest(HttpMethod.Post, "/v1/orders");
        message.Headers.Add("X-Idempotency-Key", idempotencyKey);
        message.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var order = await SendAsync(message, cancellationToken, allowNotFound: false);
        return order ?? throw new MercadoPagoUnavailableException(
            "Mercado Pago returned no order.");
    }

    public async Task<MercadoPagoOrder?> GetOrderAsync(
        string orderId,
        CancellationToken cancellationToken)
    {
        using var message = BuildRequest(
            HttpMethod.Get,
            $"/v1/orders/{Uri.EscapeDataString(orderId)}");

        return await SendAsync(message, cancellationToken, allowNotFound: true);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path)
    {
        var accessToken = options.Value.AccessToken;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new MercadoPagoUnavailableException(
                "MercadoPago:AccessToken is not configured.");
        }

        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return message;
    }

    private async Task<MercadoPagoOrder?> SendAsync(
        HttpRequestMessage message,
        CancellationToken cancellationToken,
        bool allowNotFound)
    {
        HttpResponseMessage response;

        try
        {
            response = await httpClient.SendAsync(message, cancellationToken);
        }
        catch (Exception error) when (
            error is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            throw new MercadoPagoUnavailableException(
                "Mercado Pago could not be reached.",
                error);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Status only: the body may echo request data.
                logger.LogWarning(
                    "Mercado Pago {Method} {Path} failed with status {StatusCode}.",
                    message.Method,
                    message.RequestUri?.AbsolutePath,
                    (int)response.StatusCode);

                throw new MercadoPagoUnavailableException(
                    $"Mercado Pago responded with {(int)response.StatusCode}.");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(json);
        }
    }

    internal static MercadoPagoOrder Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            return new MercadoPagoOrder(
                root.GetProperty("id").GetString()!,
                root.GetProperty("status").GetString() ?? string.Empty,
                root.TryGetProperty("status_detail", out var detail)
                    ? detail.GetString() ?? string.Empty
                    : string.Empty,
                root.TryGetProperty("external_reference", out var reference)
                    ? reference.GetString() ?? string.Empty
                    : string.Empty,
                decimal.Parse(
                    root.GetProperty("total_amount").GetString()!,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture),
                root.TryGetProperty("currency", out var currency)
                    ? currency.GetString() ?? string.Empty
                    : string.Empty,
                root.TryGetProperty("checkout_url", out var checkout)
                    ? checkout.GetString()
                    : null);
        }
        catch (Exception error) when (
            error is JsonException or KeyNotFoundException or FormatException
                or InvalidOperationException)
        {
            throw new MercadoPagoUnavailableException(
                "Mercado Pago returned an unreadable order.",
                error);
        }
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);
}
