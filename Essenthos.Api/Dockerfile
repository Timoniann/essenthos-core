FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
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
RUN dotnet publish "Essenthos.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Essenthos.Api.dll"]
