# RespectTheYield — Session Handoff

## Goal
Prevent vehicles on Yield/Stop lanes from proceeding into an intersection unless the crossing is clear. Base game allows them to squeeze into gaps and block opposing traffic. We only affect lanes Traffic mod has marked `PriorityType.Yield` or `PriorityType.Stop`.

## Dependency Added
Traffic mod (https://github.com/krzychu124/Traffic) is a required dependency.  
`Traffic.dll` goes in `lib/` folder. Add to `.csproj`:
```xml
<Reference Include="Traffic">
  <HintPath>lib\Traffic.dll</HintPath>
  <Private>false</Private>
</Reference>
```

## Files

### `Systems/YieldEnforcementSystem.cs`
`GameSystemBase` that runs each simulation tick.
- Queries entities with `LaneHandle` + `Lane` — these ARE the lane entities (not edge entities)
- Filters to lanes where priority is `Yield` or `Stop` → builds `NativeHashSet<Entity> controlledLanes` keyed on lane entity
- Schedules `EnforceYieldJob` against all vehicle entities with `Car` + `CarCurrentLane`
- Disposes `controlledLanes` via `Dependency` after job completes

### `Jobs/EnforceYieldJob.cs`
`IJobChunk` struct `EnforceYieldJob`.
- Takes `ControlledLanes` (lane entity set), `ComponentTypeHandle<CarCurrentLane>` (read-write), `ComponentTypeHandle<Car>` (read-only)
- Per vehicle: checks `CarCurrentLane.m_Lane` AND `m_ChangeLane` against `ControlledLanes`
- If on controlled lane and crossing NOT clear: sets `Game.Vehicles.CarLaneFlags.IsBlocked` on `m_LaneFlags`, writes back
- If NOT on controlled lane but `IsBlocked` was set: clears the flag
- `IsCrossingClear()` is a stub returning `true` — see open task below

### Registration in `Mod.cs`
Already wired:
```csharp
updateSystem.UpdateAt<YieldEnforcementSystem>(SystemUpdatePhase.GameSimulation);
```

## Key Data Structures Confirmed (via ILSpy)

### `CarCurrentLane` (Game.Vehicles)
```csharp
public Entity m_Lane;       // current lane entity
public Entity m_ChangeLane; // lane entity being changed into
public float3 m_CurvePosition;
public CarLaneFlags m_LaneFlags;  // Game.Vehicles.CarLaneFlags
public float m_ChangeProgress;
public float m_Duration;
public float m_Distance;
public float m_LanePosition;
```

### `Game.Vehicles.CarLaneFlags` (confirmed via ILSpy)
Blocking flag: `IsBlocked = 0x4000`  
Other relevant: `Queue = 0x1000`, `IgnoreBlocker = 0x2000`, `PushBlockers = 0x100000`

### `Game.Net.CarLaneFlags` (confirmed via ILSpy)
These are on the net lane/road data, NOT on vehicle components:  
`Yield = 0x400`, `Stop = 0x800`, `RightOfWay = 0x4000000`, `Forward = 0x100000`

### LaneHandle entity identity
`LaneHandle` (Traffic mod) is on lane entities — same entity kind as `CarCurrentLane.m_Lane`.  
No `Owner` lookup needed; store the queried entity directly in `controlledLanes`.

## What Is NOT Yet Implemented

### `IsCrossingClear()` — needs path component ILSpy

Need to know which lane the vehicle will enter AFTER the yield point, then check if any other vehicle currently occupies that lane.

Plan once path component is confirmed:
1. In `YieldEnforcementSystem.OnUpdate`, build `NativeHashSet<Entity> occupiedLanes` from all vehicle `CarCurrentLane.m_Lane` values
2. Pass `OccupiedLanes` to `EnforceYieldJob`
3. In `IsCrossingClear()`: get the target lane from the path component, return `!OccupiedLanes.Contains(targetLane)`

## Open ILSpy Tasks
1. **Vehicle path component** — what holds the next lane in the planned route?  
   Search for `PathElement` usage on vehicle archetypes. Candidates: `CarNavigation`, `PathInformation`, or a dynamic buffer named `PathElement`/`PathOwner`.  
   Need: the lane entity the vehicle will enter after the current one ends.
