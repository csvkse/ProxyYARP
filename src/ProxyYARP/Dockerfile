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
    && rm -f /app/*.dbg \
    && mkdir -p /app/data \
    && chown 1654:1654 /app/data

# chiseled：精简 Ubuntu 24.04（无 apt/shell），glibc 与 sdk:10.0 匹配
# 不用 -extra：InvariantGlobalization=true 用不到 ICU（~36 MB），tzdata 也不需要
# 注意：chiseled 无 shell，最终阶段不可有 RUN 指令，/app/data 在 build 阶段创建后随 COPY 带入
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
WORKDIR /app
VOLUME /app/data
ENV DB_TYPE=sqlite
ENV DB_CONNECTION="Data Source=/app/data/proxy.db;Cache=Shared;"
# --chown 会重写整层（内容存两遍），且 COPY 不更新已存在目录的属主
# 故在 build 阶段将 data 属主设为 1654（= chiseled 镜像的 $APP_UID），COPY --from 保留源属主
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["./ProxyYARP"]
