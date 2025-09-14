using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using SW.CqApi.Extensions;
using SW.CqApi.Options;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using SW.PrimitiveTypes;
using Newtonsoft.Json.Serialization;

namespace SW.CqApi.Utils
{
    internal static class TypeUtils
    {
        public static OpenApiSchema ExplodeParameter(Type parameter, OpenApiComponents components, TypeMaps maps)
        {
            return ExplodeParameter(parameter, components, maps, null);
        }

        public static OpenApiSchema ExplodeParameter(Type parameter, OpenApiComponents components, TypeMaps maps, Newtonsoft.Json.JsonSerializer serializer)
        {
            OpenApiSchema schema = new OpenApiSchema();
            var jsonifed = parameter.GetJsonType();
            string name = parameter.Name;

            if (parameter.GenericTypeArguments.Length > 0)
            {
                foreach(var genArg in parameter.GenericTypeArguments)
                {
                    ExplodeParameter(genArg, components, maps, serializer);
                }
                schema.Type = "object";
                name = parameter.GetGenericName();
            }

            if (maps.ContainsMap(parameter)){
                var map = maps.GetMap(parameter);
                schema = ExplodeParameter(map.Type, components, maps, serializer);
                schema.Example = map.OpenApiExample;
                components.Schemas[name] = schema;
            }
            else if (components.Schemas.ContainsKey(name))
            {
                schema = components.Schemas[name];
            }
            else if (!String.IsNullOrEmpty(parameter.GetDefaultSchema().Title))
            {
                schema = parameter.GetDefaultSchema();
            }
            else if (Nullable.GetUnderlyingType(parameter) != null)
            {
                schema =  ExplodeParameter(Nullable.GetUnderlyingType(parameter), components, maps, serializer);
                schema.Nullable = true;
            }
            else if (parameter.IsEnum)
            {
                List<IOpenApiAny> enumVals = new List<IOpenApiAny>();
                foreach(var enumSingle in parameter.GetEnumNames())
                {
                    var enumStr = enumSingle.ToString();
                    enumVals.Add(new OpenApiString(enumStr));
                }
                schema.Type = "string";
                schema.Enum = enumVals;
            }
            else if(parameter.IsPrimitive || IsNumericType(parameter) || parameter == typeof(string))
            {
                schema.Type = jsonifed.Type.ToJsonType();
                schema.Example = GetExample(parameter, maps, components);
            }
            else if (jsonifed.Items != null)
            {
                schema.Type = jsonifed.Type.ToJsonType();
                schema.Items = jsonifed.Items[0].GetOpenApiSchema();
                schema.Example = GetExample(parameter, maps, components);
            }
            else if(parameter.GetProperties().Length != 0 && !IsNumericType(parameter))
            {
                Dictionary<string, OpenApiSchema> props = new Dictionary<string, OpenApiSchema>();
                var namingStrategy = serializer?.ContractResolver is DefaultContractResolver resolver ? resolver.NamingStrategy : null;
                
                foreach(var prop in parameter.GetProperties())
                {
                    
                    if (prop.GetCustomAttribute<IgnoreMemberAttribute>() != null || prop.PropertyType == parameter) continue;
                    
                    // Use naming strategy for OpenAPI document generation only
                    var openApiPropertyName = namingStrategy != null ? namingStrategy.GetPropertyName(prop.Name, false) : prop.Name;
                    props[openApiPropertyName] = ExplodeParameter(prop.PropertyType, components, maps, serializer);
                }

                schema.Properties = props;
            }
            else
            {
                schema.Type = "Object";
            }
            components.Schemas[name] = schema;
            return schema;

        }

        public static bool IsNumericType(Type t )
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }
        static public IOpenApiAny GetExample(Type parameter, TypeMaps maps, OpenApiComponents components)
        {

            if (components.Schemas.ContainsKey(parameter.Name)) return components.Schemas[parameter.Name].Example;

            if (maps.ContainsMap(parameter))
            {
                return maps.GetMap(parameter).OpenApiExample;
            }
            else if (parameter == typeof(string))
            {
                int randomNum = new Random().Next() % 3;
                var words = new string[] { "foo", "bar", "baz" };
                return new OpenApiString(words[randomNum]);
            }
            else if (IsNumericType(parameter))
            {
                int randomNum = new Random().Next() % 400;
                return new OpenApiInteger(randomNum);
            }
            else if (parameter == typeof(bool))
            {
                int randomNum = new Random().Next() % 1;
                return new OpenApiBoolean(randomNum == 0);
            }
            else if (parameter.GetInterfaces().Contains(typeof(IEnumerable)))
            {
                var exampleArr = new OpenApiArray();
                int randomNum = new Random().Next() % 3;
                for(int _ = 0; _ < randomNum + 1; _++)
                {
                    var innerType = parameter.GetElementType() ?? parameter.GenericTypeArguments[0];
                    exampleArr.Add(GetExample(innerType, maps, components));
                }

                return exampleArr;
            }
            else
            {
                if (parameter.GetProperties().Length == 0) return new OpenApiNull();
                var example = new OpenApiObject();
                foreach(var prop in parameter.GetProperties())
                    example.Add(prop.Name, GetExample(prop.PropertyType, maps, components));
                return example;
            }

        }
    }
}
