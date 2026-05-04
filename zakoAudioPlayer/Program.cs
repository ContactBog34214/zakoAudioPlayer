using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Line.Framework.Audio;
using ManagedBass;
using TagLib.Flac;

#nullable enable

namespace zakoAudioPlayer
{
    class Program
    {
        static Config? activeConfig = new();
        static Exception ZakoZako = new Exception();
        static Thread configUpdateThread;
        static Thread mainUpdateThread = new Thread(() => UpdateThread());
        static Thread mainPrintThread = new Thread(() => PrintThread());
        static Thread mainMusicManagerThread = new Thread(() => MusicManagerThread());
        static Thread mainKeyListenerThread = new Thread(() => KeyListener());
        static string? Playing;
        static int MaximumWidth = 400;
        static string Page = "Home";
        static List<string> music = ["No Playing"];
        static AudioManager audio = new AudioManager();
        static int index = 0;
        static string[] metaData = ["Title", "artist", "collection"];
        static double fullTime = 0;
        static List<string[]> md = [];
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
                if (!activeConfig.Equals(lastConfig))
                {
                    lastConfig = DeepCopy<Config?>(activeConfig);
                    System.IO.File.WriteAllText(path, JsonSerializer.Serialize(activeConfig));
                }
            }
        }

        static void LoadFile(string path)
        {
            if (path != null)
            {
                index = 0;
                music = ["No Playing"];
                try
                {
                    string b = path.Split('.')[path.Split('.').Length - 1];
                    if (b == "txt")
                    {
                        string readIn = System.IO.File.ReadAllText(path);
                        music = readIn.Split('\n').ToList();
                        for (int i = music.Count - 1; i >= 0; i--)
                        {
                            if (music[i] == "")
                            {
                                music.RemoveAt(i);
                            }
                        }
                        activeConfig.targetFile = path;
                    }
                    else
                    {
                        music = [path];
                        activeConfig.targetFile = path;
                    }
                }
                catch { }
            }
            else
            {
                index = 0;
                music = ["No Playing"];
                activeConfig.targetFile = "";
            }
        }

        static void LoadMusic()
        {
            try
            {
                List<string[]> tmp = [];
                foreach (string i in music)
                {
                    var tmp2 = audio.GetMetadata(i);
                    tmp.Add([tmp2.title, tmp2.artist, tmp2.album]);
                }
                md = tmp;
            }
            catch { }
        }

        static class packageInfo
        {
            public static readonly Version version = new("2026.1.3");
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
            public int index { get; set; } = 0;

            /*
             *增量的
             */
            public static bool autoPlay = false;
        }

        static void LoadConfig(string file)
        {
            try
            {
                string tmp = System.IO.File.ReadAllText(file);
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
                var F = System.IO.File.Create(file);
                F.Dispose();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    activeConfig.speaker = "PipeWire Sound Server";
                }
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    activeConfig.speaker = "";
                }
                System.IO.File.WriteAllText(file, JsonSerializer.Serialize(activeConfig));
            }
        }

        static void Main(string[] arg)
        {
            string loadArg = "";
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
                        else
                        {
                            loadArg = arg[0];
                            startNow = true;
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
            index = activeConfig.index;
            if (activeConfig.volume > 1)
            {
                activeConfig.volume = 1;
            }
            if (activeConfig.volume < 0)
            {
                activeConfig.volume = 0;
            }
            configUpdateThread = new Thread(() => UpdateFile($"{path}/config.json"));
            if (loadArg == "")
            {
                LoadFile(activeConfig.targetFile);
            }
            else
            {
                LoadFile(loadArg);
            }
            LoadMusic();
            configUpdateThread.Start();
            mainUpdateThread.Start();
            mainPrintThread.Start();
            mainMusicManagerThread.Start();
            mainKeyListenerThread.Start();
        }

        static void Help()
        {
            Console.WriteLine("usage: zaplayer [file]");
            Console.WriteLine("operations:");
            Console.WriteLine("      zaplayer {-h --help}");
            Console.WriteLine("      zaplayer {-v --version}");
        }

        static void UpdateThread()
        {
            string? lastSpeaker = "i don't know";
            string? lastPlaying = "";
            //activeConfig.speaker = "PipeWire Sound Server";
            while (true)
            {
                if (lastSpeaker != activeConfig.speaker)
                {
                    lastSpeaker = activeConfig.speaker;
                    var devs = audio.GetAudioDevices();
                    short def = 0;
                    for (short i = 0; i < devs.Count; i++)
                    {
                        if (devs[i].Name == activeConfig.speaker)
                        {
                            audio.SwitchDevice(i);
                            lastPlaying = "";
                        }
                    }
                }
                if (index >= music.Count)
                {
                    index = index - music.Count;
                }
                if (index < 0)
                {
                    index = music.Count-1;
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
                    metaData = ["Loading", "artist", "collection"];
                    try
                    {
                        MainTrack = audio.LoadTrack("main", Playing);
                        fullTime = audio.GetAudioDuration(Playing);
                        var _metadata = audio.GetMetadata(Playing);
                        metaData[0] = _metadata.title;
                        metaData[1] = _metadata.artist;
                        metaData[2] = _metadata.album;
                        if (startNow)
                        {
                            MainTrack.Play();
                            startNow = false;
                        }
                    }
                    catch (Exception e)
                    {
                        metaData = [e.Message, e.Source, "collection"];
                        audio.UnloadTrack("main");
                        index += activeConfig.playMode;
                        MainTrack = null;
                    }
                }
                try
                {
                    MainTrack.Volume = activeConfig.volume;
                }
                catch (Exception) { }
                Thread.Sleep(50);
            }
        }

        static Vector2 BufferSize = new(Console.BufferWidth, Console.BufferHeight);

        static void PrintThread()
        {
            Console.CursorVisible = false;
            while (true)
            {
                Console.Clear();
                Console.Title = $"zakoAudioPlayer[{metaData[0]}]";
                BufferSize[0] = Console.BufferWidth;
                BufferSize[1] = Console.BufferHeight;
                if (BufferSize[0] > MaximumWidth || Console.IsInputRedirected)
                {
                    BufferSize[0] = MaximumWidth;
                }
                Console.BackgroundColor = ConsoleColor.Red;
                Console.Title = "zakoAudioPlayer";
                int mid = (int)(BufferSize[0] / 2);
                int targetLength = (int)BufferSize[0] - 3;
                string t1 = $" zakoAudioPlayer - {Page} ";
                if (t1.Length % 2 == 1)
                {
                    t1 = $"{t1} ";
                }
                if (targetLength > t1.Length - 1)
                {
                    targetLength = t1.Length - 1;
                }
                t1 = $" zakoAudioPlayer - {Page} ".Substring(0, targetLength);
                string t2 = new('=', mid - (t1.Length / 2));
                string t3 = new('=', (int)(BufferSize[0] - t1.Length - t2.Length));
                Console.WriteLine($"{t2}{t1}{t3}");
                HomePageData.usedLine = 4;
                Console.ResetColor();
                if (BufferSize[0] > 20)
                {
                    var s = Task.Run(() => renderPage());
                    var sw = new Stopwatch();
                    sw.Start();
                    while (true)
                    {
                        if (sw.ElapsedMilliseconds >= 100 || s.IsCompleted)
                        {
                            break;
                        }
                        Thread.Sleep(5);
                    }
                    sw.Stop();
                    sw.Reset();
                }
                else
                {
                    Console.WriteLine("要被挤死了喵");
                    Console.WriteLine("(`д´)");
                }
                Thread.Sleep(10);
            }
        }

        static void renderPage()
        {
            switch (Page)
            {
                case "Home":
                    HomePage();
                    break;
                case "Volume":
                    VolumePage();
                    break;
                case "SelectSpeaker":
                    SelectSpeakerPage();
                    break;
            }
        }

        static string FormatNumber(int number, int target)
        {
            if (number.ToString().Length >= target)
                return number.ToString();
            string result = number.ToString();
            result = $"{new string('0', target - result.Length)}{number}";
            return result;
        }

        static class HomePageData
        {
            public static ConsoleKey keyPress = ConsoleKey.VolumeMute;
            public static int usedLine = 0;
        }

        static Action HomePage = () =>
        {
            int mid = (int)(BufferSize[0] / 2);
            string t_1 = $" {metaData[0]}  ";
            if (t_1.Length % 2 == 1)
            {
                t_1 = $"{t_1} ";
            }
            int targetLength = (int)BufferSize[0] - 1;
            if (targetLength >= t_1.Length)
            {
                targetLength = t_1.Length;
            }
            t_1 = $"{t_1.Substring(0, targetLength - 1)}";
            string t_2 = new(' ', mid - t_1.Length / 2);
            Console.ResetColor();
            Console.Write(t_2);
            Console.BackgroundColor = ConsoleColor.Magenta;
            Console.Write(t_1);
            Console.ResetColor();
            Console.WriteLine(t_2);
            HomePageData.usedLine++;
            string t1 = $"By {metaData[1]}";
            string t2 = $"From {metaData[2]}";
            int t3 = (int)((BufferSize[0] - 2) / 2);
            int t4 = (int)(BufferSize[0] - 2 * t3) - 1;
            string apart = new(' ', t4);
            if (t1.Length > t3 || t2.Length > t3 || true)
            {
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Console.Write(t1);
                Console.ResetColor();
                Console.Write('\n');
                HomePageData.usedLine++;
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Console.Write(t2);
                Console.ResetColor();
                Console.Write('\n');
                HomePageData.usedLine++;
            }
            else
            {
                Console.Write($"{t1}{new(' ', t3 - t1.Length)}");
                Console.ResetColor();
                Console.Write(apart + ' ');
                Console.Write($"{new(' ', t3 - t2.Length)}{t2}");
                Console.ResetColor();
                Console.WriteLine();
                HomePageData.usedLine++;
            }
            Console.ResetColor();
            playingBar();
            bool s = false;
            try
            {
                s = MainTrack.IsPlaying;
            }
            catch { }
            actionBar([
                "[UpArrow]last music",
                $"[Space]{(s ? "Pause" : "Play")}",
                "[DownArrow]next music",
                "\n",
                "[LeftArrow]Rewind 10 sec",
                "[RightArrow]Forward 10 sec",
                "[F5]Speaker/Volume",
                $"[F6]Loop Mode[{(activeConfig.playMode != 1 ? "Enabled" : "Disabled")}]",
                $"[F7]Random Mode[{(activeConfig.random ? "Enabled" : "Disabled")}]",
            ]);
            DrawMusicList();
            if (key != HomePageData.keyPress)
            {
                HomePageData.keyPress = (ConsoleKey)key;
                switch (key)
                {
                    case (ConsoleKey.Spacebar):
                        try
                        {
                            if (MainTrack.IsPlaying)
                            {
                                MainTrack.Pause();
                            }
                            else
                            {
                                MainTrack.Play();
                            }
                        }
                        catch { }
                        break;
                    case (ConsoleKey.UpArrow):
                        if (!activeConfig.random)
                        {
                            index -= activeConfig.playMode;
                        }
                        else
                        {
                            var rd = new Random();
                            index = rd.Next(0, music.Count);
                        }
                        startNow = true;
                        break;
                    case (ConsoleKey.DownArrow):
                        if (!activeConfig.random)
                        {
                            index += activeConfig.playMode;
                        }
                        else
                        {
                            var rd = new Random();
                            index = rd.Next(0, music.Count);
                        }
                        startNow = true;
                        break;
                    case (ConsoleKey.F5):
                        Page = "Volume";
                        break;
                    case (ConsoleKey.F6):
                        if (activeConfig.playMode != 1)
                        {
                            activeConfig.playMode = 1;
                        }
                        else
                        {
                            activeConfig.playMode = 0;
                        }
                        break;
                    case (ConsoleKey.F7):
                        activeConfig.random = !activeConfig.random;
                        break;
                    case (ConsoleKey.LeftArrow):
                        try
                        {
                            double target = MainTrack.CurrentPosition - 10;
                            if (target < 0)
                            {
                                target = 0;
                            }
                            MainTrack.SeekToSeconds(target);
                        }
                        catch { }
                        break;
                    case (ConsoleKey.RightArrow):
                        try
                        {
                            double target = MainTrack.CurrentPosition + 10;
                            if (target > fullTime)
                            {
                                target = fullTime;
                            }
                            MainTrack.SeekToSeconds(target);
                        }
                        catch { }
                        break;
                }
            }
        };

        static void MusicManagerThread()
        {
            while (true)
            {
                try
                {
                    if (0 != fullTime && fullTime == MainTrack.CurrentPosition)
                    {
                        startNow = true;
                        index += activeConfig.playMode;
                        if (activeConfig.random)
                        {
                            var rd = new Random();
                            index = rd.Next(0, music.Count);
                        }
                        else if (activeConfig.playMode == 0)
                        {
                            startNow = false;
                            MainTrack.SeekToSeconds(0);
                            MainTrack.Play();
                        }
                    }
                }
                catch { }
                Thread.Sleep(50);
            }
        }

        static bool startNow = false;

        static class VolumePageData
        {
            public static ConsoleKey KeyPress = ConsoleKey.VolumeMute;
        }

        static Action VolumePage = () =>
        {
            string t1 = "Speaker:";
            string t2 = activeConfig.speaker;
            if (t2.Length > BufferSize[0] - t1.Length)
            {
                t2 = t2.Substring(0, (int)BufferSize[0] - t1.Length - 1);
            }
            int t3 = (int)((BufferSize[0] - t1.Length - t2.Length) / 2);
            string t4 = new(' ', t3);
            Console.ResetColor();
            Console.Write(t4);
            Console.Write(t1);
            Console.BackgroundColor = ConsoleColor.Green;
            Console.Write(t2);
            Console.ResetColor();
            Console.Write(t4);
            Console.WriteLine('\n');
            string t5 =
                $"[Volume:{new string(' ', 3 - ((int)(100 * activeConfig.volume)).ToString().Length)}{(int)(100 * activeConfig.volume)}%]";
            int progressBarLength = (int)(BufferSize[0] - t5.Length);
            int used = (int)(progressBarLength * activeConfig.volume);
            string t6 = $"{new('=', used)}|{new(' ', progressBarLength - used)}";
            Console.BackgroundColor = ConsoleColor.Green;
            Console.Write(t5);
            Console.ResetColor();
            Console.Write(t6);
            Console.Write('\n');
            actionBar([
                "[ESC]Back",
                "[LeftArrow]Reduce Volume",
                "[RightArrow]Add Volume",
                "[DownArrow]Reduce Volume(*10)",
                "[UpArrow]Add Volume(*10)",
                "[S]Choose a speaker",
            ]);
            if (VolumePageData.KeyPress != key)
            {
                VolumePageData.KeyPress = (ConsoleKey)key;
                switch (key)
                {
                    case (ConsoleKey.Escape):
                        Page = "Home";
                        break;
                    case (ConsoleKey.LeftArrow):
                        activeConfig.volume -= 0.01f;
                        break;
                    case (ConsoleKey.RightArrow):
                        activeConfig.volume += 0.01f;
                        break;
                    case (ConsoleKey.DownArrow):
                        activeConfig.volume -= 0.1f;
                        break;
                    case (ConsoleKey.UpArrow):
                        activeConfig.volume += 0.1f;
                        break;
                    case (ConsoleKey.S):
                        SelectSpeakerPageData.LoadPage();
                        Page = "SelectSpeaker";
                        break;
                }
            }
            if (activeConfig.volume > 1)
            {
                activeConfig.volume = 1;
            }
            if (activeConfig.volume < 0)
            {
                activeConfig.volume = 0;
            }
        };

        static class SelectSpeakerPageData
        {
            public static ConsoleKey KeyPress = ConsoleKey.VolumeMute;
            public static int Line = 5;
            public static List<DeviceInfo>? DevList;
            public static List<string>? DevNameList;
            public static int select = 0;

            public static void LoadPage()
            {
                select = 0;
                DevList = audio.GetAudioDevices();
                DevNameList = [];
                for (int i = 0; i < DevList.Count; i++)
                {
                    DevNameList.Add(DevList[i].Name);
                    if (DevList[i].Name == activeConfig.speaker)
                    {
                        select = i;
                    }
                }
            }
        }

        static Action SelectSpeakerPage = () =>
        {
            string t1 = "Speaker:";
            string t2 = activeConfig.speaker;
            if (t2.Length > BufferSize[0] - t1.Length)
            {
                t2 = t2.Substring(0, (int)BufferSize[0] - t1.Length - 1);
            }
            int t3 = (int)((BufferSize[0] - t1.Length - t2.Length) / 2);
            string t4 = new(' ', t3);
            Console.ResetColor();
            Console.Write(t4);
            Console.Write(t1);
            Console.BackgroundColor = ConsoleColor.Green;
            Console.Write(t2);
            Console.ResetColor();
            Console.Write(t4);
            Console.WriteLine('\n');
            int midOffset = (int)(SelectSpeakerPageData.Line / 2);
            int tmpLine = -midOffset;
            int Length = (int)BufferSize[0];
            for (; tmpLine + 2 < SelectSpeakerPageData.Line; tmpLine++)
            {
                Console.ResetColor();
                int hit = SelectSpeakerPageData.select + tmpLine;
                if (hit < 0 || hit >= SelectSpeakerPageData.DevNameList.Count)
                {
                    Console.Write('\n');
                }
                else
                {
                    string t11 = $"{hit + 1}.{SelectSpeakerPageData.DevNameList[hit]}";
                    int t12 = (int)((Length - t11.Length) / 2);
                    int t13 = (int)(Length - t11.Length - t12);
                    Console.ResetColor();
                    Console.Write(new string(' ', t12));
                    if (hit == SelectSpeakerPageData.select)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkGreen;
                    }
                    else if (SelectSpeakerPageData.DevNameList[hit] == activeConfig.speaker)
                    {
                        Console.BackgroundColor = ConsoleColor.Red;
                    }
                    else
                    {
                        Console.BackgroundColor = ConsoleColor.Magenta;
                    }
                    Console.Write(t11);
                    Console.ResetColor();
                    Console.Write(new string(' ', t13));
                    Console.Write('\n');
                }
            }
            actionBar([
                "[ESC]Back",
                "[UpArrow]Up",
                "[DownArrow]Down",
                "[Space]Select",
                "[R]Reload",
            ]);
            if (SelectSpeakerPageData.KeyPress != (ConsoleKey)key)
            {
                SelectSpeakerPageData.KeyPress = (ConsoleKey)key;
                switch ((ConsoleKey)key)
                {
                    case (ConsoleKey.Escape):
                        Page = "Volume";
                        break;
                    case (ConsoleKey.UpArrow):
                        SelectSpeakerPageData.select--;
                        break;
                    case (ConsoleKey.DownArrow):
                        SelectSpeakerPageData.select++;
                        break;
                    case (ConsoleKey.Spacebar):
                        activeConfig.speaker = SelectSpeakerPageData.DevNameList[
                            SelectSpeakerPageData.select
                        ];
                        audio.SwitchDevice(SelectSpeakerPageData.select);
                        activeConfig.speaker = SelectSpeakerPageData.DevNameList[
                            audio.CurrentDevice
                        ];
                        SelectSpeakerPageData.LoadPage();
                        break;
                    case (ConsoleKey.R):
                        SelectSpeakerPageData.LoadPage();
                        break;
                }
            }
            if (SelectSpeakerPageData.select < 0)
            {
                SelectSpeakerPageData.select = 0;
            }
            if (SelectSpeakerPageData.select >= SelectSpeakerPageData.DevNameList.Count)
            {
                SelectSpeakerPageData.select = SelectSpeakerPageData.DevNameList.Count - 1;
            }
        };
        static Action DrawMusicList = () =>
        {
            int length = (int)BufferSize[0];
            string title = "Music List";
            int height = (int)BufferSize[1] - HomePageData.usedLine - 2;
            int offset = (int)Math.Ceiling((double)height / 2);
            if (index - offset < 0)
            {
                offset = index;
            }
            else if (index > md.Count + offset - height)
            {
                /*
                 *已知md.Count,offset,height,index
                 *列出下列方程
                 *index-md.Count+height>offset
                 *offset+index+height-1<md.Count
                 */
                offset = index + height - md.Count;
            }
            int t1 = (int)(length - title.Length) / 2;
            int t2 = (int)length - title.Length - t1;
            string t3 = new('~', t1);
            string t4 = new('~', t2);
            Console.ResetColor();
            Console.Write(t3);
            Console.BackgroundColor = ConsoleColor.Yellow;
            Console.Write(title);
            Console.ResetColor();
            Console.Write(t4);
            Console.Write('\n');
            int t20 = length - 5 - 3;
            int t21 = t20 / 3;
            int t22 = t21;
            int t23 = t20 - t21 - t22;
            string t41 = "song";
            string t42 = "artist";
            string t43 = "album";
            Console.WriteLine(
                $"index {t41}{new('\t', (t21 - t41.Length) / 8)}{t42}{new('\t', (t22 - t42.Length) / 8)}{t43}"
            );
            HomePageData.usedLine++;
            int i = -offset;
            for (int j = 0; j < height; j++)
            {
                int hit = index + j + i;
                if (hit < 0 || hit >= md.Count)
                {
                    Console.ResetColor();
                    Console.Write('\n');
                    HomePageData.usedLine++;
                    continue;
                }
                string[] target = md[hit];
                string t11 = $"{new string(' ', 4 - ((hit + 1).ToString().Length))}{hit + 1}.";

                string t31 = target[0];
                string t32 = target[1];
                string t33 = target[2];
                if (t31.Length > t21)
                {
                    t31 = $"{t31.Substring(0, t21 - 3)}...";
                }
                if (t32.Length > t22)
                {
                    t32 = $"{t31.Substring(0, t22 - 3)}...";
                }
                if (t33.Length > t23)
                {
                    t33 = $"{t31.Substring(0, t23 - 3)}...";
                }
                Console.BackgroundColor = ConsoleColor.Black;
                if (hit == index)
                {
                    Console.BackgroundColor = ConsoleColor.DarkGreen;
                }
                Console.Write(t11);
                Console.Write($" {t31} ");
                Console.Write($"{new('\t', (t21 - t31.Length) / 8)}{t32} ");
                Console.Write($"{new('\t', (t22 - t32.Length) / 8)}{t33} ");
                Console.ResetColor();
                Console.Write('\n');
                HomePageData.usedLine++;
            }
        };
        static Action playingBar = () =>
        {
            double now = 0;
            try
            {
                now = MainTrack.CurrentPosition;
            }
            catch
            {
                now = 0;
            }
            string nt = $"[{(int)now / 60}:{FormatNumber((int)(now % 60), 2)}]";
            string ft = $"[{(int)fullTime / 60}:{FormatNumber((int)(fullTime % 60), 2)}]";
            if (nt.Length + ft.Length >= BufferSize[0])
            {
                Console.BackgroundColor = ConsoleColor.Magenta;
                Console.Write(nt);
                Console.ResetColor();
                Console.Write('\n');
                HomePageData.usedLine++;
                Console.BackgroundColor = ConsoleColor.Magenta;
                Console.Write(ft);
                Console.ResetColor();
                Console.Write('\n');
                HomePageData.usedLine++;
            }
            else
            {
                int progressBarLength = (int)(BufferSize[0] - ft.Length - nt.Length);
                int played = (int)(progressBarLength * (now / fullTime));
                Console.ResetColor();
                Console.Write(nt);
                Console.Write(new string('-', played));
                Console.Write(new string(' ', progressBarLength - played));
                Console.Write(ft);
                Console.ResetColor();
                Console.Write('\n');
                HomePageData.usedLine++;
            }
        };
        static Action<List<string>> actionBar = (List<string> arg) =>
        {
            int tmp = 0;
            int length = (int)BufferSize[0] + 1;
            foreach (string i in arg)
            {
                if (i == "\n")
                {
                    Console.ResetColor();
                    Console.Write('\n');
                    HomePageData.usedLine++;
                    continue;
                }
                if (tmp == 0 || i.Length + tmp + 2 <= length)
                {
                    Console.BackgroundColor = ConsoleColor.DarkGreen;
                    Console.Write($"{i} ");
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.Write(' ');
                    tmp += i.Length + 2;
                }
                else
                {
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.Write(new string(' ', length - tmp));
                    Console.ResetColor();
                    Console.Write('\n');
                    HomePageData.usedLine++;
                    Console.BackgroundColor = ConsoleColor.DarkGreen;
                    Console.Write(i);
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.Write(' ');
                }
            }
            Console.ResetColor();
            Console.Write('\n');
            HomePageData.usedLine++;
        };
        static ConsoleKey? key = null;

        static void KeyListener()
        {
            while (true)
            {
                key = Console.ReadKey().Key;
                Thread.Sleep(20);
                key = ConsoleKey.Applications;
            }
        }
    }
}
