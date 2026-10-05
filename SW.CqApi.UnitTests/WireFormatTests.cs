using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SW.CqApi.UnitTests
{
    /// <summary>
    /// Pins the bytes CqApi puts on the wire. Written against 8.2.x before the streaming
    /// serializer change, so any drift in request parsing or response formatting fails here.
    /// </summary>
    [TestClass]
    public class WireFormatTests
    {
        static TestServer server;

        // Every property set, in declaration order, camelCase, enums as numbers.
        const string FullPayload =
            "{\"id\":7,\"name\":\"Ünïcode ✓ \\\"quoted\\\"\",\"missing\":null,\"amount\":12.50,\"ratio\":0.1," +
            "\"flag\":true,\"status\":5,\"createdOn\":\"2026-10-05T10:11:12.345\",\"closedOn\":null," +
            "\"uid\":\"6f1c2b8e-0d5a-4f43-9d6e-2a1b3c4d5e6f\",\"window\":\"01:02:03\"," +
            "\"lines\":[{\"sku\":\"A-1\",\"quantity\":2.0,\"serials\":[\"S1\",\"S2\"]}]," +
            "\"numbers\":[1,2,3],\"extra\":{\"KeepCase\":\"v\"},\"blob\":\"x\"}";

        [ClassInitialize]
        public static void ClassInitialize(TestContext _)
        {
            server = new TestServer(new WebHostBuilder()
                .UseEnvironment("UnitTesting")
                .UseStartup<TestStartup>());
        }

        [ClassCleanup]
        public static void ClassCleanup() => server.Dispose();

        static StringContent Json(string json) => new StringContent(json, Encoding.UTF8, "application/json");

        [TestMethod]
        public async Task EchoRoundTripsBytesExactly()
        {
            var response = await server.CreateClient().PostAsync("cqapi/wire", Json(FullPayload));
            var body = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
            // decimal keeps its scale (12.50, 2.0), dictionary keys keep their case, nulls are written.
            Assert.AreEqual(FullPayload, body);
        }

        [TestMethod]
        public async Task ResponseContentTypeIsApplicationJson()
        {
            var response = await server.CreateClient().PostAsync("cqapi/wire", Json(FullPayload));

            Assert.AreEqual("application/json", response.Content.Headers.ContentType.ToString());
        }

        [TestMethod]
        public async Task ReadsAreLenientLikeNewtonsoft()
        {
            // Wrong property case, enum by name, number as string: all accepted today.
            var lenient = "{\"ID\":\"8\",\"Status\":\"Posted\",\"FLAG\":true}";
            var response = await server.CreateClient().PostAsync("cqapi/wire", Json(lenient));
            var body = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
            StringAssert.StartsWith(body, "{\"id\":8,\"name\":null,");
            StringAssert.Contains(body, "\"flag\":true,\"status\":5,");
        }

        [TestMethod]
        public async Task KeyedCommandReceivesKeyAndBody()
        {
            var response = await server.CreateClient().PostAsync("cqapi/wire/41/length", Json(FullPayload));
            var body = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
            Assert.AreEqual("{\"key\":41,\"blobLength\":1,\"lines\":1}", body);
        }

        [TestMethod]
        public async Task LargeRequestBodyIsRead()
        {
            // Well past any in-memory buffering threshold (and past the 85 KB large-object limit).
            var blob = new string('b', 2_000_000);
            var payload = "{\"id\":1,\"blob\":\"" + blob + "\",\"lines\":[]}";
            var response = await server.CreateClient().PostAsync("cqapi/wire/1/length", Json(payload));
            var body = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
            Assert.AreEqual("{\"key\":1,\"blobLength\":2000000,\"lines\":0}", body);
        }

        [TestMethod]
        public async Task LargeResponseIsWrittenCompletely()
        {
            var response = await server.CreateClient().GetAsync("cqapi/wire/big?count=20000");
            var body = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            StringAssert.StartsWith(body, "[{\"sku\":\"SKU-000000\",\"quantity\":0.0,\"serials\":[\"S0\"]},");
            StringAssert.EndsWith(body, "{\"sku\":\"SKU-019999\",\"quantity\":19999.0,\"serials\":[\"S19999\"]}]");
        }

        [TestMethod]
        public async Task InvalidJsonIsBadRequest()
        {
            var response = await server.CreateClient().PostAsync("cqapi/wire", Json("{\"id\": "));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [TestMethod]
        public async Task EmptyBodyIsBadRequest()
        {
            var response = await server.CreateClient().PostAsync("cqapi/wire", Json(""));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [TestMethod]
        public async Task NullBodyIsBadRequest()
        {
            var response = await server.CreateClient().PostAsync("cqapi/wire", Json("null"));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [TestMethod]
        public async Task PrimitiveResultIsWrittenAsBefore()
        {
            // Object result from a GET query handler goes through the configured serializer.
            var response = await server.CreateClient().GetAsync("cqapi/cars");

            Assert.AreEqual("{\"plate\":0,\"carType\":0}", await response.Content.ReadAsStringAsync());
            Assert.AreEqual("application/json", response.Content.Headers.ContentType.MediaType);
        }

        [TestMethod]
        public async Task StringResultIsWrittenAsBefore()
        {
            // Strings bypass the serializer: Ok(string) writes them raw, unquoted, as text/plain.
            var response = await server.CreateClient().PostAsync("cqapi/cars/3", Json("{\"plate\":4}"));

            Assert.AreEqual("34", await response.Content.ReadAsStringAsync());
            Assert.AreEqual("text/plain", response.Content.Headers.ContentType.MediaType);
        }

        // Behaviour changes in the streaming version, both previously 400.

        [TestMethod]
        public async Task JsonStringBodyIsRead()
        {
            // Was 400: the body went through JsonElement.ToString(), which unquotes a string.
            var response = await server.CreateClient().PostAsync("cqapi/wire/text", Json("\"abc\""));
            var body = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
            Assert.AreEqual("{\"received\":\"abc\"}", body);
        }

        [TestMethod]
        public async Task CommandWithoutRequestTypeNeedsNoBody()
        {
            // Was 400: MVC required a non-empty body even for handlers that take none.
            var response = await server.CreateClient().PostAsync("cqapi/wire/ping", null);
            var body = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
            Assert.AreEqual("{\"pong\":true}", body);
        }
    }
}
