FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Essenthos.Core/Essenthos.Core.csproj", "Essenthos.Core/"]
RUN dotnet restore "Essenthos.Core/Essenthos.Core.csproj"
COPY . .
WORKDIR "/src/Essenthos.Core"
RUN dotnet build "Essenthos.Core.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Essenthos.Core.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Essenthos.Core.dll"]
