#nullable disable

using dc;
using dc.cine;
using dc.level;
using dc.libs;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

using LevelInfo = Hashlink.Virtuals.virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_;

namespace TestCorruptPlusLevel
{
    public class TestCorruptPlusLevelMain : ModBase, IOnGameEndInit, IOnHeroUpdate
    {
        private const string MainLevelId = "PrisonCorruptDepths";
        private const string TransitionLevelId = "T_PrisonCorruptDepths";
        private const string PrisonCorruptId = "PrisonCorrupt";
        private const string BossArenaId = "DeathArena";
        private const string JidufuhBiomeId = "PrisonCorruptJidufuh";

        private const int VK_P = 0x50;
        private const int VK_O = 0x4F;
        private const int VK_B = 0x42;
        private const int VK_T = 0x54;

        private static string _logPath = "";
        private static string _savedAtlasName;
        private bool _pWasDown, _oWasDown, _bWasDown, _tWasDown;
        private bool _cdbReady;

        public TestCorruptPlusLevelMain(ModInfo info) : base(info) { }

        // ═══════════════════════════════════════════
        // 生命周期
        // ═══════════════════════════════════════════

        public override void Initialize()
        {
            base.Initialize();
            try
            {
                _logPath = Info.ModRoot.GetFilePath("xdt_v37_log.txt");
                File.WriteAllText(_logPath, "[TestCorruptPlusLevel] V37 complete log start\r\n", Encoding.UTF8);
            }
            catch { }
            Log("V37: 怪物种类+掉落平衡完善 | 流程: PrisonCorrupt→深层牢房→DeathArena→T_Bridge");
            Hook__LevelStruct.get += Hook__LevelStruct_get;
        }

        void IOnGameEndInit.OnGameEndInit()
        {
            try
            {
                string res = Info.ModRoot!.GetFilePath("res.pak");
                FsPak.Instance.FileSystem.loadModPak(ToHLString(res));
                Log("ResPak: " + res);
            }
            catch (Exception ex) { Log("ResPak: " + RealError(ex)); }
            Log("等待 CDB 就绪... (O=腐化牢房 P=深层牢房 B=Boss)");
        }

        private bool _injected;

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (!_injected)
            {
                if (Data.Class.level?.byId != null && Data.Class.mob?.byId != null)
                {
                    InjectAll();
                    _injected = true;
                    _cdbReady = SafeLevelExists(MainLevelId);
                    Log("自动注入完成 CDB=" + _cdbReady);
                }
            }

            if (KeyPressed(VK_P, ref _pWasDown)) OnPPressed();
            if (KeyPressed(VK_O, ref _oWasDown)) OnOPressed();
            if (KeyPressed(VK_B, ref _bWasDown)) OnBPressed();
            if (KeyPressed(VK_T, ref _tWasDown)) OnTPressed();
        }

        private bool KeyPressed(int key, ref bool wasDown)
        {
            bool down = IsKeyDown(key);
            bool r = down && !wasDown;
            wasDown = down;
            return r;
        }

        // ═══════════════════════════════════════════
        // 注入
        // ═══════════════════════════════════════════

        private void InjectAll()
        {
            try
            {
                var refl = new _Reflect();
                if (Data.Class.level?.byId == null || Data.Class.mob?.byId == null)
                { Log("Inject: CDB 未就绪"); return; }

                InjectMainLevel(refl);
                SaveOriginalAtlas(refl);
                PatchLevelLogo("Inject");
            }
            catch (Exception ex) { Log("InjectAll: " + RealError(ex)); }
        }

        private bool _mainInjected;

