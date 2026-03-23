using System.Collections.Generic;
using UnityEngine;

public static class Pathfinder
{
    public static List<Vertex> FindPath(Vertex startNode, Vertex targetNode)
    {
        List<Vertex> openSet = new List<Vertex>();
        HashSet<Vertex> closedSet = new HashSet<Vertex>();
        Dictionary<Vertex, Vertex> parentMap = new Dictionary<Vertex, Vertex>();
        Dictionary<Vertex, float> gCost = new Dictionary<Vertex, float>();
        Dictionary<Vertex, float> fCost = new Dictionary<Vertex, float>();

        openSet.Add(startNode);
        gCost[startNode] = 0;
        fCost[startNode] = Vector3.Distance(startNode.transform.position, targetNode.transform.position);

        while (openSet.Count > 0)
        {
            Vertex currentNode = openSet[0];
            for (int i = 1; i < openSet.Count; i++)
            {
                if (fCost.GetValueOrDefault(openSet[i], float.MaxValue) < fCost.GetValueOrDefault(currentNode, float.MaxValue))
                {
                    currentNode = openSet[i];
                }
            }

            openSet.Remove(currentNode);
            closedSet.Add(currentNode);

            if (currentNode == targetNode) return RetracePath(startNode, targetNode, parentMap);

            foreach (Edge edge in currentNode.connectedEdges)
            {
                // --- TRAFFIC LAWS ---
                if (edge.properties.isOneWay)
                {
                    if (!edge.properties.isReversedOneWay && currentNode == edge.vertexB) continue;
                    if (edge.properties.isReversedOneWay && currentNode == edge.vertexA) continue;
                }

                Vertex neighbor = edge.GetOppositeVertex(currentNode);
                if (closedSet.Contains(neighbor)) continue;

                float distanceToNeighbor = Vector3.Distance(currentNode.transform.position, neighbor.transform.position);
                float tentativeGCost = gCost.GetValueOrDefault(currentNode, float.MaxValue) + distanceToNeighbor;

                if (tentativeGCost < gCost.GetValueOrDefault(neighbor, float.MaxValue))
                {
                    parentMap[neighbor] = currentNode;
                    gCost[neighbor] = tentativeGCost;
                    fCost[neighbor] = tentativeGCost + Vector3.Distance(neighbor.transform.position, targetNode.transform.position);

                    if (!openSet.Contains(neighbor)) openSet.Add(neighbor);
                }
            }
        }

        return null; // No path found
    }

    private static List<Vertex> RetracePath(Vertex startNode, Vertex endNode, Dictionary<Vertex, Vertex> parentMap)
    {
        List<Vertex> path = new List<Vertex>();
        Vertex currentNode = endNode;

        while (currentNode != startNode)
        {
            path.Add(currentNode);
            currentNode = parentMap[currentNode];
        }
        path.Add(startNode);
        path.Reverse();
        return path;
    }
}