using Microsoft.Extensions.DependencyInjection;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Security.Claims;

namespace SW.CqApi
{
    internal static class IServiceProviderExtensions
    {
        /// <summary>
        /// Resolves the handler and applies its protection: authentication when the handler is
        /// protected (or <see cref="CqApiOptions.ProtectAll"/> is on and it isn't unprotected),
        /// plus a role claim when it is marked <c>[Protect(RequireRole = true)]</c>.
        /// </summary>
        public static object GetHandlerInstance(this IServiceProvider serviceProvider, HandlerInfo handlerInfo)
        {
            var instance = serviceProvider.GetService(handlerInfo.HandlerType);

            if (instance is null)
                throw new SWException($"Could not find required service {handlerInfo.Key} for resource {handlerInfo.Resource}.");

            if (handlerInfo.RequiresAuthentication)
            {
                var requestContext = serviceProvider.GetRequiredService<RequestContext>();

                if (!requestContext.IsValid)
                    throw new SWUnauthorizedException();

                if (handlerInfo.RequiredRoles != null &&
                    !requestContext.User.Claims.Any(c => c.Subject.RoleClaimType == ClaimTypes.Role && handlerInfo.RequiredRoles.Contains(c.Value, StringComparer.OrdinalIgnoreCase)))
                    throw new SWForbiddenException();
            }

            return instance;
        }
    }
}
