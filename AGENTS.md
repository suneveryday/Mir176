# Mir176 维护规则

- 本项目独立维护面向 2003 国服 1.76 体验的还原工程。服务端入口为 server/src/，登录器入口为 launcher/。
- PC 源码位于独立项目 Mir176Client 的 client/current-source/。主项目 client/ 与 resources/ 仅保留介绍及链接，地图和素材不进入主仓库或服务端发行包。
- 运行配置在 content/；工程采用值不等于原官服参数。未知值保留 null，不伪造官方设定、构建或验收结论。保留协议、存档和崩溃等正确性修复。
- 每次更新阅读 docs/COMPATIBILITY.md、各项目 compatibility.json、contracts.lock.json 与 ecosystem.lock.json。GM 与 bot 为独立仓库，只固定实际提交。
- 协议、编码、结构体、GM 字段、commandId 去重及存档回执、物品、技能、地图或恢复变化须审查 PC、GM、bot 影响。修改受保护文件后以真实 SHA256 更新锁并审查 docs/RELEASE_COMPATIBILITY.md。
- 各仓库运行 tools/check-contract.py；组合工作区运行 tools/check-ecosystem.py。先提交子项目，再固定其提交。
- 修改共享 .NET 程序集须完整重建六服务及 MySQL 插件并核对依赖。没有 ABI 证据不局部替换。
- 发布验收覆盖普通登录、物品/技能/地图、存档重登、GM 回执及相应 bot；完整运行验收前保持 pending_runtime_validation。
- 提交与推送前运行 tools/check-privacy.py 及 --history 模式；隐私、隔离边界与契约检查在 CI 中保持启用。凭据、私有配置、账号、角色、日志、玩家数据库和工具链不进入 Git 或发行包。
- 代码、Git、文档管理不授予 Docker 部署、GM 修改或 bot 运行权限。运行操作需另行授权，遵守维护锁、bot 租约、零玩家、正常退出存档、完整备份与整套回退。
- Describe deployment only as Docker deployment. Never publish actual deployment endpoints, host storage locations, machine-specific paths, real game identities, credentials, or personal document references. Keep operational adapters and original evidence local.
- 保留第三方源码许可与署名；地图、素材和二进制单独备份并记录散列。不得将仓库拆分或私有保存描述为版权授权。
