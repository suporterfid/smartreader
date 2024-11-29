#!/bin/bash

# Define the target directory
TARGET_DIR="/customer"

# Define the source directory (current project path)
SOURCE_DIR="$(pwd)"

# List of specific files to copy
FILES_TO_COPY=(
    "localhost.pfx"
    "appsettings.json"
    "customsettings.json"
    # "subdir\file4.txt"
    # Add more files as needed
)

# List of directories to create
DIRECTORIES_TO_CREATE=(
    "$TARGET_DIR/config"
    "$TARGET_DIR/plugins"
    "$TARGET_DIR/wwwroot"
    "$TARGET_DIR/wwwroot/logs"
)

# Create the target directories if they don't exist
for dir in "${DIRECTORIES_TO_CREATE[@]}"; do
    if [ ! -d "$dir" ]; then
        echo "Creating directory $dir"
        sudo mkdir -p "$dir"
    else
        echo "Directory $dir already exists"
    fi
done

# Copy specific files from the source directory to the target directory
for file in "${FILES_TO_COPY[@]}"; do
    if [ -f "$SOURCE_DIR/$file" ]; then
        echo "Copying $file to $TARGET_DIR"
        sudo cp "$SOURCE_DIR/$file" "$TARGET_DIR/"
    elif [ -d "$SOURCE_DIR/$file" ]; then
        echo "Copying directory $file to $TARGET_DIR"
        sudo cp -r "$SOURCE_DIR/$file" "$TARGET_DIR/"
    else
        echo "Warning: $file does not exist in $SOURCE_DIR"
    fi
done

# Copy the smartreader-default.json file to /customer/config as smartreader.json
SOURCE_CONFIG_FILE="$SOURCE_DIR/cap_template/config/smartreader-default.json"
TARGET_CONFIG_FILE="$TARGET_DIR/config/smartreader.json"

if [ -f "$SOURCE_CONFIG_FILE" ]; then
    echo "Copying $SOURCE_CONFIG_FILE to $TARGET_CONFIG_FILE"
    sudo cp "$SOURCE_CONFIG_FILE" "$TARGET_CONFIG_FILE"
else
    echo "Warning: $SOURCE_CONFIG_FILE does not exist"
fi

echo "Setup completed successfully"
