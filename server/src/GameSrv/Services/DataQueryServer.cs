using TcpClient = TouchSocket.Sockets.TcpClient;

namespace GameSrv.Services
{
    /// <summary>
    /// 玩家数据读写服务
    /// </summary>
    public class DataQueryServer
    {
        private readonly TcpClient _tcpClient;
        private readonly PlayerDataFrameReader _frameReader = new PlayerDataFrameReader();
        private bool SocketWorking { get; set; }

        public DataQueryServer()
        {
            _tcpClient = new TcpClient();
            _tcpClient.Connected = DataSocketConnected;
            _tcpClient.Disconnected = DataSocketDisconnected;
            _tcpClient.Received = DataSocketRead;
            SocketWorking = false;

        }

        public void Initialize()
        {
            _tcpClient.Setup(new TouchSocketConfig()
            .SetRemoteIPHost(new IPHost(IPAddress.Parse(SystemShare.Config.sDBAddr), SystemShare.Config.nDBPort))
            .ConfigureContainer(a =>
            {
                a.AddConsoleLogger();
            }));
        }

        public async Task Start()
        {
            try
            {
                if (_tcpClient.Online)
                {
                    return;
                }
                await _tcpClient.ConnectAsync();
            }
            catch (TimeoutException)
            {
                LogService.Error($"链接数据库服务器[{SystemShare.Config.sDBAddr}:{SystemShare.Config.nDBPort}]超时...");
            }
            catch (Exception)
            {
                LogService.Error($"链接数据库服务器[{SystemShare.Config.sDBAddr}:{SystemShare.Config.nDBPort}]失败...");
            }
        }

        public void Stop()
        {
            _tcpClient.Close();
        }

        public bool IsConnected => _tcpClient.Online;

        public bool SendRequest<T>(int queryId, ServerRequestMessage message, T packet)
        {
            if (!_tcpClient.Online)
            {
                return false;
            }
            ServerRequestData requestPacket = new ServerRequestData();
            requestPacket.QueryId = queryId;
            requestPacket.Message = EDCode.EncodeBuffer(SerializerUtil.Serialize(message));
            requestPacket.Packet = EDCode.EncodeBuffer(SerializerUtil.Serialize(packet));
            int signId = HUtil32.MakeLong((ushort)(queryId ^ 170), (ushort)(requestPacket.Message.Length + requestPacket.Packet.Length + ServerDataPacket.FixedHeaderLen));
            requestPacket.Sign = EDCode.EncodeBuffer(BitConverter.GetBytes(signId));
            SendMessage(SerializerUtil.Serialize(requestPacket));
            return true;
        }

        private void SendMessage(byte[] sendBuffer)
        {
            _tcpClient.Send(PlayerDataFraming.Encode(sendBuffer));
        }

        private Task DataSocketDisconnected(ITcpClientBase sender, DisconnectEventArgs e)
        {
            lock (_frameReader) _frameReader.Reset();
            LogService.Error("数据库服务器[" + sender.GetIPPort() + "]断开连接...");
            return Task.CompletedTask;
        }

        private Task DataSocketConnected(ITcpClient client, ConnectedEventArgs e)
        {
            LogService.Info("数据库服务器[" + client.RemoteIPHost + "]连接成功...");
            return Task.CompletedTask;
        }

        private Task DataSocketRead(TcpClient sender, ReceivedDataEventArgs e)
        {
            HUtil32.EnterCriticalSection(M2Share.UserDBCriticalSection);
            try
            {
                List<byte[]> completed;
                lock (_frameReader) completed = _frameReader.Feed(e.ByteBlock.Buffer.AsSpan(0, e.ByteBlock.Len));
                foreach (byte[] payload in completed)
                {
                    SocketWorking = true;
                    ProcessServerData(SerializerUtil.Deserialize<ServerRequestData>(payload));
                }
            }
            catch (Exception exception)
            {
                LogService.Error(exception);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.UserDBCriticalSection);
            }
            return Task.CompletedTask;
        }

        private void ProcessServerData(ServerRequestData responsePacket)
        {
            try
            {
                if (!SocketWorking)
                {
                    return;
                }

                if (responsePacket != null)
                {
                    int respCheckCode = responsePacket.QueryId;
                    int nLen = responsePacket.Message.Length + responsePacket.Packet.Length + ServerDataPacket.FixedHeaderLen;
                    if (nLen >= 12)
                    {
                        int queryId = HUtil32.MakeLong((ushort)(respCheckCode ^ 170), (ushort)nLen);
                        if (respCheckCode <= 0 || responsePacket.Sign == null || responsePacket.Sign.Length == 0)
                        {
                            SystemShare.Config.LoadDBErrorCount++;
                            return;
                        }
                        // The high word contains a byte length; its sign bit is valid for large replies.
                        byte[] signBuff = EDCode.DecodeBuff(responsePacket.Sign);
                        if (signBuff.Length == sizeof(int) && BitConverter.ToInt32(signBuff) == queryId)
                        {
                            PlayerDataService.Enqueue(respCheckCode, responsePacket);
                        }
                        else
                        {
                            SystemShare.Config.LoadDBErrorCount++;
                        }
                    }
                }
                else
                {
                    LogService.Error("错误的封包数据");
                }
            }
            finally
            {
                SocketWorking = false;
            }
        }
    }
}