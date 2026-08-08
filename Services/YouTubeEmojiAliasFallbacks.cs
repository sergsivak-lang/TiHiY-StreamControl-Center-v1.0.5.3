using System.Reflection;
using System.Runtime.CompilerServices;

namespace TiHiY.StreamControlCenter.Services;

internal static class YouTubeEmojiAliasFallbacks
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        try
        {
            var field = typeof(PlatformRichContentBridge).GetField("YouTube", BindingFlags.Static | BindingFlags.NonPublic);
            var resolver = field?.GetValue(null);
            var add = resolver?.GetType().GetMethod("AddFallback", BindingFlags.Instance | BindingFlags.NonPublic);
            if (resolver is null || add is null) return;

            Add(add, resolver, ":person-turquoise-waving:", "uNSzQ2M106OC1L3VGzrOsGNjopboOv-m1bnZKFGuh0DxcceSpYHhYbuyggcgnYyaF3o-AQ");
            Add(add, resolver, ":goat-turqouise-white-horns:", "jMnX4lu5GnjBRgiPtX5FwFmEyKTlWFrr5voz-Auko35oP0t3-zhPxR3PQMYa-7KhDeDtrv4");
        }
        catch { }
    }

    private static void Add(MethodInfo add, object resolver, string name, string id) =>
        add.Invoke(resolver, new object[] { name, id, $"https://yt3.ggpht.com/{id}=s48-c" });
}
