using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MareSynchronosShared.Data;
using MareSynchronosShared.Models;
using MareSynchronosShared.Services;
using MareSynchronosShared.Utils;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text.RegularExpressions;

namespace MareSynchronosServices.Discord;

public partial class MareWizardModule : InteractionModuleBase
{
    private ILogger<MareModule> _logger;
    private IServiceProvider _services;
    private DiscordBotServices _botServices;
    private IConfigurationService<ServerConfiguration> _mareClientConfigurationService;
    private IConfigurationService<ServicesConfiguration> _mareServicesConfiguration;
    private IConnectionMultiplexer _connectionMultiplexer;
    private Random random = new();

    public MareWizardModule(ILogger<MareModule> logger, IServiceProvider services, DiscordBotServices botServices,
        IConfigurationService<ServerConfiguration> mareClientConfigurationService,
        IConfigurationService<ServicesConfiguration> mareServicesConfiguration,
        IConnectionMultiplexer connectionMultiplexer)
    {
        _logger = logger;
        _services = services;
        _botServices = botServices;
        _mareClientConfigurationService = mareClientConfigurationService;
        _mareServicesConfiguration = mareServicesConfiguration;
        _connectionMultiplexer = connectionMultiplexer;
    }


    [ComponentInteraction("wizard-home:*")]
    public async Task StartWizard(bool init = false)
    {
        if (!init && !(await ValidateInteraction().ConfigureAwait(false))) return;

        using var mareDb = GetDbContext();
        bool hasAccount = await mareDb.LodeStoneAuth.AnyAsync(u => u.DiscordId == Context.User.Id && u.StartedAt == null).ConfigureAwait(false);

        if (init)
        {
            bool isBanned = await mareDb.BannedRegistrations.AnyAsync(u => u.DiscordIdOrLodestoneAuth == Context.User.Id.ToString()).ConfigureAwait(false);

            if (isBanned)
            {
                EmbedBuilder ebBanned = new();
                ebBanned.WithTitle("You are not welcome here");
                ebBanned.WithDescription("Your Discord account is banned");
                await RespondAsync(embed: ebBanned.Build(), ephemeral: true).ConfigureAwait(false);
                return;
            }
        }

        EmbedBuilder eb = new();
        eb.WithTitle("欢迎使用本服务器的Mare Synchronos服务机器人");
        eb.WithDescription("你可以做这些事情:" + Environment.NewLine + Environment.NewLine
            + (!hasAccount ? string.Empty : ("- 检查你的账号状态 按下 \"ℹ️ 用户信息\"" + Environment.NewLine))
            + (hasAccount ? string.Empty : ("- 注册一个新的Mare账号 按下 \"🌒 注册\"" + Environment.NewLine))
            + (!hasAccount ? string.Empty : ("- 如果丢失了同步密钥 按下 \"🏥 恢复\"" + Environment.NewLine))
            + (hasAccount ? string.Empty : ("- 如果你更换了你的Discord账号 按下 \"🔗 重新连接\"" + Environment.NewLine))
            + (!hasAccount ? string.Empty : ("- 创建一个小号Mare UID 按下 \"2️⃣ 辅助 UID\"" + Environment.NewLine))
            + (!hasAccount ? string.Empty : ("- 设置个性Mare UID 按下 \"💅 个性 UID\"" + Environment.NewLine))
            + (!hasAccount ? string.Empty : ("- 删除你的大号或者小号 按下 \"⚠️ 删除\""))
            );
        eb.WithColor(Color.Blue);
        ComponentBuilder cb = new();
        if (!hasAccount)
        {
            cb.WithButton("注册", "wizard-register", ButtonStyle.Primary, new Emoji("🌒"));
            cb.WithButton("重新连接", "wizard-relink", ButtonStyle.Secondary, new Emoji("🔗"));
        }
        else
        {
            cb.WithButton("用户信息", "wizard-userinfo", ButtonStyle.Secondary, new Emoji("ℹ️"));
            cb.WithButton("恢复", "wizard-recover", ButtonStyle.Secondary, new Emoji("🏥"));
            cb.WithButton("辅助 UID", "wizard-secondary", ButtonStyle.Secondary, new Emoji("2️⃣"));
            cb.WithButton("个性 UID", "wizard-vanity", ButtonStyle.Secondary, new Emoji("💅"));
            cb.WithButton("删除", "wizard-delete", ButtonStyle.Danger, new Emoji("⚠️"));
        }
        if (init)
        {
            await RespondAsync(embed: eb.Build(), components: cb.Build(), ephemeral: true).ConfigureAwait(false);
            var resp = await GetOriginalResponseAsync().ConfigureAwait(false);
            _botServices.ValidInteractions[Context.User.Id] = resp.Id;
            _logger.LogInformation("Init Msg: {id}", resp.Id);
        }
        else
        {
            await ModifyInteraction(eb, cb).ConfigureAwait(false);
        }
    }

