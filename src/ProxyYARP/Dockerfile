FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Install AOT native toolchain
RUN apt-get update && apt-get install -y clang zlib1g-dev && rm -rf /var/lib/apt/lists/*

COPY . .
WORKDIR /source/src/ProxyYARP
ARG APP_VERSION
RUN if [ -n "$APP_VERSION" ]; then \
      dotnet publish -c Release -r linux-x64 -o /app -p:Version=$APP_VERSION; \
    else \
      dotnet publish -c Release -r linux-x64 -o /app; \
    fi

# runtime-deps:10.0 与 sdk:10.0 同为 Ubuntu 24.04 (glibc 2.39)，保证 NativeAOT 二进制 glibc 版本匹配
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
WORKDIR /app
RUN mkdir -p /app/data
VOLUME /app/data
ENV DB_PATH=/app/data/proxy.db
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["./ProxyYARP"]
