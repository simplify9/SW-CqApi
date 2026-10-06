using System;
using Microsoft.AspNetCore.Builder;

namespace SW.CqApi.Extensions
{
    public static class IApplicationBuilderExtensions
    {
        /// <summary>
        /// No longer needed: each handler is its own endpoint, so preserved attributes
        /// (<see cref="CqApiOptions.PreserveCustomAttributes"/>) are endpoint metadata from startup.
        /// </summary>
        [Obsolete("Not needed with MapCqApi(); preserved attributes are endpoint metadata. Remove the call.")]
        public static void UseCqApiAttributeMiddleware(this IApplicationBuilder app)
        {
        }
    }
}