    public class VanityUidModal : IModal
    {
        public string Title => "设置个性 UID";

        [InputLabel("输入你想要设置的个性 UID")]
        [ModalTextInput("vanity_uid", TextInputStyle.Short, "5-15个字符，下划线，短横线", 5, 15)]
        public string DesiredVanityUID { get; set; }
    }

    public class VanityGidModal : IModal
    {
        public string Title => "设置个性同步贝ID";

        [InputLabel("输入你想要设置的个性同步贝ID")]
        [ModalTextInput("vanity_gid", TextInputStyle.Short, "5-20个字符，下划线，短横线", 5, 20)]
        public string DesiredVanityGID { get; set; }
    }

    public class ConfirmDeletionModal : IModal
    {
        public string Title => "确认账号删除";

        [InputLabel("输入全大写的 \"DELETE\"")]
        [ModalTextInput("confirmation", TextInputStyle.Short, "输入 DELETE")]
        public string Delete { get; set; }
    }

    private MareDbContext GetDbContext()
    {
        return _services.CreateScope().ServiceProvider.GetService<MareDbContext>();
    }

    private async Task<bool> ValidateInteraction()
    {
        if (Context.Interaction is not IComponentInteraction componentInteraction) return true;

        if (_botServices.ValidInteractions.TryGetValue(Context.User.Id, out ulong interactionId) && interactionId == componentInteraction.Message.Id)
        {
            return true;
        }

        EmbedBuilder eb = new();
        eb.WithTitle("Session expired");
        eb.WithDescription("This session has expired since you have either again pressed \"Start\" on the initial message or the bot has been restarted." + Environment.NewLine + Environment.NewLine
            + "Please use the newly started interaction or start a new one.");
        eb.WithColor(Color.Red);
        ComponentBuilder cb = new();
        await ModifyInteraction(eb, cb).ConfigureAwait(false);

        return false;
    }

    private void AddHome(ComponentBuilder cb)
    {
        cb.WithButton("返回主菜单", "wizard-home:false", ButtonStyle.Secondary, new Emoji("🏠"));
    }

    private async Task ModifyModalInteraction(EmbedBuilder eb, ComponentBuilder cb)
    {
        await (Context.Interaction as SocketModal).UpdateAsync(m =>
        {
            m.Embed = eb.Build();
            m.Components = cb.Build();
        }).ConfigureAwait(false);
    }

    private async Task ModifyInteraction(EmbedBuilder eb, ComponentBuilder cb)
    {
        await ((Context.Interaction) as IComponentInteraction).UpdateAsync(m =>
        {
            m.Embed = eb.Build();
            m.Components = cb.Build();
        }).ConfigureAwait(false);
    }

