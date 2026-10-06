using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SW.CqApi;
using SW.CqApi.Endpoints;

// In ASP.NET's own namespace, like MapControllers(), so Program.cs needs no extra using.
namespace Microsoft.AspNetCore.Builder
{
    public static class CqApiEndpointRouteBuilderExtensions
    {
        // Real handlers win over the not-found fallbacks whenever both match.
        private const int HandlerOrder = 0;
        private const int FallbackOrder = 1;

        /// <summary>
        /// Maps one endpoint per CqApi handler, e.g. <c>POST api/shipments/{key}/saveitem</c>,
        /// plus <c>swagger.json</c> and <c>_roles</c>. Call it next to <c>MapControllers()</c>.
        /// </summary>
        /// <remarks>
        /// URLs are the same as CqApi 8's single controller. A named handler beats <c>{key}</c>
        /// (<c>GET shipments/search</c> is the search handler, not a get with key "search")
        /// through ordinary route precedence, as the old controller did by hand.
        /// </remarks>
        public static IEndpointConventionBuilder MapCqApi(this IEndpointRouteBuilder endpoints)
        {
            var services = endpoints.ServiceProvider;
            var options = services.GetRequiredService<CqApiOptions>();
            var discovery = services.GetRequiredService<ServiceDiscovery>();
            var prefix = options.UrlPrefix ?? "cqapi";

            var group = endpoints.MapGroup(prefix);
            var all = new List<IEndpointConventionBuilder>();

            // The old controller carried [Authorize(AuthenticationSchemes = "Bearer")] with
            // [AllowAnonymous]: the Bearer scheme authenticates the request (filling
            // HttpContext.User for RequestContext) and nothing is refused at this stage.
            // CqApi's own protection runs when the handler is resolved.
            void Common(IEndpointConventionBuilder builder) => builder.WithMetadata(
                new AuthorizeAttribute { AuthenticationSchemes = "Bearer" },
                new AllowAnonymousAttribute());

            foreach (var handler in discovery.Handlers)
            {
                if (!TryGetRoute(handler.Key, out var method, out var template, out var hasKey, out var acceptsLookup))
                    continue;

                var info = handler;
                RequestDelegate run = context => CqApiDispatcher.Execute(
                    context, info, hasKey ? (string)context.GetRouteValue("key") : null, acceptsLookup);

                var routeTemplate = $"{info.Resource}{template}";
                var builder = group.MapMethods(routeTemplate, new[] { method }, run)
                    .WithDisplayName($"{method} {prefix}/{routeTemplate}")
                    .WithMetadata(new CqApiHandlerMetadata(info))
                    .WithMetadata(info.CustomAttributes.Cast<object>().ToArray());
                ((IEndpointConventionBuilder)builder).Add(b => ((RouteEndpointBuilder)b).Order = HandlerOrder);
                Common(builder);
                all.Add(builder);
            }

            var swagger = group.MapGet("swagger.json", (HttpContext context) =>
            {
                if (options.DisableOpenApiDocumentation)
                    return CqApiDispatcher.ExecuteResult(context.RequestServices, ActionContextFor(context),
                        new NotFoundObjectResult("OpenAPI documentation is disabled."));

                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync(discovery.GetOpenApiDocument(context.Request.PathBase.Value));
            }).WithDisplayName($"GET {prefix}/swagger.json");
            Common(swagger);

            var roles = group.MapGet("_roles", (HttpContext context) =>
                CqApiDispatcher.ExecuteResult(context.RequestServices, ActionContextFor(context),
                    new OkObjectResult(discovery.GetRoles().OrderBy(e => e).ToDictionary(k => k, v => v))))
                .WithDisplayName($"GET {prefix}/_roles");
            Common(roles);

            var role = group.MapGet("_roles/{role}", (HttpContext context) =>
                CqApiDispatcher.ExecuteResult(context.RequestServices, ActionContextFor(context),
                    new OkObjectResult((string)context.GetRouteValue("role"))))
                .WithDisplayName($"GET {prefix}/_roles/{{role}}");
            Common(role);

            // What the old controller answered for a resource or handler that doesn't exist.
            void Fallback(string httpMethod, string template, Func<HttpContext, string> handlerKey)
            {
                var builder = group.MapMethods(template, new[] { httpMethod },
                        context => CqApiDispatcher.NotFound(context, handlerKey(context)))
                    .WithDisplayName($"{httpMethod} {prefix}/{template} (no handler)");
                ((IEndpointConventionBuilder)builder).Add(b => ((RouteEndpointBuilder)b).Order = FallbackOrder);
                Common(builder);
            }

            Fallback("GET", "{resourceName}", _ => "get");
            Fallback("GET", "{resourceName}/{token}", _ => null);
            Fallback("GET", "{resourceName}/{key}/{token}", c => $"get/key/{c.GetRouteValue("token")}");
            Fallback("POST", "{resourceName}", _ => "post");
            Fallback("POST", "{resourceName}/{token}", _ => null);
            Fallback("POST", "{resourceName}/{key}/{command}", c => $"post/key/{c.GetRouteValue("command")}");
            Fallback("DELETE", "{resourceName}/{key}", _ => "delete/key");

            return new CompositeConventionBuilder(all);
        }

        /// <summary>
        /// Route for a handler key. Keys the old controller had no route for (e.g. a named
        /// delete handler) aren't mapped, as they were never reachable.
        /// </summary>
        private static bool TryGetRoute(string handlerKey, out string method, out string template, out bool hasKey, out bool acceptsLookup)
        {
            var parts = handlerKey.Split('/');
            method = parts[0].ToUpperInvariant();
            hasKey = parts.Length > 1 && parts[1] == "key";
            var name = hasKey ? (parts.Length > 2 ? parts[2] : null) : (parts.Length > 1 ? parts[1] : null);
            acceptsLookup = method == "GET";
            template = (hasKey ? "/{key}" : "") + (name != null ? $"/{name}" : "");

            return method switch
            {
                "GET" or "POST" => parts.Length <= 3,
                "DELETE" => handlerKey == "delete/key",
                _ => false,
            };
        }

        private static Microsoft.AspNetCore.Mvc.ActionContext ActionContextFor(HttpContext context) =>
            new(context, context.GetRouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

        private sealed class CompositeConventionBuilder : IEndpointConventionBuilder
        {
            private readonly IReadOnlyList<IEndpointConventionBuilder> builders;

            public CompositeConventionBuilder(IReadOnlyList<IEndpointConventionBuilder> builders) => this.builders = builders;

            public void Add(Action<EndpointBuilder> convention)
            {
                foreach (var builder in builders) builder.Add(convention);
            }
        }
    }
}
