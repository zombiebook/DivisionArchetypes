using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Duckov.Modding;
using HarmonyLib;
using ItemStatsSystem;

namespace DivisionArchetypes
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private static Harmony? _harmony;

        protected override void OnAfterSetup()
        {
            try
            {
                Debug.Log("[DivisionArchetypes] Initializing...");

                _harmony = new Harmony("com.divisionarchetypes.mod");
                _harmony.PatchAll();

                // HUD 즉시 생성
                if (ArchetypeHUD.Instance == null)
                {
                    var go = new GameObject("DivisionArchetypes_HUD");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<ArchetypeHUD>();
                    Debug.Log("[DivisionArchetypes] HUD created.");
                }

                Debug.Log("[DivisionArchetypes] Harmony patches applied.");
                Debug.Log("[DivisionArchetypes] Mod loaded! Enemies will receive random archetypes.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionArchetypes] Init failed: {ex.Message}");
            }
        }
    }

    // === 아키타입 정의 ===
    public enum Archetype
    {
        Assault,    // 기본 보병
        Rusher,     // 돌격형
        Tank,       // 중장갑
        Sniper,     // 저격수
        Support,    // 힐러/지원
        Thrower,    // 투척병
        Controller, // 제어형
        Heavy,      // 중화기
        Leader,     // 지휘관
        Scout       // 정찰병
    }

    public static class ArchetypeConfig
    {
        // 아키타입별 스탯 배율 (hp, speed, damage, aggro, armor, icon, color)
        public static readonly Dictionary<Archetype, ArchetypeStats> Stats = new Dictionary<Archetype, ArchetypeStats>
        {
            [Archetype.Assault]    = new ArchetypeStats(1.0f, 1.0f, 1.0f, 1.0f, 0f,    "▌", new Color(0.8f, 0.8f, 0.8f)),
            [Archetype.Rusher]    = new ArchetypeStats(0.6f, 1.8f, 0.8f, 1.3f, 0f,    "⚡", new Color(1f, 0.4f, 0.1f)),
            [Archetype.Tank]      = new ArchetypeStats(3.0f, 0.6f, 0.7f, 0.8f, 200f,  "🛡", new Color(0.3f, 0.3f, 1f)),
            [Archetype.Sniper]    = new ArchetypeStats(0.7f, 0.7f, 2.5f, 0.5f, 0f,    "◎", new Color(0.9f, 0.1f, 0.1f)),
            [Archetype.Support]   = new ArchetypeStats(0.8f, 1.0f, 0.6f, 1.0f, 50f,   "✚", new Color(0.2f, 1f, 0.2f)),
            [Archetype.Thrower]   = new ArchetypeStats(0.8f, 1.0f, 1.5f, 0.9f, 0f,    "●", new Color(1f, 0.6f, 0f)),
            [Archetype.Controller]= new ArchetypeStats(0.9f, 0.9f, 1.3f, 0.8f, 50f,   "◈", new Color(0.7f, 0.3f, 1f)),
            [Archetype.Heavy]     = new ArchetypeStats(2.0f, 0.7f, 1.8f, 0.7f, 150f,  "▼", new Color(0.5f, 0.2f, 0.1f)),
            [Archetype.Leader]    = new ArchetypeStats(1.5f, 1.0f, 1.2f, 1.0f, 100f,  "★", new Color(1f, 0.85f, 0f)),
            [Archetype.Scout]     = new ArchetypeStats(0.5f, 1.5f, 1.0f, 1.4f, 0f,    "◇", new Color(0.4f, 0.9f, 0.9f)),
        };

        // 가중치 (일반 적이 더 많이 나오도록)
        public static readonly (Archetype type, float weight)[] SpawnWeights = new[]
        {
            (Archetype.Assault, 25f),
            (Archetype.Rusher, 15f),
            (Archetype.Tank, 8f),
            (Archetype.Sniper, 12f),
            (Archetype.Support, 8f),
            (Archetype.Thrower, 10f),
            (Archetype.Controller, 7f),
            (Archetype.Heavy, 5f),
            (Archetype.Leader, 4f),
            (Archetype.Scout, 6f),
        };

        public static Archetype GetRandomArchetype()
        {
            float totalWeight = 0f;
            foreach (var w in SpawnWeights) totalWeight += w.weight;

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;
            foreach (var w in SpawnWeights)
            {
                cumulative += w.weight;
                if (roll <= cumulative) return w.type;
            }
            return Archetype.Assault;
        }
    }

    public class ArchetypeStats
    {
        public float HealthMult;    // 체력 배율
        public float SpeedMult;     // 이동속도 배율
        public float DamageMult;    // 데미지 배율
        public float AggroRange;    // 어그로 범위 배율
        public float ArmorAmount;   // 아머량 (0 = 없음)
        public string Icon;         // 표시 아이콘
        public Color GlowColor;    // 발광 색상

        public ArchetypeStats(float hp, float spd, float dmg, float aggro, float armor, string icon, Color color)
        {
            HealthMult = hp;
            SpeedMult = spd;
            DamageMult = dmg;
            AggroRange = aggro;
            ArmorAmount = armor;
            Icon = icon;
            GlowColor = color;
        }
    }

    // === 아키타입 마커 컴포넌트 (적에게 부착) ===
    public class ArchetypeMarker : MonoBehaviour
    {
        public Archetype Type;
        public ArchetypeStats? Stats;
        public float SupportHealTimer = 0f;
        public float LeaderBuffTimer = 0f;

        // 아머 시스템
        public float MaxArmor = 0f;
        public float CurrentArmor = 0f;
        public bool HasArmor => MaxArmor > 0f && CurrentArmor > 0f;

        private const float SUPPORT_HEAL_INTERVAL = 3f;
        private const float SUPPORT_HEAL_AMOUNT = 5f;

        // 전역 마커 리스트 (FindObjectsOfType 대체)
        public static readonly List<ArchetypeMarker> AllMarkers = new List<ArchetypeMarker>();

        void OnEnable()
        {
            AllMarkers.Add(this);
        }

        void OnDisable()
        {
            AllMarkers.Remove(this);
        }

        void OnDestroy()
        {
            AllMarkers.Remove(this);
        }

        void Update()
        {
            if (Stats == null) return;

            // Support: 주변 아군 회복
            if (Type == Archetype.Support)
            {
                var character = GetComponent<CharacterMainControl>();
                if (character == null || character.Health == null || character.Health.IsDead) return;

                float now = Time.time;
                if (now - SupportHealTimer > SUPPORT_HEAL_INTERVAL)
                {
                    SupportHealTimer = now;
                    HealNearbyAllies(character);
                }
            }

            // Rusher: 플레이어 근접 시 자폭
            if (Type == Archetype.Rusher)
            {
                UpdateRusherExplosion();
            }
        }

        // === Rusher 자폭 시스템 ===
        private const float EXPLOSION_TRIGGER_RANGE = 5f;  // 점멸 시작 거리
        private const float EXPLOSION_RANGE = 2.5f;        // 실제 폭발 거리
        private const float EXPLOSION_DAMAGE = 40f;
        private const float EXPLOSION_FUSE_TIME = 1.5f;    // 점멸 후 폭발까지 시간
        private bool _hasExploded = false;
        private bool _fuseStarted = false;
        private float _fuseStartTime = 0f;

        // Rusher 돌진 시스템
        private static FieldInfo? _pathControlField = null;
        private static MethodInfo? _moveToPosMethod = null;
        private static bool _rushCacheInit = false;

        void UpdateRusherExplosion()
        {
            if (_hasExploded) return;

            var character = GetComponent<CharacterMainControl>();
            if (character == null || character.Health == null || character.Health.IsDead) return;

            var player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;

            float dist = Vector3.Distance(transform.position, player.transform.position);

            // 항상 플레이어를 향해 돌진
            RushTowardPlayer(character, player, dist);

            // 점멸 시작 (트리거 범위 진입)
            if (!_fuseStarted && dist <= EXPLOSION_TRIGGER_RANGE)
            {
                _fuseStarted = true;
                _fuseStartTime = Time.time;
            }

            // 점멸 중: 빨간색 깜빡임
            if (_fuseStarted && !_hasExploded)
            {
                float elapsed = Time.time - _fuseStartTime;
                float blinkSpeed = Mathf.Lerp(4f, 15f, elapsed / EXPLOSION_FUSE_TIME);
                bool blinkOn = Mathf.Sin(Time.time * blinkSpeed) > 0f;

                ApplyBlinkEffect(character, blinkOn);

                if (elapsed >= EXPLOSION_FUSE_TIME || dist <= EXPLOSION_RANGE)
                {
                    _hasExploded = true;
                    DoExplosion(character, player);
                }
            }
        }

        void RushTowardPlayer(CharacterMainControl character, CharacterMainControl player, float dist)
        {
            // 30m 이내에서만 돌진
            if (dist > 30f || dist < EXPLOSION_RANGE) return;

            try
            {
                // AI PathControl 캐시 초기화
                if (!_rushCacheInit)
                {
                    _rushCacheInit = true;
                    var aiType = character.GetComponent<MonoBehaviour>()?.GetType();

                    // AICharacterController에서 pathControl 찾기
                    foreach (var comp in character.GetComponents<MonoBehaviour>())
                    {
                        if (comp == null) continue;
                        var compType = comp.GetType();
                        if (compType.Name == "AICharacterController" || compType.Name.Contains("AICharacter"))
                        {
                            var bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                            _pathControlField = compType.GetField("pathControl", bf);
                            break;
                        }
                    }

                    // AI_PathControl.MoveToPos 메서드 찾기
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var pcType = asm.GetType("AI_PathControl");
                        if (pcType != null)
                        {
                            _moveToPosMethod = pcType.GetMethod("MoveToPos",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            break;
                        }
                    }
                }

                // PathControl을 통해 플레이어 위치로 이동
                if (_pathControlField != null && _moveToPosMethod != null)
                {
                    foreach (var comp in character.GetComponents<MonoBehaviour>())
                    {
                        if (comp == null) continue;
                        if (comp.GetType().Name == "AICharacterController" || comp.GetType().Name.Contains("AICharacter"))
                        {
                            var pathControl = _pathControlField.GetValue(comp);
                            if (pathControl != null)
                            {
                                _moveToPosMethod.Invoke(pathControl, new object[] { player.transform.position });
                            }
                            break;
                        }
                    }
                }
                else
                {
                    // 폴백: 직접 transform 이동 (부드럽게)
                    Vector3 dir = (player.transform.position - transform.position).normalized;
                    transform.position += dir * Stats!.SpeedMult * 3f * Time.deltaTime;
                }
            }
            catch { }
        }

        void ApplyBlinkEffect(CharacterMainControl character, bool redOn)
        {
            try
            {
                if (character.characterModel == null) return;

                Renderer[] renderers = character.characterModel.GetComponentsInChildren<Renderer>(true);
                if (renderers == null) return;

                int emissionKey = Shader.PropertyToID("_EmissionColor");
                Color emission = redOn ? new Color(3f, 0f, 0f, 1f) : Color.black;

                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;
                    Material[] materials = renderer.materials;
                    foreach (var mat in materials)
                    {
                        if (mat == null) continue;
                        if (mat.HasProperty(emissionKey))
                        {
                            mat.SetColor(emissionKey, emission);
                        }
                    }
                }
            }
            catch { }
        }

        void DoExplosion(CharacterMainControl self, CharacterMainControl player)
        {
            try
            {
                // 폭발 이펙트
                SpawnExplosionEffect(self.transform.position);

                // 플레이어에게 데미지
                if (player.Health != null && !player.Health.IsDead)
                {
                    float after = player.Health.CurrentHealth - EXPLOSION_DAMAGE;
                    if (after < 1f) after = 1f;
                    player.Health.CurrentHealth = after;
                }

                // 자기 자신 사망
                if (self.Health != null && !self.Health.IsDead)
                {
                    self.Health.CurrentHealth = 0f;
                }

                try { player.PopText("BOOM! -" + EXPLOSION_DAMAGE + "HP", -1f); } catch { }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionArchetypes] DoExplosion FAILED: {ex.Message}");
            }
        }

        void SpawnExplosionEffect(Vector3 position)
        {
            try
            {
                // 자체 폭발 이펙트 생성 (파티클 시스템)
                var fxGO = new GameObject("RusherExplosion");
                fxGO.transform.position = position;

                // 메인 폭발 파티클
                var ps = fxGO.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.startLifetime = 0.5f;
                main.startSpeed = 8f;
                main.startSize = 1.5f;
                main.startColor = new Color(1f, 0.5f, 0f, 1f);
                main.maxParticles = 30;
                main.duration = 0.3f;
                main.loop = false;

                var emission = ps.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new ParticleSystem.Burst[] {
                    new ParticleSystem.Burst(0f, 30)
                });

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.5f;

                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(new Color(1f, 0.8f, 0.2f), 0f),
                        new GradientColorKey(new Color(1f, 0.3f, 0f), 0.5f),
                        new GradientColorKey(new Color(0.3f, 0.1f, 0f), 1f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0.8f, 0.5f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                colorOverLifetime.color = gradient;

                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.5f), new Keyframe(1f, 2f)
                ));

                // 렌더러 설정
                var renderer = fxGO.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.material = new Material(Shader.Find("Particles/Standard Unlit"));
                    renderer.material.color = new Color(1f, 0.6f, 0.1f, 1f);
                }

                // 라이트 추가
                var lightGO = new GameObject("ExplosionLight");
                lightGO.transform.position = position;
                lightGO.transform.SetParent(fxGO.transform);
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.5f, 0f);
                light.intensity = 5f;
                light.range = 12f;

                ps.Play();
                UnityEngine.Object.Destroy(fxGO, 2f);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionArchetypes] SpawnExplosionEffect error: {ex.Message}");
            }
        }

        /// <summary>
        /// 아머에 데미지 적용. 남은 데미지 반환 (아머가 흡수 못한 부분)
        /// </summary>
        public float AbsorbDamage(float damage)
        {
            if (CurrentArmor <= 0f) return damage;

            if (damage <= CurrentArmor)
            {
                CurrentArmor -= damage;
                return 0f; // 아머가 전부 흡수
            }
            else
            {
                float remaining = damage - CurrentArmor;
                CurrentArmor = 0f;
                return remaining; // 남은 데미지는 HP로
            }
        }

        void HealNearbyAllies(CharacterMainControl self)
        {
            try
            {
                // AllMarkers 리스트에서 주변 아군 찾기 (Physics 대신)
                foreach (var other in AllMarkers)
                {
                    if (other == null || other == this) continue;

                    var otherChar = other.GetComponent<CharacterMainControl>();
                    if (otherChar == null || otherChar == self || otherChar == CharacterMainControl.Main) continue;
                    if (otherChar.Health == null || otherChar.Health.IsDead) continue;

                    float dist = Vector3.Distance(transform.position, other.transform.position);
                    if (dist > 8f) continue;

                    // 체력 회복 (AddHealth)
                    try
                    {
                        otherChar.Health.AddHealth(SUPPORT_HEAL_AMOUNT);
                    }
                    catch
                    {
                        // AddHealth가 직접 호출 안 되면 리플렉션
                        try
                        {
                            var addHp = typeof(Health).GetMethod("AddHealth",
                                BindingFlags.Public | BindingFlags.Instance);
                            if (addHp != null)
                                addHp.Invoke(otherChar.Health, new object[] { SUPPORT_HEAL_AMOUNT });
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
    }

    // 적이 생성된 후 아키타입 부여 - Health.Start 패치
    [HarmonyPatch(typeof(Health), "Start")]
    public static class Health_Start_Patch
    {
        [HarmonyPostfix]
        static void Postfix(Health __instance)
        {
            try
            {
                var go = __instance.gameObject;
                if (go == null) return;

                var character = go.GetComponent<CharacterMainControl>();
                if (character == null) return;

                if (go.GetComponent<ArchetypeMarker>() != null) return;

                go.AddComponent<ArchetypeApplier>();
            }
            catch { }
        }
    }

    /// <summary>
    /// 적 생성 후 1프레임 뒤에 아키타입을 적용하는 헬퍼
    /// </summary>
    public class ArchetypeApplier : MonoBehaviour
    {
        private int _frameCount = 0;

        void Update()
        {
            _frameCount++;
            if (_frameCount < 3) return; // 3프레임 대기 (초기화 완료)

            var character = GetComponent<CharacterMainControl>();
            if (character == null || character == CharacterMainControl.Main)
            {
                Destroy(this);
                return;
            }

            // 이미 마커가 있으면 스킵
            if (GetComponent<ArchetypeMarker>() != null)
            {
                Destroy(this);
                return;
            }

            // 동물/펫/상인/더미 제외
            string name = gameObject.name.ToLower();
            if (name.Contains("pet") || name.Contains("animal") || name.Contains("dog") ||
                name.Contains("cat") || name.Contains("merchant") || name.Contains("dummy") ||
                name.Contains("npc") || name.Contains("civilian") || name.Contains("friend"))
            {
                Destroy(this);
                return;
            }

            // CharacterRandomPreset 체크 (동물 프리셋 제외)
            if (character.characterPreset != null)
            {
                string presetName = character.characterPreset.name ?? "";
                if (presetName.Contains("Animal") || presetName.Contains("Pet") ||
                    presetName.Contains("Dog") || presetName.Contains("Cat") ||
                    presetName.Contains("Merchant") || presetName.Contains("Dummy"))
                {
                    Destroy(this);
                    return;
                }
            }

            ApplyArchetype(character);
            Destroy(this);
        }

        void ApplyArchetype(CharacterMainControl character)
        {
            try
            {
                Archetype type = ArchetypeConfig.GetRandomArchetype();
                var stats = ArchetypeConfig.Stats[type];

                // 마커 부착
                var marker = gameObject.AddComponent<ArchetypeMarker>();
                marker.Type = type;
                marker.Stats = stats;

                // 아머 설정
                if (stats.ArmorAmount > 0f)
                {
                    marker.MaxArmor = stats.ArmorAmount;
                    marker.CurrentArmor = stats.ArmorAmount;
                }

                // 스탯 적용
                ApplyStats(character, stats, type);

                // 시각 효과 적용
                ApplyVisuals(character, stats);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionArchetypes] ApplyArchetype error: {ex.Message}");
            }
        }

        void ApplyStats(CharacterMainControl character, ArchetypeStats stats, Archetype type)
        {
            try
            {
                float origHp = 0f, newHp = 0f;

                // 체력 수정 (CharacterItem의 MaxHealth 스탯 사용)
                if (stats.HealthMult != 1f)
                {
                    Item? characterItem = character.CharacterItem;
                    if (characterItem != null)
                    {
                        int maxHealthHash = "MaxHealth".GetHashCode();
                        Stat? hpStat = characterItem.GetStat(maxHealthHash);
                        if (hpStat != null)
                        {
                            origHp = hpStat.BaseValue;
                            hpStat.BaseValue *= stats.HealthMult;
                            newHp = hpStat.BaseValue;

                            // 현재 체력을 최대로 갱신
                            if (character.Health != null)
                            {
                                character.Health.CurrentHealth = character.Health.MaxHealth;
                            }
                        }
                    }
                }

                // 이동속도 수정
                float origSpd = 0f;
                if (stats.SpeedMult != 1f)
                {
                    Item? characterItem = character.CharacterItem;
                    if (characterItem != null)
                    {
                        int walkHash = "WalkSpeed".GetHashCode();
                        int runHash = "RunSpeed".GetHashCode();

                        Stat? walkStat = characterItem.GetStat(walkHash);
                        Stat? runStat = characterItem.GetStat(runHash);

                        if (walkStat != null)
                        {
                            origSpd = walkStat.BaseValue;
                            walkStat.BaseValue *= stats.SpeedMult;
                        }
                        if (runStat != null) runStat.BaseValue *= stats.SpeedMult;
                    }
                }

                Debug.Log($"[DivisionArchetypes] ★ {character.name} -> {type} | HP:{origHp:F0}->{newHp:F0}(x{stats.HealthMult}) SPD:x{stats.SpeedMult} DMG:x{stats.DamageMult}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionArchetypes] ApplyStats error: {ex.Message}");
            }
        }

        void ApplyVisuals(CharacterMainControl character, ArchetypeStats stats)
        {
            try
            {
                if (character.characterModel == null) return;

                // 발광 색상 적용
                Renderer[] renderers = character.characterModel.GetComponentsInChildren<Renderer>(true);
                if (renderers == null) return;

                int emissionKey = Shader.PropertyToID("_EmissionColor");
                Color emission = stats.GlowColor * 0.3f; // 은은하게

                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;
                    Material[] materials = renderer.materials;
                    foreach (var mat in materials)
                    {
                        if (mat == null) continue;
                        if (mat.HasProperty(emissionKey))
                        {
                            mat.SetColor(emissionKey, emission);
                            try { mat.EnableKeyword("_EMISSION"); } catch { }
                        }
                    }
                    renderer.materials = materials;
                }
            }
            catch { }
        }
    }

    // === GUI: 적 HP바 옆에 아키타입 아이콘 표시 (Division 2 스타일) ===
    public class ArchetypeHUD : MonoBehaviour
    {
        public static ArchetypeHUD? Instance { get; private set; }

        private GUIStyle? _nameStyle;
        private Texture2D? _bgTex;

        // 아키타입별 아이콘 텍스처
        private static Dictionary<Archetype, Texture2D> _archetypeIcons = new Dictionary<Archetype, Texture2D>();
        private static bool _iconsLoaded = false;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            CreateTextures();
            LoadIcons();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void CreateTextures()
        {
            _bgTex = new Texture2D(1, 1);
            _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
            _bgTex.Apply();
        }

        /// <summary>
        /// 외부 icons 폴더에서 아키타입별 PNG 로드. 없으면 기본 아이콘 생성 후 저장.
        /// </summary>
        void LoadIcons()
        {
            if (_iconsLoaded) return;
            _iconsLoaded = true;

            try
            {
                string modFolder = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                string iconFolder = System.IO.Path.Combine(modFolder, "icons");

                if (!System.IO.Directory.Exists(iconFolder))
                {
                    System.IO.Directory.CreateDirectory(iconFolder);
                }

                foreach (Archetype archetype in Enum.GetValues(typeof(Archetype)))
                {
                    string fileName = archetype.ToString().ToLower() + ".png";
                    string pngPath = System.IO.Path.Combine(iconFolder, fileName);

                    var stats = ArchetypeConfig.Stats[archetype];
                    Texture2D? tex = null;

                    // 외부 파일이 있으면 로드
                    if (System.IO.File.Exists(pngPath))
                    {
                        tex = LoadTextureFromFile(pngPath);
                    }

                    // 없으면 기본 아이콘 생성 후 저장
                    if (tex == null)
                    {
                        tex = CreateDefaultIcon(archetype, stats.GlowColor);
                        try
                        {
                            System.IO.File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                        }
                        catch { }
                    }

                    _archetypeIcons[archetype] = tex;
                }

                Debug.Log($"[DivisionArchetypes] {_archetypeIcons.Count} archetype icons loaded from: {iconFolder}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionArchetypes] LoadIcons error: {ex.Message}");
            }
        }

        Texture2D? LoadTextureFromFile(string path)
        {
            try
            {
                byte[] data = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(data)) return tex;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 기본 아이콘 생성 (원형 + 아키타입 색상)
        /// </summary>
        Texture2D CreateDefaultIcon(Archetype archetype, Color baseColor)
        {
            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float radius = size / 2f - 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));

                    if (dist <= radius)
                    {
                        // 내부: 그라데이션
                        float norm = dist / radius;
                        Color c = Color.Lerp(baseColor, baseColor * 0.3f, norm);
                        c.a = 1f - (norm * 0.2f);

                        // 테두리 (바깥쪽 2px)
                        if (dist > radius - 3f)
                        {
                            c = Color.Lerp(c, Color.white, 0.5f);
                            c.a = 1f;
                        }

                        tex.SetPixel(x, y, c);
                    }
                    else
                    {
                        tex.SetPixel(x, y, new Color(0, 0, 0, 0));
                    }
                }
            }

            tex.Apply();
            return tex;
        }

        void OnGUI()
        {
            if (_nameStyle == null)
            {
                _nameStyle = new GUIStyle(GUI.skin.label);
                _nameStyle.fontSize = 11;
                _nameStyle.fontStyle = FontStyle.Bold;
                _nameStyle.alignment = TextAnchor.MiddleLeft;
            }

            var player = CharacterMainControl.Main;
            if (player == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            var markers = ArchetypeMarker.AllMarkers;
            foreach (var marker in markers)
            {
                if (marker == null || marker.Stats == null) continue;

                var character = marker.GetComponent<CharacterMainControl>();
                if (character == null || character.Health == null || character.Health.IsDead) continue;

                float dist = Vector3.Distance(player.transform.position, marker.transform.position);
                if (dist > 40f) continue;

                // HP바 위치 기준 (머리 위)
                Vector3 worldPos = marker.transform.position + Vector3.up * 2.0f;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                if (screenPos.z < 0) continue;

                float x = screenPos.x;
                float y = Screen.height - screenPos.y;

                // 거리에 따른 스케일
                float scale = Mathf.Clamp(1f - (dist / 40f), 0.4f, 1f);
                int iconSize = (int)(24 * scale);
                int fontSize = (int)(12 * scale);

                // === HP바 왼쪽에 아이콘 배치 ===
                float iconX = x - 60 * scale;
                float iconY = y - iconSize * 0.5f;

                // 배경 박스
                if (_bgTex != null)
                {
                    GUI.DrawTexture(new Rect(iconX - 2, iconY - 2, iconSize + 4, iconSize + 4), _bgTex);
                }

                // 아이콘 텍스처 표시
                if (_archetypeIcons.TryGetValue(marker.Type, out var iconTex) && iconTex != null)
                {
                    GUI.DrawTexture(new Rect(iconX, iconY, iconSize, iconSize), iconTex);
                }

                // 아키타입 이름 (아이콘 오른쪽)
                _nameStyle.fontSize = fontSize;
                _nameStyle.normal.textColor = marker.Stats.GlowColor;
                GUI.Label(new Rect(iconX + iconSize + 4, iconY + 2, 80 * scale, iconSize),
                    marker.Type.ToString(), _nameStyle);

                // === 아머바 표시 (하얀 점선, HP바 위) ===
                if (marker.MaxArmor > 0f && marker.CurrentArmor > 0f)
                {
                    float barWidth = 60f * scale;
                    float barHeight = 6f * scale;
                    float barX = x - barWidth * 0.5f;
                    float barY = y - iconSize - barHeight - 4f;

                    float armorRatio = marker.CurrentArmor / marker.MaxArmor;

                    // 배경 (어두운)
                    if (_bgTex != null)
                        GUI.DrawTexture(new Rect(barX - 1, barY - 1, barWidth + 2, barHeight + 2), _bgTex);

                    // 아머바 (하얀 점선 느낌 - 세그먼트로 표현)
                    int segments = 8;
                    float segWidth = barWidth / segments;
                    float gap = 2f * scale;
                    int filledSegments = Mathf.CeilToInt(armorRatio * segments);

                    Color armorColor = new Color(1f, 1f, 1f, 0.9f);
                    Texture2D whiteTex = Texture2D.whiteTexture;

                    for (int i = 0; i < filledSegments && i < segments; i++)
                    {
                        float segX = barX + i * segWidth + gap * 0.5f;
                        float segW = segWidth - gap;
                        if (segW < 1f) segW = 1f;

                        GUI.color = armorColor;
                        GUI.DrawTexture(new Rect(segX, barY, segW, barHeight), whiteTex);
                    }
                    GUI.color = Color.white;
                }
            }
        }
    }
    // === 데미지 수정: 아키타입별 데미지 배율 + 아머 시스템 ===
    [HarmonyPatch(typeof(Health), "Hurt")]
    public static class Health_Hurt_DamageMult_Patch
    {
        [HarmonyPrefix]
        static bool Prefix(Health __instance, object damageInfo)
        {
            try
            {
                if (damageInfo == null) return true;

                var bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var dmgField = damageInfo.GetType().GetField("damageValue", bf);
                var attackerField = damageInfo.GetType().GetField("fromCharacter", bf);

                // 적이 플레이어를 공격: 아키타입 데미지 배율 적용
                if (attackerField != null)
                {
                    var attacker = attackerField.GetValue(damageInfo) as CharacterMainControl;
                    if (attacker != null && attacker != CharacterMainControl.Main)
                    {
                        var marker = attacker.GetComponent<ArchetypeMarker>();
                        if (marker != null && marker.Stats != null && marker.Stats.DamageMult != 1f && dmgField != null)
                        {
                            float currentDmg = (float)dmgField.GetValue(damageInfo);
                            dmgField.SetValue(damageInfo, currentDmg * marker.Stats.DamageMult);
                        }
                    }
                }

                // 아머 시스템: 아머가 남아있으면 Hurt 차단
                var victim = __instance.GetComponent<CharacterMainControl>();
                if (victim != null && victim != CharacterMainControl.Main)
                {
                    var marker = victim.GetComponent<ArchetypeMarker>();
                    if (marker != null && marker.HasArmor && dmgField != null)
                    {
                        float currentDmg = (float)dmgField.GetValue(damageInfo);
                        float absorbed = Mathf.Min(currentDmg, marker.CurrentArmor);
                        marker.CurrentArmor -= absorbed;
                        float remaining = currentDmg - absorbed;

                        if (remaining <= 0f)
                        {
                            return false; // 아머가 전부 흡수, HP 무피해
                        }
                        else
                        {
                            dmgField.SetValue(damageInfo, remaining);
                        }
                    }
                }
            }
            catch { }

            return true;
        }
    }
}
