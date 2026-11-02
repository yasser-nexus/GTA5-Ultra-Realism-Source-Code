```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

namespace UltimateGTA6VibeSuite
{
    public class UltimateGTA6VibeSuiteScript : Script
    {
        #region Win32 Input (100% compatible, no Hash dependency)

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private readonly Dictionary<int, bool> _keyPrev = new Dictionary<int, bool>();

        private const int VK_N0 = 0x60;
        private const int VK_N1 = 0x61;
        private const int VK_N2 = 0x62;
        private const int VK_N3 = 0x63;
        private const int VK_N4 = 0x64;
        private const int VK_N5 = 0x65;
        private const int VK_N6 = 0x66;
        private const int VK_N7 = 0x67;
        private const int VK_N8 = 0x68;
        private const int VK_N9 = 0x69;

        private bool IsKeyJustPressed(int vk)
        {
            try
            {
                short raw = GetAsyncKeyState(vk);
                bool isDown = (raw & 0x8000) != 0;
                bool was;
                if (!_keyPrev.TryGetValue(vk, out was)) was = false;
                _keyPrev[vk] = isDown;
                return isDown && !was;
            }
            catch { return false; }
        }

        #endregion

        #region Configuration

        // Survival
        private const float MaxHunger = 100.0f;
        private const float MaxThirst = 100.0f;
        private const float MaxSleep = 100.0f;
        private const float HungerDecayPerMinute = 1.4f;
        private const float ThirstDecayPerMinute = 1.9f;
        private const float SleepDecayPerMinute = 0.9f;

        private const int EatCost = 8;
        private const float EatRestore = 30.0f;
        private const int DrinkCost = 5;
        private const float DrinkRestore = 35.0f;

        // Stamina
        private const float MaxStamina = 100.0f;
        private const float SprintDrainPerSecond = 10.5f;
        private const float StaminaRegenPerSecond = 6.5f;
        private const float ExhaustedRecoverThreshold = 30.0f;

        // Movement multipliers
        private const float InjuredMovementMul = 0.68f;
        private const float ExhaustedMovementMul = 0.55f;
        private const float CriticalHealthMovementMul = 0.82f;
        private const float StarvingMovementMul = 0.70f;
        private const float DehydratedMovementMul = 0.75f;
        private const float SleepyMovementMul = 0.80f;

        private const string InjuredClipset = "move_m@injured";
        private const string FatigueTimecycle = "Drunk";

        // Fuel
        private const float MaxFuel = 100.0f;
        private const int RefuelCost = 120;
        private const float RefuelRangeSq = 900.0f;

        // Physics
        private const float CrashSpeedThreshold = 16.0f;
        private const float CrashDecelThreshold = 7.0f;
        private const float OffRoadStressThreshold = 16.0f;
        private const float TyreBurstStressThreshold = 42.0f;

        // Heartbeat
        private const string HeartbeatName = "Heartbeat";
        private const string HeartbeatSet = "MP_HEARTBEAT_SOUNDSET";

        // Graphics presets
        private static readonly string[] PresetNames =
        {
            "Default", "Cinematic Filmic", "Filmic Warm", "Cinema Replay", "Desaturated"
        };

        private static readonly string[] PresetModifiers =
        {
            null, "NG_filmic06", "NG_filmic09", "spectator5", "hud_def_desat_Neutral"
        };

        private static readonly Vector3[] GasStations =
        {
            new Vector3(819.4f, -1028.8f, 26.4f),
            new Vector3(258.0f, -1266.7f, 29.3f),
            new Vector3(174.2f, -1562.1f, 29.3f),
            new Vector3(-1249.5f, -290.9f, 37.5f),
            new Vector3(1208.5f, -1387.9f, 35.2f),
            new Vector3(1991.5f, 3774.9f, 32.1f),
            new Vector3(-319.8f, 6083.3f, 31.4f),
            new Vector3(1784.9f, 3330.2f, 41.1f),
            new Vector3(-2095.0f, -320.3f, 13.0f),
            new Vector3(49.4f, 2778.2f, 58.0f),
            new Vector3(-1437.0f, -276.0f, 46.2f)
        };

        private static readonly Vector3 PillboxHospital = new Vector3(298.53f, -584.13f, 43.27f);
        private const int AmbulanceReward = 750;
        private const int MechanicReward = 500;

        #endregion

        #region State

        private readonly Random _rng = new Random();

        // Timing
        private int _lastTickTime;
        private float _dt = 0.016f;

        // Survival
        private float _hunger = MaxHunger;
        private float _thirst = MaxThirst;
        private float _sleep = MaxSleep;
        private int _nextSurvivalTick;
        private int _nextHungerDmg;
        private int _nextThirstDmg;
        private int _nextSleepDmg;
        private int _nextLowWarn;

        // Stamina
        private float _stamina = MaxStamina;
        private bool _exhausted;
        private int _staminaRegenDelay;

        // Persistence
        private string _savePath;
        private int _nextSave;

        // Player damage / injury
        private int _lastHealth;
        private float _lastVz;
        private int _nextRagdollTime;
        private int _legInjuryUntil;
        private int _nextRegenLock;
        private bool _injured;
        private float _lastMoveCap = -1.0f;

        // Camera
        private int _lastHeavyShot;
        private int _lastHeartbeat;
        private int _lastRoadShake;
        private int _lastGForceShake;
        private float _camTilt;
        private float _weaponKick;

        // Visuals
        private string _activeTimecycle;
        private float _lastTcStrength = -1.0f;
        private int _presetIndex;

        // Vehicle physics
        private VehicleState _activeVehicle;
        private readonly Dictionary<int, VehicleState> _vehicleStates = new Dictionary<int, VehicleState>();
        private int _nextVehicleCleanup;

        // Fuel
        private readonly Dictionary<int, VehicleFuelState> _fuelStates = new Dictionary<int, VehicleFuelState>();
        private int _activeFuelHandle = -1;
        private int _nextFuelTick;

        // Ped ragdoll
        private readonly Dictionary<int, PedState> _pedStates = new Dictionary<int, PedState>();
        private int _nextPedScan;
        private int _nextPedCleanup;

        // Jobs
        private ActiveJob _job;
        private int _nextJobTick;

        // Toggle
        private bool _enabled = true;

        // Scratch
        private readonly List<int> _scratch = new List<int>();

        // Classes
        private class VehicleState
        {
            public Vehicle V;
            public int Handle;
            public bool HandlingCaptured;
            public bool HandlingApplied;
            public float OrigMass, OrigDrive, OrigBrake, OrigTracMax, OrigTracMin;
            public float LastSpeed;
            public Vector3 LastVel;
            public float LastGForce;
            public int NextCrashOk;
            public int NextSurfaceScan;
            public int NextHandlingRefresh;
            public bool RoughTerrain;
            public float TyreStress;
            public int NextTyreBurstOk;
            public int NextStallOk;
            public int NextExplosionOk;
            public bool StallActive;
            public int StallUntil;
            public bool WasEngineOn;
            public bool EngineLoweredForStall;
            public float EngineHealthBeforeStall;
        }

        private class VehicleFuelState
        {
            public float Fuel = MaxFuel;
            public bool EngineOffByFuel;
            public float LastSpeed;
        }

        private class PedState
        {
            public int LastHealth;
            public int NextRagdollTime;
            public int LastSeen;
        }

        private enum JobType { None, Ambulance, Mechanic }

        private class ActiveJob
        {
            public JobType Type;
            public int Stage;
            public Vector3 Pickup;
            public Blip PickupBlip;
            public Blip DeliveryBlip;
            public int Deadline;
        }

        #endregion

        public UltimateGTA6VibeSuiteScript()
        {
            try
            {
                string loc = typeof(UltimateGTA6VibeSuiteScript).Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                    _savePath = Path.Combine(Path.GetDirectoryName(loc), "UltimateGTA6VibeSuite.ini");
            }
            catch { _savePath = null; }

            try { LoadSurvival(); } catch { }

            Tick += OnTick;
            Aborted += OnAborted;
            Interval = 0;

            try
            {
                ShowNotification("~b~Ultimate GTA 6 Vibe Suite~s~ loaded successfully.", false);
                ShowNotification("~y~NumPad 9~s~ to toggle the mod ON/OFF.", false);
                ShowNotification("~y~NumPad 1=Eat  2=Drink  3=Refuel  4=Sleep  5=Graphics", false);
                ShowNotification("~y~NumPad 6=Ambulance  7=Mechanic  0=Save Progress", false);
                ShowNotification("~y~HUD:~s~ Orange=Hunger  Blue=Thirst  Purple=Sleep  Green=Fuel", false);
            }
            catch { }
        }

        #region Main Tick

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                int now = Game.GameTime;
                int deltaMs = _lastTickTime == 0 ? 16 : now - _lastTickTime;
                if (deltaMs < 1) deltaMs = 1;
                if (deltaMs > 100) deltaMs = 100;
                _lastTickTime = now;
                _dt = deltaMs / 1000.0f;

                // Always read all keys so state machine stays consistent
                bool k0 = IsKeyJustPressed(VK_N0);
                bool k1 = IsKeyJustPressed(VK_N1);
                bool k2 = IsKeyJustPressed(VK_N2);
                bool k3 = IsKeyJustPressed(VK_N3);
                bool k4 = IsKeyJustPressed(VK_N4);
                bool k5 = IsKeyJustPressed(VK_N5);
                bool k6 = IsKeyJustPressed(VK_N6);
                bool k7 = IsKeyJustPressed(VK_N7);
                bool k8 = IsKeyJustPressed(VK_N8);
                bool k9 = IsKeyJustPressed(VK_N9);

                Ped player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                // Toggle
                if (k9)
                {
                    _enabled = !_enabled;
                    if (_enabled)
                    {
                        ShowNotification("~g~Ultra Realism ENABLED", false);
                    }
                    else
                    {
                        ShowNotification("~r~Ultra Realism DISABLED", false);
                        ClearTimecycle();
                        ResetMovement(player);
                        RestoreAllVehicles(now);
                    }
                }

                if (player.IsDead)
                {
                    HandlePlayerDead(player, now);
                    return;
                }

                if (!_enabled)
                {
                    return;
                }

                // Hotkeys
                if (k1) TryEat(player);
                if (k2) TryDrink(player);
                if (k3) TryRefuelRepair(player);
                if (k4) TrySleep(player);
                if (k5) CyclePreset();
                if (k6) TryStartJob(player, JobType.Ambulance);
                if (k7) TryStartJob(player, JobType.Mechanic);
                if (k0) { SaveSurvival(); ShowNotification("~g~Progress saved.", false); }
                if (k8) ShowStatusReport(player);

                // Systems
                UpdateRegenLock(now);
                UpdateSurvival(player, now);
                UpdateStamina(player, now);
                UpdatePlayerDamage(player, now);
                UpdateMovementPenalty(player, now);
                UpdateVehiclePhysics(player, now);
                UpdateFuel(player, now);
                UpdateNearbyPeds(player, now);
                UpdateCamera(player, now);
                UpdateVisuals(player, now);
                UpdateJob(player, now);
                UpdatePersistence(now);
                DrawHud(player);
                Cleanup(now);
            }
            catch
            {
                // Fail-safe: no crash cascades.
            }
        }

        private void HandlePlayerDead(Ped player, int now)
        {
            try
            {
                ClearTimecycle();
                ResetMovement(player);
                CancelJob(false);
                RestoreAllVehicles(now);

                _stamina = MaxStamina;
                _exhausted = false;
                _injured = false;
                _legInjuryUntil = 0;
                _lastHealth = player.Health;
                _lastVz = 0.0f;
                _lastMoveCap = -1.0f;
                _staminaRegenDelay = 0;
                _camTilt = 0.0f;
                _weaponKick = 0.0f;
                _lastHeartbeat = 0;
            }
            catch { }
        }

        #endregion

        #region Persistence

        private void LoadSurvival()
        {
            if (string.IsNullOrEmpty(_savePath) || !File.Exists(_savePath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(_savePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] p = line.Split('=');
                    if (p.Length != 2) continue;
                    float v;
                    if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                    switch (p[0].Trim())
                    {
                        case "Hunger": _hunger = Clamp(v, 0f, MaxHunger); break;
                        case "Thirst": _thirst = Clamp(v, 0f, MaxThirst); break;
                        case "Sleep": _sleep = Clamp(v, 0f, MaxSleep); break;
                        case "Stamina": _stamina = Clamp(v, 0f, MaxStamina); break;
                    }
                }
            }
            catch { }
        }

        private void SaveSurvival()
        {
            if (string.IsNullOrEmpty(_savePath)) return;
            try
            {
                File.WriteAllText(_savePath,
                    "Hunger=" + _hunger.ToString("F2", CultureInfo.InvariantCulture) + "\n" +
                    "Thirst=" + _thirst.ToString("F2", CultureInfo.InvariantCulture) + "\n" +
                    "Sleep=" + _sleep.ToString("F2", CultureInfo.InvariantCulture) + "\n" +
                    "Stamina=" + _stamina.ToString("F2", CultureInfo.InvariantCulture));
            }
            catch { }
        }

        private void UpdatePersistence(int now)
        {
            if (now < _nextSave) return;
            _nextSave = now + 30000;
            SaveSurvival();
        }

        #endregion

        #region Survival

        private void UpdateSurvival(Ped player, int now)
        {
            if (now < _nextSurvivalTick) return;
            int elapsed = now - _nextSurvivalTick;
            _nextSurvivalTick = now + 1000;
            if (elapsed <= 0 || elapsed > 5000) elapsed = 1000;

            float mins = elapsed / 60000.0f;
            _hunger = Clamp(_hunger - HungerDecayPerMinute * mins, 0f, MaxHunger);
            _thirst = Clamp(_thirst - ThirstDecayPerMinute * mins, 0f, MaxThirst);
            _sleep = Clamp(_sleep - SleepDecayPerMinute * mins, 0f, MaxSleep);

            if (_hunger <= 0f && now >= _nextHungerDmg) { _nextHungerDmg = now + 12000; SafeDamage(player, 1); }
            if (_thirst <= 0f && now >= _nextThirstDmg) { _nextThirstDmg = now + 8000; SafeDamage(player, 1); }
            if (_sleep <= 0f && now >= _nextSleepDmg) { _nextSleepDmg = now + 18000; SafeDamage(player, 1); }

            if (now >= _nextLowWarn)
            {
                _nextLowWarn = now + 25000;
                string warn = "";
                if (_hunger <= 20f) warn += "~o~Hunger low! ~s~";
                if (_thirst <= 20f) warn += "~b~Thirst low! ~s~";
                if (_sleep <= 20f) warn += "~p~Sleep low! ~s~";
                if (warn.Length > 0) ShowNotification(warn, false);
            }
        }

        private static void SafeDamage(Ped p, int amt)
        {
            if (p == null || !p.Exists() || p.IsDead) return;
            int h = p.Health - amt;
            if (h < 1) h = 1;
            p.Health = h;
        }

        private void TryEat(Ped player)
        {
            if (_hunger >= MaxHunger - 1f) { ShowNotification("~y~You are not hungry.", false); return; }
            if (Game.Player.Money < EatCost) { ShowNotification("~r~Need $" + EatCost + " to eat.", false); return; }

            Game.Player.Money -= EatCost;
            _hunger = Clamp(_hunger + EatRestore, 0f, MaxHunger);
            PlaySound("Menu_Accept", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            ShowNotification("~g~Ate food.~s~ Hunger: " + (int)_hunger + "% | -$" + EatCost, false);
        }

        private void TryDrink(Ped player)
        {
            if (_thirst >= MaxThirst - 1f) { ShowNotification("~y~You are not thirsty.", false); return; }
            if (Game.Player.Money < DrinkCost) { ShowNotification("~r~Need $" + DrinkCost + " to drink.", false); return; }

            Game.Player.Money -= DrinkCost;
            _thirst = Clamp(_thirst + DrinkRestore, 0f, MaxThirst);
            PlaySound("Menu_Accept", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            ShowNotification("~g~Drank water.~s~ Thirst: " + (int)_thirst + "% | -$" + DrinkCost, false);
        }

        private void TrySleep(Ped player)
        {
            if (player.IsInCombat)
            {
                ShowNotification("~r~Cannot sleep while in combat.", false);
                return;
            }

            try
            {
                OutputArgument h = new OutputArgument();
                OutputArgument m = new OutputArgument();
                OutputArgument s = new OutputArgument();
                Function.Call(Hash.GET_CLOCK_TIME, h, m, s);
                int hours = (h.GetResult<int>() + 2) % 24;
                Function.Call(Hash.SET_CLOCK_TIME, hours, m.GetResult<int>(), s.GetResult<int>());
            }
            catch { }

            _sleep = MaxSleep;
            _stamina = MaxStamina;
            _exhausted = false;
            ShowNotification("~g~Rested.~s~ Sleep: 100% | Stamina: 100%", false);
        }

        private void ShowStatusReport(Ped player)
        {
            string s =
                "~y~STATUS:~s~ " +
                "Hunger " + (int)_hunger + "% | " +
                "Thirst " + (int)_thirst + "% | " +
                "Sleep " + (int)_sleep + "% | " +
                "Stamina " + (int)_stamina + "%";
            ShowNotification(s, false);
        }

        #endregion

        #region Stamina

        private void UpdateStamina(Ped player, int now)
        {
            bool sprinting = false;
            try
            {
                sprinting = !player.IsInVehicle() &&
                            Function.Call<bool>(Hash.IS_PED_SPRINTING, player) &&
                            player.Velocity.Length() > 2f;
            }
            catch { }

            float effMax = MaxStamina * (0.6f + 0.4f * (_sleep / MaxSleep));

            if (_exhausted)
            {
                _stamina += StaminaRegenPerSecond * 0.75f * _dt;
                if (_stamina >= ExhaustedRecoverThreshold) _exhausted = false;
            }
            else if (sprinting)
            {
                float drainMul = 1.0f;
                if (_hunger < 20f) drainMul += 0.4f;
                if (_thirst < 20f) drainMul += 0.4f;
                if (_sleep < 20f) drainMul += 0.3f;

                _stamina -= SprintDrainPerSecond * drainMul * _dt;
                _staminaRegenDelay = now + 1800;
                if (_stamina <= 0f) { _stamina = 0f; _exhausted = true; }
            }
            else if (now >= _staminaRegenDelay)
            {
                float regenMul = 1.0f;
                if (_hunger < 20f) regenMul *= 0.6f;
                if (_thirst < 20f) regenMul *= 0.6f;
                if (_sleep < 20f) regenMul *= 0.7f;
                _stamina += StaminaRegenPerSecond * regenMul * _dt;
            }

            _stamina = Clamp(_stamina, 0f, effMax);
        }

        #endregion

        #region Player Damage / Ragdoll / Injury

        private void UpdateRegenLock(int now)
        {
            if (now < _nextRegenLock) return;
            _nextRegenLock = now + 1000;
            try
            {
                Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, Game.Player.Handle, 0.0f);
            }
            catch { }
        }

        private void UpdatePlayerDamage(Ped player, int now)
        {
            int curHp = player.Health;
            if (_lastHealth <= 0) { _lastHealth = curHp; _lastVz = player.Velocity.Z; return; }

            int delta = _lastHealth - curHp;

            if (delta > 0)
            {
                if (DetectLegDamage(player)) _legInjuryUntil = Math.Max(_legInjuryUntil, now + 15000);

                float momentum = Math.Max(player.Velocity.Length(), Math.Abs(_lastVz));
                if (delta >= 4 || momentum > 8f)
                    TryRagdoll(player, delta, momentum, now, ref _nextRagdollTime);
            }

            float vz = player.Velocity.Z;
            if (!player.IsInVehicle() && _lastVz < -9f && vz > -2f)
                TryRagdoll(player, 0, Math.Abs(_lastVz), now, ref _nextRagdollTime);

            _lastHealth = curHp;
            _lastVz = vz;
        }

        private bool DetectLegDamage(Ped ped)
        {
            try
            {
                OutputArgument boneArg = new OutputArgument();
                bool hasBone = Function.Call<bool>(Hash.GET_PED_LAST_DAMAGE_BONE, ped, boneArg);
                Function.Call(Hash.CLEAR_PED_LAST_DAMAGE_BONE, ped);
                if (!hasBone) return false;
                int bone = boneArg.GetResult<int>();
                if (bone == 0) return false;

                Vector3 boneWorld = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, ped, bone, 0f, 0f, 0f);
                if (boneWorld.X == 0f && boneWorld.Y == 0f && boneWorld.Z == 0f) return false;

                float relH = boneWorld.Z - ped.Position.Z;
                return relH > -0.1f && relH < 1.0f;
            }
            catch { return false; }
        }

        private void TryRagdoll(Ped ped, int dmg, float mom, int now, ref int next)
        {
            if (ped == null || !ped.Exists() || ped.IsDead || ped.IsInVehicle()) return;
            if (now < next) return;
            try { if (Function.Call<bool>(Hash.IS_PED_RAGDOLL, ped)) return; } catch { }
            if (dmg < 3 && mom < 7f) return;

            int dur = (int)Clamp(800f + dmg * 25f + mom * 45f, 800f, 3500f);
            try
            {
                Function.Call(Hash.SET_PED_TO_RAGDOLL, ped, dur, dur, 0, true, true, false);
                next = now + dur + 300;
            }
            catch { }
        }

        private void UpdateNearbyPeds(Ped player, int now)
        {
            if (now < _nextPedScan) return;
            _nextPedScan = now + 400;

            Ped[] peds = null;
            try { peds = World.GetNearbyPeds(player.Position, 45f); } catch { }
            if (peds == null) return;

            for (int i = 0; i < peds.Length; i++)
            {
                Ped ped = peds[i];
                if (ped == null || !ped.Exists() || ped.IsDead || ped.Handle == player.Handle) continue;
                int h = ped.Handle;
                int hp = ped.Health;

                PedState st;
                if (!_pedStates.TryGetValue(h, out st))
                {
                    st = new PedState { LastHealth = hp, LastSeen = now };
                    _pedStates[h] = st;
                    continue;
                }

                st.LastSeen = now;
                int dmg = st.LastHealth - hp;
                if (dmg > 0 && !ped.IsInVehicle())
                {
                    float mom = ped.Velocity.Length();
                    if (dmg >= 4 || mom > 8f) TryRagdoll(ped, dmg, mom, now, ref st.NextRagdollTime);
                }
                st.LastHealth = hp;
            }
        }

        private void UpdateMovementPenalty(Ped player, int now)
        {
            bool shouldInjure = player.Health <= player.MaxHealth * 0.5f || now < _legInjuryUntil;
            if (shouldInjure != _injured)
            {
                _injured = shouldInjure;
                try
                {
                    if (_injured) Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, player, InjuredClipset, 1.0f);
                    else Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, player, 0.0f);
                }
                catch { }
            }

            bool critical = player.Health <= player.MaxHealth * 0.2f;
            bool starving = _hunger <= 10f;
            bool dehydrated = _thirst <= 10f;
            bool sleepy = _sleep <= 20f;

            float rate = 1.0f;
            if (_injured) rate *= InjuredMovementMul;
            if (_exhausted) rate *= ExhaustedMovementMul;
            if (critical) rate *= CriticalHealthMovementMul;
            if (starving) rate *= StarvingMovementMul;
            if (dehydrated) rate *= DehydratedMovementMul;
            if (sleepy) rate *= SleepyMovementMul;
            if (player.IsInVehicle()) rate = 1.0f;

            try { Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, player, rate); } catch { }

            float cap = 3.0f;
            if (_exhausted) cap = 1.0f;
            else if (_injured) cap = 1.5f;
            else if (critical) cap = 2.0f;
            else if (starving || dehydrated) cap = 2.2f;
            else if (sleepy) cap = 2.5f;

            if (Math.Abs(_lastMoveCap - cap) > 0.01f)
            {
                try
                {
                    Function.Call(Hash.SET_PED_MAX_MOVE_BLEND_RATIO, player, cap);
                    _lastMoveCap = cap;
                }
                catch { }
            }
        }

        #endregion

        #region Vehicle Physics

        private void UpdateVehiclePhysics(Ped player, int now)
        {
            if (!player.IsInVehicle())
            {
                if (_activeVehicle != null)
                {
                    RestoreVehicle(_activeVehicle, now);
                    _vehicleStates.Remove(_activeVehicle.Handle);
                    _activeVehicle = null;
                }
                return;
            }

            Vehicle v = player.CurrentVehicle;
            if (v == null || !v.Exists())
            {
                if (_activeVehicle != null)
                {
                    RestoreVehicle(_activeVehicle, now);
                    _vehicleStates.Remove(_activeVehicle.Handle);
                    _activeVehicle = null;
                }
                return;
            }

            if (_activeVehicle == null || _activeVehicle.Handle != v.Handle)
            {
                if (_activeVehicle != null)
                {
                    RestoreVehicle(_activeVehicle, now);
                    _vehicleStates.Remove(_activeVehicle.Handle);
                }

                VehicleState st;
                if (!_vehicleStates.TryGetValue(v.Handle, out st))
                {
                    st = new VehicleState { V = v, Handle = v.Handle, LastSpeed = v.Speed, LastVel = v.Velocity };
                    CaptureHandling(v, st);
                    _vehicleStates[v.Handle] = st;
                }
                _activeVehicle = st;
                _activeVehicle.NextHandlingRefresh = now + 500;
                ApplyHandling(v, _activeVehicle);
            }

            UpdateVehicleDynamics(v, _activeVehicle, now);
        }

        private void CaptureHandling(Vehicle v, VehicleState st)
        {
            try
            {
                var h = v.HandlingData;
                st.OrigMass = h.Mass;
                st.OrigDrive = h.InitialDriveForce;
                st.OrigBrake = h.BrakeForce;
                st.OrigTracMax = h.TractionCurveMax;
                st.OrigTracMin = h.TractionCurveMin;
                st.HandlingCaptured = true;
            }
            catch { st.HandlingCaptured = false; }
        }

        private void ApplyHandling(Vehicle v, VehicleState st)
        {
            if (v == null || !v.Exists() || st == null || !st.HandlingCaptured) return;
            try
            {
                var h = v.HandlingData;
                float massScale = GetMassScale(st.OrigMass);
                float weather = GetWeatherTraction();
                float rough = st.RoughTerrain ? 0.86f : 1.0f;

                float speed = v.Speed;
                float highSpeedFactor = 1.0f;
                if (speed > 26f)
                    highSpeedFactor = Clamp(1.0f - (speed - 26f) * 0.006f, 0.82f, 1.0f);

                h.Mass = st.OrigMass * massScale;
                h.InitialDriveForce = st.OrigDrive * 0.94f;
                h.BrakeForce = st.OrigBrake * 0.92f;
                h.TractionCurveMax = Math.Max(0.05f, st.OrigTracMax * 1.03f * weather * rough * highSpeedFactor);
                h.TractionCurveMin = Math.Max(0.05f, st.OrigTracMin * 0.95f * weather * rough * highSpeedFactor);
                st.HandlingApplied = true;
            }
            catch { }
        }

        private void RestoreVehicle(VehicleState st, int now)
        {
            if (st == null) return;
            if (st.StallActive) EndStall(st.V, st, now);

            if (st.HandlingApplied && st.HandlingCaptured && st.V != null && st.V.Exists())
            {
                try
                {
                    var h = st.V.HandlingData;
                    h.Mass = st.OrigMass;
                    h.InitialDriveForce = st.OrigDrive;
                    h.BrakeForce = st.OrigBrake;
                    h.TractionCurveMax = st.OrigTracMax;
                    h.TractionCurveMin = st.OrigTracMin;
                }
                catch { }
                st.HandlingApplied = false;
            }
        }

        private void UpdateVehicleDynamics(Vehicle v, VehicleState st, int now)
        {
            if (v == null || !v.Exists() || st == null) return;

            float speed = v.Speed;
            float decel = st.LastSpeed - speed;

            if (st.StallActive && now >= st.StallUntil) EndStall(v, st, now);

            if (now >= st.NextSurfaceScan)
            {
                st.NextSurfaceScan = now + 300;
                bool rough = IsRoughTerrain(v);
                if (rough != st.RoughTerrain) { st.RoughTerrain = rough; st.NextHandlingRefresh = now; }
            }

            if (now >= st.NextHandlingRefresh)
            {
                st.NextHandlingRefresh = now + 750;
                ApplyHandling(v, st);
            }

            // Crash handling
            if (now >= st.NextCrashOk && st.LastSpeed > CrashSpeedThreshold && decel > CrashDecelThreshold)
            {
                st.NextCrashOk = now + 1400;
                HandleCrash(v, st, now, st.LastSpeed, decel);
            }

            // Tyre stress on rough terrain
            if (st.RoughTerrain && speed > OffRoadStressThreshold)
            {
                st.TyreStress += (speed - 14f) * _dt;
                if (st.TyreStress > TyreBurstStressThreshold && now >= st.NextTyreBurstOk)
                {
                    BurstTyre(v, st, now);
                    st.TyreStress = 0f;
                }
            }
            else
            {
                st.TyreStress = Math.Max(0f, st.TyreStress - 6f * _dt);
            }

            // G-force computation
            Vector3 vel = v.Velocity;
            Vector3 right = v.RightVector;
            Vector3 dVel = vel - st.LastVel;
            float invDt = 1.0f / Math.Max(_dt, 0.001f);
            float gForce = (dVel.X * right.X + dVel.Y * right.Y + dVel.Z * right.Z) * invDt;
            st.LastGForce = gForce;
            st.LastVel = vel;
            st.LastSpeed = speed;
        }

        private void HandleCrash(Vehicle v, VehicleState st, int now, float impactSpeed, float decel)
        {
            decel = Clamp(decel, 0f, 80f);

            // Engine stall on hard impacts
            if (impactSpeed > 20f && now >= st.NextStallOk) BeginStall(v, st, now);

            // Apply body/engine damage
            float dmg = (impactSpeed - CrashSpeedThreshold) * 5.5f + decel * 3.5f;

            try
            {
                float eh = Function.Call<float>(Hash.GET_VEHICLE_ENGINE_HEALTH, v);
                float bh = Function.Call<float>(Hash.GET_VEHICLE_BODY_HEALTH, v);

                eh = Clamp(eh - dmg, -4000f, 1000f);
                bh = Clamp(bh - dmg * 0.9f, -4000f, 1000f);

                Function.Call(Hash.SET_VEHICLE_ENGINE_HEALTH, v, eh);
                Function.Call(Hash.SET_VEHICLE_BODY_HEALTH, v, bh);

                // Tank explosion on catastrophic impact with damaged engine
                if (impactSpeed > 30f && eh < 450f && now >= st.NextExplosionOk)
                {
                    st.NextExplosionOk = now + 20000;
                    if (_rng.NextDouble() < 0.28)
                    {
                        Function.Call(Hash.EXPLODE_VEHICLE, v, true, false, 1.0f);
                        return; // stop further processing on explosion
                    }
                }
            }
            catch { }

            // Camera shake
            float shakeIntensity = Clamp((impactSpeed - CrashSpeedThreshold) / 35f, 0.05f, 0.6f);
            try { Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "SMALL_EXPLOSION_SHAKE", shakeIntensity); } catch { }

            // Tyre burst chance
            if (now >= st.NextTyreBurstOk)
            {
                float chance = Clamp((impactSpeed - 18f) / 45f, 0.04f, 0.5f);
                if (_rng.NextDouble() < chance) BurstTyre(v, st, now);
            }
        }

        private void BeginStall(Vehicle v, VehicleState st, int now)
        {
            if (st.StallActive || v == null || !v.Exists()) return;
            st.StallActive = true;
            st.StallUntil = now + 1800 + _rng.Next(0, 1400);
            st.NextStallOk = now + 9000;

            try { st.WasEngineOn = Function.Call<bool>(Hash.GET_IS_VEHICLE_ENGINE_RUNNING, v); }
            catch { st.WasEngineOn = false; }

            try
            {
                float eh = Function.Call<float>(Hash.GET_VEHICLE_ENGINE_HEALTH, v);
                st.EngineHealthBeforeStall = eh;
                st.EngineLoweredForStall = eh > 320f;
                if (st.EngineLoweredForStall)
                    Function.Call(Hash.SET_VEHICLE_ENGINE_HEALTH, v, 320f);
            }
            catch { st.EngineLoweredForStall = false; }

            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, false, true, true); } catch { }
        }

        private void EndStall(Vehicle v, VehicleState st, int now)
        {
            if (!st.StallActive) return;
            st.StallActive = false;
            st.StallUntil = 0;

            if (v == null || !v.Exists()) return;

            try
            {
                if (st.EngineLoweredForStall)
                {
                    float cur = Function.Call<float>(Hash.GET_VEHICLE_ENGINE_HEALTH, v);
                    float restore = Math.Min(1000f, cur + (st.EngineHealthBeforeStall - 320f));
                    Function.Call(Hash.SET_VEHICLE_ENGINE_HEALTH, v, restore);
                }

                if (st.WasEngineOn)
                {
                    float cur = Function.Call<float>(Hash.GET_VEHICLE_ENGINE_HEALTH, v);
                    if (cur > 100f)
                        Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, true, true, false);
                }
            }
            catch { }

            st.EngineLoweredForStall = false;
        }

        private void BurstTyre(Vehicle v, VehicleState st, int now)
        {
            if (v == null || !v.Exists() || st == null) return;
            if (now < st.NextTyreBurstOk) return;

            int wheels = 4;
            try
            {
                int w = Function.Call<int>(Hash.GET_VEHICLE_NUMBER_OF_WHEELS, v);
                if (w > 0 && w <= 10) wheels = w;
            }
            catch { }

            for (int i = 0; i < wheels; i++)
            {
                int idx = _rng.Next(0, wheels);
                bool burst = false;
                try { burst = Function.Call<bool>(Hash.IS_VEHICLE_TYRE_BURST, v, idx, false); } catch { }
                if (burst) continue;

                try
                {
                    Function.Call(Hash.SET_VEHICLE_TYRE_BURST, v, idx, false, 1.0f);
                    st.NextTyreBurstOk = now + 10000 + _rng.Next(0, 8000);
                    st.TyreStress = 0f;
                    return;
                }
                catch { return; }
            }
        }

        private bool IsRoughTerrain(Vehicle v)
        {
            if (v == null || !v.Exists()) return false;
            int wheels = 4;
            try
            {
                int w = Function.Call<int>(Hash.GET_VEHICLE_NUMBER_OF_WHEELS, v);
                if (w > 0 && w <= 10) wheels = w;
            }
            catch { }

            for (int i = 0; i < wheels; i++)
            {
                int mat = -1;
                try { mat = Function.Call<int>(Hash.GET_VEHICLE_WHEEL_SURFACE_MATERIAL, v, i); } catch { continue; }
                if (mat >= 5 && mat <= 20) return true;
            }
            return false;
        }

        private static float GetMassScale(float mass)
        {
            if (mass <= 0f) return 1.0f;
            if (mass < 1100f) return 0.94f;
            if (mass < 1400f) return 1.00f;
            if (mass < 1750f) return 1.06f;
            if (mass < 2200f) return 1.12f;
            return 1.18f;
        }

        private static float GetWeatherTraction()
        {
            float rain = 0f, snow = 0f;
            try { rain = Function.Call<float>(Hash.GET_RAIN_LEVEL); } catch { }
            try { snow = Function.Call<float>(Hash.GET_SNOW_LEVEL); } catch { }
            float f = 1.0f - Clamp(rain, 0f, 1f) * 0.16f - Clamp(snow, 0f, 1f) * 0.26f;
            return Clamp(f, 0.55f, 1.0f);
        }

        #endregion

        #region Fuel System

        private void UpdateFuel(Ped player, int now)
        {
            if (now < _nextFuelTick) return;
            _nextFuelTick = now + 500;

            if (!player.IsInVehicle()) { _activeFuelHandle = -1; return; }

            Vehicle v = player.CurrentVehicle;
            if (v == null || !v.Exists()) { _activeFuelHandle = -1; return; }

            int handle = v.Handle;
            _activeFuelHandle = handle;

            VehicleFuelState fs;
            if (!_fuelStates.TryGetValue(handle, out fs))
            {
                fs = new VehicleFuelState { Fuel = MaxFuel };
                _fuelStates[handle] = fs;
            }

            float speed = v.Speed;

            // Realistic fuel drain (scaled per second, may last 40-90 min of driving)
            float drainPerSecond = 0.002f + speed * 0.0008f;
            fs.Fuel = Clamp(fs.Fuel - drainPerSecond * 0.5f, 0f, MaxFuel);
            fs.LastSpeed = speed;

            // Fuel exhausted → engine permanently disabled
            if (fs.Fuel <= 0f)
            {
                fs.EngineOffByFuel = true;
                try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, false, true, true); } catch { }

                // Show warning only occasionally
                if (_rng.NextDouble() < 0.02)
                    ShowNotification("~r~⚠ OUT OF FUEL", true);
            }
            else if (fs.Fuel > 1f && fs.EngineOffByFuel)
            {
                // Engine was off due to fuel but we have some now (shouldn't happen unless refuelled)
                fs.EngineOffByFuel = false;
            }
        }

        private void TryRefuelRepair(Ped player)
        {
            Vehicle v = null;
            if (player.IsInVehicle())
            {
                v = player.CurrentVehicle;
            }
            else
            {
                Vehicle[] nearby = null;
                try { nearby = World.GetNearbyVehicles(player.Position, 4.5f); } catch { }
                if (nearby != null)
                {
                    for (int i = 0; i < nearby.Length; i++)
                    {
                        if (nearby[i] != null && nearby[i].Exists() && nearby[i].Driver == null)
                        {
                            v = nearby[i];
                            break;
                        }
                    }
                }
            }

            if (v == null || !v.Exists())
            {
                ShowNotification("~r~No vehicle nearby to refuel.", false);
                return;
            }

            bool nearPump = false;
            Vector3 vpos = v.Position;
            for (int i = 0; i < GasStations.Length; i++)
            {
                Vector3 s = GasStations[i];
                float dx = vpos.X - s.X, dy = vpos.Y - s.Y, dz = vpos.Z - s.Z;
                if (dx * dx + dy * dy + dz * dz <= RefuelRangeSq) { nearPump = true; break; }
            }

            if (!nearPump)
            {
                ShowNotification("~r~You must be at a gas station to refuel.", false);
                return;
            }

            VehicleFuelState fs;
            if (!_fuelStates.TryGetValue(v.Handle, out fs))
            {
                fs = new VehicleFuelState { Fuel = MaxFuel };
                _fuelStates[v.Handle] = fs;
            }

            if (fs.Fuel >= MaxFuel - 1f && fs.EngineOffByFuel == false)
            {
                ShowNotification("~y~Tank is already full.", false);
                return;
            }

            if (Game.Player.Money < RefuelCost)
            {
                ShowNotification("~r~Need $" + RefuelCost + " to refuel and repair.", false);
                return;
            }

            Game.Player.Money -= RefuelCost;
            fs.Fuel = MaxFuel;
            fs.EngineOffByFuel = false;

            // Repair engine
            try
            {
                Function.Call(Hash.SET_VEHICLE_ENGINE_HEALTH, v, 1000.0f);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, true, true, false);
            }
            catch { }

            PlaySound("Menu_Accept", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            ShowNotification("~g~Refueled & Repaired.~s~ -$" + RefuelCost, false);
        }

        #endregion

        #region Camera & Heartbeat

        private void UpdateCamera(Ped player, int now)
        {
            float desiredTilt = 0f;

            // Heavy weapon recoil
            try
            {
                if (Function.Call<bool>(Hash.IS_PED_SHOOTING, player) && now - _lastHeavyShot > 140)
                {
                    WeaponHash wh = player.Weapons.Current.Hash;
                    if (IsHeavyWeapon(wh))
                    {
                        _lastHeavyShot = now;
                        Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "HAND_SHAKE", 0.20f);
                        float kick = (float)(_rng.NextDouble() * 1.6 - 0.8);
                        _weaponKick = Clamp(_weaponKick + kick, -2.5f, 2.5f);
                    }
                }
            }
            catch { }

            // G-Force from vehicle
            if (player.IsInVehicle() && _activeVehicle != null)
            {
                Vehicle v = player.CurrentVehicle;
                if (v != null && v.Exists() && v.Handle == _activeVehicle.Handle)
                {
                    float gForceTilt = Clamp(_activeVehicle.LastGForce * 0.04f, -2.2f, 2.2f);
                    desiredTilt += gForceTilt;

                    if (Math.Abs(_activeVehicle.LastGForce) > 7f && now - _lastGForceShake > 150)
                    {
                        _lastGForceShake = now;
                        float inten = Clamp((Math.Abs(_activeVehicle.LastGForce) - 7f) / 30f, 0.02f, 0.12f);
                        try { Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "HAND_SHAKE", inten); } catch { }
                    }

                    float speed = v.Speed;
                    if (_activeVehicle.RoughTerrain && speed > 9f)
                    {
                        if (now - _lastRoadShake > 160)
                        {
                            _lastRoadShake = now;
                            float inten = Clamp((speed - 8f) / 40f, 0.01f, 0.09f);
                            try { Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "ROAD_VIBRATION_SHAKE", inten); } catch { }
                        }
                        desiredTilt += (float)Math.Sin(now / 90.0) * Clamp((speed - 8f) / 50f, 0f, 0.8f);
                    }
                }
            }

            desiredTilt += _weaponKick;
            _weaponKick = Lerp(_weaponKick, 0f, 0.18f);
            if (Math.Abs(_weaponKick) < 0.005f) _weaponKick = 0f;

            _camTilt = Lerp(_camTilt, desiredTilt, 0.16f);
            if (Math.Abs(_camTilt) < 0.0005f) _camTilt = 0f;

            try { Function.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING, _camTilt); } catch { }

            // Heartbeat
            bool critical = player.Health <= player.MaxHealth * 0.2f;
            if (critical)
            {
                float sev = 1f - Clamp(player.Health / (player.MaxHealth * 0.2f), 0f, 1f);
                int interval = (int)Lerp(1100f, 520f, sev);
                if (now - _lastHeartbeat >= interval)
                {
                    _lastHeartbeat = now;
                    try { Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "HAND_SHAKE", 0.10f + sev * 0.14f); } catch { }
                    PlaySound(HeartbeatName, HeartbeatSet);
                }
            }
            else _lastHeartbeat = 0;
        }

        private static bool IsHeavyWeapon(WeaponHash w)
        {
            switch (w)
            {
                case WeaponHash.RPG:
                case WeaponHash.Minigun:
                case WeaponHash.GrenadeLauncher:
                case WeaponHash.HomingLauncher:
                case WeaponHash.Railgun:
                case WeaponHash.SniperRifle:
                case WeaponHash.HeavySniper:
                case WeaponHash.MG:
                case WeaponHash.CombatMG:
                    return true;
                default: return false;
            }
        }

        #endregion

        #region Visuals

        private void CyclePreset()
        {
            _presetIndex = (_presetIndex + 1) % PresetModifiers.Length;
            string name = PresetNames[_presetIndex];
            string mod = PresetModifiers[_presetIndex];

            _activeTimecycle = null;
            _lastTcStrength = -1f;

            try
            {
                Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                if (!string.IsNullOrEmpty(mod))
                {
                    Function.Call(Hash.SET_TIMECYCLE_MODIFIER, mod);
                    Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, 1.0f);
                    _activeTimecycle = mod;
                    _lastTcStrength = 1.0f;
                }
            }
            catch { }

            ShowNotification("~b~Graphics Preset:~s~ " + name, false);
        }

        private void UpdateVisuals(Ped player, int now)
        {
            // If user selected a graphics preset, skip fatigue overlay so we don't fight it
            if (_presetIndex != 0) return;

            bool critical = player.Health <= player.MaxHealth * 0.2f;
            bool fatigue = _exhausted || _stamina <= 15f;
            bool starving = _hunger <= 10f;
            bool dehydrated = _thirst <= 10f;
            bool sleepy = _sleep <= 15f;

            if (!critical && !fatigue && !starving && !dehydrated && !sleepy)
            {
                ClearTimecycle();
                return;
            }

            float pulse = (float)((Math.Sin(now / 160.0) + 1.0) * 0.5);
            float strength = 0f;

            if (fatigue) strength = Math.Max(strength, 0.12f + (1f - _stamina / MaxStamina) * 0.08f);
            if (starving || dehydrated || sleepy) strength = Math.Max(strength, 0.16f);
            if (critical)
            {
                float sev = 1f - Clamp(player.Health / (player.MaxHealth * 0.2f), 0f, 1f);
                strength = Math.Max(strength, 0.18f + sev * 0.12f + pulse * 0.08f);
            }

            SetTimecycle(FatigueTimecycle, Clamp(strength, 0.05f, 0.45f));
        }

        private void SetTimecycle(string name, float strength)
        {
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                if (_activeTimecycle != name)
                {
                    if (!string.IsNullOrEmpty(_activeTimecycle)) Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                    Function.Call(Hash.SET_TIMECYCLE_MODIFIER, name);
                    _activeTimecycle = name;
                    _lastTcStrength = -1f;
                }

                if (Math.Abs(_lastTcStrength - strength) > 0.01f)
                {
                    Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, strength);
                    _lastTcStrength = strength;
                }
            }
            catch { }
        }

        private void ClearTimecycle()
        {
            if (string.IsNullOrEmpty(_activeTimecycle)) return;
            try { Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER); } catch { }
            _activeTimecycle = null;
            _lastTcStrength = -1f;
        }

        #endregion

        #region HUD

        private void DrawHud(Ped player)
        {
            if (!_enabled || player == null || !player.Exists() || player.IsDead) return;

            try
            {
                const float barX = 0.08f;
                const float barW = 0.12f;
                const float barH = 0.008f;
                const float yStart = 0.40f;
                const float ySpacing = 0.012f;

                DrawBar(barX, yStart, barW, barH, _hunger / MaxHunger, 255, 165, 0);
                DrawBar(barX, yStart + ySpacing, barW, barH, _thirst / MaxThirst, 50, 150, 255);
                DrawBar(barX, yStart + ySpacing * 2, barW, barH, _sleep / MaxSleep, 180, 100, 255);

                if (player.IsInVehicle())
                {
                    Vehicle v = player.CurrentVehicle;
                    if (v != null && v.Exists())
                    {
                        VehicleFuelState fs;
                        if (_fuelStates.TryGetValue(v.Handle, out fs))
                        {
                            float ratio = fs.Fuel / MaxFuel;
                            int r = ratio < 0.15f ? 255 : 100;
                            int g = ratio < 0.15f ? 60 : 255;
                            DrawBar(barX, yStart + ySpacing * 3, barW, barH, ratio, r, g, 100);
                        }
                    }
                }
            }
            catch { }
        }

        private void DrawBar(float xCenter, float yCenter, float width, float height, float fill, int r, int g, int b)
        {
            try
            {
                Function.Call(Hash.DRAW_RECT, xCenter, yCenter, width, height, 0, 0, 0, 140, false);

                float clampedFill = Clamp(fill, 0f, 1f);
                if (clampedFill <= 0f) return;

                float fillW = width * clampedFill;
                float fillX = xCenter - width * 0.5f + fillW * 0.5f;
                Function.Call(Hash.DRAW_RECT, fillX, yCenter, fillW, height * 0.7f, r, g, b, 220, false);
            }
            catch { }
        }

        #endregion

        #region Jobs

        private void TryStartJob(Ped player, JobType type)
        {
            if (_job != null) { ShowNotification("~r~Finish or cancel your current job first.", false); return; }

            Vector3 pos = GenerateJobTarget(player.Position);
            if (pos == Vector3.Zero) { ShowNotification("~r~Could not find a job location.", false); return; }

            ActiveJob job = new ActiveJob
            {
                Type = type,
                Stage = 0,
                Pickup = pos,
                Deadline = Game.GameTime + 240000
            };

            try
            {
                job.PickupBlip = World.CreateBlip(pos);
                job.PickupBlip.Sprite = type == JobType.Ambulance ? BlipSprite.Health : BlipSprite.Repair;
                job.PickupBlip.Color = type == JobType.Ambulance ? BlipColor.Green : BlipColor.Yellow;
                job.PickupBlip.IsRouteEnabled = true;
                job.PickupBlip.Name = type == JobType.Ambulance ? "Patient" : "Broken Vehicle";
            }
            catch { job.PickupBlip = null; }

            _job = job;

            ShowNotification(
                type == JobType.Ambulance
                    ? "~g~Ambulance Job:~s~ Head to the patient marker."
                    : "~y~Mechanic Job:~s~ Drive to the broken vehicle marker.",
                false
            );
        }

        private Vector3 GenerateJobTarget(Vector3 origin)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    double angle = _rng.NextDouble() * Math.PI * 2.0;
                    double dist = 150.0 + _rng.NextDouble() * 150.0;
                    float x = origin.X + (float)(Math.Cos(angle) * dist);
                    float y = origin.Y + (float)(Math.Sin(angle) * dist);

                    Vector3 candidate = World.GetNextPositionOnStreet(new Vector3(x, y, origin.Z));
                    if (candidate != Vector3.Zero && origin.DistanceTo(candidate) > 100f)
                        return candidate;
                }
                catch { }
            }
            return Vector3.Zero;
        }

        private void UpdateJob(Ped player, int now)
        {
            if (_job == null) return;
            if (now < _nextJobTick) return;
            _nextJobTick = now + 400;

            if (now > _job.Deadline)
            {
                ShowNotification("~r~Job timed out.", false);
                CancelJob(true);
                return;
            }

            float dist = player.Position.DistanceTo(_job.Pickup);

            if (_job.Stage == 0)
            {
                if (dist <= 10f)
                {
                    _job.Stage = 1;
                    _job.Deadline = now + 60000;
                    ShowNotification(
                        _job.Type == JobType.Ambulance
                            ? "~g~Patient located.~s~ Press NumPad 6 to treat."
                            : "~y~Vehicle located.~s~ Press NumPad 7 to repair.",
                        false
                    );

                    try { if (_job.PickupBlip != null && _job.PickupBlip.Exists()) _job.PickupBlip.Delete(); } catch { }
                }
            }
            else if (_job.Stage == 1)
            {
                bool interact = IsKeyJustPressed(_job.Type == JobType.Ambulance ? VK_N6 : VK_N7);
                if (interact && dist <= 12f)
                {
                    // Delivered to appropriate target
                    Vector3 dest = _job.Type == JobType.Ambulance ? PillboxHospital : PillboxHospital;
                    ShowNotification(
                        _job.Type == JobType.Ambulance
                            ? "~g~Patient treated.~s~ Deliver to Pillbox Hospital."
                            : "~y~Vehicle repaired.~s~ Deliver to depot.",
                        false
                    );

                    try
                    {
                        _job.DeliveryBlip = World.CreateBlip(dest);
                        _job.DeliveryBlip.Sprite = BlipSprite.Health;
                        _job.DeliveryBlip.Color = BlipColor.Blue;
                        _job.DeliveryBlip.IsRouteEnabled = true;
                        _job.DeliveryBlip.Name = "Delivery";
                    }
                    catch { }

                    _job.Stage = 2;
                    _job.Deadline = now + 240000;
                }
            }
            else if (_job.Stage == 2)
            {
                float deliveryDist = player.Position.DistanceTo(PillboxHospital);
                if (deliveryDist <= 14f)
                {
                    int reward = _job.Type == JobType.Ambulance ? AmbulanceReward : MechanicReward;
                    Game.Player.Money += reward;
                    PlaySound("Mission_Pass_Notify", "DLC_HEISTS_GENERAL_FRONTEND_SOUNDS");
                    ShowNotification("~g~Job Complete!~s~ +$" + reward, true);
                    CancelJob(false);
                }
            }
        }

        private void CancelJob(bool notify)
        {
            if (_job == null) return;
            try { if (_job.PickupBlip != null && _job.PickupBlip.Exists()) _job.PickupBlip.Delete(); } catch { }
            try { if (_job.DeliveryBlip != null && _job.DeliveryBlip.Exists()) _job.DeliveryBlip.Delete(); } catch { }
            _job = null;
            if (notify) ShowNotification("~y~Job cancelled.", false);
        }

        #endregion

        #region Cleanup / Abort

        private void Cleanup(int now)
        {
            if (now >= _nextPedCleanup)
            {
                _nextPedCleanup = now + 5000;
                _scratch.Clear();
                foreach (var kv in _pedStates)
                    if (now - kv.Value.LastSeen > 15000) _scratch.Add(kv.Key);
                for (int i = 0; i < _scratch.Count; i++) _pedStates.Remove(_scratch[i]);
                _scratch.Clear();
            }

            if (now >= _nextVehicleCleanup)
            {
                _nextVehicleCleanup = now + 5000;
                _scratch.Clear();

                foreach (var kv in _vehicleStates)
                {
                    if (_activeVehicle != null && kv.Key == _activeVehicle.Handle) continue;
                    Vehicle v = kv.Value.V;
                    if (v == null || !v.Exists() || v.Health <= 0)
                        _scratch.Add(kv.Key);
                }

                for (int i = 0; i < _scratch.Count; i++)
                {
                    VehicleState st;
                    if (_vehicleStates.TryGetValue(_scratch[i], out st))
                    {
                        RestoreVehicle(st, now);
                        _vehicleStates.Remove(_scratch[i]);
                    }
                }
                _scratch.Clear();
            }
        }

        private void RestoreAllVehicles(int now)
        {
            if (_vehicleStates.Count == 0) { _activeVehicle = null; return; }
            _scratch.Clear();
            foreach (var kv in _vehicleStates) _scratch.Add(kv.Key);
            for (int i = 0; i < _scratch.Count; i++)
            {
                VehicleState st;
                if (_vehicleStates.TryGetValue(_scratch[i], out st))
                {
                    RestoreVehicle(st, now);
                    _vehicleStates.Remove(_scratch[i]);
                }
            }
            _scratch.Clear();
            _activeVehicle = null;
        }

        private void ResetMovement(Ped player)
        {
            if (player == null || !player.Exists()) return;
            try
            {
                Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, player, 0.0f);
                Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, player, 1.0f);
                Function.Call(Hash.SET_PED_MAX_MOVE_BLEND_RATIO, player, 3.0f);
                Function.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING, 0.0f);
            }
            catch { }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            try
            {
                CancelJob(false);
                ClearTimecycle();
                ResetMovement(Game.Player.Character);
                RestoreAllVehicles(Game.GameTime);
                try { Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, Game.Player.Handle, 1.0f); } catch { }
                SaveSurvival();
            }
            catch { }
        }

        #endregion

        #region Helpers

        private void ShowNotification(string msg, bool blink)
        {
            try { GTA.UI.Notification.Show(msg, blink); } catch { }
        }

        private void PlaySound(string name, string set)
        {
            try { Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, name, set, true); } catch { }
        }

        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static float Lerp(float from, float to, float amount)
        {
            return from + (to - from) * amount;
        }

        #endregion
    }
}
```
