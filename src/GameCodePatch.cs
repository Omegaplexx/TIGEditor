using System;
using System.IO;
using System.IO.Compression;

namespace TigEditor
{
    // Patches to the game itself. Every feature is independent: one section
    // holding all of our code is appended to the file, and a feature is on or
    // off depending on whether a jump from the game code leads into it.
    //
    // A hook replaces a few bytes of the game with a jump into our section. Our
    // code does its work, restores what it replaced and jumps back, so the game
    // never notices. Addresses below are in the game executable.
    //
    //   level.ini       hook on the level loader at 0x443E66
    //   noclip          hook on the event poll at 0x446782, for F1 and the
    //                   noclip argument, plus a raised command line argument
    //                   limit at 0x341F7
    //   medal banner    hook on the banner at 0x42EB30
    //   custom art      two hooks. The one at 0x41DA50 remembers the game_1 and
    //                   later atlas objects with their names at start up; the
    //                   name arrives as a narrow string and the game prepends
    //                   res/ itself. On level load the atlas is dropped and
    //                   loaded again, and the hook at 0x4440C6 hands it the file
    //                   from the level folder instead of res/game_N.png.
    //                   The loaded image gets its own OpenGL texture number and
    //                   the original number is remembered, so switching back and
    //                   forth only swaps the number: nothing is reloaded or
    //                   deleted, and whatever holds the number keeps drawing
    //                   correctly. F2 works in levels with custom art only.
    //   level folder    the dialog lParam at 0x415DE6 and a hook on the returned
    //                   path at 0x415E06. The game has its own callback which
    //                   selects the folder we pass it. The path is kept in
    //                   tigmod-folder.bin, and if that folder is gone, the
    //                   nearest existing one up the tree is used.
    //   dpi            dpiAware in the manifest
    public static class GameCodePatch
    {
        public const uint CodeVa = 0x463000;
        public const int CodeRva = 0x63000;
        public const string SectionName = ".tigmod";

        public const int DefaultJumpInSection = 8288;
        public const int DefaultGravityInSection = 8296;
        public const int LevelHookInSection = 0;
        public const int KeyHookInSection = 768;
        public const int MedalHookInSection = 1408;
        public const int PathHookInSection = 1472;
        public const int FolderBufferInSection = 8872;
        public const int ResetHookInSection = 2112;
        public const int ImageHookInSection = 3264;
        public const int TextureHookInSection = 2496;

        // Places in the game and the bytes that were there to begin with.
        public const int LevelHookOffset = 0x43266;    // 0x443E66
        public const uint LevelHookVa = 0x443E66;
        public static readonly byte[] LevelOriginal =
            { 0xD9, 0xE8, 0x56, 0xD9, 0x1D, 0xB0, 0x86, 0x45, 0x00 };

        public const int KeyHookOffset = 0x45B82;      // 0x446782
        public const uint KeyHookVa = 0x446782;
        public static readonly byte[] KeyOriginal = { 0xFF, 0x25, 0xF0, 0xC2, 0x44, 0x00 };

        public const int MedalHookOffset = 0x2DF30;    // 0x42EB30
        public const uint MedalHookVa = 0x42EB30;
        public static readonly byte[] MedalOriginal = { 0x56, 0x6A, 0x05, 0x8B, 0xF1 };

        // Texture loading. The hook is needed to remember the atlas objects and
        // to hand them an image from the level folder.
        public const int TextureHookOffset = 0x1CE50;   // 0x41DA50
        public const uint TextureHookVa = 0x41DA50;
        public static readonly byte[] TextureOriginal = { 0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8 };

        // The image loading thunk, where the atlas file is swapped.

        // Places where the game starts a level that is not from a folder: the
        // stock art comes back there. The folder itself is not forgotten, so
        // entering a custom level again puts its images back.
        public static readonly int[] ResetOffsets = { 0x384F3, 0x3ADF3, 0x3D1B3, 0x3FC63 };
        public static readonly uint[] ResetVa = { 0x4390F3, 0x43B9F3, 0x43DDB3, 0x440863 };
        public const int ResetStubSize = 38;
        public static readonly byte[] ResetOriginal = { 0xA3, 0x70, 0x95, 0x45, 0x00 };

        // A safety check: every stub ends with the address it returns to in the
        // game. A wrong stub size would land the jump in the middle of someone
        // else's code and bring the game down.
        public static bool ResetStubsLookRight(byte[] file)
        {
            int raw = FindSectionRaw(file);
            if (raw < 0) return false;
            for (int i = 0; i < ResetVa.Length; i++)
            {
                int at = raw + ResetHookInSection + i * ResetStubSize;
                if (file[at] != 0x60) return false;                      // pushad
                uint back = BitConverter.ToUInt32(file, at + ResetStubSize - 5);
                if (back != ResetVa[i] + ResetOriginal.Length) return false;
            }
            return true;
        }

