#!/bin/bash

# Set up logging and output directories
LOG_DIR="build_logs"
OUTPUT_DIR="cap_deploy"
TIMESTAMP=$(date '+%Y%m%d_%H%M%S')
LOG_FILE="${LOG_DIR}/build_log_${TIMESTAMP}.txt"

# Create necessary directories
mkdir -p "${LOG_DIR}"
mkdir -p "${OUTPUT_DIR}"

# Logging function
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "${LOG_FILE}"
}

# Cleanup function
cleanup() {
    log "Cleaning up resources..."
    docker container rm -f smartreader-container >/dev/null 2>&1
}

# Error handling function
error_exit() {
    echo
    echo "Build failed! Check the log file for details:"
    echo "${LOG_FILE}"
    echo
    exit 1
}

# Initialize log file
log "Starting SmartReader build process..."

# Check Docker installation
log "Checking Docker installation..."
if ! docker --version >/dev/null 2>&1; then
    log "ERROR: Docker is not running or not installed."
    error_exit
fi
log "Docker is available and running."

# Clean up existing resources
log "Cleaning up Docker resources..."
log "Stopping any existing smartreader containers..."
docker ps -q --filter "name=smartreader-container" 2>/dev/null | grep -q . && docker stop smartreader-container
docker container rm -f smartreader-container >/dev/null 2>&1

# Remove existing images
log "Removing existing smartreader images..."
docker images smartreader-upgx -q 2>/dev/null | grep -q . && docker rmi -f smartreader-upgx

# Build Docker image
log "Building Docker image..."
if ! docker build --progress=plain --platform=linux/arm -t smartreader-upgx -f ./Dockerfile ../ >> "${LOG_FILE}" 2>&1; then
    log "ERROR: Docker build failed. Check the log file for details."
    error_exit
fi
log "Docker image built successfully."

# Create and run container
log "Creating container and extracting UPGX package..."
if ! docker run -d --name smartreader-container smartreader-upgx; then
    log "ERROR: Failed to create Docker container."
    error_exit
fi

# Wait for container to be ready
sleep 5

# Copy UPGX file from container
log "Copying UPGX package from container..."
if ! docker container cp smartreader-container:/etk/smartreader_cap.upgx "${OUTPUT_DIR}/"; then
    log "ERROR: Failed to copy UPGX package from container."
    cleanup
    error_exit
fi

# Verify UPGX file
if [ -f "${OUTPUT_DIR}/smartreader_cap.upgx" ]; then
    log "UPGX package successfully created and copied to ${OUTPUT_DIR}"
else
    log "ERROR: UPGX package not found in output directory."
    cleanup
    error_exit
fi

# Verify build environment
log "Verifying build environment..."
docker run --rm smartreader-upgx ldd --version >> "${LOG_FILE}" 2>&1

log "Checking GLIBC requirements of built binary..."
docker run --rm smartreader-upgx objdump -p /etk/cap/SmartReaderStandalone | grep "GLIBC" >> "${LOG_FILE}" 2>&1

# Verify binary format
log "Verifying binary format..."
docker run --rm smartreader-upgx file /etk/cap/SmartReaderStandalone >> "${LOG_FILE}" 2>&1

# Clean up
cleanup

# Success message
log "Build completed successfully!"
echo
echo "Build completed successfully! Check the log file for details:"
echo "${LOG_FILE}"
echo
echo "UPGX package location:"
echo "${OUTPUT_DIR}/smartreader_cap.upgx"
echo