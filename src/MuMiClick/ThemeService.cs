using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MuMiClick;

internal static class ThemeService
{
    public static void Apply(Window window, bool darkMode)
    {
        var palette = darkMode ? DarkPalette : LightPalette;
        foreach (var (key, color) in palette) SetBrush(window, key, color);
        ApplyTitleBar(window, darkMode);
        window.SourceInitialized += (_, _) => ApplyTitleBar(window, darkMode);
    }

    private static void SetBrush(Window window, string key, string color)
    {
        if (window.Resources[key] is SolidColorBrush)
            window.Resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private static void ApplyTitleBar(Window window, bool darkMode)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var enabled = darkMode ? 1 : 0;
        try
        {
            if (NativeMethods.DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                NativeMethods.DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#F3F6FA", ["SurfaceBrush"] = "#FFFFFF", ["SurfaceAltBrush"] = "#F7F9FC",
        ["FieldBrush"] = "#F8FAFC", ["ListBrush"] = "#FBFCFE", ["TextBrush"] = "#172033",
        ["MutedBrush"] = "#687386", ["LineBrush"] = "#E3E8F0", ["StatusBarBrush"] = "#172033",
        ["StatusPanelBrush"] = "#263248", ["StatusDetailBrush"] = "#B8C2D5", ["StatusCountBrush"] = "#D9E1EF",
        ["ModeBackgroundBrush"] = "#E8EEF8", ["ModeBorderBrush"] = "#D8E1EF", ["ModeTextBrush"] = "#4E5B70",
        ["ModeDotBrush"] = "#8793A5", ["DangerSurfaceBrush"] = "#FFF8F8", ["DangerBorderBrush"] = "#F1C9CB",
        ["DangerTextBrush"] = "#C8323A", ["ActiveEventBrush"] = "#DBEAFE", ["ActiveEventTextBrush"] = "#123B7A",
        ["ActiveEventBorderBrush"] = "#60A5FA"
    };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#111827", ["SurfaceBrush"] = "#1F2937", ["SurfaceAltBrush"] = "#273449",
        ["FieldBrush"] = "#111C2D", ["ListBrush"] = "#111C2D", ["TextBrush"] = "#F3F7FF",
        ["MutedBrush"] = "#A9B5C8", ["LineBrush"] = "#37465E", ["StatusBarBrush"] = "#0B1220",
        ["StatusPanelBrush"] = "#1F2B40", ["StatusDetailBrush"] = "#BBC8DB", ["StatusCountBrush"] = "#DCE7F7",
        ["ModeBackgroundBrush"] = "#223453", ["ModeBorderBrush"] = "#3B5D91", ["ModeTextBrush"] = "#D3E2FF",
        ["ModeDotBrush"] = "#9FB8E8", ["DangerSurfaceBrush"] = "#42252B", ["DangerBorderBrush"] = "#7A3B46",
        ["DangerTextBrush"] = "#FFB4B8", ["ActiveEventBrush"] = "#173A6B", ["ActiveEventTextBrush"] = "#E6F1FF",
        ["ActiveEventBorderBrush"] = "#60A5FA"
    };
}
