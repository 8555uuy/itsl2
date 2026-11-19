# itsl2

一个用 C# 从零手写的 Minecraft 启动器 —— 不是套壳，不是调第三方启动库，
而是自己跟 Mojang 官方接口打交道：拉版本清单、解析版本 JSON、下载客户端和依赖库。

写这个项目的目的是练手，顺便把 Minecraft 启动这件事的底层逻辑真正搞明白。

## 现在能做什么

- 扫描本地已安装的 Minecraft 版本（会校验版本 json，空文件夹不算）
- 从官方清单拉取全部正式版（release）列表
- 下载指定版本的 `client.jar`
- 按 Maven 仓库结构下载全部依赖库
- 记住上次使用的版本目录，输 `xiaobao` 就能自动填回

## 还没做

- `assets` 资源文件下载
- natives（原生库）解压
- 拼装 JVM 参数和游戏参数
- 调用 Java 真正启动游戏
- 账号登录（正版 / 离线）

所以严格说，它现在是个**下载器**，还不是完整的启动器。

## 环境要求

- Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 运行

```powershell
# 克隆
git clone https://github.com/8555uuy/itsl2.git
cd itsl2

# 跑起来
dotnet run --project src/Itsl2.Console/Itsl2.Console.csproj
```

也可以直接用 Visual Studio 打开 `Itsl2.sln`，按 F5。

下载的文件会存到桌面下的 `itsl2` 文件夹：

```
桌面/itsl2/
├── versions/
│   └── 1.21.5/
│       ├── 1.21.5.json
│       └── client.jar
└── libraries/
    └── com/google/gson/gson/2.10.1/gson-2.10.1.jar
```

## 项目结构

```
itsl2/
├── Itsl2.sln
├── .gitignore
├── LICENSE
├── README.md
└── src/
    └── Itsl2.Console/
        ├── Itsl2.Console.csproj
        ├── Program.cs                 主程序：菜单交互、流程调度、版本扫描
        ├── FileDownloader.cs          下载单个文件，自动建目录
        └── McOnlineVersionFetcher.cs  联网拉清单、解析版本 JSON、过滤依赖库
```

## 代码说明

三个文件各管一摊，职责不混：

**`Program.cs`** —— 只负责跟用户打交道。显示菜单、读输入、按选择调下面的类。
里面还有个 `McVersionScanner`，负责扫本地版本目录。

**`FileDownloader.cs`** —— 给个网址和保存路径，它就把文件拉下来。
会先检查目标文件夹存不存在，不存在就建；状态码不是 200 就跳过，不让一个 404 把整批下载搞崩。

**`McOnlineVersionFetcher.cs`** —— 跟 Mojang 官方服务器打交道，三件事：
1. 拉版本清单，只留正式版
2. 拉某个版本的详情 JSON
3. 解析详情 JSON，算出要下载哪些文件

### 关于版本 JSON 的两种格式

Minecraft 的版本 JSON 有两套结构，这个项目都得认：

| | 旧格式（1.13 以前） | 新格式（1.19 以后） |
|---|---|---|
| 原生库标记 | `natives: {"windows": "natives-windows"}` | 没有 `natives` 字段，直接写 `name: org.lwjgl:lwjgl:3.4.3:natives-windows` |
| 下载地址 | `downloads.classifiers["natives-windows"]` | `downloads.artifact` |
| 启动参数 | `minecraftArguments`（一整条字符串） | `arguments.game`（结构化数组） |

### 关于 `rules` 过滤

每个依赖库可能带 `rules` 字段，说明它只在某些系统上需要。比如：

```json
"rules": [{ "action": "allow", "os": { "name": "osx" } }]
```

这条表示只有 macOS 要下载。Windows 上就该跳过。

判断逻辑跟官方启动器一致：

1. 没有 `rules` → 全平台都要
2. 有 `rules` → 一条条看，**最后一条命中的规则说了算**
3. 一条都没命中 → 不要

另外还有按 CPU 架构区分的库（`arm64` / `x86`），官方在 `rules` 里不一定写清楚，
所以代码里额外按库名后缀补了一道判断。

## 遇到过的坑

记录一下，给以后的自己（和别的初学者）少走弯路：

- **`natives` 的键是小写 `windows`，不是 `Windows`** —— 写成大写永远取不到地址
- **原生库的真实下载地址不在 `natives` 里** —— 它在 `downloads.classifiers` 下面
- **官方 JSON 里有重复条目** —— 同一个库可能写两遍，只是 `rules` 不同，去重时按下载路径判
- **`Console.Clear()` 在输出重定向时会抛异常** —— 想用管道抓输出的话得包一层

## 待办

- [ ] 下载 `assets` 资源（按 hash 分桶存）
- [ ] 解压 natives 到独立目录
- [ ] 拼装完整的启动命令行
- [ ] 探测 / 下载 Java 运行时
- [ ] 真正把游戏拉起来

## 许可

[MIT](LICENSE)
