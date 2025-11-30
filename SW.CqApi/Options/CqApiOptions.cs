using Microsoft.OpenApi.Any;
using SW.CqApi.AuthOptions;
using SW.CqApi.Options;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace SW.CqApi
{
    public class ResourceDescriptions
    {
        private readonly IDictionary<string, string> Descriptions = new Dictionary<string, string>();

        public void Add(string key, string description)
        {
            Descriptions[key] = description;
        }

        public string Get(string key)
        {
            if (Descriptions.ContainsKey(key))
                return Descriptions[key];
            else return null;
        }

        public bool ContainsDescription(string key)
        {
            if (Descriptions.ContainsKey(key)) return true;
            return false;
        }
    }

    public class CqApiOptions
    {
        /// <summary>
        /// The project's name
        /// </summary>
        public string ApplicationName { get; set; }

        /// <summary>
        /// The project's description
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The controller prefix, how the API is accessed. 
        /// Do not add a trailing slash.
        /// </summary>
        public string UrlPrefix { get; set; } = "cqapi";

        /// <summary>
        /// Any resource with its key found here, will have its description replaced with the value
        /// </summary>
        public ResourceDescriptions ResourceDescriptions { get; }

        /// <summary>
        /// Authentication settings
        /// </summary>
        public CqApiAuthOptions AuthOptions { get; set; }

        /// <summary>
        /// Maps for types, how they're sent through the API
        /// </summary>
        public TypeMaps Maps { get; }

        /// <summary>
        /// Protect all APIs by default. To unprotect specific APIs, use UnProtect attribute.
        /// </summary>
        public bool ProtectAll { get; set; }
        
        /// <summary>
        /// OpenApi Documentation is added by default. Use this option to disable it.
        /// </summary>
        public bool DisableOpenApiDocumentation { get; set; }

        public string RolePrefix { get; set; }

        public Newtonsoft.Json.JsonSerializer Serializer { get; set; }

        /// <summary>
        /// Custom attributes to be preserved and applied to endpoints. 
        /// This allows CqApi to recognize and apply ASP.NET Core attributes like [EnableRateLimiting].
        /// </summary>
        public IList<Type> PreserveCustomAttributes { get; }

        public CqApiOptions()
        {
            Maps = new TypeMaps();
            AuthOptions = new CqApiAuthOptions();
            ResourceDescriptions = new ResourceDescriptions();
            PreserveCustomAttributes = new List<Type>();
            Serializer = new Newtonsoft.Json.JsonSerializer();
            Serializer.ContractResolver = new CamelCasePropertyNamesContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    ProcessDictionaryKeys = false
                }
            };
        }
    }
}