using DiscordGeminiAdmin.SharedKernel;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;

namespace DiscordGeminiAdmin.Infrastructure.Messaging;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>Wolverine как in-process медиатор: находит *UseCase и *DomainEventHandler.</summary>
        public IServiceCollection AddMessaging(IConfiguration configuration)
        {
            return services.AddWolverine(opts =>
            {
                opts.Discovery.IncludeAssembly(typeof(DiscordGeminiAdmin.Application.DependencyInjection).Assembly);
                opts.Discovery.CustomizeHandlerDiscovery(t =>
                {
                    t.Includes.WithNameSuffix("UseCase");
                    t.Includes.WithNameSuffix("DomainEventHandler");
                });
                opts.PersistMessagesWithPostgresql(configuration.GetConnectionString("DefaultConnection")!);
                opts.UseEntityFrameworkCoreTransactions();
                opts.OnException<Exception>()
                    .RetryWithCooldown(
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(15));
                opts.PublishDomainEventsFromEntityFrameworkCore<AggregationRoot<Guid>>(x => x.DomainEvents);
            });
        }
    }
}
