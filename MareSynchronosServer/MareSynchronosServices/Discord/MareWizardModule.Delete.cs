using Discord.Interactions;
using Discord;
using MareSynchronosShared.Data;
using MareSynchronosShared.Utils;

namespace MareSynchronosServices.Discord;

public partial class MareWizardModule
{
    [ComponentInteraction("wizard-delete")]
    public async Task ComponentDelete()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        using var mareDb = GetDbContext();
        EmbedBuilder eb = new();
        eb.WithTitle("删除账号");
        eb.WithDescription("你可以在此删除你的主要或者辅助 UID。" + Environment.NewLine + Environment.NewLine
            + "__提升: 删除你的主要 UID也会同时删除所有的辅助 UID。__" + Environment.NewLine + Environment.NewLine
            + "- 1️⃣ 是你的主要账号/UID" + Environment.NewLine
            + "- 2️⃣ 是你所有的辅助UID" + Environment.NewLine
            + "如果你在使用个性 UID的话，原始的UID会在账号选项的第二行显示。");
        eb.WithColor(Color.Blue);

        ComponentBuilder cb = new();
        await AddUserSelection(mareDb, cb, "wizard-delete-select").ConfigureAwait(false);
        AddHome(cb);
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-delete-select")]
    public async Task SelectionDeleteAccount(string uid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        using var mareDb = GetDbContext();
        bool isPrimary = mareDb.Auth.Single(u => u.UserUID == uid).PrimaryUserUID == null;
        EmbedBuilder eb = new();
        eb.WithTitle($"你确定要删除 {uid} 吗？");
        eb.WithDescription($"此操作不可逆转。你所有的配对，加入的同步贝，储存在 {uid} 账号上所有的信息都会被" +
            $"不可逆的删除。" +
            (isPrimary ? (Environment.NewLine + Environment.NewLine +
            "⚠️ **你即将删除一个主要UID，所有的辅助UID也会被同时删除。** ⚠️") : string.Empty));
        eb.WithColor(Color.Purple);
        ComponentBuilder cb = new();
        cb.WithButton("取消", "wizard-delete", emote: new Emoji("❌"));
        cb.WithButton($"删除 {uid}", "wizard-delete-confirm:" + uid, ButtonStyle.Danger, emote: new Emoji("🗑️"));
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-delete-confirm:*")]
    public async Task ComponentDeleteAccountConfirm(string uid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        await RespondWithModalAsync<ConfirmDeletionModal>("wizard-delete-confirm-modal:" + uid).ConfigureAwait(false);
    }

    [ModalInteraction("wizard-delete-confirm-modal:*")]
    public async Task ModalDeleteAccountConfirm(string uid, ConfirmDeletionModal modal)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        try
        {
            if (!string.Equals("DELETE", modal.Delete, StringComparison.Ordinal))
            {
                EmbedBuilder eb = new();
                eb.WithTitle("确认不正确");
                eb.WithDescription($"你输入了 {modal.Delete} 但是要求的是 DELETE。请重新尝试并输入 DELETE 来确认。");
                eb.WithColor(Color.Red);
                ComponentBuilder cb = new();
                cb.WithButton("取消", "wizard-delete", emote: new Emoji("❌"));
                cb.WithButton("重试", "wizard-delete-confirm:" + uid, emote: new Emoji("🔁"));

                await ModifyModalInteraction(eb, cb).ConfigureAwait(false);
            }
            else
            {
                var maxGroupsByUser = _mareClientConfigurationService.GetValueOrDefault(nameof(ServerConfiguration.MaxGroupUserCount), 3);

                using var db = GetDbContext();
                var user = db.Users.Single(u => u.UID == uid);
                await SharedDbFunctions.PurgeUser(_logger, user, db, maxGroupsByUser).ConfigureAwait(false);

                EmbedBuilder eb = new();
                eb.WithTitle($"账号 {uid} 成功删除");
                eb.WithColor(Color.Green);
                ComponentBuilder cb = new();
                AddHome(cb);

                await ModifyModalInteraction(eb, cb).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling modal delete account confirm");
        }
    }
}
