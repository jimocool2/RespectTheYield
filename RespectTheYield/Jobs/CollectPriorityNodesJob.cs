namespace RespectTheYield.Jobs
{
    using Game.Common;
    using Game.Vehicles;
    using RespectTheYield.Helpers;
    using Traffic.Components.PrioritySigns;
    using Unity.Burst;
    using Unity.Burst.Intrinsics;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Mathematics;

    [BurstCompile]
    public struct CollectPriorityNodesJob : IJobChunk
    {
        [ReadOnly] public NativeHashSet<Entity> ControlledLanes;
        [ReadOnly] public EntityTypeHandle EntityHandle;
        [ReadOnly] public ComponentTypeHandle<CarCurrentLane> CarCurrentLaneHandle;
        [ReadOnly] public BufferLookup<CarNavigationLane> CarNavigationLaneLookup;
        [ReadOnly] public ComponentLookup<Owner> OwnerLookup;
        [ReadOnly] public ComponentLookup<Game.Net.NodeLane> NodeLaneLookup;
        [ReadOnly] public ComponentLookup<Game.Net.Curve> CurveLookup;
        [ReadOnly] public ComponentLookup<LaneHandle> LaneHandleLookup;
        [ReadOnly] public ComponentLookup<Game.Net.TrafficLights> TrafficLightsLookup;
        [ReadOnly] public ComponentLookup<Game.Net.LaneSignal>    LaneSignalLookup;
        [ReadOnly] public ComponentLookup<Game.Net.CarLane>       CarLaneLookup;
        public NativeHashSet<Entity> PriorityNodes;
        public NativeParallelMultiHashMap<Entity, ArrivalInfo> NodeArrivals;
        public NativeHashSet<Entity> OccupiedLanes;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var entities = chunk.GetNativeArray(EntityHandle);
            var currentLanes = chunk.GetNativeArray(ref CarCurrentLaneHandle);

            for (int i = 0; i < chunk.Count; i++)
            {
                var cl = currentLanes[i];

                // Populate OccupiedLanes (moved from main thread to avoid sync point).
                OccupiedLanes.Add(cl.m_Lane);
                if (cl.m_ChangeLane != Entity.Null)
                    OccupiedLanes.Add(cl.m_ChangeLane);

                bool isControlled = ControlledLanes.Contains(cl.m_Lane)
                    || (cl.m_ChangeLane != Entity.Null && ControlledLanes.Contains(cl.m_ChangeLane));

                // Get nav buffer once; used by both priority-nodes and arrivals paths.
                CarNavigationLaneLookup.TryGetBuffer(entities[i], out var navLanes);

                // Priority-nodes path: only non-controlled vehicles create claims.
                if (!isControlled)
                {
                    if (NodeLaneLookup.HasComponent(cl.m_Lane))
                    {
                        if (OwnerLookup.TryGetComponent(cl.m_Lane, out var curOwner) && curOwner.m_Owner != Entity.Null)
                            PriorityNodes.Add(curOwner.m_Owner);
                    }

                    if (navLanes.IsCreated && navLanes.Length > 0)
                    {
                        for (int j = 0; j < navLanes.Length; j++)
                        {
                            var lane = navLanes[j].m_Lane;
                            if (!NodeLaneLookup.HasComponent(lane))
                                break;
                            if (OwnerLookup.TryGetComponent(lane, out var owner) && owner.m_Owner != Entity.Null)
                                PriorityNodes.Add(owner.m_Owner);
                        }
                    }
                }

                // Arrivals path: record all vehicles (controlled and non-controlled)
                // at their next crossing node for same-priority rule checks.
                Entity crossingLaneEntity = Entity.Null;

                // Vehicle mid-crossing: current lane is a crossing lane.
                if (NodeLaneLookup.HasComponent(cl.m_Lane))
                    crossingLaneEntity = cl.m_Lane;

                // Vehicle approaching: first navLane that is a crossing lane.
                if (crossingLaneEntity == Entity.Null && navLanes.IsCreated && navLanes.Length > 0)
                {
                    var firstNav = navLanes[0].m_Lane;
                    if (NodeLaneLookup.HasComponent(firstNav))
                        crossingLaneEntity = firstNav;
                }

                if (crossingLaneEntity == Entity.Null)
                    continue;
                if (!CurveLookup.TryGetComponent(crossingLaneEntity, out var curve))
                    continue;
                if (!OwnerLookup.TryGetComponent(crossingLaneEntity, out var nodeOwner) || nodeOwner.m_Owner == Entity.Null)
                    continue;

                // Entry tangent: first control segment of bezier (xz plane).
                // Exit tangent: last control segment of bezier (xz plane).
                var bezier = curve.m_Bezier;
                var entryDir = bezier.b.xyz - bezier.a.xyz;
                var exitDir  = bezier.d.xyz - bezier.c.xyz;
                float2 entryTangent = math.normalizesafe(new float2(entryDir.x, entryDir.z));
                float2 exitTangent  = math.normalizesafe(new float2(exitDir.x,  exitDir.z));

                PriorityType priority = PriorityType.Default;
                if (LaneHandleLookup.TryGetComponent(cl.m_Lane, out var lh))
                    priority = lh.priority;

                bool isUnsafe = CarLaneLookup.TryGetComponent(crossingLaneEntity, out var carLane)
                    && (carLane.m_Flags & Game.Net.CarLaneFlags.Unsafe) != 0;

                bool hasGreen = false;
                if (LaneSignalLookup.TryGetComponent(crossingLaneEntity, out var laneSignal)
                    && TrafficLightsLookup.TryGetComponent(nodeOwner.m_Owner, out var trafficLights))
                {
                    hasGreen = (laneSignal.m_GroupMask & (1u << (int)trafficLights.m_CurrentSignalGroup)) != 0u;
                }

                NodeArrivals.Add(nodeOwner.m_Owner, new ArrivalInfo
                {
                    EntryTangent = entryTangent,
                    ExitTangent  = exitTangent,
                    Priority     = priority,
                    VehicleEntityIndex = entities[i].Index,
                    HasGreen     = hasGreen,
                    IsUnsafeLane = isUnsafe,
                });
            }
        }
    }
}
