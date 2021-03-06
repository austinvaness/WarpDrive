﻿using VRage.Game.Components;
using Sandbox.ModAPI;
using System.Text;
using Sandbox.ModAPI.Interfaces.Terminal;
using System.Collections.Generic;
using System;
using VRage.Game.ModAPI;
using VRage.Game;
using Sandbox.Game.Entities;
using System.Linq;
using VRage.Utils;
using Sandbox.Game;
using Sandbox.Game.Screens.Terminal.Controls;
using Sandbox.Game.EntityComponents;
using VRage.ModAPI;
using VRage.Game.Entity;
using ProtoBuf;
using System.IO;

namespace WarpDriveMod
{
    public static class WarpConstants
    {
        public const double startSpeed = 100 / 60f;
        public const double maxSpeed = 100000 / 60f;
        public const double warpAccel = 100 / 60f;
        public const int chargeTicks = 9 * 60;
        public const double warpBubbleBuffer = 10;

        public const float baseRequiredPower = 32;
        public const int powerRequirementMultiplier = 5;

        public const string warnDestablalized = "Warp field destabilized!";
        public const string warnOverload = "Warp drive overloaded!";
        public const string warnDamaged = "Warp drive damaged!";
        public const string warnNoPower = "Not enough power!";
        public const string warnStatic = "Unable to move static grid!";
        public const string warnInUse = "Grid is already at warp!";
        public const string warnNoEstablish = "Unable to establish warp field!";
        public const string warnOverheat = "Warp drive overheated!";
        public const string warnSafety = "Safety triggered, disengaging warp drive!"; //If safety is triggered, there is nobody to read the message!
        public const string warnExitBubble = "You have left the influence of the warp drive!";
        public const float warnDefaultTime = 5;
        public const string warnDefaultFont = "Red";

        public const float maxHeat = 100; // Shutdown when this amount of heat has been reached.
        public const float heatGain = 10 / 60f; // Amount of heat gained per tick
        public const float heatDissipationDrive = 1 / 60f; // Amount of heat dissipated by warp drives every tick
        public const float heatDissapationRadiator = 0.85f / 60f; // Amount of heat dissipated by radiators every tick

        public static MySoundPair inWarpSound = new MySoundPair("ShipJumpDriveRecharge", true);
        public static MySoundPair chargingSound = new MySoundPair("ShipJumpDriveCharging", true);
        public static MySoundPair jumpInSound = new MySoundPair("ShipJumpDriveJumpIn", true);
        public static MySoundPair jumpOutSound = new MySoundPair("ShipJumpDriveJumpOut", true);

        public const int groupSystemDelay = 1;

        public const string warpEffect = "Warp";
        public static string warpBubble = "Models\\Environment\\SafeZone\\Safezone.mwm";
        
