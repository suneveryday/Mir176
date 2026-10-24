# 安装与配套

服务端和独立登录器从 [Mir176 Release](https://github.com/suneveryday/Mir176/releases) 下载；PC 程序、完整资源与服务端地图从 [Mir176Client Release](https://github.com/suneveryday/Mir176Client/releases) 下载。

玩家将 Client-Program 与 Resources 解压到同一新目录，运行 Check-Client.cmd 后打开 Mir176Launcher.exe。管理员将 Server-Maps ZIP 放在服务端 deploy.sh 旁，按 [Docker 部署说明](../deployment/README.md) 准备独立安装。

distribution.json 记录配套组件版本、下载地址和资源编号。Release 的 package-manifest.json、release-manifest.json 与 SHA256SUMS.txt 提供归档及逐文件完整性信息。资源集合编号由固定索引派生，不可在同一编号下静默更换文件。

## 源码与离线打包

服务端完整构建：`bash tools/build-server.sh`。Host 使用 `dotnet build server/ClassicHost/ClassicHost.csproj -c Release -o .build/host`。取得合法地图后，在仓库根运行：

```sh
python3 tools/build-classic-content.py --maps MAP_DIRECTORY --output .build/content
python3 tools/prepare-docker-release.py --output .build/server-package --map-resource-descriptor CLIENT_CHECKOUT/resources/server-maps.json
```

服务端包不包含地图二进制，首次 Docker 部署时校验并导入独立地图包。打包器校验六服务及 MySQL 插件的共享依赖，移除调试文件。客户端打包工具在独立项目 tools/ 中。

## 发行检查

```sh
python3 tools/check-contract.py
python3 tools/check-privacy.py
python3 tools/check-privacy.py --history
python3 tools/check-release-package.py --kind server SERVER.zip
python3 tools/check-release-package.py --kind launcher LAUNCHER.zip
```

客户端项目使用 client-program、client-resources 和 server-maps 类型检查。发布清单须绑定当前源码、真实组件版本及文件散列。预览包保持 pending_runtime_validation；构建和检查不替代实机运行验收。账号、玩家库、凭据与现用配置不随包交付。
