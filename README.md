# Recorder.Douyin — 抖音录播姬

基于 C# 实现的抖音直播录制工具，支持推流下载与弹幕接收同步录制。

> 弹幕接收与视频流接收参考了 [AllLive](https://github.com/xiaoyaocz/AllLive) 并基于其进行二次开发。

## 项目结构

```
Recorder.Douyin/
├── Recorder.Shared/             公共类库（HttpUtils、SharedUtils、DanmakuArgs）
├── API.Douyin/                  抖音直播间 API 客户端
├── Downloader.Douyin/           FLV 推流下载 + HEVC 编码管道
├── Danmaku.Douyin/              抖音直播弹幕客户端（protobuf-net）
├── Recorder.Core/               录播姬主入口（Console App）
└── Recorder.Douyin.slnx         解决方案文件
```

### 类库说明

| 项目 | 路径 | 框架 | 职责 |
|------|------|:----:|------|
| `Recorder.Shared` | `Recorder.Shared/` | net10.0 | 公共工具类：统一 HttpClient、随机 ID/msToken 生成、弹幕参数模型 |
| `API.Douyin` | `API.Douyin/` | net10.0 | 直播间 API 调用、Cookie 管理、画质解析、房间信息查询 |
| `Downloader.Douyin` | `Downloader.Douyin/` | net10.0 | FLV 分段下载、ffmpeg HEVC 转码 |
| `Danmaku.Douyin` | `Danmaku.Douyin/` | net10.0 | WebSocket 弹幕接收、protobuf 消息解析、JS 签名计算 |
| `Recorder.Core` | `Recorder.Core/` | net10.0 | 录播姬主入口，控制台应用 |

### 类库依赖

```
Recorder.Core
├── Recorder.Shared
├── API.Douyin
│   └── Recorder.Shared
├── Downloader.Douyin
│   └── Recorder.Shared
└── Danmaku.Douyin
    └── Recorder.Shared
```

每个类库独立项目、独立编译，通过 `ProjectReference` 引用。

## 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- （可选）[ffmpeg](https://ffmpeg.org/) — HEVC 编码需要

## 快速开始

```bash
# 克隆仓库
git clone https://github.com/LitenPanJun/Recorder.Douyin.git
cd Recorder.Douyin

# 构建
dotnet build

# 运行
dotnet run --project Recorder.Core
```

## API 使用示例

```csharp
using API.Douyin;

var client = new DouyinLiveClient();

// 通过直播间 ID 获取房间信息
var detail = await client.GetRoomDetailAsync("123456789");

// 通过用户唯一标识获取房间信息
var detail = await client.GetRoomDetailByUserUniqueIdAsync("user123");

// 获取可用画质列表
var qualities = await client.GetPlayQualitiesAsync(detail);

// 一键获取房间信息 + 画质
var result = await client.GetRoomDetailWithQualitiesAsync("123456789");
```

## 推流下载

```csharp
using Downloader.Douyin;

var downloader = new StreamDownloader();
var result = await downloader.DownloadAsync(
    url: "https://...flv",
    outputPath: "./recordings/live",
    segmentDuration: TimeSpan.FromMinutes(10),
    enableHevc: false,
    progress: new Progress<DownloadProgress>(p => Console.WriteLine(p)));
```

## 弹幕与推流同步录制

弹幕接收与推流下载可同时进行，实现完整的直播录制（视频流 + 弹幕消息）：

```csharp
using API.Douyin;
using DouyinDanmaku.Models;
using DouyinDanmaku.Services;
using Downloader.Douyin;

// 1. 获取直播间信息和画质
var liveClient = new DouyinLiveClient();
var detail = await liveClient.GetRoomDetailAsync("webRid");
var qualities = await liveClient.GetPlayQualitiesAsync(detail);
var best = qualities.First();

// 2. 启动弹幕接收
var danmaku = new DouyinDanmakuClient();
danmaku.OnMessage += msg => SaveDanmaku(msg); // 保存弹幕到文件
await danmaku.StartAsync(detail.DanmakuData!);

// 3. 同步启动推流下载
var downloader = new StreamDownloader();
var result = await downloader.DownloadAsync(
    url: best.Urls.First(),
    outputPath: $"./recordings/{detail.RoomId}",
    segmentDuration: TimeSpan.FromMinutes(10),
    progress: new Progress<DownloadProgress>(p => Console.WriteLine(p)));
```

## 开发指南

### 分支制度

| 分支 | 用途 | 来源 |
|------|------|------|
| `main` | 生产就绪代码 | — |
| `develop` | 主开发分支 | `main` |
| `feature/<name>` | 功能开发 | `develop` |
| `fix/<name>` | 缺陷修复 | `develop` |
| `release/<version>` | 发布准备 | `develop` |

### 提交规范

```
<type>(<scope>): <subject>
```

- **type**: `feat` / `fix` / `refactor` / `chore` / `docs` / `style` / `test`
- **scope**: 影响范围（英文），如 `core`、`danmaku`、`stream`、`api`
- **subject**: 描述内容（中文，简洁明了）

示例：
```
feat(core): 添加直播间监控核心逻辑
fix(danmaku): 修复弹幕断线重连异常
refactor(stream): 重构推流下载管道分割策略
```

### 构建验证

```bash
dotnet build
```

提交前确保 0 错误、0 警告。

## 许可协议

本项目使用 **GNU Affero General Public License v3 (AGPL-3.0)** 发布。

核心要求：
- 全部 Fork 必须开源（含修改说明）
- 修改后的代码通过网络提供服务时，必须向用户提供源代码
- 允许商业使用，但必须保持源代码开放

详见 [LICENSE](./LICENSE) 文件。
