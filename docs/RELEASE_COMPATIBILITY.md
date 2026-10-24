# 当前发行配套

| 组件 | 版本 | 契约 |
| --- | --- | --- |
| Docker 服务端 | 0.1.4 | compatibility.json 中的全部 v1 接口 |
| Windows 客户端 | 0.1.6 | legacy-wire、scroll-retain、map-navigation v1 |
| 独立登录器 | 0.1.6 | 客户端旁 INI 的地址和端口 |
| GM / bot | ecosystem.lock.json | 固定实际源码提交 |

客户端 0.1.6 修正本机环境检查的误判，已完成官方 Release / Win32 完整构建及 11 项 Windows 离线环境回归；详见客户端发布兼容性说明。服务端、登录器及资源不变，PC、GM、bot 的协议、编码、结构体、物品/技能/地图、commandId 回执和存档恢复均不变，无存档迁移。服务端运行程序集和两个 Windows EXE 的真实散列由发行清单绑定；内部兼容标识保留。运行配置从 content/ 编译，经验配置保留现行数值。

文件完整性、隐私、契约和离线打包检查是发行校验范围。完整 PC/服务端/GM/bot 运行状态为 pending_runtime_validation，不根据文档或打包整理重新授予运行验收。

无玩家存档迁移。部署维护需单独授权并持有维护锁，确认 bot 租约结束、玩家退出及正常存档，完整备份后成套更新。失败恢复同一套程序、配置、数据库和世界状态；不单独回退共享 DLL。
