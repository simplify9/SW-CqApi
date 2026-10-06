using System;
using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.CqApi.UnitTests.Resources.Endpoints
{
    /// <summary>Stands in for an attribute like [EnableRateLimiting] listed in PreserveCustomAttributes.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MarkerAttribute : Attribute
    {
        public MarkerAttribute(string name) => Name = name;
        public string Name { get; }
    }

    // GET cqapi/endpoints/marked
    [HandlerName("marked")]
    [Marker("limited")]
    class Marked : IQueryHandler<object>
    {
        public Task<object> Handle() => Task.FromResult<object>(new { ok = true });
    }
}
