namespace RespectTheYield.Helpers
{
    using Traffic.Components.PrioritySigns;
    using Unity.Mathematics;

    public struct ArrivalInfo
    {
        public float2 EntryTangent;
        public float2 ExitTangent;
        public PriorityType Priority;
        public int VehicleEntityIndex;
        public bool HasGreen;
        public bool IsUnsafeLane;
    }
}
