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

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "Agon.Api.dll"]
