using System;
using System.IO;

namespace CoAInstaller
{
    /// <summary>
    /// HD for any official CoA64.exe, without DXVK. The official client opens its HD archives
    /// (patch-4, patch-5, patch-X, patch-U, patch-Z, patch-Q, patch-R, patch-V) and sets up the
    /// HD characters only after x64\d3d9.dll (DXVK) has loaded: one flag, set next to
    /// "DXVK loaded:", is checked in two places. Where DXVK does not load (no Vulkan, an old
    /// driver, an antivirus, a missing or wrong DLL) the client silently uses system D3D9 and
    /// drops every HD archive - the races are back on the old models.
    ///
    /// The native HD build (NativeHd) removes that check in the source, but exists for one
    /// package version only. This patch does the same to whatever CoA64.exe a package ships:
    /// both checks are found by their code, not by a checksum, so a newer CoA64 build is
    /// patched as well. Only "-hd 0" still turns HD off, as in the native build.
    ///
    ///   archive filter:  cmp byte ptr [flag], 0 / jne load-all   ->  jmp load-all
    ///   HD character setup:  cmp byte ptr [flag], 0 / je skip    ->  nop
    ///
    /// Both instructions must name the same flag; anything else leaves the file untouched.
    /// </summary>
    static class Coa64HdPatch
    {
        public enum Result { Patched, AlreadyPatched, NotFound }

        public static Result Apply(byte[] exe)
        {
            int filter = -1, setup = -1;
            long filterFlag = 0, setupFlag = 0;
            int filters = 0, setups = 0;
            for (int i = 0; i + 26 < exe.Length; i++)
            {
                // cmp byte ptr [rip + disp32], 0
                if (exe[i] != 0x80 || exe[i + 1] != 0x3D || exe[i + 6] != 0x00) continue;
                long flag = i + 7 + BitConverter.ToInt32(exe, i + 2);
                // ... jne/jmp rel8 ; lea rbx, [rip + table] ; lea rsi, [rip + end]
                if ((exe[i + 7] == 0x75 || exe[i + 7] == 0xEB) && exe[i + 9] == 0x48 && exe[i + 10] == 0x8D && exe[i + 11] == 0x1D &&
                    exe[i + 16] == 0x48 && exe[i + 17] == 0x8D && exe[i + 18] == 0x35)
                {
                    filter = i; filterFlag = flag; filters++;
                }
                // ... je rel32 (or six nops) ; mov dword ptr [rsp + 0x20], 1 ; mov r9d, 0x40
                bool je = exe[i + 7] == 0x0F && exe[i + 8] == 0x84;
                bool nops = exe[i + 7] == 0x90 && exe[i + 8] == 0x90 && exe[i + 9] == 0x90 && exe[i + 10] == 0x90 && exe[i + 11] == 0x90 && exe[i + 12] == 0x90;
                if ((je || nops) &&
                    exe[i + 13] == 0xC7 && exe[i + 14] == 0x44 && exe[i + 15] == 0x24 && exe[i + 16] == 0x20 && exe[i + 17] == 0x01 &&
                    exe[i + 18] == 0x00 && exe[i + 19] == 0x00 && exe[i + 20] == 0x00 &&
                    exe[i + 21] == 0x41 && exe[i + 22] == 0xB9 && exe[i + 23] == 0x40)
                {
                    setup = i; setupFlag = flag; setups++;
                }
            }
            if (filters != 1 || setups != 1 || filterFlag != setupFlag) return Result.NotFound;
            bool done = exe[filter + 7] == 0xEB && exe[setup + 7] == 0x90;
            if (done) return Result.AlreadyPatched;
            exe[filter + 7] = 0xEB;
            for (int k = 7; k < 13; k++) exe[setup + k] = 0x90;
            return Result.Patched;
        }

        /// <summary>Patches a CoA64.exe in place; the unpatched file is kept as .coa-patcher-backup.</summary>
        public static Result ApplyToFile(string path)
        {
            if (!File.Exists(path)) return Result.NotFound;
            byte[] exe = File.ReadAllBytes(path);
            Result result = Apply(exe);
            if (result != Result.Patched) return result;
            string backup = path + ".coa-patcher-backup";
            try { if (!File.Exists(backup)) File.Copy(path, backup, false); } catch { }
            CoaUninstall.Capture(path);
            File.WriteAllBytes(path, exe);
            return result;
        }
    }
}
