using EasyDevice;
using EasyScript;

namespace AutoSwshRng.Upstream;

public static class EasyConUpstreamInfo
{
    public static string? DeviceAssemblyName => typeof(DirectionKey).Assembly.GetName().Name;

    public static string? ScriptAssemblyName => typeof(GamePadKey).Assembly.GetName().Name;

    public static string? DirectionKeyTypeName => typeof(DirectionKey).FullName;

    public static string? GamePadKeyTypeName => typeof(GamePadKey).FullName;
}