        // Audio sync measurement: time stamps into tigmod-sync.log. Decoding the
        // music from memory only stores times in variables, and the sound start
        // prints them afterwards.
        //
        // Every hook must give the game all its registers untouched. A system
        // call clobbers ecx and edx, and forgetting that in one wrapper brought
        // the game down on the very first track.
        //
        // Sounds load before the game window exists, so the writer avoids the
        // game's own facilities: it takes the time from the system and builds the
        // number itself by dividing by ten.
        public const int SyncDecodeInSection = 3776;
        public const int SyncPlayInSection = 4160;

        // The event poll hook is also installed for the level folder, so the keys
        // ask permission for themselves: F1 works with the noclip patch only,
        // F2 with the extra features only.
        public const int KeysNoClipInSection = 12272;
        public const int KeysTexInSection = 12280;

        // Holding the level: right after the game calls for music we wait until
        // the music thread reports that sound has started. The wait is capped.
        public const int HoldHookInSection = 7616;
        public const int HoldExtraInSection = 12320;

        // The hold sits on music thread creation, the one place every entry into
        // a level passes through. The previous spot, right after the level code
        // called for music, did not always fire: that routine has four callers.
        public const int HoldHookOffset = 0x13AA0;      // 0x4146A0
        public static readonly byte[] HoldOriginal = { 0xE8, 0x15, 0xFA, 0x02, 0x00 };

        public const int PinFlagInSection = 12296;






        public const int SyncMemoryInSection = 4800;

        public const int MemoryCallOffset = 0x134CD;    // 0x4140CD, decoding from memory
        public static readonly byte[] MemoryOriginal = { 0xFF, 0x15, 0x18, 0xC0, 0x44, 0x00 };

        public const int DecodeCallOffset = 0x138C1;    // 0x4144C1, decoding a track
        public static readonly byte[] DecodeOriginal = { 0xFF, 0x15, 0x1C, 0xC0, 0x44, 0x00 };

        public const int PlayCallOffset = 0x1364A;      // 0x41424A, starting a source
        public static readonly byte[] PlayOriginal = { 0xFF, 0x15, 0x90, 0xC2, 0x44, 0x00 };


        public static bool SyncHookOn(byte[] file) { return file[PlayCallOffset] == 0xE8; }



        // Measuring and holding live on the same hooks: they are turned on by
        // flags in our section, not by different patches to the game.
        public static bool PinOn(byte[] file)
        {
            int raw = FindSectionRaw(file);
            return raw >= 0 && SyncHookOn(file)
                   && BitConverter.ToInt32(file, raw + PinFlagInSection) != 0;
        }

        public static int HoldExtraMs(byte[] file)
        {
            int raw = FindSectionRaw(file);
            if (raw < 0 || !SyncHookOn(file)) return 0;
            int v = BitConverter.ToInt32(file, raw + HoldExtraInSection);
            return v < 0 || v > 1000 ? 0 : v;
        }

        public static void SetSyncOptions(byte[] file, bool pin, int holdMs)
        {
            int raw = FindSectionRaw(file);
            if (raw < 0) return;
            BitConverter.GetBytes(pin ? 1 : 0).CopyTo(file, raw + PinFlagInSection);
            BitConverter.GetBytes(Math.Max(0, Math.Min(1000, holdMs))).CopyTo(file, raw + HoldExtraInSection);
        }


        public static void SetSyncHook(byte[] file, bool on)
        {
            if (on)
            {
                Divert(file, DecodeCallOffset, SyncDecodeInSection, 0x4144C1, DecodeOriginal.Length, 0xE8);
                Divert(file, PlayCallOffset, SyncPlayInSection, 0x41424A, PlayOriginal.Length, 0xE8);
                Divert(file, MemoryCallOffset, SyncMemoryInSection, 0x4140CD, MemoryOriginal.Length, 0xE8);
                Divert(file, HoldHookOffset, HoldHookInSection, 0x4146A0, HoldOriginal.Length, 0xE8);
            }
            else
            {
                DecodeOriginal.CopyTo(file, DecodeCallOffset);
                PlayOriginal.CopyTo(file, PlayCallOffset);
                MemoryOriginal.CopyTo(file, MemoryCallOffset);
                HoldOriginal.CopyTo(file, HoldHookOffset);
            }
        }

