#!/usr/bin/env bash
# Runs one command with the Groq API key from the macOS Keychain, so the key is never kept in a
# plain file. Store it once with:
#   security add-generic-password -a "$USER" -s chatapp-groq-key -w
# Then, for example:
#   scripts/with-groq.sh dotnet run --project backend/src/Chat.Api
#   scripts/with-groq.sh docker compose --profile app up --build
set -euo pipefail

service=chatapp-groq-key

if [ "$#" -eq 0 ]; then
  echo "Usage: $0 <command> [args...]" >&2
  exit 2
fi

# CHATAPP_KEYCHAIN names another keychain file; without it the login keychain is searched.
if ! key=$(security find-generic-password -s "$service" -w ${CHATAPP_KEYCHAIN:+"$CHATAPP_KEYCHAIN"} 2>/dev/null); then
  echo "No Groq key in the Keychain. Store one with:" >&2
  echo "  security add-generic-password -a \"\$USER\" -s $service -w" >&2
  exit 1
fi

# The app reads the first name; docker-compose.yml passes the second into the container.
export Ai__Models__groq__ApiKey="$key"
export GROQ_API_KEY="$key"
exec "$@"
