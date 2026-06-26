FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and all project files first (for layer caching)
COPY Conquer.sln .
COPY Redux/Redux.csproj Redux/
COPY Crypto/Crypto.csproj Crypto/
COPY Maps/Maps.csproj Maps/
COPY Database/Database.csproj Database/
COPY Network/Network.csproj Network/
COPY Packets/Packets.csproj Packets/
RUN dotnet restore Conquer.sln

# Copy all source
COPY . .
RUN dotnet publish Redux/Redux.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Redux.dll"]
