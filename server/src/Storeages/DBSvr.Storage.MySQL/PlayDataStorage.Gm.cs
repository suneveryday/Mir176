using DBSrv.Storage.Impl;
using OpenMir2.Packets.ServerPackets;

namespace DBSrv.Storage.MySQL;

public partial class PlayDataStorage
{
    private static void SaveGmState(StorageContext context, int playerId, CharacterDataInfo record)
    {
        // Validate the typed payload before committing any role mutation.
        var state = System.Text.Json.JsonSerializer.Deserialize<OpenMir2.Data.GmCharacterState>(record.GmStateJson ?? "{}");
        if (state == null || state.Commands == null || state.Commands.Count > 1024)
            throw new System.InvalidOperationException("Invalid GM state");
        using var command = context.CreateCommand();
        command.CommandText = "INSERT INTO characters_gm_state(PlayerId,Payload,Revision) VALUES(@PlayerId,@Payload,@Revision) ON DUPLICATE KEY UPDATE Payload=@Payload,Revision=@Revision";
        command.Parameters.AddWithValue("@PlayerId", playerId);
        command.Parameters.AddWithValue("@Payload", record.GmStateJson ?? "{}");
        command.Parameters.AddWithValue("@Revision", record.PersistenceRevision);
        command.ExecuteNonQuery();
    }

    private static void LoadGmState(StorageContext context, int playerId, CharacterDataInfo record)
    {
        using var command = context.CreateCommand();
        command.CommandText = "SELECT Payload,Revision FROM characters_gm_state WHERE PlayerId=@PlayerId";
        command.Parameters.AddWithValue("@PlayerId", playerId);
        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            if (reader.GetInt64(1) != record.PersistenceRevision) throw new System.InvalidOperationException("GM state revision mismatch");
            record.GmStateJson = reader.GetString(0);
        }
    }
}
