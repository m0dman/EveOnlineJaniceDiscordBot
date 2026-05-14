#!/bin/bash
cd "$(dirname "$0")"
echo "Starting EVE Online Janice Bot..."

# Prefer running published binary if it exists
if [ -f "./EveOnlineBot" ]; then
    exec ./EveOnlineBot
fi
if [ -f "./bin/Release/net8.0/linux-x64/publish/EveOnlineBot" ]; then
    exec ./bin/Release/net8.0/linux-x64/publish/EveOnlineBot
fi

# Otherwise run from source
exec dotnet run
