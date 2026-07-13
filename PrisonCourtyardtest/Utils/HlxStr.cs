using System.Runtime.InteropServices;
using System.Text;

namespace PrisonCourtyardtest.Utils;

/// <summary>
/// Extension method to convert C# string to Haxe dc.String.
/// </summary>
public static class HlxStr
{
    public static dc.String AsHlxStr(this string text)
    {
        if (text == null) return null!;
        IntPtr utf8 = IntPtr.Zero;
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            utf8 = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, utf8, bytes.Length);
            Marshal.WriteByte(utf8, bytes.Length, 0);
            return dc.String.Class.fromUTF8.Invoke(utf8);
        }
        finally
        {
            if (utf8 != IntPtr.Zero) Marshal.FreeHGlobal(utf8);
        }
    }
}
