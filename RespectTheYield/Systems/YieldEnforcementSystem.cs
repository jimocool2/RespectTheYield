namespace RespectTheYield.Systems
{
    using Game;
    using Game.Common;
    using Game.Net;
    using Game.Vehicles;
    using RespectTheYield.Helpers;
    using RespectTheYield.Jobs;
    using Traffic.Components.PrioritySigns;
    using Unity.Collections;
    using Unity.Entities;

    public partial class YieldEnforcementSystem : GameSystemBase
    {
        private PrefixLogger m_Log;
        private EntityQuery m_YieldLaneQuery;
        private EntityQuery m_VehicleQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Log = new PrefixLogger(nameof(YieldEnforcementSystem));

            m_YieldLaneQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<LaneHandle>(),
                    ComponentType.ReadOnly<Lane>(),
                }
            });

            m_VehicleQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Car>(),
                    ComponentType.ReadWrite<CarCurrentLane>(),
                    ComponentType.Exclude<Deleted>(),
                }
            });

            m_Log.Info("Created");
        }

        protected override void OnUpdate()
        {
            if (Mod.Instance?.Setting?.ModEnabled == false)
                return;

            if (m_YieldLaneQuery.IsEmpty)
                return;

            // Complete all pending jobs so we can safely read buffers on the main thread.
            CompleteDependency();

            var laneHandleEntities = m_YieldLaneQuery.ToEntityArray(Allocator.TempJob);
            var laneHandles = m_YieldLaneQuery.ToComponentDataArray<LaneHandle>(Allocator.TempJob);

            var controlledLanes = new NativeHashSet<Entity>(laneHandles.Length, Allocator.TempJob);
            var higherPriorityLanes = new NativeHashSet<Entity>(laneHandles.Length, Allocator.TempJob);
            for (int i = 0; i < laneHandles.Length; i++)
            {
                var lh = laneHandles[i];
                if (lh.priority == PriorityType.Yield || lh.priority == PriorityType.Stop)
                    controlledLanes.Add(laneHandleEntities[i]);
                else
                    higherPriorityLanes.Add(laneHandleEntities[i]);
            }

            laneHandles.Dispose();
            laneHandleEntities.Dispose();

            if (controlledLanes.IsEmpty)
            {
                controlledLanes.Dispose();
                higherPriorityLanes.Dispose();
                return;
            }

            var allEntities = m_VehicleQuery.ToEntityArray(Allocator.TempJob);
            var allCurrentLanes = m_VehicleQuery.ToComponentDataArray<CarCurrentLane>(Allocator.TempJob);

            var occupiedLanes = new NativeHashSet<Entity>(allCurrentLanes.Length * 2, Allocator.TempJob);
            // Crossings that higher-priority vehicles are queued to enter next.
            var priorityCrossings = new NativeHashSet<Entity>(64, Allocator.TempJob);

            for (int i = 0; i < allCurrentLanes.Length; i++)
            {
                var cl = allCurrentLanes[i];
                occupiedLanes.Add(cl.m_Lane);
                if (cl.m_ChangeLane != Entity.Null)
                    occupiedLanes.Add(cl.m_ChangeLane);

                if (higherPriorityLanes.Contains(cl.m_Lane) &&
                    EntityManager.HasBuffer<CarNavigationLane>(allEntities[i]))
                {
                    var navBuffer = EntityManager.GetBuffer<CarNavigationLane>(allEntities[i]);
                    if (navBuffer.Length > 0)
                        priorityCrossings.Add(navBuffer[0].m_Lane);
                }
            }

            allCurrentLanes.Dispose();
            allEntities.Dispose();
            higherPriorityLanes.Dispose();

            var enforceJob = new EnforceYieldJob
            {
                ControlledLanes = controlledLanes,
                OccupiedLanes = occupiedLanes,
                PriorityCrossings = priorityCrossings,
                EntityHandle = GetEntityTypeHandle(),
                CarCurrentLaneHandle = GetComponentTypeHandle<CarCurrentLane>(false),
                CarNavigationLaneLookup = GetBufferLookup<CarNavigationLane>(true),
            };

            Dependency = enforceJob.ScheduleParallel(m_VehicleQuery, Dependency);

            Dependency = controlledLanes.Dispose(Dependency);
            Dependency = occupiedLanes.Dispose(Dependency);
            Dependency = priorityCrossings.Dispose(Dependency);
        }
    }
}
