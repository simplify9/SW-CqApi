using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SW.CqApi.UnitTests.Resources.Wire
{
    public enum WireStatus
    {
        Draft = 0,
        Posted = 5
    }

    public class WireLine
    {
        public string Sku { get; set; }
        public decimal Quantity { get; set; }
        public string[] Serials { get; set; }
    }

    public class WirePayload
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Missing { get; set; }
        public decimal Amount { get; set; }
        public double Ratio { get; set; }
        public bool Flag { get; set; }
        public WireStatus Status { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime? ClosedOn { get; set; }
        public Guid Uid { get; set; }
        public TimeSpan Window { get; set; }
        public List<WireLine> Lines { get; set; }
        public int[] Numbers { get; set; }
        public IDictionary<string, string> Extra { get; set; }
        public string Blob { get; set; }
    }

    // POST cqapi/wire -> returns the request unchanged, so a test can compare bytes in and out.
    class Echo : ICommandHandler<WirePayload, object>
    {
        public Task<object> Handle(WirePayload request) => Task.FromResult<object>(request);
    }

    // POST cqapi/wire/text -> a request type that is a bare JSON string.
    [HandlerName("text")]
    class EchoText : ICommandHandler<string, object>
    {
        public Task<object> Handle(string request) => Task.FromResult<object>(new { received = request });
    }

    // POST cqapi/wire/{key}/length -> keyed command, reports what arrived.
    [HandlerName("length")]
    class Length : ICommandHandler<int, WirePayload, object>
    {
        public Task<object> Handle(int key, WirePayload request) =>
            Task.FromResult<object>(new { key, blobLength = request.Blob?.Length ?? 0, lines = request.Lines?.Count ?? 0 });
    }

    // POST cqapi/wire/ping -> command without a request body.
    [HandlerName("ping")]
    class Ping : ICommandHandler<object>
    {
        public Task<object> Handle() => Task.FromResult<object>(new { pong = true });
    }

    // GET cqapi/wire/big?count=N -> a large response, to exercise buffering past the in-memory threshold.
    [HandlerName("big")]
    class Big : IQueryHandler<BigRequest, object>
    {
        public Task<object> Handle(BigRequest request)
        {
            var lines = new List<WireLine>();
            for (var i = 0; i < request.Count; i++)
                lines.Add(new WireLine { Sku = $"SKU-{i:D6}", Quantity = i, Serials = new[] { $"S{i}" } });
            return Task.FromResult<object>(lines);
        }
    }

    public class BigRequest
    {
        public int Count { get; set; }
    }
}
