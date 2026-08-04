# .NET 构建工具与游戏宿主兼容性

## 当前裁决

Mod 与公共 Protocol 产物继续目标 `net6.0`，不直接改成 `net8.0` 或 `net10.0`。Stardew Valley 1.6 与当前 SMAPI 仍由 .NET 6 宿主加载 Mod；目标更高版本的程序集不能被该宿主安全加载。SMAPI 官方 Mod 开发指南也仍建议以 .NET 6.0 为目标。

这不表示 CI 必须继续使用已停止支持的 .NET 6 SDK。仓库区分两个版本职责：

- **构建 SDK**：CI 使用当前受支持的 .NET 10 LTS SDK；微软 SDK 支持向下构建 `net6.0`。
- **测试 Runtime**：CI 同时安装 Microsoft.NETCore.App 6.x，用它运行 `net6.0` 测试进程，保持与游戏宿主一致。
- **发行产物**：Mod、Protocol 和传输 Spike 仍为 `net6.0`；发行包不携带或替换游戏 Runtime。

## EOL warning 的处理

.NET SDK 会对 `net6.0` 目标重复报告停止支持 warning。仓库只对上述受宿主约束的项目关闭 `CheckEolTargetFramework`，并在 CI 中显式验证“构建 SDK 为 10.x、测试 Runtime 包含 6.x”。这项关闭只消除已知且无法由本仓独立修复的重复提示，不代表 .NET 6 已恢复安全支持，也不应扩展到不受游戏宿主约束的新服务。

## 何时真正升级目标框架

只有以下条件同时满足时，才升级 Mod 的 Target Framework：

1. Stardew Valley 与 SMAPI 的正式稳定版本已经迁移到同一受支持 Runtime；
2. SMAPI 官方 Mod 开发指南明确推荐新的 Target Framework；
3. Windows、macOS 与 Linux CI 均能完成 Protocol、Mod、测试和发行包构建；
4. 至少在一个真实游戏进程中证明 SMAPI 能加载新产物，并完成只读查询与一次受控动作验收。

上游迁移前，不通过多目标发行、私带 Runtime、修改游戏启动参数或忽略加载失败来提前声称已升级。

## 参考

- [Stardew Valley Wiki：IDE reference](https://stardewvalleywiki.com/Modding:IDE_reference)
- [Stardew Valley Wiki：Migrate to Stardew Valley 1.6](https://stardewvalleywiki.com/Modding:Migrate_to_Stardew_Valley_1.6)
- [Microsoft：.NET 支持策略](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)
- [Microsoft：SDK 向下目标支持](https://learn.microsoft.com/dotnet/core/porting/versioning-sdk-msbuild-vs#targeting-and-support-rules)
