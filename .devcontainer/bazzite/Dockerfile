# Dev container image for PseudoCode on Bazzite/Flatpak hosts.

FROM mcr.microsoft.com/devcontainers/dotnet:1-10.0-jammy

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_EnableDiagnostics=0

RUN curl -fsSL https://dl.yarnpkg.com/debian/pubkey.gpg \
        | gpg --batch --yes --dearmor -o /usr/share/keyrings/yarn-archive-keyring.gpg \
    && apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get install -y --no-install-recommends \
        build-essential \
        cups-client \
        htop \
        libfontconfig1 \
        libice6 \
        libsm6 \
        libx11-6 \
        libxcursor1 \
        libxext6 \
        libxrender1 \
        nano \
        tmux \
        vim \
        xauth \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspaces

RUN chown -R vscode:vscode /workspaces && \
    mkdir -p /home/vscode/.vscode-server && \
    chown -R vscode:vscode /home/vscode

USER vscode

ENTRYPOINT ["/bin/bash"]
CMD ["sleep", "infinity"]
