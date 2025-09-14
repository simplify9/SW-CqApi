using Microsoft.OpenApi.Models;
using SW.CqApi.Options;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.CqApi.Utils
{
    internal static class CustomAttributeUtils
    {
        

        public static OpenApiResponses ToOpenApiResponses(this IEnumerable<ReturnsAttribute> attributes, OpenApiComponents components, TypeMaps maps, Newtonsoft.Json.JsonSerializer serializer = null)
        {
            var responses = new OpenApiResponses();
            foreach(var attribute in attributes)
            {
                OpenApiSchema schema = TypeUtils.ExplodeParameter(attribute.Type, components, maps, serializer);
                var mediaType = new OpenApiMediaType
                {
                    Schema = schema
                };

                responses.Add(attribute.StatusCode.ToString(), new OpenApiResponse
                {
                    Description = attribute.Description,
                    Content = { ["application/json"] = mediaType }
                });
            }

            return responses;
        }

    }
}
