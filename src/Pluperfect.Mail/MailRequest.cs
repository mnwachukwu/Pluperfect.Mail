namespace Pluperfect.Mail;

/// <summary>A party on a message. Display name is optional; the address is not.</summary>
/// <param name="Address">The email address. Required.</param>
/// <param name="DisplayName">What a client shows instead of the address. Optional.</param>
public sealed record MailAddress(string Address, string? DisplayName = null);

/// <summary>
/// One outbound message.
/// </summary>
/// <remarks>
/// The contract every consumer codes against. A change to this shape is a breaking change across
/// every repository that references this library.
///
/// Deliberately narrow. There is no attachment support, no templating, no batching and no
/// scheduling, because nothing in this fleet sends mail that needs any of them. Each is easy to add
/// later and impossible to remove once a caller depends on it.
/// </remarks>
public sealed record MailRequest
{
    /// <summary>Who the message is from. Must be on a domain the transport is allowed to send as.</summary>
    public required MailAddress From { get; init; }

    /// <summary>Who the message is to.</summary>
    public required MailAddress To { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>The HTML body.</summary>
    public required string HtmlBody { get; init; }

    /// <summary>
    /// Optional plain-text alternative.
    /// </summary>
    /// <remarks>
    /// Worth filling in. A message with no text part is a small negative signal to spam filters,
    /// and it is the version a screen reader or a text-mode client gets.
    /// </remarks>
    public string? TextBody { get; init; }

    /// <summary>Where a reply should go, when that is not the <see cref="From"/> address.</summary>
    /// <remarks>
    /// The usual case for a contact form: the message is from <c>no-reply@</c> so it authenticates
    /// against a domain the server owns, while replying should reach the person who filled the form
    /// in. Setting <see cref="From"/> to the visitor's address instead would fail DMARC.
    /// </remarks>
    public MailAddress? ReplyTo { get; init; }

    /// <summary>An optional blind copy.</summary>
    public MailAddress? Bcc { get; init; }
}
