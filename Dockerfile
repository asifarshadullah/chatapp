# One image serves the built UI and the API from the same origin.

FROM node:20.20.2-bookworm-slim AS ui
WORKDIR /src/frontend/chat-ui
COPY frontend/chat-ui/package.json frontend/chat-ui/package-lock.json ./
RUN npm ci
COPY frontend/chat-ui/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0.426 AS api
WORKDIR /src
# Project files first, so the restore layer is reused until a dependency changes.
COPY global.json ./
COPY backend/Directory.Build.props backend/
COPY backend/src/Chat.Api/Chat.Api.csproj backend/src/Chat.Api/
COPY backend/src/Chat.Application/Chat.Application.csproj backend/src/Chat.Application/
COPY backend/src/Chat.Domain/Chat.Domain.csproj backend/src/Chat.Domain/
COPY backend/src/Chat.Infrastructure/Chat.Infrastructure.csproj backend/src/Chat.Infrastructure/
COPY backend/src/Chat.Identity.Application/Chat.Identity.Application.csproj backend/src/Chat.Identity.Application/
COPY backend/src/Chat.Identity.Domain/Chat.Identity.Domain.csproj backend/src/Chat.Identity.Domain/
COPY backend/src/Chat.Identity.Infrastructure/Chat.Identity.Infrastructure.csproj backend/src/Chat.Identity.Infrastructure/
COPY backend/src/Chat.Billing.Application/Chat.Billing.Application.csproj backend/src/Chat.Billing.Application/
COPY backend/src/Chat.Billing.Domain/Chat.Billing.Domain.csproj backend/src/Chat.Billing.Domain/
COPY backend/src/Chat.Billing.Infrastructure/Chat.Billing.Infrastructure.csproj backend/src/Chat.Billing.Infrastructure/
RUN dotnet restore backend/src/Chat.Api/Chat.Api.csproj
COPY backend/src/ backend/src/
RUN dotnet publish backend/src/Chat.Api/Chat.Api.csproj --no-restore -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0.32
LABEL org.opencontainers.image.source=https://github.com/asifarshadullah/chatapp
WORKDIR /app
COPY --from=api /app ./
COPY --from=ui /src/frontend/chat-ui/dist ./wwwroot

# Only ever reached through the host's TLS proxy or on a developer's own machine.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
ARG APP_COMMIT
ENV APP_COMMIT=$APP_COMMIT

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Chat.Api.dll"]
