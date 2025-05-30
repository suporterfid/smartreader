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

# UPGX packaging stage - CORRECTED VERSION
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
    e2fsprogs \
    util-linux \
    && rm -rf /var/lib/apt/lists/* \
    && localedef -i en_US -c -f UTF-8 -A /usr/share/locale/locale.alias en_US.UTF-8

WORKDIR /etk
RUN mkdir -p /etk/cap/config

# Extract ETK tools completely and locate cap_gen.sh
COPY --from=build /src/SmartReaderStandalone/etk_tools/8.4.0_Octane_Embedded_Development_Tools.tar.gz /tmp/
RUN cd /tmp \
    && tar -xvzf 8.4.0_Octane_Embedded_Development_Tools.tar.gz \
    && echo "Listing extracted contents..." \
    && echo "=== Top level directories ===" \
    && find /tmp -maxdepth 2 -type d | sort \
    && echo "=== Looking for toolchain directories ===" \
    && find /tmp -type d -name "*toolchain*" -o -name "*arm*" | sort \
    && echo "=== Looking for cap_gen.sh ===" \
    && CAP_GEN_SCRIPT=$(find /tmp -name "cap_gen.sh" -type f | head -1) \
    && if [ -n "$CAP_GEN_SCRIPT" ]; then \
        echo "cap_gen.sh found at: $CAP_GEN_SCRIPT"; \
        chmod +x "$CAP_GEN_SCRIPT"; \
        echo "cap_gen.sh made executable"; \
    else \
        echo "ERROR: cap_gen.sh not found!"; \
        echo "Available .sh files:"; \
        find /tmp -name "*.sh" -type f; \
        exit 1; \
    fi \
    && echo "=== Verifying toolchain binaries ===" \
    && TOOLCHAIN_DIR=$(find /tmp -type d -name "*toolchain*" | head -1) \
    && if [ -n "$TOOLCHAIN_DIR" ]; then \
        echo "Toolchain directory found: $TOOLCHAIN_DIR"; \
        find "$TOOLCHAIN_DIR" -name "fallocate" -o -name "mkfs.ext4" -o -name "e2fsck" -o -name "rauc" 2>/dev/null || true; \
    else \
        echo "No toolchain directory found, searching for binaries anywhere..."; \
        find /tmp -name "fallocate" -o -name "mkfs.ext4" -o -name "e2fsck" -o -name "rauc" 2>/dev/null || true; \
    fi \
    && echo "=== Directory structure analysis ===" \
    && find /tmp -type d -name "*bin*" -o -name "*sbin*" | head -10 \
    && rm -f 8.4.0_Octane_Embedded_Development_Tools.tar.gz

# Copy application files
COPY --from=build /app/ /etk/cap/
COPY --from=build /app/cap_template/* /etk/cap/
COPY --from=build /src/SmartReaderStandalone/cap_template/config/smartreader-default.json /etk/cap/config/

# Set permissions
RUN chmod -R 777 /etk

# Move required files and copy arm-toolchain
RUN CAP_GEN_SCRIPT=$(find /tmp -name "cap_gen.sh" -type f | head -1) \
    && if [ -n "$CAP_GEN_SCRIPT" ]; then \
        echo "Using cap_gen.sh from: $CAP_GEN_SCRIPT"; \
        cp "$CAP_GEN_SCRIPT" /etk/cap_gen.sh; \
        chmod +x /etk/cap_gen.sh; \
    else \
        echo "ERROR: cap_gen.sh not found after extraction!"; \
        exit 1; \
    fi \
    && echo "=== Copying arm-toolchain ===" \
    && TOOLCHAIN_DIR=$(find /tmp -type d -name "*toolchain*" | head -1) \
    && if [ -n "$TOOLCHAIN_DIR" ]; then \
        echo "Copying toolchain from: $TOOLCHAIN_DIR"; \
        cp -r "$TOOLCHAIN_DIR" /etk/arm-toolchain; \
        echo "Toolchain copied successfully"; \
        echo "Verifying copied binaries:"; \
        find /etk/arm-toolchain -name "fallocate" -o -name "mkfs.ext4" -o -name "e2fsck" -o -name "rauc" 2>/dev/null || true; \
    else \
        echo "ERROR: arm-toolchain directory not found!"; \
        exit 1; \
    fi \
    && for file in cap_description.in capcommand.sh; do \
        if [ -f "/etk/cap/$file" ]; then \
            echo "Moving $file..."; \
            case $file in \
                cap_description.in) mv "/etk/cap/$file" /etk/ ;; \
                capcommand.sh) mv "/etk/cap/$file" /etk/ ;; \
            esac \
        else \
            echo "File $file not found in /etk/cap/"; \
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

# Final verification before UPGX generation
RUN echo "=== FINAL VERIFICATION ===" \
    && echo "cap_gen.sh at /etk/cap_gen.sh: $([ -f /etk/cap_gen.sh ] && echo "EXISTS" || echo "NOT EXISTS")" \
    && echo "cap_description.in at /etk/cap_description.in: $([ -f /etk/cap_description.in ] && echo "EXISTS" || echo "NOT EXISTS")" \
    && if [ -f /etk/cap_gen.sh ]; then \
        echo "cap_gen.sh permissions:"; \
        ls -la /etk/cap_gen.sh; \
    fi \
    && if [ -f /etk/cap_description.in ]; then \
        echo "cap_description.in content (first lines):"; \
        head -5 /etk/cap_description.in; \
    fi \
    && echo "=== Checking for required binaries in /etk/arm-toolchain ===" \
    && echo "Looking for fallocate..." \
    && find /etk/arm-toolchain -name "fallocate" -type f 2>/dev/null || echo "fallocate not found in /etk/arm-toolchain" \
    && echo "Looking for mkfs.ext4..." \
    && find /etk/arm-toolchain -name "mkfs.ext4" -type f 2>/dev/null || echo "mkfs.ext4 not found in /etk/arm-toolchain" \
    && echo "Looking for e2fsck..." \
    && find /etk/arm-toolchain -name "e2fsck" -type f 2>/dev/null || echo "e2fsck not found in /etk/arm-toolchain" \
    && echo "Looking for rauc..." \
    && find /etk/arm-toolchain -name "rauc" -type f 2>/dev/null || echo "rauc not found in /etk/arm-toolchain" \
    && echo "=== Creating missing links ===" \
    && mkdir -p /etk/arm-toolchain/sbin \
    && if [ ! -f /etk/arm-toolchain/sbin/mkfs.ext4 ] && which mkfs.ext4; then \
        echo "Creating symlink for mkfs.ext4"; \
        ln -s $(which mkfs.ext4) /etk/arm-toolchain/sbin/mkfs.ext4; \
    fi \
    && echo "=== Final binary check ===" \
    && ls -la /etk/arm-toolchain/bin/fallocate 2>/dev/null || echo "fallocate missing" \
    && ls -la /etk/arm-toolchain/sbin/mkfs.ext4 2>/dev/null || echo "mkfs.ext4 missing" \
    && ls -la /etk/arm-toolchain/sbin/e2fsck 2>/dev/null || echo "e2fsck missing" \
    && ls -la /etk/arm-toolchain/bin/rauc 2>/dev/null || echo "rauc missing"

# Generate UPGX package using the located cap_gen.sh
RUN if [ -f "/etk/cap_gen.sh" ] && [ -f "/etk/cap_description.in" ]; then \
        echo "Executing UPGX package generation..."; \
        /etk/cap_gen.sh -d /etk/cap_description.in -o /etk/smartreader_cap.upgx \
        && chmod 777 /etk/smartreader_cap.upgx \
        && cp /etk/smartreader_cap.upgx /tmp/ \
        && echo "UPGX package generated successfully!"; \
    else \
        echo "ERROR: Required files not found"; \
        echo "cap_gen.sh exists: $([ -f /etk/cap_gen.sh ] && echo "YES" || echo "NO")"; \
        echo "cap_description.in exists: $([ -f /etk/cap_description.in ] && echo "YES" || echo "NO")"; \
        echo "Contents of /tmp:"; \
        ls -la /tmp/ | head -10; \
        echo "Contents of /etk:"; \
        ls -la /etk/ | head -10; \
        exit 1; \
    fi