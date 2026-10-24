# Actor notification ordering regression

Run `ActorMessageOrder.csproj` with `ServerBin` pointing at a complete GameSrv build. It exercises the actual ordinary-message entry points, broadcast/view scan, explicit priorities, delayed delivery and concurrent producers using synthetic in-memory actors only. The candidate must pass nine checks, including a monster pushed before moving outside an observer's view. With `--expect-old`, an unchanged baseline must reproduce delivery of `RM_DISAPPEAR` before the older `RM_PUSH`.

```sh
dotnet run --project tests/actor-message-order/ActorMessageOrder.csproj -p:ServerBin="<baseline GameSrv directory>" -- --expect-old
dotnet run --project tests/actor-message-order/ActorMessageOrder.csproj -p:ServerBin="<candidate GameSrv directory>"
python3 tests/actor-message-order/build-client-regression.py --out .build/actor-message-order/client-routing.pas
fpc -Mdelphi .build/actor-message-order/client-routing.pas
.build/actor-message-order/client-routing
```

The Pascal generator extracts the current PC `SM_HIDE` and actor-creation routing branches without changing their bodies. Minimal scene/actor doubles replace storage and graphics. Two checks show that a late `SM_BACKSTEP` recreates a removed monster and that push-before-hide leaves no stale actor. This validates message routing, not the complete Windows application or its animation loop.

No game login, database access, process attachment or deployment is performed. Original incident evidence remains private. Reproducing a matching mechanism does not prove the uncaptured message order of a particular live incident. The complete six-service and MySQL plugin build plus PC/Docker gameplay acceptance remain release requirements.

PC路由生成器已迁到独立项目 [Mir176Client/tests/actor-message-order](https://github.com/suneveryday/Mir176Client/tree/main/tests/actor-message-order)；本仓库保留服务端顺序回归。
