FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Agon.Api/Agon.Api.csproj src/Agon.Api/
COPY src/Agon.Application/Agon.Application.csproj src/Agon.Application/
COPY src/Agon.Domain/Agon.Domain.csproj src/Agon.Domain/
COPY src/Agon.Infrastructure/Agon.Infrastructure.csproj src/Agon.Infrastructure/

RUN dotnet restore src/Agon.Api/Agon.Api.csproj

COPY src ./src

RUN dotnet publish src/Agon.Api/Agon.Api.csproj \
  --configuration Release \
  --output /app/publish \
  /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Create a dedicated non-root user and group for the application process.
# Running as a non-root user limits the blast radius of any container escape
# or application-level vulnerability.
RUN groupadd --gid 1001 appgroup \
    && useradd --uid 1001 --gid appgroup --no-create-home --shell /bin/false appuser

COPY --from=build /app/publish .

# Transfer ownership of all published files to the non-root user before
# switching context, so the process can read its own binaries.
RUN chown -R appuser:appgroup /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

USER appuser

ENTRYPOINT ["dotnet", "Agon.Api.dll"]
