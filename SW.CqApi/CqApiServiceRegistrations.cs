using Microsoft.Extensions.DependencyInjection;

namespace SW.CqApi
{
    /// <summary>
    /// The application's service registrations, kept so <see cref="ServiceDiscovery"/> can read
    /// handler types without constructing handlers. Read once, after the container is built.
    /// </summary>
    internal sealed class CqApiServiceRegistrations
    {
        public CqApiServiceRegistrations(IServiceCollection services) => Services = services;

        public IServiceCollection Services { get; }
    }
}
