# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/CustomerSupportCrm.Api/*.csproj src/CustomerSupportCrm.Api/
COPY src/CustomerSupportCrm.Application/*.csproj src/CustomerSupportCrm.Application/
COPY src/CustomerSupportCrm.Domain/*.csproj src/CustomerSupportCrm.Domain/
COPY src/CustomerSupportCrm.Infrastructure/*.csproj src/CustomerSupportCrm.Infrastructure/
COPY src/CustomerSupportCrm.Contracts/*.csproj src/CustomerSupportCrm.Contracts/
RUN dotnet restore src/CustomerSupportCrm.Api/CustomerSupportCrm.Api.csproj
COPY src/ src/
RUN dotnet publish src/CustomerSupportCrm.Api/CustomerSupportCrm.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
USER $APP_UID
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CustomerSupportCrm.Api.dll"]
