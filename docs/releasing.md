# 版本、Tag 与 GitHub Release 维护流程

本文面向仓库维护者，规定产品版本、Tag、自动构建、Draft Release 和实机发布门禁。公共协议、能力与 Skill 包契约仍以 [`spec/VERSIONING.md`](../spec/VERSIONING.md) 为准；仓库产品版本不会自动改变这些契约版本。

## 一、版本格式

产品版本使用以下 SemVer 子集：

```text
MAJOR.MINOR.PATCH
MAJOR.MINOR.PATCH-alpha.N
MAJOR.MINOR.PATCH-beta.N
MAJOR.MINOR.PATCH-rc.N
```

Tag 必须在产品版本前增加 `v`，例如 `v0.1.0-alpha.2` 或 `v0.1.0`。预发布标识必须带正整数序号；禁止省略 Patch、使用 `latest`、使用构建元数据，或删除、移动和重建已经发布的 Tag。

- `alpha.N`：能力仍在演进，允许公开记录的已知边界；
- `beta.N`：主要能力范围已经冻结，以修复 Bug 和兼容问题为主；
- `rc.N`：只接受发版阻塞修复；
- 稳定版 Patch：向后兼容的 Bug 修复；
- 稳定版 Minor：向后兼容的功能增补；
- 稳定版 Major：明确的破坏性产品升级。

在 `0.x` 阶段发生明显破坏性变化时提升 Minor。仓库只维护最新预览系列，不为每个旧预览版本建立长期维护分支。

## 二、产品版本权威

根目录 [`VERSION`](../VERSION) 是唯一人工修改入口。同步脚本把它确定性写入 Mod manifest、Mod 项目、MCP 项目和 Python 包：

```bash
python3 scripts/release_version.py set 0.1.0-alpha.2
python3 scripts/release_version.py check
```

Mod 保留 SemVer；Python 包采用等价 PEP 440：`alpha.N → aN`、`beta.N → bN`、`rc.N → rcN`。不要手工分别修改多个版本文件，也不要在 GitHub Actions 中用 `sed` 或输入参数覆盖源码版本。

## 三、版本 PR

每次发布先建立普通 Feature Branch，并在一个范围明确的 PR 中完成版本变化和本次发布所需说明。合并前至少运行：

```bash
python3 scripts/release_version.py check
./scripts/verify.sh
```

涉及 Mod 发行包时，再使用合法安装的游戏或与 CI 相同的公开引用程序集运行：

```bash
./mod/scripts/build.sh --package
uv run --project mcp python scripts/audit_packages.py --with-mod
```

版本 PR 必须先通过仓库分支保护并合并到 `main`。Tag 不能代替代码审查，也不能指向只存在于 Feature Branch 的提交。

## 四、手动发版演练

在 GitHub Actions 中手动运行 `Release` 工作流。手动运行只构建并上传临时 Artifact，不创建 GitHub Release，因此适合在打 Tag 前验证完整发行链路。

演练应产出且只产出：

- `StardewValleyMCP-Mod-v<SemVer>.zip`；
- `stardew_valley_mcp-<PEP 440>-py3-none-any.whl`；
- `stardew_valley_mcp-<PEP 440>.tar.gz`；
- `SHA256SUMS.txt`。

下载 Artifact 后使用：

```bash
shasum -a 256 -c SHA256SUMS.txt
```

确认 Mod ZIP、wheel 和 sdist 都来自工作流显示的同一 Commit。演练成功不等于已经完成游戏实机验收。

## 五、创建 Tag

确认版本 PR 已合并、`main` 已同步且演练通过后，创建签名的 annotated Tag：

```bash
git switch main
git pull --ff-only
python3 scripts/release_version.py check --tag v0.1.0-alpha.2
git tag -s v0.1.0-alpha.2 -m "StardewValley MCP v0.1.0-alpha.2"
git push origin v0.1.0-alpha.2
```

Release 工作流会重新验证 Tag 格式、源码版本、annotated Tag 类型以及目标 Commit 属于远端 `main` 历史。alpha、beta 和 rc 自动标记为 prerelease；稳定版不标记。工作流只创建 Draft Release，不会直接公开。

## 六、Draft Release 验收

必须从 Draft Release 下载真实资产，不得用本地 `bin/` 或先前工作流 Artifact 代替。按顺序完成：

1. `SHA256SUMS.txt` 能校验三个包；
2. 解压 Mod ZIP，确认只有声明的 Mod、Protocol、依赖和许可证文件；
3. 安装 wheel，确认 `stardew-valley-mcp --version` 与 Release 对应，`doctor` 通过；
4. 把 Release Mod ZIP 安装到隔离 Mods 目录，通过 SMAPI 启动；
5. 加载测试存档，确认 Mod 版本、只读连接和 `stardew_query_runtime`；
6. 获得明确写授权后执行一次 `face`，再查询朝向确认后置状态；
7. 检查 SMAPI 日志没有加载错误、共享秘密或本机绝对路径泄漏。

实机结论必须引用本次下载资产的版本与 SHA-256。只有以上门禁完成后，维护者才手动 Publish Draft Release。

## 七、失败、撤回与紧急修复

- Tag 工作流失败且 Release 尚未创建：修复代码或工作流，提交新 PR；不要移动已经推送的 Tag。若 Tag 已对外可见，使用下一个预发布或 Patch 版本。
- Draft 资产错误：保持 Draft 不公开，修复后发布新版本；不要用同名资产静默覆盖已验收的外部文件。
- Release 已公开但存在缺陷：在 Release Notes 明确标记问题，必要时将 Release 标为 prerelease，并尽快发布下一 Patch；不要删除历史来隐藏问题。
- 发现安全问题：不要先建立公开 Issue；由维护者私下确认影响范围并准备修复版本，安全报告入口在仓库安全政策建立后以 `SECURITY.md` 为准。

自动生成的 Release Notes 是本次变化摘要，不替代破坏性变化、迁移步骤、已知边界和实机验证结果。维护者应在 Publish 前补齐这些内容。

## 八、合并后的仓库治理

发布工程启用后，`main` 应要求 Ubuntu/macOS Verify 与 Mod Package 检查通过，并继续禁止 Force Push 和分支删除。`v*` Tag 应通过 GitHub Ruleset 禁止删除和强制更新；Feature Branch 在 PR 合并后自动删除。上述远端设置不由 Release Workflow 自行修改，避免工作流获得不必要的管理权限。
