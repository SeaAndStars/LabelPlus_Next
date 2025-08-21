# 请参阅 https://aka.ms/customizecontainer 以了解如何自定义调试容器，以及 Visual Studio 如何使用此 Dockerfile 生成映像以更快地进行调试。

# 通过这些 ARG，可以在从 VS 进行调试时交换用于生成最终映像的基础
ARG LAUNCHING_FROM_VS
# 此操作会设置最终的基础映像，但仅当已定义 LAUNCHING_FROM_VS 时才会如此
ARG FINAL_BASE_IMAGE=${LAUNCHING_FROM_VS:+aotdebug}

# 此阶段用于在快速模式(默认为调试配置)下从 VS 运行时
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app


# 此阶段用于生成服务项目
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
# 安装 clang/zlib1g 开发依赖项以发布到本机
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
    clang zlib1g-dev
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["LabelPlus_Next.Server/LabelPlus_Next.Server.csproj", "LabelPlus_Next.Server/"]
RUN dotnet restore "./LabelPlus_Next.Server/LabelPlus_Next.Server.csproj"
COPY . .
WORKDIR "/src/LabelPlus_Next.Server"
RUN dotnet build "./LabelPlus_Next.Server.csproj" -c $BUILD_CONFIGURATION -o /app/build

# 此阶段用于发布要复制到最终阶段的服务项目
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LabelPlus_Next.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=true

# 从 VS 启动以支持常规模式(不使用调试配置时为默认值)下的调试时，此阶段用作最终阶段的基础
FROM base AS aotdebug
USER root
# 安装 GDB 以支持本机调试
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
    gdb
USER app

# 此阶段在生产中使用，或在常规模式下从 VS 运行时使用(在不使用调试配置时为默认值)
FROM ${FINAL_BASE_IMAGE:-mcr.microsoft.com/dotnet/runtime-deps:9.0} AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["./LabelPlus_Next.Server"]