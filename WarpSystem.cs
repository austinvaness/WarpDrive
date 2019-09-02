using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.World.Generator;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace WarpDriveMod
{
    public class WarpSystem
    {
        public bool Valid => grid.Valid;
        public int InvalidOn => grid.InvalidOn;
        public int Id { get; private set; }
        public State WarpState { get; private set; }
        public event Action<WarpSystem> onSystemInvalidated;

        private GridSystem grid;
        private MatrixD gridMatrix;
        private Dictionary<IMyCubeGrid, HashSet<WarpDrive>> warpDrives = new Dictionary<IMyCubeGrid, HashSet<WarpDrive>>();
        private MyParticleEffect effect;
        private double currentSpeedPt = WarpConstants.startSpeed;
        private MyEntity3DSoundEmitter sound;
        private int startChargeRuntime = -1;
        private bool hasEnoughPower = true;
        private int functionalDrives;
        private IMyCubeGrid startWarpSource;
        private float totalHeat = 0;

        public WarpSystem (WarpDrive block, WarpSystem oldSystem)
        {
            Id = WarpDriveSession.Instance.Rand.Next(int.MinValue, int.MaxValue);

            grid = new GridSystem((MyCubeGrid)block.Block.CubeGrid);

            GridSystem.BlockCounter warpDriveCounter = new GridSystem.BlockCounter((b) => b?.GameLogic.GetAs<WarpDrive>() != null);
            warpDriveCounter.onBlockAdded += OnDriveAdded;
            warpDriveCounter.onBlockRemoved += OnDriveRemoved;
            grid.AddCounter("WarpDrives", warpDriveCounter);

            grid.onSystemInvalidated += OnSystemInvalidated;

            sound = new MyEntity3DSoundEmitter(grid.MainGrid);
            sound.CanPlayLoopSounds = true;

            if (oldSystem != null)
            {
                totalHeat = oldSystem.totalHeat;
                startWarpSource = oldSystem.startWarpSource;
                if (startWarpSource?.MarkedForClose == true)
                    startWarpSource = null;
                WarpState = oldSystem.WarpState;
                if (WarpState == State.Charging)
                {
                    PlayParticleEffect();
                    startChargeRuntime = oldSystem.startChargeRuntime;
                    WarpState = State.Charging;
                }
                else if (WarpState == State.Active)
                {
                    currentSpeedPt = oldSystem.currentSpeedPt;
                    WarpState = State.Active;
                }
            }
            block.SetWarpSystem(this);
        }

        public void UpdateBeforeSimulation ()
        {
            if (WarpDriveSession.Instance == null)
                return;

            if (warpDrives.Count == 0)
                grid.Invalidate();


            UpdateHeatPower();

            gridMatrix = grid.FindWorldMatrix();

            if (WarpState == State.Charging)
                InCharge();

            if (WarpState == State.Active)
                InWarp();
        }

        private void InWarp ()
        {
            if (IsInGravity())
            {
                SendMessage(WarpConstants.warnDestablalized);
                Dewarp();
                return;
            }

            if(!hasEnoughPower)
            {
                SendMessage(WarpConstants.warnNoPower);
                Dewarp();
                return;
            }

            if(functionalDrives == 0)
            {
                SendMessage(WarpConstants.warnDamaged);
                Dewarp();
                return;
            }

            if(totalHeat >= WarpConstants.maxHeat)
            {
                SendMessage(WarpConstants.warnOverheat);
                Dewarp();
                return;
            }

            if (!grid.IsStatic)
                SetStatic(true);

            if (currentSpeedPt < WarpConstants.maxSpeed)
                currentSpeedPt += WarpConstants.warpAccel;
            if (currentSpeedPt > WarpConstants.maxSpeed)
                currentSpeedPt = WarpConstants.maxSpeed;

            sound.SetPosition(grid.MainGrid.PositionComp.GetPosition());

            gridMatrix.Translation += gridMatrix.Forward * currentSpeedPt;
            grid.MainGrid.Teleport(gridMatrix);
        }

        private void SetStatic (bool isStatic)
        {
            foreach(MyCubeGrid g in grid.Grids)
            {
                if(g.Physics != null && g.Physics.Enabled)
                {
                    if (isStatic)
                    {
                        g.Physics.ClearSpeed();
                        g.ConvertToStatic();
                    }
                    else
                    {
                        g.OnConvertToDynamic();
                    }
                }
            }
        }

        public void ToggleWarp (IMyCubeGrid source)
        {
            if (!hasEnoughPower)
                return;

            if (WarpState == State.Idle)
            {
                StartCharging();
                startWarpSource = source;
            }
            else
            {
                Dewarp();
            }
        }

        public bool Contains (WarpDrive drive)
        {
            return grid.Contains((MyCubeGrid)drive.Block.CubeGrid);
        }

        private void StartCharging ()
        {
            if (IsInGravity())
            {
                SendMessage(WarpConstants.warnNoEstablish);
                WarpState = State.Idle;
                return;
            }

            if (!grid.IsStatic)
            {
                sound.PlaySound(WarpConstants.jumpInSound, true);
                WarpState = State.Charging;
                startChargeRuntime = WarpDriveSession.Instance.Runtime;

                sound.PlaySound(WarpConstants.chargingSound, true);
                sound.VolumeMultiplier = 1;

                PlayParticleEffect();
            }
            else
            {
                SendMessage(WarpConstants.warnStatic);
            }
        }

        private void StartWarp ()
        {
            if (IsInGravity())
            {
                SendMessage(WarpConstants.warnNoEstablish);
                return;
            }

            if (grid.IsStatic)
            {
                SendMessage(WarpConstants.warnStatic);
                return;
            }

            if (effect != null)
                StopParticleEffect();

            sound.PlaySound(WarpConstants.inWarpSound, true);
            sound.VolumeMultiplier = 5;

            WarpState = State.Active;

            Vector3D? currentVelocity = grid.MainGrid?.Physics?.LinearVelocity;
            if(currentVelocity.HasValue)
            {
                double dot = Vector3D.Dot(currentVelocity.Value, gridMatrix.Forward);
                if (double.IsNaN(dot) || gridMatrix == MatrixD.Zero)
                    dot = 0;
                currentSpeedPt = MathHelper.Clamp(dot, WarpConstants.startSpeed, WarpConstants.maxSpeed);
            }
            else
            {
                currentSpeedPt = WarpConstants.startSpeed;
            }

            SetStatic(true);
        }

        private void Dewarp ()
        {
            if (WarpState == State.Active)
            {
                sound.PlaySound(WarpConstants.jumpOutSound, true);
                SetStatic(false);
            }
            else
            {
                sound.StopSound(false);
            }
            sound.VolumeMultiplier = 1;

            WarpState = State.Idle;

            StopParticleEffect();
        }

        private void InCharge ()
        {
            if (functionalDrives == 0)
            {
                SendMessage(WarpConstants.warnDamaged);
                Dewarp();
                return;
            }

            if(!hasEnoughPower)
            {
                SendMessage(WarpConstants.warnNoPower);
                Dewarp();
                return;
            }

            if (IsInGravity())
            {
                SendMessage(WarpConstants.warnNoEstablish);
                Dewarp();
                return;
            }

            if(grid.IsStatic)
            {
                SendMessage(WarpConstants.warnStatic);
                Dewarp();
                return;
            }

            if(effect != null)
                effect.WorldMatrix = MatrixD.CreateWorld(effect.WorldMatrix.Translation, -gridMatrix.Forward, gridMatrix.Up);
            UpdateParticleEffect();

            if (Math.Abs(WarpDriveSession.Instance.Runtime - startChargeRuntime) >= WarpConstants.chargeTicks)
            {
                StartWarp();
            }
        }

        bool IsInGravity ()
        {
            Vector3D position = grid.MainGrid.PositionComp.GetPosition();
            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(position);
            if (planet == null)
                return false;

            MyGravityProviderComponent gravComp = planet.Components.Get<MyGravityProviderComponent>();
            if (gravComp == null)
                return false;

            return gravComp.GetGravityMultiplier(position) > 0;
        }

        void FreezeGrid ()
        {
            foreach (MyCubeGrid grid in grid.Grids)
                grid.Physics?.ClearSpeed();
        }

        private void UpdateHeatPower ()
        {
            float totalPower = 0;
            if (WarpState == State.Charging)
                totalPower = WarpConstants.baseRequiredPower;
            if (WarpState == State.Active)
            {
                float percent = (float)(1 + currentSpeedPt / WarpConstants.maxSpeed * WarpConstants.powerRequirementMultiplier);
                totalPower = WarpConstants.baseRequiredPower * percent;
                SendMessage($"Speed: {currentSpeedPt * 60 / 1000:0} km/s", 1f / 60, "White");
            }

            if (warpDrives.Count == 0)
                return;

            HashSet<WarpDrive> controllingDrives;
            if (startWarpSource == null || !warpDrives.TryGetValue(startWarpSource, out controllingDrives))
            {
                if (grid.MainGrid == null || !warpDrives.TryGetValue(grid.MainGrid, out controllingDrives))
                    controllingDrives = warpDrives.FirstPair().Value;
            }

            int radiators = 0;
            hasEnoughPower = true;
            int temp = 0;
            foreach(WarpDrive drive in controllingDrives)
            {
                if (drive.Block.IsFunctional && drive.Block.IsWorking)
                {
                    if (functionalDrives == 0)
                    {
                        // First tick
                        drive.RequiredPower = totalPower / controllingDrives.Count;
                    }
                    else
                    {
                        // later ticks
                        if (!drive.HasPower)
                            hasEnoughPower = false;
                        drive.RequiredPower = totalPower / functionalDrives;
                    }
                    temp++;
                    radiators += drive.Radiators;
                }
                else
                {
                    drive.RequiredPower = 0;
                }
            }
            functionalDrives = temp;
            totalHeat -= (WarpConstants.heatDissipationDrive * temp + WarpConstants.heatDissapationRadiator * radiators);
            if (WarpState == State.Active)
                totalHeat += WarpConstants.heatGain;
            if(totalHeat <= 0)
            {
                totalHeat = 0;
                //SendMessage($"Heat: 0% ({radiators})", 1f / 60, "White");
            }
            else if(WarpState == State.Active || totalHeat > 0)
            {
                int percentHeat = (int)(totalHeat / WarpConstants.maxHeat * 100);
                string display = $"Heat: {percentHeat}%";
                string font = "White";
                if (percentHeat >= 65)
                    font = "Red";
                if (percentHeat >= 75)
                    display += '!';
                if (percentHeat >= 85)
                    display += '!';
                SendMessage(display, 1f / 60, font);
            }

        }

        private void PlayParticleEffect ()
        {
            if (effect != null)
            {
                effect.Play();
                return;
            }

            Vector3D forward = gridMatrix.Forward;
            MatrixD fromDir = MatrixD.CreateFromDir(-forward);
            Vector3D effectOffset = forward * grid.MainGrid.PositionComp.WorldAABB.HalfExtents.AbsMax() * 2.0;
            fromDir.Translation = grid.MainGrid.PositionComp.WorldAABB.Center + effectOffset;
            MyParticlesManager.TryCreateParticleEffect("Warp", fromDir, out effect);
        }

        private void UpdateParticleEffect ()
        {
            if (effect == null || effect.IsStopped)
                return;
            Vector3D forward = gridMatrix.Forward;
            Vector3D effectOffset = forward * grid.MainGrid.PositionComp.WorldAABB.HalfExtents.AbsMax() * 2.0;
            effect.SetTranslation(grid.MainGrid.PositionComp.WorldAABB.Center + effectOffset);
        }

        private void StopParticleEffect ()
        {
            if (effect == null)
                return;

            effect.StopEmitting(10f);
            effect = null;
        }

        private void OnSystemInvalidated (GridSystem system)
        {
            SetStatic(false);
            sound?.StopSound(true);
            effect?.Stop();
            onSystemInvalidated?.Invoke(this);
            onSystemInvalidated = null;
        }

        private void SendMessage (string msg, float seconds = 5, string font = "Red")
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;
            IMyPlayer player = MyAPIGateway.Session.Player;
            IMyShipController cockpit = player?.Character?.Parent as IMyShipController;
            if (cockpit?.CubeGrid == null || !grid.Contains((MyCubeGrid)cockpit.CubeGrid))
                return;
            MyVisualScriptLogicProvider.ShowNotification(msg, (int)(seconds * 1000), font, player.IdentityId);
        }

        private void OnDriveAdded (IMyCubeBlock block)
        {
            WarpDrive drive = block.GameLogic.GetAs<WarpDrive>();
            drive.SetWarpSystem(this);

            HashSet<WarpDrive> gridDrives;
            if(!warpDrives.TryGetValue(block.CubeGrid, out gridDrives))
                gridDrives = new HashSet<WarpDrive>();
            gridDrives.Add(drive);
            warpDrives [block.CubeGrid] = gridDrives;
        }

        private void OnDriveRemoved (IMyCubeBlock block)
        {
            WarpDrive drive = block.GameLogic.GetAs<WarpDrive>();

            HashSet<WarpDrive> gridDrives;
            if (warpDrives.TryGetValue(block.CubeGrid, out gridDrives))
            {
                gridDrives.Remove(drive);
                if (gridDrives.Count > 0)
                    warpDrives [block.CubeGrid] = gridDrives;
                else
                    warpDrives.Remove(block.CubeGrid);
            }
        }

        public override bool Equals (object obj)
        {
            var system = obj as WarpSystem;
            return system != null &&
                   Id == system.Id;
        }

        public override int GetHashCode ()
        {
            return 2108858624 + Id.GetHashCode();
        }

        public enum State
        {
            Idle, Charging, Active
        }
    }

}
