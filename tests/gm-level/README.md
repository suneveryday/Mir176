# 可配置加等级回归

完整构建服务端后，从仓库根执行：

```sh
dotnet build tests/gm-level/GmLevel.csproj -p:ServerBin="$PWD/.build/server/GameSrv" -o .build/gm-level-tests
cp .build/server/GameSrv/*.dll .build/gm-level-tests/
dotnet .build/gm-level-tests/GmLevel.dll
```

34 项检查直接调用真实 GM 命令实现，覆盖旧版默认加一级、指定增加量、等级上限、溢出输入、同编号同参数重试、同编号不同参数冲突、回执序列化与精确只读存档确认。仅创建内存角色，不连接服务、不操作数据库，不授予部署或真实 GM 变更权限。

GMTool 的 Windows 控件、数值传参和原请求重试测试见其独立仓库 `tests/pending-confirmation/run-level-windows.ps1`，仅连接本机模拟 HTTP 接口。静态或离线测试不等于 Docker/PC 运行验收。
