using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct MoveTrafficJob : IJobParallelFor
{
    [NativeDisableParallelForRestriction]
    public NativeArray<CarData> cars;
    [ReadOnly] public NativeArray<CarSpatialData> spatialData;
    [ReadOnly] public NativeArray<LaneStruct> lanes;
    [ReadOnly] public NativeArray<float3> laneWaypoints;
    [ReadOnly] public NativeArray<Connection> connections;
    [ReadOnly] public NativeArray<int> laneTailCarIndices;

    public float deltaTime;

    public void Execute(int i)
    {
        CarSpatialData mapData = spatialData[i];
        int carIndex = mapData.carIndex;
        CarData car = cars[carIndex];
        LaneStruct currentLane = lanes[car.currentLaneIndex];

        // =========================================================================
        // PHASE 1: ROUTE PRE-PLANNING & BRAIN (IDM)
        // =========================================================================
        if (car.upcomingConnectionIndex == -1)
        {
            if (currentLane.connectionCount > 0)
            {
                Unity.Mathematics.Random rand = new Unity.Mathematics.Random(car.randomSeed);
                car.upcomingConnectionIndex = currentLane.connectionStart + rand.NextInt(0, currentLane.connectionCount);
                car.randomSeed = rand.NextUInt();
            }
        }

        float v = car.currentSpeed;
        float v0 = currentLane.speedLimit;

        if (car.upcomingConnectionIndex != -1)
        {
            Connection conn = connections[car.upcomingConnectionIndex];
            // Lookahead distance now respects the trim!
            float distToEnd = (currentLane.length - conn.exitTrim) - car.distanceAlongLane;
            if (distToEnd > 0f && distToEnd < 40f)
            {
                float intersectionTurnSpeed = 8f;
                v0 = math.min(v0, math.lerp(intersectionTurnSpeed, currentLane.speedLimit, distToEnd / 40f));
            }
        }

        float leaderSpeed = v0;
        float distanceToLeader = 999f;
        bool hasLeader = false;

        if (i > 0)
        {
            CarSpatialData leaderMap = spatialData[i - 1];
            if (leaderMap.laneIndex == car.currentLaneIndex)
            {
                distanceToLeader = leaderMap.distanceAlongLane - car.distanceAlongLane - 4.0f;
                if (distanceToLeader < 0.1f) distanceToLeader = 0.1f;
                leaderSpeed = cars[leaderMap.carIndex].currentSpeed;
                hasLeader = true;
            }
        }

        if (car.upcomingConnectionIndex != -1)
        {
            Connection nextConn = connections[car.upcomingConnectionIndex];
            int destLaneIndex = nextConn.laneIndex;
            int tailIdx = laneTailCarIndices[destLaneIndex];

            if (tailIdx != -1)
            {
                CarData tailCar = cars[spatialData[tailIdx].carIndex];
                float distToLaneEnd = currentLane.length - car.distanceAlongLane;
                float crossLaneDist = distToLaneEnd + nextConn.curveLength + tailCar.distanceAlongLane - 4.0f;

                if (crossLaneDist < 0.1f) crossLaneDist = 0.1f;
                if (crossLaneDist < distanceToLeader)
                {
                    distanceToLeader = crossLaneDist;
                    leaderSpeed = tailCar.currentSpeed;
                    hasLeader = true;
                }
            }
        }

        float a = 2.0f; float b = 1.5f; float T = 1.2f; float s0 = 2.5f; float accel = 0f;

        if (hasLeader)
        {
            float deltaV = v - leaderSpeed;
            float s_star = s0 + (v * T) + ((v * deltaV) / (2f * math.sqrt(a * b)));
            if (s_star < s0) s_star = s0;
            accel = a * (1f - math.pow(v / v0, 4f) - math.pow(s_star / distanceToLeader, 2f));
            if (distanceToLeader < 2.0f) car.currentSpeed = math.min(car.currentSpeed, leaderSpeed);
            if (distanceToLeader < 0.1f) car.currentSpeed = leaderSpeed * 0.8f;
        }
        else
        {
            accel = a * (1f - math.pow(v / v0, 4f));
        }

        car.currentSpeed += accel * deltaTime;
        if (car.currentSpeed < 0f) car.currentSpeed = 0f;
        car.distanceAlongLane += car.currentSpeed * deltaTime;

        // =========================================================================
        // PHASE 2: THE SYNCED LANE TRANSITION
        // =========================================================================
        bool inIntersection = false;

        if (car.upcomingConnectionIndex != -1)
        {
            Connection conn = connections[car.upcomingConnectionIndex];

            // Trigger 5 meters EARLY
            float triggerDistance = currentLane.length - conn.exitTrim;
            float totalPathLength = triggerDistance + conn.curveLength;

            if (car.distanceAlongLane >= totalPathLength)
            {
                car.currentLaneIndex = conn.laneIndex;

                // Land 5 meters DEEP into the new lane (plus any frame overflow)
                car.distanceAlongLane = conn.entryTrim + (car.distanceAlongLane - totalPathLength);

                car.upcomingConnectionIndex = -1;
                currentLane = lanes[car.currentLaneIndex];
            }
            else if (car.distanceAlongLane > triggerDistance)
            {
                inIntersection = true;
            }
        }

        // =========================================================================
        // PHASE 3: THE PHYSICS (Position & Rotation)
        // =========================================================================
        if (inIntersection)
        {
            Connection conn = connections[car.upcomingConnectionIndex];

            // Calculate excess distance from the trimmed start!
            float triggerDistance = currentLane.length - conn.exitTrim;
            float excessDistance = car.distanceAlongLane - triggerDistance;

            float t = math.clamp(excessDistance / conn.curveLength, 0f, 1f);

            float u = 1f - t; float tt = t * t; float uu = u * u;
            float3 p0 = conn.p0; float3 p1 = conn.p1; float3 p2 = conn.p2;

            float3 pos = uu * p0;
            pos += 2f * u * t * p1;
            pos += tt * p2;

            car.position = pos;

            float3 tangent = 2f * u * (p1 - p0) + 2f * t * (p2 - p1);
            if (math.lengthsq(tangent) > 0.001f)
            {
                float3 dir = math.normalize(tangent);
                quaternion targetRot = quaternion.LookRotationSafe(dir, new float3(0, 0, -1f));
                car.rotation = math.slerp(car.rotation, targetRot, deltaTime * 20f);
            }
        }
        else
        {
            float distanceTracked = 0f;
            int startWp = currentLane.startWaypointIndex;

            for (int w = 0; w < currentLane.waypointCount - 1; w++)
            {
                float3 wpA = laneWaypoints[startWp + w];
                float3 wpB = laneWaypoints[startWp + w + 1];
                float segmentLength = math.distance(wpA, wpB);

                if (car.distanceAlongLane <= distanceTracked + segmentLength || w == currentLane.waypointCount - 2)
                {
                    float progress = math.clamp((car.distanceAlongLane - distanceTracked) / segmentLength, 0f, 1f);
                    car.position = math.lerp(wpA, wpB, progress);

                    float3 dir = math.normalize(wpB - wpA);
                    if (math.lengthsq(dir) > 0.001f)
                    {
                        quaternion targetRot = quaternion.LookRotationSafe(dir, new float3(0, 0, -1f));
                        car.rotation = math.slerp(car.rotation, targetRot, deltaTime * 15f);
                    }
                    break;
                }
                distanceTracked += segmentLength;
            }
        }

        cars[carIndex] = car;
    }
}