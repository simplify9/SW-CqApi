using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.OpenApi.Readers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SW.CqApi.UnitTests
{
    [TestClass]
    public class OpenApiDocumentTests
    {
        static TestServer server;

        [ClassInitialize]
        public static void ClassInitialize(TestContext _)
        {
            server = new TestServer(new WebHostBuilder()
                .UseEnvironment("UnitTesting")
                .UseStartup<TestStartup>());
        }

        [ClassCleanup]
        public static void ClassCleanup() => server.Dispose();

        static async Task<string> GetDocument()
        {
            var response = await server.CreateClient().GetAsync("cqapi/swagger.json");
            var body = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
            return body;
        }

        [TestMethod]
        public async Task DocumentIsGeneratedForArraysDictionariesAndCycles()
        {
            var document = new OpenApiStringReader().Read(await GetDocument(), out var diagnostic);

            Assert.IsNotNull(document, string.Join("; ", diagnostic.Errors.Select(e => e.Message)));
            Assert.IsTrue(document.Paths.ContainsKey("/cqapi/docs/arrays"));
            Assert.IsTrue(document.Paths.ContainsKey("/cqapi/docs/{key}/cycle"));
        }

        [TestMethod]
        public async Task EveryPathStartsWithSlash()
        {
            var document = new OpenApiStringReader().Read(await GetDocument(), out _);

            var bare = document.Paths.Keys.Where(p => !p.StartsWith("/")).ToList();
            Assert.AreEqual(0, bare.Count, string.Join(", ", bare));
        }

        [TestMethod]
        public async Task ArrayElementAndDictionaryValueTypesAreDescribed()
        {
            var document = new OpenApiStringReader().Read(await GetDocument(), out _);
            var schemas = document.Components.Schemas;

            var serials = schemas["ArrayRequest"].Properties["serials"];
            Assert.AreEqual("array", serials.Type);
            Assert.AreEqual("string", serials.Items.Type);

            var totals = schemas["DictionaryRequest"].Properties["totals"];
            Assert.AreEqual("object", totals.Type);
            Assert.AreEqual("number", totals.AdditionalProperties.Type);
        }

        [TestMethod]
        public async Task DocumentIsBuiltOnceAndReused()
        {
            Assert.AreEqual(await GetDocument(), await GetDocument());
        }

        [TestMethod]
        public async Task DocumentPassesOpenApiValidation()
        {
            new OpenApiStringReader().Read(await GetDocument(), out var diagnostic);

            Assert.AreEqual(0, diagnostic.Errors.Count, string.Join("; ", diagnostic.Errors.Select(e => e.Message)));
        }

        [TestMethod]
        public async Task KeyedQueryPutsKeyInPathAndRequestInQuery()
        {
            // cars/QueryGen2: IQueryHandler<int, TestQDto> at GET /cqapi/cars/{key}?multiplier=
            var document = new OpenApiStringReader().Read(await GetDocument(), out _);
            var parameters = document.Paths["/cqapi/cars/{key}"].Operations[Microsoft.OpenApi.Models.OperationType.Get].Parameters;

            var key = parameters.Single(p => p.In == Microsoft.OpenApi.Models.ParameterLocation.Path);
            Assert.AreEqual("key", key.Name);
            Assert.IsTrue(key.Required);
            Assert.AreEqual("multiplier", parameters.Single(p => p.In == Microsoft.OpenApi.Models.ParameterLocation.Query).Name);
        }
    }
}