    private async Task AddUserSelection(MareDbContext mareDb, ComponentBuilder cb, string customId)
    {
        var discordId = Context.User.Id;
        var existingAuth = await mareDb.LodeStoneAuth.Include(u => u.User).SingleOrDefaultAsync(e => e.DiscordId == discordId).ConfigureAwait(false);
        if (existingAuth != null)
        {
            SelectMenuBuilder sb = new();
            sb.WithPlaceholder("选择一个UID");
            sb.WithCustomId(customId);
            var existingUids = await mareDb.Auth.Include(u => u.User).Where(u => u.UserUID == existingAuth.User.UID || u.PrimaryUserUID == existingAuth.User.UID)
                .OrderByDescending(u => u.PrimaryUser == null).ToListAsync().ConfigureAwait(false);
            foreach (var entry in existingUids)
            {
                sb.AddOption(string.IsNullOrEmpty(entry.User.Alias) ? entry.UserUID : entry.User.Alias,
                    entry.UserUID,
                    !string.IsNullOrEmpty(entry.User.Alias) ? entry.User.UID : null,
                    entry.PrimaryUserUID == null ? new Emoji("1️⃣") : new Emoji("2️⃣"));
            }
            cb.WithSelectMenu(sb);
        }
    }

    private async Task AddGroupSelection(MareDbContext db, ComponentBuilder cb, string customId)
    {
        var primary = (await db.LodeStoneAuth.Include(u => u.User).SingleAsync(u => u.DiscordId == Context.User.Id).ConfigureAwait(false)).User;
        var secondary = await db.Auth.Include(u => u.User).Where(u => u.PrimaryUserUID == primary.UID).Select(u => u.User).ToListAsync().ConfigureAwait(false);
        var primaryGids = (await db.Groups.Include(u => u.Owner).Where(u => u.OwnerUID == primary.UID).ToListAsync().ConfigureAwait(false));
        var secondaryGids = (await db.Groups.Include(u => u.Owner).Where(u => secondary.Select(u => u.UID).Contains(u.OwnerUID)).ToListAsync().ConfigureAwait(false));
        SelectMenuBuilder gids = new();
        if (primaryGids.Any() || secondaryGids.Any())
        {
            foreach (var item in primaryGids)
            {
                gids.AddOption(item.Alias ?? item.GID, item.GID, (item.Alias == null ? string.Empty : item.GID) + $" ({item.Owner.Alias ?? item.Owner.UID})", new Emoji("1️⃣"));
            }
            foreach (var item in secondaryGids)
            {
                gids.AddOption(item.Alias ?? item.GID, item.GID, (item.Alias == null ? string.Empty : item.GID) + $" ({item.Owner.Alias ?? item.Owner.UID})", new Emoji("2️⃣"));
            }
            gids.WithCustomId(customId);
            gids.WithPlaceholder("Select a Syncshell");
            cb.WithSelectMenu(gids);
        }
    }

    private async Task<string> GenerateLodestoneAuth(ulong discordid, string hashedLodestoneId, MareDbContext dbContext)
    {
        var auth = StringUtils.GenerateRandomString(32);
        LodeStoneAuth lsAuth = new LodeStoneAuth()
        {
            DiscordId = discordid,
            HashedLodestoneId = hashedLodestoneId,
            LodestoneAuthString = auth,
            StartedAt = DateTime.UtcNow
        };

        dbContext.Add(lsAuth);
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        return (auth);
    }

    private int? ParseCharacterIdFromLodestoneUrl(string lodestoneUrl)
    {
        // var regex = new Regex(@"https:\/\/(na|eu|de|fr|jp)\.finalfantasyxiv\.com\/lodestone\/character\/\d+");
        var regex = new Regex(@"^\d{8,}$");
        var matches = regex.Match(lodestoneUrl);
        var isLodestoneUrl = matches.Success;
        if (!isLodestoneUrl) return null;

        lodestoneUrl = matches.Groups[0].ToString();
        var stringId = lodestoneUrl;
        if (!int.TryParse(stringId, out int lodestoneId))
        {
            return null;
        }

        return lodestoneId;
    }

    private string GetSZJCookie()
    {
        return _mareServicesConfiguration.GetValueOrDefault(nameof(ServicesConfiguration.SZJCookie), string.Empty);
    }

}
