using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using Line.Framework.Audio;
using ManagedBass;

namespace zakoAudioPlayer
{
    class Program
    {
        static Config? activeConfig = new();
        static Thread configUpdateThread;
        static Thread mainUpdateThread = new Thread(() => UpdateThread());
        static string? Playing;
        static List<string> music =
        [
            "/home/smellyfish/.local/share/Steam/steamapps/workshop/content/977950/2124854584/Xtrullor - Cry.mp3",
        ];
        static AudioManager audio = new();
        static int index = 0;
        static string[] metaData = ["No playing", "", ""];

        static Track? MainTrack;

        public static T DeepCopy<T>(T obj)
        {
            string json = JsonSerializer.Serialize(obj);
            return JsonSerializer.Deserialize<T>(json);
        }

        static void UpdateFile(string path)
        {
            Config? lastConfig = DeepCopy<Config?>(activeConfig);
            while (true)
            {
                Thread.Sleep(2000);
                if (activeConfig.Equals(lastConfig))
                {
                    lastConfig = DeepCopy<Config?>(activeConfig);
                    File.WriteAllText(path, JsonSerializer.Serialize(activeConfig));
                }
            }
        }

        static class packageInfo
        {
            public static readonly Version version = new("2026.1.0");
            public static readonly string description =
                @"It's just a media player
                a light,fast media player
            ";
        }

        static List<string> AudioList = new();

        class Config
        {
            public string targetFile { get; set; } = "";
            public string speaker { get; set; } = "";
            public bool random { get; set; } = false;
            public float volume { get; set; } = 1.0f;
            public Int16 playMode { get; set; } = 0;

            /*
             *增量的
             */
            public static bool autoPlay = false;
        }

        static void LoadConfig(string file)
        {
            try
            {
                string tmp = File.ReadAllText(file);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    UnmappedMemberHandling = System
                        .Text
                        .Json
                        .Serialization
                        .JsonUnmappedMemberHandling
                        .Skip,
                };
                activeConfig = JsonSerializer.Deserialize<Config>(tmp, options);
                if (activeConfig == null)
                    activeConfig = new();
            }
            catch (UnauthorizedAccessException e)
            {
                Console.WriteLine($"Permission denied({e.Message})");
                throw new UnauthorizedAccessException();
            }
            catch
            {
                var F = File.Create(file);
                F.Dispose();
                File.WriteAllText(file, JsonSerializer.Serialize(new Config()));
            }
        }

        static void Main(string[] arg)
        {
            if (arg.Length != 0)
            {
                switch (arg[0])
                {
                    case ("-h"):
                    case ("--help"):
                        Help();
                        return;
                    case ("-v"):
                    case ("--version"):
                        Console.WriteLine($"zakoAduioPlayer,Version{Program.packageInfo.version}");
                        Console.WriteLine(Program.packageInfo.description);
                        return;
                    default:
                        if (arg[0].StartsWith("-"))
                        {
                            Console.WriteLine("error: no operation specified (use -h for help)");
                            return;
                        }
                        break;
                }
            }
            string path = Environment.GetEnvironmentVariable("HOME");
            path = $"{path}/.local/share";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                path = "$/HOME/AppData/Local";
            }
            path = $"{path}/zakoAudioPlayer";
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            LoadConfig($"{path}/config.json");
            configUpdateThread = new Thread(() => UpdateFile(path));
            configUpdateThread.Start();
            mainUpdateThread.Start();
            return;
        }

        static void Help()
        {
            Console.WriteLine("usage: zaplayer [file(s)]");
            Console.WriteLine("operations:");
            Console.WriteLine("      zaplayer {-h --help}");
            Console.WriteLine("      zaplayer {-v --version}");
        }

        static void UpdateThread()
        {
            string? lastSpeaker = "i don't know";
            string? lastPlaying = "";
            while (true)
            {
                Thread.Sleep(50);
                if (lastSpeaker != activeConfig.speaker)
                {
                    lastSpeaker = activeConfig.speaker;
                    var devs = audio.GetAudioDevices();
                    short def;
                    for (short i = 0; i < devs.Count; i++)
                    {
                        if (devs[i].IsDefault)
                            def = i;
                        if (devs[i].Name == activeConfig.speaker)
                        {
                            audio.SwitchDevice(i);
                            break;
                        }
                        else if (i + 1 == devs.Count)
                        {
                            activeConfig.speaker = devs[i].Name;
                            lastSpeaker = activeConfig.speaker;
                            audio.SwitchToDefaultDevice();
                        }
                    }
                }
                if (index >= music.Count)
                {
                    index = index - music.Count;
                }
                if (index < 0)
                {
                    index = music.Count;
                }
                if (music.Count != 0)
                {
                    try
                    {
                        if (music[index] != Playing)
                        {
                            Playing = music[index];
                        }
                    }
                    catch (Exception) { }
                }
                if (lastPlaying != Playing && Playing != "")
                {
                    lastPlaying = Playing;
                    audio.UnloadTrack("main");
                    MainTrack = null;

                    try
                    {
                        MainTrack = audio.LoadTrack("main", Playing);
                        Console.WriteLine($"Loaded {Playing}");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        audio.UnloadTrack("main");
                        index += activeConfig.playMode;
                        MainTrack = null;
                    }
                }
                try
                {
                    if (MainTrack.Volume != activeConfig.volume)
                    {
                        MainTrack.Volume = activeConfig.volume;
                    }
                }
                catch (Exception) { }
            }
        }
    }
}
