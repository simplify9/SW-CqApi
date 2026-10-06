using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using SW.CqApi.Utils;
using SW.PrimitiveTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SW.CqApi
{
    public class ServiceDiscovery
    {

        /* Resource Abc
         *  get / => search     "get"
         *  get /key => get     "get/key"
         *  
         *  post /              "post"
         *  post /command1      "post/{token}"
         *  post /command2      
         *  post /key/command   "post/key/{token}"
         *  
         *  put /key            "put/key"
         *  
         *  delete /key         "delete/key"
         * 
         */

        //private readonly ILogger<ServiceDiscovery> logger;
        private readonly IDictionary<string, IDictionary<string, HandlerInfo>> resourceHandlers = new Dictionary<string, IDictionary<string, HandlerInfo>>(StringComparer.OrdinalIgnoreCase);
        private readonly CqApiOptions options;

        public ServiceDiscovery(IServiceProvider serviceProvider, ILogger<ServiceDiscovery> logger, CqApiOptions options)
        {
            this.options = options;

            foreach (var serviceType in DiscoverHandlerTypes(serviceProvider))
            {
                var interfaceType = serviceType.GetTypeInfo().ImplementedInterfaces.Where(i => typeof(IHandler).IsAssignableFrom(i) && i != typeof(IHandler)).Single();
                var interfaceTypeNormalized = interfaceType.IsGenericType ? interfaceType.GetGenericTypeDefinition() : interfaceType;

                var typeNameArray = serviceType.FullName.Split('.');
                var resourceName = typeNameArray[typeNameArray.Length - 2].ToLower();

                var handlerNameAttribute = serviceType.GetCustomAttribute<HandlerNameAttribute>();
                var handlerName = handlerNameAttribute == null ? "" : $"/{handlerNameAttribute.Name.ToLower()}";

                if (!resourceHandlers.ContainsKey(resourceName))
                    resourceHandlers.Add(resourceName, new Dictionary<string, HandlerInfo>(StringComparer.OrdinalIgnoreCase));

                var handlerKey = $"{HandlerTypeMetadata.Handlers[interfaceTypeNormalized].Key}{handlerName}";

                // Capture custom attributes specified in options
                var customAttributes = new List<Attribute>();
                foreach (var attributeType in options.PreserveCustomAttributes)
                {
                    var attr = serviceType.GetCustomAttribute(attributeType);
                    if (attr != null)
                    {
                        customAttributes.Add(attr);
                    }
                }

                var method = interfaceType.GetMethod("Handle");
                var protect = serviceType.GetCustomAttribute<ProtectAttribute>();
                var unprotect = serviceType.GetCustomAttribute<UnprotectAttribute>();

                string[] requiredRoles = null;
                if (protect?.RequireRole ?? false)
                {
                    var prefix = string.IsNullOrWhiteSpace(options.RolePrefix) ? resourceName : $"{options.RolePrefix}.{resourceName}";
                    requiredRoles = new[] { $"{prefix}.{serviceType.Name}", $"{prefix}.*" };
                }

                resourceHandlers[resourceName][handlerKey] = new HandlerInfo
                {
                    HandlerType = serviceType,
                    Method = method,
                    ArgumentTypes = method.GetParameters().Select(p => p.ParameterType).ToList(),
                    Key = handlerKey,
                    Resource = resourceName,
                    NormalizedInterfaceType = interfaceTypeNormalized,
                    CustomAttributes = customAttributes,
                    Invoker = HandlerInvoker.Create(method),
                    RequiresAuthentication = (options.ProtectAll && unprotect == null) || protect != null,
                    RequiredRoles = requiredRoles,
                };
            }
        }

        /// <summary>
        /// Handler types in registration order, read from the service registrations so that
        /// discovering them doesn't construct every handler (and its dependencies) at startup.
        /// A handler registered through a factory or as an instance is constructed once to learn
        /// its type, as before.
        /// </summary>
        private static IEnumerable<Type> DiscoverHandlerTypes(IServiceProvider serviceProvider)
        {
            var registrations = serviceProvider.GetService<CqApiServiceRegistrations>();
            if (registrations == null)
            {
                using var scope = serviceProvider.CreateScope();
                return scope.ServiceProvider.GetServices<IHandler>().Select(h => h.GetType()).ToList();
            }

            var types = new List<Type>();
            IServiceScope fallbackScope = null;
            try
            {
                var index = 0;
                foreach (var descriptor in registrations.Services.Where(d => d.ServiceType == typeof(IHandler)).ToList())
                {
                    var type = descriptor.IsKeyedService ? null : descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
                    if (type == null && !descriptor.IsKeyedService)
                    {
                        fallbackScope ??= serviceProvider.CreateScope();
                        type = fallbackScope.ServiceProvider.GetServices<IHandler>().ElementAt(index).GetType();
                    }
                    if (type != null) types.Add(type);
                    if (!descriptor.IsKeyedService) index++;
                }
            }
            finally
            {
                fallbackScope?.Dispose();
            }
            return types;
        }

        public bool TryResolveHandler(string resourceName, string handlerKey, out HandlerInfo handlerInfo)
        {
            if (resourceHandlers.TryGetValue(resourceName, out var handlers))

                if (handlers.TryGetValue(handlerKey, out handlerInfo))
                    return true;

            handlerInfo = null;
            return false;

        }

        public HandlerInfo ResolveHandler(string resourceName, string handlerKey)
        {
            if (TryResolveHandler(resourceName, handlerKey, out var handlerInfo))
                return handlerInfo;

            throw new SWNotFoundException($"{resourceName}/{handlerKey}");
        }

        /// <summary>Every handler, one per resource and handler key.</summary>
        public IEnumerable<HandlerInfo> Handlers => resourceHandlers.Values.SelectMany(h => h.Values);

        public IEnumerable<string> GetRoles()
        {
            return resourceHandlers.GetRoles();
        }

        // The handler set is fixed after startup, so each document only needs building once.
        // Keyed by path base, of which a service sees one or two (none locally, "/accounting"
        // behind the ingress). A failed build isn't cached; the next request tries again.
        private const int MaxCachedOpenApiDocuments = 8;
        private readonly ConcurrentDictionary<string, string> openApiDocuments = new ConcurrentDictionary<string, string>();

        public string GetOpenApiDocument() => GetOpenApiDocument(null);

        /// <param name="pathBase">
        /// The request's path base (e.g. "/accounting"). Declared as the document's server, so
        /// Swagger UI's "Try it out" calls /accounting/api/... rather than /api/... at the host root.
        /// </param>
        public string GetOpenApiDocument(string pathBase)
        {
            pathBase ??= string.Empty;
            if (openApiDocuments.TryGetValue(pathBase, out var cached)) return cached;

            var document = BuildOpenApiDocument(pathBase);
            if (openApiDocuments.Count < MaxCachedOpenApiDocuments)
                openApiDocuments.TryAdd(pathBase, document);
            return document;
        }

        private string BuildOpenApiDocument(string pathBase)
        {

            string apiPrefix = $"/{options.UrlPrefix}";

            var desc = $"This API includes ways to manipulate ";
            var keysArr = resourceHandlers.Keys.ToArray<string>();
            for (byte i = 0; i < keysArr.Length; i++)
                desc += i + 1 == keysArr.Length ? $"{keysArr[i]}.\n" :
                        i + 1 == keysArr.Length - 1 ? $"{keysArr[i]} and " :
                        $"{keysArr[i]},";

            var components = new OpenApiComponents();
            var document = new OpenApiDocument
            {
                
                Info = new OpenApiInfo
                {
                    Version = "V3",
                    // Required by the spec; Traxis services set Description but not ApplicationName.
                    Title = options.ApplicationName ?? options.Description ?? "CqApi",
                    Description = desc
                },
                // Without a server, clients resolve paths against the host root and lose the path base.
                Servers = string.IsNullOrEmpty(pathBase)
                    ? new List<OpenApiServer>()
                    : new List<OpenApiServer> { new OpenApiServer { Url = pathBase } },
                Paths = new OpenApiPaths(),
                Components = components,
            };
            var roles = resourceHandlers.GetRoles();
            document.Components = components.AddSecurity(roles, options.AuthOptions);
            foreach (var res in resourceHandlers)
            {

                string description = options.ResourceDescriptions.ContainsDescription(res.Key) ?
                                     options.ResourceDescriptions.Get(res.Key) : 
                                     $"Commands and Queries related to {res.Key}";

                var tag = new OpenApiTag {
                    Name = res.Key,
                    Description = description
                };
                document.Tags.Add(tag);

                foreach (var handler in res.Value)
                {



                    var interfaceType = handler.Value.NormalizedInterfaceType;
                    var apiOperation = HandlerTypeMetadata.Handlers[interfaceType].OpenApiOperation.Clone();
                    apiOperation.Tags.Add(tag);
                    var returns = handler.Value.HandlerType.GetCustomAttributes<ReturnsAttribute>().ToList();
                    returns.Add(new ReturnsAttribute()
                    {
                        Type = handler.Value.Method.ReturnType.GetGenericArguments()[0],
                        StatusCode = 200
                    });
                        
                    var protect = handler.Value.HandlerType.GetCustomAttribute<ProtectAttribute>();
                    
                    apiOperation.Responses = OpenApiUtils.GetOpenApiResponses(handler.Value.Method, returns, components, interfaceType.Name, options.Maps, options.Serializer);

                    if (protect != null){
                        apiOperation.AddSecurity($"{res.Key}.{handler.Value.HandlerType.Name.ToLower()}", components);
                    }

                    if (handler.Key == "get")
                    {
                        string path = $"{apiPrefix}/{res.Key}";
                        initializePath(document, path);
                        apiOperation.Parameters = OpenApiUtils.GetOpenApiParameters(handler.Value.Method.GetParameters(), components, options.Maps, false, options.Serializer);
                        //apiOperation.RequestBody = OpenApiUtils.GetOpenApiResponses()
                        document.Paths[path].Operations.Add(OperationType.Get, apiOperation);
                    }

                    else if (handler.Key == "get/key")
                    {
                        string path = $"{apiPrefix}/{res.Key}/{{key}}";
                        initializePath(document, path);
                        apiOperation.Parameters = OpenApiUtils.GetOpenApiParameters(handler.Value.Method.GetParameters(), components, options.Maps, true, options.Serializer);
                        document.Paths[path].Operations.Add(OperationType.Get, apiOperation);
                    }

                    else if (handler.Key.StartsWith("get/key"))
                    {
                        string path = $"{apiPrefix}/{res.Key}/{{key}}{handler.Key.Substring(handler.Key.LastIndexOf('/'))}";
                        initializePath(document, path);
                        apiOperation.Parameters = OpenApiUtils.GetOpenApiParameters(handler.Value.Method.GetParameters().Take(1), components, options.Maps, true, options.Serializer);
                        document.Paths[path].Operations.Add(OperationType.Get, apiOperation);
                    }

                    else if (handler.Key.StartsWith("get/"))
                    {
                        string path = $"{apiPrefix}/{res.Key}{handler.Key.Substring(handler.Key.LastIndexOf('/'))}";
                        initializePath(document, path);
                        apiOperation.Parameters = OpenApiUtils.GetOpenApiParameters(handler.Value.Method.GetParameters(), components, options.Maps, false, options.Serializer);
                        document.Paths[path].Operations.Add(OperationType.Get, apiOperation);
                    }

                    else if (handler.Key == "post")
                    {
                        string path = $"{apiPrefix}/{res.Key}";
                        initializePath(document, path);
                        apiOperation.RequestBody = OpenApiUtils.GetOpenApiRequestBody(handler.Value.Method, components, options.Maps, false, options.Serializer);
                        document.Paths[path].Operations.Add(OperationType.Post, apiOperation);
                    }

                    else if (handler.Key == "post/key")
                    {
                        string path = $"{apiPrefix}/{res.Key}/{{key}}";

                        initializePath(document, path );
                        apiOperation.Parameters = OpenApiUtils.GetOpenApiParameters(handler.Value.Method.GetParameters().Take(1), components, options.Maps, true, options.Serializer);
                        apiOperation.RequestBody = OpenApiUtils.GetOpenApiRequestBody(handler.Value.Method, components, options.Maps, true, options.Serializer);
                        document.Paths[path].Operations.Add(OperationType.Post, apiOperation);
                    }

                    else if (handler.Key.StartsWith("post/key"))
                    {
                        string path = $"{apiPrefix}/{res.Key}/{{key}}{handler.Key.Substring(handler.Key.LastIndexOf('/'))}";
                        initializePath(document, path);
                        apiOperation.Parameters = OpenApiUtils.GetOpenApiParameters(handler.Value.Method.GetParameters().Take(1), components, options.Maps, true, options.Serializer);
                        apiOperation.RequestBody = OpenApiUtils.GetOpenApiRequestBody(handler.Value.Method, components, options.Maps, true, options.Serializer);
                        document.Paths[path].Operations.Add(OperationType.Post, apiOperation);
                    }

                    else if (handler.Key.StartsWith("post/"))
                    {

                        string path = $"{apiPrefix}/{res.Key}{handler.Key.Substring(handler.Key.LastIndexOf('/'))}";
                        initializePath(document, path);
                        apiOperation.RequestBody = OpenApiUtils.GetOpenApiRequestBody(handler.Value.Method, components, options.Maps, false, options.Serializer);
                        document.Paths[path].Operations.Add(OperationType.Post, apiOperation);
                    }
                }
            }

            return document.Serialize(OpenApiSpecVersion.OpenApi3_0, OpenApiFormat.Json);
        }


        void initializePath(OpenApiDocument document, string path)
        {
            if (!document.Paths.ContainsKey(path))

                document.Paths.Add(path, new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>()
                });
        }

    }
}
