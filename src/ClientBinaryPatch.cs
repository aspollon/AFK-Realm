using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CoAInstaller
{
    /// <summary>
    /// The two client-side fixes that cannot live in patch-Y.MPQ:
    /// * CoA Custom's Ascension.exe gets a customize thunk and the character-select context thunk;
    /// * its EsteriaAppearance.dll gets a two-second delay in the texture worker that otherwise
    ///   starts before the D3D9 device is ready.
    ///
    /// Both are version-checked. An unknown or already patched file is left alone. The patched
    /// copy is a temporary file next to the normal root files, so installing the client patch and
    /// exporting the ZIP both use it.
    /// </summary>
    static class ClientBinaryPatch
    {
        const uint ExeImageBase = 0x00400000;
        const uint ExeEappRva = 0x009FC000;
        const uint ExeCustomizeSite = 0x004D8EF9;
        const uint ExeSelectSite = 0x004E3E74;
        const uint ExeThunk = 0x00DFC760;
        const uint ExeMarker = 0x00DFC840;
        const uint ExeContextThunk = 0x00DFC880;

        const uint DllWorkerRva = 0x00041EF8;
        const uint DllThunkRva = 0x00054560;

        static readonly byte[] CustomizeBefore = FromHex("8d55e852895dfc");
        static readonly byte[] CustomizeThunk = FromHex(
            "9c60a1a0b1b60085c00f84b10000008b5018528b1db4c1df0081eb20ed02008d8bc0870200ffd183c404" +
            "83f82e741683f830741183f831740c83f832743383f833742eeb7b83ec10a1a0b1b600508d442404508d" +
            "8bb0fa0100ffd183c4080fb65424058d4de852e814e867ff83c410eb4f83ec10a1a0b1b600508d442404" +
            "508d8bb0fa0100ffd183c408bf050000000fb6143c8d4de852e8e4e767ff4783ff0d7cedbf000000000f" +
            "b69740c8df008d4de852e8c9e767ff4783ff047cea83c410619d8d55e852895dfce9d2c66dff");
        static readonly byte[] SelectContext = FromHex("9c6051ff15c8c1df0083c404619d");
        static readonly byte[] Hrc1 = Encoding.ASCII.GetBytes("HRC1");

        static readonly byte[] DllWorkerBefore = FromHex("6a0c6828160610");
        static readonly byte[] DllWorkerThunk = FromHex(
            "e800000000582d654505006068d0070000ff9088500500616a0c8d882816060051e979d9feff");

        struct Section
        {
            public int Header;
            public uint VirtualSize;
            public uint VirtualAddress;
            public uint RawSize;
            public uint RawOffset;
        }

        internal static void Prepare(ClientPatch.Plan plan)
        {
            foreach (var root in plan.RootFiles)
            {
                string name = Path.GetFileName(root.To ?? "").ToLowerInvariant();
                if (name != "ascension.exe" && name != "ascension-b.exe" && name != "esteriaappearance.dll") continue;
                if (!File.Exists(root.From)) continue;

                try
                {
                    byte[] source = File.ReadAllBytes(root.From);
                    byte[] patched = name == "esteriaappearance.dll" ? PatchEsteria(source) : PatchAscension(source);
                    if (patched == null) continue;

                    string dir = Path.Combine(Path.GetTempPath(), "afk-realm-client-patch");
                    Directory.CreateDirectory(dir);
                    string hash;
                    using (var sha = SHA256.Create())
                        hash = BitConverter.ToString(sha.ComputeHash(patched)).Replace("-", "").Substring(0, 16);
                    string temp = Path.Combine(dir, hash + "-" + Path.GetFileName(root.From));
                    File.WriteAllBytes(temp, patched);
                    root.From = temp;
                }
                catch
                {
                    // An unknown client is left unchanged; the Python tools in mod-custom-races can
                    // still be run by hand. Never stop the whole client patch for this.
                }
            }
        }

        static byte[] PatchAscension(byte[] source)
        {
            byte[] data = (byte[])source.Clone();
            if (data.Length < 0x100 || U16(data, 0) != 0x5A4D) return null;

            int nt = (int)U32(data, 0x3C);
            if (nt <= 0 || nt + 24 > data.Length) return null;
            int optional = nt + 24;
            int sectionCount = U16(data, nt + 6);
            int optionalSize = U16(data, nt + 20);
            if (U32(data, optional + 28) != ExeImageBase) return null;
            int sectionTable = optional + optionalSize;
            var sections = ReadSections(data, sectionTable, sectionCount);
            Section eapp;
            if (!sections.TryGetValue(".eapp", out eapp)) return null;
            if (eapp.VirtualAddress != ExeEappRva || eapp.RawSize != 0x800) return null;

            int site = FileOffset(sections, ExeCustomizeSite, ExeImageBase);
            if (site < 0 || !Same(data, site, CustomizeBefore)) return null;
            int selectSite = FileOffset(sections, ExeSelectSite, ExeImageBase);
            if (selectSite < 0 || data[selectSite] != 0xE9) return null;
            int oldTarget = (int)ExeSelectSite + 5 + BitConverter.ToInt32(data, selectSite + 1);

            // Grow .eapp by 0x400 bytes; it is the last section and SizeOfImage already covers it.
            int newRaw = 0xC00;
            int eappStart = (int)eapp.RawOffset;
            if (data.Length < eappStart + newRaw)
            {
                var grown = new byte[eappStart + newRaw];
                Buffer.BlockCopy(data, 0, grown, 0, data.Length);
                data = grown;
            }
            Put32(data, eapp.Header + 8, (uint)newRaw);
            Put32(data, eapp.Header + 16, (uint)newRaw);
            sections[".eapp"] = new Section { Header = eapp.Header, VirtualAddress = eapp.VirtualAddress, VirtualSize = (uint)newRaw, RawSize = (uint)newRaw, RawOffset = eapp.RawOffset };

            CopyAt(data, sections, ExeThunk, CustomizeThunk);
            CopyAt(data, sections, ExeMarker, Hrc1);
            byte[] context = new byte[SelectContext.Length + 5];
            Buffer.BlockCopy(SelectContext, 0, context, 0, SelectContext.Length);
            PutJump(context, SelectContext.Length, oldTarget, (int)ExeContextThunk + SelectContext.Length);
            CopyAt(data, sections, ExeContextThunk, context);
            PutJump(data, site, (int)ExeThunk, (int)ExeCustomizeSite);
            PutJump(data, selectSite, (int)ExeContextThunk, (int)ExeSelectSite);
            return data;
        }

        static byte[] PatchEsteria(byte[] source)
        {
            byte[] data = (byte[])source.Clone();
            if (data.Length < 0x100 || U16(data, 0) != 0x5A4D) return null;

            int nt = (int)U32(data, 0x3C);
            if (nt <= 0 || nt + 24 > data.Length) return null;
            int optional = nt + 24;
            int sectionCount = U16(data, nt + 6);
            int optionalSize = U16(data, nt + 20);
            uint imageBase = U32(data, optional + 28);
            int sectionTable = optional + optionalSize;
            var sections = ReadSections(data, sectionTable, sectionCount);

            uint siteVa = imageBase + DllWorkerRva;
            int site = FileOffset(sections, siteVa, imageBase);
            if (site < 0) return null;
            if (data[site] == 0xE9) return null;                         // already patched
            if (!Same(data, site, DllWorkerBefore)) return null;

            uint thunkVa = imageBase + DllThunkRva;
            int thunk = FileOffset(sections, thunkVa, imageBase);
            if (thunk < 0 || thunk + DllWorkerThunk.Length > data.Length) return null;
            for (int i = 0; i < DllWorkerThunk.Length; i++)
                if (data[thunk + i] != 0) return null;

            Buffer.BlockCopy(DllWorkerThunk, 0, data, thunk, DllWorkerThunk.Length);
            PutJump(data, site, (int)thunkVa, (int)siteVa);
            // The jump is five bytes; the two stock bytes after it are never reached.
            data[site + 5] = 0x90;
            data[site + 6] = 0x90;
            return data;
        }

        static Dictionary<string, Section> ReadSections(byte[] data, int table, int count)
        {
            var result = new Dictionary<string, Section>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; ++i)
            {
                int header = table + i * 40;
                if (header + 40 > data.Length) break;
                string name = Encoding.ASCII.GetString(data, header, 8).TrimEnd('\0');
                result[name] = new Section
                {
                    Header = header,
                    VirtualSize = U32(data, header + 8),
                    VirtualAddress = U32(data, header + 12),
                    RawSize = U32(data, header + 16),
                    RawOffset = U32(data, header + 20)
                };
            }
            return result;
        }

        static int FileOffset(Dictionary<string, Section> sections, uint va, uint imageBase)
        {
            uint rva = va - imageBase;
            foreach (var section in sections.Values)
            {
                if (section.VirtualAddress <= rva && rva < section.VirtualAddress + section.RawSize)
                    return (int)(section.RawOffset + (rva - section.VirtualAddress));
            }
            return -1;
        }

        static void CopyAt(byte[] data, Dictionary<string, Section> sections, uint va, byte[] bytes)
        {
            int offset = FileOffset(sections, va, ExeImageBase);
            if (offset < 0 || offset + bytes.Length > data.Length) throw new InvalidDataException("patch offset outside image");
            Buffer.BlockCopy(bytes, 0, data, offset, bytes.Length);
        }

        static void PutJump(byte[] data, int at, int target, int from)
        {
            int relative = target - (from + 5);
            data[at] = 0xE9;
            Put32(data, at + 1, (uint)relative);
        }

        static void PutJump(byte[] data, int at, int target, uint from)
        {
            PutJump(data, at, target, (int)from);
        }

        static void Put32(byte[] data, int at, uint value)
        {
            data[at] = (byte)value;
            data[at + 1] = (byte)(value >> 8);
            data[at + 2] = (byte)(value >> 16);
            data[at + 3] = (byte)(value >> 24);
        }

        static bool Same(byte[] data, int at, byte[] expected)
        {
            if (at < 0 || at + expected.Length > data.Length) return false;
            for (int i = 0; i < expected.Length; ++i)
                if (data[at + i] != expected[i]) return false;
            return true;
        }

        static ushort U16(byte[] data, int at) { return (ushort)(data[at] | data[at + 1] << 8); }
        static uint U32(byte[] data, int at)
        {
            return (uint)(data[at] | data[at + 1] << 8 | data[at + 2] << 16 | data[at + 3] << 24);
        }

        static byte[] FromHex(string text)
        {
            var result = new byte[text.Length / 2];
            for (int i = 0; i < result.Length; ++i)
                result[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
            return result;
        }
    }
}
