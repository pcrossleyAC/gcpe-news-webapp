using Azure.Identity;
using Azure.Storage.Blobs;
using Gov.News.Admin;
using Gov.News.Settings;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

// Sign-in: an Entra ID app registration, configured under "AzureAd"
// (env vars AzureAd__TenantId, AzureAd__ClientId, AzureAd__ClientSecret).
// Who may sign in is controlled in the Azure portal: set "Assignment required"
// on the enterprise app and assign the editors' group.
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

var devSignIn = DevSignIn.IsEnabled(builder);
if (devSignIn)
    DevSignIn.Add(builder);

// Every page requires a signed-in user unless it opts out with [AllowAnonymous].
builder.Services.AddAuthorization(options => options.FallbackPolicy = options.DefaultPolicy);

builder.Services.AddRazorPages().AddMicrosoftIdentityUI();

// Settings storage. Locally, point ConnectionString at Azurite ("UseDevelopmentStorage=true").
// In OpenShift, set ServiceUri and the app registration above needs the
// "Storage Blob Data Contributor" role on the container.
builder.Services.AddSingleton(services =>
{
    var config = builder.Configuration.GetSection("SiteSettings");
    var containerName = config["Container"] ?? "site-settings";
    var connectionString = config["ConnectionString"];
    if (!string.IsNullOrEmpty(connectionString))
        return new BlobContainerClient(connectionString, containerName);

    var azureAd = builder.Configuration.GetSection("AzureAd");
    var credential = new ClientSecretCredential(azureAd["TenantId"], azureAd["ClientId"], azureAd["ClientSecret"]);
    var serviceUri = config["ServiceUri"] ?? throw new InvalidOperationException("Set SiteSettings:ServiceUri or SiteSettings:ConnectionString.");
    return new BlobServiceClient(new Uri(serviceUri), credential).GetBlobContainerClient(containerName);
});
builder.Services.AddSingleton(services => new BlobSiteSettingsStore(services.GetRequiredService<BlobContainerClient>()));

// OpenShift terminates TLS at the router; trust its X-Forwarded-* headers so the
// sign-in redirect goes back to https://, not http://.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHealthChecks();

var app = builder.Build();

// Locally (Azurite) create the container on first run; in Azure it is created in the portal.
if (app.Environment.IsDevelopment())
    app.Services.GetRequiredService<BlobContainerClient>().CreateIfNotExists();

if (devSignIn)
    app.Logger.LogWarning("DEV SIGN-IN IS ON: every request is signed in as {User}. Never use this outside your own machine.", builder.Configuration["DevSignIn:User"]);

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz").AllowAnonymous();
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapControllers();

app.Run();

// Lets Gov.News.Admin.Tests start the app with WebApplicationFactory<Program>.
public partial class Program;
