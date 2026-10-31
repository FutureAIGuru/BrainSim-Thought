# Build only the web service and its UKS dependency, not the desktop solution.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY UKS/UKS.csproj UKS/
COPY UKSWeb/UKSWeb.csproj UKSWeb/
RUN dotnet restore UKSWeb/UKSWeb.csproj
COPY UKS/ UKS/
COPY UKSWeb/ UKSWeb/
RUN dotnet publish UKSWeb/UKSWeb.csproj -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /publish .
USER app
EXPOSE 8080
# Hosting providers can supply PORT; local containers default to 8080.
ENTRYPOINT ["sh", "-c", "exec dotnet UKSWeb.dll --urls \"http://0.0.0.0:${PORT:-8080}\""]
