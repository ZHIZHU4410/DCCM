#nullable disable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using dc;
using dc.en;
using dc.h2d;
using dc.h3d;
using dc.libs.heaps.slib;
using dc.shader;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;

namespace EchoVision
{
    // ============================================================
    // EchoVisionMain V3
    // 真轮廓版：
    // 1. 场景层全黑
    // 2. 声波附近复制原 sprite
    // 3. 复制体变成黑色剪影
    // 4. 复制体周围叠白色偏移 sprite 形成“原图轮廓”
    // 5. UI 不盖黑幕，尽量保留显示
    //
    // 测试键：
    // V  = 手动大声波
    // F8 = 开关
    //
    // 注意：
    // HSprite 可复制轮廓；
    // HParticle / HSpriteBatch 批处理粒子无法逐个复制，这版先用白色回声线模拟。
    // ============================================================

    internal static class PseudocodeHelper
    {
        public static T ReadMem<T>(object bytes, int offset)
        {
            try
            {
                if (typeof(T) == typeof(int))
                {
                    int v = ReadInt32(bytes, offset);
                    return (T)(object)v;
                }
            }
            catch
            {
            }

            return default(T);
        }

        private static int ReadInt32(object bytes, int offset)
        {
            if (bytes == null)
                return 0;

            try
            {
                if (bytes is System.IntPtr ptr)
                {
                    if (ptr == System.IntPtr.Zero)
                        return 0;

                    return System.Runtime.InteropServices.Marshal.ReadInt32(ptr, offset);
                }

                if (bytes is byte[] ba)
                {
                    if (offset < 0 || offset + 4 > ba.Length)
                        return 0;

                    return System.BitConverter.ToInt32(ba, offset);
                }

                if (bytes is int[] ia)
                {
                    int index = offset >> 2;

                    if (index < 0 || index >= ia.Length)
                        return 0;

                    return ia[index];
                }

                if (bytes is System.Array arr)
                {
                    int index = offset >> 2;

                    if (index < 0 || index >= arr.Length)
                        return 0;

                    object v = arr.GetValue(index);

                    if (v == null)
                        return 0;

                    return System.Convert.ToInt32(v);
                }

                object ptrLike = GetPointerLikeValue(bytes);

                if (ptrLike is System.IntPtr rawPtr)
                {
                    if (rawPtr == System.IntPtr.Zero)
                        return 0;

                    return System.Runtime.InteropServices.Marshal.ReadInt32(rawPtr, offset);
                }

                if (ptrLike is long l)
                {
                    if (l == 0)
                        return 0;

                    return System.Runtime.InteropServices.Marshal.ReadInt32(new System.IntPtr(l), offset);
                }

                if (ptrLike is int i)
                {
                    if (i == 0)
                        return 0;

                    return System.Runtime.InteropServices.Marshal.ReadInt32(new System.IntPtr(i), offset);
                }
            }
            catch
            {
            }

            return 0;
        }

        private static object GetPointerLikeValue(object obj)
        {
            if (obj == null)
                return null;

            string[] names =
            {
                "ptr", "Ptr",
                "pointer", "Pointer",
                "address", "Address",
                "value", "Value",
                "data", "Data",
                "_ptr", "_pointer"
            };

