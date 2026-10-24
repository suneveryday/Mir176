# 当前发行配套

| 组件 | 版本 | 契约 |
| --- | --- | --- |
| Docker 服务端 | 0.1.4 | compatibility.json 中的全部 v1 接口 |
| Windows 客户端 | 0.1.5 | legacy-wire、scroll-retain、map-navigation v1 |
| 独立登录器 | 0.1.6 | 客户端旁 INI 的地址和端口 |
| GM / bot | ecosystem.lock.json | 固定实际源码提交 |

当前说明、打包与文件组织不改变游戏实现、协议、存档或地图字节。服务端运行程序集和两个 Windows EXE 的真实散列由发行清单绑定；源码中的内部兼容标识保留。运行配置从 content/ 编译，经验配置保留现行数值。

文件完整性、隐私、契约和离线打包检查是发行校验范围。完整 PC/服务端/GM/bot 运行状态为 pending_runtime_validation，不根据文档或打包整理重新授予运行验收。

无玩家存档迁移。部署维护需单独授权并持有维护锁，确认 bot 租约结束、玩家退出及正常存档，完整备份后成套更新。失败恢复同一套程序、配置、数据库和世界状态；不单独回退共享 DLL。
