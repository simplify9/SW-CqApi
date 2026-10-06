using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.CqApi.UnitTests.Resources.Endpoints;
using SW.HttpExtensions;

namespace SW.CqApi.UnitTests
{
    /// <summary>
    /// CqApi 10 maps one endpoint per handler. URLs and responses must match the old single
    /// controller (checked request by request against 8.2.15 when this was written); what's new is
    /// that routing, middleware and tracing can see which handler a request is for.
    /// </summary>
    [TestClass]
    public class EndpointTests
    {
        static WebApplication app;

        [ClassInitialize]
        public static async Task ClassInitialize(TestContext _)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddControllers();
            builder.Services.AddCqApi(o => o.PreserveCustomAttributes.Add(typeof(MarkerAttribute)), typeof(EndpointTests).Assembly);
            builder.Services.AddAuthentication().AddJwtBearer();

            app = builder.Build();
            app.UseRouting();
            // What a rate limiter or tracer sees once routing has picked the endpoint.
            app.Use(async (context, next) =>
            {
                var endpoint = context.GetEndpoint();
                var handler = endpoint?.Metadata.GetMetadata<CqApiHandlerMetadata>();
                if (handler != null) context.Response.Headers["X-Handler"] = $"{handler.Resource} {handler.HandlerKey}";
                var marker = endpoint?.Metadata.GetMetadata<MarkerAttribute>();
                if (marker != null) context.Response.Headers["X-Marker"] = marker.Name;
                if (endpoint is RouteEndpoint route) context.Response.Headers["X-Route"] = route.RoutePattern.RawText;
                await next();
            });
            app.UseAuthorization();
            app.UseHttpAsRequestContext();
            app.MapControllers();
            app.MapCqApi();
            await app.StartAsync();
        }

        [ClassCleanup]
        public static async Task ClassCleanup() => await app.DisposeAsync();

        [TestMethod]
        public void EachHandlerIsItsOwnEndpoint()
        {
            var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
                .Where(e => e.Metadata.GetMetadata<CqApiHandlerMetadata>() != null)
                .ToDictionary(e => e.DisplayName);

            Assert.IsTrue(endpoints.ContainsKey("GET cqapi/cars"));
            Assert.IsTrue(endpoints.ContainsKey("GET cqapi/cars/search"));
            Assert.IsTrue(endpoints.ContainsKey("GET cqapi/cars/{key}"));
            Assert.IsTrue(endpoints.ContainsKey("POST cqapi/wire/{key}/length"));
            Assert.IsTrue(endpoints.ContainsKey("DELETE cqapi/bikes/{key}"));
            Assert.AreEqual("get/key/wheel", endpoints["GET cqapi/bikes/{key}/wheel"].Metadata.GetMetadata<CqApiHandlerMetadata>().HandlerKey);
        }

        [TestMethod]
        public async Task MiddlewareSeesTheHandlerItsRouteAndPreservedAttributes()
        {
            var response = await app.GetTestClient().GetAsync("cqapi/endpoints/marked");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("endpoints get/marked", response.Headers.GetValues("X-Handler").Single());
            Assert.AreEqual("cqapi/endpoints/marked", response.Headers.GetValues("X-Route").Single());
            Assert.AreEqual("limited", response.Headers.GetValues("X-Marker").Single());
        }

        [TestMethod]
        public async Task NamedHandlerBeatsKey()
        {
            var client = app.GetTestClient();

            var named = await client.GetAsync("cqapi/bikes/3/wheel");
            var keyed = await client.GetAsync("cqapi/cars/5");

            Assert.AreEqual("bikes get/key/wheel", named.Headers.GetValues("X-Handler").Single());
            Assert.AreEqual("cars get/key", keyed.Headers.GetValues("X-Handler").Single());
            Assert.AreEqual("cqapi/cars/{key}", keyed.Headers.GetValues("X-Route").Single());
        }

        [TestMethod]
        public async Task UnknownHandlerAnswersAsBefore()
        {
            var client = app.GetTestClient();

            var unknownResource = await client.GetAsync("cqapi/nothing");
            Assert.AreEqual(HttpStatusCode.NotFound, unknownResource.StatusCode);
            Assert.AreEqual("nothing/get", await unknownResource.Content.ReadAsStringAsync());

            var unknownToken = await client.PostAsync("cqapi/nothing/1", null);
            Assert.AreEqual(HttpStatusCode.NotFound, unknownToken.StatusCode);
            Assert.AreEqual("application/problem+json", unknownToken.Content.Headers.ContentType.MediaType);

            var unknownCommand = await client.PostAsync("cqapi/cars/9/unknown", null);
            Assert.AreEqual("cars/post/key/unknown", await unknownCommand.Content.ReadAsStringAsync());
        }

        [TestMethod]
        public async Task LookupIsBoundLikeMvc()
        {
            var client = app.GetTestClient();

            var empty = await client.GetAsync("cqapi/cars?lookup=");
            var bad = await client.GetAsync("cqapi/cars?lookup=maybe");

            Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
            StringAssert.Contains(await empty.Content.ReadAsStringAsync(), "The value '' is invalid.");
            StringAssert.Contains(await bad.Content.ReadAsStringAsync(), "The value 'maybe' is not valid.");
        }

        [TestMethod]
        public async Task OpenApiDocumentIsServedAsJson()
        {
            var response = await app.GetTestClient().GetAsync("cqapi/swagger.json");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/json", response.Content.Headers.ContentType.MediaType);
        }
    }
}
