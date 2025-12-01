using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SW.CqApi.Filters
{
    /// <summary>
    /// Action filter that copies preserved custom attributes from resolved handlers
    /// onto the current endpoint metadata so other middleware (rate-limit, auth, etc.)
    /// can see them.
    /// </summary>
    internal class CqApiAttributeFilter : IAsyncActionFilter
    {
        private readonly ServiceDiscovery _serviceDiscovery;
        private readonly CqApiOptions _options;

        public CqApiAttributeFilter(ServiceDiscovery serviceDiscovery, CqApiOptions options)
        {
            _serviceDiscovery = serviceDiscovery;
            _options = options;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var httpContext = context.HttpContext;
            var path = httpContext.Request.Path.Value;
            if (string.IsNullOrEmpty(path) || !path.Contains($"/{_options.UrlPrefix}/"))
            {
                await next();
                return;
            }

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var prefixIndex = Array.FindIndex(segments, s => s.Equals(_options.UrlPrefix, StringComparison.OrdinalIgnoreCase));
            if (prefixIndex < 0 || prefixIndex >= segments.Length - 1)
            {
                await next();
                return;
            }

            var resourceName = segments[prefixIndex + 1];
            string handlerKey = DetermineHandlerKey(httpContext.Request.Method, segments, prefixIndex);

            if (_serviceDiscovery.TryResolveHandler(resourceName, handlerKey, out var handlerInfo))
            {
                var endpoint = httpContext.GetEndpoint();
                if (endpoint != null && handlerInfo.CustomAttributes.Any())
                {
                    var newEndpoint = new Endpoint(
                        endpoint.RequestDelegate,
                        new EndpointMetadataCollection(endpoint.Metadata.Concat(handlerInfo.CustomAttributes.Cast<object>())),
                        endpoint.DisplayName
                    );

                    httpContext.SetEndpoint(newEndpoint);
                }
            }

            await next();
        }

        private string DetermineHandlerKey(string httpMethod, string[] segments, int prefixIndex)
        {
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