        public static MyDefinitionId ElectricityId = MyResourceDistributorComponent.ElectricityId;
    }


    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class WarpDriveSession : MySessionComponentBase
    {
        public static WarpDriveSession Instance;
        public Random Rand { get; private set; } = new Random();
        public int Runtime { get; private set; } = 0;

        private readonly List<WarpSystem> warpSystems = new List<WarpSystem>();
        private readonly List<WarpSystem> newSystems = new List<WarpSystem>();
        private readonly List<WarpDrive> requireSystem = new List<WarpDrive>();
        private const ushort packetToggleWarp = 4110;
        private const ushort packetEffectState = 4111;



        public WarpDriveSession()
        {
            Instance = this;
        }

        public override void BeforeStart ()
        {
            CreateControls(); // TODO: Fix for reliability
            if(MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Multiplayer.RegisterMessageHandler(packetToggleWarp, ReceiveToggleWarp);
            }
            else
            {
                MyAPIGateway.Multiplayer.RegisterMessageHandler(packetToggleWarp, ReceiveStateUpdate);
            }
            /*if (MyAPIGateway.Session.IsServer)
            {
                if (isPlayer)
                {
                    // Session is host, toggle the warp drive directly.
                    MyLog.Default.WriteLineAndConsole("Initialized Warp Drive mod on a hosted multiplayer world.");
                    toggle = ToggleWarp;
                }
                else
                {
                    // Do not create terminal controls on dedicated server.
                    MyLog.Default.WriteLineAndConsole("Initialized Warp Drive mod on dedicated server.");
                    return;
                }
            }
            else 
            {
                if (isPlayer)
                {
                    // Session is client, tell the host to toggle the warp drive.
                    toggle = TransmitToggleWarp;
                    MyLog.Default.WriteLineAndConsole("Initialized Warp Drive mod on a multiplayer client.");
                }
                else
                {
                    throw new Exception("Session is not host or client. What?!");
                }
            }*/
        }

        #region Controls
        private void CreateControls()
        {

            // Actions
            IMyTerminalAction toggleWarp = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>("ToggleWarp");
            toggleWarp.Enabled = IsWarpDrive;
            toggleWarp.Name = new StringBuilder("Toggle Warp");
            toggleWarp.Action = ToggleWarp;
            toggleWarp.Icon = "Textures\\GUI\\Icons\\Actions\\Toggle.dds";
            toggleWarp.Writer = GetWarpStatusText;
            MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(toggleWarp);

            IMyTerminalAction toggleSafety = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>("ToggleSafety");
            toggleSafety.Enabled = IsWarpDrive;
            toggleSafety.Name = new StringBuilder("Toggle Safety");
            toggleSafety.Action = ToggleSafety;
            toggleSafety.Icon = "Textures\\GUI\\Icons\\Actions\\Toggle.dds";
            toggleSafety.Writer = GetSafetyText;
            MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(toggleSafety);

            // Controls
            IMyTerminalControlButton startWarpBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyUpgradeModule>("StartWarpBtn");
            startWarpBtn.Tooltip = MyStringId.GetOrCompute("Toggles the status of the warp drives on the ship");
            startWarpBtn.Title = MyStringId.GetOrCompute("Toggle Warp");
            startWarpBtn.Enabled = IsWarpDrive;
            startWarpBtn.Visible = IsWarpDrive;
            startWarpBtn.SupportsMultipleBlocks = false;
            startWarpBtn.Action = ToggleWarp;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(startWarpBtn);

            IMyTerminalControlCheckbox safetyCheckbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyUpgradeModule>("Safety");
            safetyCheckbox.Tooltip = MyStringId.GetOrCompute("When checked, the warp drive will not function without a player on the ship.");
            safetyCheckbox.Title = MyStringId.GetOrCompute("Safety");
            safetyCheckbox.OffText = MyStringId.GetOrCompute("Off");
            safetyCheckbox.OnText = MyStringId.GetOrCompute("On");
            safetyCheckbox.Enabled = IsWarpDrive;
            safetyCheckbox.Visible = IsWarpDrive;
            safetyCheckbox.SupportsMultipleBlocks = false;
            safetyCheckbox.Setter = SetWarpSafety;
            safetyCheckbox.Getter = GetWarpSafety;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(safetyCheckbox);

            // Pb Properties
            IMyTerminalControlProperty<bool> inWarp = MyAPIGateway.TerminalControls.CreateProperty<bool, IMyUpgradeModule>("WarpStatus");
            inWarp.Enabled = IsWarpDrive;
            inWarp.Visible = IsWarpDrive;
            inWarp.SupportsMultipleBlocks = false;
            inWarp.Setter = SetWarpStatus;
            inWarp.Getter = GetWarpStatus;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(inWarp);

            IMyTerminalControlProperty<bool> warpSafetyProp = MyAPIGateway.TerminalControls.CreateProperty<bool, IMyUpgradeModule>("WarpSafety");
            warpSafetyProp.Enabled = IsWarpDrive;
            warpSafetyProp.Visible = IsWarpDrive;
            warpSafetyProp.SupportsMultipleBlocks = false;
            warpSafetyProp.Setter = SetWarpSafety;
            warpSafetyProp.Getter = GetWarpSafety;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(warpSafetyProp);

            IMyTerminalControlProperty<float> heatPercent = MyAPIGateway.TerminalControls.CreateProperty<float, IMyUpgradeModule>("WarpHeat");
            heatPercent.Enabled = IsWarpDrive;
            heatPercent.Visible = IsWarpDrive;
            heatPercent.SupportsMultipleBlocks = false;
            heatPercent.Setter = (x, y) => { };
            heatPercent.Getter = GetWarpHeat;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(heatPercent);
        }

        private void GetWarpStatusText (IMyTerminalBlock block, StringBuilder s)
        {
            WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
            if (!HasValidSystem(drive))
                return;
            switch (drive.System.WarpState)
            {
                case WarpSystem.State.Charging:
                    s.Append("-");
                    break;
                case WarpSystem.State.Active:
                    s.Append("On");
                    break;
                case WarpSystem.State.Idle:
                    s.Append("Off");
                    break;
            }
        }

        private void GetSafetyText (IMyTerminalBlock block, StringBuilder s)
        {
            WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
            if (!HasValidSystem(drive))
                return;
            if (drive.System.Safety)
                s.Append("On");
            else
                s.Append("Off");
        }

        private void ToggleSafety (IMyTerminalBlock block)
        {
            WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
            if (!HasValidSystem(drive))
                return;
            drive.System.Safety = !drive.System.Safety;
        }

        private bool GetWarpSafety (IMyTerminalBlock block)
        {
            WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
            if (!HasValidSystem(drive))
                return false;
            return drive.System.Safety;
        }

        private void SetWarpSafety (IMyTerminalBlock block, bool state)
        {
            WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
            if (!HasValidSystem(drive))
                return;
            drive.System.Safety = state;
        }

        private float GetWarpHeat(IMyTerminalBlock block)
        {
            WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
            if (!HasValidSystem(drive))
                return -1;
            return drive.System.HeatPercent;
        }

        private bool GetWarpStatus (IMyTerminalBlock block)
        {
            WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
            if (!HasValidSystem(drive))
                return false;
            return drive.System.WarpState != WarpSystem.State.Idle;
        }

        private void SetWarpStatus (IMyTerminalBlock block, bool state)
        {
            WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
            if (!HasValidSystem(drive))
                return;
            if(state)
            {
                if(drive.System.WarpState == WarpSystem.State.Idle)
                    drive.System.ToggleWarp(block.CubeGrid);
            }
            else
            {
                if (drive.System.WarpState != WarpSystem.State.Idle)
                    drive.System.ToggleWarp(block.CubeGrid);
            }
        }
        #endregion

        private void ReceiveToggleWarp (byte [] data)
        {
            if(MyAPIGateway.Session.IsServer)
            {
                // Message is from client and should be relayed
                MyAPIGateway.Multiplayer.SendMessageToOthers(packetToggleWarp, data);
            }

            long id = BitConverter.ToInt64(data, 0);
            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(id, out entity))
                return;
            IMyFunctionalBlock block = entity as IMyFunctionalBlock;
            if (block != null)
                ToggleWarp(block);
        }

