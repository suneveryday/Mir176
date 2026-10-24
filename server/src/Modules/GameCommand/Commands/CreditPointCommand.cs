using OpenMir2;
using SystemModule;
using SystemModule.Actors;
using SystemModule.Enums;

namespace CommandModule.Commands
{
    /// <summary>
    /// 调整指定玩家声望点
    /// </summary>
    [Command("CreditPoint", "调整指定玩家声望点", "人物名称 控制符(+,-,=) 声望点数(0-255)", 10)]
    public class CreditPointCommand : GameCommand
    {
        [ExecuteCommand]
        public void Execute(string[] @params, IPlayerActor PlayerActor)
        {
            if (@params == null)
            {
                return;
            }
            string sHumanName = @params.Length > 0 ? @params[0] : "";
            string sCtr = @params.Length > 1 ? @params[1] : "";
            if (@params.Length != 3 || string.IsNullOrEmpty(sHumanName) || sHumanName[0] == '?'
                || (sCtr != "=" && sCtr != "+" && sCtr != "-")
                || !int.TryParse(@params[2], out int nPoint) || nPoint < 0 || nPoint > byte.MaxValue)
            {
                PlayerActor.SysMsg(Command.CommandHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            IPlayerActor mIPlayerActor = SystemShare.WorldEngine.GetPlayObject(sHumanName);
            if (mIPlayerActor == null)
            {
                PlayerActor.SysMsg(string.Format(CommandHelp.NowNotOnLineOrOnOtherServer, sHumanName), MsgColor.Red, MsgType.Hint);
                return;
            }
            int previous = mIPlayerActor.CreditPoint;
            int updated = sCtr == "=" ? nPoint : sCtr == "+" ? previous + nPoint : previous - nPoint;
            if (updated < 0 || updated > byte.MaxValue)
            {
                PlayerActor.SysMsg("声望点必须在 0 到 255 之间，本次未修改。", MsgColor.Red, MsgType.Hint);
                return;
            }
            mIPlayerActor.CreditPoint = (byte)updated;
            mIPlayerActor.SysMsg($"你的声望点已由 {previous} 调整为 {updated}。", MsgColor.Green, MsgType.Hint);
            PlayerActor.SysMsg($"{sHumanName}的声望点已由 {previous} 调整为 {updated}。", MsgColor.Green, MsgType.Hint);
        }
    }
}
