FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/Multiplay.Server/Multiplay.Server.csproj", "src/Multiplay.Server/"]
COPY ["src/Multiplay.Shared/Multiplay.Shared.csproj",  "src/Multiplay.Shared/"]
RUN dotnet restore "src/Multiplay.Server/Multiplay.Server.csproj"
COPY . .
RUN dotnet publish "src/Multiplay.Server/Multiplay.Server.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Multiplay.Server.dll"]
