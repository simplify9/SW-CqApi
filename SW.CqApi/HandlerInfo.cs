using System;
using System.Collections.Generic;
using System.Reflection;
using SW.PrimitiveTypes;

namespace SW.CqApi
{
    public class HandlerInfo
    {
        public Type HandlerType { get; set; }
        public MethodInfo Method { get; set; }
        public IList<Type> ArgumentTypes { get; set; }
        public string Key { get; set; }
        public string Resource { get; set; }

        public Type NormalizedInterfaceType { get; set; }

        /// <summary>
        /// Custom attributes found on the handler that should be preserved
        /// </summary>
        public IList<Attribute> CustomAttributes { get; set; } = new List<Attribute>();

        // Everything below is worked out once at startup, so a request does no reflection.

        internal HandlerInvoker Invoker { get; set; }

        /// <summary>True when the request must carry a valid <see cref="RequestContext"/>.</summary>
        internal bool RequiresAuthentication { get; set; }

        /// <summary>Role claims of which one is required, or null when no role is required.</summary>
        internal string[] RequiredRoles { get; set; }
    }
}
