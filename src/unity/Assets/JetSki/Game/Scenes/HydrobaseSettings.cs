using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HydrobaseSettings : MonoBehaviour {
	public Vector3 gravity = new Vector3(0, -50f, 0);
	void Start () {
		//Physics.autoSimulation = true;
		Physics.gravity = gravity;
		//GameObject.Find("9001").GetComponent<Rigidbody>().mass = .1f;
	}
}
