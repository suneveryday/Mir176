# 参与维护

先阅读 [维护规则](AGENTS.md)、[代码规范](docs/CODING_STANDARDS.md) 与 [兼容性](docs/COMPATIBILITY.md)。GitHub 项目名为 Mir176，程序和协议中的项目标识为 Mir2Classic176。

从 main 创建 `codex/<主题>` 分支。保持改动聚焦，保留文件编码和第三方许可；不提交运行配置、账号、玩家数据库、日志、工具链或素材二进制。

```sh
git config core.hooksPath .githooks
python3 tools/check-contract.py
python3 tools/check-privacy.py
python3 tools/check-privacy.py --history
```

提交说明应包含问题、修改后的行为、验证与实际限制。受保护实现变化须审查客户端、GM 与 bot，先提交子项目，再更新 ecosystem.lock.json 并执行组合检查。CI 中的源码、隐私、打包和离线回归检查保持启用。

Git 管理与代码检查不授予 Docker 部署或真实角色操作权限。完整运行组合验收另行记录，未完成时保持 pending_runtime_validation。
