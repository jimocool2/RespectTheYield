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

    [BurstCompile]
    public struct EnforceYieldJob : IJobChunk
    {
        [ReadOnly] public NativeHashSet<Entity> ControlledLanes;
        [ReadOnly] public NativeHashSet<Entity> OccupiedLanes;
        [ReadOnly] public NativeHashSet<Entity> PriorityNodes;
        [ReadOnly] public NativeParallelMultiHashMap<Entity, ArrivalInfo> NodeArrivals;
        public bool RightHandRuleEnabled;
        public bool LeftTurnYieldEnabled;
        public bool UnsafeLaneYieldEnabled;
        [ReadOnly] public EntityTypeHandle EntityHandle;
        public ComponentTypeHandle<CarCurrentLane> CarCurrentLaneHandle;
        [ReadOnly] public BufferLookup<CarNavigationLane> CarNavigationLaneLookup;
        [ReadOnly] public ComponentLookup<Owner> OwnerLookup;
        [ReadOnly] public ComponentLookup<Game.Net.NodeLane> NodeLaneLookup;
        [ReadOnly] public ComponentLookup<LaneHandle> LaneHandleLookup;
        [ReadOnly] public ComponentLookup<Game.Net.TrafficLights> TrafficLightsLookup;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var entities = chunk.GetNativeArray(EntityHandle);
            var currentLanes = chunk.GetNativeArray(ref CarCurrentLaneHandle);

            for (int i = 0; i < chunk.Count; i++)
            {
                var currentLane = currentLanes[i];

                bool onControlledLane = ControlledLanes.Contains(currentLane.m_Lane)
                    || (currentLane.m_ChangeLane != Entity.Null && ControlledLanes.Contains(currentLane.m_ChangeLane));

                if (!onControlledLane)
                {
                    if ((currentLane.m_LaneFlags & CarLaneFlags.IsBlocked) != 0)
                    {
                        currentLane.m_LaneFlags &= ~CarLaneFlags.IsBlocked;
                        currentLanes[i] = currentLane;
                    }
                    continue;
                }

                if (ShouldBlock(entities[i], currentLane.m_Lane))
                {
                    currentLane.m_LaneFlags |= CarLaneFlags.IsBlocked;
                    currentLanes[i] = currentLane;
                }
                else
                {
                    if ((currentLane.m_LaneFlags & CarLaneFlags.IsBlocked) != 0)
                    {
                        currentLane.m_LaneFlags &= ~CarLaneFlags.IsBlocked;
                        currentLanes[i] = currentLane;
                    }
                }
            }
        }

        private bool ShouldBlock(Entity vehicle, Entity currentLaneEntity)
        {
            if (!CarNavigationLaneLookup.TryGetBuffer(vehicle, out var navLanes) || navLanes.Length == 0)
                return false;

            // Priority-hierarchy check: block if crossing lane is occupied or has a priority vehicle queued.
            for (int j = 0; j < navLanes.Length; j++)
            {
                var lane = navLanes[j].m_Lane;
                if (!NodeLaneLookup.HasComponent(lane))
                    break;

                if (OccupiedLanes.Contains(lane))
                    return true;

                if (OwnerLookup.TryGetComponent(lane, out var owner) && owner.m_Owner != Entity.Null
                    && PriorityNodes.Contains(owner.m_Owner))
                    return true;
            }

            // Same-priority rules: right-hand rule and left-turn yield.
            // Find this vehicle's first crossing lane.
            Entity myCrossingLane = Entity.Null;
            {
                var firstNav = navLanes[0].m_Lane;
                if (NodeLaneLookup.HasComponent(firstNav))
                    myCrossingLane = firstNav;
            }

            if (myCrossingLane == Entity.Null)
                return false;
            if (!OwnerLookup.TryGetComponent(myCrossingLane, out var myNodeOwner) || myNodeOwner.m_Owner == Entity.Null)
                return false;

            // Read this vehicle's own ArrivalInfo (already computed by CollectPriorityNodesJob).
            // Avoids re-deriving curve tangents, signal group, and unsafe flag via component lookups.
            ArrivalInfo myInfo = default;
            bool foundSelf = false;
            if (NodeArrivals.TryGetFirstValue(myNodeOwner.m_Owner, out var scanInfo, out var scanIt))
            {
                do
                {
                    if (scanInfo.VehicleEntityIndex == vehicle.Index)
                    {
                        myInfo = scanInfo;
                        foundSelf = true;
                        break;
                    }
                }
                while (NodeArrivals.TryGetNextValue(out scanInfo, ref scanIt));
            }
            if (!foundSelf)
                return false;

            var myEntry = myInfo.EntryTangent;
            var myExit  = myInfo.ExitTangent;
            float myCross2d = myEntry.x * myExit.y - myEntry.y * myExit.x;
            bool myIsLeftTurn = myCross2d > 0.1f;

            // Unsafe lane: yield to any non-unsafe vehicle at the same node.
            if (UnsafeLaneYieldEnabled && myInfo.IsUnsafeLane
                && NodeArrivals.TryGetFirstValue(myNodeOwner.m_Owner, out var unsafeOther, out var unsafeIt))
            {
                do
                {
                    if (unsafeOther.VehicleEntityIndex != vehicle.Index && !unsafeOther.IsUnsafeLane)
                        return true;
                }
                while (NodeArrivals.TryGetNextValue(out unsafeOther, ref unsafeIt));
            }

            PriorityType myPriority = PriorityType.Default;
            if (LaneHandleLookup.TryGetComponent(currentLaneEntity, out var myLh))
                myPriority = myLh.priority;

            bool isTrafficLightNode = TrafficLightsLookup.TryGetComponent(myNodeOwner.m_Owner, out _);
            bool myHasGreen = myInfo.HasGreen;

            // cos(130°) ~ -0.6428: cap the right-hand window to prevent mutual deadlock.
            const float kRightHandDotMin = -0.6428f;
            // Dot threshold for "oncoming" (anti-parallel within ~120°).
            const float kOppositeDotMax = -0.5f;
            const float kTurnThreshold  = 0.1f;
            // Skip vehicles going roughly the same direction (within ~45°).
            const float kSameDirDotMax  = 0.7f;

            if (!NodeArrivals.TryGetFirstValue(myNodeOwner.m_Owner, out var other, out var it))
                return false;

            do
            {
                if (other.VehicleEntityIndex == vehicle.Index)
                    continue;

                if (isTrafficLightNode)
                {
                    if (myHasGreen)
                        continue;
                    if (!other.HasGreen)
                        continue;
                }
                else
                {
                    if (other.Priority != myPriority)
                        continue;
                }

                // Ignore vehicles going roughly the same direction.
                float sameDirDot = myEntry.x * other.EntryTangent.x + myEntry.y * other.EntryTangent.y;
                if (sameDirDot > kSameDirDotMax)
                    continue;

                // Rule 1: Right-Hand Rule.
                // Other is to my right if cross(myEntry, otherEntry) > 0 (clockwise) and angle < 130°.
                float rightCross = myEntry.x * other.EntryTangent.y - myEntry.y * other.EntryTangent.x;
                float rightDot   = sameDirDot; // already computed above
                bool fromMyRight = rightCross > 0f && rightDot > kRightHandDotMin;

                if (RightHandRuleEnabled && fromMyRight)
                {
                    // Cycle-breaking: if I am also to the other's right, only the lower entity index yields.
                    float reverseRightCross = other.EntryTangent.x * myEntry.y - other.EntryTangent.y * myEntry.x;
                    float reverseRightDot   = other.EntryTangent.x * myEntry.x + other.EntryTangent.y * myEntry.y;
                    bool iAmFromOthersRight = reverseRightCross > 0f && reverseRightDot > kRightHandDotMin;

                    if (iAmFromOthersRight)
                    {
                        if (vehicle.Index < other.VehicleEntityIndex)
                            continue;
                    }

                    return true;
                }

                // Rule 2: Left-Turn Yield.
                if (LeftTurnYieldEnabled && myIsLeftTurn)
                {
                    float dot = sameDirDot;
                    float otherCross2d = other.EntryTangent.x * other.ExitTangent.y - other.EntryTangent.y * other.ExitTangent.x;
                    bool otherIsLeftTurn  = otherCross2d >  kTurnThreshold;
                    bool otherIsRightTurn = otherCross2d < -kTurnThreshold;
                    if (dot < kOppositeDotMax && !otherIsLeftTurn && !otherIsRightTurn)
                        return true;
                }
            }
            while (NodeArrivals.TryGetNextValue(out other, ref it));

            return false;
        }
    }
}
