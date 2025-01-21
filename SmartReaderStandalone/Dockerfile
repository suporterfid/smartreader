#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.
# run the build:
# docker build --platform=linux/arm -t smartreader -f ./Dockerfile ../
# docker build --progress=plain --platform=linux/arm -t smartreader-upgx -f ./Dockerfile ../
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER app
WORKDIR /app
EXPOSE 8443

ARG BUILDPLATFORM=linux/arm

#FROM --platform=$BUILDPLATFORM  mcr.microsoft.com/dotnet/sdk:8.0 AS build
FROM --platform=$BUILDPLATFORM  mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH=arm
WORKDIR /src
COPY *.sln .
COPY plugin-contract/*.csproj ./plugin-contract/
COPY ConsoleAppHash/*.csproj ./ConsoleAppHash/
RUN dotnet restore plugin-contract/plugin-contract.csproj
COPY ["SmartReaderStandalone/SmartReaderStandalone.csproj", "SmartReaderStandalone/"]
COPY ["SmartReaderStandalone/Impinj.Atlas.dll", "SmartReaderStandalone/"]
COPY ["SmartReaderStandalone/ImpinjLogger.dll", "SmartReaderStandalone/"]
COPY ["SmartReaderStandalone/TagDataTranslation.dll", "SmartReaderStandalone/"]
COPY ["SmartReaderStandalone/localhost.pfx", "SmartReaderStandalone/"]
RUN dotnet restore 
# RUN dotnet restore "./././SmartReaderStandalone.csproj" --arch $TARGETARCH
COPY plugin-contract/. ./plugin-contract/
COPY ConsoleAppHash/. ./ConsoleAppHash/
COPY . .
WORKDIR "/src/."
RUN dotnet build "./SmartReaderStandalone/SmartReaderStandalone.csproj" -c $BUILD_CONFIGURATION -o /app/build --arch $TARGETARCH

## FROM  --platform=$BUILDPLATFORM build AS publish
#FROM build AS publish
#WORKDIR "/src/."
#ARG BUILD_CONFIGURATION=Release
#ARG TARGETARCH=arm
#RUN dotnet publish "./SmartReaderStandalone/SmartReaderStandalone.csproj" --no-restore -o /app -c $BUILD_CONFIGURATION /p:UseAppHost=false --arch $TARGETARCH --self-contained
RUN dotnet publish "./SmartReaderStandalone/SmartReaderStandalone.csproj" -o /app -c $BUILD_CONFIGURATION /p:UseAppHost=true --runtime linux-arm --self-contained
RUN ls -l /app
RUN dotnet publish "./SmartReaderStandalone/SmartReaderStandalone.csproj" -o /app64 /p:UseAppHost=true --runtime win-x64 --self-contained
RUN ls -l /app64
# final stage/image
#FROM mcr.microsoft.com/dotnet/runtime:8.0-jammy-arm32v7AS stage
#WORKDIR /app
#COPY --from=stage /app /app 
ENTRYPOINT ["dotnet", "SmartReaderStandalone.dll"]

#FROM base AS final
#WORKDIR /app
#COPY --from=publish /app/publish .
#ENTRYPOINT ["dotnet", "SmartReaderStandalone.dll"]

# Use a temporary container to copy published files to a volume
#FROM busybox AS export
#WORKDIR /app
#COPY --from=stage /app /app
#
## Define the volume where published files will be exported
#VOLUME /export
#
## Copy published files to the volume
#CMD cp -r /app/* /export/
#RUN echo "hello" > /export/hello.txt

#FROM --platform=linux/x86_64 ubuntu:20.04 as myupgx
#RUN apt-get update --fix-missing && export DEBIAN_FRONTEND=noninteractive
FROM --platform=linux/x86_64 ubuntu:20.04 as myupgx
RUN apt-get update --fix-missing && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install \
    build-essential \
    cmake \
    file \
    squashfs-tools \
    dos2unix \
    zip \
    unzip \
    locales && rm -rf /var/lib/apt/lists/* \
	&& localedef -i en_US -c -f UTF-8 -A /usr/share/locale/locale.alias en_US.UTF-8
ENV LANG en_US.utf8
#RUN apt-get update --fix-missing && export DEBIAN_FRONTEND=noninteractive
#RUN apt-get update --fix-missing && export DEBIAN_FRONTEND=noninteractive 
#RUN apt-get -y install build-essential 
#RUN apt-get -y install cmake
#RUN apt-get -y install file
#RUN apt-get -y install squashfs-tools
#RUN apt-get -y install dos2unix
#RUN apt-get -y install zip
#RUN apt-get -y install unzip
WORKDIR /etk
RUN chmod 777 /etk
#COPY etk_tools/8.1.0_Octane_Embedded_Development_Tools.tar.gz /tmp/
COPY --from=build /src/SmartReaderStandalone/etk_tools/8.1.0_Octane_Embedded_Development_Tools.tar.gz /tmp/
RUN tar -xvzf /tmp/8.1.0_Octane_Embedded_Development_Tools.tar.gz -C /tmp/
RUN rm -rf /tmp/8.1.0_Octane_Embedded_Development_Tools.tar.gz
#COPY bin/Release/net8.0/linux-arm/* /etk/cap/
COPY --from=build /app /etk/cap
## RUN [ -d /etk/cap/wwwroot ] || mkdir /etk/cap/wwwroot
#COPY bin/Release/net8.0/publish/cap_template/* /etk/cap/
COPY --from=build /app/cap_template/* /etk/cap
RUN rm -rf /etk/cap/SmartReaderStandalone.deps.json
#RUN rm -rf /etk/cap/SmartReaderStandalone.dll
#RUN rm -rf /etk/cap/SmartReaderStandalone.exe
#RUN rm -rf /etk/cap/SmartReaderStandalone.pdb
COPY --from=build /app/SmartReaderStandalone.deps.json /etk/cap
#COPY --from=build /app64/SmartReaderStandalone.dll /etk/cap
#COPY --from=build /app64/SmartReaderStandalone.exe /etk/cap
#COPY --from=build /app64/SmartReaderStandalone.pdb /etk/cap
## ---- copy binaries for ETK compatibility on cross compiling/packing ---- 
## COPY cap_template/* /etk/cap/ 
## RUN mv /etk/cap/cap_gen.sh /etk/cap_gen.sh
RUN ls -l /etk
RUN ls -l /etk/cap
RUN ls -l /etk/cap/build
RUN rm -rf /etk/cap/build
RUN ls -l /etk/cap/wwwroot
RUN mv /etk/cap/cap_gen.sh /tmp/8.1.0_Octane_Embedded_Development_Tools/
RUN mv /etk/cap/cap_description.in /etk/cap_description.in
RUN mv /etk/cap/capcommand.sh /etk/capcommand.sh 
## RUN mv /etk/cap/cap_template/* /etk/cap/ 

#RUN  [ -d /etk/cap/wwwroot ] && rm -rf /etk/cap/wwwroot 
#RUN mv /etk/cap/css /etk/cap/wwwroot/
#RUN mv /etk/cap/fonts /etk/cap/wwwroot/
#RUN mv /etk/cap/licenses /etk/cap/wwwroot/
#RUN mv /etk/cap/js /etk/cap/wwwroot/
#RUN mv /etk/cap/*.js /etk/cap/wwwroot/
#RUN mv /etk/cap/*.html /etk/cap/wwwroot/
#RUN mv /etk/cap/*.ico /etk/cap/wwwroot/
#RUN mkdir /etk/cap/wwwroot/css
#RUN mv /etk/cap/wwwroot/*.css /etk/cap/wwwroot/css/
#RUN mv /etk/cap/*.css /etk/cap/wwwroot/css/


RUN rm -rf /etk/cap/config/*
#COPY cap_template/config/smartreader-default.json /etk/cap/config/
COPY --from=build /src/SmartReaderStandalone/cap_template/config/smartreader-default.json /etk/cap/config/
RUN chmod 755 /etk/cap/start
RUN chmod 755 /etk/cap/SmartReaderStandalone*
RUN chmod 755 /etk/cap/*.so
RUN chmod 777 /etk/cap/config
RUN chmod 777 /etk/cap/config/*.json
#RUN chmod 755 /etk/cap/*.a
RUN chmod 755 /etk/cap/*.dll
RUN chmod 755 /etk/cap/*.json
RUN dos2unix /etk/cap/*.json
RUN dos2unix /etk/*.in
RUN dos2unix /etk/cap/cust_app_upgrade
RUN dos2unix /etk/cap/config/*.json
RUN chmod -R 775 /etk/cap/*
# RUN rm /etk/cap/smartreader-default.json
#RUN rm /etk/cap/*.js
#RUN rm /etk/cap/*.html
#RUN rm -rf /etk/cap/css
#RUN rm -rf /etk/cap/fonts
#RUN rm -rf /etk/cap/js
#RUN rm -rf /etk/cap/licenses
#RUN ls -lh /etk/cap
RUN /tmp/8.1.0_Octane_Embedded_Development_Tools/cap_gen.sh -d /etk/cap_description.in -o /etk/smartreader_cap.upgx
RUN chmod 777 /etk/smartreader_cap.upgx
RUN cp /etk/smartreader_cap.upgx /tmp/
#RUN rm -rf /tmp/etk/*
#RUN cp /etk/smartreader_cap.upgx /tmp/etk/

#COPY --from=cap_template /etk/smartreader_cap.upgx .

# Add user
#ARG UID
#RUN useradd -mu${UID} -s/bin/bash dev
#USER dev


