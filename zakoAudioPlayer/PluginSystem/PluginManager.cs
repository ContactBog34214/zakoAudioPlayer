// PluginManager.cs
using System.Reflection;

namespace zakoAudioPlayer.Plugin;

public class PluginManager : IDisposable
{
    // 管理器自身版本号（用于兼容性检查）
    public int ManagerVersion { get; init; } = 1;

    private readonly Dictionary<
        string,
        (PluginBase Instance, PluginLoadContext Context)
    > _loadedPlugins = new();

    /// <summary>
    /// 加载一个插件
    /// </summary>
    /// <param name="dllPath">插件 dll 完整路径</param>
    /// <returns>是否加载成功</returns>
    public bool LoadPlugin(string dllPath, Action<PluginBase>? Hook = null)
    {
        try
        {
            string pluginDir = Path.GetDirectoryName(dllPath)!;
            var context = new PluginLoadContext(pluginDir);
            var assembly = context.LoadFromAssemblyPath(dllPath);

            // 查找继承自 PluginBase 的非抽象类
            var pluginType = assembly
                .GetTypes()
                .FirstOrDefault(t => t.IsSubclassOf(typeof(PluginBase)) && !t.IsAbstract);

            if (pluginType == null)
            {
                Console.WriteLine($"未找到有效的插件类型: {dllPath}");
                return false;
            }

            var plugin = (PluginBase)Activator.CreateInstance(pluginType)!;

            // 检查 id 唯一性
            if (_loadedPlugins.ContainsKey(plugin.Id))
            {
                Console.WriteLine($"插件 ID 重复: {plugin.Id}，已存在，跳过加载");
                return false;
            }

            // 版本兼容性检查
            if (
                ManagerVersion < plugin.MinimumCompatibleVersion
                || ManagerVersion > plugin.MaximumCompatibleVersion
            )
            {
                Console.WriteLine(
                    $"插件 {plugin.Name} (ID: {plugin.Id}) 版本不兼容：需要 [{plugin.MinimumCompatibleVersion}, {plugin.MaximumCompatibleVersion}]，当前管理器版本 {ManagerVersion}"
                );
                return false;
            }

            // 加载成功，保存
            _loadedPlugins.Add(plugin.Id, (plugin, context));
            Console.WriteLine($"成功加载插件: {plugin.Name} (ID: {plugin.Id})");

            // 自动执行 Main 方法
            Thread run = new(() => plugin.Main());
            run.IsBackground = true;
            run.Start();
            if (Hook != null)
            {
                Action<PluginBase> hook = new(Hook);
                hook.Invoke(plugin);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载插件失败 {dllPath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 按 ID 卸载插件
    /// </summary>
    public bool UnloadPlugin(string pluginId)
    {
        if (!_loadedPlugins.TryGetValue(pluginId, out var entry))
            return false;

        // 释放插件资源（若实现了 IDisposable）
        (entry.Instance as IDisposable)?.Dispose();

        // 卸载 AssemblyLoadContext
        entry.Context.Unload();

        _loadedPlugins.Remove(pluginId);

        // 强制垃圾回收，帮助卸载
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Console.WriteLine($"已卸载插件: {pluginId}");
        return true;
    }

    /// <summary>
    /// 获取已加载的插件 ID 列表
    /// </summary>
    public IReadOnlyList<string> GetLoadedPluginIds() => _loadedPlugins.Keys.ToList();

    public void Dispose()
    {
        foreach (var id in _loadedPlugins.Keys.ToList())
        {
            UnloadPlugin(id);
        }
    }
}
