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

        public static IList<OpenApiParameter> GetOpenApiParameters(IEnumerable<ParameterInfo> parameters, OpenApiComponents components, TypeMaps maps, bool withKey = false, Newtonsoft.Json.JsonSerializer serializer = null)
        {
            var openApiParams = new List<OpenApiParameter>();
            foreach(var parameter in parameters)
            {
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
                            In = withKey ? ParameterLocation.Path : ParameterLocation.Query,
                            Schema = prop.Value
                        });
                    }
                }
                else
                {
                    openApiParams.Add(new OpenApiParameter
                    {
                        Name = parameter.Name,
                        //Required = !parameter.IsOptional,
                        AllowEmptyValue = parameter.IsOptional,
                        In = withKey ? ParameterLocation.Path : ParameterLocation.Query,
                        Schema = TypeUtils.ExplodeParameter(parameter.ParameterType, components, maps, serializer)
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

            var bodyName = "";
            if(relevantParameters.Count != 0)
                bodyName = relevantParameters[0].ParameterType.Name;


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
