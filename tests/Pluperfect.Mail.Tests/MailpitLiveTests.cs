using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace Pluperfect.Mail.Tests;

/// <summary>
/// Sends a message to a running Mailpit and finds it there. Excluded from CI by <c>TestCategory!=Live</c>, and skipped
/// unless <c>MAILPIT_URL</c> names one, such as <c>http://localhost:8025</c>.
/// </summary>
[TestFixture]
[Category("Live")]
public sealed class MailpitLiveTests
{
    private const string UrlVariable = "MAILPIT_URL";

    [Test]
    public async Task Mailpit_keeps_a_message_sent_through_its_Send_API()
    {
        var url = Environment.GetEnvironmentVariable(UrlVariable);
        if (string.IsNullOrWhiteSpace(url))
        {
            Assert.Ignore($"Set {UrlVariable} to a running Mailpit to run this.");
            return;
        }

        using var httpClient = new HttpClient { BaseAddress = MailpitMailer.BaseAddressFor(url), Timeout = TimeSpan.FromSeconds(10) };
        var subject = $"Pluperfect.Mail live test {Guid.NewGuid():N}";

        await new MailpitMailer(httpClient, NullLogger<MailpitMailer>.Instance).SendAsync(new MailRequest
        {
            From = new MailAddress("no-reply@pluperfect.test", "Pluperfect.Mail"),
            To = new MailAddress("someone@pluperfect.test"),
            Subject = subject,
            HtmlBody = "<p>Sent by <code>MailpitMailer</code> over Mailpit's Send API.</p>",
            TextBody = "Sent by MailpitMailer over Mailpit's Send API.",
        });

        var found = await httpClient.GetFromJsonAsync<JsonElement>($"api/v1/search?query={Uri.EscapeDataString($"subject:\"{subject}\"")}");
        found.GetProperty("messages").EnumerateArray().Count(m => m.GetProperty("Subject").GetString() == subject).ShouldBe(1);
    }
}
