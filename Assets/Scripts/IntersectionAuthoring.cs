using UnityEngine;

public enum IntersectionControlType
{
    Uncontrolled,
    StopSign,
    TrafficLight
}

public class IntersectionAuthoring : MonoBehaviour
{
    public IntersectionControlType controlType;
    public long osmNodeId;
}
