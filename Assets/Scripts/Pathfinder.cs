using System.Collections.Generic;
using UnityEngine;

public static class Pathfinder
{
    public static List<TransitNode> FindPath(TransitNode startNode, TransitNode targetNode)
    {
        // Nodes we need to evaluate
        List<TransitNode> openSet = new List<TransitNode>();
        // Nodes we've already evaluated
        HashSet<TransitNode> closedSet = new HashSet<TransitNode>();

        // Memory to retrace our steps once we find the target
        Dictionary<TransitNode, TransitNode> parentMap = new Dictionary<TransitNode, TransitNode>();

        // gCost is the distance from the start node
        Dictionary<TransitNode, float> gCost = new Dictionary<TransitNode, float>();
        // fCost is gCost + distance to the target node
        Dictionary<TransitNode, float> fCost = new Dictionary<TransitNode, float>();

        openSet.Add(startNode);
        gCost[startNode] = 0;
        fCost[startNode] = Vector3.Distance(startNode.transform.position, targetNode.transform.position);

        while (openSet.Count > 0)
        {
            // 1. Find the node in the open set with the lowest fCost
            TransitNode currentNode = openSet[0];
            for (int i = 1; i < openSet.Count; i++)
            {
                float currentFCost = fCost.GetValueOrDefault(currentNode, float.MaxValue);
                float nextFCost = fCost.GetValueOrDefault(openSet[i], float.MaxValue);

                if (nextFCost < currentFCost)
                {
                    currentNode = openSet[i];
                }
            }

            openSet.Remove(currentNode);
            closedSet.Add(currentNode);

            // 2. If we found the destination, trace the path backward and return it!
            if (currentNode == targetNode)
            {
                return RetracePath(startNode, targetNode, parentMap);
            }

            // 3. Otherwise, look at all connected streets
            foreach (TransitNode neighbor in currentNode.neighbors)
            {
                if (closedSet.Contains(neighbor)) continue;

                float distanceToNeighbor = Vector3.Distance(currentNode.transform.position, neighbor.transform.position);
                float tentativeGCost = gCost.GetValueOrDefault(currentNode, float.MaxValue) + distanceToNeighbor;

                // If this is a shorter path to the neighbor than we've found before
                if (tentativeGCost < gCost.GetValueOrDefault(neighbor, float.MaxValue))
                {
                    parentMap[neighbor] = currentNode;
                    gCost[neighbor] = tentativeGCost;
                    fCost[neighbor] = tentativeGCost + Vector3.Distance(neighbor.transform.position, targetNode.transform.position);

                    if (!openSet.Contains(neighbor))
                    {
                        openSet.Add(neighbor);
                    }
                }
            }
        }

        // If we search the whole connected graph and find nothing, return null
        Debug.LogWarning("No path found between these nodes. They might be disconnected.");
        return null;
    }

    private static List<TransitNode> RetracePath(TransitNode startNode, TransitNode endNode, Dictionary<TransitNode, TransitNode> parentMap)
    {
        List<TransitNode> path = new List<TransitNode>();
        TransitNode currentNode = endNode;

        while (currentNode != startNode)
        {
            path.Add(currentNode);
            currentNode = parentMap[currentNode];
        }
        path.Add(startNode);

        // We traced from end to start, so flip it to go start to end
        path.Reverse();
        return path;
    }
}