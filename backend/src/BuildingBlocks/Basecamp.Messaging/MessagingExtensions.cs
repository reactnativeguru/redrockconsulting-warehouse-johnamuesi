using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basecamp.Messaging;

/// <summary>
/// One place where every service wires up MassTransit.
///
/// The key idea: services publish/consume via MassTransit's abstractions
/// (IPublishEndpoint, IConsumer&lt;T&gt;), never via RabbitMQ APIs directly.
/// That means the broker is a *configuration* choice:
///   Messaging:Transport = RabbitMq        (local dev, docker-compose, k8s)
///   Messaging:Transport = AzureServiceBus (Azure deployment - fully managed)
///   Messaging:Transport = InMemory        (quick demos without a broker)
/// </summary>
public static class MessagingExtensions
{
    public static IServiceCollection AddBasecampMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? registerConsumers = null)
    {
        var transport = configuration["Messaging:Transport"] ?? "RabbitMq";

        services.AddMassTransit(x =>
        {
            // Queue names derived from consumer class names, e.g.
            // OrderSubmittedConsumer -> "order-submitted" queue.
            x.SetKebabCaseEndpointNameFormatter();

            // Needed by UseDelayedRedelivery (and handy for scheduling messages yourself).
            x.AddDelayedMessageScheduler();

            // Each service registers its own consumers / outbox here.
            registerConsumers?.Invoke(x);

            switch (transport.ToLowerInvariant())
            {
                case "azureservicebus":
                    x.UsingAzureServiceBus((context, cfg) =>
                    {
                        cfg.Host(configuration.GetConnectionString("ServiceBus"));
                        cfg.UseServiceBusMessageScheduler(); // native ASB scheduled messages
                        ConfigureCommon(cfg);
                        cfg.ConfigureEndpoints(context);
                    });
                    break;

                case "inmemory":
                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.UseDelayedMessageScheduler();
                        ConfigureCommon(cfg);
                        cfg.ConfigureEndpoints(context);
                    });
                    break;

                default:
                    x.UsingRabbitMq((context, cfg) =>
                    {
                        cfg.Host(
                            configuration["Messaging:RabbitMq:Host"] ?? "localhost",
                            configuration["Messaging:RabbitMq:VirtualHost"] ?? "/",
                            h =>
                            {
                                h.Username(configuration["Messaging:RabbitMq:Username"] ?? "guest");
                                h.Password(configuration["Messaging:RabbitMq:Password"] ?? "guest");
                            });
                        // Uses the rabbitmq_delayed_message_exchange plugin
                        // (pre-installed in the masstransit/rabbitmq docker image).
                        cfg.UseDelayedMessageScheduler();
                        ConfigureCommon(cfg);
                        // Creates exchanges/queues/bindings for every registered consumer.
                        cfg.ConfigureEndpoints(context);
                    });
                    break;
            }
        });

        return services;
    }

    /// <summary>Resilience policy shared by every transport.</summary>
    private static void ConfigureCommon(IBusFactoryConfigurator cfg)
    {
        // 1) Second-level (delayed) redelivery: for longer outages (e.g. DB down).
        //    Message goes back to the broker and comes back later.
        cfg.UseDelayedRedelivery(r => r.Intervals(
            TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)));

        // 2) Immediate in-memory retry: for transient blips (deadlocks, timeouts).
        cfg.UseMessageRetry(r => r.Exponential(
            retryLimit: 5,
            minInterval: TimeSpan.FromMilliseconds(100),
            maxInterval: TimeSpan.FromSeconds(5),
            intervalDelta: TimeSpan.FromMilliseconds(200)));

        // If all retries fail, MassTransit moves the message to "<queue>_error".
        // That's our dead-letter queue: inspect it, fix the bug, re-drive the messages.
    }
}
