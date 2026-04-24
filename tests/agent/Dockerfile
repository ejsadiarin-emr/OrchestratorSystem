# Pre-build step: host runs `dotnet publish -c Release -r linux-x64` first
FROM mcr.microsoft.com/dotnet/runtime-deps:9.0

WORKDIR /app
COPY ./publish/linux-x64/ /app/

ENTRYPOINT ["./DeploymentPoC.Agent"]