        private bool IsWarpDrive(IMyTerminalBlock block)
        {
            return block?.GameLogic?.GetAs<WarpDrive>() != null;
        }

        public override void UpdateBeforeSimulation ()
        {
            if (!MyAPIGateway.Session.IsServer)
                return;
            Runtime++;

            for (int i = requireSystem.Count - 1; i >= 0; i--)
            {
                WarpDrive drive = requireSystem [i];
                if(drive.System == null || drive.System.InvalidOn <= Runtime - WarpConstants.groupSystemDelay)
                {
                    requireSystem.RemoveAtFast(i);
                    drive.SetWarpSystem(GetWarpSystem(drive));
                }
                else if(HasValidSystem(drive))
                {
                    requireSystem.RemoveAtFast(i);
                }
            }

            for (int i = warpSystems.Count - 1; i >= 0; i--)
            {
                WarpSystem s = warpSystems [i];
                if (s.Valid)
                    s.UpdateBeforeSimulation();
                else
                    warpSystems.RemoveAtFast(i);
            }

            foreach (WarpSystem s in newSystems)
                warpSystems.Add(s);
            newSystems.Clear();

            //MyVisualScriptLogicProvider.ShowNotification($"{warpSystems.Count} grids. {Runtime / 100}", 16);
        }

        protected override void UnloadData ()
        {
            Instance = null;
        }

        public WarpSystem GetWarpSystem(WarpDrive drive)
        {
            if (HasValidSystem(drive))
                return drive.System; // Why are you here?!?!

            foreach(WarpSystem s in warpSystems)
            {
                if (s.Valid && s.Contains(drive))
                    return s;
            }
            foreach(WarpSystem s in newSystems)
            {
                if (s.Contains(drive))
                    return s;
            }
            WarpSystem newSystem = new WarpSystem(drive, drive.System);
            newSystems.Add(newSystem);
            return newSystem;
        }

