using Gov.News.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Gov.News.Api;
using Gov.News.Api.Models;
using Gov.News.Website.Middleware;
using Gov.News.Website.Models;
using Gov.News.Website.Providers;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Link = Gov.News.Website.Models.ConnectViewModel.ExternalConnectLink;

namespace Gov.News.Website.Controllers
{
    public class DefaultController : Shared.IndexController<Home>
    {
        private readonly IWebHostEnvironment _env;
        private readonly ISiteSettingsSource _siteSettings;

        public DefaultController(Repository repository, IConfiguration configuration, IWebHostEnvironment env, ISiteSettingsSource siteSettings) : base(repository, configuration)
        {
            _env = env;
            _siteSettings = siteSettings;
        }

        /// <summary>Featured accounts first, then the rest by name.</summary>
        private static Link[] ToLinks(SocialNetworkLinks links) =>
            links.InDisplayOrder().Select(l => new Link() { Url = l.Url, Title = l.Text, Summary = l.Summary }).ToArray();

        [ResponseCache(CacheProfileName = "Default"), Noarchive]
        public async Task<ActionResult> Reference(string reference)
        {
            if (!Regex.IsMatch(reference, @"\d+"))
                throw new ArgumentException();

            var pair = await Repository.ApiClient.Posts.GetKeyFromReferenceAsync(string.Format("NEWS-{0}", reference), Repository.APIVersion);
            if (pair == null)
                return await NotFound();

            return Redirect(NewsroomExtensions.GetPostUri(pair.Value, pair.Key).ToString());
        }

        [ResponseCache(CacheProfileName = "Default"), Noarchive]
        public async Task<ActionResult> Index(string postKind)
        {
            var model = await GetHomePosts(postKind);

            if (model == null)
                return await SearchNotFound();
            ViewBag.GoogleSiteVerification = Properties.Settings.Default.GoogleSiteVerification;
            ViewBag.BingSiteVerification = Properties.Settings.Default.BingSiteVerification;
            if (Properties.Settings.Default.NewsMediaHostUri != null)
            {
                ViewBag.ProxyUrl = Properties.Settings.Default.NewsMediaHostUri.ToString() + "embed/";
            }
            else
            {
                ViewBag.ProxyUrl = new Uri("https://media.news.gov.bc.ca/embed/").ToString();
            }

            return View("HomeView", model);
        }

        [Route("robots.txt")]
        public ContentResult DynamicRobotsFile()
        {
            StringBuilder content = new StringBuilder();

            if (!_env.IsProduction())
            {
                content.AppendLine("user-agent: *");
                content.AppendLine("Disallow: /");
            }
            else
            {
                content.AppendLine("user-agent: *");
                content.AppendLine("Disallow: /assets");
                content.AppendLine("Disallow: /error");
                content.AppendLine("Disallow: /files");

                // search pages (all case variants)
                content.AppendLine("Disallow: /search");
                content.AppendLine("Disallow: /Search");
                content.AppendLine("Disallow: /SEARCH");

                content.AppendLine("Disallow: /subscribe");
            }

            return this.Content(content.ToString(), "text/plain", Encoding.UTF8);
        }

