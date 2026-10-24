# Character state regression

This suite uses the current compiled game and storage assemblies. It covers all 255 levels for all three classes, administrator-list parsing and atomic reload, ordinary-player isolation, credit-point bounds, the signed-byte credit migration, and real MySQL save/reload of all fourteen item attributes across sparse equipment, bag and warehouse slots. Same-name items are identified by instance ID. It also checks relocation, a second save and reload, missing legacy attributes and duplicate-record rejection.

Build the six services and MySQL plugin first. Build `CharacterState.csproj` with `ServerBin` pointing to the GameSrv output and `StorageBin` pointing to the MySQL plugin output. Copy the corresponding runtime DLLs beside the test executable.

Run only against a disposable database created from `schema.sql`. `MIR2_TEST_DB` must identify a synthetic database named `repair_test` on the isolated Docker-network alias `testdb`; the executable refuses other targets. Do not publish a database port. Pass `server/src/Storeages/DBSvr.Storage.MySQL/Migrations/20261018-credit-range.sql` as the single executable argument. Keep generated credentials, logs and database files outside Git, and remove the test container, volume and network afterwards.

The suite does not log into a game server or execute operations on an existing player. Passing it is not Windows gameplay or Docker deployment acceptance.
