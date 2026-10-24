using MySqlConnector;
using OpenMir2.Enums;
using OpenMir2.Extensions;
using System.Data;
using System.Text.Json;

namespace GameSrv.DB
{
    public class MySqlDB : IDataSource
    {
        private IDbConnection _dbConnection;

        public int LoadItemsDB()
        {
            int result = -1;
            const string sSQLString = "SELECT * FROM stditems ORDER BY ID";
            var staged = new M2Server.Items.GameItemSystem();
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);
                if (!Open())
                {
                    return result;
                }
                using (IDataReader dr = Query(sSQLString))
                {
                    while (dr.Read())
                    {
                        StdItem stdItem = new StdItem();
                        int idx = ReadItemInteger(dr, "ID", 1, ushort.MaxValue, false);
                        if (idx <= staged.ItemCount)
                            throw new InvalidDataException("物品ID必须递增且唯一。");
                        while (staged.ItemCount < idx - 1) staged.AddItem(new StdItem { Name = string.Empty });
                        stdItem.Name = dr.GetString("NAME");
                        if (string.IsNullOrWhiteSpace(stdItem.Name))
                            throw new InvalidDataException($"物品{idx}名称为空。");
                        M2Server.Classic.ClassicCatalog.RequireItem(idx, stdItem.Name);
                        stdItem.StdMode = (byte)ReadItemInteger(dr, "StdMode", byte.MinValue, byte.MaxValue, true);
                        stdItem.Shape = (byte)ReadItemInteger(dr, "SHAPE", byte.MinValue, byte.MaxValue, true);
                        stdItem.Weight = (byte)ReadLegacyItemNumber(dr, "WEIGHT", idx, stdItem.Name, byte.MaxValue);
                        stdItem.AniCount = (byte)ReadItemInteger(dr, "ANICOUNT", byte.MinValue, byte.MaxValue, true);
                        stdItem.SpecialPwr = (sbyte)ReadItemInteger(dr, "SOURCE", sbyte.MinValue, sbyte.MaxValue, false);
                        stdItem.ItemDesc = (byte)ReadItemInteger(dr, "RESERVED", byte.MinValue, byte.MaxValue, true);
                        stdItem.Looks = (ushort)ReadItemInteger(dr, "IMGINDEX", ushort.MinValue, ushort.MaxValue, true);
                        stdItem.DuraMax = (ushort)ReadLegacyItemNumber(dr, "DURAMAX", idx, stdItem.Name, ushort.MaxValue);
                        stdItem.AC = HUtil32.MakeWord((ushort)ReadItemInteger(dr, "AC", ushort.MinValue, ushort.MaxValue, true), (ushort)ReadItemInteger(dr, "ACMAX", ushort.MinValue, ushort.MaxValue, true));
                        stdItem.MAC = HUtil32.MakeWord((ushort)ReadItemInteger(dr, "MAC", ushort.MinValue, ushort.MaxValue, true), (ushort)ReadItemInteger(dr, "MACMAX", ushort.MinValue, ushort.MaxValue, true));
                        stdItem.DC = HUtil32.MakeWord((ushort)ReadItemInteger(dr, "DC", ushort.MinValue, ushort.MaxValue, true), (ushort)ReadItemInteger(dr, "DCMAX", ushort.MinValue, ushort.MaxValue, true));
                        stdItem.MC = HUtil32.MakeWord((ushort)ReadItemInteger(dr, "MC", ushort.MinValue, ushort.MaxValue, true), (ushort)ReadItemInteger(dr, "MCMAX", ushort.MinValue, ushort.MaxValue, true));
                        stdItem.SC = HUtil32.MakeWord((ushort)ReadItemInteger(dr, "SC", ushort.MinValue, ushort.MaxValue, true), (ushort)ReadItemInteger(dr, "SCMAX", ushort.MinValue, ushort.MaxValue, true));
                        stdItem.Need = (byte)ReadItemInteger(dr, "NEED", byte.MinValue, byte.MaxValue, true);
                        stdItem.NeedLevel = (byte)ReadLegacyItemNumber(dr, "NEEDLEVEL", idx, stdItem.Name, byte.MaxValue);
                        stdItem.NeedIdentify = 0;
                        stdItem.Price = (int)ReadItemInteger(dr, "PRICE", int.MinValue, int.MaxValue, true);
                        stdItem.Stock = (int)ReadItemInteger(dr, "STOCK", int.MinValue, int.MaxValue, true);
                        stdItem.AtkSpd = (byte)ReadItemInteger(dr, "ATKSPD", byte.MinValue, byte.MaxValue, true);
                        stdItem.Agility = (byte)ReadItemInteger(dr, "AGILITY", byte.MinValue, byte.MaxValue, true);
                        stdItem.Accurate = (byte)ReadItemInteger(dr, "ACCURATE", byte.MinValue, byte.MaxValue, true);
                        stdItem.MgAvoid = (byte)ReadItemInteger(dr, "MGAVOID", byte.MinValue, byte.MaxValue, true);
                        stdItem.Strong = (byte)ReadItemInteger(dr, "STRONG", byte.MinValue, byte.MaxValue, true);
                        stdItem.Undead = (byte)ReadItemInteger(dr, "UNDEAD", byte.MinValue, byte.MaxValue, true);
                        stdItem.HpAdd = (int)ReadItemInteger(dr, "HPADD", int.MinValue, int.MaxValue, true);
                        stdItem.MpAdd = (int)ReadItemInteger(dr, "MPADD", int.MinValue, int.MaxValue, true);
                        stdItem.ExpAdd = (int)ReadItemInteger(dr, "EXPADD", int.MinValue, int.MaxValue, true);
                        stdItem.EffType1 = (byte)ReadItemInteger(dr, "EFFTYPE1", byte.MinValue, byte.MaxValue, true);
                        stdItem.EffRate1 = (byte)ReadItemInteger(dr, "EFFRATE1", byte.MinValue, byte.MaxValue, true);
                        stdItem.EffValue1 = (byte)ReadItemInteger(dr, "EFFVALUE1", byte.MinValue, byte.MaxValue, true);
                        stdItem.EffType2 = (byte)ReadItemInteger(dr, "EFFTYPE2", byte.MinValue, byte.MaxValue, true);
                        stdItem.EffRate2 = (byte)ReadItemInteger(dr, "EFFRATE2", byte.MinValue, byte.MaxValue, true);
                        stdItem.EffValue2 = (byte)ReadItemInteger(dr, "EFFVALUE2", byte.MinValue, byte.MaxValue, true);
                        stdItem.Slowdown = (byte)ReadItemInteger(dr, "SLOWDOWN", byte.MinValue, byte.MaxValue, true);
                        stdItem.Tox = (byte)ReadItemInteger(dr, "TOX", byte.MinValue, byte.MaxValue, true);
                        stdItem.ToxAvoid = (byte)ReadItemInteger(dr, "TOXAVOID", byte.MinValue, byte.MaxValue, true);
                        stdItem.UniqueItem = (byte)ReadItemInteger(dr, "UNIQUEITEM", byte.MinValue, byte.MaxValue, true);
                        stdItem.OverlapItem = (byte)ReadItemInteger(dr, "OVERLAPITEM", byte.MinValue, byte.MaxValue, true);
                        stdItem.Light = (byte)ReadItemInteger(dr, "LIGHT", byte.MinValue, byte.MaxValue, true);
                        stdItem.ItemType = (byte)ReadItemInteger(dr, "ITEMTYPE", byte.MinValue, byte.MaxValue, true);
                        stdItem.ItemSet = (ushort)ReadItemInteger(dr, "ITEMSET", ushort.MinValue, ushort.MaxValue, true);
                        stdItem.Reference = dr.GetString("REFERENCE");
                        staged.AddItem(stdItem);
                    }
                }
                if (staged.ItemCount == 0) throw new InvalidDataException("物品目录为空。");
                M2Share.GameLogGold = M2Share.GetGameLogItemNameList(Grobal2.StringGoldName) == 1;
                // M2Share.GameLogHumanDie = M2Share.GetGameLogItemNameList(CommandHelp.HumanDieEvent) == 1;
                M2Share.GameLogGameGold = M2Share.GetGameLogItemNameList(SystemShare.Config.GameGoldName) == 1;
                M2Share.GameLogGamePoint = M2Share.GetGameLogItemNameList(SystemShare.Config.GamePointName) == 1;
                // Publish once; readers keep the previous complete catalog on any failure.
                SystemShare.ItemSystem = staged;
                result = 1;
            }
            catch (Exception ex)
            {
                LogService.Error($"物品目录加载失败，保留原目录: {ex.Message}");
                return -1;
            }
            finally
            {
                try { Close(); }
                finally { HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection); }
            }
            return result;
        }

        private static int ReadItemInteger(IDataReader reader, string field, int minimum, int maximum, bool allowNull)
        {
            object raw = reader[field];
            if (raw is DBNull)
            {
                if (allowNull) return minimum == int.MinValue ? -1 : 0;
                throw new InvalidDataException($"物品字段{field}不能为空。");
            }
            long value = Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture);
            if (value < minimum || value > maximum)
                throw new InvalidDataException($"物品字段{field}={value}超出范围{minimum}..{maximum}。");
            return (int)value;
        }

        private static int ReadLegacyItemNumber(IDataReader reader, string field, int id, string name, int maximum)
        {
            // Exact records from the existing catalog previously loaded as zero. Keep those values
            // with an explicit warning; all new or changed out-of-range records fail validation.
            object raw = reader[field];
            if (raw is not DBNull)
            {
                long value = Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture);
                bool legacy = (id == 63 && name == "金箱子" && field == "WEIGHT" && value == 500) ||
                    (id == 262 && name == "开天战廓" && field == "DURAMAX" && value == 90000) ||
                    (id == 263 && name == "乾坤" && field == "DURAMAX" && value == 90000) ||
                    (id == 264 && name == "玄灵青蟒" && field == "DURAMAX" && value == 90000) ||
                    (id == 439 && name == "玛法内功心法" && field == "NEEDLEVEL" && value == 100000) ||
                    (id == 975 && name == "明王头盔" && field == "DURAMAX" && value == 80000);
                if (legacy)
                {
                    LogService.Warn($"旧物品兼容记录 ID={id} {name} {field}={value}，保留原运行值0；内容数值需单独确定。");
                    return 0;
                }
            }
            return ReadItemInteger(reader, field, 0, maximum, true);
        }

        public int LoadMagicDB()
        {
            const string sSQLString = "select * from magics";
            int result = -1;
            HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);
            try
            {
                var staged = new List<MagicInfo>();
                if (!Open())
                {
                    return result;
                }
                using IDataReader dr = Query(sSQLString);
                while (dr.Read())
                {
                    MagicInfo magic = new MagicInfo
                    {
                        MagicId = dr.GetUInt16("MagId"),
                        MagicName = dr.GetString("MagName"),
                        EffectType = (byte)dr.GetInt32("EffectType"),
                        Effect = (byte)dr.GetInt32("Effect"),
                        Spell = dr.GetUInt16("Spell"),
                        Power = dr.GetUInt16("Power"),
                        MaxPower = dr.GetUInt16("MaxPower"),
                        Job = (byte)dr.GetInt32("Job")
                    };
                    magic.TrainLevel[0] = (byte)dr.GetInt32("NeedL1");
                    magic.TrainLevel[1] = (byte)dr.GetInt32("NeedL2");
                    magic.TrainLevel[2] = (byte)dr.GetInt32("NeedL3");
                    magic.TrainLevel[3] = (byte)dr.GetInt32("NeedL3");
                    magic.MaxTrain[0] = dr.GetInt32("L1Train");
                    magic.MaxTrain[1] = dr.GetInt32("L2Train");
                    magic.MaxTrain[2] = dr.GetInt32("L3Train");
                    magic.MaxTrain[3] = magic.MaxTrain[2];
                    magic.TrainLv = 3;
                    magic.DelayTime = dr.GetInt32("Delay");
                    magic.DefSpell = (byte)dr.GetInt32("DefSpell");
                    magic.DefPower = (byte)dr.GetInt32("DefPower");
                    magic.DefMaxPower = (byte)dr.GetInt32("DefMaxPower");
                    magic.Desc = dr.GetString("Descr");
                    if (magic.MagicId > 0)
                    {
                        M2Server.Classic.ClassicCatalog.RequireSkill(magic.MagicId, magic.MagicName);
                        staged.Add(magic);
                    }
                    else
                    {
                        magic = null;
                    }
                }
                if (staged.Count != 33) throw new InvalidDataException("Classic skill catalog must contain 33 skills.");
                SystemShare.WorldEngine.SwitchMagicList();
                foreach (var magic in staged) SystemShare.WorldEngine.AddMagicList(magic);
                result = 1;
            }
            catch (Exception ex)
            {
                result = -1;
                LogService.Error("Classic skill loading failed: " + ex.GetType().Name);
            }
            finally
            {
                Close();
                HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);
            }
            return result;
        }

        public int LoadMonsterDB()
        {
            int result = -1;
            const string sSQLString = "select * from monsters";
            HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);
            try
            {
                var staged = new List<MonsterInfo>();
                if (!Open())
                {
                    return result;
                }
                using IDataReader dr = Query(sSQLString);
                while (dr.Read())
                {
                    MonsterInfo monster = new MonsterInfo
                    {
                        ItemList = new List<MonsterDropItem>(),
                        Name = dr.GetString("NAME").Trim(),
                        Race = (byte)dr.GetInt32("Race"),
                        RaceImg = (byte)dr.GetInt32("RaceImg"),
                        Appr = dr.GetUInt16("Appr"),
                        Level = dr.GetByte("Lvl"),
                        btLifeAttrib = (byte)dr.GetInt32("Undead"),
                        CoolEye = dr.GetByte("CoolEye"),
                        Exp = dr.GetInt32("Exp")
                    };
                    // 城门或城墙的状态跟HP值有关，如果HP异常，将导致城墙显示不了
                    if (monster.Race == ActorRace.SabukWall || monster.Race == ActorRace.SabukDoor)
                    {
                        monster.HP = dr.GetUInt16("HP");// 如果为城墙或城门由HP不加倍
                    }
                    else
                    {
                        monster.HP = (ushort)HUtil32.Round(dr.GetInt32("HP") * (SystemShare.Config.MonsterPowerRate / 10.0));
                    }
                    monster.MP = (ushort)HUtil32.Round(dr.GetInt32("MP") * (SystemShare.Config.MonsterPowerRate / 10.0));
                    monster.AC = (ushort)HUtil32.Round(dr.GetInt32("AC") * (SystemShare.Config.MonsterPowerRate / 10.0));
                    monster.MAC = (ushort)HUtil32.Round(dr.GetInt32("MAC") * (SystemShare.Config.MonsterPowerRate / 10.0));
                    monster.DC = (ushort)HUtil32.Round(dr.GetInt32("DC") * (SystemShare.Config.MonsterPowerRate / 10.0));
                    monster.MaxDC = (ushort)HUtil32.Round(dr.GetInt32("DCMAX") * (SystemShare.Config.MonsterPowerRate / 10.0));
                    monster.MC = (ushort)HUtil32.Round(dr.GetInt32("MC") * (SystemShare.Config.MonsterPowerRate / 10.0));
                    monster.SC = (ushort)HUtil32.Round(dr.GetInt32("SC") * (SystemShare.Config.MonsterPowerRate / 10.0));
                    monster.Speed = dr.GetByte("SPEED");
                    monster.HitPoint = dr.GetByte("HIT");
                    monster.WalkSpeed = (ushort)HUtil32._MAX(200, dr.GetInt32("WALK_SPD"));
                    monster.WalkStep = (ushort)HUtil32._MAX(1, dr.GetInt32("WalkStep"));
                    monster.WalkWait = (ushort)dr.GetInt32("WalkWait");
                    monster.AttackSpeed = (ushort)dr.GetInt32("ATTACK_SPD");
                    if (monster.WalkSpeed < 200)
                    {
                        monster.WalkSpeed = 200;
                    }
                    if (monster.AttackSpeed < 200)
                    {
                        monster.AttackSpeed = 200;
                    }
                    monster.ItemList = null;
                    GameShare.LocalDb.LoadMonitems(monster.Name, ref monster.ItemList);
                    //if (SystemShare.WorldEngine.MonsterList.ContainsKey(monster.Name))
                    //{
                    //    LogService.Warn($"怪物名称[{monster.Name}]重复,请确认数据是否正常.");
                    //    continue;
                    //}
                    //if (monster.ItemList == null || monster.ItemList.Count <= 0)
                    //{
                    //    LogService.Debug($"怪物[{monster.Name}]爆率文件为空.");
                    //}
                    M2Server.Classic.ClassicCatalog.RequireMonster(monster.Name);
                    staged.Add(monster);
                }
                if (staged.Count < 100) throw new InvalidDataException("Classic monster catalog is incomplete.");
                SystemShare.WorldEngine.ClearMonsterList();
                foreach (var monster in staged) SystemShare.WorldEngine.AddMonsterList(monster);
                result = 1;
            }
            catch (Exception ex)
            {
                result = -1;
                LogService.Error("Classic monster loading failed: " + ex.GetType().Name);
            }
            finally
            {
                Close();
                HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);
            }
            return result;
        }

        /// <summary>
        /// 加载寄售系统数据
        /// </summary>
        public void LoadSellOffItemList()
        {
            if (!Open())
            {
                LogService.Error("读取物品寄售列表失败.");
                return;
            }
            try
            {
                const string sSQLString = "select * from goldsales";
                using IDataReader dr = Query(sSQLString);
                while (dr.Read())
                {
                    string sDealChrName = dr.GetString("DealCharName");
                    string sBuyChrName = dr.GetString("BuyCharName");
                    DateTime dSellDateTime = dr.GetDateTime("SellDateTime");
                    byte nState = dr.GetByte("State");
                    int nSellGold = dr.GetInt32("SellGold");
                    string sUseItems = dr.GetString("UseItems");
                    if ((!string.IsNullOrEmpty(sDealChrName)) && (!string.IsNullOrEmpty(sBuyChrName)) && (nState < 4))
                    {
                        DealOffInfo DealOffInfo = new DealOffInfo();
                        DealOffInfo.sDealChrName = sDealChrName;
                        DealOffInfo.sBuyChrName = sBuyChrName;
                        DealOffInfo.dSellDateTime = dSellDateTime;
                        DealOffInfo.nSellGold = nSellGold;
                        DealOffInfo.UseItems = JsonSerializer.Deserialize<UserItem[]>(sUseItems);
                        DealOffInfo.Flag = nState;
                        M2Share.SellOffItemList.Add(DealOffInfo);
                    }
                }
                LogService.Info($"读取物品寄售列表成功...[{M2Share.SellOffItemList.Count}]");
            }
            finally
            {
                Close();
            }
        }

        private const string SaveSellItemSql = "INSERT INTO goldsales (DealCharName, BuyCharName, SellDateTime, State, SellGold,UseItems) values (@DealChrName, @BuyChrName, @SellDateTime, @State, @SellGold,@UseItems)";

        /// <summary>
        /// 保存寄售系统数据
        /// </summary>
        public void SaveSellOffItemList()
        {
            if (!Open())
            {
                LogService.Error("保存物品寄售数据失败.");
                return;
            }
            DealOffInfo DealOffInfo;
            const string sSQLString = "delete from goldsales";
            using MySqlTransaction transaction = ((MySqlConnection)_dbConnection).BeginTransaction();
            try
            {
                using MySqlCommand delete = new MySqlCommand(sSQLString, (MySqlConnection)_dbConnection, transaction);
                delete.ExecuteNonQuery();
                if (M2Share.SellOffItemList.Count > 0)
                {
                    for (int i = 0; i < M2Share.SellOffItemList.Count; i++)
                    {
                        DealOffInfo = M2Share.SellOffItemList[i];
                        if (DealOffInfo != null)
                        {
                            MySqlCommand command = new MySqlCommand();
                            command.Connection = (MySqlConnection)_dbConnection;
                            command.Transaction = transaction;
                            command.CommandText = SaveSellItemSql;
                            command.Parameters.AddWithValue("@DealChrName", DealOffInfo.sDealChrName);
                            command.Parameters.AddWithValue("@BuyChrName", DealOffInfo.sBuyChrName);
                            command.Parameters.AddWithValue("@SellDateTime", DealOffInfo.dSellDateTime);
                            command.Parameters.AddWithValue("@State", DealOffInfo.Flag);
                            command.Parameters.AddWithValue("@SellGold", DealOffInfo.nSellGold);
                            command.Parameters.AddWithValue("@UseItems", JsonSerializer.Serialize(DealOffInfo.UseItems));
                            command.ExecuteNonQuery();
                        }
                    }
                }
                transaction.Commit();
                LogService.Info($"保存物品寄售列表成功...[{M2Share.SellOffItemList.Count}]");
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
            finally
            {
                Close();
            }
        }

        public int LoadUpgradeWeaponRecord(string sNPCName, IList<WeaponUpgradeInfo> DataList)
        {
            throw new NotSupportedException("武器升级队列由角色存档事务恢复，不允许独立NPC队列读取。");
        }

        public int SaveUpgradeWeaponRecord(string sNPCName, IList<WeaponUpgradeInfo> DataList)
        {
            throw new NotSupportedException("武器升级队列必须与角色装备、背包及金币同事务保存。");
        }

        private IDataReader Query(string sSQLString)
        {
            MySqlCommand command = new MySqlCommand();
            command.Connection = (MySqlConnection)_dbConnection;
            command.CommandText = sSQLString;
            return command.ExecuteReader();
        }

        private int Execute(string sSQLString)
        {
            MySqlCommand command = new MySqlCommand();
            command.Connection = (MySqlConnection)_dbConnection;
            command.CommandText = sSQLString;
            return command.ExecuteNonQuery();
        }

        private bool Open()
        {
            if (_dbConnection == null)
            {
                try
                {
                    _dbConnection = new MySqlConnection(SystemShare.Config.ConnctionString);
                    _dbConnection.Open();
                    return true;
                }
                catch (Exception e)
                {
                    LogService.Error("游戏资料数据库连接失败。");
                    LogService.Error(e.StackTrace);
                    return false;
                }
            }
            else if (_dbConnection.State == ConnectionState.Closed)
            {
                _dbConnection = new MySqlConnection(SystemShare.Config.ConnctionString);
                _dbConnection.Open();
            }
            return true;
        }

        private void Close()
        {
            if (_dbConnection != null)
            {
                _dbConnection.Close();
                _dbConnection.Dispose();
            }
        }
    }
}
