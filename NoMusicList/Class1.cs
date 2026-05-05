using zakoAudioPlayer.Plugin;

namespace NoMusicList;

public class NoMusicList : PluginBase
{
    public NoMusicList()
    {
        Name = "No MusicList";
        Author = "SmellyFish";
        Id = "zakoAudioPlayer.Plugin.NoMusicList";
        MinimumCompatibleVersion = 1;
        MaximumCompatibleVersion = 1;
        Main = () => { };
        Mixin.Add("zakoAudioPlayer.Pages.HomePage.MusicList", () => { });
    }
}
