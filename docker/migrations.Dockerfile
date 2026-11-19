# Builds an EF Core migration bundle and runs it once against NUNAM_MIGRATION_CONNECTION.
# Build context is the repo root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
RUN dotnet tool install --global dotnet-ef --version 10.0.8
ENV PATH="$PATH:/root/.dotnet/tools"
COPY ["Nunam.Core/Nunam.Core.csproj", "Nunam.Core/"]
RUN dotnet restore "Nunam.Core/Nunam.Core.csproj"
COPY Nunam.Core/ Nunam.Core/
RUN dotnet ef migrations bundle --project Nunam.Core --startup-project Nunam.Core \
      --self-contained -r linux-x64 -o /out/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
WORKDIR /app
COPY --from=build /out/efbundle .
USER $APP_UID
ENTRYPOINT ["/bin/sh", "-c", "exec ./efbundle --connection \"$NUNAM_MIGRATION_CONNECTION\""]
