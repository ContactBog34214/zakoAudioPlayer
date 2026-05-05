using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.Numerics;
using System.Runtime.InteropServices;
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
        /// <summary>
        /// 判断字符是否为全角（常用 CJK 及韩文、日文范围）
        /// </summary>
        static bool IsFullWidth(char c)
        {
            return (c >= 0x1100 && c <= 0x11FF)
                || // 韩文辅音/元音
                (c >= 0x3130 && c <= 0x318F)
                || // 韩文兼容字母
                (c >= 0xAC00 && c <= 0xD7AF)
                || // 韩文音节
                (c >= 0x4E00 && c <= 0x9FFF)
                || // CJK 统一表意文字
                (c >= 0x3040 && c <= 0x30FF)
                || // 日文平假名/片假名
                (c >= 0xFF00 && c <= 0xFFEF)
                || // 全角 ASCII
                (c >= 0x3000 && c <= 0x303F); // CJK 标点符号
        }

        /// <summary>
        /// 获取字符串的显示宽度（全角2，半角1）
        /// </summary>
        static int GetDisplayWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            int width = 0;
            foreach (char c in text)
                width += IsFullWidth(c) ? 2 : 1;
            return width;
        }

        /// <summary>
        /// 按显示宽度右侧填充空格，确保总显示宽度达到 targetWidth
        /// </summary>
        static string PadRightToWidth(string text, int targetWidth)
        {
            int current = GetDisplayWidth(text);
            if (current >= targetWidth)
                return text;
            return text + new string(' ', targetWidth - current);
        }

        /// <summary>
        /// 按显示宽度截断字符串，若超长则末尾加省略号（省略号占1个宽度）
        /// </summary>
        static string TruncateToWidth(string text, int maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0)
                return "";
            if (GetDisplayWidth(text) <= maxWidth)
                return text;

            int width = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int charWidth = IsFullWidth(text[i]) ? 2 : 1;
                if (width + charWidth > maxWidth)
                {
                    // 需要截断，如果连一个字符都放不下，返回省略号
                    if (i == 0)
                        return "…";
                    string truncated = text.Substring(0, i);
                    // 确保加省略号后不超出 maxWidth
                    while (GetDisplayWidth(truncated) + 1 > maxWidth && truncated.Length > 0)
                        truncated = truncated.Substring(0, truncated.Length - 1);
                    return truncated + "…";
                }
                width += charWidth;
            }
            return text;
        }

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

        static double audioPositionAtNext = 0;

        public static T DeepCopy<T>(T obj)
        {
            string json = JsonSerializer.Serialize(obj);
            return JsonSerializer.Deserialize<T>(json);
        }

        static void UpdateFileNow(string path)
        {
            if (activeConfig.saveAudioProgress)
            {
                try
                {
                    activeConfig.Progress = MainTrack.CurrentPosition;
                }
                catch { }
            }
            else
            {
                activeConfig.Progress = 0;
            }
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(activeConfig));
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
                    UpdateFileNow(path);
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
                        index = activeConfig.index;
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
                index = activeConfig.index;
                md = tmp;
            }
            catch { }
        }

        static class packageInfo
        {
            public static readonly Version version = new("2026.2.1");
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
            public bool autoPlay { get; set; } = false;
            public bool saveAudioProgress { get; set; } = true;
            public double Progress { get; set; } = 0;
        }

        static string targetConfigPath = "";

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
                UpdateFileNow(file);
            }
        }

        static void Main(string[] arg)
        {
            string loadArg = null;
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
                path = $"{path}/AppData/Local";
            }
            path = $"{path}/zakoAudioPlayer";
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            targetConfigPath = $"{path}/config.json";
            LoadConfig(targetConfigPath);
            if (activeConfig.saveAudioProgress && loadArg == null)
            {
                audioPositionAtNext = activeConfig.Progress;
            }
            startNow = (activeConfig.autoPlay || loadArg != null);
            if (activeConfig.volume > 1)
            {
                activeConfig.volume = 1;
            }
            if (activeConfig.volume < 0)
            {
                activeConfig.volume = 0;
            }
            configUpdateThread = new Thread(() => UpdateFile($"{path}/config.json"));
            if (loadArg == null)
            {
                LoadFile(activeConfig.targetFile);
            }
            else
            {
                LoadFile(loadArg);
            }
            LoadMusic();
            configUpdateThread.Start();
            mainPrintThread.Start();
            mainMusicManagerThread.Start();
            mainKeyListenerThread.Start();
            index = activeConfig.index;
            mainUpdateThread.Start();
            if (
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            )
            {
                PosixSignalRegistration.Create(PosixSignal.SIGINT, HandlePosixSignal); // Ctrl+C
                PosixSignalRegistration.Create(PosixSignal.SIGTERM, HandlePosixSignal); // kill 命令的默认信号
            }
        }

        private static void HandlePosixSignal(PosixSignalContext context)
        {
            context.Cancel = true; // 避免进程被立即终止
            try
            {
                audio.UnloadTrack("Main");
            }
            catch { }
            try
            {
                UpdateFileNow(targetConfigPath);
            }
            catch { }
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
                if (index != activeConfig.index)
                {
                    activeConfig.index = index;
                }
                if (lastSpeaker != activeConfig.speaker)
                {
                    lastSpeaker = activeConfig.speaker;
                    var devs = audio.GetAudioDevices();
                    short def = 0;
                    for (short i = 0; i < devs.Count; i++)
                    {
                        if (devs[i].Name == activeConfig.speaker)
                        {
                            bool tmp1 = false;
                            double tmp2 = 0;
                            try
                            {
                                tmp1 = MainTrack.IsPlaying;
                                tmp2 = MainTrack.CurrentPosition;
                            }
                            catch
                            {
                                tmp1 = activeConfig.autoPlay;
                                tmp2 = activeConfig.saveAudioProgress ? activeConfig.Progress : 0;
                            }
                            audio.SwitchDevice(i);
                            lastPlaying = "";
                            startNow = tmp1;
                            audioPositionAtNext = tmp2;
                        }
                    }
                }
                if (index >= music.Count)
                {
                    index = index - music.Count;
                }
                if (index < 0)
                {
                    index = music.Count - 1;
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
                            try
                            {
                                MainTrack.Play();
                            }
                            catch { }

                            startNow = false;
                        }
                        if (audioPositionAtNext != 0)
                        {
                            try
                            {
                                MainTrack.SeekToSeconds(audioPositionAtNext);
                            }
                            catch { }

                            audioPositionAtNext = 0;
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
                if (GetDisplayWidth(t1) % 2 == 1)
                {
                    t1 = $"{t1} ";
                }
                if (targetLength > GetDisplayWidth(t1) - 1)
                {
                    targetLength = GetDisplayWidth(t1) - 1;
                }
                t1 = $" zakoAudioPlayer - {Page} ".Substring(0, targetLength);
                string t2 = new('=', mid - (GetDisplayWidth(t1) / 2));
                string t3 = new(
                    '=',
                    (int)(BufferSize[0] - GetDisplayWidth(t1) - GetDisplayWidth(t2))
                );
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
            string t_1 = $" {metaData[0]} ";
            int targetLength = (int)BufferSize[0] - 1;
            if (targetLength >= GetDisplayWidth(t_1))
            {
                targetLength = GetDisplayWidth(t_1);
            }
            t_1 = $"{TruncateToWidth(t_1, targetLength)}";
            string t_2 = new(' ', mid - GetDisplayWidth(t_1) / 2);
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
            if (GetDisplayWidth(t1) > t3 || GetDisplayWidth(t2) > t3 || true)
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
                Console.Write($"{t1}{new(' ', t3 - GetDisplayWidth(t1))}");
                Console.ResetColor();
                Console.Write(apart + ' ');
                Console.Write($"{new(' ', t3 - GetDisplayWidth(t2))}{t2}");
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
                        bool tmp1 = false;
                        try
                        {
                            tmp1 = MainTrack.IsPlaying;
                        }
                        catch { }
                        startNow = tmp1;
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
            if (GetDisplayWidth(t2) > BufferSize[0] - GetDisplayWidth(t1))
            {
                t2 = t2.Substring(0, (int)BufferSize[0] - GetDisplayWidth(t1) - 1);
            }
            int t3 = (int)((BufferSize[0] - GetDisplayWidth(t1) - GetDisplayWidth(t2)) / 2);
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
            int progressBarLength = (int)(BufferSize[0] - GetDisplayWidth(t5));
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
            if (GetDisplayWidth(t2) > BufferSize[0] - GetDisplayWidth(t1))
            {
                t2 = TruncateToWidth(t2, (int)BufferSize[0] - GetDisplayWidth(t1) - 1);
            }
            int t3 = (int)((BufferSize[0] - GetDisplayWidth(t1) - GetDisplayWidth(t2)) / 2);
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
                    int t12 = (int)((Length - GetDisplayWidth(t11)) / 2);
                    int t13 = (int)(Length - GetDisplayWidth(t11) - t12);
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
            int height = (int)BufferSize[1] - HomePageData.usedLine - 0;
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
            int t1 = (int)(length - GetDisplayWidth(title)) / 2;
            int t2 = (int)length - GetDisplayWidth(title) - t1;
            string t3 = new('~', t1);
            string t4 = new('~', t2);
            Console.ResetColor();
            Console.Write(t3);
            Console.BackgroundColor = ConsoleColor.DarkYellow;
            Console.Write(title);
            Console.ResetColor();
            Console.Write(t4);
            Console.Write('\n');
            int t20 = length - 6 - 3;
            int t21 = t20 / 3;
            int t22 = t21;
            int t23 = t20 - t21 - t22;
            string t41 = "song";
            string t42 = "artist";
            string t43 = "album";
            Console.WriteLine(
                $"index {PadRightToWidth(t41, t21)}{PadRightToWidth(t42, t22)}{PadRightToWidth(t43, t23)}"
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

                string t31 = TruncateToWidth(target[0], t21);
                string t32 = TruncateToWidth(target[1], t22);
                string t33 = TruncateToWidth(target[2], t23);
                Console.BackgroundColor = ConsoleColor.Black;
                if (hit == index)
                {
                    Console.BackgroundColor = ConsoleColor.DarkGray;
                }
                Console.Write(t11);
                string t100 = TruncateToWidth(
                    $" {PadRightToWidth(t31, t21)} {PadRightToWidth(t32, t22)} {PadRightToWidth(t33, t23)} ",
                    t20 + 5
                );
                Console.Write(t100.Substring(0, t100.Length - 1));
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
                if (tmp == 0 || GetDisplayWidth(i) + tmp + 2 <= length)
                {
                    Console.BackgroundColor = ConsoleColor.DarkCyan;
                    Console.Write($"{i} ");
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.Write(' ');
                    tmp += GetDisplayWidth(i) + 2;
                }
                else
                {
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.Write(new string(' ', length - tmp));
                    Console.ResetColor();
                    Console.Write('\n');
                    HomePageData.usedLine++;
                    Console.BackgroundColor = ConsoleColor.DarkCyan;
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
