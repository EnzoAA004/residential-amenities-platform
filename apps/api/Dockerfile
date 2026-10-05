FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY apps/api/ResidentialAmenities.Api.csproj apps/api/
RUN dotnet restore apps/api/ResidentialAmenities.Api.csproj

COPY apps/api/ apps/api/
RUN dotnet publish apps/api/ResidentialAmenities.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

# Official .NET Linux images define APP_UID for the built-in non-root app user.
USER $APP_UID

ENTRYPOINT ["dotnet", "ResidentialAmenities.Api.dll"]
