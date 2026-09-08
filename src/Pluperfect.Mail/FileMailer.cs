using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Pluperfect.Mail;

/// <summary>
/// Writes the composed message to disk as <c>.eml</c> rather than delivering it.
/// </summary>
/// <remarks>
/// Not a stub, and not a no-op. It composes the real MIME the recipient would receive, so the
/// result can be opened in any mail client and read exactly as they would see it — headers,
/// alternatives, encoding and all. A test that asserts against this file is asserting against the
/// message, not against a mock's recollection of one.
///
/// Selected whenever <see cref="PluperfectMailOptions.PickupDirectory"/> is set, which is how an
/// application becomes fully developable and testable with no network and no credentials anywhere.
/// </remarks>
/// <param name="options">Where to write, and the rest of the transport configuration.</param>
/// <param name="logger">Records the path written, so a developer can find the file.</param>
public sealed partial class FileMailer(
    IOptions<PluperfectMailOptions> options,
    ILogger<FileMailer> logger) : IMailer
{
    /// <inheritdoc />
    public async Task SendAsync(MailRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var directory = options.Value.PickupDirectory;

        if (string.IsNullOrWhiteSpace(directory))
        {
            // Unreachable through AddPluperfectMail, which only selects this implementation when
            // the directory is set. Reachable by anyone constructing it directly, and a clear
            // message costs less than the confusion of a NullReferenceException.
            throw new MailDeliveryException(
                "Mail:PickupDirectory is not configured, so there is nowhere to write. This is a "
                + "configuration mistake rather than a delivery failure.");
        }

        var message = Compose(request);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.eml");

        try
        {
            var stream = File.Create(path);
            await using (stream.ConfigureAwait(false))
            {
                await message.WriteToAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException ex)
        {
            throw new MailDeliveryException($"Could not write the message to {path}.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new MailDeliveryException($"Not permitted to write the message to {path}.", ex);
        }

        LogWroteToDisk(logger, path);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Wrote {Path} instead of sending: Mail:PickupDirectory is set.")]
    private static partial void LogWroteToDisk(ILogger logger, string path);

    /// <summary>Builds the MIME message a transport would put on the wire.</summary>
    /// <param name="request">The message to compose.</param>
    /// <returns>The composed message.</returns>
    internal static MimeMessage Compose(MailRequest request)
    {
        var message = new MimeMessage();
        message.From.Add(ToMailbox(request.From));
        message.To.Add(ToMailbox(request.To));

        if (request.ReplyTo is not null)
        {
            message.ReplyTo.Add(ToMailbox(request.ReplyTo));
        }

        if (request.Bcc is not null)
        {
            message.Bcc.Add(ToMailbox(request.Bcc));
        }

        message.Subject = request.Subject;
        message.Body = new BodyBuilder
        {
            HtmlBody = request.HtmlBody,
            TextBody = request.TextBody,
        }.ToMessageBody();

        return message;
    }

    private static MailboxAddress ToMailbox(MailAddress address) =>
        new(address.DisplayName ?? string.Empty, address.Address);
}
