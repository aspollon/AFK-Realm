using System;
using System.Runtime.InteropServices;

// .NET 8 keeps ProtectedData in a NuGet package. The shared code only needs it for the GitHub token, so this is the
// Windows data protection API itself (the same as .NET Framework uses, so tokens saved by the classic version still
// open). Elsewhere there is no such store: the token is kept as it is, in a file only the user can read.
namespace System.Security.Cryptography
{
    public enum DataProtectionScope { CurrentUser = 0, LocalMachine = 1 }

    public static class ProtectedData
    {
        public static byte[] Protect(byte[] userData, byte[] optionalEntropy, DataProtectionScope scope)
        {
            if (!OperatingSystem.IsWindows()) return (byte[])userData.Clone();
            return Call(userData, optionalEntropy, scope, true);
        }

        public static byte[] Unprotect(byte[] encryptedData, byte[] optionalEntropy, DataProtectionScope scope)
        {
            if (!OperatingSystem.IsWindows()) throw new CryptographicException("No data protection on this system.");
            return Call(encryptedData, optionalEntropy, scope, false);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Blob { public int Size; public IntPtr Data; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptProtectData(ref Blob dataIn, string description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptUnprotectData(ref Blob dataIn, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob dataOut);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr mem);

        static byte[] Call(byte[] data, byte[] entropy, DataProtectionScope scope, bool protect)
        {
            var input = new Blob(); var extra = new Blob(); var output = new Blob();
            try
            {
                input.Size = data.Length; input.Data = Marshal.AllocHGlobal(Math.Max(1, data.Length));
                Marshal.Copy(data, 0, input.Data, data.Length);
                if (entropy != null && entropy.Length > 0)
                {
                    extra.Size = entropy.Length; extra.Data = Marshal.AllocHGlobal(entropy.Length);
                    Marshal.Copy(entropy, 0, extra.Data, entropy.Length);
                }
                int flags = 0x1 | (scope == DataProtectionScope.LocalMachine ? 0x4 : 0);   // UI forbidden, machine key
                bool ok = protect ? CryptProtectData(ref input, "", ref extra, IntPtr.Zero, IntPtr.Zero, flags, ref output)
                                  : CryptUnprotectData(ref input, IntPtr.Zero, ref extra, IntPtr.Zero, IntPtr.Zero, flags, ref output);
                if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data);
                if (extra.Data != IntPtr.Zero) Marshal.FreeHGlobal(extra.Data);
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }
    }
}
