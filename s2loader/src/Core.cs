// s2 loader — ядро: формат сейва, образы, настройки, библиотека
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace S2Loader
{
    // ========================================================================
    //  SKATER.P (big-endian)
    //   [0..4]    CRC-32/BZIP2 по [4 .. 0x400000)
    //   [0x10..]  узлы: u32 flag | u32 hash | u32 type | u32 size | payload | pad4
    //   корень 0x59F2D89B: последний ребёнок — контейнер CAS (0x9946) с базой cas_db (образ скейтера)
    // ========================================================================
    class SaveFile
    {
        public const int CrcEnd = 0x400000;
        const uint CareerHash = 0x2FBC8E87;
        const uint ProfileHash = 0x900625F7;
        const uint InventoryHash = 0xDC90B828;
        const uint CasContainer = 0x00009946;
        const uint CasBlob = 0x631BF1BC;
        const int MoneyRel = 0x13560;
        const uint MoneyId = 0x2B;
        public const uint MaxMoney = 99999999;
        public const int NickMax = 31;

        public string Path;
        public byte[] Data;
        public int MoneyOffset = -1;
        public int NickOffset = -1;
        public string Nick = "";
        public int Items = -1;
        public int InvPayload = -1;
        public int InvSize;
        public int CasOffset = -1;      // начало 16-байтового заголовка контейнера CAS
        public int CasLength;           // заголовок + payload
        public bool CasIsLast;
        public bool CrcOk;

        public static readonly string[] Unlockables = {
            "cac_board_unlocks", "cac_clothing_appleyard", "cac_clothing_barbee",
            "cac_clothing_bigblack", "cac_clothing_brown", "cac_clothing_busenitz",
            "cac_clothing_cardiel", "cac_clothing_carroll", "cac_clothing_cole",
            "cac_clothing_dill", "cac_clothing_duffy", "cac_clothing_dyrdek",
            "cac_clothing_haslam", "cac_clothing_johnson", "cac_clothing_kennedy",
            "cac_clothing_koston", "cac_clothing_ladd", "cac_clothing_navarette",
            "cac_clothing_puig", "cac_clothing_rattray", "cac_clothing_szafranski",
            "cac_clothing_way", "cac_shoe_unlocks", "cac_truck_unlocks",
            "cac_wheel_unlocks"
        };

        public static readonly string[] Sponsors = {
            "cac_board_almost_inventory", "cac_board_antihero_inventory", "cac_board_aws_inventory",
            "cac_board_baker_inventory", "cac_board_blind_inventory", "cac_board_chocolate_inventory",
            "cac_board_cliche_inventory", "cac_board_creature_inventory", "cac_board_element_inventory",
            "cac_board_flip_inventory", "cac_board_girl_inventory", "cac_board_planb_inventory",
            "cac_board_real_inventory", "cac_board_zero_inventory", "cac_branded_gvr",
            "cac_branded_jam1", "cac_branded_jam2", "cac_branded_jam3",
            "cac_branded_mega", "cac_branded_street1", "cac_branded_street2",
            "cac_branded_street3", "cac_clothing_sbm", "cac_clothing_thrasher",
            "cac_demo_dyrdek", "cac_hom_damage", "cac_hom_grabbag",
            "cac_hom_points", "cac_hom_style", "cac_kotm",
            "cac_online_amatuer", "cac_online_icon", "cac_online_legend",
            "cac_online_pro", "cac_path_dyrdek", "cac_prostatus_almost",
            "cac_prostatus_antihero", "cac_prostatus_aws", "cac_prostatus_baker",
            "cac_prostatus_blind", "cac_prostatus_chocolate", "cac_prostatus_cliche",
            "cac_prostatus_creature", "cac_prostatus_element", "cac_prostatus_flip",
            "cac_prostatus_girl", "cac_prostatus_planb", "cac_prostatus_real",
            "cac_prostatus_zero", "cac_shoe_adidas_inventory", "cac_shoe_circa_inventory",
            "cac_shoe_dc_inventory", "cac_shoe_dvs_inventory", "cac_shoe_emerica_inventory",
            "cac_shoe_es_inventory", "cac_shoe_etnies_inventory", "cac_shoe_fallen_inventory",
            "cac_shoe_globe_inventory", "cac_shoe_icecream_inventory", "cac_shoe_ipath_inventory",
            "cac_shoe_lakai_inventory", "cac_shoe_macbeth_inventory", "cac_shoe_nike_inventory",
            "cac_shoe_osiris_inventory", "cac_shoe_supra_inventory", "cac_shoe_vans_inventory",
            "cac_shoe_vox_inventory", "cac_truck_destructo_inventory", "cac_truck_independent_inventory",
            "cac_truck_krux_inventory", "cac_truck_royal_inventory", "cac_truck_silver_inventory",
            "cac_truck_tensor_inventory", "cac_truck_thunder_inventory", "cac_truck_venture_inventory",
            "cac_wheel_autobahn_inventory", "cac_wheel_fillmore_inventory", "cac_wheel_gold_inventory",
            "cac_wheel_hubba_inventory", "cac_wheel_momentum_inventory", "cac_wheel_ricta_inventory",
            "cac_wheel_spitfire_inventory"
        };

        public static readonly string[] DlcPacks = {
            "cac_dlc_first_gear_pack_inventory", "cac_throwback_pack_inventory"
        };

        public static int GearTotal { get { return Unlockables.Length + Sponsors.Length; } }

        // ------------------------------------------------------------ низкий уровень
        static uint[] table;
        static uint[] Table()
        {
            if (table != null) return table;
            table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i << 24;
                for (int k = 0; k < 8; k++)
                    c = (c & 0x80000000) != 0 ? (c << 1) ^ 0x04C11DB7 : (c << 1);
                table[i] = c;
            }
            return table;
        }

        public static uint Crc(byte[] d, int start, int end)
        {
            uint[] t = Table();
            uint c = 0xFFFFFFFF;
            for (int i = start; i < end; i++)
                c = (c << 8) ^ t[((c >> 24) ^ d[i]) & 0xFF];
            return ~c;
        }

        public static uint BE(byte[] d, int o)
        {
            return ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];
        }

        public static void PutBE(byte[] d, int o, uint v)
        {
            d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v;
        }

        public static uint NameHash(string s)
        {
            uint h = 0xFFFFFFFF;
            foreach (char c in s) h = unchecked(h * 33 + (uint)c);
            return h;
        }

        class Node { public uint Hash; public uint Type; public int Size; public int Header; public int Payload; public int Depth; }

        static void Walk(byte[] d, int o, int end, List<Node> outList, int depth)
        {
            if (depth > 32) throw new Exception("tree too deep");
            while (o + 16 <= end)
            {
                Node n = new Node();
                n.Header = o;
                n.Hash = BE(d, o + 4);
                n.Type = BE(d, o + 8);
                uint size = BE(d, o + 12);
                n.Payload = o + 16;
                n.Depth = depth;
                if (size > (uint)(end - n.Payload)) throw new Exception(string.Format("broken node at 0x{0:X}", o));
                n.Size = (int)size;
                outList.Add(n);
                if (n.Type == 0) Walk(d, n.Payload, n.Payload + n.Size, outList, depth + 1);
                o = (n.Payload + n.Size + 3) & ~3;
            }
        }

        // ------------------------------------------------------------ загрузка
        public static SaveFile Load(string path)
        {
            SaveFile s = new SaveFile();
            s.Path = path;
            s.Data = File.ReadAllBytes(path);
            s.Analyze();
            return s;
        }

        public static SaveFile FromBytes(byte[] data, string path)
        {
            SaveFile s = new SaveFile();
            s.Path = path;
            s.Data = data;
            s.Analyze();
            return s;
        }

        int RootEnd { get { return 0x20 + (int)BE(Data, 0x1C); } }

        public void Analyze()
        {
            byte[] d = Data;
            if (d.Length < CrcEnd + 4) throw new Exception("not a SKATER.P (file too small)");
            CrcOk = BE(d, 0) == Crc(d, 4, CrcEnd);
            int total = (int)BE(d, 0x1C);
            if (total <= 0 || 0x20 + total > CrcEnd) throw new Exception("doesn't look like a Skate 2 save");
            List<Node> nodes = new List<Node>();
            Walk(d, 0x10, 0x20 + total, nodes, 0);

            MoneyOffset = -1; NickOffset = -1; Nick = ""; Items = -1; InvPayload = -1; InvSize = 0;
            CasOffset = -1; CasLength = 0; CasIsLast = false;
            for (int i = 0; i < nodes.Count; i++)
            {
                Node n = nodes[i];
                if (n.Hash == CareerHash && n.Size >= MoneyRel + 4)
                {
                    int o = n.Payload + MoneyRel;
                    if (BE(d, o - 12) == 4 && BE(d, o - 8) == MoneyId && BE(d, o - 4) == 4) MoneyOffset = o;
                }
                else if (n.Hash == ProfileHash && n.Size >= 40 && BE(d, n.Payload + 4) == 0x20)
                {
                    NickOffset = n.Payload + 8;
                    int e = NickOffset;
                    while (e < NickOffset + 32 && d[e] != 0) e++;
                    Nick = Encoding.UTF8.GetString(d, NickOffset, e - NickOffset);
                }
                else if (n.Hash == InventoryHash && n.Size >= 12)
                {
                    Items = (int)BE(d, n.Payload + 8);
                    InvPayload = n.Payload; InvSize = n.Size;
                }
                else if (n.Hash == CasContainer && n.Type == 0 && n.Depth == 1 && i + 1 < nodes.Count
                         && nodes[i + 1].Hash == CasBlob)
                {
                    CasOffset = n.Header;
                    CasLength = 16 + n.Size;
                    CasIsLast = ((n.Payload + n.Size + 3) & ~3) >= RootEnd;
                }
            }
        }

        public bool HasMoney { get { return MoneyOffset >= 0; } }
        public bool HasInventory { get { return InvPayload >= 0; } }
        public bool HasOutfit { get { return CasOffset >= 0 && CasIsLast; } }
        public bool HasCareer { get { return HasMoney && HasInventory; } }
        public uint Money { get { return HasMoney ? BE(Data, MoneyOffset) : 0; } }

        // ------------------------------------------------------------ деньги / ник
        public void SetMoney(uint v)
        {
            if (!HasMoney) throw new Exception("no career in this save - start career in game and save");
            if (v > MaxMoney) v = MaxMoney;
            PutBE(Data, MoneyOffset, v);
        }

        public void SetNick(string nick)
        {
            if (NickOffset < 0) throw new Exception("no profile in this save");
            nick = (nick ?? "").Trim();
            if (nick.Length == 0) throw new Exception("name is empty");
            foreach (char c in nick)
                if (c < 0x20 || c > 0x7E) throw new Exception("name: latin letters, digits and basic symbols only");
            byte[] b = Encoding.ASCII.GetBytes(nick);
            if (b.Length > NickMax) throw new Exception("name longer than " + NickMax + " characters");
            for (int i = 0; i < 32; i++) Data[NickOffset + i] = i < b.Length ? b[i] : (byte)0;
            Nick = nick;
        }

        // ------------------------------------------------------------ открытия
        HashSet<ulong> ReadUnlocks(out int cnt)
        {
            cnt = (int)BE(Data, InvPayload + 8);
            HashSet<ulong> set = new HashSet<ulong>();
            for (int i = 0; i < cnt; i++)
            {
                int o = InvPayload + 12 + i * 21;
                set.Add(((ulong)BE(Data, o + 4) << 32) | BE(Data, o + 12));
            }
            return set;
        }

        static ulong Key(uint cat, string name) { return ((ulong)cat << 32) | NameHash(name); }

        public int CountGearUnlocked()
        {
            if (!HasInventory) return 0;
            int cnt, have = 0;
            HashSet<ulong> set = ReadUnlocks(out cnt);
            foreach (string n in Unlockables) if (set.Contains(Key(15, n))) have++;
            foreach (string n in Sponsors) if (set.Contains(Key(16, n))) have++;
            return have;
        }

        public bool DlcUnlocked()
        {
            if (!HasInventory) return false;
            int cnt;
            HashSet<ulong> set = ReadUnlocks(out cnt);
            foreach (string n in DlcPacks) if (!set.Contains(Key(16, n))) return false;
            return true;
        }

        public int UnlockAllGear(bool withDlc)
        {
            if (!HasInventory) throw new Exception("no unlock list in this save - start career");
            int cnt;
            HashSet<ulong> set = ReadUnlocks(out cnt);
            int cap = (InvSize - 12) / 21;
            int added = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                if (pass == 2 && !withDlc) break;
                uint cat = pass == 0 ? 15u : 16u;
                string[] list = pass == 0 ? Unlockables : (pass == 1 ? Sponsors : DlcPacks);
                foreach (string n in list)
                {
                    ulong k = Key(cat, n);
                    if (set.Contains(k)) continue;
                    if (cnt >= cap) throw new Exception("unlock list is full");
                    int o = InvPayload + 12 + cnt * 21;
                    PutBE(Data, o, 4); PutBE(Data, o + 4, cat); PutBE(Data, o + 8, 4); PutBE(Data, o + 12, NameHash(n));
                    PutBE(Data, o + 16, 1); Data[o + 20] = 0;
                    set.Add(k); cnt++; added++;
                }
            }
            PutBE(Data, InvPayload + 8, (uint)cnt);
            Items = cnt;
            return added;
        }

        // ------------------------------------------------------------ образ (CAS)
        public byte[] GetOutfit()
        {
            if (!HasOutfit) throw new Exception("skater look not found in this save");
            byte[] b = new byte[CasLength];
            Array.Copy(Data, CasOffset, b, 0, CasLength);
            PutBE(b, 0, 0);
            return b;
        }

        public static bool IsOutfitBlob(byte[] b)
        {
            if (b == null || b.Length < 48) return false;
            if (BE(b, 4) != CasContainer || BE(b, 8) != 0) return false;
            if ((long)BE(b, 12) + 16 != b.Length) return false;
            return BE(b, 16 + 4) == CasBlob;
        }

        public void ApplyOutfit(byte[] blob)
        {
            if (!IsOutfitBlob(blob)) throw new Exception("outfit file is damaged");
            if (!HasOutfit) throw new Exception("skater look not found in this save");
            int start = CasOffset;
            int newEnd = (start + blob.Length + 3) & ~3;
            if (newEnd > CrcEnd) throw new Exception("outfit doesn't fit into the save");
            byte[] nd = new byte[Data.Length];
            Array.Copy(Data, 0, nd, 0, start);
            Array.Copy(blob, 0, nd, start, blob.Length);
            PutBE(nd, start, BE(Data, start));           // флаг узла берём из сейва
            PutBE(nd, 0x1C, (uint)(newEnd - 0x20));      // новый размер корня
            Data = nd;
            Analyze();
        }

        // ------------------------------------------------------------ запись
        public void FixCrc()
        {
            PutBE(Data, 0, Crc(Data, 4, CrcEnd));
            CrcOk = true;
        }

        public void WriteTo(string path)
        {
            FixCrc();
            string tmp = path + ".s2tmp";
            File.WriteAllBytes(tmp, Data);
            if (File.Exists(path)) File.Copy(tmp, path, true); else File.Move(tmp, path);
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // ========================================================================
    //  части образа — по названиям слотов внутри cas_db
    // ========================================================================
    static class OutfitInfo
    {
        static readonly string[,] Known = {
            { "OuterTorso", "top" }, { "InnerTorso", "undershirt" }, { "Pants", "pants" }, { "Feet", "shoes" },
            { "Socks", "socks" }, { "Hair", "hair" }, { "Hat", "hat" }, { "Glasses", "glasses" },
            { "Accessory", "accessory" }, { "Jewellery", "jewellery" }, { "WristItem", "wristband" },
            { "Gloves", "gloves" }, { "SkateBoard", "deck" }, { "SkateTruck", "trucks" },
            { "SkateWheel", "wheels" }, { "Rostral", "face" }, { "Organ", "body" }, { "FacialHair", "facial hair" },
            { "Tattoo", "tattoo" }, { "Pads", "pads" }, { "Helmet", "helmet" }
        };

        public static List<string> Parts(byte[] blob)
        {
            List<string> res = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            int i = 0;
            while (i < blob.Length)
            {
                if (blob[i] >= 'A' && blob[i] <= 'Z')
                {
                    int j = i;
                    while (j < blob.Length && ((blob[j] >= 'A' && blob[j] <= 'Z') || (blob[j] >= 'a' && blob[j] <= 'z'))) j++;
                    if (j - i >= 3 && j < blob.Length && blob[j] == 0)
                    {
                        string w = Encoding.ASCII.GetString(blob, i, j - i);
                        for (int k = 0; k < Known.GetLength(0); k++)
                            if (Known[k, 0] == w && seen.Add(w)) { res.Add(Known[k, 1]); break; }
                    }
                    i = j + 1;
                }
                else i++;
            }
            return res;
        }
    }

    // ========================================================================
    //  настройки (простой key=value)
    // ========================================================================
    class Config
    {
        public string GameExe = "";
        public string SaveDir = "";
        public string Slot = "";          // "" = сейв из игры как есть
        public string Outfit = "";        // "" = не менять образ
        public bool OptMoney;
        public bool OptUnlock;
        public bool OptDlc = true;
        public bool OptSync = true;
        public int KeepBackups = 40;

        string file;

        public static Config Load(string file)
        {
            Config c = new Config();
            c.file = file;
            if (!File.Exists(file)) return c;
            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "game": c.GameExe = v; break;
                    case "saves": c.SaveDir = v; break;
                    case "slot": c.Slot = v; break;
                    case "outfit": c.Outfit = v; break;
                    case "money": c.OptMoney = v == "1"; break;
                    case "unlock": c.OptUnlock = v == "1"; break;
                    case "dlc": c.OptDlc = v == "1"; break;
                    case "sync": c.OptSync = v == "1"; break;
                    case "keep": int n; if (int.TryParse(v, out n) && n >= 5) c.KeepBackups = n; break;
                }
            }
            return c;
        }

        public void Save()
        {
            try
            {
                string[] lines = {
                    "game=" + GameExe, "saves=" + SaveDir, "slot=" + Slot, "outfit=" + Outfit,
                    "money=" + (OptMoney ? "1" : "0"), "unlock=" + (OptUnlock ? "1" : "0"),
                    "dlc=" + (OptDlc ? "1" : "0"), "sync=" + (OptSync ? "1" : "0"), "keep=" + KeepBackups };
                File.WriteAllLines(file, lines, Encoding.UTF8);
            }
            catch { }
        }

        public string GameSave { get { return SaveDir.Length > 0 ? System.IO.Path.Combine(SaveDir, "SKATER.P") : ""; } }
        public bool GameOk { get { return GameExe.Length > 0 && File.Exists(GameExe); } }
        public bool SaveDirOk { get { return SaveDir.Length > 0 && Directory.Exists(SaveDir); } }
    }

    // ========================================================================
    //  библиотека: слоты сейвов, образы, бэкапы
    // ========================================================================
    class SlotInfo
    {
        public string Name;
        public string File;
        public bool Live;          // сейв прямо из папки игры
        public bool Valid;
        public string Error = "";
        public string Nick = "";
        public uint Money;
        public bool HasCareer;
        public int Gear;
        public bool Dlc;
        public DateTime Modified;
    }

    class OutfitEntry
    {
        public string Name;
        public string File;
        public string Image;       // может быть null
        public List<string> Parts = new List<string>();
        public int Size;
        public DateTime Modified;
    }

    class Library
    {
        public readonly string Root, SavesDir, OutfitsDir, BackupsDir;
        public List<SlotInfo> Slots = new List<SlotInfo>();
        public List<OutfitEntry> Outfits = new List<OutfitEntry>();
        Config cfg;

        public Library(string root, Config cfg)
        {
            Root = root; this.cfg = cfg;
            SavesDir = System.IO.Path.Combine(root, "saves");
            OutfitsDir = System.IO.Path.Combine(root, "outfits");
            BackupsDir = System.IO.Path.Combine(root, "backups");
            Directory.CreateDirectory(SavesDir);
            Directory.CreateDirectory(OutfitsDir);
            Directory.CreateDirectory(BackupsDir);
        }

        public static string SafeName(string s)
        {
            s = (s ?? "").Trim();
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            if (s.Length > 40) s = s.Substring(0, 40);
            return s;
        }

        static SlotInfo Describe(string name, string file, bool live)
        {
            SlotInfo si = new SlotInfo();
            si.Name = name; si.File = file; si.Live = live;
            try
            {
                if (!System.IO.File.Exists(file)) { si.Error = "file missing"; return si; }
                si.Modified = System.IO.File.GetLastWriteTime(file);
                SaveFile s = SaveFile.Load(file);
                si.Valid = true;
                si.Nick = s.Nick;
                si.Money = s.Money;
                si.HasCareer = s.HasCareer;
                si.Gear = s.CountGearUnlocked();
                si.Dlc = s.DlcUnlocked();
            }
            catch (Exception ex) { si.Error = ex.Message; }
            return si;
        }

        public void Refresh()
        {
            Slots.Clear();
            Slots.Add(Describe("game save", cfg.GameSave, true));
            List<SlotInfo> list = new List<SlotInfo>();
            foreach (string dir in Directory.GetDirectories(SavesDir))
            {
                string f = System.IO.Path.Combine(dir, "SKATER.P");
                if (!System.IO.File.Exists(f)) continue;
                list.Add(Describe(System.IO.Path.GetFileName(dir), f, false));
            }
            list.Sort(delegate(SlotInfo a, SlotInfo b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            Slots.AddRange(list);

            Outfits.Clear();
            foreach (string f in Directory.GetFiles(OutfitsDir, "*.s2fit"))
            {
                OutfitEntry o = new OutfitEntry();
                o.File = f;
                o.Name = System.IO.Path.GetFileNameWithoutExtension(f);
                o.Modified = System.IO.File.GetLastWriteTime(f);
                foreach (string ext in new string[] { ".png", ".jpg", ".jpeg", ".bmp" })
                {
                    string img = System.IO.Path.ChangeExtension(f, ext);
                    if (System.IO.File.Exists(img)) { o.Image = img; break; }
                }
                try
                {
                    byte[] b = System.IO.File.ReadAllBytes(f);
                    o.Size = b.Length;
                    if (SaveFile.IsOutfitBlob(b)) o.Parts = OutfitInfo.Parts(b);
                }
                catch { }
                Outfits.Add(o);
            }
            Outfits.Sort(delegate(OutfitEntry a, OutfitEntry b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
        }

        public SlotInfo FindSlot(string name)
        {
            foreach (SlotInfo s in Slots) if (!s.Live && s.Name == name) return s;
            return null;
        }

        public OutfitEntry FindOutfit(string name)
        {
            foreach (OutfitEntry o in Outfits) if (o.Name == name) return o;
            return null;
        }

        public string UniqueSlotName(string baseName)
        {
            baseName = SafeName(baseName);
            if (baseName.Length == 0) baseName = "slot";
            string n = baseName; int k = 2;
            while (Directory.Exists(System.IO.Path.Combine(SavesDir, n))) n = baseName + " " + (k++);
            return n;
        }

        public string UniqueOutfitName(string baseName)
        {
            baseName = SafeName(baseName);
            if (baseName.Length == 0) baseName = "outfit";
            string n = baseName; int k = 2;
            while (System.IO.File.Exists(System.IO.Path.Combine(OutfitsDir, n + ".s2fit"))) n = baseName + " " + (k++);
            return n;
        }

        public string AddSlotFromFile(string src, string name)
        {
            SaveFile.Load(src);   // проверка
            name = UniqueSlotName(name);
            string dir = System.IO.Path.Combine(SavesDir, name);
            Directory.CreateDirectory(dir);
            System.IO.File.Copy(src, System.IO.Path.Combine(dir, "SKATER.P"), true);
            return name;
        }

        public void RenameSlot(string oldName, string newName)
        {
            newName = SafeName(newName);
            if (newName.Length == 0) throw new Exception("empty name");
            string a = System.IO.Path.Combine(SavesDir, oldName), b = System.IO.Path.Combine(SavesDir, newName);
            if (Directory.Exists(b)) throw new Exception("slot already exists");
            Directory.Move(a, b);
        }

        public void DeleteSlot(string name)
        {
            string dir = System.IO.Path.Combine(SavesDir, name);
            string f = System.IO.Path.Combine(dir, "SKATER.P");
            if (System.IO.File.Exists(f)) Backup(f, "deleted_" + name);
            Directory.Delete(dir, true);
        }

        public string AddOutfit(byte[] blob, string name)
        {
            if (!SaveFile.IsOutfitBlob(blob)) throw new Exception("not a skater outfit");
            name = UniqueOutfitName(name);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(OutfitsDir, name + ".s2fit"), blob);
            return name;
        }

        public void RenameOutfit(OutfitEntry o, string newName)
        {
            newName = SafeName(newName);
            if (newName.Length == 0) throw new Exception("empty name");
            string nf = System.IO.Path.Combine(OutfitsDir, newName + ".s2fit");
            if (System.IO.File.Exists(nf)) throw new Exception("outfit already exists");
            System.IO.File.Move(o.File, nf);
            if (o.Image != null)
                System.IO.File.Move(o.Image, System.IO.Path.ChangeExtension(nf, System.IO.Path.GetExtension(o.Image)));
        }

        public void DeleteOutfit(OutfitEntry o)
        {
            System.IO.File.Delete(o.File);
            if (o.Image != null && System.IO.File.Exists(o.Image)) System.IO.File.Delete(o.Image);
        }

        public void SetOutfitImage(OutfitEntry o, string img)
        {
            if (o.Image != null && System.IO.File.Exists(o.Image)) System.IO.File.Delete(o.Image);
            string ext = System.IO.Path.GetExtension(img).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp") ext = ".png";
            System.IO.File.Copy(img, System.IO.Path.ChangeExtension(o.File, ext), true);
        }

        public string Backup(string file, string tag)
        {
            if (!System.IO.File.Exists(file)) return null;
            string name = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + SafeName(tag) + ".P";
            string dst = System.IO.Path.Combine(BackupsDir, name);
            System.IO.File.Copy(file, dst, true);
            Prune();
            return dst;
        }

        void Prune()
        {
            try
            {
                string[] all = Directory.GetFiles(BackupsDir, "*.P");
                if (all.Length <= cfg.KeepBackups) return;
                Array.Sort(all);
                for (int i = 0; i < all.Length - cfg.KeepBackups; i++) System.IO.File.Delete(all[i]);
            }
            catch { }
        }

        public string LatestBackup(string tagContains)
        {
            string[] all = Directory.GetFiles(BackupsDir, "*.P");
            Array.Sort(all);
            for (int i = all.Length - 1; i >= 0; i--)
                if (tagContains == null || System.IO.Path.GetFileName(all[i]).Contains(tagContains)) return all[i];
            return null;
        }

        // поиск SKATER.P рядом с игрой и в типичных местах
        public static List<string> FindSaves(string gameExe)
        {
            List<string> found = new List<string>();
            List<string> roots = new List<string>();
            if (!string.IsNullOrEmpty(gameExe) && System.IO.File.Exists(gameExe))
            {
                string gd = System.IO.Path.GetDirectoryName(gameExe);
                roots.Add(gd);
                string up = System.IO.Path.GetDirectoryName(gd);
                if (up != null) roots.Add(up);
            }
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            visited = 0;
            foreach (string r in roots) Scan(r, 0, found);
            return found;
        }

        static int visited;

        static void Scan(string dir, int depth, List<string> found)
        {
            if (depth > 5 || found.Count > 20 || visited > 8000 || string.IsNullOrEmpty(dir)) return;
            visited++;
            try
            {
                string f = System.IO.Path.Combine(dir, "SKATER.P");
                if (System.IO.File.Exists(f) && !found.Contains(dir)) found.Add(dir);
                string name = System.IO.Path.GetFileName(dir).ToLowerInvariant();
                if (depth > 0 && (name == "windows" || name == "node_modules" || name.StartsWith("."))) return;
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    string n = System.IO.Path.GetFileName(sub).ToLowerInvariant();
                    if (depth >= 1 && !(n.Contains("skate") || n.Contains("save") || n.Contains("ea") || n.Contains("user") || n.Contains("content") || n.Contains("data") || depth < 2)) continue;
                    Scan(sub, depth + 1, found);
                }
            }
            catch { }
        }
    }
}
