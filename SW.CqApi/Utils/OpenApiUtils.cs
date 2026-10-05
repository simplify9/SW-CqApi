using Microsoft.OpenApi.Models;
using SW.CqApi.Options;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SW.CqApi.Utils
{
    internal static class OpenApiUtils
    {
        public static OpenApiOperation Clone(this OpenApiOperation baseApiOperation)
        {
            return new OpenApiOperation
            {
                Deprecated = baseApiOperation.Deprecated,
                Description = baseApiOperation.Description,
                Callbacks = baseApiOperation.Callbacks,
                Extensions = baseApiOperation.Extensions,
                Responses = baseApiOperation.Responses,
                RequestBody = baseApiOperation.RequestBody,
                OperationId = baseApiOperation.OperationId,
                ExternalDocs = baseApiOperation.ExternalDocs,
                Parameters = baseApiOperation.Parameters,
                Security = baseApiOperation.Security,
                Servers = baseApiOperation.Servers,
                Summary = baseApiOperation.Summary
            };
        }
        public static OpenApiResponses GetOpenApiResponses(MethodInfo methodInfo, IEnumerable<ReturnsAttribute> returnsAttributes, OpenApiComponents components, string InterfaceType, TypeMaps maps, Newtonsoft.Json.JsonSerializer serializer = null)
        {
            //var methodInfo = handlerInfo.Method;
            // var returnMediaType = new OpenApiMediaType
            // {
            //     Schema = new OpenApiSchema
            //     {
            //         Type = methodInfo.ReturnType.FullName.Contains("Task") ? methodInfo.ReturnType.GenericTypeArguments[0].Name : methodInfo.ReturnType.FullName ,
            //     }
            // };
            
            var responses = returnsAttributes
                .ToOpenApiResponses(components, maps, serializer)
                .GetDefaultResponses(methodInfo, InterfaceType);
            return responses;
        }

        /// <param name="withKey">
        /// The first parameter is the route key: it is described as the required <c>{key}</c> path
        /// parameter, and every other parameter (or request-object property) goes in the query.
        /// </param>
        public static IList<OpenApiParameter> GetOpenApiParameters(IEnumerable<ParameterInfo> parameters, OpenApiComponents components, TypeMaps maps, bool withKey = false, Newtonsoft.Json.JsonSerializer serializer = null)
        {
            var openApiParams = new List<OpenApiParameter>();
            var isKey = withKey;
            foreach(var parameter in parameters)
            {
                if (isKey)
                {
                    isKey = false;
                    openApiParams.Add(new OpenApiParameter
                    {
                        Name = "key",
                        Required = true,
                        In = ParameterLocation.Path,
                        Schema = TypeUtils.ExplodeParameter(parameter.ParameterType, components, maps, serializer)
                    });
                    continue;
                }

                if (parameter.GetCustomAttribute<IgnoreMemberAttribute>() != null) continue;
                var schemaParam = TypeUtils.ExplodeParameter(parameter.ParameterType, components, maps, serializer);
                if (schemaParam.Properties.Count > 0)
                {
                    foreach(var prop in schemaParam.Properties)
                    {
                        openApiParams.Add(new OpenApiParameter
                        {
                            Name = prop.Key,
                            Required = !prop.Value.Nullable,
                            AllowEmptyValue = !prop.Value.Nullable,
                            In = ParameterLocation.Query,
                            Schema = prop.Value
                        });
                    }
                }
                else
                {
                    openApiParams.Add(new OpenApiParameter
                    {
                        Name = parameter.Name,
                        AllowEmptyValue = parameter.IsOptional,
                        In = ParameterLocation.Query,
                        Schema = schemaParam
                    });
                }
            }
            return openApiParams;
        }


        public static OpenApiRequestBody GetOpenApiRequestBody(MethodInfo methodInfo, OpenApiComponents components, TypeMaps maps, bool withKey = false, Newtonsoft.Json.JsonSerializer serializer = null)
        {

            var conentMediaType  = new OpenApiMediaType
            {
                Schema = new OpenApiSchema
                {
                    Type = methodInfo.ReturnType.FullName.Contains("Task") ? 
                        methodInfo.ReturnType.GenericTypeArguments[0].Name : methodInfo.ReturnType.FullName 
                }
            };
            List<ParameterInfo> relevantParameters = withKey ? methodInfo.GetParameters().Skip(1).ToList() : methodInfo.GetParameters().ToList();
            var paramterDict = new Dictionary<string, OpenApiSchema>();


            foreach(var param in relevantParameters)
            {
                paramterDict[param.Name] = TypeUtils.ExplodeParameter(param.ParameterType, components, maps, serializer);
            }

            // A command without a request type has no body to describe.
            if (relevantParameters.Count == 0)
                return null;

            var bodyName = TypeUtils.SchemaName(relevantParameters[0].ParameterType);


            var requestBody = new OpenApiRequestBody
            {
                Description = "Command Body",
                Required = relevantParameters.Any(p => !p.IsOptional),
                Content =
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.Schema,
                                Id = bodyName
                            }
                        }
                    }
                },
            };

            return requestBody;
        }
    }
}
