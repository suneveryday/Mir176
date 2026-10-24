namespace DBSrv.Storage.MySQL
{
    public partial class PlayDataStorage
    {
        private static void SaveItemSnapshot(StorageContext context, int playerId, string table, OpenMir2.Packets.ServerPackets.ServerUserItem[] items)
        {
            using var delete = context.CreateCommand();
            delete.CommandText = $"DELETE FROM {table} WHERE PlayerId=@PlayerId";
            delete.Parameters.AddWithValue("@PlayerId", playerId);
            delete.ExecuteNonQuery();
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                bool present = item != null && item.Index != 0 && item.MakeIndex > 0;
                using var insert = context.CreateCommand();
                insert.CommandText = $"INSERT INTO {table}(PlayerId,Position,MakeIndex,StdIndex,Dura,DuraMax) VALUES(@PlayerId,@Position,@MakeIndex,@StdIndex,@Dura,@DuraMax)";
                insert.Parameters.AddWithValue("@PlayerId", playerId);
                insert.Parameters.AddWithValue("@Position", i);
                insert.Parameters.AddWithValue("@MakeIndex", present ? item.MakeIndex : 0);
                insert.Parameters.AddWithValue("@StdIndex", present ? item.Index : 0);
                insert.Parameters.AddWithValue("@Dura", present ? item.Dura : 0);
                insert.Parameters.AddWithValue("@DuraMax", present ? item.DuraMax : 0);
                insert.ExecuteNonQuery();
            }
        }

        private static void SaveItemAttributes(StorageContext context, int playerId, OpenMir2.Packets.ServerPackets.CharacterData data, string escrowJson)
        {
            using var delete = context.CreateCommand();
            delete.CommandText = "DELETE FROM characters_item_attr WHERE PlayerId=@PlayerId";
            delete.Parameters.AddWithValue("@PlayerId", playerId);
            delete.ExecuteNonQuery();
            var ids = new System.Collections.Generic.HashSet<int>();
            using var escrow = System.Text.Json.JsonDocument.Parse(escrowJson ?? "{}");
            foreach (var entry in escrow.RootElement.EnumerateObject())
            {
                int id = entry.Value.GetProperty("UserItem").GetProperty("MakeIndex").GetInt32();
                if (id <= 0 || !ids.Add(id)) throw new System.InvalidOperationException("Invalid duplicate weapon escrow ID");
            }
            foreach (var items in new[] { data.HumItems, data.BagItems, data.StorageItems })
            foreach (var item in items)
            {
                if (item == null || item.Index == 0 || item.MakeIndex <= 0) continue;
                if (!ids.Add(item.MakeIndex)) throw new System.InvalidOperationException("Duplicate item ID in character snapshot");
                using var insert = context.CreateCommand();
                insert.CommandText = "INSERT INTO characters_item_attr(PlayerId,MakeIndex,Value0,Value1,Value2,Value3,Value4,Value5,Value6,Value7,Value8,Value9,Value10,Value11,Value12,Value13) VALUES(@PlayerId,@MakeIndex,@V0,@V1,@V2,@V3,@V4,@V5,@V6,@V7,@V8,@V9,@V10,@V11,@V12,@V13)";
                insert.Parameters.AddWithValue("@PlayerId", playerId);
                insert.Parameters.AddWithValue("@MakeIndex", item.MakeIndex);
                for (int i = 0; i < 14; i++) insert.Parameters.AddWithValue("@V" + i, item.Desc[i]);
                insert.ExecuteNonQuery();
            }
        }

        private static void SaveWeaponEscrow(StorageContext context, int playerId, string json, long revision)
        {
            using var command = context.CreateCommand();
            command.CommandText = "INSERT INTO characters_weapon_escrow(PlayerId,Payload,Revision) VALUES(@PlayerId,@Payload,@Revision) ON DUPLICATE KEY UPDATE Payload=@Payload,Revision=@Revision";
            command.Parameters.AddWithValue("@PlayerId", playerId);
            command.Parameters.AddWithValue("@Payload", json ?? "{}");
            command.Parameters.AddWithValue("@Revision", revision);
            command.ExecuteNonQuery();
        }

        private static bool IsStaleSnapshot(StorageContext context, int playerId, long revision)
        {
            // Lock the role first, including the first save before an escrow row exists.
            using var role = context.CreateCommand();
            role.CommandText = "SELECT Id FROM characters WHERE Id=@PlayerId FOR UPDATE";
            role.Parameters.AddWithValue("@PlayerId", playerId);
            if (role.ExecuteScalar() == null) throw new System.InvalidOperationException("Character disappeared while saving");
            using var read = context.CreateCommand();
            read.CommandText = "SELECT Revision FROM characters_weapon_escrow WHERE PlayerId=@PlayerId FOR UPDATE";
            read.Parameters.AddWithValue("@PlayerId", playerId);
            object existing = read.ExecuteScalar();
            return existing != null && System.Convert.ToInt64(existing) >= revision;
        }

        private static void LoadWeaponEscrow(StorageContext context, int playerId, OpenMir2.Packets.ServerPackets.CharacterDataInfo record)
        {
            using var command = context.CreateCommand();
            command.CommandText = "SELECT Payload,Revision FROM characters_weapon_escrow WHERE PlayerId=@PlayerId";
            command.Parameters.AddWithValue("@PlayerId", playerId);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                record.WeaponUpgradeEscrowJson = reader.GetString(0);
                record.PersistenceRevision = reader.GetInt64(1);
            }
        }
    }
}
