# GM 工具配套

经典版 GM 工具通过独立接口管理金币、等级、生命/魔法、物品、技能、装备、宠物和地图。工具与 bot 的源码及版本由各自仓库维护，实际配套提交见 ecosystem.lock.json。

## 世界绑定与回执

`GET /api/classic/profile` 返回项目、范围、世界 GUID、目录 SHA256、等级边界和能力。工具要求 classic-gm-profile/v1 匹配，读请求和 `POST /api/classic/commands` 绑定项目与世界；绑定错误或目录未加载时拒绝操作。

每个命令使用唯一 commandId，超时后先查询原回执。不得用新 ID 重发不确定结果的修改。目录外物品、技能和地图不允许管理；物品名称与 ID 须一致。

`classic-gm-instance.id` 是世界身份，须随数据库及世界状态备份，正常重启和升级时保留。不要复制给其他世界、删除待确认记录或随意重绑配置。

## Docker 部署准备

GM 默认关闭，基础 Compose 不映射管理端口或提供令牌。显式启用 CLASSIC_GM_ENABLED 并提供独立有效密钥后，Host 仅将令牌文件交给 GameSrv。

获准维护后，在服务端解压目录先运行 configure.py 配置游戏，再运行 `python3 configure-gm.py --directory . --port PORT`，生成本机私有密钥。compose.gm.example.yaml 为显式启用的附加配置，须与基础 Compose 配套使用。配置脚本仅写本地文件，不启动服务。

不要分享 .env、.secrets、世界身份或工具真实配置，也不要将管理接口直接暴露到公网。实际启用与角色修改需单独授权，遵守维护锁、bot 租约、零玩家、正常退出存档、完整备份和成套回退。

身份、目录和离线模拟通过不等于真实角色修改、存档与重登通过。完整组合状态保持 pending_runtime_validation。
