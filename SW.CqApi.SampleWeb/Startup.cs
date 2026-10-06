using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SW.CqApi.SampleWeb.Model;
using SW.HttpExtensions;
using SW.PrimitiveTypes;

namespace SW.CqApi.SampleWeb
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers().AddJsonOptions(config =>
            {
                config.JsonSerializerOptions.PropertyNamingPolicy = null;
            });

            // var serializer = new JsonSerializer();
            // serializer.Converters.Add(new PropertyMatchSpecificationJsonConverter());
            // serializer.ContractResolver = new CamelCasePropertyNamesContractResolver();

            services.AddCqApi(config =>
            {
                config.ResourceDescriptions.Add("Parcels", "Description test");
                // config.ProtectAll = true;
                config.UrlPrefix = "api";
              //  config.Serializer = serializer;
            });

            services.AddRazorPages();

            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/";
            }).AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.SaveToken = true;

                options.TokenValidationParameters = new TokenValidationParameters()
                {
                    ValidIssuer = Configuration["Token:Issuer"],
                    ValidAudience = Configuration["Token:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["Token:Key"]))
                };
            });

            services.AddScoped<RequestContext>();
            services.AddJwtTokenParameters();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();
            //app.UseCqApi();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseHttpAsRequestContext();

            app.UseSwaggerUI(c => { c.SwaggerEndpoint("/api/swagger.json", "My API V1"); });

            app.Use(async (context, next) =>
            {
                var routeData = context.GetRouteData();
                //var tenant = routeData?.Values["tenant"]?.ToString();
                //if (!string.IsNullOrEmpty(tenant))
                //    context.Items["tenant"] = tenant;

                await next();
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapCqApi();
                endpoints.MapRazorPages();
            });
        }
    }
}