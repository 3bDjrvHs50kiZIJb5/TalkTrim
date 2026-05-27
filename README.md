# TalkTrim

口播视频剪辑后台：识别口播稿、生成双语字幕、去口气加速与成片压制。

基于 **ASP.NET Core 10** + **Blazor Server**，管理端使用 [NeoAdmin.Blazor](https://www.nuget.org/packages/NeoAdmin.Blazor)。

## 功能概览

- **口播稿识别**：从视频提取音频，调用阿里云 DashScope（Paraformer）进行 ASR，生成字幕与时间轴
- **双语字幕**：支持字幕翻译与 SRT/ASS 导出，压制时烧录到成片
- **去口气**：按句间停顿上限裁切静音，同步调整字幕时间轴
- **视频压制**：调速、字幕烧录、可选片尾拼接，输出成片
- **博客模块**：文章、频道、评论等（NeoAdmin 脚手架）
- **后台任务**：转写、压制在后台队列中异步执行

## 环境要求

| 依赖 | 说明 |
|------|------|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | 本地开发与构建 |
| [Node.js](https://nodejs.org/) | 构建 Tailwind CSS（`dotnet build` 时会自动 `npm install`） |
| `ffmpeg` / `ffprobe` | 音视频处理，需在系统 `PATH` 中（Docker 镜像已内置） |
| 阿里云 DashScope API Key | 语音识别与翻译，在配置中填写 |
| 阿里云 OSS（可选） | 大文件上传与任务处理时访问远程视频，可关闭 |

## 快速开始

### 本地运行

```bash
cd TalkTrim
dotnet run
```

浏览器访问 <http://localhost:5038>。开发时也可使用：

```bash
./dotnet10.sh   # dotnet watch run
```

默认管理员账号见 `appsettings.json` 中 `NeoAdmin.SeedAdminUserName` / `SeedAdminPassword`（默认为 `admin` / `admin`）。登录后进入 `/Admin` 管理视频项目。

### Docker 部署

```bash
cd TalkTrim
./docker-auto.sh
```

默认映射宿主机端口 **5050**（可通过环境变量 `HOST_PORT` 修改）。数据目录（数据库、上传、日志）通过 `docker-compose.yaml` 挂载到项目目录。

## 配置说明

主要配置在 `TalkTrim/appsettings.json`。本地密钥请复制 `appsettings.Development.example.json` 为 `appsettings.Development.json` 后填写（该文件已加入 `.gitignore`，不会提交到仓库）。

| 节点 | 作用 |
|------|------|
| `NeoAdmin` | SQLite 连接、上传目录、种子管理员等 |
| `DashScope` | API Key、语言提示、翻译目标、字幕切分与语气词处理等 |
| `Oss` | 是否启用 OSS、Bucket、密钥、对象前缀等 |

启用 OSS 后，视频与中间文件可存于对象存储；未启用时使用本地上传目录 `wwwroot/uploads`。

## 项目结构

```
TalkTrim/
├── TalkTrim.csproj      # Web 主项目
├── Program.cs           # 应用入口与 DI 注册
├── Entities/            # 数据实体（Video、Blog）
├── Services/            # 转写、压制、OSS、字幕等业务
├── Components/          # Blazor 页面与后台组件
├── Api/                 # REST API
├── fonts/               # 字幕压制用字体
├── wwwroot/             # 静态资源与上传文件
├── docker-compose.yaml
└── Dockerfile
```

## 典型工作流

1. 在后台创建**视频项目**，上传或填写视频地址
2. 提交**口播稿识别**任务，生成字幕与口播稿
3. 编辑字幕、调速、去口气参数等
4. 提交**视频压制**任务，得到成片 URL

首页提供 API 文档入口（Swagger）与后台登录链接。

## 许可证

未指定许可证时，以仓库所有者约定为准。
