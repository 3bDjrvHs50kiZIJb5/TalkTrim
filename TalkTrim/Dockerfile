# 单项目模板：仅引用 NeoAdmin.Blazor NuGet 包
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# dotnet publish 会触发 csproj 中的 npm install / build:css，构建阶段需 Node
RUN apt-get update && \
    apt-get install -y --no-install-recommends nodejs npm && \
    rm -rf /var/lib/apt/lists/*

COPY TalkTrim.csproj .
RUN dotnet restore TalkTrim.csproj

COPY . .
RUN dotnet publish TalkTrim.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update && \
    apt-get install -y --no-install-recommends locales ffmpeg && \
    sed -i '/en_US.UTF-8/s/^# //g' /etc/locale.gen && \
    sed -i '/zh_CN.UTF-8/s/^# //g' /etc/locale.gen && \
    locale-gen && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN mkdir -p /app/Logs && \
    mkdir -p /app/wwwroot/uploads && \
    mkdir -p /app/wwwroot/avatars && \
    mkdir -p /app/keys && \
    chmod -R 777 /app/Logs && \
    chmod -R 777 /app/wwwroot

RUN rm -f /app/neoadmin.db || true

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production
ENV LANG=zh_CN.UTF-8
ENV LC_ALL=zh_CN.UTF-8
ENV LC_CTYPE=zh_CN.UTF-8

EXPOSE 80
ENTRYPOINT ["dotnet", "TalkTrim.dll"]
