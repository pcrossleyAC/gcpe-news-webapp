# gcpe-news-webapp
BC Government News web application


Introduction
----------------
This is a C# application that provides a user interface for the BC Gov News website. 

Repository Map
--------------
- .s2i : Source to image bash assemble script
- **Gov.News.WebApp**: C# .NET 10 MVC Frontend App (the public site)
- **Gov.News.Admin**: Internal site-settings admin (Razor Pages, Entra ID sign-in). Not publicly routed.
- **Gov.News.Settings**: Shared settings model, validation and Azure Blob storage, used by both apps
- **Gov.News.Settings.Tests**: Tests for the settings library
- Gov.News.sln: Solution containing all of the above
- Dotnet-Snyk: Source for a Dotnet + Snyk container
- Dotnet-Sonar: Source for a SonarQube scanner
- functional-tests: BDD Tests
- OpenShift: OpenShift scripts and documentation


Installation
------------
Follow the instructions in the [OpenShift](openshift) folder.
	
Developer Prerequisites
-----------------------

**Gov.News.WebApp**
- Visual Studio 2022
(or comparable IDE able to edit C# code)


**DevOps**
- OpenShift Container Platform 3.7 or newer (Origin is supported)
- RedHat OpenShift tools
- Docker
- Full Git install, including Git bash  
- A familiarity with Jenkins

Development
-----------
A Visual Studio 2017 solution has been provided, however Visual Studio Pro is not required to build the solution.  You may use Visual Studio Community or the command line (dotnet build) 


Contribution
------------

Please report any [issues](https://github.com/bcgov/gcpe-news-webapp/issues).

[Pull requests](https://github.com/bcgov/gcpe-news-webapp/pulls) are always welcome.

If you would like to contribute, please see our [contributing](CONTRIBUTING.md) guidelines.

Please note that this project is released with a [Contributor Code of Conduct](CODE_OF_CONDUCT.md). By participating in this project you agree to abide by its terms.

License
-------

    Copyright 2018 Province of British Columbia

    Licensed under the Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at 

       http://www.apache.org/licenses/LICENSE-2.0

    Unless required by applicable law or agreed to in writing, software
    distributed under the License is distributed on an "AS IS" BASIS,
    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
    See the License for the specific language governing permissions and
    limitations under the License.

Maintenance
-----------

This repository is maintained by [GCPE](http://www.gov.bc.ca/).
Click [here](https://github.com/orgs/bcgov/teams/gcpe/repositories) for a complete list of our repositories on GitHub.

Site settings
-------------
Banner, footer links, social links and theme colours live in one JSON document in an
Azure Blob container (`site-settings`), edited through **Gov.News.Admin**:

- `live.json` is what the public site reads (cached for 60 seconds; falls back to the last
  good copy, then to `Gov.News.Settings/defaults.json`)
- `drafts/<editor>.json` holds each editor's own unpublished changes
- `history/NNNNNN.json` keeps every published revision for restore

Publishing is guarded by blob ETags, so two editors can't overwrite each other.

**Admin configuration** (environment variables)

| Variable | Value |
|---|---|
| `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__ClientSecret` | The Entra ID app registration |
| `SiteSettings__ServiceUri` | e.g. `https://<account>.blob.core.windows.net` |
| `SiteSettings__Container` | Defaults to `site-settings` |

**Public site configuration** (environment variables; read-only access)

| Variable | Value |
|---|---|
| `SiteSettings__ConnectionString` | Azurite (`UseDevelopmentStorage=true`) or a connection string with a read-only SAS token |
| or `SiteSettings__ServiceUri` + `SiteSettings__TenantId`, `__ClientId`, `__ClientSecret` | An app registration with **Storage Blob Data Reader** on the container |
| `SiteSettings__Container` | Defaults to `site-settings` |

With none of these set, the public site uses the built-in defaults and behaves exactly as before.
It checks for a new revision once a minute, and pages are cached in browsers for a minute,
so a publish reaches visitors within about two minutes.

In the Azure portal: add `https://<admin host>/signin-oidc` as a redirect URI, set
**Assignment required** on the enterprise application and assign the editors' group, and give
the app registration **Storage Blob Data Contributor** on the container.

**Running the tests**

The storage tests run against [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite)
and are skipped if it isn't running:

    docker run -d -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0
    dotnet test Gov.News.sln

**Trying the admin locally without Entra**

With Azurite running, this signs every request in as the given user. It only works when
`ASPNETCORE_ENVIRONMENT=Development`; in any other environment the setting is ignored.

    ASPNETCORE_ENVIRONMENT=Development DevSignIn__User=you@gov.bc.ca \
    SiteSettings__ConnectionString=UseDevelopmentStorage=true \
    AzureAd__TenantId=organizations AzureAd__ClientId=00000000-0000-0000-0000-000000000000 \
    dotnet run --project Gov.News.Admin --no-launch-profile