        public void DelayedGetWarpSystem(WarpDrive drive)
        {
            requireSystem.Add(drive);
        }

        private void ToggleWarp(IMyTerminalBlock block)
        {
            if (MyAPIGateway.Session.IsServer)
            {
                WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
                if (!HasValidSystem(drive))
                    return;

                drive.System.ToggleWarp(block.CubeGrid);
            }
            else
            {
                WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
                if (drive == null)
                    return;
                MyAPIGateway.Multiplayer.SendMessageToServer(packetToggleWarp, BitConverter.GetBytes(block.EntityId));
            }
        }

        private bool HasValidSystem(WarpDrive drive)
        {
            return drive?.System != null && drive.System.Valid;
        }

        #region Server
        public void SendStateToClients (WarpEffectUpdate.State state, long gridSystemId, long gridId, float gridRadius)
        {
            try
            {
                // Send info to all players, they can ignore the data if they wish
                byte [] data = MyAPIGateway.Utilities.SerializeToBinary(new WarpEffectUpdate(state, gridSystemId, gridId, gridRadius));
                MyAPIGateway.Multiplayer.SendMessageToOthers(packetEffectState, data); 
                if (MyAPIGateway.Session.Player != null) // The server is also a client
                    PerformStateUpdate(state, gridSystemId, gridId, gridRadius);
            }
            catch
            {

            }
        }
        #endregion

        #region Client
        Dictionary<long, WarpEffects> warpEffects = new Dictionary<long, WarpEffects>();


        public override void Draw ()
        {
            if (MyAPIGateway.Session.Player == null)
                return;

            foreach (WarpEffects effect in warpEffects.Values)
                effect.UpdatePosition();
        }

        public void ReceiveStateUpdate(byte[] data)
        {
            
            try
            {
                WarpEffectUpdate update = MyAPIGateway.Utilities.SerializeFromBinary<WarpEffectUpdate>(data);
                PerformStateUpdate(update.state, update.gridSystemId, update.gridId, update.gridRadius);
            }
            catch 
            { 

            }
        }

        private void PerformStateUpdate (WarpEffectUpdate.State state, long gridSystemId, long gridId, float gridRadius)
        {
            if (state == WarpEffectUpdate.State.Destroy)
            {
                warpEffects.Remove(gridSystemId);
            }
            else
            {
                IMyCubeGrid grid = MyAPIGateway.Entities.GetEntityById(gridId) as IMyCubeGrid;
                if (grid == null && state != WarpEffectUpdate.State.HardStop && state != WarpEffectUpdate.State.SoftStop)
                    return;
                WarpEffects effect;
                if (!warpEffects.TryGetValue(gridSystemId, out effect))
                {
                    effect = new WarpEffects();
                    warpEffects [gridSystemId] = effect;
                }
                switch (state)
                {
                    case WarpEffectUpdate.State.Charging:
                        effect.PlayCharging(grid);
                        return;

                    case WarpEffectUpdate.State.InWarp:
                        effect.PlayInWarp(grid, gridRadius);
                        return;

                    case WarpEffectUpdate.State.StopWarp:
                        effect.PlayStopWarp(grid);
                        return;

                    case WarpEffectUpdate.State.HardStop:
                        effect.FullStop();
                        return;

                    case WarpEffectUpdate.State.SoftStop:
                        effect.FullStop(false);
                        return;
                }
            }
        }

        [ProtoContract]
        public class WarpEffectUpdate
        {
            [ProtoMember(1)]
            public State state;
            [ProtoMember(2)]
            public long gridSystemId;
            [ProtoMember(3)]
            public long gridId;
            [ProtoMember(4)]
            public float gridRadius;

            public WarpEffectUpdate()
            {

            }

            public WarpEffectUpdate (State state, long gridSystemId, long gridId, float gridRadius)
            {
                this.state = state;
                this.gridSystemId = gridSystemId;
                this.gridId = gridId;
                this.gridRadius = gridRadius;
            }

            public enum State
            {
                Charging, InWarp, StopWarp, HardStop, SoftStop, Destroy
            }
        }
        #endregion
    }
}
