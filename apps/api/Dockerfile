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

RUN addgroup --system appgroup \
    && adduser --system --ingroup appgroup --home /app appuser

COPY --from=build --chown=appuser:appgroup /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080
USER appuser

ENTRYPOINT ["dotnet", "ResidentialAmenities.Api.dll"]
