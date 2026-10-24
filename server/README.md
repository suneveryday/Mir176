# Mir176 服务端

[src/](src/) 包含六服务、共享程序集、MySQL 插件和模块实现；[ClassicHost/](ClassicHost/) 为服务监督程序。运行配置在 [content/](../content/)，Docker 部署入口在 [deployment/](../deployment/README.md)。

直接使用请从 [Release](https://github.com/suneveryday/Mir176/releases) 下载 Server-Docker 包，并从 [客户端 Release](https://github.com/suneveryday/Mir176Client/releases) 取得 Server-Maps ZIP，放在 deploy.sh 同级目录。

源码需要 .NET 8 SDK，在仓库根运行 `bash tools/build-server.sh`。完整打包步骤见 [安装与配套](../docs/DISTRIBUTION.md)。当前运行组件 0.1.4，完整运行组合仍待验证。源码许可见 [LICENSE](LICENSE)。
