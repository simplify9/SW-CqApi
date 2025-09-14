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
                schema.Example = GetExample(parameter, maps, components, serializer);
            }
            else if (jsonifed.Items != null)
            {
                schema.Type = jsonifed.Type.ToJsonType();
                schema.Items = jsonifed.Items[0].GetOpenApiSchema();
                schema.Example = GetExample(parameter, maps, components, serializer);
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
        static public IOpenApiAny GetExample(Type parameter, TypeMaps maps, OpenApiComponents components, Newtonsoft.Json.JsonSerializer serializer = null)
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
            else if (parameter == typeof(int) || parameter == typeof(int?))
            {
                return new OpenApiInteger(123);
            }
            else if (parameter == typeof(long) || parameter == typeof(long?))
            {
                return new OpenApiLong(123456789);
            }
            else if (parameter == typeof(double) || parameter == typeof(double?) || 
                     parameter == typeof(decimal) || parameter == typeof(decimal?) ||
                     parameter == typeof(float) || parameter == typeof(float?))
            {
                return new OpenApiDouble(123.45);
            }
            else if (IsNumericType(parameter))
            {
                int randomNum = new Random().Next() % 400;
                return new OpenApiInteger(randomNum);
            }
            else if (parameter == typeof(bool) || parameter == typeof(bool?))
            {
                return new OpenApiBoolean(true);
            }
            else if (parameter == typeof(DateTime) || parameter == typeof(DateTime?))
            {
                return new OpenApiString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            }
            else if (parameter == typeof(Guid) || parameter == typeof(Guid?))
            {
                return new OpenApiString(Guid.NewGuid().ToString());
            }
            else if (parameter != typeof(string) && (parameter.GetInterfaces().Contains(typeof(IEnumerable)) || 
                     parameter.IsGenericType && parameter.GetGenericTypeDefinition() == typeof(List<>) ||
                     parameter.IsGenericType && parameter.GetGenericTypeDefinition() == typeof(IList<>) ||
                     parameter.IsGenericType && parameter.GetGenericTypeDefinition() == typeof(ICollection<>)))
            {
                var exampleArr = new OpenApiArray();
                var innerType = parameter.GetElementType() ?? 
                               (parameter.GenericTypeArguments.Length > 0 ? parameter.GenericTypeArguments[0] : typeof(object));
                
                // Generate 1-2 example items for the array
                int itemCount = new Random().Next(1, 3);
                for(int _ = 0; _ < itemCount; _++)
                {
                    var innerExample = GetExample(innerType, maps, components, serializer);
                    if (innerExample != null)
                        exampleArr.Add(innerExample);
                }

                return exampleArr;
            }
            else
            {
                if (parameter.GetProperties().Length == 0) return new OpenApiNull();
                var example = new OpenApiObject();
                var namingStrategy = serializer?.ContractResolver is DefaultContractResolver resolver ? resolver.NamingStrategy : null;
                
                foreach(var prop in parameter.GetProperties())
                {
                    if (prop.GetCustomAttribute<IgnoreMemberAttribute>() != null) continue;
                    
                    var propertyName = namingStrategy != null ? namingStrategy.GetPropertyName(prop.Name, false) : prop.Name;
                    var propertyExample = GetExample(prop.PropertyType, maps, components, serializer);
                    if (propertyExample != null)
                        example.Add(propertyName, propertyExample);
                }
                return example;
            }

        }
    }
}