            try
            {
                System.Type t = obj.GetType();

                for (int i = 0; i < names.Length; i++)
                {
                    string n = names[i];

                    PropertyInfo p = t.GetProperty(
                        n,
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance |
                        BindingFlags.Static
                    );

                    if (p != null && p.GetIndexParameters().Length == 0)
                    {
                        try
                        {
                            object v = p.GetValue(obj, null);

                            if (v != null)
                                return v;
                        }
                        catch
                        {
                        }
                    }

                    FieldInfo f = t.GetField(
                        n,
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance |
                        BindingFlags.Static
                    );

                    if (f != null)
                    {
                        try
                        {
                            object v = f.GetValue(obj);

                            if (v != null)
                                return v;
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }

    public class EchoVisionMain : ModBase, IOnHeroUpdate, IOnGameExit
    {
        private static bool Enabled = true;

        // 场景纯黑程度
        private const double DarknessAlpha = 1.0;

        // 主体黑色剪影透明度倍率
        private const double SilhouetteAlphaMul = 0.96;

        // 白色描边透明度倍率
        private const double OutlineAlphaMul = 0.92;

        // 白色描边偏移距离，越大轮廓越粗
        private const double OutlineOffsetPx = 2.2;

        // 每帧最多 Echo 的实体数量，防止卡顿
        private const int MaxEchoEntitiesPerFrame = 90;

        // 每帧最多额外 Echo 的 HSprite 数量，武器/部分特效/头部/附属物
        private const int MaxExtraSpritesPerFrame = 80;

        // 每帧最多地图边数量
        private const int MaxTerrainEdgesPerFrame = 2800;

        private const int MaxPings = 36;
        private const double TilePx = 24.0;

        private const double FootStepInterval = 0.13;
        private const double AttackPingCooldown = 0.18;
        private const double EnemyScanInterval = 0.12;
        private const double EntityScanInterval = 0.18;

        private const int VK_V = 0x56;
        private const int VK_F8 = 0x77;

        private static readonly int[] ActionKeys =
        {
            0x01, 0x02, 0x20, 0x10, 0x4A, 0x4B, 0x49, 0x55
        };

        private const int Black = 0x000000;
        private const int White = 0xFFFFFF;
        private const int Gray1 = 0xD8D8D8;
        private const int Gray2 = 0x909090;
        private const int Gray3 = 0x505050;

        private static dc.h2d.Object OverlayRoot = null;
        private static dc.h2d.Object EchoSpriteLayer = null;
        private static Graphics BlackGfx = null;
        private static Graphics EchoGfx = null;

        private static object CurrentLevelKey = null;

        private static double LastHeroX = 0.0;
        private static double LastHeroY = 0.0;
        private static bool HasLastHeroPos = false;

        private static double FootStepTimer = 0.0;
        private static double AttackTimer = 0.0;
        private static double EnemyScanTimer = 0.0;
        private static double EntityScanTimer = 0.0;

        private static bool LastVDown = false;
        private static bool LastF8Down = false;

        private static readonly List<Entity> CachedEntities = new List<Entity>();
        private static readonly HashSet<int> CachedEntityKeys = new HashSet<int>();

        // === Loading warmup: defers heavy processing for ~1.8s after level load ===
        private const double WarmupDuration = 1.8;
        private static double _warmupRemaining = WarmupDuration;
        private static int _frameCounter;

        // === Terrain edge pre-build: one-time full map scan at level load ===
        private struct TerrainEdge
        {
            public double X1, Y1, X2, Y2;
        }
        private static readonly List<TerrainEdge> _terrainEdges = new List<TerrainEdge>(8192);
        private static bool _terrainEdgesReady = false;

        // === Reusable collections: avoids per-frame List allocations (GC pressure) ===
        private static readonly List<SpriteCandidate> _sharedExtraSprites = new List<SpriteCandidate>(80);
        private static readonly List<SpriteCandidate> _sharedHeroSprites = new List<SpriteCandidate>(80);
        private static readonly List<int> _sharedRemoveKeys = new List<int>(64);

        private class EchoPing
        {
            public double X;
            public double Y;
            public double Age;
            public double Life;
            public double StartRadius;
            public double MaxRadius;
            public double Intensity;
            public int Kind;
        }

        private class EntityTrack
        {
            public double X;
            public double Y;
            public int Life;
            public double Cooldown;
        }

        private class SpriteEcho
        {
            public int Key;
            public HSprite Source;
            public Entity Owner;
            public HSprite Main;
            public HSprite[] Outline;
            public string GroupName;
            public int Frame;
            public double LastVisible;
            public bool UsedThisFrame;
            public bool IsChildSprite;
        }

        private class SpriteCandidate
        {
            public HSprite Sprite;
            public Entity Owner;
            public bool IsChildSprite;
        }

        private static readonly List<EchoPing> Pings = new List<EchoPing>();
        private static readonly Dictionary<int, EntityTrack> Tracks = new Dictionary<int, EntityTrack>();
        private static readonly Dictionary<int, SpriteEcho> SpriteEchoes = new Dictionary<int, SpriteEcho>();

        public EchoVisionMain(ModInfo info) : base(info)
        {
        }

        public override void Initialize()
        {
            base.Initialize();
            System.Console.WriteLine("[EchoVision] V3 真轮廓版已加载：黑色剪影 + 白色描边 + 交互物/武器/部分特效。V=测试声波，F8=开关。");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            try
            {
                if (dt <= 0.0 || double.IsNaN(dt) || double.IsInfinity(dt))
                    dt = 1.0 / 60.0;

                _frameCounter++;

                Hero hero = GetHero();

                if (hero == null || hero.destroyed || hero._level == null)
                {
                    ClearOverlay();
                    ResetRuntimeState();
                    return;
                }

                HandleToggleKeys(hero);

                if (!Enabled)
                {
                    ClearOverlay();
                    UpdateTimersOnly(dt);
                    return;
                }

                // Detect level transition → reset warmup timer
                object levelBefore = CurrentLevelKey;
                EnsureOverlay(hero);
                if (!object.ReferenceEquals(levelBefore, CurrentLevelKey))
                    _warmupRemaining = WarmupDuration;

                bool warmingUp = _warmupRemaining > 0.0;
                if (warmingUp)
                    _warmupRemaining -= dt;

                // Always run: sound detection, ping updates (lightweight, critical feedback)
                DetectHeroSound(hero, dt);
                DetectEnemySound(hero, dt);
                UpdatePings(dt);
                DrawBlackWorld(hero);

                if (warmingUp)
                {
                    // Warmup: skip deep reflection, only scan opponents iterator + hero
                    UpdateEntityCacheLightweight(hero, dt);
                    // [声波已注释] DrawSoftEchoRings();
                    DrawEchoSpritesHeroOnly(hero, dt);
                }
                else
                {
                    UpdateEntityCache(hero, dt);
                    DrawEchoGraphics(hero);
                    DrawEchoSprites(hero, dt);
                }

                // Cleanup every other frame is sufficient
                if ((_frameCounter & 1) == 0)
                    CleanupUnusedSpriteEchoes(dt);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[EchoVision] OnHeroUpdate 失败：" + RealError(ex));
            }
        }

        // ============================================================
        // 输入 / 声音
        // ============================================================

        private static void HandleToggleKeys(Hero hero)
        {
            bool vDown = IsKeyDown(VK_V);
            bool f8Down = IsKeyDown(VK_F8);

            // [声波已注释] V 键手动测试声波
            // if (vDown && !LastVDown)
            // {
            //     double hx = GetEntityX(hero);
            //     double hy = GetEntityCenterY(hero);
            //
            //     EmitPing(hx, hy, 12.0, 700.0, 1.2, 1.25, 9);
            //     System.Console.WriteLine("[EchoVision] 手动测试声波");
            // }

            if (f8Down && !LastF8Down)
            {
                Enabled = !Enabled;

                if (!Enabled)
                    ClearOverlay();

                System.Console.WriteLine("[EchoVision] Enabled = " + Enabled);
            }

            LastVDown = vDown;
            LastF8Down = f8Down;
        }

        private static void DetectHeroSound(Hero hero, double dt)
        {
            double hx = GetEntityX(hero);
            double hy = GetEntityCenterY(hero);

            if (!HasLastHeroPos)
            {
                LastHeroX = hx;
                LastHeroY = hy;
                HasLastHeroPos = true;
                // [声波已注释] 首次进入关卡初始声波
                // EmitPing(hx, hy, 8.0, 360.0, 0.82, 0.72, 0);
                return;
            }

            double dx = hx - LastHeroX;
            double dy = hy - LastHeroY;
            double moved = System.Math.Sqrt(dx * dx + dy * dy);

            FootStepTimer -= dt;
            AttackTimer -= dt;

            if (moved > 1.25 && FootStepTimer <= 0.0)
            {
                // [声波已注释] 脚步声波
                // double strength = Clamp(0.45 + moved / 22.0, 0.45, 0.9);
                // EmitPing(hx, hy + 9.0, 6.0, 285.0 + moved * 4.0, strength, 0.54, 1);
                FootStepTimer = FootStepInterval;
            }

            if (AttackTimer <= 0.0 && IsAnyKeyDown(ActionKeys))
            {
                // [声波已注释] 攻击声波
                // EmitPing(hx, hy, 12.0, 510.0, 1.04, 0.86, 2);
                AttackTimer = AttackPingCooldown;
            }

            LastHeroX = hx;
            LastHeroY = hy;
        }

        private static void DetectEnemySound(Hero hero, double dt)
        {
            EnemyScanTimer -= dt;

            if (EnemyScanTimer > 0.0)
                return;

            EnemyScanTimer = EnemyScanInterval;

            if (hero == null || hero._team == null)
                return;

            try
            {
                var iterator = hero._team.opponentsIterator.reset(hero._team);

                while (iterator != null && iterator.hasNext())
                {
                    Entity e = iterator.next();

                    if (!IsValidEntityBasic(e))
                        continue;

                    int key = RuntimeHelpers.GetHashCode(e);
                    double x = GetEntityX(e);
                    double y = GetEntityCenterY(e);
                    int life = e.life;

                    EntityTrack tr;

                    if (!Tracks.TryGetValue(key, out tr))
                    {
                        tr = new EntityTrack();
                        tr.X = x;
                        tr.Y = y;
                        tr.Life = life;
                        tr.Cooldown = 0.0;
                        Tracks[key] = tr;
                        continue;
                    }

                    tr.Cooldown -= EnemyScanInterval;

                    double dx = x - tr.X;
                    double dy = y - tr.Y;
                    double moved = System.Math.Sqrt(dx * dx + dy * dy);

                    if (life != tr.Life && tr.Cooldown <= 0.0)
                    {
                        // [声波已注释] 敌人受伤声波
                        // EmitPing(x, y, 10.0, 445.0, 1.0, 0.78, 4);
                        tr.Cooldown = 0.22;
                    }
                    else if (moved > 8.0 && tr.Cooldown <= 0.0)
                    {
                        // [声波已注释] 敌人移动声波
                        // EmitPing(x, y, 6.0, 285.0, 0.5, 0.48, 3);
                        tr.Cooldown = 0.30;
                    }

                    tr.X = x;
                    tr.Y = y;
                    tr.Life = life;
                }
            }
            catch
            {
            }
        }

        private static void EmitPing(double x, double y, double startRadius, double maxRadius, double intensity, double life, int kind)
        {
            EchoPing p = new EchoPing();
            p.X = x;
            p.Y = y;
            p.Age = 0.0;
            p.Life = life;
            p.StartRadius = startRadius;
            p.MaxRadius = maxRadius;
            p.Intensity = intensity;
            p.Kind = kind;

            Pings.Add(p);

            while (Pings.Count > MaxPings)
                Pings.RemoveAt(0);
        }

        private static void UpdatePings(double dt)
        {
            for (int i = Pings.Count - 1; i >= 0; i--)
            {
                EchoPing p = Pings[i];

                if (p == null)
                {
                    Pings.RemoveAt(i);
                    continue;
                }

                p.Age += dt;

                if (p.Age >= p.Life)
                    Pings.RemoveAt(i);
            }
        }

        private static void UpdateTimersOnly(double dt)
        {
            if (FootStepTimer > 0.0)
                FootStepTimer -= dt;

            if (AttackTimer > 0.0)
                AttackTimer -= dt;

            if (EnemyScanTimer > 0.0)
                EnemyScanTimer -= dt;

            if (EntityScanTimer > 0.0)
                EntityScanTimer -= dt;
        }

        // ============================================================
        // Overlay
        // ============================================================

        private static void EnsureOverlay(Hero hero)
        {
            try
            {
                object levelKey = hero._level;

                if (OverlayRoot != null && object.ReferenceEquals(CurrentLevelKey, levelKey))
                    return;

                ClearOverlay();

                if (hero._level == null || hero._level.scroller == null)
                    return;

                OverlayRoot = new dc.h2d.Object(hero._level.scroller);

                int layer = 999999;

                try
                {
                    int foreground = Const.Class.DP_FOREGROUND;
                    layer = foreground + 999;
                }
                catch
                {
                }

                try
                {
                    hero._level.scroller.addChildAt(OverlayRoot, layer);
                }
                catch
                {
                }

                OverlayRoot.x = 0.0;
                OverlayRoot.y = 0.0;
                OverlayRoot.alpha = 1.0;
                OverlayRoot.visible = true;

                BlackGfx = new Graphics(OverlayRoot);
                EchoGfx = new Graphics(OverlayRoot);
                EchoSpriteLayer = new dc.h2d.Object(OverlayRoot);

                CurrentLevelKey = levelKey;

                // Pre-build terrain edge list once after overlay is ready
                BuildTerrainEdges(hero);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[EchoVision] 创建 Overlay 失败：" + RealError(ex));
            }
        }

        private static void DrawBlackWorld(Hero hero)
        {
            if (OverlayRoot == null)
                return;

            if (BlackGfx == null)
                BlackGfx = new Graphics(OverlayRoot);

            // Direct clear instead of SafeClearGraphics to avoid recreate path
            try { BlackGfx.clear(); } catch { BlackGfx = new Graphics(OverlayRoot); }

            if (BlackGfx == null)
                return;

            double cx;
            double cy;
            double vw;
            double vh;

            GetViewportWorldRect(hero, out cx, out cy, out vw, out vh);

            double marginX = vw * 0.75 + 300.0;
            double marginY = vh * 0.75 + 220.0;

            double x = cx - vw * 0.5 - marginX;
            double y = cy - vh * 0.5 - marginY;
            double w = vw + marginX * 2.0;
            double h = vh + marginY * 2.0;

            DrawFilledRect(BlackGfx, x, y, w, h, Black, DarknessAlpha);
        }

        private static void DrawEchoGraphics(Hero hero)
        {
            if (OverlayRoot == null)
                return;

            if (EchoGfx == null)
                EchoGfx = new Graphics(OverlayRoot);

            // Direct clear instead of SafeClearGraphics to avoid recreate path
            try { EchoGfx.clear(); } catch { EchoGfx = new Graphics(OverlayRoot); }

            if (EchoGfx == null)
                return;

            // [声波已注释] DrawSoftEchoRings();
            DrawTerrainOutlines(hero);
            // [声波已注释] DrawFxApproximation(hero);
        }

        // ============================================================
        // 实体扫描：敌人、子弹、门、箱子、机关、掉落物、可交互物
        // ============================================================

        private static void UpdateEntityCache(Hero hero, double dt)
        {
            EntityScanTimer -= dt;

            if (EntityScanTimer > 0.0 && CachedEntities.Count > 0)
                return;

            EntityScanTimer = EntityScanInterval;

            CachedEntities.Clear();
            CachedEntityKeys.Clear();

            AddEntityCached(hero);

            try
            {
                if (hero._team != null)
                {
                    var iterator = hero._team.opponentsIterator.reset(hero._team);

                    while (iterator != null && iterator.hasNext())
                    {
                        Entity e = iterator.next();
                        AddEntityCached(e);
                    }
                }
            }
            catch
            {
            }

            // Skip deep reflection if opponents iterator already found enough entities
            if (CachedEntities.Count >= MaxEchoEntitiesPerFrame)
                return;

            // 从 Level / Map / Hero 相关对象里反射找 Entity。
            // 这一步就是为了拿到 Door、Chest、Loot、Bullet、Trap、NPC、交互物等。
            HashSet<int> visitedObjects = new HashSet<int>();

            ScanObjectForEntities(hero._level, 0, 4, visitedObjects);
            ScanObjectForEntities(hero.inventory, 0, 2, visitedObjects);
            ScanObjectForEntities(hero.weaponsManager, 0, 3, visitedObjects);
            ScanObjectForEntities(hero.activeSkillsManager, 0, 3, visitedObjects);
            ScanObjectForEntities(hero.mainSkillsManager, 0, 3, visitedObjects);
            ScanObjectForEntities(hero, 0, 2, visitedObjects);
        }

        /// <summary>
        /// Lightweight entity cache during warmup: only scans opponents iterator + hero.
        /// Skips deep reflection entirely to avoid hitting partially-initialized objects.
        /// </summary>
        private static void UpdateEntityCacheLightweight(Hero hero, double dt)
        {
            EntityScanTimer -= dt;

            if (EntityScanTimer > 0.0 && CachedEntities.Count > 0)
                return;

            EntityScanTimer = EntityScanInterval;

            CachedEntities.Clear();
            CachedEntityKeys.Clear();

            AddEntityCached(hero);

            try
            {
                if (hero._team != null)
                {
                    var iterator = hero._team.opponentsIterator.reset(hero._team);

                    while (iterator != null && iterator.hasNext())
                    {
                        Entity e = iterator.next();
                        AddEntityCached(e);
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddEntityCached(Entity e)
        {
            if (!IsValidEntityBasic(e))
                return;

            int key = RuntimeHelpers.GetHashCode(e);

            if (CachedEntityKeys.Contains(key))
                return;

            CachedEntityKeys.Add(key);
            CachedEntities.Add(e);
        }

        private static void ScanObjectForEntities(object obj, int depth, int maxDepth, HashSet<int> visited)
        {
            if (obj == null || depth > maxDepth || CachedEntities.Count > 520)
                return;

            System.Type t;

            try
            {
                t = obj.GetType();
            }
            catch
            {
                return;
            }

            if (IsPrimitiveLike(t))
                return;

            int objKey = RuntimeHelpers.GetHashCode(obj);

            if (visited.Contains(objKey))
                return;

            visited.Add(objKey);

            Entity ent = obj as Entity;

            if (ent != null)
            {
                AddEntityCached(ent);
                return;
            }

            // Array / Haxe ArrayObj
            if (TryScanArrayLikeForEntities(obj, depth, maxDepth, visited))
                return;

            string typeName = "";

            try
            {
                typeName = t.FullName ?? "";
            }
            catch
            {
            }

            bool broadScan =
                depth <= 1 ||
                typeName.Contains("Level") ||
                typeName.Contains("Map") ||
                typeName.Contains("Manager") ||
                typeName.Contains("Inventory") ||
                typeName.Contains("Team") ||
                typeName.Contains("Entity");

            FieldInfo[] fields = null;

            try
            {
                fields = t.GetFields(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance
                );
            }
            catch
            {
            }

            if (fields != null)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo f = fields[i];
                    string n = f.Name ?? "";

                    if (!broadScan && !LikelyEntityContainerName(n))
                        continue;

                    object v = null;

                    try
                    {
                        v = f.GetValue(obj);
                    }
                    catch
                    {
                    }

                    if (v != null)
                        ScanObjectForEntities(v, depth + 1, maxDepth, visited);
                }
            }

            PropertyInfo[] props = null;

            try
            {
                props = t.GetProperties(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance
                );
            }
            catch
            {
            }

            if (props != null)
            {
                for (int i = 0; i < props.Length; i++)
                {
                    PropertyInfo p = props[i];

                    if (p.GetIndexParameters().Length > 0)
                        continue;

                    string n = p.Name ?? "";

                    if (!broadScan && !LikelyEntityContainerName(n))
                        continue;

                    object v = null;

                    try
                    {
                        v = p.GetValue(obj, null);
                    }
                    catch
                    {
                    }

                    if (v != null)
                        ScanObjectForEntities(v, depth + 1, maxDepth, visited);
                }
            }
        }

        private static bool TryScanArrayLikeForEntities(object obj, int depth, int maxDepth, HashSet<int> visited)
        {
            if (obj == null)
                return false;

            try
            {
                System.Array arr = obj as System.Array;

                if (arr != null)
                {
                    int len = arr.Length;
                    int max = len > 900 ? 900 : len;

                    for (int i = 0; i < max; i++)
                    {
                        object v = null;

                        try
                        {
                            v = arr.GetValue(i);
                        }
                        catch
                        {
                        }

                        if (v != null)
                            ScanObjectForEntities(v, depth + 1, maxDepth, visited);
                    }

                    return true;
                }

                double lenDouble;
                object innerArray;

                if (TryGetNumberMember(obj, "length", out lenDouble) && TryGetMemberValue(obj, "array", out innerArray))
                {
                    int len = (int)lenDouble;
                    int max = len > 900 ? 900 : len;

                    System.Array inner = innerArray as System.Array;

                    if (inner != null)
                    {
                        int realMax = inner.Length < max ? inner.Length : max;

                        for (int i = 0; i < realMax; i++)
                        {
                            object v = null;

                            try
                            {
                                v = inner.GetValue(i);
                            }
                            catch
                            {
                            }

                            if (v != null)
                                ScanObjectForEntities(v, depth + 1, maxDepth, visited);
                        }

                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        // ============================================================
        // Echo Sprite 绘制
        // ============================================================

        /// <summary>
        /// Fast viewport bounds check. Returns 1.0 if point is within the viewport + margin,
        /// 0.0 otherwise. Replaces the expensive O(N) GetVisibilityAt ping iteration.
        /// Modeled after Entity._isOnScreen() from GamePseudocode/dc/Entity.cs.
        /// </summary>
        private static double GetViewportVisibility(double wx, double wy, double vcx, double vcy, double vw, double vh)
        {
            double margin = 160.0; // generous margin matching terrain loop
            double halfW = vw * 0.5 + margin;
            double halfH = vh * 0.5 + margin;

            double dx = wx - vcx;
            double dy = wy - vcy;

            return (dx >= -halfW && dx <= halfW && dy >= -halfH && dy <= halfH) ? 1.0 : 0.0;
        }

        private static void DrawEchoSprites(Hero hero, double dt)
        {
            if (EchoSpriteLayer == null)
                return;

            // Precompute viewport bounds once per frame for all entities
            double vcx, vcy, vw, vh;
            GetViewportWorldRect(hero, out vcx, out vcy, out vw, out vh);

            for (int i = 0; i < CachedEntities.Count; i++)
            {
                if (i >= MaxEchoEntitiesPerFrame)
                    break;

                Entity e = CachedEntities[i];

                if (!IsValidEntityBasic(e))
                    continue;

                double vis = GetViewportVisibility(GetEntityX(e), GetEntityCenterY(e), vcx, vcy, vw, vh);

                if (e == hero && vis < 0.22)
                    vis = 0.22;

                if (vis <= 0.025)
                    continue;

                HSprite spr = GetEntitySprite(e);

                if (spr != null)
                    UseSpriteEcho(spr, e, false, vis);

                // 额外拿武器、头部、部分特效、附属物 HSprite（复用共享列表）
                _sharedExtraSprites.Clear();
                CollectExtraSpritesInto(e, MaxExtraSpritesPerFrame, _sharedExtraSprites);

                for (int j = 0; j < _sharedExtraSprites.Count; j++)
                {
                    SpriteCandidate c = _sharedExtraSprites[j];

                    if (c == null || c.Sprite == null)
                        continue;

                    UseSpriteEcho(c.Sprite, c.Owner ?? e, c.IsChildSprite, vis * 0.96);
                }
            }

            // Hero 武器管理器、主动技能、主技能里经常藏着武器 HSprite / 特效 HSprite（复用共享列表）
            _sharedHeroSprites.Clear();
            CollectSpritesFromObject(hero.weaponsManager, hero, 0, 3, _sharedHeroSprites, MaxExtraSpritesPerFrame);
            CollectSpritesFromObject(hero.activeSkillsManager, hero, 0, 3, _sharedHeroSprites, MaxExtraSpritesPerFrame);
            CollectSpritesFromObject(hero.mainSkillsManager, hero, 0, 3, _sharedHeroSprites, MaxExtraSpritesPerFrame);
            CollectSpritesFromObject(hero.heroHead, hero, 0, 3, _sharedHeroSprites, MaxExtraSpritesPerFrame);

            double heroVis = GetViewportVisibility(GetEntityX(hero), GetEntityCenterY(hero), vcx, vcy, vw, vh);
            if (heroVis < 0.22)
                heroVis = 0.22;

            for (int i = 0; i < _sharedHeroSprites.Count; i++)
            {
                SpriteCandidate c = _sharedHeroSprites[i];

                if (c == null || c.Sprite == null)
                    continue;

                UseSpriteEcho(c.Sprite, c.Owner ?? hero, c.IsChildSprite, heroVis * 0.95);
            }
        }

        /// <summary>
        /// Warmup-only echo rendering: processes only the hero entity.
        /// Avoids the cascade of HSprite creation for dozens of entities all at once.
        /// </summary>
        private static void DrawEchoSpritesHeroOnly(Hero hero, double dt)
        {
            if (EchoSpriteLayer == null || hero == null)
                return;

            if (!IsValidEntityBasic(hero))
                return;

            double vcx, vcy, vw, vh;
            GetViewportWorldRect(hero, out vcx, out vcy, out vw, out vh);
            double vis = GetViewportVisibility(GetEntityX(hero), GetEntityCenterY(hero), vcx, vcy, vw, vh);

            if (vis < 0.22)
                vis = 0.22;

            HSprite spr = GetEntitySprite(hero);

            if (spr != null)
                UseSpriteEcho(spr, hero, false, vis);

            // Hero weapons/head sprites: reuse shared list
            _sharedHeroSprites.Clear();
            CollectSpritesFromObject(hero.weaponsManager, hero, 0, 3, _sharedHeroSprites, MaxExtraSpritesPerFrame);
            CollectSpritesFromObject(hero.activeSkillsManager, hero, 0, 3, _sharedHeroSprites, MaxExtraSpritesPerFrame);
            CollectSpritesFromObject(hero.mainSkillsManager, hero, 0, 3, _sharedHeroSprites, MaxExtraSpritesPerFrame);
            CollectSpritesFromObject(hero.heroHead, hero, 0, 3, _sharedHeroSprites, MaxExtraSpritesPerFrame);

            for (int i = 0; i < _sharedHeroSprites.Count; i++)
            {
                SpriteCandidate c = _sharedHeroSprites[i];

                if (c == null || c.Sprite == null)
                    continue;

                UseSpriteEcho(c.Sprite, c.Owner ?? hero, c.IsChildSprite, vis * 0.95);
            }
        }

        private static void UseSpriteEcho(HSprite src, Entity owner, bool isChildSprite, double visibility)
        {
            if (src == null || owner == null)
                return;

            try
            {
                if (src.destroyed)
                    return;
            }
            catch
            {
            }

            int key = RuntimeHelpers.GetHashCode(src);

            SpriteEcho echo;

            if (!SpriteEchoes.TryGetValue(key, out echo) || echo == null || echo.Main == null)
            {
                echo = CreateSpriteEcho(src, owner, isChildSprite, key);

                if (echo == null)
                    return;

                SpriteEchoes[key] = echo;
            }

            echo.Source = src;
            echo.Owner = owner;
            echo.IsChildSprite = isChildSprite;
            echo.UsedThisFrame = true;
            echo.LastVisible = 0.18;

            UpdateSpriteEcho(echo, visibility);
        }

        private static SpriteEcho CreateSpriteEcho(HSprite src, Entity owner, bool isChildSprite, int key)
        {
            try
            {
                if (EchoSpriteLayer == null || src == null || src.lib == null || src.groupName == null)
                    return null;

                SpriteEcho e = new SpriteEcho();
                e.Key = key;
                e.Source = src;
                e.Owner = owner;
                e.IsChildSprite = isChildSprite;
                e.GroupName = SafeToString(src.groupName);
                e.Frame = src.frame;

                e.Outline = new HSprite[8];

                // 8层白色偏移轮廓
                for (int i = 0; i < e.Outline.Length; i++)
                {
                    int frame = src.frame;
                    HSprite o = NewHSpriteFromSource(src, frame);
                    EchoSpriteLayer.addChild(o);
                    CopyPivot(src, o);
                    MakeSpriteSolid(o, White, 1.0);
                    e.Outline[i] = o;
                }

                int mainFrame = src.frame;
                e.Main = NewHSpriteFromSource(src, mainFrame);
                EchoSpriteLayer.addChild(e.Main);
                CopyPivot(src, e.Main);
                MakeSpriteSolid(e.Main, Black, 1.0);

                return e;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[EchoVision] CreateSpriteEcho 失败：" + RealError(ex));
                return null;
            }
        }

        private static void UpdateSpriteEcho(SpriteEcho echo, double visibility)
        {
            if (echo == null || echo.Source == null || echo.Main == null)
                return;

            HSprite src = echo.Source;

            try
            {
                string group = SafeToString(src.groupName);
                int frame = src.frame;

                if (group != echo.GroupName || frame != echo.Frame)
                {
                    RecreateEchoSprites(echo);

                    if (echo.Main == null)
                        return;
                }

                double baseX;
                double baseY;

                GetSpriteWorldPos(src, echo.Owner, echo.IsChildSprite, out baseX, out baseY);

                double alpha = Clamp(visibility, 0.0, 1.0);

                double scaleX = src.scaleX;
                double scaleY = src.scaleY;

                // 如果是本地子 Sprite，缩放会叠加 owner 主 Sprite
                if (echo.IsChildSprite && echo.Owner != null && echo.Owner.spr != null)
                {
                    try
                    {
                        scaleX *= echo.Owner.spr.scaleX;
                        scaleY *= echo.Owner.spr.scaleY;
                    }
                    catch
                    {
                    }
                }

                double rot = src.rotation;

                if (echo.IsChildSprite && echo.Owner != null && echo.Owner.spr != null)
                {
                    try
                    {
                        rot += echo.Owner.spr.rotation;
                    }
                    catch
                    {
                    }
                }

                // 轮廓层偏移
                double off = OutlineOffsetPx;

                SetSpriteTransform(echo.Outline[0], baseX - off, baseY, scaleX, scaleY, rot, alpha * OutlineAlphaMul);
                SetSpriteTransform(echo.Outline[1], baseX + off, baseY, scaleX, scaleY, rot, alpha * OutlineAlphaMul);
                SetSpriteTransform(echo.Outline[2], baseX, baseY - off, scaleX, scaleY, rot, alpha * OutlineAlphaMul);
                SetSpriteTransform(echo.Outline[3], baseX, baseY + off, scaleX, scaleY, rot, alpha * OutlineAlphaMul);

                double d = off * 0.72;
                SetSpriteTransform(echo.Outline[4], baseX - d, baseY - d, scaleX, scaleY, rot, alpha * OutlineAlphaMul * 0.82);
                SetSpriteTransform(echo.Outline[5], baseX + d, baseY - d, scaleX, scaleY, rot, alpha * OutlineAlphaMul * 0.82);
                SetSpriteTransform(echo.Outline[6], baseX - d, baseY + d, scaleX, scaleY, rot, alpha * OutlineAlphaMul * 0.82);
                SetSpriteTransform(echo.Outline[7], baseX + d, baseY + d, scaleX, scaleY, rot, alpha * OutlineAlphaMul * 0.82);

                // 主体黑色剪影
                SetSpriteTransform(echo.Main, baseX, baseY, scaleX, scaleY, rot, alpha * SilhouetteAlphaMul);
            }
            catch
            {
                HideSpriteEcho(echo);
            }
        }

        private static void RecreateEchoSprites(SpriteEcho echo)
        {
            if (echo == null || echo.Source == null)
                return;

            DestroySpriteEchoSprites(echo);

            try
            {
                HSprite src = echo.Source;

                echo.GroupName = SafeToString(src.groupName);
                echo.Frame = src.frame;
                echo.Outline = new HSprite[8];

                for (int i = 0; i < echo.Outline.Length; i++)
                {
                    int frame = src.frame;
                    HSprite o = NewHSpriteFromSource(src, frame);
                    EchoSpriteLayer.addChild(o);
                    CopyPivot(src, o);
                    MakeSpriteSolid(o, White, 1.0);
                    echo.Outline[i] = o;
                }

                int mainFrame = src.frame;
                echo.Main = NewHSpriteFromSource(src, mainFrame);
                EchoSpriteLayer.addChild(echo.Main);
                CopyPivot(src, echo.Main);
                MakeSpriteSolid(echo.Main, Black, 1.0);
            }
            catch
            {
                echo.Main = null;
                echo.Outline = null;
            }
        }

        private static void SetSpriteTransform(HSprite spr, double x, double y, double sx, double sy, double rot, double alpha)
        {
            if (spr == null)
                return;

            alpha = Clamp(alpha, 0.0, 1.0);

            try
            {
                spr.posChanged = true;
                spr.x = x;
                spr.y = y;
                spr.scaleX = sx;
                spr.scaleY = sy;
                spr.rotation = rot;
                spr.alpha = alpha;

                SetSpriteAlphaColor(spr, alpha);

                bool visible = alpha > 0.015;
                spr.set_visible(visible);
            }
            catch
            {
            }
        }

        private static void GetSpriteWorldPos(HSprite src, Entity owner, bool isChild, out double x, out double y)
        {
            x = 0.0;
            y = 0.0;

            try
            {
                if (!isChild)
                {
                    x = src.x;
                    y = src.y;
                    return;
                }

                if (owner != null && owner.spr != null)
                {
                    x = owner.spr.x + src.x;
                    y = owner.spr.y + src.y;
                    return;
                }

                x = src.x;
                y = src.y;
            }
            catch
            {
                x = GetEntityX(owner);
                y = GetEntityCenterY(owner);
            }
        }

        private static void CleanupUnusedSpriteEchoes(double dt)
        {
            if (SpriteEchoes.Count <= 0)
                return;

            _sharedRemoveKeys.Clear();

            foreach (KeyValuePair<int, SpriteEcho> kv in SpriteEchoes)
            {
                SpriteEcho e = kv.Value;

                if (e == null)
                {
                    _sharedRemoveKeys.Add(kv.Key);
                    continue;
                }

                if (!e.UsedThisFrame)
                {
                    e.LastVisible -= dt;

                    if (e.LastVisible <= 0.0)
                    {
                        _sharedRemoveKeys.Add(kv.Key);
                    }
                    else
                    {
                        HideSpriteEcho(e);
                    }
                }

                e.UsedThisFrame = false;
            }

            if (_sharedRemoveKeys.Count > 0)
            {
                for (int i = 0; i < _sharedRemoveKeys.Count; i++)
                {
                    int key = _sharedRemoveKeys[i];

                    SpriteEcho e;

                    if (SpriteEchoes.TryGetValue(key, out e))
                        DestroySpriteEcho(e);

                    SpriteEchoes.Remove(key);
                }
            }
        }

        private static void HideSpriteEcho(SpriteEcho echo)
        {
            if (echo == null)
                return;

            try
            {
                if (echo.Main != null)
                    echo.Main.set_visible(false);
            }
            catch
            {
            }

            if (echo.Outline != null)
            {
                for (int i = 0; i < echo.Outline.Length; i++)
                {
                    try
                    {
                        if (echo.Outline[i] != null)
                            echo.Outline[i].set_visible(false);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void DestroySpriteEcho(SpriteEcho echo)
        {
            if (echo == null)
                return;

            DestroySpriteEchoSprites(echo);
        }

        private static void DestroySpriteEchoSprites(SpriteEcho echo)
        {
            if (echo == null)
                return;

            try
            {
                if (echo.Main != null)
                    echo.Main.remove();
            }
            catch
            {
            }

            if (echo.Outline != null)
            {
                for (int i = 0; i < echo.Outline.Length; i++)
                {
                    try
                    {
                        if (echo.Outline[i] != null)
                            echo.Outline[i].remove();
                    }
                    catch
                    {
                    }
                }
            }

            echo.Main = null;
            echo.Outline = null;
        }

        private static void MakeSpriteSolid(HSprite spr, int color, double alpha)
        {
            if (spr == null)
                return;

            try
            {
                GradientHiLo shader = new GradientHiLo(color, color, null);
                spr.addShader(shader);
            }
            catch
            {
            }

            try
            {
                Vector c = spr.color;
                c.x = ((color >> 16) & 255) / 255.0;
                c.y = ((color >> 8) & 255) / 255.0;
                c.z = (color & 255) / 255.0;
                c.w = alpha;
            }
            catch
            {
            }

            try
            {
                spr.alpha = alpha;
            }
            catch
            {
            }
        }

        private static void SetSpriteAlphaColor(HSprite spr, double alpha)
        {
            if (spr == null)
                return;

            try
            {
                Vector c = spr.color;
                c.w = alpha;
            }
            catch
            {
            }
        }


        private static HSprite NewHSpriteFromSource(HSprite src, int frame)
        {
            if (src == null)
                return null;

            Ref<int> f = default;
            HSprite hs = new HSprite(src.lib, src.groupName, f, null);

            try
            {
                hs.setFrame(frame);
            }
            catch
            {
            }

            return hs;
        }

        private static void CopyPivot(HSprite src, HSprite dst)
        {
            if (src == null || dst == null)
                return;

            try
            {
                dst.pivot.centerFactorX = src.pivot.centerFactorX;
                dst.pivot.centerFactorY = src.pivot.centerFactorY;
                dst.pivot.usingFactor = true;
                dst.pivot.isUndefined = false;
            }
            catch
            {
                try
                {
                    dst.pivot.centerFactorX = 0.5;
                    dst.pivot.centerFactorY = 0.5;
                    dst.pivot.usingFactor = true;
                    dst.pivot.isUndefined = false;
                }
                catch
                {
                }
            }
        }

        private static void CollectExtraSpritesInto(Entity owner, int maxCount, List<SpriteCandidate> result)
        {
            if (owner == null || maxCount <= 0 || result == null)
                return;

            CollectSpritesFromObject(owner, owner, 0, 2, result, maxCount);
        }

        private static void CollectSpritesFromObject(object obj, Entity owner, int depth, int maxDepth, List<SpriteCandidate> result, int maxCount)
        {
            if (obj == null || result == null || result.Count >= maxCount || depth > maxDepth)
                return;

            System.Type t;

            try
            {
                t = obj.GetType();
            }
            catch
            {
                return;
            }

            if (IsPrimitiveLike(t))
                return;

            HSprite direct = obj as HSprite;

            if (direct != null)
            {
                HSprite ownerSpr = owner != null ? owner.spr : null;

                if (direct != ownerSpr)
                {
                    SpriteCandidate c = new SpriteCandidate();
                    c.Sprite = direct;
                    c.Owner = owner;
                    c.IsChildSprite = ownerSpr != null;

                    result.Add(c);
                }

                return;
            }

            if (obj is System.Array arr)
            {
                int max = arr.Length > 80 ? 80 : arr.Length;

                for (int i = 0; i < max; i++)
                {
                    object v = null;

                    try
                    {
                        v = arr.GetValue(i);
                    }
                    catch
                    {
                    }

                    if (v != null)
                        CollectSpritesFromObject(v, owner, depth + 1, maxDepth, result, maxCount);

                    if (result.Count >= maxCount)
                        return;
                }

                return;
            }

            double lenDouble;
            object innerArray;

            if (TryGetNumberMember(obj, "length", out lenDouble) && TryGetMemberValue(obj, "array", out innerArray))
            {
                System.Array inner = innerArray as System.Array;

                if (inner != null)
                {
                    int len = (int)lenDouble;
                    int max = len > inner.Length ? inner.Length : len;

                    if (max > 80)
                        max = 80;

                    for (int i = 0; i < max; i++)
                    {
                        object v = null;

                        try
                        {
                            v = inner.GetValue(i);
                        }
                        catch
                        {
                        }

                        if (v != null)
                            CollectSpritesFromObject(v, owner, depth + 1, maxDepth, result, maxCount);

                        if (result.Count >= maxCount)
                            return;
                    }

                    return;
                }
            }

            FieldInfo[] fields = null;

            try
            {
                fields = t.GetFields(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance
                );
            }
            catch
            {
            }

            if (fields != null)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo f = fields[i];
                    string n = f.Name ?? "";

                    if (!LikelySpriteFieldName(n) && depth > 0)
                        continue;

                    object v = null;

                    try
                    {
                        v = f.GetValue(obj);
                    }
                    catch
                    {
                    }

                    if (v != null)
                        CollectSpritesFromObject(v, owner, depth + 1, maxDepth, result, maxCount);

                    if (result.Count >= maxCount)
                        return;
                }
            }

            PropertyInfo[] props = null;

            try
            {
                props = t.GetProperties(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance
                );
            }
            catch
            {
            }

            if (props != null)
            {
                for (int i = 0; i < props.Length; i++)
                {
                    PropertyInfo p = props[i];

                    if (p.GetIndexParameters().Length > 0)
                        continue;

                    string n = p.Name ?? "";

                    if (!LikelySpriteFieldName(n) && depth > 0)
                        continue;

                    object v = null;

                    try
                    {
                        v = p.GetValue(obj, null);
                    }
                    catch
                    {
                    }

                    if (v != null)
                        CollectSpritesFromObject(v, owner, depth + 1, maxDepth, result, maxCount);

                    if (result.Count >= maxCount)
                        return;
                }
            }
        }

        // ============================================================
        // 地图轮廓 / 批处理特效近似
        // ============================================================

        /// <summary>
        /// One-time full-map scan to pre-build all terrain edge segments.
        /// Called once per level load. Mimics BiomeDisp.renderBackWalls
        /// which also iterates all tiles at level load time.
        /// </summary>
        private static void BuildTerrainEdges(Hero hero)
        {
            _terrainEdges.Clear();
            _terrainEdgesReady = false;

            try
            {
                var map = hero._level.map;
                if (map == null || map.collisions == null)
                    return;

                int wid = map.wid;
                int hei = map.hei;

                for (int ty = 0; ty < hei; ty++)
                {
                    for (int tx = 0; tx < wid; tx++)
                    {
                        if (!IsSolidTileRaw(map, tx, ty))
                            continue;

                        double wx0 = tx * TilePx;
                        double wy0 = ty * TilePx;
                        double wx1 = wx0 + TilePx;
                        double wy1 = wy0 + TilePx;

                        // Top edge (neighbor above is non-solid)
                        if (!IsSolidTileRaw(map, tx, ty - 1))
                            _terrainEdges.Add(new TerrainEdge { X1 = wx0, Y1 = wy0, X2 = wx1, Y2 = wy0 });
                        // Bottom edge
                        if (!IsSolidTileRaw(map, tx, ty + 1))
                            _terrainEdges.Add(new TerrainEdge { X1 = wx0, Y1 = wy1, X2 = wx1, Y2 = wy1 });
                        // Left edge
                        if (!IsSolidTileRaw(map, tx - 1, ty))
                            _terrainEdges.Add(new TerrainEdge { X1 = wx0, Y1 = wy0, X2 = wx0, Y2 = wy1 });
                        // Right edge
                        if (!IsSolidTileRaw(map, tx + 1, ty))
                            _terrainEdges.Add(new TerrainEdge { X1 = wx1, Y1 = wy0, X2 = wx1, Y2 = wy1 });
                    }
                }

                _terrainEdgesReady = true;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[EchoVision] BuildTerrainEdges failed: " + RealError(ex));
            }
        }

        /// <summary>
        /// Direct tile collision read without hero parameter — used for full-map scanning.
        /// </summary>
        private static bool IsSolidTileRaw(dc.level.LevelMap map, int tx, int ty)
        {
            if (tx < 0 || ty < 0 || tx >= map.wid || ty >= map.hei)
                return false;

            var collisions = map.collisions;
            if (collisions == null)
                return false;

            int idx = tx + ty * map.wid;
            if (idx < 0 || idx >= collisions.length)
                return false;

            int v = PseudocodeHelper.ReadMem<int>(collisions.bytes, idx << 2);
            return (v & 1) != 0;
        }

        private static void DrawTerrainOutlines(Hero hero)
        {
            if (!_terrainEdgesReady || _terrainEdges.Count == 0)
                return;

            try
            {
                double vcx, vcy, vw, vh;
                GetViewportWorldRect(hero, out vcx, out vcy, out vw, out vh);
                double margin = 160.0;
                double xMin = vcx - vw * 0.5 - margin;
                double xMax = vcx + vw * 0.5 + margin;
                double yMin = vcy - vh * 0.5 - margin;
                double yMax = vcy + vh * 0.5 + margin;

                int drawn = 0;
                int count = _terrainEdges.Count;

                for (int i = 0; i < count; i++)
                {
                    if (drawn >= MaxTerrainEdgesPerFrame)
                        return;

                    TerrainEdge e = _terrainEdges[i];

                    // Fast AABB check: skip if both endpoints are outside viewport
                    if (e.X1 < xMin && e.X2 < xMin) continue;
                    if (e.X1 > xMax && e.X2 > xMax) continue;
                    if (e.Y1 < yMin && e.Y2 < yMin) continue;
                    if (e.Y1 > yMax && e.Y2 > yMax) continue;

                    DrawTerrainEdge(e.X1, e.Y1, e.X2, e.Y2);
                    drawn++;
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[EchoVision] DrawTerrainOutlines 失败：" + RealError(ex));
            }
        }

        private static void DrawTerrainEdge(double wx1, double wy1, double wx2, double wy2)
        {
            // Terrain edges are already viewport-bounded by the calling loop,
            // no per-edge visibility check needed. Always draw at full alpha.
            double a = 1.0;

            DrawThickLine(EchoGfx, wx1, wy1, wx2, wy2, 5.2, Gray3, a * 0.16);
            DrawThickLine(EchoGfx, wx1, wy1, wx2, wy2, 2.4, Gray1, a * 0.46);
            DrawThickLine(EchoGfx, wx1, wy1, wx2, wy2, 1.0, White, a * 0.82);
        }

        private static bool IsSolidTile(Hero hero, int tx, int ty)
        {
            try
            {
                if (hero == null || hero._level == null || hero._level.map == null)
                    return false;

                var map = hero._level.map;

                if (tx < 0 || ty < 0 || tx >= map.wid || ty >= map.hei)
                    return false;

                var collisions = map.collisions;

                if (collisions == null)
                    return false;

                int idx = tx + ty * map.wid;

                if (idx < 0 || idx >= collisions.length)
                    return false;

                int v = PseudocodeHelper.ReadMem<int>(collisions.bytes, idx << 2);

                return (v & 1) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static void DrawSoftEchoRings()
        {
            for (int i = 0; i < Pings.Count; i++)
            {
                EchoPing p = Pings[i];

                double t = Clamp(p.Age / p.Life, 0.0, 1.0);
                double ease = 1.0 - (1.0 - t) * (1.0 - t);
                double radius = p.StartRadius + (p.MaxRadius - p.StartRadius) * ease;
                double alpha = Clamp(p.Intensity * (1.0 - t), 0.0, 1.0);

                DrawRing(EchoGfx, p.X, p.Y, radius, 2.0, White, alpha * 0.15, 88);

                if (radius > 90.0)
                    DrawRing(EchoGfx, p.X, p.Y, radius * 0.66, 1.0, Gray2, alpha * 0.055, 64);
            }
        }

        // 对 HParticle/HSpriteBatch 这种批处理特效，暂时用声波点附近白线模拟。
        private static void DrawFxApproximation(Hero hero)
        {
            if (Pings.Count <= 0)
                return;

            for (int i = 0; i < Pings.Count; i++)
            {
                EchoPing p = Pings[i];

                double t = Clamp(p.Age / p.Life, 0.0, 1.0);
                double alpha = Clamp(p.Intensity * (1.0 - t), 0.0, 1.0) * 0.22;

                if (alpha <= 0.02)
                    continue;

                double radius = p.StartRadius + (p.MaxRadius - p.StartRadius) * (1.0 - (1.0 - t) * (1.0 - t));

                int spokes = 10;

                for (int s = 0; s < spokes; s++)
                {
                    double a = s * 6.28318530718 / spokes;
                    double r1 = radius * 0.72;
                    double r2 = radius * 0.98;

                    double x1 = p.X + System.Math.Cos(a) * r1;
                    double y1 = p.Y + System.Math.Sin(a) * r1;
                    double x2 = p.X + System.Math.Cos(a) * r2;
                    double y2 = p.Y + System.Math.Sin(a) * r2;

                    DrawThickLine(EchoGfx, x1, y1, x2, y2, 1.0, White, alpha);
                }
            }
        }

        // ============================================================
        // 可见度 / 坐标
        // ============================================================

        private static double GetVisibilityAt(double wx, double wy)
        {
            double best = 0.0;

            for (int i = 0; i < Pings.Count; i++)
            {
                EchoPing p = Pings[i];

                double t = Clamp(p.Age / p.Life, 0.0, 1.0);
                double ease = 1.0 - (1.0 - t) * (1.0 - t);
                double radius = p.StartRadius + (p.MaxRadius - p.StartRadius) * ease;

                double dx = wx - p.X;
                double dy = wy - p.Y;
                double dist = System.Math.Sqrt(dx * dx + dy * dy);

                double band = 68.0 + p.Intensity * 54.0;
                double ring = 1.0 - System.Math.Abs(dist - radius) / band;
                ring = Clamp(ring, 0.0, 1.0);

                double afterGlow = 0.0;

                if (dist < radius)
                {
                    double inside = 1.0 - dist / System.Math.Max(radius, 1.0);
                    afterGlow = 0.34 * inside;
                }

                double fade = 1.0 - t;
                double v = (ring * 1.05 + afterGlow) * fade * p.Intensity;

                if (v > best)
                    best = v;
            }

            return Clamp(best, 0.0, 1.0);
        }

        private static void GetViewportWorldRect(Hero hero, out double cx, out double cy, out double w, out double h)
        {
            cx = GetEntityX(hero);
            cy = GetEntityCenterY(hero);
            w = 480.0;
            h = 270.0;

            try
            {
                if (hero != null && hero._level != null && hero._level.viewport != null)
                {
                    var vp = hero._level.viewport;

                    cx = vp.realX;
                    cy = vp.realY;
                    w = vp.wid;
                    h = vp.hei;

                    if (w <= 32.0)
                        w = 480.0;

                    if (h <= 32.0)
                        h = 270.0;
                }
            }
            catch
            {
            }
        }

        private static double GetEntityX(Entity e)
        {
            try
            {
                return ((double)e.cx + e.xr) * TilePx;
            }
            catch
            {
                return 0.0;
            }
        }

        private static double GetEntityCenterY(Entity e)
        {
            try
            {
                return ((double)e.cy + e.yr) * TilePx - e.hei * 0.5;
            }
            catch
            {
                return 0.0;
            }
        }

        private static HSprite GetEntitySprite(Entity e)
        {
            try
            {
                if (e != null)
                    return e.spr;
            }
            catch
            {
            }

            return null;
        }

        private static bool IsValidEntityBasic(Entity e)
        {
            if (e == null)
                return false;

            try
            {
                if (e.destroyed)
                    return false;
            }
            catch
            {
                return false;
            }

            try
            {
                if (e.life <= 0)
                {
                    string tn = e.GetType().FullName ?? "";

                    // 门、箱子、掉落物、机关可能没有常规 life，也允许进候选
                    if (!tn.Contains(".inter.") &&
                        !tn.Contains(".loot.") &&
                        !tn.Contains(".ltrap.") &&
                        !tn.Contains("Door") &&
                        !tn.Contains("Chest") &&
                        !tn.Contains("Breakable") &&
                        !tn.Contains("Loot") &&
                        !tn.Contains("Trap"))
                    {
                        return false;
                    }
                }
            }
            catch
            {
            }

            try
            {
                HSprite spr = e.spr;

                if (spr == null)
                    return false;

                if (spr.destroyed)
                    return false;
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static Hero GetHero()
        {
            try
            {
                if (dc.pr.Game.Class.ME != null && dc.pr.Game.Class.ME.hero != null)
                    return dc.pr.Game.Class.ME.hero;
            }
            catch
            {
            }

            return null;
        }

        // ============================================================
        // Graphics 工具
        // ============================================================

        private static void DrawFilledRect(Graphics g, double x, double y, double w, double h, int color, double alpha)
        {
            DrawFilledQuad(g, x, y, x + w, y, x + w, y + h, x, y + h, color, alpha);
        }

        private static void DrawThickLine(Graphics g, double x1, double y1, double x2, double y2, double thickness, int color, double alpha)
        {
            if (g == null || alpha <= 0.0)
                return;

            double dx = x2 - x1;
            double dy = y2 - y1;
            double len = System.Math.Sqrt(dx * dx + dy * dy);

            if (len <= 0.01)
                return;

            double nx = -dy / len * thickness * 0.5;
            double ny = dx / len * thickness * 0.5;

            DrawFilledQuad(
                g,
                x1 + nx, y1 + ny,
                x2 + nx, y2 + ny,
                x2 - nx, y2 - ny,
                x1 - nx, y1 - ny,
                color,
                alpha
            );
        }

        private static void DrawRing(Graphics g, double cx, double cy, double radius, double thickness, int color, double alpha, int segments)
        {
            if (g == null || radius <= 1.0 || alpha <= 0.0)
                return;

            if (segments < 8)
                segments = 8;

            double r0 = radius - thickness * 0.5;
            double r1 = radius + thickness * 0.5;

            if (r0 < 1.0)
                r0 = 1.0;

            for (int i = 0; i < segments; i++)
            {
                double a0 = i * 6.28318530718 / segments;
                double a1 = (i + 1) * 6.28318530718 / segments;

                double x1 = cx + System.Math.Cos(a0) * r0;
                double y1 = cy + System.Math.Sin(a0) * r0;
                double x2 = cx + System.Math.Cos(a1) * r0;
                double y2 = cy + System.Math.Sin(a1) * r0;
                double x3 = cx + System.Math.Cos(a1) * r1;
                double y3 = cy + System.Math.Sin(a1) * r1;
                double x4 = cx + System.Math.Cos(a0) * r1;
                double y4 = cy + System.Math.Sin(a0) * r1;

                DrawFilledQuad(g, x1, y1, x2, y2, x3, y3, x4, y4, color, alpha);
            }
        }

        private static void DrawFilledQuad(
            Graphics g,
            double x1, double y1,
            double x2, double y2,
            double x3, double y3,
            double x4, double y4,
            int color,
            double alpha)
        {
            if (g == null || alpha <= 0.0)
                return;

            alpha = Clamp(alpha, 0.0, 1.0);

            double r;
            double gr;
            double b;

            ColorToRgb01(color, out r, out gr, out b);

            BeginFill(g);

            AddVertex(g, x1, y1, r, gr, b, alpha);
            AddVertex(g, x2, y2, r, gr, b, alpha);
            AddVertex(g, x3, y3, r, gr, b, alpha);
            AddVertex(g, x4, y4, r, gr, b, alpha);
            AddVertex(g, x1, y1, r, gr, b, alpha);

            g.endFill();
        }

        private static void BeginFill(Graphics g)
        {
            Ref<int> c = default;
            Ref<double> a = default;
            g.beginFill(c, a);
        }

        private static void AddVertex(Graphics g, double x, double y, double r, double gr, double b, double a)
        {
            Ref<double> u = default;
            Ref<double> v = default;
            g.addVertex(x, y, r, gr, b, a, u, v);
        }

        private static void ColorToRgb01(int color, out double r, out double g, out double b)
        {
            r = ((color >> 16) & 255) / 255.0;
            g = ((color >> 8) & 255) / 255.0;
            b = (color & 255) / 255.0;
        }

        private static void SafeClearGraphics(ref Graphics g)
        {
            if (g == null)
                return;

            object dummy;

            if (TryInvokeBestMethod(g, "clear", out dummy))
                return;

            try
            {
                g.remove();
            }
            catch
            {
            }

            try
            {
                if (OverlayRoot != null)
                    g = new Graphics(OverlayRoot);
                else
                    g = null;
            }
            catch
            {
                g = null;
            }
        }

        // ============================================================
        // 清理
        // ============================================================

        private static void ClearOverlay()
        {
            foreach (KeyValuePair<int, SpriteEcho> kv in SpriteEchoes)
                DestroySpriteEcho(kv.Value);

            SpriteEchoes.Clear();

            try
            {
                if (BlackGfx != null)
                    BlackGfx.remove();
            }
            catch
            {
            }

            try
            {
                if (EchoGfx != null)
                    EchoGfx.remove();
            }
            catch
            {
            }

            try
            {
                if (EchoSpriteLayer != null)
                    EchoSpriteLayer.remove();
            }
            catch
            {
            }

            try
            {
                if (OverlayRoot != null)
                    OverlayRoot.remove();
            }
            catch
            {
            }

            BlackGfx = null;
            EchoGfx = null;
            EchoSpriteLayer = null;
            OverlayRoot = null;
            CurrentLevelKey = null;

            _terrainEdges.Clear();
            _terrainEdgesReady = false;
        }

        private static void ResetRuntimeState()
        {
            Pings.Clear();
            Tracks.Clear();
            CachedEntities.Clear();
            CachedEntityKeys.Clear();

            HasLastHeroPos = false;
            FootStepTimer = 0.0;
            AttackTimer = 0.0;
            EnemyScanTimer = 0.0;
            EntityScanTimer = 0.0;

            LastVDown = false;
            LastF8Down = false;

            _warmupRemaining = WarmupDuration;
            _frameCounter = 0;
            _terrainEdges.Clear();
            _terrainEdgesReady = false;
        }

        void IOnGameExit.OnGameExit()
        {
            ClearOverlay();
            ResetRuntimeState();
            Enabled = true;
            System.Console.WriteLine("[EchoVision] 已清理");
        }

        // ============================================================
        // 通用工具
        // ============================================================

        private static bool IsAnyKeyDown(int[] keys)
        {
            if (keys == null)
                return false;

            for (int i = 0; i < keys.Length; i++)
            {
                if (IsKeyDown(keys[i]))
                    return true;
            }

            return false;
        }

        private static bool IsKeyDown(int vkey)
        {
            try
            {
                return ((int)GetAsyncKeyState(vkey) & 32768) != 0;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        private static int FloorToInt(double v)
        {
            return (int)System.Math.Floor(v);
        }

        private static int CeilToInt(double v)
        {
            return (int)System.Math.Ceiling(v);
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min)
                return min;

            if (v > max)
                return max;

            return v;
        }

        private static bool LikelyEntityContainerName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;

            n = n.ToLowerInvariant();

            return n.Contains("ent") ||
                   n.Contains("mob") ||
                   n.Contains("loot") ||
                   n.Contains("door") ||
                   n.Contains("trap") ||
                   n.Contains("bullet") ||
                   n.Contains("ammo") ||
                   n.Contains("chest") ||
                   n.Contains("inter") ||
                   n.Contains("npc") ||
                   n.Contains("item") ||
                   n.Contains("projectile");
        }

        private static bool LikelySpriteFieldName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;

            n = n.ToLowerInvariant();

            return n.Contains("spr") ||
                   n.Contains("sprite") ||
                   n.Contains("fx") ||
                   n.Contains("head") ||
                   n.Contains("trail") ||
                   n.Contains("weapon") ||
                   n.Contains("preload") ||
                   n.Contains("beat") ||
                   n.Contains("glow");
        }

        private static bool IsPrimitiveLike(System.Type t)
        {
            if (t == null)
                return true;

            if (t.IsPrimitive || t.IsEnum)
                return true;

            if (t == typeof(string) ||
                t == typeof(decimal) ||
                t == typeof(System.IntPtr))
                return true;

            return false;
        }

        private static bool TryGetMemberValue(object obj, string name, out object value)
        {
            value = null;

            if (obj == null || string.IsNullOrEmpty(name))
                return false;

            try
            {
                System.Type t = obj.GetType();

                PropertyInfo p = t.GetProperty(
                    name,
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.Static
                );

                if (p != null && p.GetIndexParameters().Length == 0)
                {
                    value = p.GetValue(obj, null);
                    return true;
                }

                FieldInfo f = t.GetField(
                    name,
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.Static
                );

                if (f != null)
                {
                    value = f.GetValue(obj);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryGetNumberMember(object obj, string name, out double value)
        {
            value = 0.0;

            object raw;

            if (!TryGetMemberValue(obj, name, out raw) || raw == null)
                return false;

            try
            {
                value = System.Convert.ToDouble(raw);
                return true;
            }
            catch
            {
            }

            return false;
        }

        private static bool TryInvokeBestMethod(object obj, string methodName, out object result, params object[] args)
        {
            result = null;

            if (obj == null || string.IsNullOrEmpty(methodName))
                return false;

            try
            {
                System.Type t = obj.GetType();

                MethodInfo[] methods = t.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.Static
                );

                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];

                    if (m == null || m.Name != methodName)
                        continue;

                    ParameterInfo[] ps = m.GetParameters();

                    if (ps.Length != args.Length)
                        continue;

                    try
                    {
                        result = m.Invoke(obj, args);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static string SafeToString(object obj)
        {
            if (obj == null)
                return "";

            try
            {
                return obj.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string RealError(Exception ex)
        {
            if (ex == null)
                return "";

            if (ex.InnerException != null)
                return ex.InnerException.Message;

            return ex.Message;
        }
    }
}
