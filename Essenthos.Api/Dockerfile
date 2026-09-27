FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Only the API and what it references. Essenthos.Forge is deliberately absent: the loaders, both
# SIL.Machine packages and every parser stay off the image a server runs, and the compiler enforces
# that the API never needed them.
COPY ["Essenthos.Api/Essenthos.Api.csproj", "Essenthos.Api/"]
COPY ["Essenthos.Corpus/Essenthos.Corpus.csproj", "Essenthos.Corpus/"]
COPY ["Essenthos.Accounts/Essenthos.Accounts.csproj", "Essenthos.Accounts/"]
RUN dotnet restore "Essenthos.Api/Essenthos.Api.csproj"
COPY Essenthos.Api/ Essenthos.Api/
COPY Essenthos.Corpus/ Essenthos.Corpus/
COPY Essenthos.Accounts/ Essenthos.Accounts/
WORKDIR "/src/Essenthos.Api"
RUN dotnet build "Essenthos.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# The commit this image is built from. The build context carries no .git, so the SDK cannot find it
# itself; given here, it ends up after the + of the informational version, which /v1/health/version
# reports so whoever deploys can tell what an environment runs.
ARG SOURCE_COMMIT=
RUN dotnet publish "Essenthos.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false ${SOURCE_COMMIT:+/p:SourceRevisionId=$SOURCE_COMMIT}

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Essenthos.Api.dll"]
