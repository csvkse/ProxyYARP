FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source

# Install AOT native toolchain
RUN apt-get update && apt-get install -y clang zlib1g-dev && rm -rf /var/lib/apt/lists/*

COPY . .
WORKDIR /source/src/ProxyYARP
RUN dotnet publish -c Release -r linux-x64 -o /app

FROM debian:bookworm-slim
WORKDIR /app
RUN mkdir -p /app/data
VOLUME /app/data
ENV DB_PATH=/app/data/proxy.db
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["./ProxyYARP"]
