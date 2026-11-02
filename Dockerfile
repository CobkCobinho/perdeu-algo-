FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PerdeuAlgo/PerdeuAlgo.csproj src/PerdeuAlgo/
RUN dotnet restore src/PerdeuAlgo/PerdeuAlgo.csproj
COPY src/PerdeuAlgo/ src/PerdeuAlgo/
RUN dotnet publish src/PerdeuAlgo/PerdeuAlgo.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PerdeuAlgo.dll"]
