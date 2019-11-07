using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace WarpDriveMod
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false, "WarpDriveLarge")]
    public class WarpDrive : MyGameLogicComponent
    {
        public IMyFunctionalBlock Block { get; private set; }
        public WarpSystem System { get; private set; }
        
        // Ugly workaround
        public float RequiredPower
        {
            get
            {
                return _requiredPower;
            }
            set
            {
                prevRequiredPower = _requiredPower;
                _requiredPower = value;
            }
        }
        private float prevRequiredPower;
        private float _requiredPower;

        public int Radiators { get; private set; }
        public bool HasPower => sink.CurrentInputByType(WarpConstants.ElectricityId) >= prevRequiredPower;

        private MyResourceSinkComponent sink;
        private int initStart;
        private bool started = false;

        public override void Init (MyObjectBuilder_EntityBase objectBuilder)
        {
            Block = (IMyFunctionalBlock)Entity;
            if (MyAPIGateway.Session.IsServer)
            {
                Block.AppendingCustomInfo += Block_AppendingCustomInfo;

                Block.AddUpgradeValue("Radiators", 0);
                Block.OnUpgradeValuesChanged += OnUpgradeValuesChanged;
                InitPowerSystem();
                sink.Update();
                if (WarpDriveSession.Instance != null)
                    initStart = WarpDriveSession.Instance.Runtime;
                NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
            }
            else
            {

            }
        }

        private void Block_AppendingCustomInfo (IMyTerminalBlock arg1, StringBuilder arg2)
        {
            arg2?.Append("Radiators: ").Append(Radiators).Append("/ 16\n");
        }

        private void OnUpgradeValuesChanged ()
        {
            Radiators = (int)Block.UpgradeValues ["Radiators"];
            Block.RefreshCustomInfo();
        }

        public override void UpdateBeforeSimulation ()
        {
            if (WarpDriveSession.Instance == null)
                return;

            if (!started)
            {
                if (System != null && System.Valid)
                {
                    started = true;
                }
                else if (initStart <= WarpDriveSession.Instance.Runtime - WarpConstants.groupSystemDelay)
                {
                    System = WarpDriveSession.Instance.GetWarpSystem(this);
                    System.OnSystemInvalidated += OnSystemInvalidated;
                    started = true;
                }
            }
            else
            {
                sink.Update();
            }
        }

        public override void Close ()
        {
            if (System != null)
                System.OnSystemInvalidated -= OnSystemInvalidated;
        }

        private void InitPowerSystem ()
        {
            MyResourceSinkComponent powerSystem = new MyResourceSinkComponent();
            powerSystem.Init(MyStringHash.GetOrCompute("Utility"), WarpConstants.baseRequiredPower * WarpConstants.powerRequirementMultiplier, ComputeRequiredPower);
            Entity.Components.Add(powerSystem);

            sink = powerSystem;
        }

        private float ComputeRequiredPower ()
        {
            if (System == null || System.WarpState == WarpSystem.State.Idle)
                RequiredPower = 0;
            return RequiredPower;
        }

        private void OnSystemInvalidated (WarpSystem system)
        {
            if(System != null)
                System.OnSystemInvalidated -= OnSystemInvalidated;
            if (Block.MarkedForClose || Block.CubeGrid.MarkedForClose)
                return;
            WarpDriveSession.Instance.DelayedGetWarpSystem(this);
        }

        public void SetWarpSystem (WarpSystem system)
        {
            System = system;
            System.OnSystemInvalidated += OnSystemInvalidated;
        }

        public override bool Equals (object obj)
        {
            WarpDrive drive = obj as WarpDrive;
            return drive != null &&
                   EqualityComparer<IMyFunctionalBlock>.Default.Equals(Block, drive.Block);
        }

        public override int GetHashCode ()
        {
            return 957606482 + EqualityComparer<IMyFunctionalBlock>.Default.GetHashCode(Block);
        }
    }
}
