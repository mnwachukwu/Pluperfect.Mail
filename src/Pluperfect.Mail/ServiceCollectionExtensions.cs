using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pluperfect.Mail;

/// <summary>Registers this library with an application's container.</summary>
public static class ServiceCollectionExtensions
{
    internal const string NoTransport =
        "Mail has no transport. Set Mail:ServerToken to send through Postmark, Mail:MailpitUrl to hand messages to "
        + "Mailpit, or Mail:PickupDirectory to write them to disk instead.";

    private const string PostmarkBaseAddress = "https://api.postmarkapp.com/";

    /// <summary>
    /// Adds <see cref="IMailer"/>, selecting a transport from configuration.
    /// </summary>
    /// <remarks>
    /// The selection rule: <c>Mail:PickupDirectory</c> wins if it is set, and messages are written
    /// to disk. Then <c>Mail:MailpitUrl</c>, and messages go to Mailpit. Otherwise
    /// <c>Mail:ServerToken</c> is required and messages go to Postmark. A configuration with none of
    /// them fails at startup rather than on the first message.
    ///
    /// The choice is made once, here, from configuration read at registration. It is not re-read
    /// per message: a box does not change its mind about whether it can send, and a transport that
    /// could flip at runtime would make an outage depend on which message you looked at.
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Configuration carrying the <c>Mail</c> section.</param>
    /// <returns>The container, for chaining.</returns>
    public static IServiceCollection AddPluperfectMail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(PluperfectMailOptions.Section);

        services.AddOptions<PluperfectMailOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PluperfectMailOptions>, PluperfectMailOptionsValidator>());

        var options = section.Get<PluperfectMailOptions>() ?? new PluperfectMailOptions();

        if (!string.IsNullOrWhiteSpace(options.PickupDirectory))
        {
            services.AddSingleton<IMailer, FileMailer>();
            return services;
        }

        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        if (!string.IsNullOrWhiteSpace(options.MailpitUrl))
        {
            var mailpit = MailpitMailer.BaseAddressFor(options.MailpitUrl);

            services.AddHttpClient(MailpitMailer.HttpClientName, client =>
            {
                client.BaseAddress = mailpit;
                client.Timeout = timeout;
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            services.AddSingleton<IMailer>(provider => new MailpitMailer(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(MailpitMailer.HttpClientName),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MailpitMailer>>()));

            return services;
        }

        // Reaching here without a token is possible only when validation is bypassed, and the
        // client below would then send unauthenticated requests that Postmark rejects one at a
        // time. Failing at registration keeps that from looking like an outage.
        if (string.IsNullOrWhiteSpace(options.ServerToken))
        {
            throw new InvalidOperationException(NoTransport);
        }

        var serverToken = options.ServerToken;

        services.AddHttpClient(PostmarkMailer.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(PostmarkBaseAddress);
            client.Timeout = timeout;
            client.DefaultRequestHeaders.Add("X-Postmark-Server-Token", serverToken);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddSingleton<IMailer>(provider => new PostmarkMailer(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(PostmarkMailer.HttpClientName),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PostmarkMailer>>()));

        return services;
    }
}
