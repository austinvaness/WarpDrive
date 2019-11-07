using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.World.Generator;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
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
        public event Action<WarpSystem> OnSystemInvalidated;
        public float HeatPercent => GetHeatPercent();
        public bool Safety = true; // TODO: Safe this to storage

        private List<IMyPlayer> gridPlayers;
        private readonly GridSystem grid;
        private MatrixD gridMatrix;
        private readonly Dictionary<IMyCubeGrid, HashSet<WarpDrive>> warpDrives = new Dictionary<IMyCubeGrid, HashSet<WarpDrive>>();
        private double currentSpeedPt = WarpConstants.startSpeed;
        private int startChargeRuntime = -1;
        private bool hasEnoughPower = true;
        private int functionalDrives;
        private IMyCubeGrid startWarpSource;
        private float totalHeat = 0;
        private BoundingSphereD warpBubble;

        private float GetHeatPercent()
        {
            return Math.Max(Math.Min(totalHeat / WarpConstants.maxHeat, 1), 0);
        }

        public WarpSystem (WarpDrive block, WarpSystem oldSystem)
        {
            Id = WarpDriveSession.Instance.Rand.Next(int.MinValue, int.MaxValue);

            grid = new GridSystem((MyCubeGrid)block.Block.CubeGrid);

            GridSystem.BlockCounter warpDriveCounter = new GridSystem.BlockCounter((b) => b?.GameLogic.GetAs<WarpDrive>() != null);
            warpDriveCounter.OnBlockAdded += OnDriveAdded;
            warpDriveCounter.OnBlockRemoved += OnDriveRemoved;
            grid.AddCounter("WarpDrives", warpDriveCounter);

            grid.OnSystemInvalidated += InvalidateSystem;

            if (oldSystem != null)
            {
                totalHeat = oldSystem.totalHeat;
                startWarpSource = oldSystem.startWarpSource;
                if (startWarpSource?.MarkedForClose == true)
                    startWarpSource = null;
                WarpState = oldSystem.WarpState;
                if (WarpState == State.Charging)
                {
                    UpdateEffect(WarpDriveSession.WarpEffectUpdate.State.Charging);
                    startChargeRuntime = oldSystem.startChargeRuntime;
                    WarpState = State.Charging;
                }
                else if (WarpState == State.Active)
                {
                    //UpdateEffect(WarpDriveSession.WarpEffectUpdate.State.InWarp);
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

        #region WarpStates
        private void StartCharging ()
        {
            grid.GetFreePlayers(ref gridPlayers);
            if (Safety && gridPlayers.Count == 0)
            {
                WarpState = State.Idle;
                return;
            }

            if (IsInGravity())
            {
                SendMessage(WarpConstants.warnNoEstablish);
                WarpState = State.Idle;
                return;
            }

            if (!grid.IsStatic && !grid.IsInVoxels())
            {
                WarpState = State.Charging;
                startChargeRuntime = WarpDriveSession.Instance.Runtime;

                UpdateEffect(WarpDriveSession.WarpEffectUpdate.State.Charging);
            }
            else
            {
                SendMessage(WarpConstants.warnStatic);
            }
        }

        private void InCharge ()
        {
            grid.GetFreePlayers(ref gridPlayers);
            warpBubble = grid.WorldVolume();
            warpBubble.Radius += WarpConstants.warpBubbleBuffer;
            if (gridPlayers.Count == 0 && Safety)
                return;

            if (functionalDrives == 0)
            {
                Dewarp(WarpConstants.warnDamaged);
                return;
            }

            if (!hasEnoughPower)
            {
                Dewarp(WarpConstants.warnNoPower);
                return;
            }

            if (IsInGravity())
            {
                Dewarp(WarpConstants.warnNoEstablish);
                return;
            }

            if (grid.IsStatic)
            {
                Dewarp(WarpConstants.warnStatic);
                return;
            }

            int remaining = WarpConstants.chargeTicks - Math.Abs(WarpDriveSession.Instance.Runtime - startChargeRuntime);
            if (Math.Max(remaining, 0) % 60 == 0)
                SendMessage("Warp drive start in " + (remaining / 60), 0.99f, "White");
            if (remaining <= 0)
                StartWarp();
        }

        private void StartWarp ()
        {
            grid.GetFreePlayers(ref gridPlayers);
            warpBubble = grid.WorldVolume();
            warpBubble.Radius += WarpConstants.warpBubbleBuffer;
            if (gridPlayers.Count == 0 && Safety)
                return;

            if (IsInGravity())
            {
                SendMessage(WarpConstants.warnNoEstablish);
                return;
            }

            if (grid.IsStatic || grid.IsInVoxels())
            {
                SendMessage(WarpConstants.warnStatic);
                return;
            }

            UpdateEffect(WarpDriveSession.WarpEffectUpdate.State.InWarp);

            WarpState = State.Active;

            Vector3D? currentVelocity = grid.MainGrid?.Physics?.LinearVelocity;
            if (currentVelocity.HasValue)
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

        private void InWarp ()
        {
            if (gridPlayers == null)
                grid.GetFreePlayers(ref gridPlayers);

            if (IsInGravity())
            {
                Dewarp(WarpConstants.warnDestablalized);
                return;
            }

            if(!hasEnoughPower)
            {
                Dewarp(WarpConstants.warnNoPower);
                return;
            }

            if(functionalDrives == 0)
            {
                Dewarp(WarpConstants.warnDamaged);
                return;
            }

            if(totalHeat >= WarpConstants.maxHeat)
            {
                Dewarp(WarpConstants.warnOverheat);
                return;
            }

            if (!grid.IsStatic)
                SetStatic(true);

            if (currentSpeedPt < WarpConstants.maxSpeed)
                currentSpeedPt += WarpConstants.warpAccel;
            if (currentSpeedPt > WarpConstants.maxSpeed)
                currentSpeedPt = WarpConstants.maxSpeed;
            
            Vector3D translate = gridMatrix.Forward * currentSpeedPt;
            //MyAPIGateway.Utilities.ShowNotification($"{warpBubble.Radius:0.00}m", 16);
            if (UpdateGridPlayers(translate) && Safety)
                return;


            gridMatrix.Translation += translate;
            warpBubble.Center += translate;
            grid.MainGrid.Teleport(gridMatrix);
            //DrawAllLines();
        }

        private void Dewarp ()
        {
            if (WarpState == State.Active)
            {
                UpdateEffect(WarpDriveSession.WarpEffectUpdate.State.StopWarp);
                SetStatic(false);
            }
            else
            {
                UpdateEffect(WarpDriveSession.WarpEffectUpdate.State.SoftStop);
            }

            WarpState = State.Idle;

        }

        private void Dewarp (string warning)
        {
            Dewarp();
            SendMessage(warning);
        }
        #endregion

        private void Debug(string s)
        {
            MyAPIGateway.Utilities.ShowNotification(s);
        }

        private bool UpdateGridPlayers(Vector3D translate = new Vector3D())
        {
            for (int i = gridPlayers.Count - 1; i >= 0; i--)
            {
                IMyPlayer p = gridPlayers [i];
                if (warpBubble.Contains(p.GetPosition()) == ContainmentType.Disjoint)
                {
                    if (gridPlayers.Count == 1)
                    {
                        if(Safety)
                            Dewarp(WarpConstants.warnSafety);
                        gridPlayers.RemoveAtFast(i);
                        return true;
                    }
                    else
                    {
                        MyVisualScriptLogicProvider.ShowNotification(WarpConstants.warnExitBubble, (int)(WarpConstants.warnDefaultTime * 1000), WarpConstants.warnDefaultFont, p.IdentityId);
                        gridPlayers.RemoveAtFast(i);
                    }
                }
                else if (translate != Vector3D.Zero && p.Character?.Physics != null)
                {
                    p.Character.SetPosition(p.Character.GetPosition() + translate);
                }
            }
            return gridPlayers.Count == 0;
        }

        private void DrawAllLines()
        {
            // Debug line
            {
                //MatrixD direction = MatrixD.CreateWorld(Vector3D.Zero, Vector3D.Forward, Vector3D.Up);
                Vector4 color = Color.OrangeRed;
                //MySimpleObjectDraw.DrawTransparentBox(ref direction, ref warpBubble, ref color, MySimpleObjectRasterizer.Solid, 1, lineWidth: 0.5f);

                Vector3D start = warpBubble.Center;
                Vector3D end = (Vector3D.Normalize(MyAPIGateway.Session.Player.GetPosition() - start) * warpBubble.Radius) + start;
                MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("Square"), ref color, 1);
            }



            double r = warpBubble.Radius;//Math.Max(GetRadius() + 5, 5);
            Vector3D pos = grid.MainGrid.Physics.CenterOfMassWorld;
            Vector3D centerStart = pos - (gridMatrix.Forward * r);
            Vector3D centerEnd = pos + (gridMatrix.Forward * r);
            DrawLine(centerStart + (gridMatrix.Left * r), centerEnd + (gridMatrix.Left * r), 25);
            DrawLine(centerStart + (gridMatrix.Right * r), centerEnd + (gridMatrix.Right * r), 25);
            DrawLine(centerStart + (gridMatrix.Up * r), centerEnd + (gridMatrix.Up * r), 25);
            DrawLine(centerStart + (gridMatrix.Down * r), centerEnd + (gridMatrix.Down * r), 25);
        }
        
        private void DrawLine (Vector3D startPos, Vector3D endPos, float rad)
        {
            Vector4 baseCol = Color.LightBlue;
            string material = "WeaponLaser";
            float ranf = MyUtils.GetRandomFloat(0.75f * rad, 1.5f * rad);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf * 0.66f);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf * 0.33f);
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

        public void ToggleWarp (IMyCubeGrid source, bool? requestedState = null)
        {
            if (!hasEnoughPower)
                return;

            if (WarpState == State.Idle)
            {
                if(!requestedState.HasValue || requestedState.Value)
                {
                    StartCharging();
                    startWarpSource = source;
                }
            }
            else
            {
                if(!requestedState.HasValue || !requestedState.Value)
                {
                    Dewarp();
                }
            }
        }

        public bool Contains (WarpDrive drive)
        {
            return grid.Contains((MyCubeGrid)drive.Block.CubeGrid);
        }

        private void UpdateEffect(WarpDriveSession.WarpEffectUpdate.State state)
        {
            IMyCubeGrid main = grid.MainGrid;
            if(main != null)
                WarpDriveSession.Instance.SendStateToClients(state, grid.Id, main.EntityId, (float)warpBubble.Radius);
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

        private void UpdateHeatPower ()
        {
            float totalPower = 0;
            if (WarpState == State.Charging)
                totalPower = WarpConstants.baseRequiredPower;
            if (WarpState == State.Active)
            {
                float percent = (float)(1 + currentSpeedPt / WarpConstants.maxSpeed * WarpConstants.powerRequirementMultiplier);
                totalPower = WarpConstants.baseRequiredPower * percent;
                SendMessage ($"Speed: {currentSpeedPt * 60 / 1000:0} km/s", 1f / 60, "White");
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
            int numFunctional = 0;
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
                    numFunctional++;
                    radiators += drive.Radiators;
                }
                else
                {
                    drive.RequiredPower = 0;
                }
            }
            functionalDrives = numFunctional;
            totalHeat -= (WarpConstants.heatDissipationDrive * numFunctional + WarpConstants.heatDissapationRadiator * radiators);
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
        
        private void InvalidateSystem (GridSystem system)
        {
            SetStatic(false);
            UpdateEffect(WarpDriveSession.WarpEffectUpdate.State.Destroy);
            OnSystemInvalidated?.Invoke(this);
            OnSystemInvalidated = null;
        }

        private void SendMessage (string msg, float seconds = WarpConstants.warnDefaultTime, string font = WarpConstants.warnDefaultFont)
        {
            if (!MyAPIGateway.Session.IsServer || gridPlayers == null)
                return;
            foreach (IMyPlayer p in gridPlayers)
                MyVisualScriptLogicProvider.ShowNotification(msg, (int)(seconds * 1000), font, p.IdentityId);

            /*if (MyAPIGateway.Utilities.IsDedicated)
                return;
            IMyPlayer player = MyAPIGateway.Session.Player;
            IMyShipController cockpit = player?.Character?.Parent as IMyShipController;
            if (cockpit?.CubeGrid == null || !grid.Contains((MyCubeGrid)cockpit.CubeGrid))
                return;
            MyVisualScriptLogicProvider.ShowNotification(msg, (int)(seconds * 1000), font, player.IdentityId);*/
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

    public class WarpEffects
    {
        private MyEntity3DSoundEmitter sound;
        private MyParticleEffect effect;
        private long effectGridId;
        private IMyEntity bubble;

        public WarpEffects()
        {

        }

        ~WarpEffects()
        {
            if (sound != null && sound.Entity != null)
                sound.Entity.PositionComp.OnPositionChanged -= PositionChanged;
        }

        private void CheckSoundGrid(IMyCubeGrid grid)
        {
            if(sound == null)
            {
                sound = new MyEntity3DSoundEmitter((MyEntity)grid)
                {
                    CanPlayLoopSounds = true
                };
                grid.PositionComp.OnPositionChanged += PositionChanged;
            }
            else if(sound.Entity == null)
            {
                sound.Entity = (MyEntity)grid;
            }
            else if (sound.Entity.EntityId != grid.EntityId)
            {
                sound.Entity.PositionComp.OnPositionChanged -= PositionChanged;
                sound.Entity = (MyEntity)grid;
                grid.PositionComp.OnPositionChanged += PositionChanged;
            }
        }

        private void PositionChanged (MyPositionComponentBase pos)
        {
            bubble?.SetPosition(pos.WorldAABB.Center);
        }

        public void UpdatePosition ()
        {
            if (sound == null && sound.Entity != null)
                return;
            //sound.FastUpdate(false);
            sound.SetPosition(sound.Entity.PositionComp.GetPosition());
            //MyAPIGateway.Utilities.ShowNotification($"Position changed! {sound.SourcePosition}", 16);
        }

        public void PlayCharging(IMyCubeGrid grid)
        {
            if (effect != null && effectGridId == grid.EntityId)
            {
                effect.Play();
            }
            else
            {
                effectGridId = grid.EntityId;
                effect?.Stop();
                MatrixD orientation = MatrixD.CreateFromDir(Vector3D.Backward, Vector3D.Up);
                BoundingBoxD box = grid.PositionComp.WorldAABB;
                orientation.Translation = Vector3D.Forward * box.HalfExtents.AbsMax() * 2.0;
                Vector3D worldPos = box.Center;
                MyParticlesManager.TryCreateParticleEffect(WarpConstants.warpEffect, ref orientation, ref worldPos, grid.Render.GetRenderObjectID(), out effect);
            }

            CheckSoundGrid(grid);
            sound.PlaySound(WarpConstants.chargingSound, true);
            sound.VolumeMultiplier = 2;
        }

        public void PlayInWarp(IMyCubeGrid grid, float radius)
        {
            StopEffect(false);

            CheckSoundGrid(grid);
            sound.PlaySound(WarpConstants.jumpInSound, true);
            sound.PlaySound(WarpConstants.inWarpSound, false);
            sound.VolumeMultiplier = 6;

            bubble = CreateBubble(radius);
        }

        IMyEntity CreateBubble(float radius)
        {
            MyEntity ent = new MyEntity();
            ent.Init(null, WarpConstants.warpBubble, null, radius * 0.004f, null);
            ent.Render.CastShadows = false;
            ent.IsPreview = true;
            ent.Save = false;
            ent.SyncFlag = false;
            ent.NeedsWorldMatrix = true;
            ent.Flags |= EntityFlags.IsNotGamePrunningStructureObject;
            MyEntities.Add(ent);
            return ent;
        }

        public void PlayStopWarp(IMyCubeGrid grid)
        {
            StopEffect();

            CheckSoundGrid(grid);
            sound.PlaySound(WarpConstants.jumpOutSound, true);
            sound.VolumeMultiplier = 2;

            if (bubble != null)
                bubble.Close();
        }

        public void FullStop(bool instant = true)
        {
            if(sound != null)
            {
                sound.StopSound(instant);
                sound.VolumeMultiplier = 1;
            }
            StopEffect(instant);
            if (bubble != null)
                bubble.Close();
        }

        private void StopEffect (bool instant = true)
        {
            if(effect != null)
            {
                effect.Stop(instant);
                effect = null;
            }
        }

    }
}
