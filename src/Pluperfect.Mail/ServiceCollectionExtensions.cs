using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pluperfect.Mail;

/// <summary>Registers this library with an application's container.</summary>
public static class ServiceCollectionExtensions
{
    private const string PostmarkBaseAddress = "https://api.postmarkapp.com/";

    /// <summary>
    /// Adds <see cref="IMailer"/>, selecting a transport from configuration.
    /// </summary>
    /// <remarks>
    /// The selection rule: <c>Mail:PickupDirectory</c> wins if it is set, and messages are written
    /// to disk. Otherwise <c>Mail:ServerToken</c> is required and messages go to Postmark. A
    /// configuration with neither fails at startup rather than on the first message.
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

        // Reaching here without a token is possible only when validation is bypassed, and the
        // client below would then send unauthenticated requests that Postmark rejects one at a
        // time. Failing at registration keeps that from looking like an outage.
        if (string.IsNullOrWhiteSpace(options.ServerToken))
        {
            throw new InvalidOperationException(
                "Mail has no transport. Set Mail:ServerToken to send through Postmark, or "
                + "Mail:PickupDirectory to write messages to disk instead.");
        }

        var serverToken = options.ServerToken;
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

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
