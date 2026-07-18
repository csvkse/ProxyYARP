FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Install AOT native toolchain
RUN apt-get update && apt-get install -y clang zlib1g-dev && rm -rf /var/lib/apt/lists/*

COPY . .
WORKDIR /source/src/ProxyYARP
# 不写死 -r：publish 自动取构建机 RID，arm runner 产出 linux-arm64，x64 runner 产出 linux-x64
ARG APP_VERSION
RUN if [ -n "$APP_VERSION" ]; then \
      dotnet publish -c Release -o /app -p:Version=$APP_VERSION; \
    else \
      dotnet publish -c Release -o /app; \
    fi \
    && mkdir -p /app/data

# chiseled-extra：精简 Ubuntu 24.04（无 apt/shell 冗余，含 busybox/certs/tzdata），glibc 与 sdk:10.0 匹配
# 注意：chiseled 无 shell，最终阶段不可有 RUN 指令，/app/data 在 build 阶段创建后随 COPY 带入
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra
WORKDIR /app
VOLUME /app/data
ENV DB_PATH=/app/data/proxy.db
COPY --from=build --chown=$APP_UID /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["./ProxyYARP"]
