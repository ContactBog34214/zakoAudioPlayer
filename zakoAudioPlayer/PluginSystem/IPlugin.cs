namespace zakoAudioPlayer.Plugin;

public abstract class PluginBase
{
    public string Name { get; protected set; } = "Unknown Plugin";
    public string Id { get; init; }
    public string Author { get; init; } = "";
    public int version { get; init; }
    public int MinimumCompatibleVersion { get; init; } = 1;
    public int MaximumCompatibleVersion { get; init; } = 1;

    public Dictionary<string, Delegate> Mixin { get; set; } = [];
    public Action? Main { get; init; }
}
