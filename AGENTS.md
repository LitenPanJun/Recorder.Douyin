# AGENTS.md — AI Agent 规则文档

## 项目概述

**抖音录播姬（Douyin Recorder）** —— 基于 C# 实现的抖音直播录制工具，集成弹幕接收与推流下载功能。

### 类库依赖

| 类库 | 位置 | 说明 |
|------|------|------|
| Danmaku.Douyin | `Danmaku.Douyin/` | 抖音直播弹幕客户端（protobuf-net） |
| Douyin.Live | `Downloader.Douyin/Douyin.Live/` | 抖音直播间 API 客户端 |
| Douyin.StreamDownloader | `Downloader.Douyin/Douyin.StreamDownloader/` | FLV 推流下载 + HEVC 编码管道 |

类库保持独立项目、独立编译，通过 ProjectReference 引用，编译时复制输出并携带依赖。
类库项目**统一加入解决方案**（`Recorder.Douyin.slnx`），但文件夹和源码编辑保持独立，各自独立开发和二次编译。
对类库可进行合理抽象、解耦和改造以服务录播姬需求，但**不得过度解耦**，应保持类库与录播姬核心的分离状态。

---

## Git 工作流规则

### 1. 单步骤执行 Git 提交

每一次操作步骤（无论多微小）都应独立提交，做到事无巨细：

```bash
# ✅ 正确示例（每个步骤独立提交）
git add -A && git commit -m "feat(core): 添加直播间监控功能" && git push
git add -A && git commit -m "fix(danmaku): 修正弹幕解析超时阈值" && git push
git add -A && git commit -m "refactor(stream): 提取重复的 HTTP 工具方法" && git push

# ❌ 禁止积攒多个改动后一次性提交
```

### 2. 使用严格的分支制度开发

| 分支 | 用途 | 来源 |
|------|------|------|
| `main` | 生产就绪代码，仅当用户明确要求时合并 | — |
| `develop` | 主开发分支 | `main` |
| `feature/<name>` | 功能特性开发 | `develop` |
| `fix/<name>` | 缺陷修复 | `develop` |
| `release/<version>` | 发布准备 | `develop` |

- `main` 分支禁止直接提交，仅接受 Pull Request 合并
- 所有功能开发从 `develop` 切出 `feature/<英文名称>` 分支
- 分支名使用英文小写 + 连字符，如 `feature/danmaku-integration`

### 3. 规范的 Commit Message 格式

```
<type>(<scope>): <subject>
```

- **`<type>`** — 标准化词汇（英文），可选值：
  - `feat` — 新功能
  - `fix` — 缺陷修复
  - `refactor` — 重构
  - `chore` — 工程配置/构建
  - `docs` — 文档
  - `style` — 代码格式
  - `test` — 测试
- **`<scope>`** — 影响范围（英文），如 `core`、`danmaku`、`stream`、`ui`
- **`<subject>`** — 描述内容（中文，简洁明了）

**示例：**
```
feat(core): 添加直播间监控核心逻辑
fix(danmaku): 修复弹幕断线重连异常
refactor(stream): 重构推流下载管道分割策略
chore(project): 初始化项目结构和 Git 工作流
docs(readme): 更新部署说明和配置指南
```

### 5. 禁止主动推送

除非用户在消息中明确要求执行推送动作（如"推送"、"push"、"提交并推送"等），否则 Agent **不得执行任何 `git push` 操作**。仅允许 `git add`、`git commit` 等本地操作。

### 6. 提交前检查

提交前执行以下验证：

```bash
# 构建验证
dotnet build

# 检查未跟踪文件
git status
```

---

## 基本准则

1. 不提交任何以 `.` 开头的文件夹（如 `.libs/`、`.vs/` 等）
2. 不提交 `bin/`、`obj/` 等构建产物
3. 不提交配置文件中的敏感信息（密钥、Token 等）
4. `.libs/` 为原始参考，不得直接修改；其副本（`Danmaku.Douyin/`、`Downloader.Douyin/`）可进行合理抽象、解耦和改造，但**不得过度解耦**，应保持类库与录播姬核心的分离状态
5. 本项目模块遵循 `Recorder.<模块名>` 命名约定（如 `Recorder.Core`）
6. 若有超出规则的新要求，按新要求执行但不修改规则文件本身
7. `main` 分支禁止主动合并——除非用户明确要求，否则不得将任何分支合并到 `main`

## 开源协议

本项目使用 **GNU Affero General Public License v3 (AGPL-3.0)** 发布，核心要求：
- 全部 Fork 必须开源（含修改说明）
- 修改后的代码通过网络提供服务时，必须向用户提供源代码
- 允许商业使用，但必须保持源代码开放
- 未经授权的闭源修改/发布可追究法律责任

详见 [LICENSE](./LICENSE) 文件。
