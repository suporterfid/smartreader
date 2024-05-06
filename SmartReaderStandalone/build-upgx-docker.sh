#!/bin/bash

# Check if Docker is running
docker --version >/dev/null 2>&1
if [ $? -ne 0 ]; then
    echo "Error: Docker is not running or not installed."
    read -p "Press enter to exit"
    exit 1
fi

# Clearing Docker system
docker system prune -a -f
if [ $? -ne 0 ]; then
    echo "Error: Failed to prune Docker system."
    read -p "Press enter to exit"
    exit 1
fi

# Building Docker image
docker build --progress=plain --platform=linux/arm -t smartreader-upgx -f ./Dockerfile ../
if [ $? -ne 0 ]; then
    echo "Error: Failed to build Docker image."
    read -p "Press enter to exit"
    exit 1
fi

# Running Docker container
docker run -d --name smartreader-container smartreader-upgx 
if [ $? -ne 0 ]; then
    echo "Error: Failed to run Docker container."
    read -p "Press enter to exit"
    exit 1
fi

# Copying file from Docker container
docker container cp smartreader-container:/etk/smartreader_cap.upgx cap_deploy/
if [ $? -ne 0 ]; then
    echo "Error: Failed to copy file from Docker container."
    read -p "Press enter to exit"
    exit 1
fi

# Removing Docker container
docker container rm -f smartreader-container
if [ $? -ne 0 ]; then
    echo "Error: Failed to remove Docker container."
    read -p "Press enter to exit"
    exit 1
fi

read -p "Press enter to exit"
