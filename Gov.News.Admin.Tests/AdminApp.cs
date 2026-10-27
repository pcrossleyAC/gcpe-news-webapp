using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Gov.News.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gov.News.Admin.Tests;

/// <summary>Skips a test when the Azurite storage emulator isn't listening on localhost:10000.</summary>
public sealed class AzuriteFactAttribute : FactAttribute
{
    private static readonly bool IsRunning = Probe();

    public AzuriteFactAttribute()
    {
        if (!IsRunning)
            Skip = "Azurite is not listening on localhost:10000.";
    }

    private static bool Probe()
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", 10000).Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// The real admin app, in memory, with its own empty Azurite container and Entra sign-in
/// replaced by a test scheme: requests carrying <see cref="TestAuthHandler.UserHeader"/> are signed in as that user.
/// </summary>
public sealed class AdminApp : WebApplicationFactory<Program>
{
    public BlobContainerClient Container { get; } = new("UseDevelopmentStorage=true", "admin-" + Guid.NewGuid().ToString("N"));
    public BlobSiteSettingsStore Store => new(Container);

    public AdminApp() => Container.Create();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AzureAd:TenantId", "organizations");
        builder.UseSetting("AzureAd:ClientId", "11111111-2222-3333-4444-555555555555");
        builder.UseSetting("AzureAd:ClientSecret", "test");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(Container);
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.Scheme;
                options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                options.DefaultChallengeScheme = TestAuthHandler.Scheme;
            });
        });
    }

    public Editor SignIn(string user)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        return new Editor(client);
    }
}

public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user))
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim("preferred_username", user.ToString()), new Claim(ClaimTypes.Name, user.ToString())], Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

/// <summary>A signed-in browser: loads the banner page and submits its form the way the browser would.</summary>
public sealed partial class Editor(HttpClient client)
{
    public HttpClient Client { get; } = client;
    public string Html { get; private set; } = "";
    private readonly Dictionary<string, string> _hidden = [];

    public async Task<HttpResponseMessage> OpenBannerAsync()
    {
        var response = await Client.GetAsync("/Banner");
        await ReadAsync(response);
        return response;
    }

    /// <summary>Posts the banner form with the page's hidden fields plus <paramref name="fields"/>.</summary>
    public async Task<HttpResponseMessage> SubmitAsync(string handler, IDictionary<string, string> fields, string? query = null)
    {
        var form = new Dictionary<string, string>(_hidden);
        foreach (var (key, value) in fields)
            form[key] = value;
        var response = await Client.PostAsync($"/Banner?handler={handler}{query}", new FormUrlEncodedContent(form));
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await OpenBannerAsync();
        else
            await ReadAsync(response);
        return response;
    }

    private async Task ReadAsync(HttpResponseMessage response)
    {
        Html = await response.Content.ReadAsStringAsync();
        _hidden.Clear();
        foreach (Match m in HiddenInput().Matches(Html))
        {
            var name = WebUtility.HtmlDecode(m.Groups["name"].Value);
            if (name is "__RequestVerificationToken" or "BaseETag")
                _hidden[name] = WebUtility.HtmlDecode(m.Groups["value"].Value);
        }
    }

    [GeneratedRegex("<input[^>]*type=\"hidden\"[^>]*name=\"(?<name>[^\"]+)\"[^>]*value=\"(?<value>[^\"]*)\"|<input[^>]*name=\"(?<name>[^\"]+)\"[^>]*type=\"hidden\"[^>]*value=\"(?<value>[^\"]*)\"")]
    private static partial Regex HiddenInput();
}