        private void InjectMainLevel(_Reflect refl)
        {
            if (_mainInjected) return;

            var tKey = ToHLString("PrisonCourtyard");
            if (!Data.Class.level.byId.exists(tKey)) { Log("Inject: PrisonCourtyard 不存在"); return; }

            // ── PrisonCorruptDepths 主关卡 ──
            var mainKey = ToHLString(MainLevelId);
            if (!Data.Class.level.byId.exists(mainKey))
            {
                object mainTpl = Data.Class.level.byId.get(tKey);
                refl.setField(mainTpl, ToHLString("id"), ToHLString(MainLevelId));
                refl.setField(mainTpl, ToHLString("group"), 0);
                refl.setField(mainTpl, ToHLString("name"), ToHLString("深层腐化牢房"));
                refl.setField(mainTpl, ToHLString("biome"), ToHLString("PrisonCorrupt"));

                // ── 怪物：保留原版 9 种，调整数量和难度 ──
                BalanceLevelMobs(refl, mainTpl);

                // ── 掉落/平衡（参考 Ossuary worldDepth=2 标准）──
                SetLootBalance(refl, mainTpl);

                // ── 出口 → Boss 房间 ──
                FixNextToSingle(refl, mainTpl, BossArenaId);

                Data.Class.level.byId.set(mainKey, mainTpl);
                Log("Inject: " + MainLevelId + " exit → " + BossArenaId);
            }
            else Log("Inject: " + MainLevelId + " 已存在");

            // ── 过渡关卡 ──
            var transKey = ToHLString(TransitionLevelId);
            if (!Data.Class.level.byId.exists(transKey))
            {
                var srcKey = ToHLString("T_OssuaryAfterPrison");
                if (Data.Class.level.byId.exists(srcKey))
                {
                    object transTpl = Data.Class.level.byId.get(srcKey);
                    refl.setField(transTpl, ToHLString("id"), transKey);
                    refl.setField(transTpl, ToHLString("name"), ToHLString("通往深层腐化牢房"));
                    FixNextToSingle(refl, transTpl, MainLevelId);
                    Data.Class.level.byId.set(transKey, transTpl);
                }
            }

            _mainInjected = true;
        }

        // ═══════════════════════════════════════════
        // Atlas 管理（保存原始值 + Hook 中精准 swap）
        // ═══════════════════════════════════════════

        private static void SaveOriginalAtlas(_Reflect refl)
        {
            if (_savedAtlasName != null) return;
            try
            {
                if (Data.Class.biome?.byId == null) return;
                var key = ToHLString("PrisonCorrupt");
                if (!Data.Class.biome.byId.exists(key)) return;
                object bio = Data.Class.biome.byId.get(key);
                dynamic dyn = bio;
                try { _savedAtlasName = dyn.atlasName?.ToString(); } catch { }
                Log("SaveAtlas: 原始=" + (_savedAtlasName ?? "null"));
            }
            catch { }
        }

        /// <summary>直接修改 PrisonCorrupt biome 的 atlasName</summary>
        private static void SetBiomeAtlasDirect(string atlasName)
        {
            try
            {
                if (Data.Class.biome?.byId == null) return;
                var key = ToHLString("PrisonCorrupt");
                if (!Data.Class.biome.byId.exists(key)) return;
                object bio = Data.Class.biome.byId.get(key);
                var refl = new _Reflect();
                refl.setField(bio, ToHLString("atlasName"), ToHLString(atlasName));
            }
            catch { }
        }

        // ═══════════════════════════════════════════
        // 怪物平衡（保留原版 9 种怪物，调参数）
        // ═══════════════════════════════════════════

        /// <summary>
        /// PrisonCourtyard 模板自带 9 种怪物（已验证正常生成）。
        /// 不改 mob 名称，只调整 quantityFactor 和 minDifficulty。
        /// </summary>
        private static void BalanceLevelMobs(_Reflect refl, object levelObj)
        {
            // 目标参数 (index → quantityFactor, minDifficulty)
            var targets = new (double qty, int minDiff)[] {
                (1.5, 0),  // [0] Zombie
                (1.8, 0),  // [1] Runner
                (0.6, 0),  // [2] Shielder
                (3.0, 0),  // [3] BatDasher
                (0.8, 0),  // [4] Grenader
                (0.3, 3),  // [5] ClusterGrenader
                (0.5, 2),  // [6] Ninja
                (0.3, 4),  // [7] AggressiveZombie
                (2.0, 1),  // [8] BatKamikaze
            };

            try
            {
                dynamic dynLvl = levelObj;
                object rawMobs;
                try { rawMobs = dynLvl.mobs; } catch { return; }
                if (rawMobs == null) return;

                dynamic mobsArr = rawMobs;
                int len;
                try { len = mobsArr.length; } catch { return; }

                object[] items = null;
                try { items = (object[])mobsArr.array; }
                catch
                {
                    try
                    {
                        var arrKey = ToHLString("array");
                        items = (object[])refl.GetType().GetMethod("getField")?.Invoke(refl, new[] { rawMobs, arrKey });
                    }
                    catch { }
                }
                if (items == null) return;

                int changed = 0;
                for (int i = 0; i < len && i < items.Length && i < targets.Length; i++)
                {
                    try
                    {
                        object entry = items[i];
                        if (entry == null) continue;
                        refl.setField(entry, ToHLString("quantityFactor"), targets[i].qty);
                        refl.setField(entry, ToHLString("minDifficulty"), targets[i].minDiff);
                        changed++;
                    }
                    catch { }
                }
                Log("Mobs: " + changed + "/" + len + " 条目已平衡");
            }
            catch { }
        }

