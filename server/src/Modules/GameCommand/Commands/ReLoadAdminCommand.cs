using SystemModule;
using SystemModule.Actors;
using SystemModule.Enums;

namespace CommandModule.Commands
{
    /// <summary>
    /// 重新加载管理员列表
    /// </summary>
    [Command("ReLoadAdmin", "重新加载管理员列表", 10)]
    public class ReLoadAdminCommand : GameCommand
    {
        [ExecuteCommand]
        public void Execute(IPlayerActor PlayerActor)
        {
            if (!AdminPermissions.Reload())
            {
                PlayerActor.SysMsg("管理员名单重新加载失败，保留原名单。", MsgColor.Red, MsgType.Hint);
                return;
            }
            SystemShare.WorldEngine.SendServerGroupMsg(213, SystemShare.ServerIndex, "");
            PlayerActor.SysMsg("管理员名单已重新加载，角色重新登录后生效。", MsgColor.Green, MsgType.Hint);
        }
    }
}