        // Reading a resource into memory, which is how the game takes sounds.
        // We swap the path when the level folder holds a file of the same name.
        public const int ResHookInSection = 5120;
        // File opening. The game calls it from nine places, and everything it
        // reads, sounds included, goes through them.
        public static readonly int[] OpenOffsets =
        {
            0x1591D, 0x1B96A, 0x434FC, 0x43E1C, 0x43EAC,
            0x43F39, 0x4420C, 0x4423D, 0x4437B
        };
        public static readonly uint[] OpenVa =
        {
            0x41651D, 0x41C56A, 0x4440FC, 0x444A1C, 0x444AAC,
            0x444B39, 0x444E0C, 0x444E3D, 0x444F7B
        };
        public static readonly byte[] OpenOriginal = { 0xFF, 0x15, 0x10, 0xC1, 0x44, 0x00 };

        public const int ImageHookOffset = 0x434C6;     // 0x4440C6
        public const uint ImageHookVa = 0x4440C6;
        public static readonly byte[] ImageOriginal = { 0xFF, 0x25, 0x28, 0xC3, 0x44, 0x00 };

        public const int PathHookOffset = 0x15206;     // 0x415E06
        public const uint PathHookVa = 0x415E06;
        public static readonly byte[] PathOriginal = { 0xFF, 0x15, 0x38, 0xC3, 0x44, 0x00 };

        // The folder dialog. The game can open at a given place but passes an
        // empty lParam to the callback, so we put our own buffer there.
        public const int FolderParamOffset = 0x151E6;  // 0x415DE6
        public static readonly byte[] FolderOriginal =
            { 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00 };

        // Our own dialog callback: besides selecting the folder it expands the
        // tree down to it, so nothing has to be walked by hand.
        public const int CallbackParamOffset = 0x151DE;   // 0x415DDE
        public static readonly byte[] CallbackOriginal =
            { 0xC7, 0x44, 0x24, 0x2C, 0xA0, 0x5C, 0x41, 0x00 };

        // The command line argument limit, a cmp eax, 3.
        public const int ParamLimitOffset = 0x341F7;

        // The application manifest.
        public const int ManifestOffset = 0x57F4C;
        public const int ManifestLength = 598;
        public const string DpiManifest = "<assembly xmlns=\"urn:schemas-microsoft-com:asm.v1\" manifestVersion=\"1.0\"><dependency><dependentAssembly><assemblyIdentity type=\"win32\" name=\"Microsoft.VC90.CRT\" version=\"9.0.21022.8\" processorArchitecture=\"x86\" publicKeyToken=\"1fc8b3b9a1e18e3b\"/></dependentAssembly></dependency><application xmlns=\"urn:schemas-microsoft-com:asm.v3\"><windowsSettings><dpiAware xmlns=\"http://schemas.microsoft.com/SMI/2005/WindowsSettings\">true</dpiAware></windowsSettings></application></assembly>";

        // The section code is stored compressed, four times smaller in the build.
        private const int PayloadSize = 14760;
        // The section code sits in a file of its own and is embedded as a
        // resource: as a byte table in the source it took fifteen kilobytes.
        private static byte[] Packed()
        {
            using (Stream s = System.Reflection.Assembly.GetExecutingAssembly()
                       .GetManifestResourceStream("patch.gz"))
            {
                byte[] all = new byte[s.Length];
                int done = 0;
                while (done < all.Length)
                {
                    int n = s.Read(all, done, all.Length - done);
                    if (n <= 0) break;
                    done += n;
                }
                return all;
            }
        }


        private static byte[] payload;

