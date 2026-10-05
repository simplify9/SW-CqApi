using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SW.CqApi.UnitTests.Resources.Docs
{
    // Request and response shapes that used to break swagger.json generation.

    public class ArrayRequest
    {
        public string[] Serials { get; set; }
        public int[][] Grid { get; set; }
        public ArrayItem[] Items { get; set; }
    }

    public class ArrayItem
    {
        public string Code { get; set; }
    }

    public class DictionaryRequest
    {
        public Dictionary<string, decimal> Totals { get; set; }
        public IDictionary<string, ArrayItem> ByCode { get; set; }
    }

    // A -> B -> A: recursed until StackOverflowException before.
    public class CycleParent
    {
        public string Name { get; set; }
        public List<CycleChild> Children { get; set; }
    }

    public class CycleChild
    {
        public CycleParent Parent { get; set; }
        public CycleChild[] Siblings { get; set; }
    }

    [HandlerName("arrays")]
    class Arrays : ICommandHandler<ArrayRequest, ArrayItem[]>
    {
        public Task<ArrayItem[]> Handle(ArrayRequest request) => Task.FromResult(request.Items);
    }

    [HandlerName("dictionaries")]
    class Dictionaries : ICommandHandler<DictionaryRequest, object>
    {
        public Task<object> Handle(DictionaryRequest request) => Task.FromResult<object>(request);
    }

    [HandlerName("cycle")]
    class Cycle : ICommandHandler<int, CycleParent, CycleChild>
    {
        public Task<CycleChild> Handle(int key, CycleParent request) => Task.FromResult(new CycleChild());
    }

    class Search : IQueryHandler<ArrayRequest, List<ArrayItem>>
    {
        public Task<List<ArrayItem>> Handle(ArrayRequest request) => Task.FromResult(new List<ArrayItem>());
    }
}
