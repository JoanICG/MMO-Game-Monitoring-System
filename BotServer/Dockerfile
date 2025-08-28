# Bot Server Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
ENV DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION=1
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8
WORKDIR /app
EXPOSE 8082

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY BotServer/src/*.csproj ./BotServer/
COPY Shared/*.csproj ./Shared/
RUN dotnet restore ./BotServer/ --disable-parallel

# Copy everything else and publish
COPY BotServer/src/ ./BotServer/
COPY Shared/ ./Shared/
RUN dotnet publish ./BotServer/ -c Release -o /out

FROM base AS final
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "BotServer.dll"]
