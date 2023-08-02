#!/bin/bash

# Function to load the configuration from config.sh
load_config() {
  source config.sh
}

load_upgrade() {
  source upgrade_config.sh
}

# Initial load of the configuration
load_config

# Loop
while true; do
    # Make use of the variables from the configuration
    echo "URL: $url_upgrade"
    echo "Username: $username"
    echo "Password: $password"

    # Some operations inside the loop

    # Reload the configuration at the end of each loop iteration
    load_config

    # Make the HTTP GET request using curl with SSL certificate validation and basic authentication
    response=$(curl -s -k -u "$username:$password" "$url_upgrade")

    # Use jq to extract the value of the "status" property
    status=$(echo "$response" | jq -r '.status')

    # Check the value of the "status" property and print the result
    if [ "$status" = "successful" ]; then
    echo "upgrade succeeded."
    #response=$(curl -s -k -u "$username:$password" "$url_reboot")
    curl -X POST -H "Content-Type: application/json" -d '{}' -k -u "$username:$password" "$url_reboot"
    elif [ "$status" = "verifying" ]; then
    echo "verifying upgrade."    
    elif [ "$status" = "installing" ]; then
    echo "installing upgrade."
    elif [ "$status" = "failed" ]; then
    echo "upgrade failed."
    else
    echo "Unknown status: $status"
    fi

    # Sleep for a while before the next iteration
    sleep 5
done
