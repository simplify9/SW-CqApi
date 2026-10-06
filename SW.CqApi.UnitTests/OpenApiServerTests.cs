using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Readers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.HttpExtensions;
using System.Net;
using System.Threading.Tasks;

namespace SW.CqApi.UnitTests
{
    /// <summary>
    /// The services run behind the ingress with UsePathBase("/accounting") etc. The document must
    /// declare that path base as its server, or Swagger UI's "Try it out" calls /api/... at the
    /// host root.
    /// </summary>
    [TestClass]
    public class OpenApiServerTests
    {
        static TestServer server;

        class PathBaseStartup
        {
            private readonly TestStartup inner;

            public PathBaseStartup(IConfiguration configuration) => inner = new TestStartup(configuration);

            public void ConfigureServices(IServiceCollection services) => inner.ConfigureServices(services);

            public void Configure(IApplicationBuilder app)
            {
                app.UsePathBase("/svc");
                app.UseRouting();
                app.UseAuthorization();
                app.UseHttpAsRequestContext();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }
        }

        [ClassInitialize]
        public static void ClassInitialize(TestContext _)
        {
            server = new TestServer(new WebHostBuilder()
                .UseEnvironment("UnitTesting")
                .UseStartup<PathBaseStartup>());
        }

        [ClassCleanup]
        public static void ClassCleanup() => server.Dispose();

        static async Task<Microsoft.OpenApi.Models.OpenApiDocument> Get(string url)
        {
            var response = await server.CreateClient().GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
            var document = new OpenApiStringReader().Read(body, out var diagnostic);
            Assert.AreEqual(0, diagnostic.Errors.Count, string.Join("; ", diagnostic.Errors));
            return document;
        }

        [TestMethod]
        public async Task PathBaseIsDeclaredAsTheServer()
        {
            var document = await Get("svc/cqapi/swagger.json");

            Assert.AreEqual(1, document.Servers.Count);
            Assert.AreEqual("/svc", document.Servers[0].Url);
        }

        [TestMethod]
        public async Task WithoutPathBaseNoServerIsDeclared()
        {
            // UsePathBase is optional: the same app also answers without the prefix (as locally).
            var document = await Get("cqapi/swagger.json");

            Assert.AreEqual(0, document.Servers.Count);
        }

        [TestMethod]
        public async Task EachPathBaseGetsItsOwnDocument()
        {
            var withBase = await Get("svc/cqapi/swagger.json");
            var withoutBase = await Get("cqapi/swagger.json");
            var withBaseAgain = await Get("svc/cqapi/swagger.json");

            Assert.AreEqual("/svc", withBase.Servers[0].Url);
            Assert.AreEqual(0, withoutBase.Servers.Count);
            Assert.AreEqual("/svc", withBaseAgain.Servers[0].Url);
        }
    }
}
