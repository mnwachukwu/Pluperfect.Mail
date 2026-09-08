using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using NUnit.Framework;
using Shouldly;

namespace Pluperfect.Mail.Tests;

/// <summary>
/// Asserts against the composed message rather than against a mock.
/// </summary>
/// <remarks>
/// Reading the <c>.eml</c> back with MimeKit exercises the same composition a real transport puts on
/// the wire, so a header that would be wrong in production is wrong here too.
/// </remarks>
[TestFixture]
public sealed class FileMailerTests
{
    private string directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), $"pluperfect-mail-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Writes_a_readable_message_carrying_every_field()
    {
        var mailer = CreateMailer();

        await mailer.SendAsync(new MailRequest
        {
            From = new MailAddress("no-reply@pluperfect.dev", "Pluperfect Development"),
            To = new MailAddress("mattn@pluperfect.dev"),
            ReplyTo = new MailAddress("visitor@example.com", "A Visitor"),
            Bcc = new MailAddress("archive@pluperfect.dev"),
            Subject = "Contact form",
            HtmlBody = "<p>Hello</p>",
            TextBody = "Hello",
        });

        var written = Directory.GetFiles(directory, "*.eml").ShouldHaveSingleItem();
        var message = await MimeMessage.LoadAsync(written);

        message.From.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("no-reply@pluperfect.dev");
        message.From.Mailboxes.Single().Name.ShouldBe("Pluperfect Development");
        message.To.Mailboxes.Single().Address.ShouldBe("mattn@pluperfect.dev");
        message.ReplyTo.Mailboxes.Single().Address.ShouldBe("visitor@example.com");
        message.Bcc.Mailboxes.Single().Address.ShouldBe("archive@pluperfect.dev");
        message.Subject.ShouldBe("Contact form");
        message.HtmlBody.ShouldNotBeNull().ShouldContain("Hello");
        message.TextBody.ShouldBe("Hello");
    }

    [Test]
    public async Task Omits_optional_parties_that_were_not_supplied()
    {
        var mailer = CreateMailer();

        await mailer.SendAsync(new MailRequest
        {
            From = new MailAddress("no-reply@courtney.care"),
            To = new MailAddress("me@courtney.care"),
            Subject = "No optional parties",
            HtmlBody = "<p>Body</p>",
        });

        var message = await MimeMessage.LoadAsync(Directory.GetFiles(directory, "*.eml").Single());

        message.ReplyTo.ShouldBeEmpty();
        message.Bcc.ShouldBeEmpty();
        message.TextBody.ShouldBeNull();
    }

    [Test]
    public void Reports_a_missing_pickup_directory_as_configuration_rather_than_delivery()
    {
        var mailer = new FileMailer(
            Options.Create(new PluperfectMailOptions()),
            NullLogger<FileMailer>.Instance);

        var request = new MailRequest
        {
            From = new MailAddress("no-reply@pluperfect.dev"),
            To = new MailAddress("mattn@pluperfect.dev"),
            Subject = "Nowhere to write",
            HtmlBody = "<p>Body</p>",
        };

        var thrown = Should.ThrowAsync<MailDeliveryException>(
            async () => await mailer.SendAsync(request)).Result;

        thrown.Message.ShouldContain("Mail:PickupDirectory");
    }

    [Test]
    public void Registration_selects_the_file_transport_when_a_pickup_directory_is_set()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPluperfectMail(Configuration(("Mail:PickupDirectory", directory)));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMailer>().ShouldBeOfType<FileMailer>();
    }

    [Test]
    public void Registration_selects_Postmark_when_only_a_server_token_is_set()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPluperfectMail(Configuration(("Mail:ServerToken", "not-a-real-token")));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMailer>().ShouldBeOfType<PostmarkMailer>();
    }

    [Test]
    public void Registration_refuses_a_configuration_with_no_transport_at_all()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Should.Throw<InvalidOperationException>(
            () => services.AddPluperfectMail(Configuration()));
    }

    private FileMailer CreateMailer() => new(
        Options.Create(new PluperfectMailOptions { PickupDirectory = directory }),
        NullLogger<FileMailer>.Instance);

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
}
