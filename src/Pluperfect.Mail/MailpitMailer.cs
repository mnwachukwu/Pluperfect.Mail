using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Pluperfect.Mail;

/// <summary>
/// Hands messages to Mailpit's HTTP Send API. Mailpit delivers nothing: it keeps each message for reading on its own
/// page, which makes it a mail server for development and for an instance whose real mail is not set up yet.
/// </summary>
/// <param name="httpClient">A client whose base address is Mailpit's, such as <c>http://localhost:8025/</c>.</param>
/// <param name="logger">Records the identifier Mailpit gives each message.</param>
public sealed partial class MailpitMailer(HttpClient httpClient, ILogger<MailpitMailer> logger) : IMailer
{
    /// <summary>The named <see cref="HttpClient"/> this transport resolves.</summary>
    public const string HttpClientName = "Mailpit";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A base address for <paramref name="mailpitUrl"/>, ending in a slash so the API's path resolves beneath it.</summary>
    /// <param name="mailpitUrl">Mailpit's address, such as <c>http://localhost:8025</c>.</param>
    /// <returns>The address as a base for <see cref="HttpClient.BaseAddress"/>.</returns>
    public static Uri BaseAddressFor(string mailpitUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailpitUrl);
        return new Uri(mailpitUrl.Trim().TrimEnd('/') + "/");
    }

    /// <inheritdoc />
    public async Task SendAsync(MailRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new SendRequest
        {
            From = ToParty(request.From),
            To = [ToParty(request.To)],
            ReplyTo = request.ReplyTo is null ? null : [ToParty(request.ReplyTo)],
            Bcc = request.Bcc is null ? null : [request.Bcc.Address],
            Subject = request.Subject,
            Html = request.HtmlBody,
            Text = request.TextBody,
        };

        var where = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "its address";
        HttpResponseMessage response;

        try
        {
            response = await httpClient
                .PostAsJsonAsync("api/v1/send", payload, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MailDeliveryException($"Could not reach Mailpit at {where}. Start Mailpit, then try again.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MailDeliveryException($"Mailpit at {where} did not answer in time.", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var sent = await ReadAsync<SendResponse>(response, cancellationToken).ConfigureAwait(false);
                LogAccepted(logger, sent?.Id);
                return;
            }

            var refusal = await ReadAsync<ErrorResponse>(response, cancellationToken).ConfigureAwait(false);
            throw new MailDeliveryException(
                $"Mailpit refused the message with {(int)response.StatusCode}: {refusal?.Error ?? response.ReasonPhrase}");
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mailpit kept the message as {Id}.")]
    private static partial void LogAccepted(ILogger logger, string? id);

    private static Party ToParty(MailAddress address) =>
        new(address.Address, string.IsNullOrWhiteSpace(address.DisplayName) ? null : address.DisplayName);

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Party(string Email, string? Name);

    private sealed record SendRequest
    {
        public required Party From { get; init; }

        public required IReadOnlyList<Party> To { get; init; }

        public IReadOnlyList<Party>? ReplyTo { get; init; }

        public IReadOnlyList<string>? Bcc { get; init; }

        public required string Subject { get; init; }

        [JsonPropertyName("HTML")]
        public required string Html { get; init; }

        public string? Text { get; init; }
    }

    private sealed record SendResponse
    {
        [JsonPropertyName("ID")]
        public string? Id { get; init; }
    }

    private sealed record ErrorResponse
    {
        public string? Error { get; init; }
    }
}
