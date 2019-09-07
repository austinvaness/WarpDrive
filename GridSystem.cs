using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace WarpDriveMod
{
    public class GridSystem : IEquatable<GridSystem>
    {
        /// <summary>
        /// True if at least 1 of the grids in the system is static.
        /// </summary>
        public bool IsStatic => staticCount > 0;
        public bool Valid => IsValid();
        public int InvalidOn { get; private set; }
        public Dictionary<string, BlockCounter> BlockCounters { get; private set; } = new Dictionary<string, BlockCounter>();
        public IReadOnlyCollection<MyCubeGrid> Grids => grids;
        public int Id { get; private set; }
        public MyCubeGrid MainGrid => grids.FirstOrDefault();
        public bool HasCockpit { get; private set; }

        private int staticCount;
        private Dictionary<MyCubeGrid, HashSet<IMyShipController>> cockpits = new Dictionary<MyCubeGrid, HashSet<IMyShipController>>();
        private SortedSet<MyCubeGrid> grids = new SortedSet<MyCubeGrid>(new GridByCount());
        private bool _valid = true;

        /// <summary>
        /// Called when a grid no longer belongs to this grid system.
        /// </summary>
        public event Action<GridSystem> onSystemInvalidated;

        public GridSystem (MyCubeGrid firstGrid)
        {
            if (firstGrid == null)
                throw new NullReferenceException("Attempt to create a grid using a null grid.");
            
            Id = WarpDriveSession.Instance.Rand.Next(int.MinValue, int.MaxValue);
            if (firstGrid.MarkedForClose)
                return;

            List<IMyCubeGrid> connectedGrids = MyAPIGateway.GridGroups.GetGroup(firstGrid, GridLinkTypeEnum.Logical);
            foreach (IMyCubeGrid grid in connectedGrids)
            {
                if(!Add((MyCubeGrid)grid))
                    throw new ArgumentException($"Invalid add state with {firstGrid.EntityId} and {grid.EntityId}");
            }
        }

        public bool Contains(MyCubeGrid grid)
        {
            return grids.Contains(grid);
            //return MyAPIGateway.GridGroups.HasConnection(MainGrid, grid, GridLinkTypeEnum.Logical);
        }

        private bool Add(MyCubeGrid grid)
        {
            if (grid == null)
                throw new NullReferenceException("Attempt to add a null grid.");

            if (!grids.Add(grid))
                throw new ArgumentException("Grid already exists.");

            if (grid.IsStatic)
                staticCount++;
            grid.OnBlockAdded += Grid_OnBlockAdded;
            grid.OnBlockRemoved += Grid_OnBlockRemoved;
            grid.OnStaticChanged += Grid_OnIsStaticChanged;
            grid.OnClose += Grid_OnClose;
            grid.OnGridSplit += Grid_OnGridSplit;

            foreach (MyCubeBlock s in grid.GetFatBlocks())
                Grid_OnBlockAdded(s.SlimBlock);

            return true;
        }

        public void AddCounter(string key, BlockCounter counter)
        {
            foreach(MyCubeGrid grid in grids)
            {
                foreach(MyCubeBlock block in grid.GetFatBlocks())
                    counter.TryAddCount(block);
            }
            BlockCounters [key] = counter;
        }

        private void Grid_OnBlockRemoved (IMySlimBlock obj)
        {
            MyCubeGrid grid = (MyCubeGrid)obj.CubeGrid;
            IMyCubeBlock fat = obj.FatBlock;
            if (fat == null || grid == null)
                return;
            foreach(BlockCounter counter in BlockCounters.Values)
                counter.TryRemoveCount(fat);

            if(IsShipController(fat))
            {
                HashSet<IMyShipController> gridCockpits;
                if (cockpits.TryGetValue(grid, out gridCockpits))
                {
                    gridCockpits.Remove((IMyShipController)fat);
                    cockpits [grid] = gridCockpits;
                }
            }

            Resort(grid);
        }

        private void Grid_OnBlockAdded (IMySlimBlock obj)
        {
            MyCubeGrid grid = (MyCubeGrid)obj.CubeGrid;
            IMyCubeBlock fat = obj.FatBlock;
            if (fat == null || grid == null)
                return;
            foreach (BlockCounter counter in BlockCounters.Values)
                counter.TryAddCount(fat);

            if (IsShipController(fat))
            {
                HashSet<IMyShipController> gridCockpits;
                if (!cockpits.TryGetValue(grid, out gridCockpits))
                {
                    gridCockpits = new HashSet<IMyShipController>();
                    gridCockpits.Add((IMyShipController)fat);
                    cockpits [grid] = gridCockpits;
                }
                else
                {
                    gridCockpits.Add((IMyShipController)fat);
                }
            }

            Resort(grid);
        }

        private void Resort (MyCubeGrid grid)
        {
            if (grids.Remove(grid))
                grids.Add(grid);
        }

        private void Grid_OnClose (IMyEntity obj)
        {
            Invalidate();
        }
        private void Grid_OnGridSplit (MyCubeGrid arg1, MyCubeGrid arg2)
        {
            Invalidate();
        }

        public bool IsValid()
        {
            if (!_valid || InvalidOn == WarpDriveSession.Instance.Runtime)
                return _valid;

            // Update the state of the Valid bool
            int realCount = 0;
            IMyCubeGrid first = grids.FirstOrDefault();
            if(first != null)
                realCount = MyAPIGateway.GridGroups.GetGroup(first, GridLinkTypeEnum.Logical).Count;
            if (realCount != grids.Count)
            {
                //MyLog.Default.WriteLine($"Grid counts: {realCount} vs my {grids.Count}");
                Invalidate();
                return false;
            }
            InvalidOn = WarpDriveSession.Instance.Runtime;
            return true;
        }

        public void Invalidate()
        {
            _valid = false;
            onSystemInvalidated?.Invoke(this);
            onSystemInvalidated = null;
            foreach(BlockCounter counter in BlockCounters.Values)
                counter.Dispose();
            foreach(MyCubeGrid grid in grids)
            {
                grid.OnBlockAdded -= Grid_OnBlockAdded;
                grid.OnBlockRemoved -= Grid_OnBlockRemoved;
                grid.OnStaticChanged -= Grid_OnIsStaticChanged;
                grid.OnClose -= Grid_OnClose;
                grid.OnGridSplit -= Grid_OnGridSplit;
            }
        }

        private void Grid_OnIsStaticChanged (MyCubeGrid arg1, bool arg2)
        {
            if (arg1.IsStatic)
                staticCount++;
            else
                staticCount--;
        }

        #region WorldMatrix

        private bool IsShipController (IMyCubeBlock block)
        {
            if (block == null || !(block is IMyTerminalBlock) || block is IMyCryoChamber)
                return false;
            return (block as IMyShipController)?.CanControlShip == true;
        }

        public IMyShipController FindMainCockpit ()
        {
            if (grids.Count == 0)
            {
                HasCockpit = false;
                return null;
            }

            // Loop through all grids starting at largest until an in use one is found
            foreach (MyCubeGrid grid in grids)
            {
                {
                    // Use the main cockpit if it exists
                    IMyTerminalBlock block = grid.MainCockpit;
                    if (block != null && IsShipController(block))
                    {
                        HasCockpit = ((IMyShipController)block).IsUnderControl;
                        return (IMyShipController)block;
                    }
                }

                HashSet<IMyShipController> gridCockpits;
                if(cockpits.TryGetValue(grid, out gridCockpits))
                {
                    foreach (IMyShipController cockpit in gridCockpits)
                    {
                        if (cockpit.IsUnderControl)
                        {
                            HasCockpit = true;
                            return cockpit;
                        }
                    }
                }
            }

            HasCockpit = false;
            // No in use cockpit was found.
            {
                MyCubeGrid largestGrid = grids.FirstOrDefault();
                if (largestGrid == null)
                    return null;

                HashSet<IMyShipController> gridCockpits;
                if (cockpits.TryGetValue(largestGrid, out gridCockpits))
                {
                    return gridCockpits.FirstOrDefault();
                }
            }

            return null;
        }

        public MatrixD FindWorldMatrix ()
        {
            if (grids.Count == 0)
                return Matrix.Zero;

            MyCubeGrid largestGrid = grids.First();
            IMyShipController cockpit = FindMainCockpit();
            if (cockpit != null)
            {
                MatrixD result = cockpit.WorldMatrix;
                result.Translation = largestGrid.WorldMatrix.Translation;
                return result;
            }
            return largestGrid.WorldMatrix;
        }

        #endregion
        public override bool Equals (object obj)
        {
            return Equals(obj as GridSystem);
        }

        public bool Equals (GridSystem other)
        {
            return other != null &&
                   Id == other.Id;
        }

        public override int GetHashCode ()
        {
            return 2108858624 + Id.GetHashCode();
        }

        public class BlockCounter
        {
            public int Count { get; private set; }
            public event Action<IMyCubeBlock> onBlockAdded;
            public event Action<IMyCubeBlock> onBlockRemoved;
            private Func<IMyCubeBlock, bool> method;

            public BlockCounter (Func<IMyCubeBlock, bool> method)
            {
                this.method = method;
            }

            public void TryAddCount(IMyCubeBlock block)
            {
                if (method.Invoke(block))
                {
                    Count++;
                    onBlockAdded?.Invoke(block);
                }
            }
            public void TryRemoveCount (IMyCubeBlock block)
            {
                if (method.Invoke(block))
                {
                    Count--;
                    onBlockRemoved?.Invoke(block);
                }
            }

            public void Dispose ()
            {
                onBlockAdded = null;
                onBlockRemoved = null;
            }
        }
        private class GridByCount : IComparer<MyCubeGrid>
        {
            public int Compare (MyCubeGrid x, MyCubeGrid y)
            {
                int result1 = y.BlocksCount.CompareTo(x.BlocksCount);
                if (result1 == 0)
                    return x.EntityId.CompareTo(y.EntityId);
                return result1;
            }
        }
    }
}
