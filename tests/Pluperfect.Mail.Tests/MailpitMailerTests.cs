using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace Pluperfect.Mail.Tests;

[TestFixture]
public sealed class MailpitMailerTests
{
    private static readonly MailRequest Everything = new()
    {
        From = new MailAddress("no-reply@pluperfect.dev", "Pluperfect Development"),
        To = new MailAddress("mattn@pluperfect.dev", "Matt"),
        ReplyTo = new MailAddress("visitor@example.com", "A Visitor"),
        Bcc = new MailAddress("archive@pluperfect.dev"),
        Subject = "Contact form",
        HtmlBody = "<p>Hello</p>",
        TextBody = "Hello",
    };

    [Test]
    public async Task Sends_every_field_in_the_shape_Mailpit_takes()
    {
        var handler = new RecordingHandler(_ => Answer(HttpStatusCode.OK, """{"ID":"abc123"}"""));

        await Mailer(handler).SendAsync(Everything);

        handler.Uris.ShouldHaveSingleItem().ToString().ShouldBe("http://mailpit.test:8025/api/v1/send");
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        var root = body.RootElement;
        root.GetProperty("From").GetProperty("Email").GetString().ShouldBe("no-reply@pluperfect.dev");
        root.GetProperty("From").GetProperty("Name").GetString().ShouldBe("Pluperfect Development");
        root.GetProperty("To")[0].GetProperty("Email").GetString().ShouldBe("mattn@pluperfect.dev");
        root.GetProperty("To")[0].GetProperty("Name").GetString().ShouldBe("Matt");
        root.GetProperty("ReplyTo")[0].GetProperty("Email").GetString().ShouldBe("visitor@example.com");
        root.GetProperty("Bcc")[0].GetString().ShouldBe("archive@pluperfect.dev");
        root.GetProperty("Subject").GetString().ShouldBe("Contact form");
        root.GetProperty("HTML").GetString().ShouldBe("<p>Hello</p>");
        root.GetProperty("Text").GetString().ShouldBe("Hello");
    }

    [Test]
    public async Task Leaves_out_the_parties_and_text_that_were_not_given()
    {
        var handler = new RecordingHandler(_ => Answer(HttpStatusCode.OK, """{"ID":"abc123"}"""));

        await Mailer(handler).SendAsync(new MailRequest
        {
            From = new MailAddress("no-reply@courtney.care"),
            To = new MailAddress("me@courtney.care"),
            Subject = "No optional parties",
            HtmlBody = "<p>Body</p>",
        });

        using var body = JsonDocument.Parse(handler.Bodies.Single());
        var root = body.RootElement;
        root.TryGetProperty("ReplyTo", out _).ShouldBeFalse();
        root.TryGetProperty("Bcc", out _).ShouldBeFalse();
        root.TryGetProperty("Text", out _).ShouldBeFalse();
        root.GetProperty("From").TryGetProperty("Name", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Says_where_it_looked_when_Mailpit_is_not_running()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("No connection could be made."));

        var thrown = await Should.ThrowAsync<MailDeliveryException>(async () => await Mailer(handler).SendAsync(Everything));

        thrown.Message.ShouldBe("Could not reach Mailpit at http://mailpit.test:8025. Start Mailpit, then try again.");
    }

    [Test]
    public async Task Reports_a_refusal_in_Mailpits_own_words()
    {
        var handler = new RecordingHandler(_ => Answer(HttpStatusCode.BadRequest, """{"Error":"invalid From address"}"""));

        var thrown = await Should.ThrowAsync<MailDeliveryException>(async () => await Mailer(handler).SendAsync(Everything));

        thrown.Message.ShouldBe("Mailpit refused the message with 400: invalid From address");
    }

    [Test]
    public void Registration_selects_Mailpit_when_its_address_is_set()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPluperfectMail(Configuration(("Mail:MailpitUrl", "http://localhost:8025"), ("Mail:ServerToken", "not-a-real-token")));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMailer>().ShouldBeOfType<MailpitMailer>();
    }

    [Test]
    public void Registration_prefers_the_pickup_directory_to_Mailpit()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPluperfectMail(Configuration(("Mail:PickupDirectory", Path.GetTempPath()), ("Mail:MailpitUrl", "http://localhost:8025")));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMailer>().ShouldBeOfType<FileMailer>();
    }

    [Test]
    public void Validation_accepts_Mailpit_alone_and_refuses_an_address_that_is_not_http()
    {
        var validator = new PluperfectMailOptionsValidator();

        validator.Validate(null, new PluperfectMailOptions { MailpitUrl = "http://localhost:8025" }).Succeeded.ShouldBeTrue();
        var refused = validator.Validate(null, new PluperfectMailOptions { MailpitUrl = "localhost:8025" });

        refused.Failed.ShouldBeTrue();
        refused.FailureMessage.ShouldContain("Mail:MailpitUrl");
    }

    private static MailpitMailer Mailer(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = MailpitMailer.BaseAddressFor("http://mailpit.test:8025") }, NullLogger<MailpitMailer>.Instance);

    private static HttpResponseMessage Answer(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Uri> Uris { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return answer(request);
        }
    }
}
