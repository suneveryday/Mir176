# 经典UI与仓库正确性回归

`ClassicUi.csproj` 链接本项目完整重建的真实服务程序集，以内存 `INetChannel` 截获实际消息字节，不连接运行数据库或真实角色。覆盖空仓库的32字节无正文回包、merchant和分页信息、50/1条分段不重复、非法目录条目仍完成请求，以及扩展穿戴拒绝回执和自由加点不变更遗留字段。

`merchant-policy.py` 核对药品、书籍、食品等商人不提供修理，特殊修理只在明确采用的铁匠/修理商人开放，并保留仓库和升级武器脚本的独立处理。该白名单属于工程采用，不是恢复的2003脚本。

`build-client-regression.py --out <隔离目录>` 从现行 `ClassicClientPolicy.pas` 生成语义相同的UTF-8测试副本及实际消息ID夹具，在Free Pascal中分别编译默认分支和 `UI_0508` 分支。检查四页导航、经典穿戴边界、右手勋章/照明与符毒保留，以及后期命令拒绝和普通操作保留。编码转换仅用于可移植测试，不修改Windows源码字节，也不替代官方Delphi完整构建或实机验证。

界面绘制、点击区域、物品使用和保存重登仍须通过对应Windows客户端实测；Docker部署与组合发布保持独立门禁。

PC界面策略生成器已迁到 [Mir176Client/tests/classic-ui](https://github.com/suneveryday/Mir176Client/tree/main/tests/classic-ui)；本目录保留服务端策略回归。
