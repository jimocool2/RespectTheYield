namespace RespectTheYield.Jobs
{
    using Game.Vehicles;
    using Unity.Burst;
    using Unity.Burst.Intrinsics;
    using Unity.Collections;
    using Unity.Entities;

    [BurstCompile]
    public struct EnforceYieldJob : IJobChunk
    {
        [ReadOnly] public NativeHashSet<Entity> ControlledLanes;
        [ReadOnly] public NativeHashSet<Entity> OccupiedLanes;
        [ReadOnly] public NativeHashSet<Entity> PriorityCrossings;
        [ReadOnly] public EntityTypeHandle EntityHandle;
        public ComponentTypeHandle<CarCurrentLane> CarCurrentLaneHandle;
        [ReadOnly] public BufferLookup<CarNavigationLane> CarNavigationLaneLookup;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var entities = chunk.GetNativeArray(EntityHandle);
            var currentLanes = chunk.GetNativeArray(ref CarCurrentLaneHandle);

            for (int i = 0; i < chunk.Count; i++)
            {
                var currentLane = currentLanes[i];

                bool onControlledLane = ControlledLanes.Contains(currentLane.m_Lane)
                                     || ControlledLanes.Contains(currentLane.m_ChangeLane);

                if (!onControlledLane)
                {
                    if ((currentLane.m_LaneFlags & CarLaneFlags.IsBlocked) != 0)
                    {
                        currentLane.m_LaneFlags &= ~CarLaneFlags.IsBlocked;
                        currentLanes[i] = currentLane;
                    }
                    continue;
                }

                if (IsCrossingClear(entities[i]))
                {
                    if ((currentLane.m_LaneFlags & CarLaneFlags.IsBlocked) != 0)
                    {
                        currentLane.m_LaneFlags &= ~CarLaneFlags.IsBlocked;
                        currentLanes[i] = currentLane;
                    }
                }
                else
                {
                    // Do NOT set UpdateOptimalLane alongside IsBlocked — the nav system
                    // only clears IsBlocked when UpdateOptimalLane is set, so omitting it
                    // keeps our block flag intact through the nav system's update.
                    currentLane.m_LaneFlags |= CarLaneFlags.IsBlocked;
                    currentLanes[i] = currentLane;
                }
            }
        }

        private bool IsCrossingClear(Entity vehicle)
        {
            // CarNavigationSystem consumes PathElements into this lookahead buffer.
            // Index 0 is the immediate next lane — the crossing the vehicle wants to enter.
            if (!CarNavigationLaneLookup.TryGetBuffer(vehicle, out var navLanes) || navLanes.Length == 0)
                return true;

            var targetCrossing = navLanes[0].m_Lane;
            return !OccupiedLanes.Contains(targetCrossing) && !PriorityCrossings.Contains(targetCrossing);
        }
    }
}
