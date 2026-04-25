FROM mcr.microsoft.com/devcontainers/dotnet:1-10.0-bookworm

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

# Native libraries commonly needed by Avalonia/Skia on Linux.
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get install -y --no-install-recommends \
        libfontconfig1 \
        libice6 \
        libsm6 \
        libx11-6 \
        libxcursor1 \
        libxext6 \
        libxrender1 \
    && rm -rf /var/lib/apt/lists/*
