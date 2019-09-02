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
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Character), false, "WarpDriveLarge")]
    public class AsteroidBlocker : MyGameLogicComponent
    {
        private IMyCharacter cha;
        private MyEntity guard;

        public override void Init (MyObjectBuilder_EntityBase objectBuilder)
        {
            //cha = (IMyCharacter)Entity;
            //guard = EmptyEntity("AstBlcr" + cha.DisplayName, "");


            //guard.PositionComp.LocalMatrix = EntityShapeMatrix;
            //guard.PositionComp.LocalAABB = EntityAabbScaled;
            //guard.PositionComp.WorldMatrix *= cha.WorldMatrix.GetOrientation();
            //guard.PositionComp.SetPosition(cha.WorldMatrix.Translation);

            //NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateBeforeSimulation ()
        {
        }

        public override void Close ()
        {
        }

        public static MyEntity EmptyEntity (string displayName, string model)
        {
            try
            {
                var ent = new MyEntity { NeedsWorldMatrix = true };
                ent.Init(null, model, null, null, null);
                ent.Render.CastShadows = false;
                ent.IsPreview = true;
                ent.Render.Visible = true;
                ent.Save = false;
                ent.SyncFlag = false;
                MyEntities.Add(ent);
                return ent;
            }
            catch (Exception ex) { MyLog.Default.WriteLine($"Exception in EmptyEntity: {ex}"); return null; }
        }
    }
}
