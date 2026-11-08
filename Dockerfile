FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY [".editorconfig", "./"]
COPY ["Analyzer/DGA.Analyzer/DGA.Analyzer.csproj", "Analyzer/DGA.Analyzer/"]
COPY ["DiscordGeminiAdmin/DiscordGeminiAdmin.csproj", "DiscordGeminiAdmin/"]
RUN dotnet restore "DiscordGeminiAdmin/DiscordGeminiAdmin.csproj"
COPY ["Analyzer/DGA.Analyzer/", "Analyzer/DGA.Analyzer/"]
COPY ["DiscordGeminiAdmin/", "DiscordGeminiAdmin/"]
WORKDIR "/src/DiscordGeminiAdmin"
RUN dotnet publish "./DiscordGeminiAdmin.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DiscordGeminiAdmin.dll"]
