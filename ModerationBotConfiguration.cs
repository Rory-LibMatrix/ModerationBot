using Microsoft.Extensions.Configuration;

namespace ModerationBot;

public class ModerationBotConfiguration {
    public ModerationBotConfiguration(IConfiguration config) => config.GetRequiredSection("ModerationBot").Bind(this);

    public List<string> Admins { get; set; } = new();
    public bool DemoMode { get; set; } = false;
}
