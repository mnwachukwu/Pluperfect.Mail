using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Pluperfect.Mail;

/// <summary>
/// Everything this library reads from configuration.
/// </summary>
/// <remarks>
/// Bound to the <c>Mail</c> section, which a consuming application also binds its own options to.
/// Two classes over one section is deliberate and works: each reads only the keys it declares. The
/// division is that transport belongs here and correspondents belong to the application — this
/// library has no opinion about who a contact form mails, and an application has no business
/// holding a server token.
/// </remarks>
public sealed class PluperfectMailOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string Section = "Mail";

    /// <summary>
    /// When set, messages are written here as <c>.eml</c> instead of being delivered.
    /// </summary>
    /// <remarks>
    /// The offline path. A composed message can be opened and read exactly as its recipient would
    /// see it, with no network, no credentials and no risk of mailing a real person from a
    /// development machine.
    ///
    /// Setting this takes precedence over <see cref="ServerToken"/>. A box configured with both
    /// writes files and sends nothing, which is why production must leave this empty.
    /// </remarks>
    public string? PickupDirectory { get; set; }

    /// <summary>
    /// Mailpit's address, such as <c>http://localhost:8025</c>. When set, and no pickup directory is, messages go to
    /// Mailpit, which keeps them for reading on its own page and delivers nothing.
    /// </summary>
    public string? MailpitUrl { get; set; }

    /// <summary>
    /// The Postmark <em>server</em> token this application sends with.
    /// </summary>
    /// <remarks>
    /// A server token, not an account token: it can send mail from one Postmark server and can do
    /// nothing else, so a leaked one costs a single project's sending rather than the account.
    ///
    /// Belongs in the deployment's own configuration file on its own box, mode <c>0600</c>, or in
    /// an environment variable. It does not belong in a repository, in a container image, or in a
    /// CI secret — a CI secret is reachable by every workflow run and everyone who can approve one,
    /// and CI has no reason to send mail.
    /// </remarks>
    public string? ServerToken { get; set; }

    /// <summary>
    /// How long to wait on Postmark before giving up.
    /// </summary>
    /// <remarks>
    /// Short on purpose. The usual caller is an HTTP request a person is waiting on, and a contact
    /// form that hangs for thirty seconds is worse for that person than one that fails quickly and
    /// says so.
    /// </remarks>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Enforces the one rule data annotations cannot express: exactly one transport must be configured.
/// </summary>
/// <remarks>
/// Registered with <c>ValidateOnStart</c>, so a box missing its token fails when the unit starts and
/// systemd reports it, rather than on the first visitor who submits a form. A mail server that
/// silently accepts a message it cannot deliver is the failure this exists to prevent.
/// </remarks>
public sealed class PluperfectMailOptionsValidator : IValidateOptions<PluperfectMailOptions>
{
    /// <summary>Validates the options.</summary>
    /// <param name="name">The named options instance, unused.</param>
    /// <param name="options">The options to validate.</param>
    /// <returns>Success, or a failure naming the setting to fix.</returns>
    public ValidateOptionsResult Validate(string? name, PluperfectMailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hasPickup = !string.IsNullOrWhiteSpace(options.PickupDirectory);
        var hasMailpit = !string.IsNullOrWhiteSpace(options.MailpitUrl);
        var hasToken = !string.IsNullOrWhiteSpace(options.ServerToken);

        if (!hasPickup && !hasMailpit && !hasToken)
        {
            return ValidateOptionsResult.Fail(ServiceCollectionExtensions.NoTransport);
        }

        if (hasMailpit && !IsWebAddress(options.MailpitUrl!))
        {
            return ValidateOptionsResult.Fail(
                "Mail:MailpitUrl must be an http or https address, such as http://localhost:8025.");
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>Whether <paramref name="value"/> is an absolute http or https address.</summary>
    /// <param name="value">The address to check.</param>
    /// <returns>True for an absolute http or https address.</returns>
    public static bool IsWebAddress(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
