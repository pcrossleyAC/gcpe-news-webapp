using System.Net;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gov.News.Admin.Tests;

public class DevSignInTests
{
    private static HttpClient Start(string environment, string? devUser)
    {
        var container = new BlobContainerClient("UseDevelopmentStorage=true", "dev-" + Guid.NewGuid().ToString("N"));
        container.Create();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("AzureAd:TenantId", "organizations");
            builder.UseSetting("AzureAd:ClientId", "11111111-2222-3333-4444-555555555555");
            builder.UseSetting("AzureAd:ClientSecret", "test");
            if (devUser is not null)
                builder.UseSetting("DevSignIn:User", devUser);
            builder.ConfigureServices(services => services.AddSingleton(container));
        });
        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [AzuriteFact]
    public async Task Production_ignores_the_dev_sign_in_setting_and_sends_people_to_Entra()
    {
        var response = await Start("Production", "intruder@example.com").GetAsync("/Banner");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://login.microsoftonline.com/", response.Headers.Location!.ToString());
    }

    [AzuriteFact]
    public async Task Development_without_the_setting_still_requires_Entra()
    {
        var response = await Start("Development", null).GetAsync("/Banner");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://login.microsoftonline.com/", response.Headers.Location!.ToString());
    }

    [AzuriteFact]
    public async Task Development_with_the_setting_signs_in_as_that_user()
    {
        var response = await Start("Development", "jane.editor@gov.bc.ca").GetAsync("/Banner");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("jane.editor@gov.bc.ca", await response.Content.ReadAsStringAsync());
    }
}
