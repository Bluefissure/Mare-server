using Discord.Interactions;
using Discord;
using MareSynchronosShared.Data;
using MareSynchronosShared.Models;
using MareSynchronosShared.Utils;
using Microsoft.EntityFrameworkCore;

namespace MareSynchronosServices.Discord;

public partial class MareWizardModule
{
    [ComponentInteraction("wizard-recover")]
    public async Task ComponentRecover()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        using var mareDb = GetDbContext();
        EmbedBuilder eb = new();
        eb.WithColor(Color.Blue);
        eb.WithTitle("恢复");
        eb.WithDescription("如果你丢失了同步密钥，可以在此恢复。" + Environment.NewLine + Environment.NewLine
            + "用下面的选择框来选取需要恢复的UID。" + Environment.NewLine + Environment.NewLine
            + "- 1️⃣ 是你的主要账号/UID" + Environment.NewLine
            + "- 2️⃣ 是你所有的辅助UID" + Environment.NewLine
            + "如果你在使用个性 UID的话，原始的UID会在账号选项的第二行显示。");
        ComponentBuilder cb = new();
        await AddUserSelection(mareDb, cb, "wizard-recover-select").ConfigureAwait(false);
        AddHome(cb);
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-recover-select")]
    public async Task SelectionRecovery(string uid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        using var mareDb = GetDbContext();
        EmbedBuilder eb = new();
        eb.WithColor(Color.Green);
        await HandleRecovery(mareDb, eb, uid).ConfigureAwait(false);
        ComponentBuilder cb = new();
        AddHome(cb);
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }

    private async Task HandleRecovery(MareDbContext db, EmbedBuilder embed, string uid)
    {
        string computedHash = string.Empty;
        Auth auth;
        var previousAuth = await db.Auth.Include(u => u.User).FirstOrDefaultAsync(u => u.UserUID == uid).ConfigureAwait(false);
        if (previousAuth != null)
        {
            db.Auth.Remove(previousAuth);
        }

        computedHash = StringUtils.Sha256String(StringUtils.GenerateRandomString(64) + DateTime.UtcNow.ToString());
        auth = new Auth()
        {
            HashedKey = StringUtils.Sha256String(computedHash),
            User = previousAuth.User,
            PrimaryUserUID = previousAuth.PrimaryUserUID
        };

        await db.Auth.AddAsync(auth).ConfigureAwait(false);

        embed.WithTitle($"您的账号 {uid} 恢复成功。");
        embed.WithDescription("这里是你的同步密钥。不要与任何人分享它 **如果它丢失了，是无法恢复的。**"
                              + Environment.NewLine + Environment.NewLine
                              + $"**{computedHash}**"
                              + Environment.NewLine + Environment.NewLine
                              + "输入此同步密钥到Mare服务设置中并重新连接服务。");

        await db.Auth.AddAsync(auth).ConfigureAwait(false);
        await db.SaveChangesAsync().ConfigureAwait(false);
    }
}