        // ═══════════════════════════════════════════
        // 掉落/平衡
        // ═══════════════════════════════════════════

        private static void SetLootBalance(_Reflect refl, object levelObj)
        {
            var fields = new (string name, object value)[] {
                ("baseLootLevel",    3),
                ("minGold",          3000),
                ("mobDensity",       1.1),
                ("eliteWanderChance", 0.2),
                ("eliteRoomChance",  0.8),
                ("cellBonus",        0.2),
                ("tripleUps",        2),
                ("doubleUps",        2),
                ("quarterUpsBC3",    2),
                ("quarterUpsBC4",    1),
            };

            int set = 0;
            foreach (var f in fields)
            {
                try
                {
                    refl.setField(levelObj, ToHLString(f.name), f.value);
                    set++;
                }
                catch { }
            }
            Log("Loot: " + set + "/" + fields.Length + " 字段已设置 (Ossuary标准)");
        }

        // ═══════════════════════════════════════════
        // NextLevels 修正
        // ═══════════════════════════════════════════

        private static void FixNextToSingle(_Reflect refl, object levelObj, string target)
        {
            try
            {
                dynamic dyn = levelObj;
                object raw;
                try { raw = dyn.nextLevels; } catch { return; }
                if (raw == null) return;
                dynamic arr = raw;
                int len;
                try { len = arr.length; } catch { return; }
                if (len == 0) return;

                object[] items = null;
                try { items = (object[])arr.array; } catch { return; }
                if (items == null || items.Length == 0) return;

                object entry = items[0];
                refl.setField(entry, ToHLString("level"), ToHLString(target));
                while (true) { try { if (arr.length <= 1) break; arr.pop(); } catch { break; } }
                Log("NextLevels → " + target);
            }
            catch { }
        }

        private void PatchLevelLogo(string reason)
        {
            try
            {
                if (Assets.Class?.levelLogos?.textureCoordinateByLevelKind == null ||
                    Assets.Class.levelLogos.levelLogoTexture == null) return;

                var logos = Assets.Class.levelLogos;
                string[] fbIds = { "Ossuary", "PrisonCorrupt", "PrisonStart" };
                dc.String fbKey = null;
                foreach (var f in fbIds)
                {
                    fbKey = ToHLString(f);
                    try { if (logos.textureCoordinateByLevelKind.exists.Invoke(fbKey)) break; } catch { }
                    fbKey = null;
                }
                if (fbKey == null) return;

                object coord;
                try { coord = logos.textureCoordinateByLevelKind.get.Invoke(fbKey); } catch { return; }
                if (coord == null) return;

                foreach (var t in new[] { MainLevelId, TransitionLevelId, PrisonCorruptId, BossArenaId })
                    try { logos.textureCoordinateByLevelKind.set.Invoke(ToHLString(t), coord); } catch { }
                Log("Logo: ok " + reason);
            }
            catch { }
        }

        // ═══════════════════════════════════════════
        // 按键
        // ═══════════════════════════════════════════

        private void OnOPressed()
        {
            try
            {
                Log("O → 腐化牢房");
                PatchLevelLogo("O");
                LevelTransition.Class.@goto(ToHLString(PrisonCorruptId));
            }
            catch (Exception ex) { Log("O fail: " + RealError(ex)); }
        }

