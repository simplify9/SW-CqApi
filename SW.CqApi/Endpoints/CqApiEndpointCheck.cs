using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SW.CqApi.Endpoints
{
    /// <summary>
    /// CqApi 8 was reached through MapControllers(); CqApi 10 needs MapCqApi(). A service
    /// upgraded without it would answer 404 to everything, so say so loudly at startup.
    /// </summary>
    internal sealed class CqApiEndpointCheck : IHostedService
    {
        private readonly IServiceProvider services;
        private readonly IHostApplicationLifetime lifetime;
        private readonly ILogger<CqApiEndpointCheck> logger;

        public CqApiEndpointCheck(IServiceProvider services, IHostApplicationLifetime lifetime, ILogger<CqApiEndpointCheck> logger)
        {
            this.services = services;
            this.lifetime = lifetime;
            this.logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            lifetime.ApplicationStarted.Register(() =>
            {
                var endpoints = services.GetService<EndpointDataSource>()?.Endpoints;
                if (endpoints == null || endpoints.Count == 0) return;

                var discovery = services.GetRequiredService<ServiceDiscovery>();
                if (discovery.Handlers.Any() && !endpoints.Any(e => e.Metadata.GetMetadata<CqApiHandlerMetadata>() != null))
                    logger.LogCritical("CqApi handlers are registered but no CqApi endpoints are mapped. Call endpoints.MapCqApi() (CqApi 10 no longer runs through MapControllers()).");
            });
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
