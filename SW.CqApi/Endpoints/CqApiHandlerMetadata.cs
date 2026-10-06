using System;

namespace SW.CqApi
{
    /// <summary>
    /// Endpoint metadata naming the CqApi handler behind an endpoint, e.g. for tracing or logs:
    /// <c>context.GetEndpoint()?.Metadata.GetMetadata&lt;CqApiHandlerMetadata&gt;()</c>.
    /// </summary>
    public sealed class CqApiHandlerMetadata
    {
        internal CqApiHandlerMetadata(HandlerInfo handler)
        {
            Resource = handler.Resource;
            HandlerKey = handler.Key;
            HandlerType = handler.HandlerType;
        }

        /// <summary>The resource (folder) name, e.g. <c>shipments</c>.</summary>
        public string Resource { get; }

        /// <summary>The handler key within the resource, e.g. <c>post/key/saveitem</c>.</summary>
        public string HandlerKey { get; }

        public Type HandlerType { get; }
    }
}
