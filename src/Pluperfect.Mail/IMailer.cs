namespace Pluperfect.Mail;

/// <summary>
/// How an application sends mail.
/// </summary>
/// <remarks>
/// One method, deliberately. A caller composes a <see cref="MailRequest"/> and hands it over; which
/// transport carries it is a deployment decision expressed in configuration, not a choice the call
/// site makes. That is what lets the same code path be exercised offline, against a directory of
/// <c>.eml</c> files, and in production against Postmark.
///
/// Implementations throw <see cref="MailDeliveryException"/> when a message cannot be delivered.
/// Nothing here returns a status code: a caller that wants to know whether mail was sent should
/// treat a thrown exception as the answer, because a return value invites being ignored.
/// </remarks>
public interface IMailer
{
    /// <summary>Delivers one message.</summary>
    /// <param name="request">The message to send.</param>
    /// <param name="cancellationToken">Cancels the delivery attempt.</param>
    /// <exception cref="MailDeliveryException">The message could not be delivered.</exception>
    Task SendAsync(MailRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when a message cannot be delivered.</summary>
/// <remarks>
/// One exception type for every transport, so a caller's error handling does not have to know
/// whether it is talking to a file system or an HTTP API. <see cref="Exception.InnerException"/>
/// carries the transport's own failure where there is one.
/// </remarks>
public sealed class MailDeliveryException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public MailDeliveryException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying failure.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The transport's own exception.</param>
    public MailDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with no message.</summary>
    public MailDeliveryException()
    {
    }
}
