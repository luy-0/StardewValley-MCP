# 第三方组件声明

本文件按实际发行边界记录第三方组件。仓库源码、MCP Python 发行包和 Mod ZIP 的内容不同，因此许可义务也分别说明。

## 源码仓库

本仓库不包含《星露谷物语》、SMAPI、.NET SDK、Python、`uv` 或 `protoc` 的二进制与游戏资产。这些软件只作为用户自行取得的运行或构建前置条件，其商标与版权归各自权利人所有。

`spec/proto/` 生成的 C# 与 Python 代码来自本项目自有 Proto 输入；Protocol Buffers 官方许可证明确生成代码归输入文件所有者，但生成代码仍需对应的 Protocol Buffers 运行库。

## MCP Python 发行包

`stardew-valley-mcp` wheel 和 sdist 不内嵌第三方 Python 包。安装器会根据包元数据分别取得以下直接运行依赖；准确版本以 `mcp/uv.lock` 为开发与验收基线。

| 组件 | 当前锁定版本 | 许可证 | 来源 |
|---|---:|---|---|
| jsonschema | 4.26.0 | MIT | https://github.com/python-jsonschema/jsonschema |
| MCP Python SDK | 1.28.1 | MIT | https://github.com/modelcontextprotocol/python-sdk |
| protobuf | 7.35.1 | BSD-3-Clause | https://github.com/protocolbuffers/protobuf |
| PyYAML | 6.0.3 | MIT | https://github.com/yaml/pyyaml |

这些组件及其传递依赖不属于本项目 wheel 内容；它们各自的发行包携带自己的许可证与元数据。

## Mod ZIP

Mod ZIP 会随项目程序集一起分发 `Google.Protobuf.dll`：

| 组件 | 当前锁定版本 | 许可证 | 版权归属 | 来源 |
|---|---:|---|---|---|
| Google.Protobuf | 3.34.1 | BSD-3-Clause | Copyright 2008 Google Inc. | https://github.com/protocolbuffers/protobuf |

其完整许可证文本位于 `licenses/Google.Protobuf-BSD-3-Clause.txt`，并必须与 `Google.Protobuf.dll` 一同进入 Mod ZIP。

## 构建与测试依赖

`Pathoschild.Stardew.ModBuildConfig`、Microsoft.NET.Test.Sdk、NUnit、NUnit3TestAdapter、Hatchling、pytest 与 build 只参与源码构建或测试，不进入当前运行时发行包。若未来改变打包方式并开始内嵌其中任何组件，必须在发布前重新审计并更新本文件与对应发行物。