        private void OnTPressed()
        {
            try { LevelTransition.Class.@goto(ToHLString("SewerShort")); }
            catch { }
        }

        private void OnPPressed()
        {
            try
            {
                if (!_cdbReady) { Log("P: CDB 未就绪"); return; }
                Log("P → 深层腐化牢房");
                PatchLevelLogo("P");
                Ref<bool> nd = default;
                var trans = new LevelTransition(ToHLString(MainLevelId), null, null, null, nd);
                if (trans != null) trans.loadNewLevel();
            }
            catch (Exception ex) { Log("P fail: " + RealError(ex)); }
        }

        private void OnBPressed()
        {
            try
            {
                if (!_cdbReady) { Log("B: CDB 未就绪"); return; }
                Log("B → Boss 房间");
                PatchLevelLogo("B");
                Ref<bool> nd = default;
                var trans = new LevelTransition(ToHLString("DeathArena"), null, null, null, nd);
                if (trans != null) trans.loadNewLevel();
            }
            catch (Exception ex) { Log("B fail: " + RealError(ex)); }
        }

        // ═══════════════════════════════════════════
        // Hook
        // ═══════════════════════════════════════════

        private LevelStruct Hook__LevelStruct_get(Hook__LevelStruct.orig_get orig, User user, LevelInfo level, Rand rng)
        {
            string id = "";
            try { if (level?.id != null) id = Norm(level.id.ToString()); } catch { }

            // ══ 在 LevelStruct 创建前精准 swap biome atlas ══
            // 原理：所有使用 PrisonCorrupt biome 的关卡共享同一个 biome CDB 对象
            // 在关卡创建前 swap atlasName，让正确的图集被加载
            if (SameId(id, PrisonCorruptId))
            {
                // 腐化牢房 → 原始纹理
                if (_savedAtlasName != null) SetBiomeAtlasDirect(_savedAtlasName);
                return new PrisonCorruptLevelStruct(user, level, rng);
            }
            if (SameId(id, MainLevelId) || SameId(id, "DeathArena"))
            {
                // 深层腐化牢房 / Boss 房间 → jidufuh 纹理
                SetBiomeAtlasDirect("jidufuh");
                if (SameId(id, MainLevelId))
                    return new TestCorruptPlusLevelStruct(user, level, rng);
                else
                    return new MimicKingArenaLevelStruct(user, level, rng);
            }
            if (SameId(id, TransitionLevelId))
                return new TestCorruptPlusTransitionStruct(user, level, rng);

            return orig(user, level, rng);
        }

        // ═══════════════════════════════════════════
        // 工具
        // ═══════════════════════════════════════════

        private bool SafeLevelExists(string id)
        {
            try { return Data.Class.level?.byId != null && Data.Class.level.byId.exists(ToHLString(id)); }
            catch { return false; }
        }

        private static bool SameId(string a, string b) =>
            string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

        private static string Norm(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            while (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
                s = s.Substring(1, s.Length - 2).Trim();
            return s;
        }

        public static void Log(string msg)
        {
            string line = "[TestCorruptPlusLevel] " + msg;
            try { Console.WriteLine(line); } catch { }
            try { if (!string.IsNullOrEmpty(_logPath)) File.AppendAllText(_logPath, line + "\r\n", Encoding.UTF8); } catch { }
        }

        public static dc.String ToHLString(string text)
        {
            if (text == null) return null;
            IntPtr utf8 = IntPtr.Zero;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                utf8 = Marshal.AllocHGlobal(bytes.Length + 1);
                Marshal.Copy(bytes, 0, utf8, bytes.Length);
                Marshal.WriteByte(utf8, bytes.Length, 0);
                return dc.String.Class.fromUTF8.Invoke(utf8);
            }
            finally { if (utf8 != IntPtr.Zero) Marshal.FreeHGlobal(utf8); }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private static bool IsKeyDown(int vKey) => ((int)GetAsyncKeyState(vKey) & 0x8000) != 0;

        private static string RealError(Exception ex)
        {
            if (ex == null) return "";
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
        }
    }
}
