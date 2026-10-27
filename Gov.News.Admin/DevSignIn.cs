using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Gov.News.Admin;

/// <summary>
/// Local-only stand-in for Entra sign-in, so the admin can be tried before an app registration exists.
/// It turns on only when BOTH the environment is Development AND <c>DevSignIn:User</c> is set;
/// every request is then signed in as that user.
/// </summary>
public static class DevSignIn
{
    public const string Scheme = "DevSignIn";

    public static bool IsEnabled(WebApplicationBuilder builder) =>
        builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(builder.Configuration["DevSignIn:User"]);

    public static void Add(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, Handler>(Scheme, _ => { });
        builder.Services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = Scheme;
            options.DefaultAuthenticateScheme = Scheme;
            options.DefaultChallengeScheme = Scheme;
        });
    }

    private sealed class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration config)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = config["DevSignIn:User"]!;
            var identity = new ClaimsIdentity([new Claim("preferred_username", user), new Claim(ClaimTypes.Name, user)], DevSignIn.Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), DevSignIn.Scheme)));
        }
    }
}
