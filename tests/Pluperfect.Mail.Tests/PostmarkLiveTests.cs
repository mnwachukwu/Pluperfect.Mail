using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace Pluperfect.Mail.Tests;

/// <summary>
/// Sends a real message through Postmark.
/// </summary>
/// <remarks>
/// Excluded from CI by <c>TestCategory!=Live</c>, and self-ignoring when the token is absent, so a
/// developer without credentials sees a skip rather than a failure.
///
/// It exists because every other test here proves composition rather than delivery. Postmark's
/// contract — the header it wants, the casing of the payload, the shape of a refusal — is the one
/// thing no offline test can check, and it is exactly where a mail library breaks silently.
///
/// Run it with the token in the environment:
///
///   $env:POSTMARK_SERVER_TOKEN = (Get-Content "$env:USERPROFILE\.secrets\postmark-server-pluperfect" -Raw).Trim()
///   dotnet test --filter TestCategory=Live
///
/// One token per project, named for the project it belongs to. Which one you use decides whose
/// activity log the message appears in, not which domain it may send as - sender domains are
/// account-level in Postmark and every server can use all of them.
/// </remarks>
[TestFixture]
[Category("Live")]
public sealed class PostmarkLiveTests
{
    private const string TokenVariable = "POSTMARK_SERVER_TOKEN";
    private const string RecipientVariable = "POSTMARK_TEST_RECIPIENT";
    private const string SenderVariable = "POSTMARK_TEST_SENDER";

    [Test]
    public async Task Sends_a_real_message_through_Postmark()
    {
        var token = Environment.GetEnvironmentVariable(TokenVariable);
        var recipient = Environment.GetEnvironmentVariable(RecipientVariable);
        var sender = Environment.GetEnvironmentVariable(SenderVariable);

        if (string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(recipient)
            || string.IsNullOrWhiteSpace(sender))
        {
            Assert.Ignore(
                $"Set {TokenVariable}, {SenderVariable} and {RecipientVariable} to run this.");
            return;
        }

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.postmarkapp.com/"),
            Timeout = TimeSpan.FromSeconds(20),
        };
        httpClient.DefaultRequestHeaders.Add("X-Postmark-Server-Token", token);
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        var mailer = new PostmarkMailer(httpClient, NullLogger<PostmarkMailer>.Instance);

        var request = new MailRequest
        {
            From = new MailAddress(sender, "Pluperfect.Mail"),
            To = new MailAddress(recipient),
            Subject = "Pluperfect.Mail live delivery test",
            HtmlBody = "<p>Sent by <code>PostmarkMailer</code> over Postmark's HTTP API.</p>",
            TextBody = "Sent by PostmarkMailer over Postmark's HTTP API.",
        };

        // Not wrapped in Should.NotThrow: a MailDeliveryException carries Postmark's own error code
        // and message, and letting it surface puts that text in the failure report where it is
        // actually useful.
        await mailer.SendAsync(request);

        Assert.Pass("Postmark accepted the message.");
    }

    [Test]
    public async Task Reports_a_refusal_as_MailDeliveryException()
    {
        var token = Environment.GetEnvironmentVariable(TokenVariable);

        if (string.IsNullOrWhiteSpace(token))
        {
            Assert.Ignore($"Set {TokenVariable} to run this.");
            return;
        }

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.postmarkapp.com/"),
            Timeout = TimeSpan.FromSeconds(20),
        };
        httpClient.DefaultRequestHeaders.Add("X-Postmark-Server-Token", token);
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        var mailer = new PostmarkMailer(httpClient, NullLogger<PostmarkMailer>.Instance);

        // A sender on a domain this account does not own. Postmark refuses it, which is what proves
        // a refusal is surfaced as MailDeliveryException rather than swallowed.
        var request = new MailRequest
        {
            From = new MailAddress("nobody@example.invalid"),
            To = new MailAddress("nobody@example.invalid"),
            Subject = "Should be refused",
            HtmlBody = "<p>Should be refused.</p>",
        };

        var thrown = await Should.ThrowAsync<MailDeliveryException>(
            async () => await mailer.SendAsync(request));

        thrown.Message.ShouldContain("Postmark refused");
    }
}
