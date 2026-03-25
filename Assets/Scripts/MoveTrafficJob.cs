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
    [ReadOnly] public NativeArray<int> laneTailCarIndices; // The lookahead brain is back!

    public float deltaTime;

    public void Execute(int i)
    {
        CarSpatialData mapData = spatialData[i];
        int carIndex = mapData.carIndex;
        CarData car = cars[carIndex];
        LaneStruct currentLane = lanes[car.currentLaneIndex];

        // =========================================================================
        // PHASE 1: ROUTE PRE-PLANNING & BRAIN
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
        float v0 = math.max(currentLane.speedLimit, 5f); // NaN Protection
        float leaderSpeed = v0;
        float distanceToLeader = 999f;
        bool hasLeader = false;

        // Turn braking (Slow down for curves)
        if (car.upcomingConnectionIndex != -1)
        {
            Connection conn = connections[car.upcomingConnectionIndex];
            float distToEnd = (currentLane.length - conn.exitTrim) - car.distanceAlongLane;
            if (distToEnd > 0f && distToEnd < 40f)
            {
                float intersectionTurnSpeed = 8f;
                v0 = math.min(v0, math.lerp(intersectionTurnSpeed, currentLane.speedLimit, distToEnd / 40f));
            }
        }

        // Same-Lane Lookahead
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

        // --- RESTORED & FIXED CROSS-LANE LOOKAHEAD ---
        if (car.upcomingConnectionIndex != -1)
        {
            Connection nextConn = connections[car.upcomingConnectionIndex];
            int destLaneIndex = nextConn.laneIndex;
            int tailIdx = laneTailCarIndices[destLaneIndex];

            if (tailIdx != -1)
            {
                CarData tailCar = cars[spatialData[tailIdx].carIndex];
                float triggerDistance = currentLane.length - nextConn.exitTrim;

                // Calculate EXACT remaining distance to the new lane without going negative!
                float remainingDistanceToNewLane = 0f;
                if (car.distanceAlongLane <= triggerDistance)
                {
                    // Still on the straight road
                    remainingDistanceToNewLane = (triggerDistance - car.distanceAlongLane) + nextConn.curveLength;
                }
                else
                {
                    // Already in the intersection curve
                    float excess = car.distanceAlongLane - triggerDistance;
                    remainingDistanceToNewLane = nextConn.curveLength - excess;
                }

                float crossLaneDist = remainingDistanceToNewLane + tailCar.distanceAlongLane - 4.0f;

                if (crossLaneDist < 0.1f) crossLaneDist = 0.1f;
                if (crossLaneDist < distanceToLeader)
                {
                    distanceToLeader = crossLaneDist;
                    leaderSpeed = tailCar.currentSpeed;
                    hasLeader = true;
                }
            }
        }

        // IDM Acceleration Physics
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
        // PHASE 2 & 3: PHYSICS (Snap & Bezier Logic)
        // =========================================================================
        bool inIntersection = false;

        if (car.upcomingConnectionIndex != -1)
        {
            Connection conn = connections[car.upcomingConnectionIndex];
            float triggerDistance = currentLane.length - conn.exitTrim;
            float totalPathLength = triggerDistance + conn.curveLength;

            if (car.distanceAlongLane >= totalPathLength)
            {
                car.currentLaneIndex = conn.laneIndex;
                car.distanceAlongLane = conn.entryTrim + (car.distanceAlongLane - totalPathLength);
                car.upcomingConnectionIndex = -1;
                currentLane = lanes[car.currentLaneIndex];
            }
            else if (car.distanceAlongLane > triggerDistance)
            {
                inIntersection = true;
            }
        }

        if (inIntersection)
        {
            Connection conn = connections[car.upcomingConnectionIndex];
            float triggerDistance = currentLane.length - conn.exitTrim;
            float excessDistance = car.distanceAlongLane - triggerDistance;

            float safeCurveLength = math.max(conn.curveLength, 0.01f);
            float t = math.clamp(excessDistance / safeCurveLength, 0f, 1f);

            float u = 1f - t; float tt = t * t; float uu = u * u;
            float uuu = uu * u; float ttt = tt * t;

            float3 p0 = conn.p0; float3 p1 = conn.p1;
            float3 p2 = conn.p2; float3 p3 = conn.p3;

            float3 pos = uuu * p0;
            pos += 3f * uu * t * p1;
            pos += 3f * u * tt * p2;
            pos += ttt * p3;

            car.position = pos;

            float3 tangent = 3f * uu * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * tt * (p3 - p2);
            if (math.lengthsq(tangent) > 0.001f)
            {
                float3 dir = math.normalize(tangent);
                quaternion targetRot = quaternion.LookRotationSafe(dir, new float3(0, 0, -1f));
                car.rotation = math.slerp(car.rotation, targetRot, deltaTime * 30f);
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
                    float safeSegLen = math.max(segmentLength, 0.001f);
                    float progress = math.clamp((car.distanceAlongLane - distanceTracked) / safeSegLen, 0f, 1f);
                    car.position = math.lerp(wpA, wpB, progress);

                    float3 dir = wpB - wpA;
                    if (math.lengthsq(dir) > 0.001f)
                    {
                        dir = math.normalize(dir);
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