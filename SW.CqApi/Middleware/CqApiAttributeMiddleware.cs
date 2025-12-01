using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SW.CqApi.Middleware
{
    /// <summary>
    /// Middleware that dynamically applies custom attributes from handlers to the current endpoint metadata.
    /// This allows ASP.NET Core middleware (like rate limiting) to see attributes from CqApi handlers.
    /// </summary>
    public class CqApiAttributeMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ServiceDiscovery _serviceDiscovery;
        private readonly CqApiOptions _options;

        public CqApiAttributeMiddleware(
            RequestDelegate next,
            ServiceDiscovery serviceDiscovery,
            CqApiOptions options)
        {
            _next = next;
            _serviceDiscovery = serviceDiscovery;
            _options = options;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Check if this is a CqApi request
            var path = context.Request.Path.Value;
            if (string.IsNullOrEmpty(path) || !path.Contains($"/{_options.UrlPrefix}/"))
            {
                await _next(context);
                return;
            }

            // Parse the path to extract resource name and handler key
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var prefixIndex = Array.FindIndex(segments, s => s.Equals(_options.UrlPrefix, StringComparison.OrdinalIgnoreCase));
            
            if (prefixIndex < 0 || prefixIndex >= segments.Length - 1)
            {
                await _next(context);
                return;
            }

            var resourceName = segments[prefixIndex + 1];
            
            // Determine handler key based on HTTP method and path structure
            string handlerKey = DetermineHandlerKey(context.Request.Method, segments, prefixIndex);

            // Try to resolve the handler
            if (_serviceDiscovery.TryResolveHandler(resourceName, handlerKey, out var handlerInfo))
            {
                // Get the endpoint
                var endpoint = context.GetEndpoint();
                if (endpoint != null && handlerInfo.CustomAttributes.Any())
                {
                    // Apply custom attributes as endpoint metadata
                    var metadata = new EndpointMetadataCollection(handlerInfo.CustomAttributes.Cast<object>().ToArray());
                    
                    // Create a new endpoint with the additional metadata
                    var newEndpoint = new Endpoint(
                        endpoint.RequestDelegate,
                        new EndpointMetadataCollection(endpoint.Metadata.Concat(handlerInfo.CustomAttributes.Cast<object>())),
                        endpoint.DisplayName
                    );

                    // Replace the endpoint in the context
                    context.SetEndpoint(newEndpoint);
                }
            }

            await _next(context);
        }

        private string DetermineHandlerKey(string httpMethod, string[] segments, int prefixIndex)
        {
            // This logic mirrors CqApiController's routing logic
            var segmentCount = segments.Length - prefixIndex - 2; // Subtract prefix and resource name

            return httpMethod.ToUpperInvariant() switch
            {
                "GET" when segmentCount == 0 => "get",
                "GET" when segmentCount == 1 => "get/key",
                "GET" when segmentCount >= 2 => $"get/key/{segments[prefixIndex + 3]}",
                "POST" when segmentCount == 0 => "post",
                "POST" when segmentCount == 1 => "post/key",
                "POST" when segmentCount >= 2 => $"post/key/{segments[prefixIndex + 3]}",
                "DELETE" when segmentCount == 1 => "delete/key",
                _ => httpMethod.ToLowerInvariant()
            };
        }
    }
}
