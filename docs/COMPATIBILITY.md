# 兼容性

Mir176 提供服务端协议，Mir176Client、独立 GM 工具和 bot 消费这些接口。各项目的实际组合由 ecosystem.lock.json 固定。

| 契约 | 必须保持的行为 |
| --- | --- |
| mir2-legacy-wire/v1 | 消息编号、12 字节小端头、编码、长度、动作及 ACK |
| mir2-gm-api/v1 | 角色、物品、地图、命令接口的字段与状态 |
| classic-gm-profile/v1 | 项目与世界绑定、目录指纹、能力与等级边界 |
| mir2-command-receipt/v1 | 精确 commandId 去重、持久化回执、超时只读查询 |
| mir2-scroll-retain/v1 | 使用成功状态与原卷轴实例返回语义 |
| mir2-map-navigation/v1 | 地图编号、行走标志、坐标、门与传送 |

compatibility.json 声明提供和依赖关系；contracts.lock.json 固定关键实现的 SHA256。修改受保护文件后审查各消费方并更新真实散列及 RELEASE_COMPATIBILITY.md。共享 .NET 变化须重建全部六服务及 MySQL 插件。

各项目运行 `python3 tools/check-contract.py`。按照 ecosystem.lock.json 克隆并检出客户端、GM 和 bot 后，主项目运行 `python3 tools/check-ecosystem.py`。缺失子项目、提交不一致或未提交修改均不能视为通过。主项目 CI 不自动取得其他私有仓库。

静态检查与构建不能代替普通登录、动作、物品/技能/地图、保存重登、GM 回执和 bot 验收。完整组合状态为 pending_runtime_validation。凭据及本机运行记录仅在私有工作区维护。
