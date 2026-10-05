using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using SW.CqApi.Extensions;
using SW.CqApi.Options;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using SW.PrimitiveTypes;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace SW.CqApi.Utils
{
    internal static class TypeUtils
    {
        // Object schemas whose properties are still being walked, per document. A type met again
        // while it is in here is a cycle, and is emitted as a $ref: the schema objects themselves
        // would otherwise form a loop the OpenAPI writer follows until the process overflows its stack.
        private static readonly ConditionalWeakTable<OpenApiComponents, HashSet<string>> inProgress = new();

        public static OpenApiSchema ExplodeParameter(Type parameter, OpenApiComponents components, TypeMaps maps)
        {
            return ExplodeParameter(parameter, components, maps, null);
        }

        public static OpenApiSchema ExplodeParameter(Type parameter, OpenApiComponents components, TypeMaps maps, Newtonsoft.Json.JsonSerializer serializer)
        {
            OpenApiSchema schema = new OpenApiSchema();
            var jsonifed = parameter.GetJsonType();
            string name = SchemaName(parameter);

            // JSON.NET's dynamic types are free-form JSON. JToken enumerates JTokens, so treating it
            // as a collection below would recurse forever.
            if (typeof(JToken).IsAssignableFrom(parameter))
            {
                if (typeof(JObject).IsAssignableFrom(parameter)) schema.Type = "object";
                else if (typeof(JArray).IsAssignableFrom(parameter)) { schema.Type = "array"; schema.Items = new OpenApiSchema(); }
                components.Schemas[name] = schema;
                return schema;
            }

            // Dictionaries are JSON objects keyed by string: describe the value type only.
            // They also implement IEnumerable<KeyValuePair<,>>, so this must come before collections.
            var dictionaryValueType = GetDictionaryValueType(parameter);
            if (dictionaryValueType != null)
            {
                schema.Type = "object";
                schema.AdditionalPropertiesAllowed = true;
                schema.AdditionalProperties = ExplodeParameter(dictionaryValueType, components, maps, serializer);
                components.Schemas[name] = schema;
                return schema;
            }

            // Handle collection types first (List<T>, IList<T>, etc.)
            if (IsCollection(parameter))
            {
                schema.Type = "array";
                // Arrays aren't generic (string[] has no generic arguments), so read the element type
                // the way that works for both.
                var elementType = GetCollectionElementType(parameter);

                // A collection of itself (directly or through another collection) can't be described
                // finitely; leave its items free-form rather than recurse.
                var walking = inProgress.GetOrCreateValue(components);
                var collectionKey = "collection:" + parameter.FullName;
                if (!walking.Add(collectionKey))
                    return new OpenApiSchema { Type = "array", Items = new OpenApiSchema() };
                try
                {
                    schema.Items = ExplodeParameter(elementType, components, maps, serializer);
                }
                finally
                {
                    walking.Remove(collectionKey);
                }
                schema.Example = GetExample(parameter, maps, components, serializer);
                components.Schemas[name] = schema;
                return schema;
            }

            if (parameter.GenericTypeArguments.Length > 0)
            {
                foreach(var genArg in parameter.GenericTypeArguments)
                {
                    ExplodeParameter(genArg, components, maps, serializer);
                }
                schema.Type = "object";
            }

            if (maps.ContainsMap(parameter)){
                var map = maps.GetMap(parameter);
                schema = ExplodeParameter(map.Type, components, maps, serializer);
                schema.Example = map.OpenApiExample;
                components.Schemas[name] = schema;
            }
            else if (components.Schemas.ContainsKey(name))
            {
                if (inProgress.GetOrCreateValue(components).Contains(name))
                    return new OpenApiSchema
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = name }
                    };

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
                schema.Type = "array";
                schema.Items = jsonifed.Items[0].GetOpenApiSchema();
                schema.Example = GetExample(parameter, maps, components, serializer);
            }
            else if(parameter.GetProperties().Length != 0 && !IsNumericType(parameter))
            {
                Dictionary<string, OpenApiSchema> props = new Dictionary<string, OpenApiSchema>();
                // Registered and marked in progress before walking the properties, so a type that
                // refers back to itself through another type becomes a $ref instead of recursing
                // until the process dies with a StackOverflowException.
                schema.Properties = props;
                components.Schemas[name] = schema;
                var walking = inProgress.GetOrCreateValue(components);
                walking.Add(name);
                var namingStrategy = serializer?.ContractResolver is DefaultContractResolver resolver ? resolver.NamingStrategy : null;

                try
                {
                    foreach(var prop in parameter.GetProperties())
                    {

                        if (prop.GetCustomAttribute<IgnoreMemberAttribute>() != null || prop.PropertyType == parameter) continue;

                        // Use naming strategy for OpenAPI document generation only
                        var openApiPropertyName = namingStrategy != null ? namingStrategy.GetPropertyName(prop.Name, false) : prop.Name;
                        props[openApiPropertyName] = ExplodeParameter(prop.PropertyType, components, maps, serializer);
                    }
                }
                finally
                {
                    walking.Remove(name);
                }
            }
            else
            {
                schema.Type = "Object";
            }
            components.Schemas[name] = schema;
            return schema;

        }

        /// <summary>
        /// The components key a type's schema is stored under, and the id a $ref to it uses. Valid
        /// per the OpenAPI key pattern ^[a-zA-Z0-9.-_]+$, so List`1 or Nullable&lt;Double&gt; become
        /// List_1 and Nullable_Double_.
        /// </summary>
        public static string SchemaName(Type type)
        {
            var name = !IsCollection(type) && GetDictionaryValueType(type) == null &&
                       !typeof(JToken).IsAssignableFrom(type) && type.GenericTypeArguments.Length > 0
                ? type.GetGenericName()
                : type.Name;

            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (!char.IsAsciiLetterOrDigit(chars[i]) && chars[i] != '.' && chars[i] != '-' && chars[i] != '_')
                    chars[i] = '_';
            return new string(chars);
        }

        private static bool IsCollection(Type parameter) =>
            parameter != typeof(string) &&
            (parameter.IsGenericType &&
             (parameter.GetGenericTypeDefinition() == typeof(List<>) ||
              parameter.GetGenericTypeDefinition() == typeof(IList<>) ||
              parameter.GetGenericTypeDefinition() == typeof(ICollection<>) ||
              parameter.GetGenericTypeDefinition() == typeof(IEnumerable<>)) ||
             parameter.GetInterfaces().Any(i => i.IsGenericType &&
                 (i.GetGenericTypeDefinition() == typeof(IList<>) ||
                  i.GetGenericTypeDefinition() == typeof(ICollection<>) ||
                  i.GetGenericTypeDefinition() == typeof(IEnumerable<>))));

        private static Type GetCollectionElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return type.GetGenericArguments()[0];

            return type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0] ?? typeof(object);
        }

        private static Type GetDictionaryValueType(Type type)
        {
            static bool IsDictionary(Type t) => t.IsGenericType &&
                (t.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                 t.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

            var dictionary = IsDictionary(type) ? type : type.GetInterfaces().FirstOrDefault(IsDictionary);
            return dictionary?.GetGenericArguments()[1];
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
        // Examples nest no deeper than this; a cyclic type would otherwise recurse forever.
        private const int MaxExampleDepth = 8;

        static public IOpenApiAny GetExample(Type parameter, TypeMaps maps, OpenApiComponents components, Newtonsoft.Json.JsonSerializer serializer = null, int depth = 0)
        {
            if (depth > MaxExampleDepth) return new OpenApiNull();

            var schemaName = SchemaName(parameter);
            if (components.Schemas.ContainsKey(schemaName)) return components.Schemas[schemaName].Example;

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
                    var innerExample = GetExample(innerType, maps, components, serializer, depth + 1);
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
                    var propertyExample = GetExample(prop.PropertyType, maps, components, serializer, depth + 1);
                    if (propertyExample != null)
                        example.Add(propertyName, propertyExample);
                }
                return example;
            }

        }
    }
}