        [ResponseCache(CacheProfileName = "Feed"), Noindex]
        public async Task<ActionResult> Top(string type, string format)
        {
            if (!string.Equals(type, "feed", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException();

            var topKeys = IndexModel.GetTopPostKeys(await GetAllCategories());

            var model = await GetSyndicationFeedViewModel("Top Stories", topKeys);
            return await GetNewsFeedContent(format, model, false, false);
        }

        [ResponseCache(CacheProfileName = "Feed"), Noindex]
        public async Task<ActionResult> CategoryTop(string key, string type, string format)
        {
            if (!string.Equals(type, "feed", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException();

            var categories = await GetAllCategories();
            var category = categories.Where(c => c.Key == key).ToList();
            var topKeys = IndexModel.GetTopPostKeys(category);

            var model = await GetSyndicationFeedViewModel("Top Stories", topKeys);
            return await GetNewsFeedContent(format, model, false, false);
        }

        [ResponseCache(CacheProfileName = "Feed"), Noindex]
        public async Task<ActionResult> Feature(string type, string format)
        {
            if (!string.Equals(type, "feed", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException();

            var featureKeys = IndexModel.GetFeaturePostKeys(await GetAllCategories());

            var model = await GetSyndicationFeedViewModel("Featured Stories", featureKeys);
            return await GetNewsFeedContent(format, model, false, false);
        }

        [ResponseCache(CacheProfileName = "Feed"), Noindex]
        public async Task<ActionResult> CategoryFeature(string key, string type, string format)
        {
            if (!string.Equals(type, "feed", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException();

            var categories = await GetAllCategories();
            var category = categories.Where(c => c.Key == key).ToList();
            var topKeys = IndexModel.GetFeaturePostKeys(category);

            var model = await GetSyndicationFeedViewModel("Top Stories", topKeys);
            return await GetNewsFeedContent(format, model, false, false);
        }

        [ResponseCache(CacheProfileName = "Page")]
        public async Task<ActionResult> Connect()
        {
            var model = await GetConnectModel();
            return View("ConnectView", model);
        }

        [Noindex]
        public async Task<ActionResult> Search(string q = null, string date = null, string ministry = null, string sector = null, string city = null, string content = null, string language = null,
            DateTime? fromDate = null, DateTime? toDate = null, string page = null)
        {
            var filters = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(date))
            {
                filters.Add("Date", date);
            }
            if (ministry != null)
            {
                var ministries = await Repository.GetMinistriesAsync();
                var ministryName = ministries.FirstOrDefault(m => m.Key == ministry)?.Name;
                filters.Add("Ministry", ministryName ?? ministry);
            }
            if (sector != null)
            {
                var sectors = await Repository.GetSectorsAsync();
                var sectorName = sectors.FirstOrDefault(s => s.Key == sector)?.Name;
                filters.Add("Sector", sectorName ?? sector);
            }
            if (!string.IsNullOrEmpty(city))
            {
                filters.Add("City", city);
            }
            if (content != null)
            {
                filters.Add("Content", content);
            }
            if (!string.IsNullOrEmpty(language))
            {
                filters.Add("Language", language);
            }

            var queryModel = new SearchViewModel.SearchQuery(q, fromDate, toDate, filters);
            if (Properties.Settings.Default.NewsMediaHostUri != null)
            {
                ViewBag.ProxyUrl = Properties.Settings.Default.NewsMediaHostUri.ToString() + "embed/";
            }
            else
            {
                ViewBag.ProxyUrl = new Uri("https://media.news.gov.bc.ca/embed/").ToString();
            }

            return View("SearchView", await Search(queryModel, page));
        }

        public new Task<ActionResult> NotFound()
        {
            return SearchNotFound();
        }

        [ResponseCache(CacheProfileName = "Default")]
        public async Task<ActionResult> Sitemap(int? count=null, int skip=0)
        {
            List<Uri> model = new List<Uri>();

            var defaultPostKeys = await Repository.ApiClient.Posts.GetAllKeysAsync("home", "default", "default", count, skip, Repository.APIVersion);
            
            foreach (var pair in defaultPostKeys)
            {
                model.Add(NewsroomExtensions.GetPostUri(pair.Value, pair.Key));
            }

            return View("SitemapView", model);
        }

        public async Task<ActionResult> CarouselImage(string slideId)
        {
            var slide = await Repository.GetSlideAsync(slideId);

            return GetCachedImage(slide?.Image, slide?.ImageType, slide?.Timestamp, null);
        }

        [ResponseCache(CacheProfileName = "Page")]
        public async Task<ActionResult> Privacy()
        {
            var model = await GetBaseModel();
            model.Title = "Privacy";
            return View("PrivacyView", model);
        }

        [ResponseCache(CacheProfileName = "Page")]
        public async Task<ActionResult> Live()
        {
            var model = await GetBaseModel();
            model.Title = "Live";
            return View("LiveView", model);
        }

        [ResponseCache(CacheProfileName = "Page")]
        public ActionResult SiteStatus(bool? showErrors)
        {
            List<string> model = new List<string>();

            model.Add(SiteStatusString("Subscribe API call: ", showErrors, () =>
            {
                IList<KeyValuePair2> tags = Repository.ApiClient.Subscribe.SubscriptionItemsAsync("tags", Repository.APIVersion).Result;
                return tags.Count() > 0 ? "OK" : "Failed";
            }));

            model.Add(SiteStatusString("Newsletters count:  ", showErrors, () =>
            {
                IEnumerable<Newsletter> newsletters = Repository.GetNewslettersAsync().Result;
                return newsletters.Count().ToString();
            }));

            Post post = null;
            model.Add(SiteStatusString("Hub DB access, post key: ", showErrors, () =>
            {
                post = Repository.GetPostAsync("2017FLNR0208-001391").Result;
                return post.Key;
            }));

            model.Add(SiteStatusString("Media proxy url: ", showErrors, () =>
            {
                var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Referrer = new Uri(string.Concat(Request.Scheme, "://", Request.Host.ToUriComponent(), Request.PathBase.ToUriComponent(), Request.Path, Request.QueryString));

                var result = client.GetAsync(post.AssetUrl).Result.ReasonPhrase;

                if (result != "OK") throw new Exception(result);
                return "OK";
            }));

            model.Add(SiteStatusString("Post cache size: ", showErrors, () =>
            {
                return Repository._cache[typeof(Post)].Count().ToString();
            }));

            return View("SiteStatus", model);
        }
        public static string SiteStatusString(string s, bool? showErrors, Func<string> func)
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

            string value;
            try
            {
                value = func();
                s = "OK: " + s;
            }
            catch (Exception ex)
            {
                if (showErrors == false) throw ex;
                Exception inner = ex.InnerException;
                if (inner == null) value = ex.Message;
                else value = (inner.InnerException ?? inner).Message;
            }

            timer.Stop();
            s += value + " (" + (timer.ElapsedMilliseconds) + " ms)";

            return s;
        }

        [ResponseCache(CacheProfileName = "Page")]
        public async Task<ActionResult> Contacts()
        {
            var model = await GetBaseModel();
            model.Title = "Media Contacts";
            return View("CommContacts", model);
        }

        [ResponseCache(CacheProfileName = "Page")]
        public ActionResult Contact()
        {
            return Redirect(Properties.Settings.Default.ContactUri.ToString());
        }

        public ActionResult Error()
        {
            var exception = HttpContext.Features.Get<IExceptionHandlerFeature>();

            ViewData["statusCode"] = HttpContext.Response.StatusCode;
            ViewData["message"] = exception.Error.Message;
            ViewData["stackTrace"] = exception.Error.StackTrace;

            return View("ErrorView");
        }

        public async Task<ConnectViewModel> GetConnectModel()
        {
            var model = new ConnectViewModel();
            await LoadAsync(model);

            // Social accounts come from published site settings (Gov.News.Admin), or the built-in defaults.
            var connect = (await _siteSettings.GetAsync()).Connect;
            model.FacebookLinks = ToLinks(connect.Facebook);
            model.YoutubeLinks = ToLinks(connect.Youtube);
            model.FlickrLinks = ToLinks(connect.Flickr);
            model.TwitterLinks = ToLinks(connect.X);
            model.InstagramLinks = ToLinks(connect.Instagram);
            model.BlueskyLinks = ToLinks(connect.Bluesky);
            model.ThreadsLinks = ToLinks(connect.Threads);
            model.BlogsLinks = ToLinks(connect.Blogs);

            var rssLinks = new List<Link>();
            var ministries = model.Ministries.Select(m => m.Index).OrderBy(c => c.Name == "Office of the Premier" ? 0 : 1).ThenBy(c => c.Name);
            var sectors = await Repository.GetSectorsAsync();
            var tags = await Repository.GetTagsAsync();
            var categories = ministries.Union(sectors).Union(tags).OrderBy(c => c.Name).ToList();
            foreach (var category in categories)
                rssLinks.Add(new Link() { Url = category.GetUri().ToString().TrimEnd('/') + "/feed", Title = category.Name });

            model.RssLinks = rssLinks
                .OrderBy(t => t.Title)
                .Prepend(new Link() { Url = "https://news.gov.bc.ca/factsheets/feed", Title = "Factsheets & Opinion Editorials" })
                .Prepend(new Link() { Url = "https://news.gov.bc.ca/feed", Title = "BC Gov News" }).ToArray();

            model.LinkedinLinks = new Link[]
            {
                new Link() { Url = "https://www.linkedin.com/company/bc-public-service/", Title = "BC Public Service", Summary = ""},
                new Link() { Url = "https://www.linkedin.com/company/official-workbc/", Title = "WorkBC ", Summary = ""},
                new Link() { Url = "https://www.linkedin.com/company/trade-invest-british-columbia/", Title = "Trade & Invest BC", Summary = "" },
                new Link() { Url = "https://www.linkedin.com/company/british-columbia-geological-survey/", Title = "British Columbia Geological Survey", Summary = "" },
            }.OrderBy(t => t.Title).Prepend(new Link() { Url = "https://www.linkedin.com/company/bcgov/", Title = "Government of BC", Summary = "" }).ToArray();

            return model;
        }

        public async Task<BaseViewModel> GetBaseModel()
        {
            var model = new BaseViewModel();
            await LoadAsync(model);
            return model;
        }

        public async Task<IList<DataIndex>> GetAllCategories()
        {
            var categoryModels = new List<DataIndex> { await Repository.GetHomeAsync() };
            categoryModels.AddRange(await Repository.GetMinistriesAsync());
            categoryModels.AddRange(await Repository.GetSectorsAsync());
            //categoryModels.AddRange(await Repository.GetThemesAsync());
            return categoryModels;
        }

        public async Task<SyndicationFeedViewModel> GetSyndicationFeedViewModel(string title, IEnumerable<string> postKeys)
        {
            var model = new SyndicationFeedViewModel();
            model.AlternateUri = new Uri(Configuration["NewsHostUri"]);

            model.Title = title;
            model.AlternateUri = null;
            model.Entries = (await Repository.GetPostsAsync(postKeys.Take(ProviderHelpers.MaximumSyndicationItems))).Where(e => e != null);

            return model;
        }
    }
}
