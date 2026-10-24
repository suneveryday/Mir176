using GameSrv.Services;

namespace GameSrv.Word.Threads
{
    public class CharacterDataProcessor : TimerScheduledService
    {

        private readonly object UserCriticalSection = new object();

        public CharacterDataProcessor() : base(TimeSpan.FromMilliseconds(500), "StorageProcessor")
        {

        }

        public override void Initialize(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        protected override void Startup(CancellationToken stoppingToken)
        {
            LogService.Info("玩家数据数据线程启动...");
        }

        protected override void Stopping(CancellationToken stoppingToken)
        {
            LogService.Info("玩家数据数据线程停止...");
        }

        protected override Task ExecuteInternal(CancellationToken stoppingToken)
        {
            const string sExceptionMsg = "[Exception] StorageProcessor::ExecuteInternal";
            try
            {
                M2Share.FrontEngine.ProcessGameDate();
                if (GameShare.DataServer.IsConnected) ProcessReadStorage();
                ProcessSaveStorage();
            }
            catch (Exception ex)
            {
                LogService.Error(sExceptionMsg);
                LogService.Error(ex.StackTrace);
            }
            return Task.CompletedTask;
        }

        public async Task FlushPendingAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (M2Share.FrontEngine.GetSaveRcdList().Count > 0)
            {
                M2Share.FrontEngine.ProcessGameDate();
                ProcessSaveStorage();
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException("停服时角色存档仍未获得数据库确认，不能宣称保存完成。");
                await Task.Delay(100);
            }
        }

        private static void ProcessSaveStorage()
        {
            IList<SavePlayerRcd> saveRcdTempList = M2Share.FrontEngine.GetTempSaveRcdList();
            for (int i = 0; i < saveRcdTempList.Count; i++)
            {
                SavePlayerRcd saveRcd = saveRcdTempList[i];
                if (saveRcd == null)
                {
                    continue;
                }
                if (saveRcd.IsSaveing)
                {
                    continue;
                }
                saveRcd.IsSaveing = true;
                if (!PlayerDataService.SaveCharacterData(saveRcd, ref saveRcd.QueryId))
                {
                    saveRcd.IsSaveing = false;
                    saveRcd.ReTryCount++;
                }
            }
            M2Share.FrontEngine.ClearSaveRcdTempList();
            PlayerDataService.ProcessSaveQueue();
        }

        private void ProcessReadStorage()
        {
            bool boReTryLoadDb = false;
            IList<LoadDBInfo> loadRcdTempList = M2Share.FrontEngine.GetLoadTempList();
            for (int i = 0; i < loadRcdTempList.Count; i++)
            {
                LoadDBInfo loadDbInfo = loadRcdTempList[i];
                if (loadDbInfo.SessionID == 0)
                {
                    continue;
                }
                if (!LoadCharacterData(loadDbInfo, ref boReTryLoadDb))
                {
                    // Soft logout retains the certificate. Wait for the previous immutable
                    // snapshot's durable ACK before loading; pending is not a failed login.
                    if (boReTryLoadDb && HUtil32.GetTickCount() - loadDbInfo.NewUserTick < 60000)
                    {
                        M2Share.FrontEngine.AddToLoadRcdList(loadDbInfo);
                    }
                    else
                    {
                        M2Share.NetChannel.CloseUser(loadDbInfo.GateIdx, loadDbInfo.SocketId);
                        LogService.Warn("角色载入失败或等待存档超时，关闭该连接。");
                    }
                }
            }
            M2Share.FrontEngine.ClearLoadRcdTempList();
            PlayerDataService.ProcessQueryQueue();
        }

        private static bool LoadCharacterData(LoadDBInfo loadUser, ref bool reTry)
        {
            int queryId = 0;
            bool result = false;
            reTry = false;
            if (M2Share.FrontEngine.InSaveRcdList(loadUser.ChrName) || (SystemShare.WorldEngine is GameSrv.Word.WorldServer world && world.GetPlayObjectEx(loadUser.ChrName) != null))
            {
                reTry = true;// 反回TRUE,则重新加入队列
                return false;
            }
            /*if ((SystemShare.WorldEngine is GameSrv.Word.WorldServer world && world.GetPlayObjectEx(loadUser.ChrName) != null))
            {
                SystemShare.WorldEngine.KickPlayObjectEx(loadUser.ChrName);
                boReTry = true;// 反回TRUE,则重新加入队列
                return false;
            }*/
            if (!PlayerDataService.QueryCharacterData(loadUser.Account, loadUser.ChrName, loadUser.sIPaddr, ref queryId, loadUser.SessionID))
            {
                M2Share.NetChannel.SendOutConnectMsg(loadUser.GateIdx, loadUser.SocketId, loadUser.GSocketIdx); // 获取数据失败,发送连接断开消息
            }
            else
            {
                UserOpenInfo userOpenInfo = new UserOpenInfo
                {
                    ChrName = loadUser.ChrName,
                    LoadUser = loadUser,
                    HumanRcd = null,
                    QueryId = queryId
                };
                SystemShare.WorldEngine.AddUserOpenInfo(userOpenInfo);
                result = true;
            }
            return result;
        }
    }
}