# Game Server Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
ENV DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION=1
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8
WORKDIR /app
EXPOSE 8080
EXPOSE 8081/udp

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY GameServer/src/*.csproj ./GameServer/
COPY Shared/*.csproj ./Shared/
RUN dotnet restore ./GameServer/ --disable-parallel

# Copy everything else and publish
COPY GameServer/src/ ./GameServer/
COPY Shared/ ./Shared/
RUN dotnet publish ./GameServer/ -c Release -o /out

FROM base AS final
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "GameServer.dll"]
