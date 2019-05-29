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

namespace WarpDriveMod
{
    public static class WarpConstants
    {
        public const double startSpeed = 100 / 60f;
        public const double maxSpeed = 100000 / 60f;
        public const double warpAccel = 100 / 60f;
        public const double chargeTicks = 9 * 60;

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

        public const float maxHeat = 100; // Shutdown when this amount of heat has been reached.
        public const float heatGain = 10 / 60f; // Amount of heat gained per tick
        public const float heatDissipationDrive = 1 / 60f; // Amount of heat dissipated by warp drives every tick
        public const float heatDissapationRadiator = 2 / 60f; // Amount of heat dissipated by radiators every tick

        public static MySoundPair inWarpSound = new MySoundPair("ShipJumpDriveRecharge", true);
        public static MySoundPair chargingSound = new MySoundPair("ShipJumpDriveCharging", true);
        public static MySoundPair jumpInSound = new MySoundPair("ShipJumpDriveJumpIn", true);
        public static MySoundPair jumpOutSound = new MySoundPair("ShipJumpDriveJumpOut", true);

        public const int groupSystemDelay = 1;

        public static MyDefinitionId ElectricityId = MyResourceDistributorComponent.ElectricityId;
    }


    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class WarpDriveSession : MySessionComponentBase
    {
        public static WarpDriveSession Instance;
        public Random Rand { get; private set; } = new Random();
        public int Runtime { get; private set; } = 0;

        private List<WarpSystem> warpSystems = new List<WarpSystem>();
        private List<WarpSystem> newSystems = new List<WarpSystem>();
        private List<WarpDrive> requireSystem = new List<WarpDrive>();
        private bool isHost;
        private bool isPlayer;
        private const ushort toggleWarpPacketId = 4110;

        public WarpDriveSession()
        {
            Instance = this;
        }

        public override void BeforeStart ()
        {
            isPlayer = !MyAPIGateway.Utilities.IsDedicated;
            isHost = MyAPIGateway.Multiplayer.IsServer;

            Action<IMyTerminalBlock> toggle = ToggleWarp;
            if(isHost)
            {
                MyAPIGateway.Multiplayer.RegisterMessageHandler(toggleWarpPacketId, ReceiveToggleWarp);
                if (isPlayer)
                {
                    MyLog.Default.WriteLineAndConsole("Initialized Warp Drive mod on a hosted multiplayer world.");
                }
                else
                {
                    MyLog.Default.WriteLineAndConsole("Initialized Warp Drive mod on dedicated server.");
                    return;
                }
            }
            else 
            {
                if (isPlayer)
                {
                    toggle += TransmitToggleWarp;
                    MyLog.Default.WriteLineAndConsole("Initialized Warp Drive mod on a multiplayer client.");
                }
                else
                {
                    throw new Exception("Session is not host or client. What?!");
                }
            }

            IMyTerminalAction startWarp = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>("StartWarp");
            startWarp.Enabled = IsWarpDrive;
            startWarp.Name = new StringBuilder("Toggle Warp");
            startWarp.Action = toggle;
            startWarp.Icon = "Textures\\GUI\\Icons\\Actions\\Toggle.dds";
            MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(startWarp);

            IMyTerminalControlButton startWarpBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyUpgradeModule>("StartWarpBtn");
            startWarpBtn.Tooltip = MyStringId.GetOrCompute("Toggles the status of the warp drives on the ship");
            startWarpBtn.Title = MyStringId.GetOrCompute("Toggle Warp");
            startWarpBtn.Enabled = IsWarpDrive;
            startWarpBtn.Visible = IsWarpDrive;
            startWarpBtn.SupportsMultipleBlocks = false;
            startWarpBtn.Action = toggle;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(startWarpBtn);
        }

        private void ReceiveToggleWarp (byte [] data)
        {
            long id = BitConverter.ToInt64(data, 0);
            //long blockId = BitConverter.ToInt64(data, 8);
            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(id, out entity))
                return;
            IMyFunctionalBlock block = entity as IMyFunctionalBlock;
            if (block != null)
                ToggleWarp(block);
        }

        private void TransmitToggleWarp (IMyTerminalBlock block)
        {
            WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
            if (drive == null)
                return;
            //if (!HasValidSystem(drive)) client WarpDrives don't have any values
            //    return;

            //byte [] data1 = BitConverter.GetBytes(block.CubeGrid.EntityId);
            //byte [] data2 = BitConverter.GetBytes(block.EntityId);
            //Array.Resize(ref data1, data1.Length + data2.Length);
            //Array.Copy(data2, 0, data1, data1.Length, data2.Length);
            MyAPIGateway.Multiplayer.SendMessageToServer(toggleWarpPacketId, BitConverter.GetBytes(block.EntityId));
        }


        private bool IsWarpDrive(IMyTerminalBlock block)
        {
            return block?.GameLogic?.GetAs<WarpDrive>() != null;
        }

        public override void UpdateBeforeSimulation ()
        {
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

        }

        public override void UpdateAfterSimulation ()
        {
            foreach (WarpSystem s in warpSystems)
                s.UpdateAfterSimulation();

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
            WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
            if (!HasValidSystem(drive))
                return;

            drive.System.ToggleWarp(block.CubeGrid);
        }

        private bool HasValidSystem(WarpDrive drive)
        {
            return drive?.System != null && drive.System.Valid;
        }
    }
}