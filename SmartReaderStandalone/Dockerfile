#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["SmartReaderStandalone.csproj", "SmartReaderStandalone/"]
COPY ["Impinj.Atlas.dll", "SmartReaderStandalone/"]
COPY ["ImpinjLogger.dll", "SmartReaderStandalone/"]
COPY ["TagDataTranslation.dll", "SmartReaderStandalone/"]
COPY ["localhost.pfx", "SmartReaderStandalone/"]
RUN dotnet restore "SmartReaderStandalone/SmartReaderStandalone.csproj"
COPY . .
WORKDIR "/src/SmartReaderStandalone"
RUN dotnet build "SmartReaderStandalone.csproj" -c Release -o /app/build

FROM build AS publish
#RUN dotnet publish "SmartReaderStandalone.csproj" -c Release -o /app/publish --self-contained false --no-restore
#RUN dotnet publish "SmartReaderStandalone.csproj" -c Release -o /app/publish -r linux-arm --self-contained false --no-restore
RUN dotnet publish "SmartReaderStandalone.csproj" /p:PublishProfile=SmartReaderStandalone/Properties/PublishProfiles/FolderProfile.pubxml

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SmartReaderStandalone.dll"]