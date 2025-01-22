# Build stage with Ubuntu 20.04 (GLIBC 2.31)
FROM --platform=$BUILDPLATFORM ubuntu:20.04 AS build

ENV DOTNET_RUNTIME_IDENTIFIER=linux-arm
ENV DOTNET_TARGET_GLIBC_VERSION=2.31
ENV DOTNET_DISABLE_NEWER_GLIBC=1
ENV DEBIAN_FRONTEND=noninteractive
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH=arm
ARG DOTNET_SDK_VERSION=8.0.101

# Add repository for old glibc packages
# RUN echo "deb http://security.ubuntu.com/ubuntu focal-security main" > /etc/apt/sources.list.d/focal-security.list

# Install build dependencies and .NET SDK
RUN apt-get update && apt-get install -y \
    wget \
    curl \
    git \
    build-essential \
    file \
    tree \
    ca-certificates \
    libicu66 \  
    && rm -rf /var/lib/apt/lists/* \
    && wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh --version $DOTNET_SDK_VERSION --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && rm dotnet-install.sh

# Verify installations
RUN dotnet --info && ldd --version

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV PATH="/usr/share/dotnet:${PATH}"

WORKDIR /src

# Copy project files
COPY *.sln .
COPY plugin-contract/*.csproj ./plugin-contract/
COPY ConsoleAppHash/*.csproj ./ConsoleAppHash/
COPY ["SmartReaderStandalone/SmartReaderStandalone.csproj", "SmartReaderStandalone/"]
COPY ["SmartReaderStandalone/Impinj.Atlas.dll", "SmartReaderStandalone/"]
COPY ["SmartReaderStandalone/ImpinjLogger.dll", "SmartReaderStandalone/"]
COPY ["SmartReaderStandalone/TagDataTranslation.dll", "SmartReaderStandalone/"]
COPY ["SmartReaderStandalone/localhost.pfx", "SmartReaderStandalone/"]

# Restore projects
RUN dotnet restore plugin-contract/plugin-contract.csproj
RUN dotnet restore "SmartReaderStandalone/SmartReaderStandalone.csproj" \
    --runtime linux-arm \
    /p:RuntimeIdentifiers=linux-arm

# Copy source code
COPY plugin-contract/. ./plugin-contract/
COPY ConsoleAppHash/. ./ConsoleAppHash/
COPY . .
WORKDIR "/src/."

# Create build directory and publish
#RUN mkdir -p /build/arm \
    #&& dotnet publish "./SmartReaderStandalone/SmartReaderStandalone.csproj" \
    #-o /build/arm \
    #-c $BUILD_CONFIGURATION \
    #--runtime linux-arm \
    #--self-contained true \
    #/p:Platform=ARM \
    #/p:RuntimeIdentifiers=linux-arm \
    #/p:UseAppHost=true \
    #/p:CrossGenDuringPublish=false \
    #/p:UseRootCurrentVersion=false \
    #/p:StripSymbols=false \
    #/p:InvariantGlobalization=true \
    #/p:RuntimeIdentifier=linux-arm \
    #/p:IncludeNativeLibrariesForSelfExtract=false \
    #/p:CopyOutputSymbolsToPublishDirectory=true \
    #/p:PreserveCompilationContext=true \
    #/p:DebugType=portable \
    #/p:EnableCompressionInSingleFile=false \
    #/p:CopyLocalLockFileAssemblies=true \
    #/p:DebugSymbols=true

# Create build directory and publish
RUN mkdir -p /build/arm \
    && dotnet publish "./SmartReaderStandalone/SmartReaderStandalone.csproj" \
    -o /build/arm \
    -c $BUILD_CONFIGURATION \
    --runtime linux-arm \
    --self-contained true \
    /p:Platform=ARM \
    /p:RuntimeIdentifiers=linux-arm \
    /p:UseAppHost=true \
    /p:CrossGenDuringPublish=false \
    /p:UseRootCurrentVersion=true \  
    /p:StripSymbols=false \
    /p:InvariantGlobalization=true \
    /p:RuntimeIdentifier=linux-arm \
    /p:IncludeNativeLibrariesForSelfExtract=true \ 
    /p:CopyOutputSymbolsToPublishDirectory=true \
    /p:PreserveCompilationContext=true \
    /p:DebugType=portable \
    /p:EnableCompressionInSingleFile=false \
    /p:CopyLocalLockFileAssemblies=true \
    /p:DebugSymbols=true \
    /p:UseSystemLibraries=true \ 
    /p:UseLegacyGlibcBehavior=true  

# Verify build
RUN echo "Verifying ARM binary:" \
    && file /build/arm/SmartReaderStandalone \
    && echo "GLIBC requirements:" \
    && objdump -p /build/arm/SmartReaderStandalone | grep GLIBC

# Create app directory
RUN mkdir -p /app && cp -r /build/arm/* /app/

# Build Windows version
RUN dotnet publish "./SmartReaderStandalone/SmartReaderStandalone.csproj" \
    -o /app64 \
    --runtime win-x64 \
    --self-contained true \
    /p:UseAppHost=true

# UPGX packaging stage
FROM --platform=linux/x86_64 ubuntu:20.04 AS myupgx

ENV DEBIAN_FRONTEND=noninteractive
ENV LANG=en_US.utf8

# Install required packages
RUN apt-get update && apt-get install -y \
    build-essential \
    cmake \
    file \
    squashfs-tools \
    dos2unix \
    zip \
    unzip \
    locales \
    tree \
    && rm -rf /var/lib/apt/lists/* \
    && localedef -i en_US -c -f UTF-8 -A /usr/share/locale/locale.alias en_US.UTF-8

WORKDIR /etk
RUN mkdir -p /etk/cap/config

# Extract ETK tools
COPY --from=build /src/SmartReaderStandalone/etk_tools/8.4.0_Octane_Embedded_Development_Tools.tar.gz /tmp/
RUN cd /tmp \
    && tar -xvzf 8.4.0_Octane_Embedded_Development_Tools.tar.gz \
    && rm -f 8.4.0_Octane_Embedded_Development_Tools.tar.gz

# Copy application files
COPY --from=build /app/ /etk/cap/
COPY --from=build /app/cap_template/* /etk/cap/
COPY --from=build /src/SmartReaderStandalone/cap_template/config/smartreader-default.json /etk/cap/config/

# Set permissions
RUN chmod -R 777 /etk

# Move required files
RUN for file in cap_gen.sh cap_description.in capcommand.sh; do \
    if [ -f "/etk/cap/$file" ]; then \
        case $file in \
            cap_gen.sh) mv "/etk/cap/$file" /tmp/8.4.0_Octane_Embedded_Development_Tools/ ;; \
            cap_description.in) mv "/etk/cap/$file" /etk/ ;; \
            capcommand.sh) mv "/etk/cap/$file" /etk/ ;; \
        esac \
    fi \
done

# Set file permissions and convert line endings
RUN chmod 755 /etk/cap/start \
    && chmod 755 /etk/cap/SmartReaderStandalone* \
    && chmod 777 /etk/cap/config \
    && chmod 777 /etk/cap/config/*.json \
    && chmod 755 /etk/cap/*.json \
    && find /etk -type f -name "*.json" -exec dos2unix {} \; \
    && find /etk -type f -name "*.in" -exec dos2unix {} \; \
    && dos2unix /etk/cap/cust_app_upgrade

# Generate UPGX package
RUN if [ -f "/tmp/8.4.0_Octane_Embedded_Development_Tools/cap_gen.sh" ] && [ -f "/etk/cap_description.in" ]; then \
        /tmp/8.4.0_Octane_Embedded_Development_Tools/cap_gen.sh -d /etk/cap_description.in -o /etk/smartreader_cap.upgx \
        && chmod 777 /etk/smartreader_cap.upgx \
        && cp /etk/smartreader_cap.upgx /tmp/; \
    else \
        echo "ERROR: Missing required files" \
        && echo "cap_gen.sh exists: $([[ -f /tmp/8.4.0_Octane_Embedded_Development_Tools/cap_gen.sh ]] && echo "yes" || echo "no")" \
        && echo "cap_description.in exists: $([[ -f /etk/cap_description.in ]] && echo "yes" || echo "no")" \
        && exit 1; \
    fi