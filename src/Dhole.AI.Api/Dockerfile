# syntax=docker/dockerfile:1

# Base image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update && \
  apt-get install -y tzdata apt-utils libgdiplus libc6-dev && \
  ln -s /usr/lib/libgdiplus.so /usr/lib/gdiplus.dll && \
  rm -rf /var/lib/apt/lists/*

WORKDIR /app
EXPOSE 5206
EXPOSE 5307
ENV TZ=America/Costa_Rica

# Build image
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG GITHUB_ACTOR=github-actions
WORKDIR /src

COPY ["NuGet.Config", "./NuGet.Config"]
COPY ["src/Dhole.AI.Api/Dhole.AI.Api.csproj", "src/Dhole.AI.Api/"]
COPY ["src/Dhole.AI.Application/Dhole.AI.Application.csproj", "src/Dhole.AI.Application/"]
COPY ["src/Dhole.AI.Domain/Dhole.AI.Domain.csproj", "src/Dhole.AI.Domain/"]
COPY ["src/Dhole.AI.Persistence/Dhole.AI.Persistence.csproj", "src/Dhole.AI.Persistence/"]
COPY ["src/Dhole.AI.Infrastructure/Dhole.AI.Infrastructure.csproj", "src/Dhole.AI.Infrastructure/"]
COPY ["src/Dhole.AI.Contracts/Dhole.AI.Contracts.csproj", "src/Dhole.AI.Contracts/"]

RUN --mount=type=secret,id=github_token,required=true \
  export NuGetPackageSourceCredentials_github="Username=${GITHUB_ACTOR};Password=$(cat /run/secrets/github_token);ValidAuthenticationTypes=Basic" && \
  dotnet restore "src/Dhole.AI.Api/Dhole.AI.Api.csproj" --configfile /src/NuGet.Config

COPY . .

WORKDIR "/src/src/Dhole.AI.Api"
RUN dotnet build "Dhole.AI.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

# Publish image
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src/src/Dhole.AI.Api

RUN dotnet publish "Dhole.AI.Api.csproj" \
  -c $BUILD_CONFIGURATION \
  -o /app/publish \
  --no-restore \
  /p:UseAppHost=false

# Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN chmod 755 /app

ENTRYPOINT ["dotnet", "Dhole.AI.Api.dll"]
