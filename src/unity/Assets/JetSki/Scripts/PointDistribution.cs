using UnityEngine;
using System.Collections;
using System.Collections.Generic;
 
public class PointDistribution : MonoBehaviour
{
    public float scaling = 32;
    public int points = 128;
    public PrimitiveType primitiveType = PrimitiveType.Cube;

    void Start ()
    {
        Vector3[] pts = PointsOnSphere(points);
        List<GameObject> uspheres = new List<GameObject>();
        int i = 0;
       
        foreach (Vector3 value in pts)
        {
            uspheres.Add(GameObject.CreatePrimitive(primitiveType));
            uspheres[i].transform.parent = transform;
            uspheres[i].transform.position = value * scaling;
            i++;
        }
    }
 
    Vector3[] PointsOnSphere(int n)
    {
        List<Vector3> upts = new List<Vector3>();
        float inc = Mathf.PI * (3 - Mathf.Sqrt(5));
        float off = 2.0f / n;
        float x = transform.position.x;
        float y = transform.position.y;
        float z = transform.position.z;
        float r = 0;
        float phi = 0;
       
        for (var k = 0; k < n; k++){
            y = k * off - 1 + (off /2);
            r = Mathf.Sqrt(1 - y * y);
            phi = k * inc;
            x = Mathf.Cos(phi) * r;
            z = Mathf.Sin(phi) * r;
           
            upts.Add(new Vector3(x, y, z));
        }
        Vector3[] pts = upts.ToArray();
        return pts;
    }
}