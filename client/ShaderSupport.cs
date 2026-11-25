// What src/ClientShaders.cs needs from AFK Realm, for the launcher (English only, Windows only).
namespace CoAInstaller
{
    static class L
    {
        public static string T(string text) => text;
        public static string F(string format, params object[] args) => string.Format(format, args);
    }

    static class Os
    {
        public const bool Windows = true;
    }

    static class Product
    {
        public const string FileStem = "AFK-Client";
    }
}
