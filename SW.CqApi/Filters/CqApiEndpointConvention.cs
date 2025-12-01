using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SW.CqApi.Filters
{
    /// <summary>
    /// Applies custom attributes from handlers to the CqApi controller actions
    /// </summary>
    internal class CqApiEndpointConvention : IControllerModelConvention
    {
        private readonly ServiceDiscovery _serviceDiscovery;
        private readonly CqApiOptions _options;

        public CqApiEndpointConvention(ServiceDiscovery serviceDiscovery, CqApiOptions options)
        {
            _serviceDiscovery = serviceDiscovery;
            _options = options;
        }

        public void Apply(ControllerModel controller)
        {
            // Only apply to CqApiController
            if (controller.ControllerType.Name != "CqApiController")
                return;

            // For each action in the controller, we could potentially add metadata
            // However, since CqApi uses dynamic routing, we'll add a filter instead
            foreach (var action in controller.Actions)
            {
                // Add a custom filter that will apply attributes at runtime
                action.Filters.Add(new CqApiAttributeFilter(_serviceDiscovery, _options));
            }
        }
    }
}