        private static byte[] Payload
        {
            get
            {
                if (payload != null) return payload;
                using (MemoryStream packed = new MemoryStream(Packed()))
                using (GZipStream unpacked = new GZipStream(packed, CompressionMode.Decompress))
                {
                    byte[] all = new byte[PayloadSize];
                    int read = 0;
                    while (read < all.Length)
                    {
                        int n = unpacked.Read(all, read, all.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    payload = all;
                }
                return payload;
            }
        }

        public static bool HasSection(byte[] file)
        {
            return FindSectionRaw(file) >= 0;
        }

        public static bool LevelHookOn(byte[] file) { return file[LevelHookOffset] == 0xE9; }
        public static bool KeyHookOn(byte[] file) { return file[KeyHookOffset] == 0xE9; }

        // Noclip is recognised by its own patch in the game, not by the event
        // poll hook: the level folder patch needs that same hook.
        public static bool NoClipReady(byte[] file)
        {
            return KeyHookOn(file) && file[ParamLimitOffset] == 4;
        }
        public static bool MedalHookOn(byte[] file) { return file[MedalHookOffset] == 0xE9; }
        public static bool FolderHookOn(byte[] file)
        {
            return file[PathHookOffset] == 0xE8 &&
                   BitConverter.ToInt32(file, FolderParamOffset + 4) != 0;
        }

        // Appends the section if it is not there yet.
        public static bool EnsureSection(byte[] file, out byte[] result, out string error)
        {
            result = file;
            error = null;
            if (HasSection(file)) return true;

            int pe = BitConverter.ToInt32(file, 0x3C);
            int sectionCount = BitConverter.ToUInt16(file, pe + 6);
            int optSize = BitConverter.ToUInt16(file, pe + 20);
            int sectionTable = pe + 24 + optSize;
            int fileAlign = BitConverter.ToInt32(file, pe + 24 + 36);
            int sectionAlign = BitConverter.ToInt32(file, pe + 24 + 32);

            if (fileAlign <= 0 || sectionAlign <= 0) { error = S.ErrBadHeaders; return false; }
            if (sectionTable + (sectionCount + 1) * 40 > BitConverter.ToInt32(file, pe + 24 + 60))
            { error = S.ErrNoSectionRoom; return false; }

            int raw = Align(file.Length, fileAlign);
            int rawSize = Align(Payload.Length, fileAlign);
            byte[] output = new byte[raw + rawSize];
            Buffer.BlockCopy(file, 0, output, 0, file.Length);
            Buffer.BlockCopy(Payload, 0, output, raw, Payload.Length);

            int header = sectionTable + sectionCount * 40;
            for (int i = 0; i < 8; i++)
                output[header + i] = i < SectionName.Length ? (byte)SectionName[i] : (byte)0;
            BitConverter.GetBytes(Payload.Length).CopyTo(output, header + 8);
            BitConverter.GetBytes(CodeRva).CopyTo(output, header + 12);
            BitConverter.GetBytes(rawSize).CopyTo(output, header + 16);
            BitConverter.GetBytes(raw).CopyTo(output, header + 20);
            BitConverter.GetBytes(0).CopyTo(output, header + 24);
            BitConverter.GetBytes(0).CopyTo(output, header + 28);
            BitConverter.GetBytes(0).CopyTo(output, header + 32);
            BitConverter.GetBytes(unchecked((int)0xE0000020)).CopyTo(output, header + 36);

            BitConverter.GetBytes((ushort)(sectionCount + 1)).CopyTo(output, pe + 6);
            BitConverter.GetBytes(Align(CodeRva + Payload.Length, sectionAlign)).CopyTo(output, pe + 24 + 56);

            // .rdata and .text become writable: physics constants and the noclip byte live there
            for (int i = 0; i < sectionCount; i++)
            {
                int h = sectionTable + i * 40;
                string name = System.Text.Encoding.ASCII.GetString(output, h, 8).TrimEnd('\0');
                if (name != ".rdata" && name != ".text") continue;
                int flags = BitConverter.ToInt32(output, h + 36);
                BitConverter.GetBytes(flags | unchecked((int)0x80000000)).CopyTo(output, h + 36);
            }

            // a randomised load base does not fit the absolute addresses in our code
            int dllChars = BitConverter.ToUInt16(output, pe + 24 + 70);
            BitConverter.GetBytes((ushort)(dllChars & ~0x0040)).CopyTo(output, pe + 24 + 70);

            result = output;
            return true;
        }

        // A jump or a call into our code in place of a spot in the game. They
        // differ in the first byte only, and the rest is padded with nops.
        private static void Divert(byte[] file, int offset, int codeOffset, uint hookVa,
                                   int padTo, byte opcode)
        {
            file[offset] = opcode;
            BitConverter.GetBytes((int)(CodeVa + codeOffset - (hookVa + 5))).CopyTo(file, offset + 1);
            for (int i = 5; i < padTo; i++) file[offset + i] = 0x90;
        }

        private static void Jump(byte[] file, int offset, int codeOffset, uint hookVa, int padTo)
        {
            Divert(file, offset, codeOffset, hookVa, padTo, 0xE9);
        }

        private static void Call(byte[] file, int offset, int codeOffset, uint hookVa, int padTo)
        {
            Divert(file, offset, codeOffset, hookVa, padTo, 0xE8);
        }

        public static void SetLevelHook(byte[] file, bool on)
        {
            if (on) Jump(file, LevelHookOffset, LevelHookInSection, LevelHookVa, LevelOriginal.Length);
            else LevelOriginal.CopyTo(file, LevelHookOffset);
        }

        // The event poll hook, wanted by both noclip and the level folder: the
        // levels folder is prepared inside it.
        public static void SetKeyHook(byte[] file, bool on)
        {
            if (on) Jump(file, KeyHookOffset, KeyHookInSection, KeyHookVa, KeyOriginal.Length);
            else KeyOriginal.CopyTo(file, KeyHookOffset);
        }

        public static void SetKeyFlags(byte[] file, bool noClip, bool textures)
        {
            int raw = FindSectionRaw(file);
            if (raw < 0) return;
            BitConverter.GetBytes(noClip ? 1 : 0).CopyTo(file, raw + KeysNoClipInSection);
            BitConverter.GetBytes(textures ? 1 : 0).CopyTo(file, raw + KeysTexInSection);
        }

        // The noclip marker: a raised argument limit, so the game accepts the
        // word noclip on the command line.
        public static void SetNoClip(byte[] file, bool on)
        {
            if (on && file[ParamLimitOffset] == 3) file[ParamLimitOffset] = 4;
            else if (!on && file[ParamLimitOffset] == 4) file[ParamLimitOffset] = 3;
        }

        public static void SetMedalHook(byte[] file, bool on)
        {
            if (on) Jump(file, MedalHookOffset, MedalHookInSection, MedalHookVa, MedalOriginal.Length);
            else MedalOriginal.CopyTo(file, MedalHookOffset);
        }

        public static bool TextureHookOn(byte[] file) { return file[TextureHookOffset] == 0xE9; }

        public static void SetTextureHook(byte[] file, bool on)
        {
            if (on)
            {
                Jump(file, TextureHookOffset, TextureHookInSection, TextureHookVa, TextureOriginal.Length);
                Jump(file, ImageHookOffset, ImageHookInSection, ImageHookVa, ImageOriginal.Length);
                for (int i = 0; i < OpenOffsets.Length; i++)
                    Divert(file, OpenOffsets[i], ResHookInSection, OpenVa[i], OpenOriginal.Length, 0xE8);
                for (int i = 0; i < ResetOffsets.Length; i++)
                    Jump(file, ResetOffsets[i], ResetHookInSection + i * ResetStubSize,
                         ResetVa[i], ResetOriginal.Length);
            }
            else
            {
                TextureOriginal.CopyTo(file, TextureHookOffset);
                ImageOriginal.CopyTo(file, ImageHookOffset);
                foreach (int off in OpenOffsets) OpenOriginal.CopyTo(file, off);
                foreach (int off in ResetOffsets) ResetOriginal.CopyTo(file, off);
            }
        }

        public static void SetFolderHook(byte[] file, bool on)
        {
            if (on)
            {
                Call(file, PathHookOffset, PathHookInSection, PathHookVa, PathOriginal.Length);
                BitConverter.GetBytes(CodeVa + (uint)FolderBufferInSection)
                    .CopyTo(file, FolderParamOffset + 4);
                // Our callback with tree expansion crashes the dialog, so the
                // game's own callback stays: it selects the folder, which is enough.
                CallbackOriginal.CopyTo(file, CallbackParamOffset);
            }
            else
            {
                PathOriginal.CopyTo(file, PathHookOffset);
                FolderOriginal.CopyTo(file, FolderParamOffset);
                CallbackOriginal.CopyTo(file, CallbackParamOffset);
            }
        }


        public static void SetDpiAware(byte[] file, bool aware, byte[] original)
        {
            if (aware)
            {
                byte[] text = System.Text.Encoding.ASCII.GetBytes(
                    DpiManifest.PadRight(ManifestLength, ' '));
                Array.Copy(text, 0, file, ManifestOffset, ManifestLength);
            }
            else if (original != null && original.Length >= ManifestOffset + ManifestLength)
            {
                Array.Copy(original, ManifestOffset, file, ManifestOffset, ManifestLength);
            }
        }

        public static bool IsDpiAware(byte[] file)
        {
            if (file.Length < ManifestOffset + ManifestLength) return false;
            return System.Text.Encoding.ASCII.GetString(file, ManifestOffset, ManifestLength)
                       .Contains("dpiAware");
        }

        public static int FindSectionRaw(byte[] file)
        {
            int pe = BitConverter.ToInt32(file, 0x3C);
            int count = BitConverter.ToUInt16(file, pe + 6);
            int table = pe + 24 + BitConverter.ToUInt16(file, pe + 20);
            for (int i = 0; i < count; i++)
            {
                int h = table + i * 40;
                if (System.Text.Encoding.ASCII.GetString(file, h, 8).TrimEnd('\0') == SectionName)
                    return BitConverter.ToInt32(file, h + 20);
            }
            return -1;
        }

        private static int Align(int value, int align)
        {
            int rest = value % align;
            return rest == 0 ? value : value + align - rest;
        }
    }
}
