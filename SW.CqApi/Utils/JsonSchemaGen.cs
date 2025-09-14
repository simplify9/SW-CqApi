using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Schema;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.CqApi.Utils
{
    internal static class JsonSchemaGen
    {
        public static JsonSchema GetJsonType(this Type t)
        {
            JsonSchemaGenerator gen = new JsonSchemaGenerator();
            gen.UndefinedSchemaIdHandling = UndefinedSchemaIdHandling.UseTypeName;
            JsonSchema json = gen.Generate(t.SimplifyType());
            return json;
        }

        public static Type SimplifyType(this Type t)
        {
            var intfs = t.GetInterfaces();
            if (Nullable.GetUnderlyingType(t) != null)
            {
                return Nullable.GetUnderlyingType(t);
            }
            else if (t.ContainsGenericParameters)
            {
                return t.GenericTypeArguments[0];
            }
            else
            {
                return t;
            }

        }

        public static OpenApiSchema GetOpenApiSchema(this JsonSchema json )
        {
            OpenApiSchema op = new OpenApiSchema();
            if(json.Items != null)
                op.Items = json.Items[0].GetOpenApiSchema();

            if(json.Properties != null)
            {
                var props = new Dictionary<string, OpenApiSchema>();
                foreach (var prop in json.Properties)
                {
                    if (prop.Value.Id == json.Id || prop.Value.Items != null && prop.Value.Items.Count > 0 && prop.Value.Items[0].Id == json.Id) continue;
                    props[prop.Key] = prop.Value.GetOpenApiSchema();
                }
                op.Properties = props;
            }

            string jsonType = json.Type.ToJsonType();

            op.Type = jsonType;
            return op;
        }

        public static string ToJsonType(this JsonSchemaType? jsonSchemaType)
        {
            if (jsonSchemaType == null) return "";
            string jstString = jsonSchemaType.ToString();
            string splitJson = jstString.Split(',').Length > 1 ? jstString.Split(',')[0] : jstString;
            switch (splitJson)
            {
                case "Integer":
                case "Decimal":
                    return "number";
                case "String":
                    return "string";
                case "Object":
                    return "object";
                case "Array":
                    return "array";
                case "Boolean":
                    return "boolean";
                case "Null":
                    return "null";
                default:
                    return splitJson.ToLowerInvariant();
            }
        }
    }
}
