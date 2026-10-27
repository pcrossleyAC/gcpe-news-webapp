FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Gov.News.WebApp/Gov.News.WebApp.csproj Gov.News.WebApp/
COPY Gov.News.Settings/Gov.News.Settings.csproj Gov.News.Settings/
RUN dotnet restore Gov.News.WebApp/Gov.News.WebApp.csproj

COPY . .
RUN dotnet publish Gov.News.WebApp/Gov.News.WebApp.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Same guard as OpenShift/images/dotnet-50-bctz: B.C. stays on UTC-7 after
# November 2026, so the image must carry tzdata new enough to know that.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/* \
    && TZ=America/Vancouver date -d '2026-11-02 12:00:00 UTC' '+%F %T %z' \
    && test "$(TZ=America/Vancouver date -d '2026-11-02 12:00:00 UTC' '+%z')" = "-0700"

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "Gov.News.WebApp.dll"]
