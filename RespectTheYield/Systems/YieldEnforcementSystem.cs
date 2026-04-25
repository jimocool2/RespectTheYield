namespace RespectTheYield.Systems
{
    using Game;
    using Game.City;
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
        private CityConfigurationSystem m_CityConfigSystem;

        // Cached controlled lanes rebuilt only when the yield-lane query changes.
        private NativeHashSet<Entity> m_ControlledLanes;
        private int m_LastYieldLaneCount;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Log = new PrefixLogger(nameof(YieldEnforcementSystem));
            m_CityConfigSystem = World.GetOrCreateSystemManaged<CityConfigurationSystem>();

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

            m_ControlledLanes = new NativeHashSet<Entity>(64, Allocator.Persistent);
            m_LastYieldLaneCount = -1;

            m_Log.Info("Created");
        }

        protected override void OnDestroy()
        {
            if (m_ControlledLanes.IsCreated)
                m_ControlledLanes.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (Mod.Instance?.Setting?.ModEnabled == false)
                return;

            if (m_YieldLaneQuery.IsEmpty)
                return;

            // Rebuild controlled lanes only when the road network changes.
            int currentCount = m_YieldLaneQuery.CalculateEntityCount();
            if (currentCount != m_LastYieldLaneCount)
            {
                m_LastYieldLaneCount = currentCount;
                m_ControlledLanes.Clear();

                var laneHandleEntities = m_YieldLaneQuery.ToEntityArray(Allocator.Temp);
                var laneHandles = m_YieldLaneQuery.ToComponentDataArray<LaneHandle>(Allocator.Temp);
                for (int i = 0; i < laneHandles.Length; i++)
                {
                    var lh = laneHandles[i];
                    if (lh.priority == PriorityType.Yield || lh.priority == PriorityType.Stop)
                        m_ControlledLanes.Add(laneHandleEntities[i]);
                }
                laneHandles.Dispose();
                laneHandleEntities.Dispose();
            }

            if (m_ControlledLanes.IsEmpty)
                return;

            bool leftHandTraffic = m_CityConfigSystem.leftHandTraffic;

            int vehicleCount = m_VehicleQuery.CalculateEntityCount();
            // occupiedLanes is populated inside CollectPriorityNodesJob to avoid a main-thread sync.
            var occupiedLanes = new NativeHashSet<Entity>(vehicleCount * 2, Allocator.TempJob);
            var priorityNodes = new NativeHashSet<Entity>(64, Allocator.TempJob);
            var nodeArrivals  = new NativeParallelMultiHashMap<Entity, ArrivalInfo>(128, Allocator.TempJob);

            var collectJob = new CollectPriorityNodesJob
            {
                ControlledLanes         = m_ControlledLanes,
                EntityHandle            = GetEntityTypeHandle(),
                CarCurrentLaneHandle    = GetComponentTypeHandle<CarCurrentLane>(true),
                CarNavigationLaneLookup = GetBufferLookup<CarNavigationLane>(true),
                OwnerLookup             = GetComponentLookup<Owner>(true),
                NodeLaneLookup          = GetComponentLookup<Game.Net.NodeLane>(true),
                CurveLookup             = GetComponentLookup<Game.Net.Curve>(true),
                LaneHandleLookup        = GetComponentLookup<LaneHandle>(true),
                TrafficLightsLookup     = GetComponentLookup<Game.Net.TrafficLights>(true),
                LaneSignalLookup        = GetComponentLookup<Game.Net.LaneSignal>(true),
                CarLaneLookup           = GetComponentLookup<Game.Net.CarLane>(true),
                LeftHandTraffic         = leftHandTraffic,
                PriorityNodes           = priorityNodes,
                NodeArrivals            = nodeArrivals,
                OccupiedLanes           = occupiedLanes,
            };

            var collectDep = collectJob.Schedule(m_VehicleQuery, Dependency);

            var enforceJob = new EnforceYieldJob
            {
                ControlledLanes         = m_ControlledLanes,
                OccupiedLanes           = occupiedLanes,
                PriorityNodes           = priorityNodes,
                NodeArrivals            = nodeArrivals,
                RightHandRuleEnabled    = Mod.Instance?.Setting?.RightHandRuleEnabled ?? true,
                LeftTurnYieldEnabled    = Mod.Instance?.Setting?.LeftTurnYieldEnabled ?? true,
                UnsafeLaneYieldEnabled  = Mod.Instance?.Setting?.UnsafeLaneYieldEnabled ?? true,
                EntityHandle            = GetEntityTypeHandle(),
                CarCurrentLaneHandle    = GetComponentTypeHandle<CarCurrentLane>(false),
                CarNavigationLaneLookup = GetBufferLookup<CarNavigationLane>(true),
                OwnerLookup             = GetComponentLookup<Owner>(true),
                NodeLaneLookup          = GetComponentLookup<Game.Net.NodeLane>(true),
                LaneHandleLookup        = GetComponentLookup<LaneHandle>(true),
                TrafficLightsLookup     = GetComponentLookup<Game.Net.TrafficLights>(true),
            };

            Dependency = enforceJob.ScheduleParallel(m_VehicleQuery, collectDep);

            Dependency = occupiedLanes.Dispose(Dependency);
            Dependency = priorityNodes.Dispose(Dependency);
            Dependency = nodeArrivals.Dispose(Dependency);
        }
    }
}
