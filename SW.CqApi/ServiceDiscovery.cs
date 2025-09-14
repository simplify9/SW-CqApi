using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using SW.CqApi.Utils;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SW.CqApi
{
    internal class ServiceDiscovery
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
            using (var scope = serviceProvider.CreateScope())
            {
                var registeredHandlers = scope.ServiceProvider.GetServices<IHandler>();

                foreach (var svc in registeredHandlers)
                {
                    var serviceType = svc.GetType();
                    var interfaceType = serviceType.GetTypeInfo().ImplementedInterfaces.Where(i => typeof(IHandler).IsAssignableFrom(i) && i != typeof(IHandler)).Single();
                    var interfaceTypeNormalized = interfaceType.IsGenericType ? interfaceType.GetGenericTypeDefinition() : interfaceType;

                    var typeNameArray = serviceType.FullName.Split('.');
                    var resourceName = typeNameArray[typeNameArray.Length - 2].ToLower();

                    var handlerNameAttribute = serviceType.GetCustomAttribute<HandlerNameAttribute>();
                    var handlerName = handlerNameAttribute == null ? "" : $"/{handlerNameAttribute.Name.ToLower()}";

                    if (!resourceHandlers.ContainsKey(resourceName))
                        resourceHandlers.Add(resourceName, new Dictionary<string, HandlerInfo>(StringComparer.OrdinalIgnoreCase));

                    var handlerKey = $"{HandlerTypeMetadata.Handlers[interfaceTypeNormalized].Key}{handlerName}";

                    resourceHandlers[resourceName][handlerKey] = new HandlerInfo
                    {
                        HandlerType = serviceType,
                        Method = interfaceType.GetMethod("Handle"),
                        ArgumentTypes = interfaceType.GetMethod("Handle").GetParameters().Select(p => p.ParameterType).ToList(),
                        Key = handlerKey,
                        Resource = resourceName,
                        NormalizedInterfaceType = interfaceTypeNormalized
                    };

                }
            }
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

        public IEnumerable<string> GetRoles()
        {
            return resourceHandlers.GetRoles();
        }

        public string GetOpenApiDocument()
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
                    Title = options.ApplicationName,
                    Description = desc
                },
                Servers = new List<OpenApiServer>
                {
                    //new OpenApiServer { Url = "http://petstore.swagger.io/api" }
                },
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

                var pathItem = new OpenApiPathItem();
                document.Paths.Add(res.Key, pathItem);
                var tag = new OpenApiTag {
                    Name = res.Key,
                    Description = description
                };
                document.Tags.Add(tag);

                pathItem.Operations = new Dictionary<OperationType, OpenApiOperation>();
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
                        apiOperation.Parameters = OpenApiUtils.GetOpenApiParameters(handler.Value.Method.GetParameters(), components, options.Maps, true, options.Serializer);
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
