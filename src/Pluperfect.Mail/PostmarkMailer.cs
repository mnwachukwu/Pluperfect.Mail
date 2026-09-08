using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Pluperfect.Mail;

/// <summary>
/// Delivers messages through Postmark's HTTP API.
/// </summary>
/// <remarks>
/// HTTP rather than SMTP. There is no SMTP credential to leak, no assumption that the host can make
/// outbound connections on 587, and a failure comes back as a specific error code rather than as a
/// numeric SMTP reply that has to be parsed out of a string.
///
/// The client is configured in <see cref="ServiceCollectionExtensions.AddPluperfectMail"/>, which is
/// where the server token becomes a header. The token is never handled here, so it cannot be logged
/// by accident.
/// </remarks>
/// <param name="httpClient">The configured Postmark client.</param>
/// <param name="logger">Records message identifiers and suppressions.</param>
public sealed partial class PostmarkMailer(HttpClient httpClient, ILogger<PostmarkMailer> logger) : IMailer
{
    /// <summary>The named <see cref="HttpClient"/> this transport resolves.</summary>
    public const string HttpClientName = "Postmark";

    /// <summary>
    /// Postmark suppressed the recipient rather than delivering to them.
    /// </summary>
    /// <remarks>
    /// Returned for an address that has hard bounced or reported spam. Postmark holds the
    /// suppression deliberately, so retrying sends nothing and damages the sending reputation the
    /// suppression exists to protect.
    /// </remarks>
    private const int InactiveRecipientErrorCode = 406;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Postmark's payload is PascalCase, which is how the DTO below declares it. Leaving the
        // naming policy null keeps the property names exactly as written rather than camelCasing
        // them into fields Postmark ignores.
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public async Task SendAsync(MailRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new PostmarkSendRequest
        {
            From = Format(request.From),
            To = Format(request.To),
            Subject = request.Subject,
            HtmlBody = request.HtmlBody,
            TextBody = request.TextBody,
            ReplyTo = request.ReplyTo is null ? null : Format(request.ReplyTo),
            Bcc = request.Bcc is null ? null : Format(request.Bcc),
        };

        HttpResponseMessage response;

        try
        {
            response = await httpClient
                .PostAsJsonAsync("email", payload, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MailDeliveryException("Could not reach Postmark.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Distinguished from a caller's cancellation on purpose: a timeout is a delivery
            // failure worth reporting, while a cancelled request is the caller's own decision.
            throw new MailDeliveryException("Postmark did not answer in time.", ex);
        }

        using (response)
        {
            PostmarkSendResponse? body;

            try
            {
                body = await response.Content
                    .ReadFromJsonAsync<PostmarkSendResponse>(SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new MailDeliveryException(
                    $"Postmark answered {(int)response.StatusCode} with a body that is not JSON.", ex);
            }

            if (body is null)
            {
                throw new MailDeliveryException(
                    $"Postmark answered {(int)response.StatusCode} with an empty body.");
            }

            if (response.IsSuccessStatusCode && body.ErrorCode == 0)
            {
                LogAccepted(logger, body.MessageID);
                return;
            }

            if (body.ErrorCode == InactiveRecipientErrorCode)
            {
                // Worth its own log line rather than being folded into the generic failure: the
                // address is suppressed, so this will keep happening until somebody reactivates it
                // in Postmark, and no amount of retrying will change the outcome.
                LogRecipientSuppressed(logger, body.Message);
            }

            throw new MailDeliveryException(
                $"Postmark refused the message with error {body.ErrorCode}: {body.Message}");
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Postmark accepted the message as {MessageID}.")]
    private static partial void LogAccepted(ILogger logger, string? messageID);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Postmark suppressed the recipient and sent nothing: {Reason}")]
    private static partial void LogRecipientSuppressed(ILogger logger, string? reason);

    /// <summary>Renders an address the way an RFC 5322 header expects it.</summary>
    private static string Format(MailAddress address) =>
        string.IsNullOrWhiteSpace(address.DisplayName)
            ? address.Address
            : $"{address.DisplayName} <{address.Address}>";

    private sealed record PostmarkSendRequest
    {
        public required string From { get; init; }

        public required string To { get; init; }

        public required string Subject { get; init; }

        public required string HtmlBody { get; init; }

        public string? TextBody { get; init; }

        public string? ReplyTo { get; init; }

        public string? Bcc { get; init; }

        /// <summary>
        /// Postmark's stream for transactional mail.
        /// </summary>
        /// <remarks>
        /// Named explicitly rather than relying on the default. Postmark separates transactional
        /// from broadcast streams and applies different suppression rules to each; everything this
        /// fleet sends is transactional, and a message on the wrong stream is judged by rules that
        /// were never meant for it.
        /// </remarks>
        public string MessageStream { get; init; } = "outbound";
    }

    private sealed record PostmarkSendResponse
    {
        public int ErrorCode { get; init; }

        public string? Message { get; init; }

        public string? MessageID { get; init; }
    }
}
