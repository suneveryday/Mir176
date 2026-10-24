# Mir176 Docker 部署

服务端包包含六服务、MySQL 插件、Host、运行配置、数据库结构、Compose 及散列清单。服务端组件为 0.1.4，部署工具为 0.1.9，配套 Windows 客户端 0.1.5 与登录器 0.1.6。

## 首次安装

准备 Python 3.9+、Docker Engine（Linux 容器）与 Compose v2，并确保能下载 .NET 8 和 MySQL 8 基础镜像。

1. 解压本仓库 Release 的 Server-Docker 包。
2. 从 [Mir176Client Release](https://github.com/suneveryday/Mir176Client/releases) 下载 Server-Maps ZIP，放到 deploy.sh 同级目录，不用解压。
3. 在服务端包目录执行：

```sh
sh deploy.sh
```

按提示填写玩家可达地址、新项目名及登录/选角/游戏三个端口，也可一次指定：

```sh
sh deploy.sh --address play.example --project-name fresh-classic --login-port 7000 --select-port 7100 --game-port 7200
```

入口先验证包清单与 264 份地图，原子导入后再检查 Docker、生成独立随机凭据、构建镜像并等待容器健康。可通过 `--map-resources PATH_TO_MAPS_ZIP` 指定地图 ZIP；入口不自动下载资源。

已有配置、锁、同名 Docker 资源或变更过的地图会阻止安装。此入口仅用于新安装，不自动升级；失败保留数据，不重置密码或清除卷。请勿删除已有配置后强行重跑。运行时不要继承其他 CLASSIC_/COMPOSE_ 环境变量。

登录器使用相同玩家可达地址与登录端口。选角和游戏网关地址由服务端下发，管理员还需正确设置路由与防火墙。

## 手动准备

在服务端解压目录先运行 `python3 import-resources.py --archive PATH_TO_MAPS_ZIP`，再使用 configure.py 填写地址与端口。检查生成的本地配置后运行 `docker compose up -d --build`，通过 `docker compose ps` 和 `docker compose logs game` 检查初始化。

配置、随机凭据、数据库和世界卷必须保持配套。新项目使用独立空库；停止时用 `docker compose down`，保留数据时不要加 `-v`。GM 默认关闭，按随包 GM_TOOL.md 单独配置后启用。

## 维护与限制

更新前持有维护锁，确认 bot 租约结束且玩家全部正常退出并保存。完整备份数据库、世界状态、配置及镜像，保留原项目名和卷。成套替换六服务、共享程序集及 MySQL 插件，失败时成套回退。

当前为还原预览版，包含工程采用参数；等级上限 48 是旧经验协议的工程限制。完整玩法与 PC/GM/bot 组合仍为 pending_runtime_validation。构建和离线文件检查不等于真实 Docker 部署验收。地图和其他恢复素材的权利需分别确认。
