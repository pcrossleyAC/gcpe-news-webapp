using System;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;
using Gov.News.WebApp;
using Gov.News.Website.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Polly;
using Polly.Caching.Memory;

namespace Gov.News.Website
{
    public class Startup
    {
        // test date for granville age
        public static DateTime granvilleTestDate;

        public Startup(IWebHostEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath);
            //    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            if (env.IsDevelopment())
                builder.AddUserSecrets<Startup>();
            if (!System.Diagnostics.Debugger.IsAttached)
                builder.AddEnvironmentVariables(); // for openshift

            Configuration = builder.Build();

            Configuration.Bind(Properties.Settings.Default);

            if (Configuration["GranvilleTestDate"] != null)
            {
                try
                {
                    granvilleTestDate = DateTime.Parse(Configuration["GranvilleTestDate"]);
                }
                catch (SystemException)
                {
                }
            }

            //Data.Repository.RepositoryException += (ex) => Program.ReportException(null, ex);
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddMemoryCache();
            services.AddRazorPages().AddRazorRuntimeCompilation();
            services.AddMvc(opt =>
                {
                    opt.EnableEndpointRouting = false;
                })
                .AddMvcOptions(options =>
            {
#if DEBUG
                var cacheProfile = new CacheProfile { Location = ResponseCacheLocation.None, NoStore = true };
                var cacheProfileNoCache = new CacheProfile { Location = ResponseCacheLocation.None, NoStore = true };
#else
                var cacheProfile = new CacheProfile { Duration = 60 };
                var cacheProfileNoCache = new CacheProfile { Location = ResponseCacheLocation.None, NoStore = true };
#endif
                options.CacheProfiles.Add("Default", cacheProfile);
                options.CacheProfiles.Add("Feed", cacheProfile);
                options.CacheProfiles.Add("Embed", cacheProfile);
                options.CacheProfiles.Add("Page", cacheProfile);
                options.CacheProfiles.Add("Archive", cacheProfile);
                options.CacheProfiles.Add("NoCache", cacheProfileNoCache);

                options.Filters.Add(new TypeFilterAttribute(typeof(XFrameOptionsAttribute)));

                options.Filters.Add(new TypeFilterAttribute(typeof(RequirePermanentHttpsAttribute)));

                options.Filters.Add(new ExceptionReportingFilter());
            });

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            //TODO: Change to ServiceLifetime.Scoped once repository is no longer using static methods
            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            };

            services.AddSingleton(new Func<IServiceProvider, Gov.News.Api.IClient>((serviceProvider) =>
            {
                var client = new Gov.News.Api.Client(handler);
                client.BaseUri = new Uri(Configuration["NewsApi"]);
                return client;
            }));

            /*
            services.AddSingleton(new Func<IServiceProvider, Gcpe.Hub.Services.Legacy.INewslettersClient>((serviceProvider) =>
            {
                var client = new Gcpe.Hub.Services.Legacy.NewslettersClient();
                client.BaseUri = new Uri(Configuration.GetConnectionString("HubNewslettersClient"));
                return client;
            }));


            services.Configure<Data.RepositoryOptions>(Configuration.GetSection("Options:Gov.News.Data:Repository"));
                */

            
            services.AddSingleton<Repository, Repository>();

            services.AddSingleton<IHostedService, Hubs.LiveHub>();

            // Add the Configuration object so that controllers may use it through dependency injection
            services.AddSingleton<IConfiguration>(Configuration);

            services.Replace(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(TimestampLogger<>)));

            services
                .AddHealthChecks()
                .AddUrlGroup(new Uri(Configuration["NewsApi"] + "hc"));

            IAsyncPolicy<HttpResponseMessage> cachePolicy =
              Policy.CacheAsync<HttpResponseMessage>(
                  cacheProvider: new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions())),
                  ttl: new TimeSpan(0, 1, 0)
              );

            services.AddHttpClient("uri-group", client =>
            {
                client.BaseAddress = new Uri(Configuration["NewsApi"] + "hc");
                }).ConfigurePrimaryHttpMessageHandler(() =>
                {
                // Configure HttpClientHandler to enforce TLS 1.2
                return new HttpClientHandler
                {
                    SslProtocols = SslProtocols.Tls12
                };
            }) // default healthcheck registration name for uri ( you can change it on AddUrlGroup )	 
             .AddPolicyHandler(cachePolicy);
            services.AddRazorPages().AddRazorRuntimeCompilation();
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureDevelopmentServices(IServiceCollection services)
        {
            ConfigureServices(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/error");
            }

            app.UseHealthChecks("/hc");

            app.UseRedirect();

            // set headers for static files
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=600";
                    ctx.Context.Response.Headers[HeaderNames.Pragma] = "no-cache";
                    ctx.Context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                    ctx.Context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
                    ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                }
            });

#if USE_JAVASCRIPT_SIGNALR
            if (Properties.Settings.Default.SignalREnabled != null && Properties.Settings.Default.SignalREnabled=="true")
            {
                app.UseSignalR();
            }
#endif
            app.UseMvc(routes =>
            {
                routes.RegisterRoutes();
            });
        }
    }
}