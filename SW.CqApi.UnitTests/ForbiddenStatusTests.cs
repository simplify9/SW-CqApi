using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.HttpExtensions;
using SW.PrimitiveTypes;

namespace SW.CqApi.UnitTests
{
    namespace Resources.Forbidden
    {
        // GET cqapi/forbidden/deny
        [HandlerName("deny")]
        class Deny : IQueryHandler<object>
        {
            public Task<object> Handle() => throw new SWForbiddenException();
        }
    }

    [TestClass]
    public class ForbiddenStatusTests
    {
        static async Task<WebApplication> Start(bool forbiddenAs403)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddControllers();
            builder.Services.AddCqApi(o => o.ReturnForbiddenAs403 = forbiddenAs403, typeof(ForbiddenStatusTests).Assembly);
            builder.Services.AddAuthentication().AddJwtBearer();
            var app = builder.Build();
            app.UseRouting();
            app.UseAuthorization();
            app.UseHttpAsRequestContext();
            app.MapCqApi();
            await app.StartAsync();
            return app;
        }

        [TestMethod]
        public async Task ForbiddenIs401ByDefault()
        {
            await using var app = await Start(false);
            var response = await app.GetTestClient().GetAsync("cqapi/forbidden/deny");
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [TestMethod]
        public async Task ForbiddenIs403WhenOptedIn()
        {
            await using var app = await Start(true);
            var response = await app.GetTestClient().GetAsync("cqapi/forbidden/deny");
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType.MediaType);
        }

        [TestMethod]
        public async Task UnauthorizedStays401WhenOptedIn()
        {
            await using var app = await Start(true);
            // Cars' protectedget is [Protect]; no token means SWUnauthorizedException, still 401.
            var response = await app.GetTestClient().PostAsync("cqapi/cars/protectedget",
                new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
