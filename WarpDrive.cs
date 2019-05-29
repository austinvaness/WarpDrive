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
        public float RequiredPower;
        public int Radiators { get; private set; }
        public bool HasPower => sink.CurrentInputByType(WarpConstants.ElectricityId) >= sink.RequiredInputByType(WarpConstants.ElectricityId);

        private MyResourceSinkComponent sink;
        private int initStart;
        private bool started = false;

        public override void Init (MyObjectBuilder_EntityBase objectBuilder)
        {
            Block = (IMyFunctionalBlock)Entity;

            Block.AddUpgradeValue("Radiators", 0);
            Block.OnUpgradeValuesChanged += OnUpgradeValuesChanged;
            InitPowerSystem();
            sink.Update();
            if (WarpDriveSession.Instance != null)
                initStart = WarpDriveSession.Instance.Runtime;
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        private void OnUpgradeValuesChanged ()
        {
            Radiators = (int)Block.UpgradeValues ["Radiators"];
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
                    System.onSystemInvalidated += OnSystemInvalidated;
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
            if(System != null)
                System.onSystemInvalidated -= OnSystemInvalidated;
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

        private void OnSystemInvalidated(WarpSystem system)
        {
            if (Block.MarkedForClose || Block.CubeGrid.MarkedForClose)
                return;
            WarpDriveSession.Instance.DelayedGetWarpSystem(this);
            //SetWarpSystem(WarpDriveSession.Instance.GetWarpSystem(this));
        }

        public void SetWarpSystem(WarpSystem system)
        {
            System = system;
            System.onSystemInvalidated += OnSystemInvalidated;
        }

        public override bool Equals (object obj)
        {
            var drive = obj as WarpDrive;
            return drive != null &&
                   EqualityComparer<IMyFunctionalBlock>.Default.Equals(Block, drive.Block);
        }

        public override int GetHashCode ()
        {
            return 957606482 + EqualityComparer<IMyFunctionalBlock>.Default.GetHashCode(Block);
        }
    }
}
