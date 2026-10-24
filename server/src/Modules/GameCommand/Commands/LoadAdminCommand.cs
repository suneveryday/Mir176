using SystemModule.Actors;
using SystemModule.Enums;
using SystemModule;

namespace CommandModule.Commands
{
    /// <summary>
    /// 重新加载管理员列表
    /// </summary>
    [Command("LoadAdmin", "重新加载管理员列表", 10)]
    public class LoadAdminCommand : GameCommand
    {
        [ExecuteCommand]
        public void Execute(IPlayerActor PlayerActor)
        {
            if (PlayerActor.Permission < 6)
            {
                return;
            }
            bool loaded = AdminPermissions.Reload();
            PlayerActor.SysMsg(loaded ? "管理员名单已重新加载，角色重新登录后生效。" : "管理员名单重新加载失败，保留原名单。",
                loaded ? MsgColor.Green : MsgColor.Red, MsgType.Hint);
        }
    }
}
